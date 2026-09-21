// Copy to Assets/Editor of a disposable validation project and invoke HoFaceTrackingValidation.RunBatch.
// The marker file .ho-face-validation in that project's root is required before touching its scene.
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Editor.FaceTracking;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HoFaceTrackingValidation
{
    private const string PhaseKey = "Ho.Face.Validation.Phase";
    private static int stage;
    private static int frame;
    private static double deadline;
    private static HoFaceTrackingDebugger rig;
    private static SkinnedMeshRenderer renderer;
    private static HoShapeKeyWriter writer;
    private static int targetId;
    private static UdpClient sender;

    static HoFaceTrackingValidation()
    {
        if (SessionState.GetBool(PhaseKey, false))
        {
            deadline = EditorApplication.timeSinceStartup + 60;
            EditorApplication.update += PlayTests;
        }
    }

    public static void RunBatch()
    {
        try
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../.ho-face-validation"))) throw new Exception("Disposable project marker missing.");
            ParserTests();
            ReceiverTests();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HoFaceValidation");
            var animator = root.AddComponent<Animator>();
            var face = new GameObject("Body");
            face.transform.SetParent(root.transform, false);
            renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "ARKit52" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            foreach (string shape in HoFaceTrackingChannels.Names)
                mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.forward * 0.1f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationMesh.asset"));
            renderer.sharedMesh = mesh;
            var controller = HoFaceAnimationAssets.Generate(animator, AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationFace.controller"));
            Check(controller.parameters.Length == 52, "generator discovers all 52 shapes");
            rig = root.AddComponent<HoFaceTrackingDebugger>();
            rig.targetAnimator = animator;
            rig.faceController = controller;
            foreach (var c in rig.channels) c.mode = HoFaceInputMode.Manual;
            Channel("jawOpen").manual = 0.6f;
            Channel("mouthClose").manual = 0.25f;
            Channel("mouthSmileLeft").manual = 0.8f;
            Channel("eyeBlinkLeft").manual = 0.4f;
            Channel("eyeLookInLeft").manual = 1;
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
                Check(compiled.bindings.Count == 44, "default output filter excludes eight gaze shapes");
            var alternate = new GameObject("Meshes");
            alternate.transform.SetParent(root.transform, false);
            var target = new GameObject("Face");
            target.transform.SetParent(alternate.transform, false);
            var alternateMesh = target.AddComponent<SkinnedMeshRenderer>();
            alternateMesh.sharedMesh = mesh;
            rig.pathRemaps.Add(new HoFacePathRemap { sourcePath = "Body", target = alternateMesh });
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
                Check(compiled.bindings.TrueForAll(b => b.path == "Meshes/Face" && b.renderer == alternateMesh), "nested path remap resolves actual renderer");
            rig.pathRemaps.Clear();
            UnityEngine.Object.DestroyImmediate(alternate);
            var state = controller.layers[0].stateMachine.states[0].state;
            state.writeDefaultValues = true;
            bool rejected = false;
            try { using (var compiled = HoFaceAnimationAssets.Compile(rig)) { } } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "reject Write Defaults On instead of silently changing semantics");
            state.writeDefaultValues = false;
            var body = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationBody.controller"));
            var bodyState = body.layers[0].stateMachine.AddState("Idle");
            bodyState.writeDefaultValues = false;
            var clip = new AnimationClip { name = "BodyBaseline" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouthSmileLeft"), AnimationCurve.Constant(0, 1, 15));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.jawOpen"), AnimationCurve.Constant(0, 1, 17));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eyeLookInLeft"), AnimationCurve.Constant(0, 1, 33));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, 2));
            AssetDatabase.AddObjectToAsset(clip, body);
            bodyState.motion = clip;
            animator.runtimeAnimatorController = body;
            // 这个场景没有相机；默认剔除模式会让基础动画在不可见时停止写值，
            // 那样就分不清"基础动画的值被面捕覆盖了"和"基础动画压根没跑"。
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(root.scene, "Assets/Validation.unity");
            SessionState.SetBool(PhaseKey, true);
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { Fail(e); }
    }

    private static HoFaceChannel Channel(string name) => rig.channels.Find(c => c.shape == name);
    private static float Weight(string name) => renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(name));

    private static void PlayTests()
    {
        if (!Application.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Play tests timed out.");
            if (Time.frameCount < frame) return;
            if (stage == 0)
            {
                rig = UnityEngine.Object.FindObjectOfType<HoFaceTrackingDebugger>();
                if (rig == null || Time.frameCount < 4) return;
                renderer = rig.targetAnimator.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                // 记录接管前的 Animator 状态，作为 hasBoundPlayables 语义的实测证据。
                Debug.Log("HO_BEFORE: hasBoundPlayables=" + rig.targetAnimator.hasBoundPlayables
                    + " controller=" + (rig.targetAnimator.runtimeAnimatorController != null));
                HoFaceInputHub.Start(rig);
                Check(HoFaceInputHub.Session(rig) != null, "start session: " + HoFaceInputHub.Error(rig));
                stage++; frame = Time.frameCount + 6; return;
            }
            if (stage == 1)
            {
                Near(Weight("jawOpen"), 60, "jaw blend interpolation");
                Near(Weight("mouthSmileLeft"), 80, "simultaneous smile is not attenuated by jaw");
                Near(Weight("mouthClose"), 25, "mouthClose independent of jawOpen");
                Near(Weight("eyeBlinkLeft"), 40, "eyelid interpolation");
                Near(Weight("eyeLookInLeft"), 33, "gaze exclusion preserves body output");
                Near(rig.targetAnimator.transform.localPosition.x, 2, "body transform animation preserved");
                writer = new HoShapeKeyWriter();
                writer.BeginBuild(new System.Collections.Generic.List<Renderer> { renderer });
                targetId = writer.RegisterTarget(HoShapeKeyTarget.CreateRuntime("jawOpen", 1));
                writer.EndBuild();
                writer.Snapshot(); writer.Apply(targetId, 1, 0.016f); writer.Write();
                Near(Weight("jawOpen"), 60, "Ho writer cannot overwrite face-owned property");
                writer.RestoreWritten();
                Near(Weight("jawOpen"), 60, "Ho writer cleanup cannot overwrite face-owned property");
                Channel("mouthSmileLeft").mode = HoFaceInputMode.Release;
                stage++; frame = Time.frameCount + 6; return;
            }
            if (stage == 2)
            {
                Near(Weight("mouthSmileLeft"), 15, "release returns property to base controller");
                Near(Weight("jawOpen"), 60, "rebuild preserves unrelated channel");
                Check(!HoFaceOutputOwnership.IsReserved(renderer, renderer.sharedMesh.GetBlendShapeIndex("mouthSmileLeft")), "release removes reservation");
                Channel("jawOpen").mode = HoFaceInputMode.Live;
                rig.staleSeconds = 0.3f; rig.neutralFadeSeconds = 0.1f;
                HoFaceInputHub.Connect("127.0.0.2");
                sender = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                Send("jawOpen-90|eyeBlink_L-10|");
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 3)
            {
                Send("jawOpen-90|eyeBlink_L-10|");
                if (Mathf.Abs(Weight("jawOpen") - 90) > 0.2f) return;
                Near(Weight("jawOpen"), 90, "local UDP packet drives actual blend tree");
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 4)
            {
                if (IFacialMocapReceiver.Now - HoFaceInputHub.ReceivedAt[HoFaceTrackingChannels.IndexOf("jawOpen")] < 0.6) return;
                Near(Weight("jawOpen"), 17, "stale stream releases to base animation");
                HoFaceInputHub.Stop(rig);
                Check(rig.targetAnimator.runtimeAnimatorController != null, "stop restores original controller");
                // 这条同时是"为什么不能用 hasBoundPlayables 判所有权"的实测证据：
                // 恢复成普通 Animator + Controller 之后它又是 true。
                Check(rig.targetAnimator.hasBoundPlayables, "plain animator with a controller reports hasBoundPlayables=true");
                HoFaceInputHub.Disconnect();
                sender.Close(); sender = null;
                stage++; frame = Time.frameCount + 5; return;
            }
            if (stage == 5)
            {
                Near(Weight("jawOpen"), 17, "base continues after stop");
                Check(!HoFaceOutputOwnership.IsReserved(renderer, renderer.sharedMesh.GetBlendShapeIndex("jawOpen")), "stop removes all reservations");
                HoFaceInputHub.Start(rig);
                Check(HoFaceInputHub.Session(rig) != null, "session can restart");
                rig.enabled = false;
                Check(HoFaceInputHub.Session(rig) == null, "disable component disposes session immediately");
                Debug.Log("HO_FACE_TESTS_ALL_PASSED");
                SessionState.SetBool(PhaseKey, false);
                EditorApplication.update -= PlayTests;
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    private static void Send(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, IFacialMocapReceiver.Port));
    }

    private static void ParserTests()
    {
        Check(HoFaceTrackingChannels.Names.Length == 52, "ARKit channel count");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Check(IFacialMocapPacket.TryParse("jawOpen-60.5|eyeBlink_L-30|eyeBlink_R&42|=head#-20,5,-1,0.1,0.2,0.3|leftEye#1,-2,3|", out var p), "v1/v2 packet parsing");
            Near(p.Values[HoFaceTrackingChannels.IndexOf("jawOpen")], 0.605f, "invariant culture normalization", 0.0001f);
            Near(p.Values[HoFaceTrackingChannels.IndexOf("eyeBlinkLeft")], 0.3f, "wire left alias", 0.0001f);
            Near(p.Head[0], -20, "negative head angle");
            Check(!p.Present[HoFaceTrackingChannels.IndexOf("mouthClose")], "missing channel is not synthesized as zero");
            Check(IFacialMocapPacket.TryParse("jawOpen-NaN|mouthClose-Infinity|eyeBlink_L-20|junk-3|", out p) && p.InvalidCount == 2 && p.UnknownCount == 1 && p.ShapeCount == 1, "invalid fields do not invalidate good channels");
            Check(!IFacialMocapPacket.TryParse("=head#0,0,0,0,0,0|", out p), "pose-only packet does not refresh facial health");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static void ReceiverTests()
    {
        using (var receiver = new IFacialMocapReceiver())
        using (var device = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0)))
        {
            receiver.Start("127.0.0.2");
            byte[] data = Encoding.UTF8.GetBytes("jawOpen-37|");
            device.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, IFacialMocapReceiver.Port));
            double until = IFacialMocapReceiver.Now + 2;
            IFacialMocapPacket p = null;
            while (IFacialMocapReceiver.Now < until && p == null) { receiver.TryTake(out p, out _); Thread.Sleep(5); }
            Check(p != null, "real UDP receiver accepts configured sender");
            Near(p.Values[HoFaceTrackingChannels.IndexOf("jawOpen")], 0.37f, "UDP payload survives receiver", 0.0001f);
            bool busy = false;
            using (var second = new IFacialMocapReceiver())
                try { second.Start("127.0.0.2"); } catch (SocketException) { busy = true; }
            Check(busy, "second socket fails explicitly on occupied port");
            receiver.Dispose(); receiver.Start("127.0.0.2");
            Check(receiver.Running, "socket can reopen after disposal");
        }
    }

    private static void Near(float actual, float expected, string name, float tolerance = 0.15f) => Check(Mathf.Abs(actual - expected) < tolerance, name + " actual=" + actual + " expected=" + expected);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("HO_FACE_TEST_FAILED: " + name);
        Debug.Log("HO_FACE_TEST_PASS: " + name);
    }
    private static void Fail(Exception e)
    {
        Debug.LogException(e);
        SessionState.SetBool(PhaseKey, false);
        sender?.Close();
        HoFaceInputHub.Disconnect();
        EditorApplication.Exit(1);
    }
}
