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
            // 再挂一个**非 ARKit** 的"自家键"：果冻这类键的代表。它必须能穿过 Compile，
            // 否则影子台上算出来的姿势永远抄不回真模型。
            mesh.AddBlendShapeFrame("JellyEye", 100, new[] { Vector3.forward * 0.1f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationMesh.asset"));
            renderer.sharedMesh = mesh;
            var controller = HoFaceAnimationAssets.Generate(animator, AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationFace.controller"));
            int arkitParameters = 0;
            foreach (var p in controller.parameters)
                if (p.name.StartsWith("ARKit/", StringComparison.Ordinal)) arkitParameters++;
            Check(arkitParameters == 52, "generator discovers all 52 shapes (" + arkitParameters + ")");
            Check(controller.parameters.Length == 54, "generator adds exactly two extra parameters, the two jelly axes ("
                + controller.parameters.Length + ")");
            rig = root.AddComponent<HoFaceTrackingDebugger>();
            rig.targetAnimator = animator;
            rig.faceController = controller;
            // 默认是 All（凝视也开 —— LookAt 不是一定存在）。这里**刻意关掉凝视**，
            // 用来验证"排除凝视"这条路径本身，不能再赖默认值。
            Check(rig.outputRegions == HoFaceRegion.All, "gaze defaults to ON (LookAt may not exist)");
            rig.outputRegions = HoFaceRegion.Expression;
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
            // ── 生成器结构：驱动段（一棵 Direct 树）+ 扩展点，WD 开 ──────────────
            var layers = controller.layers;
            var directState = FindState(controller, HoFaceAnimationAssets.DriveLayerName);
            var directTree = directState != null ? directState.motion as BlendTree : null;
            var editState = FindState(controller, HoFaceAnimationAssets.EditLayerName);
            Check(layers.Length == 2, "generator emits drive layer + EDIT THIS extension point");
            Check(directState != null, "drive layer is named " + HoFaceAnimationAssets.DriveLayerName);
            Check(editState != null, "extension point is named " + HoFaceAnimationAssets.EditLayerName);
            Check(directTree != null && directTree.blendType == BlendTreeType.Direct, "drive layer is one Direct blend tree");
            Check(directTree != null && directTree.children.Length == 52, "Direct tree has one child per shape");
            Check(directState != null && directState.writeDefaultValues, "Direct tree uses Write Defaults On");
            bool ownParameters = true;
            if (directTree != null)
                foreach (var child in directTree.children)
                    if (!child.directBlendParameter.StartsWith("ARKit/", StringComparison.Ordinal)) ownParameters = false;
            Check(ownParameters, "every Direct child is weighted by its own ARKit parameter");
            Check(HoFaceAnimationAssets.IsManagedLayer(HoFaceAnimationAssets.DriveLayerName)
                && !HoFaceAnimationAssets.IsManagedLayer(HoFaceAnimationAssets.EditLayerName),
                "drive layer is managed, extension point is not");
            bool jellyX = false, jellyY = false;
            foreach (var p in controller.parameters)
            {
                if (p.name == HoFaceAnimationAssets.JellyParameterXName) jellyX = true;
                if (p.name == HoFaceAnimationAssets.JellyParameterYName) jellyY = true;
            }
            Check(jellyX && jellyY, "generated controller carries both jelly axes ("
                + HoFaceAnimationAssets.JellyParameterXName + " / " + HoFaceAnimationAssets.JellyParameterYName + ")");

            // ── 应用改动 = 就地手术：重写驱动段，但绝不碰扩展点 ──────────────────
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            int clipsBefore = CountClips(controllerPath);
            int editStateBefore = editState.GetInstanceID();
            var editMotionBefore = editState.motion;
            HoFaceAnimationAssets.Apply(controller, animator);
            var appliedDrive = FindState(controller, HoFaceAnimationAssets.DriveLayerName);
            var appliedEdit = FindState(controller, HoFaceAnimationAssets.EditLayerName);
            Check(controller.layers.Length == 2, "apply keeps the layer count");
            Check(appliedEdit != null && appliedEdit.GetInstanceID() == editStateBefore
                && ReferenceEquals(appliedEdit.motion, editMotionBefore),
                "apply leaves the EDIT THIS extension point untouched");
            Check(appliedDrive != null && ((BlendTree)appliedDrive.motion).children.Length == 52, "apply rebuilds the drive tree");
            Check(CountClips(controllerPath) == clipsBefore, "apply reuses clips instead of piling up sub-assets (" + clipsBefore + ")");

            // 反面用例：同一个 Direct 树把 Write Defaults 关掉必须被拒 —— 实测那个组合会发散
            //（0.6 的输入 → 98.98 → 246.28 → 1059.33），不能靠运气。
            directState.writeDefaultValues = false;
            bool rejected = false;
            try { using (var compiled = HoFaceAnimationAssets.Compile(rig)) { } } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "reject Direct tree with Write Defaults Off instead of letting it diverge");
            directState.writeDefaultValues = true;

            // 回归：覆盖式「初始化」必须真的把旧文件换掉。
            // 曾经是个真 bug —— 面板加了覆盖确认框，但 Generate 里"目标资产已存在就报错"的守卫
            // 忘了删，于是用户点「覆盖并初始化」后文件原封不动（还是旧的 52 层），而报错又和成功
            // 消息共用蓝色 Info 框，被当提示略过去了。
            // 注意：这一步会销毁旧控制器与其中的状态对象，所以必须放在所有引用旧状态的断言**之后**。
            var replaced = HoFaceAnimationAssets.Generate(animator, controllerPath, true);
            Check(replaced != null && replaced.layers.Length == 2,
                "re-initializing over an existing file really replaces it (layers=" + (replaced != null ? replaced.layers.Length : -1) + ")");
            Check(CountClips(controllerPath) == clipsBefore, "re-initialize does not leave the old clips behind (" + clipsBefore + ")");
            rig.faceController = replaced;

            // ── 混合树小工具：物理产参数 → 混合树消费 → 用户自己的键 ──────────────
            // 这棵树是"手搓混合树一定会踩、踩了还看不出来"的三个坑的兜底，所以每条都断言：
            //   ① 原点子节点（否则参数归零时脸上挂着四个方向的加权平均）
            //   ② 每个方向都写全部键（不留"这个方向没人管这个键"的空洞）
            //   ③ 非 ARKit 的键必须穿过 Compile（否则影子台上算完抄不回真模型）
            int jellyIndex = renderer.sharedMesh.GetBlendShapeIndex("JellyEye");
            Check(jellyIndex >= 0, "validation mesh carries a user-owned (non ARKit) key");
            var plan = new HoBlendTreePlan();
            plan.keys.Add(new HoBlendTreeKey { renderer = renderer, index = jellyIndex, shape = "JellyEye" });
            for (int i = 0; i < HoFaceBlendTreeTool.Corners.Length; i++)
            {
                var direction = new HoBlendTreeDirection
                {
                    name = HoFaceBlendTreeTool.CornerNames[i],
                    position = HoFaceBlendTreeTool.Corners[i]
                };
                direction.Fit(1);
                direction.SetWeight(0, 20f * (i + 1));
                plan.directions.Add(direction);
            }

            HoFaceBlendTreeTool.Write(animator, replaced, plan);
            var jellyState = FindState(replaced, plan.layerName);
            var jellyTree = jellyState != null ? jellyState.motion as BlendTree : null;
            Check(jellyTree != null && jellyTree.blendType == BlendTreeType.FreeformCartesian2D,
                "blend tree tool writes a 2D freeform cartesian tree");
            Check(jellyTree != null && jellyTree.blendParameter == plan.parameterX && jellyTree.blendParameterY == plan.parameterY,
                "the tree reads the two physics parameters");
            Check(jellyTree != null && jellyTree.children.Length == 5,
                "four corners plus an auto origin child (" + (jellyTree != null ? jellyTree.children.Length : -1) + ")");
            Check(replaced.parameters.Length == 54, "the tool reuses the jelly parameters instead of duplicating them");
            var jellyBinding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.JellyEye");
            if (jellyTree != null)
            {
                foreach (var child in jellyTree.children)
                {
                    var childClip = child.motion as AnimationClip;
                    var curve = childClip != null ? AnimationUtility.GetEditorCurve(childClip, jellyBinding) : null;
                    int corner = Array.IndexOf(HoFaceBlendTreeTool.Corners, child.position);
                    float expected = child.position == Vector2.zero ? 0f : 20f * (corner + 1);
                    Check(curve != null && Mathf.Abs(curve.Evaluate(0f) - expected) < 0.001f,
                        "every direction writes every key (" + child.position + " => " + expected + ")");
                }
            }

            int jellyClips = CountClips(controllerPath);
            HoFaceBlendTreeTool.Write(animator, replaced, plan);
            var jellyAgain = FindState(replaced, plan.layerName);
            var againTree = jellyAgain != null ? jellyAgain.motion as BlendTree : null;
            Check(againTree != null && againTree.children.Length == 5 && CountClips(controllerPath) == jellyClips,
                "re-running the tool is idempotent (" + jellyClips + " clips, no orphans)");

            bool hasJellyKey = false;
            int outputBindings = 0;
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
            {
                outputBindings = compiled.bindings.Count;
                foreach (var b in compiled.bindings) if (b.shape == "JellyEye") hasJellyKey = true;
            }

            Check(hasJellyKey, "user-owned keys survive Compile (the shadow result must reach the real mesh)");
            Check(outputBindings == 45, "the new pass-through adds exactly one key to the 44 gated ARKit ones ("
                + outputBindings + ")");

            // ── 混合树基础件（19 节定案：生成器只出"最基本设施"）────────────────────
            // 一：两个 0~1 的反向通道合成一根 -1~1 的单轴。混合树自己算不出新参数，
            // 所以"值"和"树"必须成对交付，这里两半都验。
            Near(HoFaceAxis.Merge(0.8f, 0.3f), 0.5f, "axis merge is positive minus negative", 0.0001f);
            Near(HoFaceAxis.Merge(0.2f, 0.9f), -0.7f, "axis merge goes negative when the other side wins", 0.0001f);
            Near(HoFaceAxis.Merge(float.NaN, 0.4f), -0.4f, "axis merge swallows NaN instead of poisoning the axis", 0.0001f);
            HoFaceAxis.Split(-0.7f, out float axisPositive, out float axisNegative);
            Check(Mathf.Abs(axisPositive) < 0.0001f && Mathf.Abs(axisNegative - 0.7f) < 0.0001f,
                "the axis splits back into two unsigned halves");

            // 二：N×M 的 2D 树 + 三姿势的双向 1D 树。
            var kitKeys = new[]
            {
                new HoPoseKey(renderer, "eyeBlinkLeft"),
                new HoPoseKey(renderer, "eyeWideLeft")
            };
            var kitGrid = HoFaceBlendTreeKit.Grid2D(replaced, animator, "Ho/BT Test Grid", "Ho/Test/X", "Ho/Test/Y",
                new[] { -1f, 0f, 1f }, new[] { 0f, 1f }, kitKeys, (clip, i, j) =>
                {
                    if (i == 2 && j == 1) HoFaceBlendTreeKit.Pose(clip, animator, renderer, "eyeBlinkLeft", 100f);
                });
            Check(kitGrid.blendType == BlendTreeType.FreeformCartesian2D && kitGrid.children.Length == 6,
                "kit builds an N x M grid (" + kitGrid.children.Length + ")");
            Check(kitGrid.blendParameter == "Ho/Test/X" && kitGrid.blendParameterY == "Ho/Test/Y",
                "the grid reads exactly the two axis parameters");

            var blinkBinding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eyeBlinkLeft");
            var wideBinding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eyeWideLeft");
            int blankCells = 0, posedCells = 0, placed = 0;
            foreach (var child in kitGrid.children)
            {
                var cell = child.motion as AnimationClip;
                var wide = cell != null ? AnimationUtility.GetEditorCurve(cell, wideBinding) : null;
                var blink = cell != null ? AnimationUtility.GetEditorCurve(cell, blinkBinding) : null;
                if (wide != null && Mathf.Abs(wide.Evaluate(0f)) < 0.001f) blankCells++;
                if (blink != null && Mathf.Abs(blink.Evaluate(0f) - 100f) < 0.001f) posedCells++;
                if (child.position == new Vector2(1f, 1f)) placed++;
            }

            Check(blankCells == 6, "every grid cell carries every key as a 0 baseline (" + blankCells + ")");
            Check(posedCells == 1 && placed == 1, "only the posed cell differs, at its grid coordinate");

            var kitAxis = HoFaceBlendTreeKit.Axis1D(replaced, animator, "Ho/BT Test Axis", "Ho/Test/Axis",
                HoFaceBlendTreeKit.ThreePointAxis, kitKeys, null);
            Check(kitAxis.blendType == BlendTreeType.Simple1D && kitAxis.children.Length == 3,
                "kit builds a three-point 1D axis (" + kitAxis.children.Length + ")");
            Check(Mathf.Abs(kitAxis.children[0].threshold + 1f) < 0.0001f
                && Mathf.Abs(kitAxis.children[1].threshold) < 0.0001f
                && Mathf.Abs(kitAxis.children[2].threshold - 1f) < 0.0001f,
                "the three-point axis sits at -1 / 0 / +1");

            int beforeClear = CountClips(controllerPath);
            HoFaceBlendTreeKit.Clear(kitGrid);
            HoFaceBlendTreeKit.Clear(kitAxis);
            Check(CountClips(controllerPath) == beforeClear - 9,
                "clearing a kit tree takes its pose clips with it (removed " + (beforeClear - CountClips(controllerPath)) + ")");

            // ── 弹簧驱动（定案 19 里果冻的落点：独立组件 + 预设 + 读已落下的键）──────────
            // 正向目标吃挤压（含过冲），反向目标吃回弹 —— 纯函数，所以直接断言，
            // 不用跑动画去肉眼看"回弹有没有真的送到别的键上"。
            Near(HoSpringConstraint.Driver(1.44f, false), 1.44f, "the forward side keeps the overshoot", 0.0001f);
            Near(HoSpringConstraint.Driver(-0.4f, false), 0f, "the forward side ignores the rebound half", 0.0001f);
            Near(HoSpringConstraint.Driver(-0.4f, true), 0.4f, "the reversed side takes the rebound half", 0.0001f);
            Near(HoSpringConstraint.Driver(1.44f, true), 0f, "the reversed side ignores the squash half", 0.0001f);

            var jellyKeys = HoSpringPresets.JellyInputKeys(new[] { renderer });
            Check(jellyKeys.Contains("eyeBlinkLeft") && jellyKeys.Contains("eyeBlinkRight"),
                "the jelly preset finds the blink keys that are actually on the mesh (" + string.Join(",", jellyKeys) + ")");
            Check(Mathf.Abs(HoSpringPresets.JellyFrequencyX - 6f) < 0.001f
                && Mathf.Abs(HoSpringPresets.JellyFrequencyY - 8.5f) < 0.001f
                && Mathf.Abs(HoSpringPresets.JellyDamping - 0.25f) < 0.001f,
                "the two jelly freedoms keep different frequencies, so the path is not a straight line");

            // 端到端（编辑期直接喂帧，不进播放模式）：读一个**已经落下来的**键 → 弹簧 → 写另一个键。
            var springProbe = new GameObject("SpringProbe");
            var springMesh = springProbe.AddComponent<SkinnedMeshRenderer>();
            springMesh.sharedMesh = renderer.sharedMesh;
            int blinkIndex = renderer.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft");
            int jellyKey = renderer.sharedMesh.GetBlendShapeIndex("JellyEye");
            springMesh.SetBlendShapeWeight(blinkIndex, 100f);
            var spring = springProbe.AddComponent<HoSpringConstraint>();
            spring.Configure(new System.Collections.Generic.List<SkinnedMeshRenderer> { springMesh }, jellyKeys,
                HoSpringPresets.JellyFrequencyX, HoSpringPresets.JellyDamping, 1f);
            spring.Targets.Add(new HoSpringTarget("JellyEye", 1f));
            spring.Rebuild();
            float springPeak = 0f;
            for (int i = 0; i < 90; i++)
            {
                spring.Step(1f / 60f);
                springPeak = Mathf.Max(springPeak, springMesh.GetBlendShapeWeight(jellyKey));
            }

            Check(springPeak > 60f, "the component reads a landed key, springs it, and writes its own key (peak=" + springPeak.ToString("F1") + ")");
            UnityEngine.Object.DestroyImmediate(springProbe);

            // ── 响应整形（死区）：分组各自生效，且只吃实时输入 ────────────────────
            rig.deadZoneMouth = 0.2f;
            Near(rig.ApplySensitivity("jawOpen", 0.10f), 0f, "dead zone suppresses live input below the threshold", 0.001f);
            Near(rig.ApplySensitivity("jawOpen", 0.60f), 0.5f, "dead zone rescales the rest of the range", 0.001f);
            Near(rig.ApplySensitivity("eyeLookInLeft", 0.60f), 0.60f, "other groups keep their own (zero) dead zone", 0.001f);
            Near(rig.ApplySensitivity("jawOpen", 1.00f), 1f, "dead zone keeps the top of the range at 1", 0.001f);
            // 立刻复位：这几条是纯函数检查，留着会污染后面的实时阶段（raw 0.9 会被整形，等不到 90）。
            rig.deadZoneMouth = 0f;

            // ── 果冻：一维弹簧的纯函数行为（"过冲"就是果冻的定义，所以直接断言它）────────
            var jelly = new HoFaceJellyState();
            float peak = 0f;
            bool settled = false;
            for (int i = 0; i < 600; i++)
            {
                HoFaceJelly.Step(ref jelly, 1f, 1f / 60f, 6f, 0.25f);
                peak = Mathf.Max(peak, jelly.value);
                if (i > 300 && Mathf.Abs(jelly.value - 1f) < 0.01f && Mathf.Abs(jelly.velocity) < 0.05f) settled = true;
            }
            Check(peak > 1.05f, "jelly overshoots a step input (that overshoot *is* the jelly, peak=" + peak.ToString("F3") + ")");
            Check(settled, "jelly settles back to the target (value=" + jelly.value.ToString("F3") + ")");

            var still = new HoFaceJellyState();
            for (int i = 0; i < 120; i++) HoFaceJelly.Step(ref still, 0f, 1f / 60f, 6f, 0.25f);
            Check(Mathf.Abs(still.value) < 1e-4f && Mathf.Abs(still.velocity) < 1e-4f, "jelly stays still when the input does not move");

            // 两个方向必须真的走成两条不同的轨迹 —— 否则"两个方向"就退化成一维直线来回，
            // 灵动的来源（Lissajous）就没了。
            var ax = new HoFaceJellyState();
            var ay = new HoFaceJellyState();
            float maxGap = 0f;
            for (int i = 0; i < 120; i++)
            {
                HoFaceJelly.Step(ref ax, 1f, 1f / 60f, 6f, 0.25f);
                HoFaceJelly.Step(ref ay, 1f, 1f / 60f, 8.5f, 0.25f);
                maxGap = Mathf.Max(maxGap, Mathf.Abs(ax.value - ay.value));
            }
            Check(maxGap > 0.15f, "the two axes diverge (that divergence *is* the Lissajous, maxGap=" + maxGap.ToString("F3") + ")");

            var huge = new HoFaceJellyState();
            HoFaceJelly.Step(ref huge, 1f, 0.5f, 6f, 0.25f);   // 卡了一帧：不能因为 dt 大就发散
            Check(!float.IsNaN(huge.value) && !float.IsInfinity(huge.value) && Mathf.Abs(huge.value) < 10f,
                "jelly survives a huge deltaTime without diverging (value=" + huge.value.ToString("F3") + ")");

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

                // ── 判别性实验：同一个 Direct 树，只改 Write Defaults ────────────────
                // 参考实现（Jerry 的 ARKit 模板）所有状态都是 WD 开，而我们旧的生成器是 WD 关。
                // 这里绕开我们的会话，直接拿一个干净 Animator 驱动，把两种 WD 摆一起比 ——
                // 会话路径下 WD Off 会被 Compile 直接拒掉（上面已断言），所以必须独立测。
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
            if (stage == 6)
            {
                float jaw = ProbeWeight("jawOpen");
                float smile = ProbeWeight("mouthSmileLeft");
                Debug.Log("HO_WDON : jawOpen=" + jaw + " mouthSmileLeft=" + smile + "  (期望 60 / 80)");
                Check(Mathf.Abs(jaw - 60f) < 0.5f, "Direct + Write Defaults ON: jawOpen = 参数×100 (actual=" + jaw + ")");
                Check(Mathf.Abs(smile - 80f) < 0.5f, "Direct + Write Defaults ON: smile = 参数×100 (actual=" + smile + ")");
                probeAnimator.runtimeAnimatorController = probeWdOff;
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 7)
            {
                float jaw = ProbeWeight("jawOpen");
                float smile = ProbeWeight("mouthSmileLeft");
                Debug.Log("HO_WDOFF: jawOpen=" + jaw + " mouthSmileLeft=" + smile
                    + "  —— 同样一个 Direct 树，只是 WD Off；如果这里发散，说明"
                    + "「Direct 树 + WD Off」才是不可用的组合，而不是 Direct 本身有问题");

                // ── 分组平滑：开着的时候真的在过滤，关掉（默认）的时候直通 ──────────
                // 断言刻意做成不依赖帧率：用很大的时间常数，只要求"没一步到位"，再要求它单调逼近。
                HoFaceInputHub.Start(rig);
                var smoothSession = HoFaceInputHub.Session(rig);
                Check(smoothSession != null, "session started for smoothing test: " + HoFaceInputHub.Error(rig));
                rig.smoothMouth = 1.0f;                 // jawOpen 属"嘴"组
                Channel("jawOpen").manual = 1.0f;
                Channel("jawOpen").mode = HoFaceInputMode.Manual;
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 8)
            {
                float transit = Weight("jawOpen");
                smoothSampleA = transit;
                Debug.Log("HO_SMOOTH transit=" + transit + " (目标 100，平滑开着就不该一步到位)");
                Check(transit > 0.01f && transit < 99.0f,
                    "group smoothing filters instead of snapping (actual=" + transit + ")");
                stage++; frame = Time.frameCount + 12; return;
            }
            if (stage == 9)
            {
                float later = Weight("jawOpen");
                Debug.Log("HO_SMOOTH later=" + later + " (应比 transit=" + smoothSampleA + " 更接近 100)");
                Check(later > smoothSampleA, "group smoothing keeps converging (A=" + smoothSampleA + " B=" + later + ")");
                rig.smoothMouth = 0f;                   // 关掉 = 直通，下一帧就该到位
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 10)
            {
                Near(Weight("jawOpen"), 100, "smoothing 0 means pass-through (no extra delay by default)");

                // 死区的端到端验证：走实时输入这条路（manual 滑杆本来就不该被死区吃）。
                rig.deadZoneMouth = 0.2f;   // 纯函数那段已经复位过，这里自己显式设置
                Channel("jawOpen").mode = HoFaceInputMode.Live;
                HoFaceInputHub.Connect("127.0.0.2");
                sender = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                Send("jawOpen-60|");
                stage++; frame = Time.frameCount + 4; return;
            }
            // ── 判别性实验：WD 开时，"默认值"到底是什么 ──────────────────────────
            // 问题：一个被状态里的片段**以权重 0** 写着的属性，Unity 会写成 0，还是会保留当前值？
            // 这不只是"区域开关能不能做成权重"的问题，它决定我们的**回退层**怎么写：
            //   · 保留 33  → 我们可以在影子台上**预置基础表情**，再用部分权重混回它，回退不用手写；
            //   · 写成 0   → 默认值是硬 0，回退必须显式做一棵树
            //                （参考实现的 MouthDefaultCorrection / Face Tracking Limits 就是这个用途）。
            // 关键：33 要在 Animator **挂上控制器之前**摆好 —— 那样它才是"当前值/基础值"。
            // 另设一个对照：JellyEye 这个键根本不在树里，如果它也被动，说明 WD 会碰没被动画的属性。
            if (stage == 11)
            {
                zeroProbeRoot = new GameObject("WdZeroProbe");
                var zeroBody = new GameObject("Body");
                zeroBody.transform.SetParent(zeroProbeRoot.transform, false);
                zeroProbeRenderer = zeroBody.AddComponent<SkinnedMeshRenderer>();
                zeroProbeRenderer.sharedMesh = renderer.sharedMesh;
                zeroProbeRenderer.SetBlendShapeWeight(zeroProbeRenderer.sharedMesh.GetBlendShapeIndex("eyeLookInLeft"), 33f);
                zeroProbeRenderer.SetBlendShapeWeight(zeroProbeRenderer.sharedMesh.GetBlendShapeIndex("JellyEye"), 33f);
                zeroProbeAnimator = zeroProbeRoot.AddComponent<Animator>();
                zeroProbeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                zeroProbeAnimator.runtimeAnimatorController = probeWdOn;   // 此刻 33 已经是当前值
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 12)
            {
                atZeroWeight = ZeroWeight("eyeLookInLeft");
                controlWeight = ZeroWeight("JellyEye");
                Debug.Log("HO_WDZERO: 权重 0 的键=" + atZeroWeight + "（33 = 不管它；0 = 写成默认值）"
                    + "　不在树里的键=" + controlWeight + "（应保持 33）");
                Check(Mathf.Abs(controlWeight - 33f) < 0.5f,
                    "WD On does not touch a property no clip in the state animates (actual=" + controlWeight + ")");
                // 实测：权重 0 的子节点**不碰**那个属性 —— 它不是"把它写成 0"。
                Check(Mathf.Abs(atZeroWeight - 33f) < 0.5f,
                    "a Direct child at weight 0 leaves the property untouched instead of writing 0 (actual=" + atZeroWeight + ")");
                zeroProbeAnimator.SetFloat("ARKit/eyeLookInLeft", 0.6f);
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 13)
            {
                float driven = ZeroWeight("eyeLookInLeft");
                // 关键实测：WD 开的余项 (1-Σw) 混的是**这个属性在 Animator 启动时的值**（这里预置的 33），
                // 不是硬 0 —— 0.6×100 + 0.4×33 = 73.2。之前探针台量到"精确 60"只是因为那边启动值是 0。
                Debug.Log("HO_WDZERO: 参数 0.6 时=" + driven + "（73.2 = 0.6×100 + 0.4×33，说明余项混的是启动值）"
                    + "　结论：zeroWeight=" + atZeroWeight);
                Check(Mathf.Abs(driven - 73.2f) < 0.6f,
                    "WD On blends the (1-sum) remainder against the value captured when the Animator started, "
                    + "not against a hard 0 (actual=" + driven + ")");
                stage++; return;
            }
            if (stage == 14)
            {
                Send("jawOpen-60|");
                if (Mathf.Abs(Weight("jawOpen") - 50f) > 0.6f) return;   // 等它被驱动上来
                Check(true, "dead zone reaches the written parameter through live input (raw 0.6 -> 50)");
                HoFaceInputHub.Stop(rig);
                HoFaceInputHub.Disconnect();
                sender.Close(); sender = null;
                rig.enabled = false;
                Check(HoFaceInputHub.Session(rig) == null, "disable component disposes session immediately");
                Debug.Log("HO_FACE_TESTS_ALL_PASSED" + (receiverSkipped ? "（receiver 段因端口被占用而跳过）" : ""));
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
    private static GameObject zeroProbeRoot;
    private static SkinnedMeshRenderer zeroProbeRenderer;
    private static Animator zeroProbeAnimator;
    private static float atZeroWeight, controlWeight;
    private static float smoothSampleA;
    private static bool receiverSkipped;

    private static float ZeroWeight(string shape) =>
        zeroProbeRenderer.GetBlendShapeWeight(zeroProbeRenderer.sharedMesh.GetBlendShapeIndex(shape));

    private static float ProbeWeight(string shape) =>
        probeRenderer.GetBlendShapeWeight(probeRenderer.sharedMesh.GetBlendShapeIndex(shape));

    private static AnimatorState FindState(AnimatorController controller, string layerName)
    {
        foreach (var layer in controller.layers)
            if (layer.name == layerName && layer.stateMachine != null && layer.stateMachine.states.Length > 0)
                return layer.stateMachine.states[0].state;
        return null;
    }

    private static int CountClips(string controllerPath) =>
        System.Linq.Enumerable.Count(
            System.Linq.Enumerable.OfType<AnimationClip>(AssetDatabase.LoadAllAssetsAtPath(controllerPath)));

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
        {
            try
            {
                receiver.Start("127.0.0.2");
            }
            catch (SocketException)
            {
                // 端口被别的程序占着 —— 实测很常见：Warudo 正连着手机，或本机的面捕面板在跑。
                // iFacialMocap 只往一个 IP:端口发，所以同一台机器上只能有一个监听者。
                // 环境问题不该让整套用例挂掉，但也**不假装通过**：显式跳过，并在最后一行里报出来。
                receiverSkipped = true;
                Debug.Log("HO_FACE_TEST_SKIPPED: receiver tests —— UDP " + IFacialMocapReceiver.Port
                    + " 已被占用（通常是 Warudo 或本机的面捕面板在监听）");
                return;
            }

            using (var device = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0)))
            {
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
