// Run in the marked SDK validation project through the packaging harness.
using System;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

public static class HoVtsAuthoringValidation
{
    private const string PathName = "Assets/VtsAuthoring.prefab";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var root = new GameObject("Authoring");
        var component = root.AddComponent<HoVtsParameterReceiver>();
        var initial = new HoFaceMiddleware { displayName = "Initial" };
        var input = new HoFaceOutput { parameter = "Canonical", expression = "Wire", curve = AnimationCurve.Linear(-180, -180, 180, 180) };
        input.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Steps,
            steps = new System.Collections.Generic.List<HoFaceStep> { new HoFaceStep { trigger = .5f, enterSeconds = .2f } } });
        initial.inputs.Add(input);
        initial.outputs.Add(new HoFaceOutput { parameter = "Constant", expression = "", defaultValue = .25f });
        component.SetMiddleware(initial);
        PrefabUtility.SaveAsPrefabAsset(root, PathName);
        UnityEngine.Object.DestroyImmediate(root);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathName);
        component = asset.GetComponent<HoVtsParameterReceiver>();
        HoFaceProfileWindow window = null;
        try
        {
            var draft = HoVtsRuleAuthoring.Clone(component.Middleware);
            draft.outputs[0].defaultValue = .75f;
            Require(component.Middleware.outputs[0].defaultValue == .25f, "draft editing cannot mutate the role");
            string backup = HoVtsRuleAuthoring.Commit(component, draft);
            Require(File.Exists(backup) && HoFaceProfile.TryParse(File.ReadAllText(backup), out var saved, out _) && saved.outputs[0].defaultValue == .25f,
                "pre-save JSON backup contains recoverable previous rules");
            draft.outputs[0].defaultValue = 1;
            Require(component.Middleware.outputs[0].defaultValue == .75f, "saved rule graph does not share draft objects");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Require(component.Middleware.outputs[0].defaultValue == .25f, "Unity undo restores the serialized rule graph");
            Undo.PerformRedo();
            Require(component.Middleware.outputs[0].defaultValue == .75f, "Unity redo restores the saved graph");
            PrefabUtility.SavePrefabAsset(asset);
            var contents = PrefabUtility.LoadPrefabContents(PathName);
            try
            {
                var reloaded = contents.GetComponent<HoVtsParameterReceiver>().Middleware;
                Require(reloaded.outputs[0].defaultValue == .75f, "prefab reload retains embedded rules");
                Require(Mathf.Abs(reloaded.inputs[0].curve.Evaluate(45) - 45) < .01f && reloaded.inputs[0].modifiers[0].steps[0].enterSeconds == .2f,
                    "prefab reload retains signed curves and nested modifier steps");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            HoFaceProfileWindow.Open(component);
            window = EditorWindow.GetWindow<HoFaceProfileWindow>();
            var windowDraft = (HoFaceMiddleware)typeof(HoFaceProfileWindow).GetField("middleware", Private).GetValue(window);
            Require(windowDraft != component.Middleware && windowDraft.outputs[0] != component.Middleware.outputs[0], "existing row editor opens a detached component draft");
            windowDraft.outputs[0].defaultValue = .5f;
            Require(component.Middleware.outputs[0].defaultValue == .75f, "row editor waits for explicit save");
            typeof(HoFaceProfileWindow).GetMethod("SaveProfile", Private).Invoke(window, null);
            Require(component.Middleware.outputs[0].defaultValue == .5f, "row editor save targets the component without a JSON asset");
            Require((TextAsset)typeof(HoFaceProfileWindow).GetField("profile", Private).GetValue(window) == null, "component editor has no required external profile file");
            string exported = HoFaceProfile.Write(component.Middleware);
            Require(HoFaceProfile.TryParse(exported, out var imported, out _) && imported.outputs[0].defaultValue == .5f, "component rules keep JSON import/export support");
            Debug.Log("HO_VTS_AUTHORING PASS");
        }
        finally
        {
            if (window != null) window.Close();
            Undo.ClearAll();
        }
    }

    public static void RunPlay()
    {
        Isolation();
        Trace();
        DebugSession();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathName);
        Require(asset != null, "authoring fixture exists for play-mode editing");
        var source = asset.GetComponent<HoVtsParameterReceiver>();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        var receiver = instance.GetComponent<HoVtsParameterReceiver>();
        try
        {
            float original = source.Middleware.outputs[0].defaultValue;
            string assetBefore = File.ReadAllText(PathName);
            double receivedAt = Time.realtimeSinceStartupAsDouble;
            receiver.ApplyRawFrame(new System.Collections.Generic.Dictionary<string, float> { { "Wire", 42 } }, true, receivedAt, "authoring-test");
            var draft = HoVtsRuleAuthoring.Clone(receiver.Middleware);
            draft.outputs[0].defaultValue = .8f;
            Require(HoVtsRuleAuthoring.Commit(receiver, draft) == null, "play-mode apply is explicitly temporary");
            Require(receiver.Get("Constant") == .8f, "temporary apply refreshes the shared runtime evaluator immediately");
            Require(source.Middleware.outputs[0].defaultValue == original && File.ReadAllText(PathName) == assetBefore, "temporary apply leaves prefab asset unchanged");
            float persisted = original == .9f ? .7f : .9f;
            draft.outputs[0].defaultValue = persisted;
            string backup = HoVtsRuleAuthoring.Commit(receiver, draft, true);
            Require(source.Middleware.outputs[0].defaultValue == persisted && receiver.Get("Constant") == persisted,
                "explicit prefab save updates author asset and live instance: source=" + source.Middleware.outputs[0].defaultValue
                + " live=" + receiver.Get("Constant") + " configured=" + receiver.Middleware.outputs[0].defaultValue
                + " hasFrame=" + receiver.HasFrame + " lastPacket=" + receiver.LastFrameTime + " lastSolve=" + receiver.LastEvaluationTime);
            Require(receiver.LastFrameTime == receivedAt && receiver.Source == "authoring-test" && receiver.GetRaw("Wire") == 42,
                "prefab save restores the raw snapshot with its original packet age and source");
            Require(File.Exists(backup) && HoFaceProfile.TryParse(File.ReadAllText(backup), out var previous, out _)
                && previous.outputs[0].defaultValue == original, "play-mode save backs up prefab rules rather than temporary instance rules");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Require(source.Middleware.outputs[0].defaultValue == original, "prefab rule save remains undoable during play");
            PrefabUtility.SavePrefabAsset(asset);
            Debug.Log("HO_VTS_AUTHORING_PLAY PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); Undo.ClearAll(); }
    }

    private static void Isolation()
    {
        var root = new GameObject("Isolation");
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        var rules = new HoFaceMiddleware();
        rules.outputs.Add(new HoFaceOutput { parameter = "Gate", expression = "Wire", defaultValue = 0 });
        rules.outputs.Add(new HoFaceOutput { parameter = "LipCustom", expression = "Wire", defaultValue = .1f });
        rules.outputs.Add(new HoFaceOutput { parameter = "BrowCustom", expression = "Wire * 2", defaultValue = .2f });
        rules.outputs.Add(new HoFaceOutput { parameter = "Gate", expression = "", defaultValue = 1 });
        rules.outputs.Add(new HoFaceOutput { parameter = "Pose", expression = "Wire * 10" });
        receiver.SetMiddleware(rules);
        string original = HoFaceProfile.Write(rules);
        try
        {
            using (var session = new HoVtsDebugSession(root, false))
            {
                var raw = new System.Collections.Generic.Dictionary<string, float> { { "Wire", .3f } };
                string[] scope = { "LipCustom", "BrowCustom", "Gate" };
                session.PinOutput("BrowCustom", .7f);
                session.IsolateOutputs(new[] { "LipCustom" }, scope);
                using (var recording = new HoVtsTraceRecorder(session, 0, 10))
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    session.Feed(raw, true, now);
                    recording.Stop();
                    Require(receiver.Get("LipCustom") == .3f && receiver.Get("BrowCustom") == .2f,
                        "isolation keeps selected custom outputs and uses declared neutral values for others");
                    Require(receiver.Get("Gate") == 1 && receiver.Get("Pose") == 3, "isolation preserves constant gates and outputs outside the explicit scope");
                    Require(session.OutputPins["BrowCustom"] == .7f && session.IsolationOverrides.Count == 1,
                        "isolation overlays manual pins without deleting them");
                    var frame = JsonUtility.FromJson<HoVtsTraceRecorder.Frame>(File.ReadAllLines(recording.FilePath)[1]);
                    Require(frame.isolationOverrides.Length == 1 && frame.isolationOverrides[0].value == .2f
                        && frame.manualOutputOverrides[0].value == .7f && frame.outputOverrides[0].value == .2f,
                        "trace distinguishes isolation, saved manual pins and effective publication");
                }
                session.RestoreIsolation();
                session.Feed(raw, true, Time.realtimeSinceStartupAsDouble);
                Require(receiver.Get("BrowCustom") == .7f && session.IsolationOverrides.Count == 0, "restore returns the previous manual override");
                session.IsolateOutputs(new[] { "BrowCustom" }, scope);
                session.Feed(raw, true, Time.realtimeSinceStartupAsDouble);
                Require(receiver.Get("LipCustom") == .1f && receiver.Get("BrowCustom") == .7f, "switching isolated regions does not accumulate old mute state");
                bool rejected = false;
                try { session.IsolateOutputs(new[] { "Unknown" }, scope); } catch (ArgumentException) { rejected = true; }
                Require(rejected && session.IsolationOverrides.ContainsKey("LipCustom"), "invalid selection leaves current isolation intact");
                receiver.SetMiddleware(HoVtsRuleAuthoring.Clone(rules));
                session.Feed(raw, true, Time.realtimeSinceStartupAsDouble);
                Require(session.IsolationOverrides.Count == 0 && receiver.Get("LipCustom") == .3f, "changing rules exits isolation instead of retaining stale defaults");
                Require(HoFaceProfile.Write(receiver.Middleware) == original, "isolation never edits character rules");
                session.ClearPins();
                session.Feed(raw, true, Time.realtimeSinceStartupAsDouble);
                Require(Mathf.Abs(receiver.Get("BrowCustom") - .6f) < .0001f, "clear all overrides also exits isolation");
            }
            Debug.Log("HO_VTS_ISOLATION PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Trace()
    {
        var root = new GameObject("Trace");
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        var rules = new HoFaceMiddleware();
        rules.inputs.Add(new HoFaceOutput { parameter = "Canonical", expression = "Wire", curve = AnimationCurve.Linear(-1, -2, 1, 2) });
        rules.outputs.Add(new HoFaceOutput { parameter = "Result", expression = "Canonical * 2", curve = AnimationCurve.Linear(-2, -6, 2, 6) });
        receiver.SetMiddleware(rules);
        try
        {
            using (var session = new HoVtsDebugSession(root, false))
            using (var recording = new HoVtsTraceRecorder(session, 0, 10))
            {
                session.PinOutput("Result", .9f);
                double now = Time.realtimeSinceStartupAsDouble;
                session.Feed(new System.Collections.Generic.Dictionary<string, float> { { "Wire", .1f } }, true, now);
                receiver.Tick(now + .05);
                for (int i = 0; i < 20; i++) receiver.Get("Result");
                Require(!receiver.Tick(now + .05) && recording.Samples == 2, "trace observes actual solves without adding solves for reads or repeated time");
                recording.Stop();
                var lines = File.ReadAllLines(recording.FilePath);
                Require(lines.Length == 4 && lines[0].Contains("ho-vts-trace") && lines[3].Contains("manual-stop"), "trace file has header, observed frames and an explicit footer");
                var frame = JsonUtility.FromJson<HoVtsTraceRecorder.Frame>(lines[1]);
                Require(Mathf.Abs(frame.inputs[0].expression - .1f) < .0001f && Mathf.Abs(frame.inputs[0].curve - .2f) < .0001f,
                    "trace captures actual input expression and signed curve stages");
                Require(Mathf.Abs(frame.outputs[0].expression - .4f) < .0001f && Mathf.Abs(frame.outputs[0].curve - 1.2f) < .0001f
                    && Mathf.Abs(frame.outputs[0].modified - 1.2f) < .0001f && frame.outputs[0].published == .9f,
                    "trace separates expression, curve, solved and overridden publication");
                Require(frame.wires[0].value == .1f && frame.outputOverrides[0].value == .9f && frame.tracked, "trace includes raw values, overrides and tracking state");
                var second = JsonUtility.FromJson<HoVtsTraceRecorder.Frame>(lines[2]);
                Require(second.clock > frame.clock && second.lastReceivedAt == frame.lastReceivedAt, "autonomous tick does not pretend a packet arrived");
                using (var changed = new HoVtsTraceRecorder(session, 0, 10))
                {
                    receiver.SetMiddleware(HoVtsRuleAuthoring.Clone(rules));
                    receiver.Tick(now + .1);
                    Require(!changed.Recording && changed.StopReason == "configuration-changed" && changed.Samples == 0,
                        "trace does not mix different rule revisions");
                }
                using (var external = new HoVtsTraceRecorder(session, 0, 10))
                {
                    receiver.ApplyRawFrame(new System.Collections.Generic.Dictionary<string, float>(), false, now + .2, "external");
                    Require(!external.Recording && external.StopReason == "input-source-changed" && external.Samples == 0,
                        "trace does not collect another input owner's data");
                }
            }
            Debug.Log("HO_VTS_TRACE PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void DebugSession()
    {
        var root = new GameObject("DebugSession");
        var child = new GameObject("VTS"); child.transform.SetParent(root.transform, false);
        var receiver = child.AddComponent<HoVtsParameterReceiver>();
        var rules = new HoFaceMiddleware();
        rules.inputs.Add(new HoFaceOutput { parameter = "Canonical", expression = "Wire" });
        rules.outputs.Add(new HoFaceOutput { parameter = "Result", expression = "Canonical * 2" });
        rules.outputs.Add(new HoFaceOutput { parameter = "Downstream", expression = "out(\"Result\") + 1" });
        receiver.SetMiddleware(rules);
        string original = HoFaceProfile.Write(rules);
        try
        {
            var session = new HoVtsDebugSession(root, false);
            Require(session.Receiver == receiver, "debug target discovers its own receiver without rules/controller inputs");
            bool duplicate = false;
            try { new HoVtsDebugSession(root, false); } catch (InvalidOperationException) { duplicate = true; }
            Require(duplicate, "two debug sessions cannot drive one receiver");
            session.PinInput("Canonical", .4f);
            session.PinOutput("Result", .9f);
            session.Feed(new System.Collections.Generic.Dictionary<string, float> { { "Wire", .2f } }, true, Time.realtimeSinceStartupAsDouble);
            Require(receiver.Get("Result") == .9f && Mathf.Abs(receiver.Get("Downstream") - 1.8f) < .0001f,
                "input pins affect expressions; output pins publish after ordered rule evaluation");
            Require(receiver.TryReadInputRow(0, out float configured, out float effective, out bool fresh)
                && configured == .2f && effective == .4f && fresh, "input monitor separates configured and overridden values");
            Require(receiver.TryReadOutputRow(0, out float modified, out float published)
                && modified == .8f && published == .9f, "output monitor separates solved and published values");
            Require(HoFaceProfile.Write(receiver.Middleware) == original, "debug pins never modify serialized role rules");
            session.ReleaseInput("Canonical");
            session.Feed(new System.Collections.Generic.Dictionary<string, float> { { "Wire", .2f } }, true, Time.realtimeSinceStartupAsDouble);
            Require(receiver.Get("Result") == .9f && Mathf.Abs(receiver.Get("Downstream") - 1.4f) < .0001f, "one input pin can be released without removing output pins");
            session.ReleaseOutput("Result");
            session.Feed(new System.Collections.Generic.Dictionary<string, float> { { "Wire", .2f } }, true, Time.realtimeSinceStartupAsDouble);
            Require(receiver.Get("Result") == .4f, "one output pin can be released independently");
            session.ClearPins();
            session.Feed(new System.Collections.Generic.Dictionary<string, float> { { "Wire", .3f } }, true, Time.realtimeSinceStartupAsDouble);
            Require(Mathf.Abs(receiver.Get("Result") - .6f) < .0001f, "clearing pins restores normal role evaluation");
            session.Dispose();
            Require(!receiver.HasFrame && receiver.ValueCount == 0, "debug stop releases only its own input");
            using (var next = new HoVtsDebugSession(root, false))
            {
                next.PinOutput("Result", .123f);
                next.Feed(new System.Collections.Generic.Dictionary<string, float>(), false, Time.realtimeSinceStartupAsDouble);
                receiver.ApplyRawFrame(new System.Collections.Generic.Dictionary<string, float> { { "Wire", 1 } }, true, Time.realtimeSinceStartupAsDouble, "external");
                Require(receiver.Get("Result") == 2, "source-bound debug overrides cannot affect another input source");
            }
            Require(receiver.Source == "external" && receiver.HasFrame, "stopping debug does not clear a newer external source");
            receiver.ClearFrame();
            Require(HoVtsDebugHost.Start(root), "new panel host starts from only the character target");
            HoVtsDebugHost.Record(5, 0);
            HoVtsDebugHost.Session.Feed(new System.Collections.Generic.Dictionary<string, float>(), false, Time.realtimeSinceStartupAsDouble);
            Require(HoVtsDebugHost.Recorder.Recording && HoVtsDebugHost.Recorder.Samples == 1, "panel host records the component session");
            HoVtsDebugWindow.Open();
            var window = EditorWindow.GetWindow<HoVtsDebugWindow>();
            try
            {
                window.position = new Rect(50, 50, 480, 540);
                typeof(HoVtsDebugWindow).GetField("target", Private).SetValue(window, root);
                for (int tab = 0; tab < 3; tab++)
                {
                    typeof(HoVtsDebugWindow).GetField("tab", Private).SetValue(window, tab);
                    window.SendEvent(new Event { type = EventType.Layout });
                    window.SendEvent(new Event { type = EventType.Repaint });
                }
                Require(HoVtsDebugHost.Running, "all three panel views render without stopping the character");
            }
            finally { window.Close(); }
            Require(HoVtsDebugHost.Running, "closing the panel keeps the debug host running");
            Require(HoVtsDebugHost.Recorder.Recording, "closing the panel keeps recording active");
            HoVtsDebugHost.Stop();
            Require(!HoVtsDebugHost.Running, "panel host stop releases the session");
            Require(!HoVtsDebugHost.Recorder.Recording && HoVtsDebugHost.Recorder.StopReason == "session-stopped", "panel host stop closes its trace with a reason");
            var extra = new GameObject("Ambiguous"); extra.transform.SetParent(root.transform, false); extra.AddComponent<HoVtsParameterReceiver>();
            bool ambiguous = false;
            try { new HoVtsDebugSession(root, false); } catch (InvalidOperationException) { ambiguous = true; }
            Require(ambiguous && root.GetComponentsInChildren<HoVtsParameterReceiver>().Length == 2, "ambiguous targets fail without creating components");
            Debug.Log("HO_VTS_DEBUG_SESSION PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Debug.Log("HO_VTS_AUTHORING ok " + message);
    }
}
