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
using Hollow.HoUnityTools.Editor.AnimationTools;
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
            // ── 装配：初始化 = 把一份现成的混合树文件搬到这台角色上 ────────────────
            // 控制器是**作品**（Jerry 的 vrc-common、我们自己编的 ho-2d-test1…），代码不再生成树。
            // 所以这里先搭一份"作者的控制器"当夹具，再断言装配的结果。
            // 别人的共享片段也先造好：装配时必须**复制成本文件的子资产**再改，绝不能就地改它。
            string externalPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationExternal.anim");
            var external = new AnimationClip { name = "MouthCloseFromElsewhere", frameRate = 60f };
            AnimationUtility.SetEditorCurve(external,
                EditorCurveBinding.FloatCurve("Source/Face", typeof(SkinnedMeshRenderer), "blendShape.mouthClose"),
                AnimationCurve.Constant(0f, 1f / 60f, 100f));
            AssetDatabase.CreateAsset(external, externalPath);

            // 动画文件夹：装配时按**槽位名**顶替模板自带的那一份。
            // 给 jawOpen 那份槽位放一个"能认出来"的版本：jawOpen 仍是 100（不打乱后面的运行时断言），
            // 但**多写一个 mouthDimpleLeft 55** —— 有它才说明用的确实是文件夹这一份。
            const string ClipFolder = "Assets/ValidationClips";
            if (AssetDatabase.IsValidFolder(ClipFolder)) AssetDatabase.DeleteAsset(ClipFolder);
            AssetDatabase.CreateFolder("Assets", "ValidationClips");
            var folderJaw = new AnimationClip { name = "jawOpen_100", frameRate = 60f };
            AnimationUtility.SetEditorCurve(folderJaw,
                EditorCurveBinding.FloatCurve("Elsewhere/Head", typeof(SkinnedMeshRenderer), "blendShape.jawOpen"),
                AnimationCurve.Constant(0f, 1f / 60f, 100f));
            AnimationUtility.SetEditorCurve(folderJaw,
                EditorCurveBinding.FloatCurve("Elsewhere/Head", typeof(SkinnedMeshRenderer), "blendShape.mouthDimpleLeft"),
                AnimationCurve.Constant(0f, 1f / 60f, 55f));
            AssetDatabase.CreateAsset(folderJaw, ClipFolder + "/jawOpen_100.anim");

            string sourcePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationSource.controller");
            var source = BuildSourceController(sourcePath, external);

            // 装配前先看槽位清单（面板那个折叠框读的就是它）：文件夹的 / 模板自带的 / 缺的。
            var slots = HoFaceAnimationAssets.Slots(source, ClipFolder);
            var jawSlot = slots.Find(s => s.name == "jawOpen_100");
            var ownSlot = slots.Find(s => s.name == "mouthSmileLeft_100");
            var emptySlot = slots.Find(s => s.name == "EmptyPlaceholder");
            Check(jawSlot != null && jawSlot.fromFolder != null && jawSlot.Status == "文件夹",
                "槽位按名字对上了文件夹里的片段（" + (jawSlot != null ? jawSlot.Status : "没有这个槽位") + "）");
            Check(ownSlot != null && ownSlot.fromFolder == null && ownSlot.Writes && ownSlot.Status == "模板自带",
                "文件夹里没有的槽位走模板自带的那份（" + (ownSlot != null ? ownSlot.Status : "没有这个槽位") + "）");
            Check(emptySlot != null && !emptySlot.Writes && emptySlot.Status == "缺",
                "模板里摆着空片段、文件夹也没有的槽位会被报成「缺」（" + (emptySlot != null ? emptySlot.Status : "没有这个槽位") + "）");

            rig = root.AddComponent<HoFaceTrackingDebugger>();
            rig.targetAnimator = animator;
            rig.treeTemplate = source;
            rig.animationFolder = ClipFolder;
            rig.meshes = new System.Collections.Generic.List<SkinnedMeshRenderer> { renderer };
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

            string controllerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationFace.controller");
            var controller = HoFaceAnimationAssets.Adopt(source, controllerPath, rig.meshes, animator, ClipFolder);
            rig.faceController = controller;
            Check(controller.layers.Length == source.layers.Length && controller.layers[0].name == source.layers[0].name,
                "装配是整份复制：层与状态原样带过来");
            Check(controller.parameters.Length == source.parameters.Length,
                "装配是整份复制：参数一个不少（" + controller.parameters.Length + "）");
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
                Check(compiled.bindings.Count == 44, "default output filter excludes eight gaze shapes");

            var jawClip = FindShapeClip(controller, "jawOpen");
            var jawCurve = jawClip != null ? AnimationUtility.GetEditorCurve(jawClip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.jawOpen")) : null;
            Check(jawCurve != null && Mathf.Abs(jawCurve.Evaluate(0f) - 100f) < 0.01f,
                "形态键曲线被重绑到驱动对象上（Source/Face → Body），值原样");
            Check(jawClip != null && AnimationUtility.GetCurveBindings(jawClip).Length == 2,
                "这个槽位用的是文件夹里的那一份（它多写了 mouthDimpleLeft，于是两条曲线）");
            var dimpleCurve = jawClip != null ? AnimationUtility.GetEditorCurve(jawClip,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouthDimpleLeft")) : null;
            Check(dimpleCurve != null && Mathf.Abs(dimpleCurve.Evaluate(0f) - 55f) < 0.01f,
                "文件夹里那份多写的键也一起被重绑过来了（55）");
            Check(AnimationUtility.GetEditorCurve(jawClip,
                EditorCurveBinding.FloatCurve("Source/Face", typeof(SkinnedMeshRenderer), "blendShape.jawOpen")) == null,
                "是搬不是复制：旧路径上的绑定没有留下来");
            var emptyClip = FindClip(controller, "EmptyPlaceholder");
            Check(emptyClip != null && AnimationUtility.GetCurveBindings(emptyClip).Length == 0,
                "没有对应动画的槽位装配后什么都不写（空片段仍是空的）");

            var stillExternal = AnimationUtility.GetEditorCurve(external,
                EditorCurveBinding.FloatCurve("Source/Face", typeof(SkinnedMeshRenderer), "blendShape.mouthClose"));
            Check(stillExternal != null && Mathf.Abs(stillExternal.Evaluate(0f) - 100f) < 0.01f,
                "别人共享的 .anim 一个字节没动");
            var copiedExternal = FindShapeClip(controller, "mouthClose");
            Check(copiedExternal != null && copiedExternal != external && AssetDatabase.GetAssetPath(copiedExternal) == controllerPath,
                "外部片段被复制成本文件的子资产");
            Check(ReferenceEquals(FindLeafMotion(FindTree(controller, "LipRegion"), "ARKit/mouthClose"), copiedExternal),
                "树里指向的是复制出来的那份，不是别人那份");

            var info = HoFaceAnimationAssets.Inspect(controller, rig.meshes);
            Check(info.shapes.Contains("jawOpen") && info.missing.Contains("HoNotOnMesh"),
                "结构摘要报得出哪些键这台模型没有（缺 " + info.missing.Count + " 个）");
            Check(info.layers == 1 && info.states == 1 && info.clips > 0,
                "结构摘要读的是资产实况（" + info.layers + " 层 / " + info.states + " 状态 / " + info.clips + " 个片段）");

            // ── 形态键基础动画生成器（通用动画工具，面捕只拿它当槽位数据）──────────────────
            const string BuiltFolder = "Assets/ValidationBuiltClips";
            if (AssetDatabase.IsValidFolder(BuiltFolder)) AssetDatabase.DeleteAsset(BuiltFolder);
            AssetDatabase.CreateFolder("Assets", "ValidationBuiltClips");
            var second = new GameObject("SecondFace");
            second.transform.SetParent(root.transform, false);
            var secondMesh = second.AddComponent<SkinnedMeshRenderer>();
            secondMesh.sharedMesh = mesh;   // 同一个网格资产：两张网格都有全部 52 个键 + JellyEye
            var built = HoBlendShapeClipBuilder.Build(new[] { renderer, secondMesh }, root.transform, BuiltFolder);
            Check(built.names.Count == 53, "每个非重合键名一份片段（" + built.names.Count + " = 52 ARKit + JellyEye）");
            var builtJaw = AssetDatabase.LoadAssetAtPath<AnimationClip>(BuiltFolder + "/jawOpen.anim");
            Check(builtJaw != null && builtJaw.name == "jawOpen", "片段名就是键名（" + BuiltFolder + "/jawOpen.anim）");
            Check(CountCurves(builtJaw, "jawOpen") == 2, "一个片段写所有有这个键的网格（"
                + CountCurves(builtJaw, "jawOpen") + " 条曲线）");
            var builtJawCurve = AnimationUtility.GetEditorCurve(builtJaw,
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.jawOpen"));
            Check(builtJawCurve != null && Mathf.Abs(builtJawCurve.Evaluate(0.5f) - 100f) < 0.01f,
                "值是 100 常量，不是斜坡（混合树采的是姿势，片段里没有时间轴）");
            string builtGuid = AssetDatabase.AssetPathToGUID(BuiltFolder + "/jawOpen.anim");
            var again = HoBlendShapeClipBuilder.Build(new[] { renderer, secondMesh }, root.transform, BuiltFolder);
            Check(again.created == 0 && again.updated == 53 && AssetDatabase.AssetPathToGUID(BuiltFolder + "/jawOpen.anim") == builtGuid,
                "重跑是覆盖式的：同名片段保留资产本身（GUID 不变），只重写曲线");
            UnityEngine.Object.DestroyImmediate(second);

            // 一个键落在两个驱动对象上：两边都要写；把对象去掉再重绑要能回来（幂等）。
            var alternate = new GameObject("Meshes");
            alternate.transform.SetParent(root.transform, false);
            var target = new GameObject("Face");
            target.transform.SetParent(alternate.transform, false);
            var alternateMesh = target.AddComponent<SkinnedMeshRenderer>();
            alternateMesh.sharedMesh = mesh;
            rig.meshes.Add(alternateMesh);
            HoFaceAnimationAssets.Retarget(controller, rig.meshes, animator);
            Check(CountShapeCurves(controller, "jawOpen") == 2,
                "同一个键在多个驱动对象上就写多个绑定（" + CountShapeCurves(controller, "jawOpen") + "）");
            rig.meshes.Remove(alternateMesh);
            HoFaceAnimationAssets.Retarget(controller, rig.meshes, animator);
            Check(CountShapeCurves(controller, "jawOpen") == 1, "重绑跟着驱动对象列表走：去掉就回到一条");
            UnityEngine.Object.DestroyImmediate(alternate);
            // ── 覆盖式装配：真的把文件换掉，但**不改 GUID** ─────────────────────────
            // 改 GUID 的话，场景里引用过这个控制器的地方（窥视对象的 Animator）就全断了。
            int clipsBefore = CountClips(controllerPath);
            string guidBefore = AssetDatabase.AssetPathToGUID(controllerPath);
            controller = HoFaceAnimationAssets.Adopt(source, controllerPath, rig.meshes, animator, ClipFolder, true);
            rig.faceController = controller;
            Check(AssetDatabase.AssetPathToGUID(controllerPath) == guidBefore,
                "覆盖式装配保留资产 GUID —— 引用它的地方不会断");
            Check(CountClips(controllerPath) == clipsBefore, "覆盖式装配不堆子资产（" + clipsBefore + " 个片段）");
            Check(CountShapeCurves(controller, "jawOpen") == 1, "覆盖后依然是重绑过的（曲线指向驱动对象）");

            // 反面用例：同一个 Direct 树把 Write Defaults 关掉必须被拒 —— 实测那个组合会发散
            //（0.6 的输入 → 98.98 → 246.28 → 1059.33），不能靠运气。
            var driveState = FindState(controller, "Ho/00 Drive");   // 夹具那层的名字，装配不该改它
            Check(driveState != null, "装配没有动层结构：驱动段还在");
            driveState.writeDefaultValues = false;
            bool rejected = false;
            try { using (var compiled = HoFaceAnimationAssets.Compile(rig)) { } } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "reject Direct tree with Write Defaults Off instead of letting it diverge");
            driveState.writeDefaultValues = true;

            // ── 果冻键不进控制器：它由独立组件（HoSpringConstraint）直接读写形态键 ──────────
            // 保留网格上这个键，是因为下面的 WD 探针需要一个
            // **没有任何片段动画它**的键做对照（"WD 开"不该去碰没被动画的属性），果冻键正好符合。
            Check(renderer.sharedMesh.GetBlendShapeIndex("JellyEye") >= 0,
                "validation mesh keeps an un-animated user key as the Write Defaults control");

            int outputBindings = 0;
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
                outputBindings = compiled.bindings.Count;

            Check(outputBindings == 44, "装配后的输出集合还是那 44 个（凝视排除）("
                + outputBindings + " bindings)");

            // ── 轴算术（中间层）：两根 0~1 的通道合成一根 -1~1 的单轴 ────────────────
            // 混合树自己算不出新参数，只能消费 —— 所以轴必须在外面算（19 节定案）。
            // 树那一半现在在控制器作品里（作者的资产），代码这边只剩这个纯函数。
            Near(HoFaceAxis.Merge(0.8f, 0.3f), 0.5f, "axis merge is positive minus negative", 0.0001f);
            Near(HoFaceAxis.Merge(0.2f, 0.9f), -0.7f, "axis merge goes negative when the other side wins", 0.0001f);
            Near(HoFaceAxis.Merge(float.NaN, 0.4f), -0.4f, "axis merge swallows NaN instead of poisoning the axis", 0.0001f);
            HoFaceAxis.Split(-0.7f, out float axisPositive, out float axisNegative);
            Check(Mathf.Abs(axisPositive) < 0.0001f && Mathf.Abs(axisNegative - 0.7f) < 0.0001f,
                "the axis splits back into two unsigned halves");

            // 二：**建树这件事已经不在代码里了**。控制器是搬来的作品（§ 装配），
            // 它的树形/坐标/每格写什么由作者在混合树编辑器里定 —— 所以这里没有 Kit 可测。

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

            // 预设只改当前这一根组件的参数：填弹簧值 + 给驱动键 + 目标为空时补两行待填（挤压 / 回弹）。
            var presetProbe = new GameObject("PresetProbe");
            var presetMesh = presetProbe.AddComponent<SkinnedMeshRenderer>();
            presetMesh.sharedMesh = renderer.sharedMesh;
            var presetSpring = presetProbe.AddComponent<HoSpringConstraint>();
            presetSpring.Meshes.Add(presetMesh);
            presetSpring.ApplyJellyPreset(true);
            Check(Mathf.Abs(presetSpring.Frequency - HoSpringPresets.JellyFrequencyY) < 0.001f
                && Mathf.Abs(presetSpring.Damping - HoSpringPresets.JellyDamping) < 0.001f,
                "the jelly preset rewrites this component's spring values (" + presetSpring.Frequency + " Hz)");
            Check(presetSpring.KeyNames.Contains("eyeBlinkLeft") && presetSpring.Targets.Count == 2
                && !presetSpring.Targets[0].Reversed && presetSpring.Targets[1].Reversed,
                "the preset fills the driving keys and leaves two ready rows (squash / rebound)");
            presetSpring.ApplyJellyPreset(false);
            Check(Mathf.Abs(presetSpring.Frequency - HoSpringPresets.JellyFrequencyX) < 0.001f && presetSpring.Targets.Count == 2,
                "applying the other axis keeps the rows that are already there");
            UnityEngine.Object.DestroyImmediate(presetProbe);

            // ── 双眼同步（中间层）：把左右合成一个值，压住"左右键各能闭双眼"导致的过眨眼 ──
            int blinkLeft = HoFaceTrackingChannels.IndexOf("eyeBlinkLeft");
            int blinkRight = HoFaceTrackingChannels.IndexOf("eyeBlinkRight");
            int lookInLeft = HoFaceTrackingChannels.IndexOf("eyeLookInLeft");
            int lookOutRight = HoFaceTrackingChannels.IndexOf("eyeLookOutRight");
            int lookUpLeft = HoFaceTrackingChannels.IndexOf("eyeLookUpLeft");
            int lookUpRight = HoFaceTrackingChannels.IndexOf("eyeLookUpRight");

            var independent = new float[52];
            independent[blinkLeft] = 0.9f;
            independent[blinkRight] = 0.5f;
            HoFaceEyeSync.Apply(independent, false, 0.5f);
            Check(Mathf.Abs(independent[blinkLeft] - 0.9f) < 0.0001f && Mathf.Abs(independent[blinkRight] - 0.5f) < 0.0001f,
                "eye sync off leaves the two eyes independent (wink still possible)");

            var synced = new float[52];
            synced[blinkLeft] = 0.9f;
            synced[blinkRight] = 0.5f;
            synced[lookInLeft] = 0.8f;
            synced[lookOutRight] = 0.2f;
            synced[lookUpLeft] = 0.9f;
            synced[lookUpRight] = 0.1f;
            HoFaceEyeSync.Apply(synced, true, 0.5f);
            Check(Mathf.Abs(synced[blinkLeft] - 0.7f) < 0.0001f && Mathf.Abs(synced[blinkRight] - 0.7f) < 0.0001f,
                "eye sync puts both eyelids on the shared value (got " + synced[blinkLeft].ToString("F2") + ")");
            Check(Mathf.Abs(synced[lookInLeft] - 0.5f) < 0.0001f && Mathf.Abs(synced[lookOutRight] - 0.5f) < 0.0001f,
                "the horizontal gaze is synced across the pair that means the same world direction");
            Check(Mathf.Abs(synced[lookUpLeft] - 0.9f) < 0.0001f && Mathf.Abs(synced[lookUpRight] - 0.1f) < 0.0001f,
                "the vertical gaze is deliberately NOT synced (same scope as the reference)");

            var mixed = new float[52];
            mixed[blinkLeft] = 0.9f;
            mixed[blinkRight] = 0.5f;
            HoFaceEyeSync.Apply(mixed, true, 1f);
            Check(Mathf.Abs(mixed[blinkLeft] - 0.5f) < 0.0001f && Mathf.Abs(mixed[blinkRight] - 0.5f) < 0.0001f,
                "the mix chooses whose value wins (1 = 全用右眼)");

            // 过眨眼的正解：模型上左右眨眼键**各自都能闭双眼**时，光合并值没用（形变还是写两遍），
            // 必须只让一侧有值。
            var single = new float[52];
            single[blinkLeft] = 0.9f;
            single[blinkRight] = 0.5f;
            HoFaceEyeSync.Apply(single, true, 0.5f, true);
            Check(Mathf.Abs(single[blinkLeft] - 0.7f) < 0.0001f && Mathf.Abs(single[blinkRight]) < 0.0001f,
                "single-key mode keeps one side and zeroes the other, so the deformation is applied once ("
                + single[blinkLeft].ToString("F2") + " / " + single[blinkRight].ToString("F2") + ")");

            // ── 表达式求值器（语法照 VBridger）：变量、优先级、函数、惰性 if、非有限折 0 ──────
            var facts = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "eyeBlinkLeft", 0.6f }, { "eyeWideLeft", 0.7f }, { "jawOpen", 0.25f }, { "mouthClose", 0.5f }
            };
            Func<string, float> reads = name => facts.TryGetValue(name, out float v) ? v : 0f;
            Check(HoFaceExpression.TryParse("eyeBlinkLeft - eyeWideLeft", out var axis, out _) && Mathf.Abs(axis.Evaluate(reads) + 0.1f) < 0.0001f,
                "表达式能算双向轴（闭 − 睁大 = −0.1）");
            Check(HoFaceExpression.TryParse("-2^2", out var power, out _) && Mathf.Abs(power.Evaluate(reads) + 4f) < 0.0001f,
                "^ 比一元负号紧（-2^2 = −4，与数学一致）");
            Check(HoFaceExpression.TryParse("clamp(1 - eyeBlinkLeft, 0, 1)", out var clamp, out _) && Mathf.Abs(clamp.Evaluate(reads) - 0.4f) < 0.0001f,
                "函数可用（clamp(1 − blink) = 0.4）");
            Check(HoFaceExpression.TryParse("sqrt(-1) + 5", out var finite, out _) && Mathf.Abs(finite.Evaluate(reads) - 5f) < 0.0001f,
                "非有限结果折 0（sqrt(−1) 当 0，不污染整条算式）");
            Check(HoFaceExpression.TryParse("jawOpen / (jawOpen - jawOpen)", out var divZero, out _) && Mathf.Abs(divZero.Evaluate(reads)) < 0.0001f,
                "除零按 0 算");
            Check(HoFaceExpression.TryParse("noSuchKey + 1", out var unknown, out _) && Mathf.Abs(unknown.Evaluate(reads) - 1f) < 0.0001f,
                "未知变量按 0（不抛异常，面板另报名字）");
            Check(!HoFaceExpression.TryParse("clamp(1, 2)", out _, out string syntaxError) && !string.IsNullOrEmpty(syntaxError),
                "元数错在解析期就报错：" + syntaxError);
            int lookups = 0;
            Func<string, float> counting = name => { lookups++; return 1f; };
            Check(HoFaceExpression.TryParse("if('jawOpen > 0.5', jawOpen, mouthClose)", out var lazy, out _)
                && Mathf.Abs(lazy.Evaluate(counting) - 1f) < 0.0001f && lookups == 2,
                "if('条件', 真, 假) 只算被选中的那支（另一支的变量一次都没取，" + lookups + " 次取值）");
            // 别的血统那种加权式：VRCFT 的 EyeLid = 0.75·(1−blink) + 0.25·wide —— **一行就能表达**。
            Check(HoFaceExpression.TryParse("0.75 - 0.75*eyeBlinkLeft + 0.25*eyeWideLeft", out var vrc, out _)
                && Mathf.Abs(vrc.Evaluate(reads) - 0.475f) < 0.0001f,
                "加权式不用专门的字段，表达式就够（VRCFT 的 EyeLid = 0.475）");

            // ── 曲线与"一行输出"：范围之外按端点算（不外推）────────────────────────
            var ramp = AnimationCurve.Linear(0.2f, 0f, 1f, 1f);
            Near(HoFaceCurve.Transfer(ramp, 0.1f), 0f, "curve clamps below its first key", 0.0001f);
            Near(HoFaceCurve.Transfer(ramp, 0.6f), 0.5f, "curve interpolates inside its key range", 0.0001f);
            Near(HoFaceCurve.Transfer(ramp, 2f), 1f, "curve clamps above its last key instead of extrapolating", 0.0001f);
            var curveRow = new HoFaceOutput { parameter = "ARKit/jawOpen", expression = "jawOpen", curve = AnimationCurve.Linear(0f, 0f, 1f, 50f) };
            Near(curveRow.Transform(0.6f), 30f, "一行输出用曲线换标度（60% → 30）", 0.01f);

            // ── 默认中间层 + 配置文件读写（我们自己的 JSON）────────────────────────
            var defaults = HoFaceMiddlewareDefaults.Create();
            var parameterNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            bool defaultsParse = true;
            foreach (var output in defaults.outputs)
            {
                if (!parameterNames.Add(output.parameter)) defaultsParse = false;
                if (!HoFaceExpression.TryParse(output.expression, out _, out _)) defaultsParse = false;
            }
            Check(defaults.outputs.Count == 56 && defaultsParse,
                "默认中间层 = 52 个 ARKit 直通 + 4 根眼睑轴，表达式全部可解析（" + defaults.outputs.Count + "）");

            string profileJson = HoFaceProfile.Write(defaults);
            Check(profileJson.Contains("\"format\": \"ho-face-middleware\"") && profileJson.Contains("jawOpen") && profileJson.Contains("keys"),
                "写出来的是带 format 头的可读 JSON");
            Check(HoFaceProfile.TryParse(profileJson, out var roundTrip, out _)
                && roundTrip.outputs.Count == defaults.outputs.Count
                && roundTrip.outputs[0].parameter == defaults.outputs[0].parameter
                && roundTrip.outputs[0].curve.length == defaults.outputs[0].curve.length,
                "写出去再读回来是一致的（行数 + 参数名 + 曲线关键点）");
            Check(HoFaceProfile.TryParse("{\"outputs\":[{\"parameter\":\"ARKit/jawOpen\",\"expression\":\"jawOpen\",\"modifiers\":[{\"kind\":\"nope\"}]}]}",
                out var forgiving, out string kindError) && forgiving.outputs.Count == 1
                && kindError != null && kindError.Contains("nope"),
                "认不出的修饰符 kind 会被点名，而不是静默丢掉");
            Check(!HoFaceProfile.TryParse("{ not json", out _, out _), "坏 JSON 报错，不会变成一张空表");

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

    /// <summary>
    /// 测试夹具：模拟"一份作者编好的混合树文件"。**控制器是作品，代码不再生成它** ——
    /// 所以夹具照我们那份控制器的形状手搭：一层驱动段 + 一棵 Direct 根树（眼/唇两棵区域子树、
    /// 每个 ARKit 键一个直通叶子、门控各一个参数）+ 4 根眼睑轴参数。
    /// **曲线绑在 `Source/Face` 上**（作者自己的模型层级），这样"重绑驱动对象"才真的被验到。
    /// </summary>
    private static AnimatorController BuildSourceController(string path, AnimationClip external)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var layers = controller.layers;
        layers[0].name = "Ho/00 Drive";
        controller.layers = layers;

        foreach (string shape in HoFaceTrackingChannels.Names)
            controller.AddParameter("ARKit/" + shape, AnimatorControllerParameterType.Float);
        controller.AddParameter("ARKit/HoNotOnMesh", AnimatorControllerParameterType.Float);
        AddGate(controller, HoFaceNaming.Gate(HoFaceGate.Eye));
        AddGate(controller, HoFaceNaming.Gate(HoFaceGate.Lip));
        for (int side = 0; side < 2; side++)
            for (int axis = 0; axis < 2; axis++)
                controller.AddParameter(HoFaceNaming.LidAxis(side, axis == 0), AnimatorControllerParameterType.Float);

        var root = new BlendTree { name = "DriveTree", blendType = BlendTreeType.Direct };
        AssetDatabase.AddObjectToAsset(root, controller);
        foreach (HoFaceGate gate in new[] { HoFaceGate.Eye, HoFaceGate.Lip })
        {
            var region = new BlendTree
            {
                name = gate == HoFaceGate.Eye ? "EyeRegion" : "LipRegion",
                blendType = BlendTreeType.Direct
            };
            AssetDatabase.AddObjectToAsset(region, controller);
            foreach (string shape in HoFaceTrackingChannels.Names)
            {
                if (HoFaceTrackingChannels.Gate(shape) != gate) continue;
                if (shape == "mouthClose") continue;   // 这个键走下面那条"别人的外部片段"
                AttachFlatLeaf(region, SourceShapeClip(controller, shape), "ARKit/" + shape);
            }

            if (gate == HoFaceGate.Lip) AttachFlatLeaf(region, external, "ARKit/mouthClose");
            AttachFlatLeaf(root, region, HoFaceNaming.Gate(gate));
        }

        // 一个"驱动对象上没有"的键：装配时曲线该原样留着，并被结构摘要报出来。
        AttachFlatLeaf(FindTree(root, "LipRegion"), SourceShapeClip(controller, "HoNotOnMesh"), "ARKit/HoNotOnMesh");

        // 一个**空槽位**：模板里摆着片段但没写任何键，文件夹里也没有同名的 —— 装配后它什么都不写。
        controller.AddParameter("ARKit/EmptySlot", AnimatorControllerParameterType.Float);
        var empty = new AnimationClip { name = "EmptyPlaceholder", frameRate = 60f };
        AssetDatabase.AddObjectToAsset(empty, controller);
        AttachFlatLeaf(FindTree(root, "EyeRegion"), empty, "ARKit/EmptySlot");

        var state = controller.layers[0].stateMachine.AddState("Face");
        state.writeDefaultValues = true;   // Direct 树的前提
        state.motion = root;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>门控参数：缺就补，并且**默认 1**（单独打开这个资产时不该是一片死脸）。</summary>
    private static void AddGate(AnimatorController controller, string name)
    {
        controller.AddParameter(name, AnimatorControllerParameterType.Float);
        var all = controller.parameters;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name != name) continue;
            all[i].defaultFloat = 1f;
            controller.parameters = all;
            return;
        }
    }

    /// <summary>作者的一格：参数 0→1，键 0→100，绑在他自己的层级（Source/Face）上。</summary>
    private static AnimationClip SourceShapeClip(AnimatorController controller, string shape)
    {
        var clip = new AnimationClip { name = shape + "_100", frameRate = 60f };
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve("Source/Face", typeof(SkinnedMeshRenderer), "blendShape." + shape),
            AnimationCurve.Constant(0f, 1f / 60f, 100f));
        AssetDatabase.AddObjectToAsset(clip, controller);
        return clip;
    }

    private static void AttachFlatLeaf(BlendTree tree, Motion motion, string parameter)
    {
        tree.AddChild(motion);
        var children = tree.children;
        children[children.Length - 1].directBlendParameter = parameter;
        tree.children = children;
    }

    private static BlendTree FindTree(AnimatorController controller, string name)
    {
        foreach (var layer in controller.layers)
            foreach (var state in layer.stateMachine.states)
                if (FindTree(state.state.motion, name) is BlendTree found) return found;
        return null;
    }

    private static BlendTree FindTree(Motion motion, string name)
    {
        if (!(motion is BlendTree tree)) return null;
        if (tree.name == name) return tree;
        foreach (var child in tree.children)
            if (FindTree(child.motion, name) is BlendTree found) return found;
        return null;
    }

    private static Motion FindLeafMotion(BlendTree tree, string parameter)
    {
        if (tree == null) return null;
        foreach (var child in tree.children)
            if (child.directBlendParameter == parameter) return child.motion;
        return null;
    }

    private static AnimationClip FindShapeClip(AnimatorController controller, string shape)
    {
        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip == null) continue;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.propertyName == "blendShape." + shape) return clip;
        }

        return null;
    }

    private static int CountShapeCurves(AnimatorController controller, string shape)
    {
        var clip = FindShapeClip(controller, shape);
        if (clip == null) return 0;
        int count = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.propertyName == "blendShape." + shape) count++;
        return count;
    }

    /// <summary>一条片段里某个键的曲线条数（不限控制器 —— 生成器那条用例直接拿片段问）。</summary>
    private static int CountCurves(AnimationClip clip, string shape)
    {
        if (clip == null) return 0;
        int count = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.propertyName == "blendShape." + shape) count++;
        return count;
    }

    /// <summary>按片段名找（装配后模板里的片段仍是子资产，只是可能没有曲线）。</summary>
    private static AnimationClip FindClip(AnimatorController controller, string name)
    {
        foreach (AnimationClip clip in controller.animationClips)
            if (clip != null && clip.name == name) return clip;
        return null;
    }

    private static void PlayTests()
    {
        if (!Application.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Play tests timed out.");
            if (Time.frameCount < frame) return;
            if (stage == 0)
            {
                rig = UnityEngine.Object.FindFirstObjectByType<HoFaceTrackingDebugger>();
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

                // ── 中间层配置文件端到端：一行一个输出（表达式 + 曲线 + 有序修饰符）──────────
                // 走完整条路：写文件 → 导入 → 组件读 → 会话求值 → 参数 → 影子混合树 → 真模型。
                // 断言刻意做成不依赖帧率：Smooth 给 1 秒，只要求"没一步到位"、再要求它单调逼近。
                string profileAssetPath = "Assets/ValidationProfile" + HoFaceProfile.Extension;
                string profileFullPath = System.IO.Path.Combine(Application.dataPath, "ValidationProfile" + HoFaceProfile.Extension);
                profileAsset = profileAssetPath;
                profileFull = profileFullPath;
                System.IO.File.WriteAllText(profileFull, HoFaceProfile.Write(new HoFaceMiddleware
                {
                    displayName = "validation",
                    outputs = new System.Collections.Generic.List<HoFaceOutput>
                    {
                        new HoFaceOutput
                        {
                            parameter = "ARKit/jawOpen", expression = "jawOpen",
                            // 曲线是**参数值**（不是百分数）：参数直接当树的子权重，所以这里是 0..1。
                            // 片段里那 100 是另一回事（树采的是姿势）。
                            modifiers = new System.Collections.Generic.List<HoFaceModifier>
                            {
                                new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = 1.0f }
                            }
                        },
                        new HoFaceOutput
                        {
                            parameter = "ARKit/mouthSmileLeft", expression = "mouthSmileLeft",
                            curve = AnimationCurve.Linear(0f, 0f, 1f, 0.5f)      // 曲线换标度：手工 0.8 → 参数 0.4
                        },
                        new HoFaceOutput { parameter = "ARKit/eyeBlinkLeft", expression = "eyeBlinkLeft" }
                    }
                }), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(profileAsset);
                rig.profile = AssetDatabase.LoadAssetAtPath<TextAsset>(profileAsset);
                rig.ReloadProfile();
                Check(rig.Middleware != null && rig.Middleware.outputs.Count == 3,
                    "组件从配置文件里读到了中间层（" + (rig.Middleware != null ? rig.Middleware.outputs.Count : -1) + " 行）");
                HoFaceInputHub.Start(rig);
                var smoothSession = HoFaceInputHub.Session(rig);
                Check(smoothSession != null, "session started for the profile test: " + HoFaceInputHub.Error(rig));
                Channel("jawOpen").manual = 1.0f;       // 曲线 0..100 + Smooth 1s
                Channel("jawOpen").mode = HoFaceInputMode.Manual;
                Channel("mouthSmileLeft").manual = 0.8f;   // 曲线 0..50
                Channel("mouthSmileLeft").mode = HoFaceInputMode.Manual;
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 8)
            {
                float transit = Weight("jawOpen");
                smoothSampleA = transit;
                var debugSession = HoFaceInputHub.Session(rig);
                Debug.Log("HO_PROFILE: weight=" + transit.ToString("F2")
                    + " param=" + (debugSession != null ? debugSession.OutputValue("ARKit/jawOpen").ToString("F4") : "?")
                    + " (weight = 参数 × 片段里的 100；Smooth 1s 所以还没到位)"
                    + " 目标 weight=100");
                Check(transit > 0.01f && transit < 99.0f,
                    "配置里的 Smooth 修饰符真的在过滤 (actual=" + transit + ")");
                stage++; frame = Time.frameCount + 12; return;
            }
            if (stage == 9)
            {
                float later = Weight("jawOpen");
                Debug.Log("HO_SMOOTH later=" + later + " (应比 transit=" + smoothSampleA + " 更大：还在往目标爬)");
                Check(later > smoothSampleA, "Smooth 修饰符持续收敛 (A=" + smoothSampleA + " B=" + later + ")");
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 10)
            {
                Near(Weight("mouthSmileLeft"), 40, "曲线换标度：0.8 进去、参数 0.4、权重 40（这一行没有修饰符，立刻到位）");
                float jaw = Weight("jawOpen");
                Check(jaw > smoothSampleA && jaw < 99f,
                    "1 秒的平滑还没到位（transit " + smoothSampleA.ToString("F1") + " → 现在 " + jaw.ToString("F1") + "）");

                // 换成**没有修饰符**的同一份配置：立刻直通，证明"爬得慢"确实是那个修饰符干的。
                System.IO.File.WriteAllText(profileFull, HoFaceProfile.Write(new HoFaceMiddleware
                {
                    displayName = "validation·no-modifier",
                    outputs = new System.Collections.Generic.List<HoFaceOutput>
                    {
                        new HoFaceOutput { parameter = "ARKit/jawOpen", expression = "jawOpen" },
                        new HoFaceOutput
                        {
                            parameter = "ARKit/mouthSmileLeft", expression = "mouthSmileLeft",
                            curve = AnimationCurve.Linear(0f, 0f, 1f, 0.5f)
                        },
                        new HoFaceOutput { parameter = "ARKit/eyeBlinkLeft", expression = "eyeBlinkLeft" }
                    }
                }), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(profileAsset);
                rig.ReloadProfile();
                Channel("jawOpen").manual = 1.0f;   // 还是手动 1.0：换配置之后应当**立刻**到位
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
                // 去掉修饰符之后直通：同一份配置、同一个输入，权重立刻到位。
                Near(Weight("jawOpen"), 100, "去掉修饰符之后直通（参数 1.0 → 权重 100）");

                // 实时输入那条路也在这里铺好：stage 17 会用它验证 UDP → 配置 → 混合树整条链。
                Channel("jawOpen").mode = HoFaceInputMode.Live;
                HoFaceInputHub.Connect("127.0.0.2");
                sender = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                Send("jawOpen-60|");

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
            // ── 判别性实验：同一属性被多个子节点写 = 相加还是平均？嵌套门控相乘吗？ ────────
            // 文档 docs/BLEND_TREE_LIMITS.md 里"能表达什么"那几张配方（乘积 / 加权平均）是
            // **从参考实现反推的**，这两条量出来之前它们只能算推断。
            if (stage == 14)
            {
                probeAnimator.runtimeAnimatorController = BuildSamePropertyProbe(false);
                probeRenderer.SetBlendShapeWeight(probeRenderer.sharedMesh.GetBlendShapeIndex("mouthSmileRight"), 0f);
                probeRenderer.SetBlendShapeWeight(probeRenderer.sharedMesh.GetBlendShapeIndex("noseSneerLeft"), 0f);
                probeAnimator.SetFloat("P/A", 0.6f);
                probeAnimator.SetFloat("P/B", 0.8f);
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 15)
            {
                float same = ProbeWeight("mouthSmileRight");
                Debug.Log("HO_MATRIX: 同一属性 0.6 + 0.8 → " + same
                    + "（140→钳 100 = 相加；70 = 平均；80 = 后写者胜）");
                Check(same > 0f, "same-property Direct children produce a value at all (actual=" + same + ")");
                probeAnimator.runtimeAnimatorController = BuildSamePropertyProbe(true);
                probeRenderer.SetBlendShapeWeight(probeRenderer.sharedMesh.GetBlendShapeIndex("noseSneerLeft"), 0f);
                probeAnimator.SetFloat("P/A", 0.6f);
                probeAnimator.SetFloat("P/B", 0.8f);
                stage++; frame = Time.frameCount + 10; return;
            }
            if (stage == 16)
            {
                float nested = ProbeWeight("noseSneerLeft");
                Debug.Log("HO_MATRIX: 嵌套门控 0.6 × 0.8 → " + nested
                    + "（48 = 相乘；100 = 不相乘；60/80 = 只有一层生效）");
                Check(nested > 0f, "nested Direct gates produce a value at all (actual=" + nested + ")");
                stage++; return;
            }
            if (stage == 17)
            {
                Send("jawOpen-60|");
                if (Mathf.Abs(Weight("jawOpen") - 60f) > 0.6f) return;   // 等它被驱动上来
                Check(true, "配置那条路端到端通了：UDP 0.6 → 表达式 → 曲线(0..100) → 参数 → 混合树 → 60");
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
    /// <summary>中间层配置文件在工程里的路径（写文件用）/ 磁盘全路径（ImportAsset 用）。</summary>
    private static string profileAsset, profileFull;
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

    /// <summary>
    /// 判别性实验用的最小控制器：两个参数 P/A、P/B 驱动一棵 Direct 树。
    /// `nested = false` 时两个子节点写**同一个属性**（量"相加还是平均"）；
    /// `nested = true` 时外层门控 A、里层门控 B 写一个属性（量"门控是否相乘"）。
    /// </summary>
    private static AnimatorController BuildSamePropertyProbe(bool nested)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(
            AssetDatabase.GenerateUniqueAssetPath("Assets/HoMatrixProbe.controller"));
        controller.AddParameter("P/A", AnimatorControllerParameterType.Float);
        controller.AddParameter("P/B", AnimatorControllerParameterType.Float);
        var sameBinding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouthSmileRight");
        var nestBinding = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.noseSneerLeft");

        var root = new BlendTree { name = nested ? "NestOuter" : "SameProperty", blendType = BlendTreeType.Direct };
        AssetDatabase.AddObjectToAsset(root, controller);

        if (!nested)
        {
            for (int i = 0; i < 2; i++)
            {
                var part = new AnimationClip { name = "same" + i, frameRate = 60f };
                AnimationUtility.SetEditorCurve(part, sameBinding, AnimationCurve.Constant(0f, 1f / 60f, 100f));
                AssetDatabase.AddObjectToAsset(part, controller);
                root.AddChild(part);
                var kids = root.children;
                kids[kids.Length - 1].directBlendParameter = i == 0 ? "P/A" : "P/B";
                root.children = kids;
            }
        }
        else
        {
            var inner = new BlendTree { name = "NestInner", blendType = BlendTreeType.Direct };
            AssetDatabase.AddObjectToAsset(inner, controller);
            var leaf = new AnimationClip { name = "nestLeaf", frameRate = 60f };
            AnimationUtility.SetEditorCurve(leaf, nestBinding, AnimationCurve.Constant(0f, 1f / 60f, 100f));
            AssetDatabase.AddObjectToAsset(leaf, controller);
            inner.AddChild(leaf);
            var innerKids = inner.children;
            innerKids[0].directBlendParameter = "P/B";
            inner.children = innerKids;

            root.AddChild(inner);
            var outerKids = root.children;
            outerKids[0].directBlendParameter = "P/A";
            root.children = outerKids;
        }

        var state = controller.layers[0].stateMachine.AddState("矩阵探针");
        state.writeDefaultValues = true;
        state.motion = root;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
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
