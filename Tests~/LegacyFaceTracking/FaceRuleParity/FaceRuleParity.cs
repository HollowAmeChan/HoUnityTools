// Run only in a disposable Unity project. See Run.ps1.
// Both production namespaces execute against REAL Unity curves and Mathf.
// The editor's device/preview controls are bypassed: this test compares the JSON rule layer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Hollow.HoUnityTools.Editor.FaceTracking;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;
using Mod = HoFaceTracking.Core;

public static class FaceRuleParity
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int frames, comparisons;
    static string fixture;
    static readonly Dictionary<string, float> Merged = (Dictionary<string, float>)typeof(HoFaceInputHub)
        .GetField("Merged", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    public static void RunBatch()
    {
        try
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../.ho-face-validation")))
                throw new Exception("Disposable project marker missing");
            fixture = Path.GetFullPath(Path.Combine(Application.dataPath, "../parity-fixture.hoface.json"));
            string[] args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-faceProfile");
            if (arg < 0 || arg + 1 == args.Length) throw new Exception("-faceProfile is required");
            string json = File.ReadAllText(args[arg + 1]);
            var pair = new Pair(json);
            Require(pair.Chain.Error == null, "real profile diagnostics: " + pair.Chain.Error);
            var wires = new List<string>();
            foreach (var row in pair.Profile.inputs)
                if (row != null && HoFaceExpression.TryParse(row.expression, out var expression, out _))
                    expression.CollectVariables(wires);
            var raw = new Dictionary<string, float>(StringComparer.Ordinal);
            var random = new System.Random(927);
            double now = 0;
            // Neutral, impulses, individual channels, random, missing packets, long smooth tails.
            for (int f = 0; f < 1200; f++)
            {
                raw.Clear();
                foreach (string wire in wires)
                {
                    float value = f < 30 || f > 900 ? 0 : f < 60 ? 1 : (float)random.NextDouble();
                    if (f >= 60 && f < 60 + wires.Count) value = wire == wires[f - 60] ? 1 : 0;
                    if (wire.StartsWith("Rotation_", StringComparison.Ordinal)) value = (value - .5f) * 180;
                    if (f >= 650 && f < 700 && random.NextDouble() < .5) continue;
                    raw[wire] = value;
                }
                float dt = f % 37 == 0 ? 0 : f % 41 == 0 ? .1f : 1f / (f % 2 == 0 ? 30 : 120);
                now += dt;
                pair.Frame(f >= 700 && f < 730 ? null : raw, dt, now);
            }
            Debug.Log("FACE_PARITY profile: inputs=" + pair.Profile.inputs.Count + " outputs="
                + pair.Profile.outputs.Count + " parameters=" + pair.Chain.Parameters.Count + " frames=1200");
            GoldenCases();
            Debug.Log("FACE_PARITY PASS frames=" + frames + " comparisons=" + comparisons);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static HoFaceOutput Row(string name, string expression, float defaultValue = 0) =>
        new HoFaceOutput { parameter = name, expression = expression, defaultValue = defaultValue,
            curve = AnimationCurve.Linear(-16, -16, 16, 16) };
    static HoFaceModifier Smooth(float seconds) => new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = seconds };
    static HoFaceModifier Delay(float seconds) => new HoFaceModifier { kind = HoFaceModifierKind.Delay, seconds = seconds };
    static void GoldenCases()
    {
        var p = new HoFaceMiddleware();
        p.inputs.Add(Row("x", "Wire", .3f));
        p.outputs.Add(Row("A", "x"));
        p.outputs[0].curve = AnimationCurve.Linear(0, 0, 1, 2);
        p.outputs.Add(Row("A", "out(\"A\") * 3"));
        p.outputs.Add(Row("B", "out(\"A\")"));
        p.outputs.Add(Row("A", "out(\"A\") + 1"));
        p.outputs.Add(Row("C", "out(\"A\") + out(\"B\")"));
        p.outputs.Add(Row("constant", "", 4));
        p.outputs[5].curve = AnimationCurve.Linear(0, 0, 1, 0);
        p.outputs.Add(Row("tiny", "0.0000005"));
        p.outputs.Add(Row("tinyRead", "out(\"tiny\") * 1000000"));
        var pair = new Pair(HoFaceProfile.Write(p));
        Require(pair.Chain.Error == null, "constants must not report parse errors");
        pair.Frame(new Dictionary<string, float> { { "Wire", .5f } }, 0, 0);
        pair.Expect("A", 4); pair.Expect("B", 3); pair.Expect("C", 7);
        pair.Expect("constant", 4); pair.Expect("tinyRead", 0);
        pair.Frame(null, .1f, .1); pair.Expect("A", 4); // input holds, output table starts fresh
        pair.Frame(new Dictionary<string, float> { { "Wire", 0 } }, .1f, .2);
        pair.Expect("A", 1); pair.Expect("B", 0); pair.Expect("C", 1);

        p = new HoFaceMiddleware();
        p.inputs.Add(Row("badInput", "out(\"later\")", .4f));
        p.outputs.Add(Row("badForward", "out(\"later\")", .7f));
        p.outputs.Add(Row("badMissing", "out(\"absent\")", .8f));
        p.outputs.Add(Row("badSelf", "out(\"badSelf\")", .9f));
        p.outputs.Add(Row("badSyntax", "out('later')", .6f));
        p.outputs.Add(Row("later", "", 1));
        p.outputs.Add(Row("readFallback", "out(\"badForward\")"));
        p.outputs.Add(Row("inputFallback", "badInput"));
        pair = new Pair(HoFaceProfile.Write(p));
        Require(pair.Chain.Error != null, "invalid references must report diagnostics");
        pair.Frame(null, .02f, 0);
        pair.Expect("badForward", .7f); pair.Expect("badMissing", .8f); pair.Expect("badSelf", .9f);
        pair.Expect("badSyntax", .6f); pair.Expect("readFallback", .7f); pair.Expect("inputFallback", .4f);

        p = new HoFaceMiddleware();
        p.inputs.Add(Row("x", "Wire"));
        p.inputs[0].modifiers.Add(Delay(.05f));
        p.inputs[0].modifiers.Add(Smooth(.08f));
        p.outputs.Add(Row("smooth", "Wire"));
        p.outputs[0].modifiers.Add(Smooth(.1f));
        p.outputs.Add(Row("delay", "Wire"));
        p.outputs[1].modifiers.Add(Delay(.05f));
        p.outputs.Add(Row("steps", "Wire"));
        p.outputs[2].modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Steps,
            steps = new List<HoFaceStep> { new HoFaceStep { trigger = .5f, target = 2, hold = .1f, threshold = .2f } } });
        p.outputs.Add(Row("chain", "out(\"smooth\") + out(\"delay\") + x"));
        p.outputs.Add(Row("chain", "out(\"chain\") * 2"));
        p.outputs[4].modifiers.Add(Smooth(.2f));
        pair = new Pair(HoFaceProfile.Write(p));
        pair.Frame(new Dictionary<string, float> { { "Wire", 1 } }, 0, 0);
        pair.Expect("smooth", 1); pair.Expect("delay", 1); pair.Expect("steps", 2); pair.Expect("chain", 6);
        pair.Frame(new Dictionary<string, float> { { "Wire", 0 } }, .02f, .02);
        pair.Expect("smooth", Mathf.Exp(-.2f)); pair.Expect("delay", 1); pair.Expect("steps", 2);
        pair.Frame(new Dictionary<string, float> { { "Wire", 0 } }, .02f, .04); pair.Expect("delay", 1);
        pair.Frame(new Dictionary<string, float> { { "Wire", 0 } }, .04f, .08); pair.Expect("delay", 0);
        pair.Frame(new Dictionary<string, float> { { "Wire", 0 } }, .04f, .12); pair.Expect("steps", 0);
        for (int f = 0; f < 800; f++)
            pair.Frame(new Dictionary<string, float> { { "Wire", f % 53 < 20 ? 1 : 0 } }, .01f, .13 + f * .01);

        // Legacy ARKit normalization is an adapter at publication, never an out() lookup alias.
        p = new HoFaceMiddleware();
        p.outputs.Add(Row("ARKit/EyeBlink_L", "", .2f));
        p.outputs.Add(Row("seen", "out(\"ARKit/EyeBlink_L\")"));
        p.outputs.Add(Row("eyeBlinkLeft", "", .6f));
        pair = new Pair(HoFaceProfile.Write(p)); pair.Frame(null, 0, 0);
        pair.Expect("eyeBlinkLeft", .6f); pair.Expect("seen", .2f);
        Debug.Log("FACE_PARITY golden cases: overwrite, ordered references, defaults, diagnostics, curves, "
            + "first-frame smooth, delay, hold/hysteresis, independent row state, tiny values, legacy keys");
    }

    sealed class Pair
    {
        public readonly HoFaceMiddleware Profile;
        public readonly Mod.HoFaceChain Chain;
        readonly HoFaceAnimationSession editor;
        public Pair(string json)
        {
            Require(HoFaceProfile.TryParse(json, out Profile, out string error), error);
            Require(Mod.HoFaceProfile.TryParse(json, out var mod, out error), error);
            Chain = new Mod.HoFaceChain(mod);
            File.WriteAllText(fixture, json);
            // No scene, Animator, live network, or substitute implementation of the rule evaluator.
            editor = (HoFaceAnimationSession)FormatterServices.GetUninitializedObject(typeof(HoFaceAnimationSession));
            Set(editor, "Settings", new HoFaceDebugSettings { profilePath = fixture });
            Set(editor, "Input", new float[52]);
            Set(editor, "inputIndex", new Dictionary<string, int>(StringComparer.Ordinal));
            Set(editor, "outputIndex", new Dictionary<string, int>(StringComparer.Ordinal));
            Set(editor, "previews", new Dictionary<string, float>(StringComparer.Ordinal));
            Set(editor, "touched", new List<string>());
            Set(editor, "outputTable", new HoFaceOutputTable());
            Call(editor, "BuildOutputs");
        }
        public void Frame(Dictionary<string, float> raw, float dt, double now)
        {
            Merged.Clear();
            if (raw != null) foreach (var pair in raw) Merged[pair.Key] = pair.Value;
            Call(editor, "EvaluateInputs", now, Mathf.Max(0, dt));
            var inputs = Get<float[]>(editor, "inputValues");
            var modInputs = Get<float[]>(Chain, "inputValues");
            var index = Get<Dictionary<string, int>>(editor, "inputIndex");
            // Feed the same rule-level input into the editor's preview channel bridge.
            for (int i = 0; i < 52; i++)
                editor.Input[i] = index.TryGetValue(HoFaceTrackingChannels.Names[i], out int row) ? inputs[row]
                    : raw != null && raw.TryGetValue(HoFaceTrackingChannels.Names[i], out float v) ? v : 0;
            Call(editor, "EvaluateOutputs", dt, now);
            Set(editor, "primed", true);
            Chain.Evaluate(raw, dt, now);
            for (int i = 0; i < inputs.Length; i++) Equal(inputs[i], modInputs[i], "input row " + i);
            var expected = Get<float[]>(editor, "outputValues");
            var actual = Get<float[]>(Chain, "outputValues");
            var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < expected.Length; i++)
            {
                Equal(expected[i], actual[i], "output row " + i + " " + Profile.outputs[i]?.parameter);
                string key = Profile.outputs[i]?.parameter;
                if (string.IsNullOrEmpty(key)) continue;
                if (key.StartsWith("ARKit/", StringComparison.Ordinal))
                {
                    key = key.Substring(6);
                    int channel = HoFaceTrackingChannels.IndexOf(key);
                    if (channel >= 0) key = HoFaceTrackingChannels.Names[channel];
                }
                parameters[key] = expected[i];
            }
            Require(parameters.Count == Chain.Parameters.Count, "published key count");
            foreach (var pair in parameters)
            {
                Require(Chain.Parameters.TryGetValue(pair.Key, out float value), "missing key " + pair.Key);
                Equal(pair.Value, value, "published " + pair.Key);
            }
            frames++;
        }
        public void Expect(string name, float expected) => Equal(expected, Chain.Parameters[name], "golden " + name);
    }
    static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Fields).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Fields).SetValue(obj, value);
    static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Fields).Invoke(obj, args);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Equal(float expected, float actual, string message)
    {
        comparisons++;
        Require(!float.IsNaN(actual) && !float.IsInfinity(actual) && Math.Abs(expected - actual) <= 1e-6f,
            "frame " + frames + " " + message + ": " + expected.ToString("R") + " != " + actual.ToString("R"));
    }
}
