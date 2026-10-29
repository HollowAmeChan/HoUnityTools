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
