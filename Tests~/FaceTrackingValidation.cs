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
                HoFaceInputHub.Stop(rig);

                // ── 判别性实验：单个 Direct 混合树能不能替代"一层一个键" ──────────────
                // 上一轮选"每个形态键一个独立 Override 图层"，理由是"Direct 树会归一化、
                // 各通道互相削弱"。这条理由决定了 52 个图层的存在是否必要，所以这里直接测：
                // 同一个 Direct 树里 jawOpen=0.6 与 mouthSmileLeft=0.8 同时给，
                // 如果两者都拿到 60/80，说明 Direct 不归一化，那 52 层就是纯多余的。
                var direct = BuildDirectController(rig.targetAnimator, AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationDirect.controller"));
                DumpDirectController(direct);
                rig.faceController = direct;
                foreach (var c in rig.channels) c.mode = HoFaceInputMode.Manual;
                Channel("jawOpen").manual = 0.6f;
                Channel("mouthSmileLeft").manual = 0.0f;
                Channel("mouthClose").manual = 0.0f;
                Channel("eyeLookInLeft").manual = 0.0f;
                HoFaceInputHub.Start(rig);
                Check(HoFaceInputHub.Session(rig) != null, "direct-tree session starts: " + HoFaceInputHub.Error(rig));
                stage++; frame = Time.frameCount + 5; return;
            }
            if (stage == 6)
            {
                // 只驱动 jawOpen（0.6），先看单路口径下是 60 还是失控。
                Debug.Log("HO_TRACE1 f=" + Time.frameCount + " jawOpen=" + Weight("jawOpen"));
                Channel("jawOpen").manual = 0.0f;
                Channel("mouthSmileLeft").manual = 0.8f;
                stage++; frame = Time.frameCount + 5; return;
            }
            if (stage == 7)
            {
                Debug.Log("HO_TRACE2 f=" + Time.frameCount + " jawOpen=" + Weight("jawOpen") + " mouthSmileLeft=" + Weight("mouthSmileLeft"));
                Channel("jawOpen").manual = 0.6f;
                stage++; frame = Time.frameCount + 5; return;
            }
            if (stage == 8)
            {
                float jaw = Weight("jawOpen");
                float smile = Weight("mouthSmileLeft");
                Debug.Log("HO_TRACE3 f=" + Time.frameCount + " jawOpen=" + jaw + " mouthSmileLeft=" + smile);
                Debug.Log("HO_DIRECT_WDOFF: jawOpen=" + jaw + " mouthSmileLeft=" + smile
                    + " —— 发散，不是归一化削弱；Direct 树在写默认值关闭时不成立");
                // 这一组只记录不判定：它是"WD Off + Direct"的反面教材。
                HoFaceInputHub.Stop(rig);

                // ── 决定性实验：同一个 Direct 树，只改 Write Defaults ────────────────
                // Jerry 的控制器**所有状态都是 WD=1**，而我们的管线全建在 WD Off 上。
                // 这里绕开我们自己的会话，直接拿一个干净 Animator 驱动，把两种 WD 摆一起比。
                probeRoot = new GameObject("DirectProbe");
                probeAnimator = probeRoot.AddComponent<Animator>();
                probeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(probeRoot.transform, false);
                probeRenderer = bodyGo.AddComponent<SkinnedMeshRenderer>();
                probeRenderer.sharedMesh = renderer.sharedMesh;
                probeWdOn = BuildDirectController(probeAnimator, AssetDatabase.GenerateUniqueAssetPath("Assets/ProbeOn.controller"), true);
                probeWdOff = BuildDirectController(probeAnimator, AssetDatabase.GenerateUniqueAssetPath("Assets/ProbeOff.controller"), false);
                probeAnimator.runtimeAnimatorController = probeWdOn;
                probeAnimator.SetFloat("ARKit/jawOpen", 0.6f);
                probeAnimator.SetFloat("ARKit/mouthSmileLeft", 0.8f);
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 9)
            {
                float jaw = ProbeWeight("jawOpen");
                float smile = ProbeWeight("mouthSmileLeft");
                Debug.Log("HO_WDON : jawOpen=" + jaw + " mouthSmileLeft=" + smile + "  (期望 60 / 80)");
                Check(Mathf.Abs(jaw - 60f) < 0.5f, "Direct + Write Defaults ON: jawOpen = 参数×100 (actual=" + jaw + ")");
                Check(Mathf.Abs(smile - 80f) < 0.5f, "Direct + Write Defaults ON: smile = 参数×100 (actual=" + smile + ")");
                probeAnimator.runtimeAnimatorController = probeWdOff;
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 10)
            {
                float jaw = ProbeWeight("jawOpen");
                float smile = ProbeWeight("mouthSmileLeft");
                Debug.Log("HO_WDOFF: jawOpen=" + jaw + " mouthSmileLeft=" + smile
                    + "  —— 同样一个 Direct 树，只是 WD Off；如果这里发散，说明"
                    + "「Direct 树 + WD Off」才是不可用的组合，而不是 Direct 本身有问题");
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

    private static Animator probeAnimator;
    private static GameObject probeRoot;
    private static SkinnedMeshRenderer probeRenderer;
    private static AnimatorController probeWdOn, probeWdOff;

    private static float ProbeWeight(string shape) =>
        probeRenderer.GetBlendShapeWeight(probeRenderer.sharedMesh.GetBlendShapeIndex(shape));

    private static void DumpDirectController(AnimatorController controller)
    {
        var layers = controller.layers;
        var info = new StringBuilder("HO_DIRECTCTRL layers=" + layers.Length);
        for (int i = 0; i < layers.Length; i++)
        {
            var sm = layers[i].stateMachine;
            string states = "";
            foreach (var cs in sm.states) states += cs.state.name + ",";
            string def = sm.defaultState != null ? sm.defaultState.name : "(null)";
            info.Append(" | L").Append(i).Append(" states=[").Append(states).Append("] default=").Append(def);
            if (sm.defaultState != null && sm.defaultState.motion is BlendTree bt)
            {
                info.Append(" tree=").Append(bt.blendType).Append(" children=").Append(bt.children.Length);
                var kids = bt.children;
                for (int k = 0; k < Mathf.Min(3, kids.Length); k++)
                    info.Append(" [").Append(kids[k].motion != null ? kids[k].motion.name : "?").Append("=>").Append(kids[k].directBlendParameter).Append("]");
            }
        }
        Debug.Log(info.ToString());
    }

    /// <summary>
    /// 对照组：整份控制器只有 **一个图层 + 一棵 Direct 混合树**，每个 ARKit 键一个子片段，
    /// 该键的参数直接当子权重。这是 VRChat 面捕的通行做法，用来判定"一层一个键"是否必要。
    /// </summary>
    private static AnimatorController BuildDirectController(Animator animator, string assetPath, bool writeDefaults = false)
    {
        var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<EditorCurveBinding>>(StringComparer.Ordinal);
        foreach (var mesh in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (mesh.sharedMesh == null || mesh.GetComponentInParent<Animator>() != animator) continue;
            foreach (string shape in HoFaceTrackingChannels.Names)
            {
                if (mesh.sharedMesh.GetBlendShapeIndex(shape) < 0) continue;
                if (!groups.TryGetValue(shape, out var list)) groups[shape] = list = new System.Collections.Generic.List<EditorCurveBinding>();
                list.Add(EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(mesh.transform, animator.transform), typeof(SkinnedMeshRenderer), "blendShape." + shape));
            }
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
        var tree = new BlendTree { name = "ARKit Direct", blendType = BlendTreeType.Direct };
        AssetDatabase.AddObjectToAsset(tree, controller);
        foreach (var pair in groups)
        {
            string parameter = "ARKit/" + pair.Key;
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            var clip = new AnimationClip { name = pair.Key + "_100", frameRate = 60f };
            foreach (var binding in pair.Value)
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, 100f));
            AssetDatabase.AddObjectToAsset(clip, controller);
            tree.AddChild(clip);
            var children = tree.children;
            children[children.Length - 1].directBlendParameter = parameter;
            tree.children = children;
        }

        var state = controller.layers[0].stateMachine.AddState("ARKit Direct");
        state.writeDefaultValues = writeDefaults;
        state.motion = tree;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
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
