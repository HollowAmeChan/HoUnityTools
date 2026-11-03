using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using HoFace.Transport;

internal static class Program
{
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            Curves(); Value(); Ir(); Sessions(); PreviewTransport(); RawFrameEnvelope(); Recording();
            AuthoringHistory(args.Length > 0 ? args[0] : FindRepositoryRoot());
            string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../.."));
            foreach (string file in new[]
            {
                Path.Combine(root, "Editor/FaceTracking/Profiles/ho-iPhoneVTS.arkit.hoface.json"),
                Path.Combine(root, "Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json"),
                Path.Combine(root, "Editor/FaceTracking/Profiles/ho-iPhoneVTS.vbparity.hoface.json")
            })
            {
                Check(File.Exists(file), "production profile present: " + file);
                Check(HoFaceValueDocument.TryParse(File.ReadAllText(file), out var profile, out string error), error);
                var session = new HoFaceValueSession(profile);
                session.Evaluate(new Dictionary<string, float>(), 0);
                Check(session.Values.Count > 0, "legacy production profile publishes defaults");
                string migrated = HoFaceValueDocument.Write(profile);
                Check(HoFaceValueDocument.TryParse(migrated, out var copy, out error), error);
                Check(copy.inputs.Count == profile.inputs.Count && copy.outputs.Count == profile.outputs.Count, "migration keeps row counts");
                Check(copy.outputs.Select(x => x.expression).SequenceEqual(profile.outputs.Select(x => x.expression)), "migration preserves expressions and order");
            }
            Check(typeof(HoFaceValueSession).Assembly.GetReferencedAssemblies().All(x => !x.Name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                "portable core has no UnityEngine assembly dependency");
            Console.WriteLine("HO_FACE_CORE PASS assertions=" + checks);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "Mouth.hoface." + name + ".json"));
    private static void Curves()
    {
        var linear = HoFaceCurveData.Linear(-180, -180, 180, 180);
        for (int i = -200; i <= 200; i++) Near(linear.Evaluate(i), Math.Max(-180, Math.Min(180, i)), "signed curve");
        var ease = new HoFaceCurveData(new HoFaceCurveKey(0, 0, 0, 0), new HoFaceCurveKey(1, 1, 0, 0));
        Near(ease.Evaluate(.25f), .15625f, "Hermite zero tangent");
        var step = new HoFaceCurveData(new HoFaceCurveKey(0, 0, 0, float.PositiveInfinity), new HoFaceCurveKey(1, 1, float.PositiveInfinity, 0));
        Near(step.Evaluate(.9f), 0, "step tangent");
        Near(step.Evaluate(1), 1, "step endpoint");
        var sorted = new HoFaceCurveData(new HoFaceCurveKey(1, 1, 1, 1), new HoFaceCurveKey(0, 0, 1, 1));
        Near(sorted.Evaluate(.3f), .3f, "key order normalized");
        Near(HoFaceCurve.Transfer(null, -22), -22, "absent curve is passthrough");
    }
    private static void Value()
    {
        Check(HoFaceValueDocument.TryParse(Sample("value"), out var profile, out string error), error);
        var session = new HoFaceValueSession(profile);
        session.Evaluate(new Dictionary<string, float> { { "JawOpen", .4f }, { "MouthSmileLeft", .2f }, { "MouthSmileRight", .8f } }, 0);
        Near(session.Values["Mouth/Open"], .4f, "mouth opening");
        Near(session.Values["Mouth/Form"], .5f, "smile merge");
        Check(!HoFaceValueDocument.TryParse(Sample("value").Replace("\"version\": 1", "\"version\": 99"), out _, out _), "future value version rejected");
        var chain = new HoFaceMiddleware();
        chain.inputs.Add(new HoFaceOutput { parameter = "A", expression = "Raw", defaultValue = .2f });
        chain.outputs.Add(new HoFaceOutput { parameter = "Result", expression = "A" });
        chain.outputs.Add(new HoFaceOutput { parameter = "Result", expression = "out(\"Result\") * 2" });
        var evaluator = new HoVtsRuleEvaluator(chain);
        evaluator.Evaluate(new Dictionary<string, float> { { "Raw", .3f } }, 0, 0);
        Near(evaluator.Parameters["Result"], .6f, "ordered duplicate outputs");
        evaluator.Evaluate(new Dictionary<string, float>(), .1f, .1);
        Near(evaluator.Parameters["Result"], .6f, "missing input keeps prior sample");
        chain.outputs.Insert(0, new HoFaceOutput { parameter = "Bad", expression = "out(\"Future\")", defaultValue = .7f });
        evaluator = new HoVtsRuleEvaluator(chain);
        Check(!string.IsNullOrEmpty(evaluator.Error), "forward output reference diagnosed");
        evaluator.Evaluate(new Dictionary<string, float>(), 0, 0);
        Near(evaluator.Parameters["Bad"], .7f, "invalid output uses declared fallback");
    }
    private static void Sessions()
    {
        var profile = new HoFaceMiddleware();
        profile.outputs.Add(new HoFaceOutput { parameter = "Random", expression = "rand(0,1)" });
        profile.outputs.Add(new HoFaceOutput { parameter = "Clock", expression = "time(1,10)" });
        var first = new HoFaceValueSession(profile, 123);
        var second = new HoFaceValueSession(profile, 123);
        for (int i = 0; i < 30; i++)
        {
            first.Evaluate(null, i * .1);
            second.Evaluate(null, i * .1);
            Near(first.Values["Random"], second.Values["Random"], "independent repeatable random");
            Near(first.Values["Clock"], i % 10, "explicit frame clock");
        }
        float previous = first.Values["Random"];
        first.Evaluate(null, 29 * .1);
        Near(first.Values["Random"], previous, "same time cannot double advance");
        bool rejected = false;
        try { first.Evaluate(null, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "backward clock rejected");
        first.Reset(); first.Evaluate(null, 0);
        second.Reset(); second.Evaluate(null, 0);
        Near(first.Values["Random"], second.Values["Random"], "reset restores random seed");

        profile = new HoFaceMiddleware();
        var output = new HoFaceOutput { parameter = "Delayed", expression = "Raw" };
        output.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Delay, seconds = .2f });
        profile.outputs.Add(output);
        var delayed = new HoFaceValueSession(profile);
        delayed.Evaluate(new Dictionary<string, float> { { "Raw", 0 } }, 0);
        delayed.Evaluate(new Dictionary<string, float> { { "Raw", 1 } }, .1);
        delayed.Evaluate(new Dictionary<string, float> { { "Raw", 1 } }, .29);
        Near(delayed.Values["Delayed"], 0, "delay does not release future sample");
        delayed.Evaluate(new Dictionary<string, float> { { "Raw", 1 } }, .31);
        Near(delayed.Values["Delayed"], 1, "delay releases due sample");
    }
    private static void Ir()
    {
        Check(HoFaceIrDocument.TryParse(Sample("ir"), out var ir, out string error), error);
        Check(HoFaceIrDocument.TryParse(ir.Write(), out var roundTrip, out error), error);
        Check(roundTrip.Write() == ir.Write(), "IR stable roundtrip");
        Check(!ir.Write().Contains("\"slot\"") && !ir.Write().Contains("\"shape\""),
            "IR targets contain no host mesh bindings");
        Check(!HoFaceIrDocument.TryParse(Sample("ir").Replace("\"version\": 2", "\"version\": 1"), out _, out _),
            "legacy mesh-bound IR version rejected");
        Check(!HoFaceIrDocument.TryParse(Sample("ir").Replace("\"id\": \"jawOpen\"", "\"id\": \"jawOpen\", \"slot\": \"Face\""), out _, out _),
            "IR v2 rejects accidental host mesh bindings");
        var evaluator = new HoFaceIrEvaluator(ir);
        for (int xi = 0; xi <= 10; xi++)
            for (int yi = 0; yi <= 10; yi++)
            {
                evaluator.Evaluate(new Dictionary<string, float> { { "Mouth/Form", xi / 10f }, { "Mouth/Open", yi / 10f } });
                Near(evaluator.CopyChildWeights("mouth").Sum(), 1, "2D weights partition unity");
                foreach (float w in evaluator.Values.Values) Check(w >= 0 && w <= 1, "final weight is in target range");
            }
        evaluator.Evaluate(new Dictionary<string, float> { { "Mouth/Form", 0 }, { "Mouth/Open", 1 } });
        Near(evaluator.Values["jawOpen"], 1, "exact open point");
        Near(evaluator.Values["mouthSmileLeft"], 0, "open point does not leak smile");
        evaluator.Evaluate(new Dictionary<string, float> { { "Mouth/Form", 1 }, { "Mouth/Open", 1 } });
        Near(evaluator.Values["jawOpen"], 1, "combined pose retains jaw");
        Near(evaluator.Values["mouthSmileLeft"], 1, "combined pose contains smile");
        var root = ir.nodes.Find(x => x.id == "mouth");
        root.children[1].x = root.children[0].x; root.children[1].y = root.children[0].y;
        Check(ir.Validate().Count > 0, "duplicate 2D point rejected");
        root.children.RemoveAt(1);
        root.children[0].node = "mouth";
        Check(ir.Validate().Any(x => x.Contains("环")), "cycle rejected");
        Check(!HoFaceIrDocument.TryParse(Sample("ir").Replace("\"version\": 2", "\"version\": 99"), out _, out _), "future IR version rejected");
        Check(!HoFaceIrDocument.TryParse(Sample("ir") + "false", out _, out _), "trailing JSON rejected");

        Check(HoFaceIrDocument.TryParse(Sample("ir"), out ir, out error), error);
        root = ir.nodes.Find(x => x.id == "mouth"); root.type = "blend1d"; root.parameterX = "Mouth/Open";
        root.children.Clear();
        root.children.Add(new HoFaceIrChild { node = "open", threshold = 1 });
        root.children.Add(new HoFaceIrChild { node = "rest", threshold = 0 });
        evaluator = new HoFaceIrEvaluator(ir);
        evaluator.Evaluate(new Dictionary<string, float> { { "Mouth/Open", .4f } });
        Near(evaluator.Values["jawOpen"], .4f, "unsorted 1D thresholds");
        root.type = "direct"; root.children.Clear();
        root.children.Add(new HoFaceIrChild { node = "open", weight = .6f });
        root.children.Add(new HoFaceIrChild { node = "openSmile", weight = .8f });
        evaluator = new HoFaceIrEvaluator(ir); evaluator.Evaluate(null);
        Near(evaluator.UnclampedValues["jawOpen"], 1.4f, "Direct accumulates contributions");
        Near(evaluator.Values["jawOpen"], 1, "one final target clamp");
        root.normalize = true;
        evaluator = new HoFaceIrEvaluator(ir); evaluator.Evaluate(null);
        Near(evaluator.Values["jawOpen"], 1, "normalized Direct");
        Near(evaluator.Values["mouthSmileLeft"], .8f / 1.4f, "normalized independent contribution");
        root.children[0].weight = float.MaxValue;
        root.children[1].weight = float.MaxValue;
        evaluator = new HoFaceIrEvaluator(ir); evaluator.Evaluate(null);
        Near(evaluator.Values["jawOpen"], 1, "large Direct weights cannot produce NaN");
        Near(evaluator.Values["mouthSmileLeft"], .5f, "large Direct weights normalize without overflow");
    }
    private static void PreviewTransport()
    {
        using var receiver = new UdpClient(21415);
        receiver.Client.ReceiveTimeout = 1000;
        using var sender = new HoFacePreviewSender();
        sender.Start(new PreviewUdpOptions { Port = 21415 });
        sender.Send(new Dictionary<string, float> { { "Mouth/Open", .4f } },
            new Dictionary<string, float> { { "jawOpen", .7f } }, true, 1.25);
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        string json = Encoding.UTF8.GetString(receiver.Receive(ref endpoint));
        Check(json.Contains("\"type\":\"hoface.preview\""), "preview packet type");
        Check(json.Contains("\"weights\":{\"jawOpen\":0.7"), "preview final weight");
        Check(json.Contains("\"version\":2"), "preview uses logical-output v2");
        Check(!json.Contains("\"targets\""), "preview packet carries no host mesh bindings");
        Check(json.Contains("\"tracked\":true"), "preview tracking state");
    }
    private static void RawFrameEnvelope()
    {
        const string json = "{\"type\":\"hoface.raw\",\"version\":1,\"sequence\":4,\"time\":2.5,\"tracked\":true,\"values\":{\"JawOpen\":0.7,\"Rotation_x\":-12}}";
        Check(HoFaceRawFrameJson.TryParse(json, out var frame, out string error), error);
        Check(frame.Tracked && frame.Values.Count == 2, "raw Unity envelope");
        Near(frame.Values["JawOpen"], .7f, "raw Unity expression value");
        Near((float)frame.Time, 2.5f, "raw Unity frame time");
        Check(!HoFaceRawFrameJson.TryParse(json.Replace("\"version\":1", "\"version\":9"), out _, out _),
            "future raw envelope rejected");
    }
    private static void Recording()
    {
        string path = Path.Combine(Path.GetTempPath(), "hoface-check-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            using (var writer = new HoFaceRecordingWriter(path, "value.json", "face.ir.json"))
            {
                writer.Write(new HoFaceRecordedFrame { time = 0, tracked = true, values = new Dictionary<string, float> { { "JawOpen", .2f } } });
                writer.Write(new HoFaceRecordedFrame { time = .1, tracked = false, values = new Dictionary<string, float> { { "JawOpen", .3f } } });
                Check(writer.Frames == 2, "record frame count");
            }
            var frames = HoFaceRecordingReader.Read(path);
            Check(frames.Count == 2 && frames[1].tracked == false, "record replay roundtrip");
            Near(frames[0].values["JawOpen"], .2f, "record value");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tools~", "HoFaceStudio", "HoFaceStudio.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("HoUnityTools root was not found.");
    }
    private static void AuthoringHistory(string root)
    {
        string valuePath = Path.Combine(root, "Tools~", "HoFaceStudio", "samples", "Mouth.hoface.value.json");
        string irPath = Path.Combine(root, "Tools~", "HoFaceStudio", "samples", "Mouth.hoface.ir.json");
        string directory = Path.Combine(Path.GetTempPath(), "hoface-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string v = Path.Combine(directory, "Mouth.hoface.value.json");
        string ir = Path.Combine(directory, "Mouth.hoface.ir.json");
        try
        {
            File.Copy(valuePath, v); File.Copy(irPath, ir);
            var document = new HoFace.Studio.AuthoringDocument();
            document.Open(v, ir);
            var before = document.Snapshot();
            var curve = document.Value.outputs.First(x => x.parameter == "Mouth/Open").curve.keys;
            curve[1].value = .8f;
            document.Edit((profile, graph) => profile.outputs.First(x => x.parameter == "Mouth/Open").curve.keys = curve);
            Check(document.IsDirty && document.CanUndo, "authored value edit marks dirty and creates undo");
            document.Undo();
            Check(document.Snapshot().Value == before.Value, "undo restores value edit");
            document.Redo();
            var graph = document.Ir.nodes.First(x => x.id == "mouth");
            graph.children[1].x = -.2f;
            var editedIr = document.Ir.Write();
            Check(HoFaceIrDocument.TryParse(editedIr, out var candidate, out string error), error);
            document.ApplyText(document.Snapshot().Value, candidate.Write());
            Check(document.CanUndo && document.IsDirty, "IR edit writes through shared authoring document");
            document.Save();
            Check(!document.IsDirty && File.ReadAllText(ir).Contains("\"x\": -0.2"), "save commits IR data");
            File.AppendAllText(v, " ");
            bool conflict = false;
            try { document.Save(); } catch (IOException) { conflict = true; }
            Check(conflict, "external edits are not overwritten");
            Check(document.Ir.nodes.First(x => x.id == "mouth").children[1].x == -.2f,
                "IR editor and authoring document share the same node model");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Check(bool value, string label)
    {
        checks++;
        if (!value) throw new Exception(label);
    }
    private static void Near(float actual, float expected, string label) =>
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) < .0001f, label + ": " + actual + " != " + expected);
}
