// Real-engine integration checks. Install only in a disposable project with Run.ps1.
using System;
using System.Collections.Generic;
using System.IO;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Editor.FaceTracking;
using HoFaceTracking.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HoVtsControllerValidation
{
    private const string Pending = "HoVtsControllerValidation.Pending";
    private static int assertions;
    private static GameObject timingProbe;
    private static HoVtsParameterReceiver timingReceiver;
    private static Transform timingHead;
    private static double timingStarted;
    private static int timingFrame;

    [InitializeOnLoadMethod]
    private static void Resume() { EditorApplication.update += RunWhenPlaying; }

    public static void RunBatch()
    {
        if (!File.Exists(Path.Combine(Application.dataPath, "../.ho-vts-validation")))
            throw new InvalidOperationException("Disposable project marker missing");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void RunWhenPlaying()
    {
        if (timingProbe != null)
        {
            try
            {
                if (timingReceiver.InputExpired)
                {
                    Check(Time.frameCount > timingFrame, "automatic timeout ran across real Unity frames");
                    Check(!timingReceiver.Tracked && !timingReceiver.HasFrame, "LateUpdate clears a silent adapter frame");
                    Near(timingReceiver.Get("Pose"), 0, "automatic timeout clears processed rules");
                    Near(timingReceiver.GetRaw("Rotation_y"), 30, "automatic timeout retains raw diagnosis");
                    RotationNear(timingHead.localRotation, Quaternion.identity, "automatic pose consumer stops writing after receiver timeout");
                    UnityEngine.Object.Destroy(timingProbe);
                    timingProbe = null;
                    Debug.Log("HO_VTS_CONTROLLER PASS assertions=" + assertions);
                    EditorApplication.Exit(0);
                }
                else if (Time.realtimeSinceStartupAsDouble - timingStarted > 5) throw new Exception("Unity automatic timeout did not run");
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            return;
        }
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        SessionState.SetBool(Pending, false);
        try
        {
            Run();
            timingProbe = new GameObject("AutomaticTiming");
            timingReceiver = timingProbe.AddComponent<HoVtsParameterReceiver>();
            timingReceiver.InputTimeoutSeconds = .3f;
            var rules = new HoFaceMiddleware();
            rules.outputs.Add(new HoFaceOutput { parameter = "Pose", expression = "Rotation_y" });
            timingReceiver.SetMiddleware(rules);
            timingHead = new GameObject("Head").transform;
            timingHead.SetParent(timingProbe.transform, false);
            var pose = timingProbe.AddComponent<HoVtsLookAt>();
            pose.ParameterReceiver = timingReceiver; pose.ReferenceFrame = timingProbe.transform; pose.Head = timingHead;
            timingStarted = Time.realtimeSinceStartupAsDouble;
            timingFrame = Time.frameCount;
            timingReceiver.ApplyRawFrame(new Dictionary<string, float> { { "Rotation_x", 0 }, { "Rotation_y", 30 }, { "Rotation_z", 0 } }, true, timingStarted);
            pose.EvaluateNow(0);
            RotationNear(timingHead.localRotation, Quaternion.Euler(0, 30, 0), "automatic timeout probe starts with an active pose");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Run()
    {
        Delivery();
        Timing();
        Pose();
        var root = new GameObject("Character");
        var originalAnimator = root.AddComponent<Animator>();
        originalAnimator.enabled = false;
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        var face = new GameObject("Face");
        face.transform.SetParent(root.transform, false);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        mesh.AddBlendShapeFrame("Driven", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
        mesh.AddBlendShapeFrame("Unowned", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
        AssetDatabase.CreateAsset(mesh, "Assets/Face.asset");
        var renderer = face.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.SetBlendShapeWeight(0, 17);
        renderer.SetBlendShapeWeight(1, 33);
        var bone = new GameObject("Bone").transform;
        bone.SetParent(root.transform, false);
        bone.localPosition = new Vector3(1, 2, 3);
        var gate = new GameObject("Gate").transform;
        gate.SetParent(root.transform, false);
        var rotation = new GameObject("Rotation").transform;
        rotation.SetParent(root.transform, false);
        var host = new GameObject("VTS");
        host.transform.SetParent(root.transform, false);
        var component = host.AddComponent<HoVtsController>();
        component.AutoRun = false;
        component.ParameterReceiver = receiver;
        component.OutputRoot = root.transform;
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Validation.controller");
        controller.AddParameter("Drive", AnimatorControllerParameterType.Float);
        controller.AddParameter("One", AnimatorControllerParameterType.Float);
        var controllerParams = controller.parameters;
        controllerParams[1].defaultFloat = 1;
        controller.parameters = controllerParams;
        var low = Clip("Low", 0, 1, 1);
        var high = Clip("High", 100, 5, 2);
        AssetDatabase.AddObjectToAsset(low, controller);
        AssetDatabase.AddObjectToAsset(high, controller);
        var tree = new BlendTree { name = "Face", blendType = BlendTreeType.Simple1D, blendParameter = "Drive", useAutomaticThresholds = false };
        tree.AddChild(low, 0); tree.AddChild(high, 1);
        AssetDatabase.AddObjectToAsset(tree, controller);
        var state = controller.layers[0].stateMachine.AddState("Face");
        state.motion = tree;
        state.writeDefaultValues = false;
        // A separate layer verifies missing dictionary parameters keep their controller defaults.
        controller.AddLayer("Constant");
        var layers = controller.layers;
        layers[1].defaultWeight = 1;
        controller.layers = layers;
        var constant = new BlendTree { name = "Constant", blendType = BlendTreeType.Simple1D, blendParameter = "One", useAutomaticThresholds = false };
        var zeroScale = ScaleClip("Zero", 1);
        var oneScale = ScaleClip("One", 3);
        AssetDatabase.AddObjectToAsset(zeroScale, controller);
        AssetDatabase.AddObjectToAsset(oneScale, controller);
        constant.AddChild(zeroScale, 0); constant.AddChild(oneScale, 1);
        AssetDatabase.AddObjectToAsset(constant, controller);
        var constantState = layers[1].stateMachine.AddState("Constant");
        constantState.motion = constant;
        constantState.writeDefaultValues = false;
        component.Controller = controller;
        HoVtsControllerBindings.Prepare(component);
        Check(component.BindingCount == 8, "authored property count");

        receiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", .5f } });
        Check(component.EvaluateNow(0), "controller evaluates: " + component.Status);
        Near(renderer.GetBlendShapeWeight(0), 50, "blend tree input");
        Near(renderer.GetBlendShapeWeight(1), 33, "unowned shape preserved");
        Near(bone.localPosition.x, 3, "position curve");
        Near(bone.localScale.y, 1.5f, "scale curve");
        Near(gate.localScale.z, 3, "missing One preserves default 1");
        Near(Quaternion.Angle(rotation.localRotation, Quaternion.Euler(0, 0, 25)), 0, "quaternion animation");
        Check(!originalAnimator.enabled && originalAnimator.runtimeAnimatorController == null, "disabled character Animator untouched");
        Check(host.GetComponent<Animator>() == null, "controller slot requires no attached Animator");

        bone.localPosition = new Vector3(3, 77, 88);
        bone.localScale = new Vector3(7, bone.localScale.y, bone.localScale.z);
        bone.localRotation = Quaternion.Euler(11, 22, 33);
        renderer.SetBlendShapeWeight(1, 65);
        receiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", 0 } });
        Check(component.EvaluateNow(0), "second evaluation");
        Near(renderer.GetBlendShapeWeight(0), 0, "authored zero is written");
        Near(renderer.GetBlendShapeWeight(1), 65, "other shape writer coexists");
        Near(bone.localPosition.x, 1, "new parameter is applied immediately");
        Near(bone.localPosition.y, 77, "unowned position axis preserved");
        Near(bone.localScale.x, 7, "unowned scale axis preserved");
        Near(Quaternion.Angle(bone.localRotation, Quaternion.Euler(11, 22, 33)), 0, "official rotation writer coexists");

        var other = new GameObject("Other");
        var otherReceiver = other.AddComponent<HoVtsParameterReceiver>();
        otherReceiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", 1 } });
        Check(component.EvaluateNow(0), "other character frame cannot change this controller");
        Near(renderer.GetBlendShapeWeight(0), 0, "per-character isolation");

        component.enabled = false;
        Check(!component.IsReady, "disabling releases graph");
        Near(renderer.GetBlendShapeWeight(0), 17, "disable restores owned shape");
        Near(renderer.GetBlendShapeWeight(1), 65, "disable preserves unowned shape");
        Near(bone.localPosition.y, 77, "disable preserves external axis");
        component.enabled = true;
        receiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", 1 } });
        Check(component.EvaluateNow(0), "reenable recreates graph");
        Near(renderer.GetBlendShapeWeight(0), 100, "reenabled output");
        receiver.ClearFrame();
        Check(!component.EvaluateNow(0), "released input stops the controller");
        Near(renderer.GetBlendShapeWeight(0), 17, "released input restores owned shape");
        receiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", 1 } });
        Check(component.EvaluateNow(0), "new input restarts released controller");
        receiver.enabled = false;
        Check(!component.EvaluateNow(0), "disabled receiver releases output");
        Near(renderer.GetBlendShapeWeight(0), 17, "receiver disable restores shape");
        receiver.enabled = true;
        component.Controller = null;
        Check(!component.EvaluateNow(0) && !component.IsReady, "removed controller releases graph");

        // Binding metadata survives serialization with the controller and role references.
        component.Controller = controller;
        HoVtsControllerBindings.Prepare(component);
        string prefabPath = "Assets/Character.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var saved = prefab.GetComponentInChildren<HoVtsController>();
        Check(saved.BindingCount == 8 && saved.Controller == controller && saved.ParameterReceiver != null && saved.OutputRoot == prefab.transform,
            "prefab retains slot, ownership and internal references");
        var second = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var secondComponent = second.GetComponentInChildren<HoVtsController>();
        secondComponent.ParameterReceiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", .25f } });
        Check(secondComponent.EvaluateNow(0), "second character independently evaluates");
        Near(second.GetComponentInChildren<SkinnedMeshRenderer>().GetBlendShapeWeight(0), 25, "second character output");
        receiver.SetProcessedValues(new Dictionary<string, float> { { "Drive", .75f } });
        Check(component.EvaluateNow(0), "first character independently evaluates");
        Near(renderer.GetBlendShapeWeight(0), 75, "first character output");
        Near(second.GetComponentInChildren<SkinnedMeshRenderer>().GetBlendShapeWeight(0), 25, "first controller cannot affect second");
        secondComponent.Stop();
        UnityEngine.Object.Destroy(second);
        component.Stop();
        UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(other);
    }

    private static void Timing()
    {
        var root = new GameObject("Timing");
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        receiver.InputTimeoutSeconds = .5f;
        var rules = new HoFaceMiddleware();
        var row = new HoFaceOutput { parameter = "Smoothed", expression = "Raw" };
        row.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = .1f });
        rules.outputs.Add(row);
        var delayed = new HoFaceOutput { parameter = "Delayed", expression = "Raw" };
        delayed.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Delay, seconds = .2f });
        rules.outputs.Add(delayed);
        var held = new HoFaceOutput { parameter = "Held", expression = "Raw" };
        held.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Steps,
            steps = new List<HoFaceStep> { new HoFaceStep { trigger = .5f, target = 1, enterSeconds = .2f } } });
        rules.outputs.Add(held);
        rules.outputs.Add(new HoFaceOutput { parameter = "One", expression = "", defaultValue = 1 });
        receiver.SetMiddleware(rules);
        receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 0 } }, true, 10, "clock-test");
        receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 10.1, "clock-test");
        Near(receiver.Get("Smoothed"), 1 - Mathf.Exp(-1), "smoothing advances on input");
        Check(receiver.Tick(10.2), "host can advance without a network packet");
        Near(receiver.Get("Smoothed"), 1 - Mathf.Exp(-2), "smoothing continues between packets");
        Check(receiver.LastFrameTime == 10.1 && receiver.LastEvaluationTime == 10.2, "packet age is separate from evaluation time");
        float before = receiver.Get("Smoothed");
        var snapshot = new Dictionary<string, float>();
        for (int i = 0; i < 20; i++) { receiver.CopyValuesTo(snapshot); receiver.Get("Smoothed"); }
        Near(receiver.Get("Smoothed"), before, "multiple consumers do not advance rules");
        Check(!receiver.Tick(10.2), "same time does not advance modifiers twice");
        receiver.Tick(10.25);
        receiver.Tick(10.29);
        Near(receiver.Get("Delayed"), 0, "delay cannot expose the next sample before it is due");
        Near(receiver.Get("Held"), 0, "enter hold waits for its full duration");
        receiver.Tick(10.31);
        Near(receiver.Get("Delayed"), 1, "delayed value releases without another network packet");
        Near(receiver.Get("Held"), 1, "enter hold completes between network packets");
        receiver.Tick(10.6);
        Check(receiver.Tracked && !receiver.InputExpired, "input is valid through timeout boundary");
        receiver.Tick(10.60001);
        Check(!receiver.Tracked && receiver.InputExpired && receiver.HasFrame, "stopped adapter expires without another input frame");
        Near(receiver.Get("Smoothed"), 0, "timeout resets stale expression to neutral");
        Near(receiver.Get("One"), 1, "timeout preserves author constants");
        Near(receiver.GetRaw("Raw"), 1, "timeout preserves raw diagnostic snapshot");
        Check(receiver.Source == "clock-test" && receiver.LastFrameTime == 10.1, "timeout does not pretend a new packet arrived");
        receiver.Tick(10.8);
        receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 11, "clock-test");
        Check(receiver.Tracked && !receiver.InputExpired && receiver.Get("Smoothed") > 0 && receiver.Get("Smoothed") < 1, "fresh input reacquires from neutral state");
        Check(!receiver.Tick(10.9), "backward host time cannot rewind filters");
        bool rejected = false;
        try { receiver.ApplyRawFrame(snapshot, true, 10.9); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && receiver.LastFrameTime == 11, "backward input time is rejected without replacing state");
        receiver.ClearFrame();
        Check(!receiver.InputExpired && !receiver.Tick(12) && !receiver.HasFrame, "clear releases timeout state and stops ticking");
        UnityEngine.Object.Destroy(root);
    }

    private static void Pose()
    {
        var root = new GameObject("PoseCharacter");
        root.transform.rotation = Quaternion.Euler(12, 150, -5);
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localRotation = Quaternion.Euler(7, 5, 11);
        Quaternion headRest = head.localRotation;
        var left = new GameObject("LeftEye").transform;
        left.SetParent(head, false);
        left.localRotation = Quaternion.Euler(3, 65, 9);
        Quaternion leftRest = left.localRotation;
        var right = new GameObject("RightEye").transform;
        right.SetParent(head, false);
        right.localRotation = Quaternion.Euler(-3, -65, 9);
        Quaternion rightRest = right.localRotation;
        var pose = root.AddComponent<HoVtsLookAt>();
        pose.AutoRun = false;
        pose.ParameterReceiver = receiver;
        pose.ReferenceFrame = root.transform;
        pose.Head = head; pose.LeftEye = left; pose.RightEye = right;
        var raw = new Dictionary<string, float> {
            { "Rotation_x", 10 }, { "Rotation_y", -25 }, { "Rotation_z", 8 },
            { "EyeLeft_x", -5 }, { "EyeLeft_y", 12 }, { "EyeLeft_z", 0 },
            { "EyeRight_x", -4 }, { "EyeRight_y", 11 }, { "EyeRight_z", 1 } };
        receiver.ApplyRawFrame(raw, true, 1);
        Check(pose.EvaluateNow(0), "pose works with no Animator or Avatar: " + pose.Status);
        Quaternion frame = root.transform.rotation;
        Quaternion headOffset = PoseRotation(10, -25, 8, false);
        Quaternion leftOffset = PoseRotation(-5, 12, 0, true);
        Quaternion rightOffset = PoseRotation(-4, 11, 1, true);
        RotationNear(head.rotation, frame * headOffset * headRest, "head uses reference axes");
        RotationNear(left.rotation, frame * leftOffset * Quaternion.Inverse(frame) * head.rotation * leftRest, "left eye uses reference axes on rotated bone");
        RotationNear(right.rotation, frame * rightOffset * Quaternion.Inverse(frame) * head.rotation * rightRest, "right eye uses independent pose");
        Quaternion first = head.localRotation;
        for (int i = 0; i < 500; i++) pose.EvaluateNow(1f / 60);
        RotationNear(head.localRotation, first, "pose offsets do not accumulate");
        root.transform.rotation = Quaternion.Euler(-10, 25, 15);
        pose.EvaluateNow(0);
        RotationNear(head.rotation, root.transform.rotation * headOffset * headRest, "moving reference frame remains consistent");

        Quaternion animatedBase = Quaternion.Euler(-7, 4, 13);
        head.localRotation = animatedBase;
        pose.EvaluateNow(0);
        RotationNear(head.rotation, root.transform.rotation * headOffset * animatedBase, "upstream pose becomes the current baseline");
        pose.HeadEnabled = false;
        pose.EvaluateNow(0);
        RotationNear(head.localRotation, animatedBase, "head can be released for official pose chain");
        Check(left.localRotation != leftRest, "eyes remain independently driven");
        pose.HeadEnabled = true;
        pose.EvaluateNow(0);
        receiver.ApplyRawFrame(raw, false, 2);
        Check(pose.EvaluateNow(0), "lost tracking returns pose");
        RotationNear(head.localRotation, animatedBase, "lost tracking head neutral");
        RotationNear(left.localRotation, leftRest, "lost tracking eye neutral");

        pose.SmoothingSeconds = .2f;
        receiver.ApplyRawFrame(raw, true, 3);
        pose.EvaluateNow(.2f);
        Quaternion smoothOffset = Quaternion.Slerp(Quaternion.identity, headOffset, 1 - Mathf.Exp(-1));
        RotationNear(head.rotation, root.transform.rotation * smoothOffset * animatedBase, "pose smoothing uses elapsed time");
        pose.SmoothingSeconds = 0;
        pose.EvaluateNow(0);
        Quaternion held = head.localRotation;
        receiver.ApplyRawFrame(new Dictionary<string, float> { { "Rotation_x", 89 } }, true, 4);
        pose.EvaluateNow(0);
        RotationNear(head.localRotation, held, "partial rotation packet holds last complete rotation");
        pose.ParameterSource = HoVtsPoseParameterSource.Processed;
        receiver.SetProcessedValues(new Dictionary<string, float> { { "Rotation_x", 0 }, { "Rotation_y", 15 }, { "Rotation_z", 0 } });
        Check(pose.EvaluateNow(0), "processed pose is an explicit optional source");
        RotationNear(head.rotation, root.transform.rotation * PoseRotation(0, 15, 0, false) * animatedBase, "processed source does not secretly read raw pose");

        Quaternion external = Quaternion.Euler(20, 10, 30);
        head.localRotation = external;
        pose.enabled = false;
        RotationNear(head.localRotation, external, "disable does not undo a newer external bone writer");
        RotationNear(left.localRotation, leftRest, "disable restores owned eye pose");
        pose.enabled = true;
        pose.EvaluateNow(0);
        receiver.ClearFrame();
        Check(!pose.EvaluateNow(0), "input release stops pose constraint");
        RotationNear(head.localRotation, external, "input release restores pose baseline");
        receiver.ApplyRawFrame(raw, true, 5);
        pose.ParameterSource = HoVtsPoseParameterSource.CalibratedRaw;
        pose.ReferenceFrame = head;
        Check(!pose.EvaluateNow(0), "driven bone cannot also be its reference frame");
        UnityEngine.Object.Destroy(root);
    }

    private static void RotationNear(Quaternion actual, Quaternion expected, string name) =>
        Check(Quaternion.Angle(actual, expected) < .1f, name + ": angular error=" + Quaternion.Angle(actual, expected));

    private static Quaternion PoseRotation(float x, float y, float z, bool eye)
    {
        float yaw = eye ? x : y;
        float pitch = eye ? -y : x;
        return Quaternion.AngleAxis(yaw, Vector3.up)
            * Quaternion.AngleAxis(pitch, Vector3.right)
            * Quaternion.AngleAxis(z, Vector3.forward);
    }

    private static void Delivery()
    {
        var character = new GameObject("DeliveryCharacter");
        var host = new GameObject("VTS");
        host.transform.SetParent(character.transform, false);
        var receiver = host.AddComponent<HoVtsParameterReceiver>();
        var rules = new HoFaceMiddleware();
        rules.outputs.Add(new HoFaceOutput { parameter = "Drive", expression = "JawOpen" });
        rules.outputs.Add(new HoFaceOutput { parameter = "Pose", expression = "RotationPitch" });
        rules.outputs.Add(new HoFaceOutput { parameter = "One", expression = "", defaultValue = 1 });
        receiver.SetMiddleware(rules);
        var delivery = new HoVtsFrameDelivery();
        var raw = new Dictionary<string, float> { { "JawOpen", .6f }, { "RotationPitch", -45 }, { "FaceFound", 1 }, { "Custom", 123 }, { "Invalid", float.NaN } };
        Check(delivery.Deliver(character, raw, true, 1, "test"), "cross-assembly message delivery: " + delivery.Status);
        Check(receiver.Tracked && receiver.Source == "test", "tracking and source delivered");
        Near(receiver.Get("Drive"), .6f, "delivery invokes character-owned rules");
        Near(receiver.Get("Pose"), -45, "signed pose has no implicit clamp");
        Near(receiver.GetRaw("Custom"), 123, "unknown VTS field retained");
        Near(receiver.GetRaw("Invalid"), 0, "nonfinite raw input sanitized");
        raw["JawOpen"] = .9f;
        Near(receiver.GetRaw("JawOpen"), .6f, "input dictionary copied before sender reuses it");

        Check(delivery.Deliver(character, raw, false, 2, "test"), "stale input delivered as untracked");
        Check(!receiver.Tracked, "freshness participates in tracking");
        Near(receiver.Get("Drive"), 0, "lost tracking releases stale expression");
        Near(receiver.Get("One"), 1, "lost tracking preserves author constants");
        raw["FaceFound"] = 0;
        delivery.Deliver(character, raw, true, 3, "test");
        Check(!receiver.Tracked, "FaceFound zero overrides fresh network input");
        raw.Remove("FaceFound");
        delivery.Deliver(character, raw, true, 4, "test");
        Check(!receiver.Tracked, "dictionary nonempty is insufficient for face tracking");
        raw["FaceFound"] = 1;
        delivery.Deliver(character, raw, true, 5, "test");
        receiver.ApplyRawFrame(raw, true, 6, "another-source");
        delivery.Release();
        Check(receiver.Source == "another-source", "old sender release does not clear another source");
        delivery.Deliver(character, raw, true, 7, "test");
        delivery.Release();
        Check(!receiver.Tracked && receiver.RawCount == 0 && receiver.ValueCount == 0, "sender release clears its frame");

        var duplicate = new GameObject("Duplicate");
        duplicate.transform.SetParent(character.transform, false);
        duplicate.AddComponent<HoVtsParameterReceiver>();
        Check(!delivery.Deliver(character, raw, true, 8, "test"), "ambiguous receivers rejected");
        Check(receiver.RawCount == 0, "ambiguous discovery delivers no partial frame");
        duplicate.SetActive(false);
        Check(delivery.Deliver(character, raw, true, 9, "test"), "inactive receiver excluded");
        receiver.enabled = false;
        Check(!delivery.Deliver(character, raw, true, 10, "test"), "disabled receiver excluded");
        receiver.enabled = true;
        Check(!delivery.Deliver(null, raw, true, 11, "test"), "missing character rejected");
        Check(character.GetComponentsInChildren<HoVtsParameterReceiver>(true).Length == 2, "delivery does not add components");
        delivery.Release();
        UnityEngine.Object.Destroy(character);
    }

    private static AnimationClip Clip(string name, float shape, float position, float scale)
    {
        var clip = new AnimationClip { name = name };
        Curve(clip, "Face", typeof(SkinnedMeshRenderer), "blendShape.Driven", shape);
        Curve(clip, "Bone", typeof(Transform), "m_LocalPosition.x", position);
        Curve(clip, "Bone", typeof(Transform), "m_LocalScale.y", scale);
        Quaternion rotation = Quaternion.Euler(0, 0, shape * .5f);
        Curve(clip, "Rotation", typeof(Transform), "m_LocalRotation.x", rotation.x);
        Curve(clip, "Rotation", typeof(Transform), "m_LocalRotation.y", rotation.y);
        Curve(clip, "Rotation", typeof(Transform), "m_LocalRotation.z", rotation.z);
        Curve(clip, "Rotation", typeof(Transform), "m_LocalRotation.w", rotation.w);
        return clip;
    }
    private static AnimationClip ScaleClip(string name, float value)
    {
        var clip = new AnimationClip { name = name };
        Curve(clip, "Gate", typeof(Transform), "m_LocalScale.z", value);
        return clip;
    }
    private static void Curve(AnimationClip clip, string path, Type type, string property, float value) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), AnimationCurve.Constant(0, 1, value));
    private static void Near(float actual, float expected, string name) => Check(Mathf.Abs(actual - expected) < .01f, name + ": " + actual + " vs " + expected);
    private static void Check(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception(name);
        Debug.Log("HO_VTS_CONTROLLER ok " + name);
    }
}
