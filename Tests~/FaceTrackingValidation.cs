// Copy to Assets/Editor of a disposable validation project and invoke HoFaceTrackingValidation.RunBatch.
// The marker file .ho-face-validation in that project's root is required before touching its scene.
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
    /// <summary>进播放之前把调试设置落在这儿，播放模式里再从它装回来（等价于面板存的那一份）。</summary>
    private const string ValidationSettingsPath = "Assets/ValidationFaceDebug.json";
    /// <summary>用例自己选的 VTS 本机监听端口。不用默认的 49984：真手机那条路可能正占着它。</summary>
    private const int TestLocalPort = 49986;
    private static int stage;
    private static int frame;
    private static double deadline;
    private static HoFaceDebugSettings rig;
    private static SkinnedMeshRenderer renderer;
    private static HoShapeKeyWriter writer;
    private static int targetId;
    private static UdpClient sender;
    /// <summary>stage 3（实时 UDP）里试了几帧 —— 只用来决定什么时候打那条体检日志。</summary>
    private static int liveTries;

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
            // ReceiverTests() 删掉了：iFacialMocap 接收端这个类已经不存在（只剩 VTS 一条路），
            // 起 socket 的那几条断言没有对象了。协议解析的覆盖在 ParserTests 的 VTS 那一段，
            // 以及离线台架 .research/profile-json-test（它连请求报文和 52 个键名一起验）。
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

            // 调试设置现在是**一个普通对象**（不是组件）：目标角色 / 模板 / 控制器按引用或路径记下来，
            // 角色上零组件 —— 和 Warudo 那边"角色身上不挂我们的东西"是同一条规矩。
            rig = new HoFaceDebugSettings();
            rig.SetCharacter(root);
            rig.SetTreeTemplate(source, sourcePath);
            rig.animationFolder = ClipFolder;
            // 要驱动的网格不再是手工填的一张表：它就是"调试对象下所有 SkinnedMeshRenderer"。
            // 下面一律现取 rig.Meshes() —— 它每次返回**新的表**，加一个删一个都只影响那一次调用。
            // 门控已经删掉了：哪些键算数由使用者自己的混合树决定。
            // 所以默认**不再排除任何键** —— 编译出来的绑定数就是控制器里那 52 个（含 8 个凝视键）。
            Check(rig.channels.Count == 52, "默认通道数 = 52 个形态键");
            // 夹具通道：全部 Manual + 五个手动值。stage 0 进播放之后会**再调一次同一个函数**
            //（播放会重载脚本域，而设置文件里不存通道 —— 通道只是过渡期字段）。
            ConfigureFixtureChannels(rig);

            string controllerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ValidationFace.controller");
            var controller = HoFaceAnimationAssets.Adopt(source, controllerPath, rig.Meshes(), animator, ClipFolder);
            rig.SetFaceController(controller, controllerPath);
            Check(controller.layers.Length == source.layers.Length && controller.layers[0].name == source.layers[0].name,
                "装配是整份复制：层与状态原样带过来");
            Check(controller.parameters.Length == source.parameters.Length,
                "装配是整份复制：参数一个不少（" + controller.parameters.Length + "）");
            using (var compiled = HoFaceAnimationAssets.Compile(rig))
                Check(compiled.bindings.Count == 52, "没有门控之后：控制器里 52 个键全部编译进来（含 8 个凝视键）");

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

            var info = HoFaceAnimationAssets.Inspect(controller, rig.Meshes());
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
            // 驱动对象列表就是"角色下所有 SkinnedMeshRenderer" —— `Meshes()` 是现取的，
            // 所以刚挂上去的这张**已经在这个表里**了（此刻 = [Body, Meshes/Face]），不用再手工加。
            var alternate = new GameObject("Meshes");
            alternate.transform.SetParent(root.transform, false);
            var target = new GameObject("Face");
            target.transform.SetParent(alternate.transform, false);
            var alternateMesh = target.AddComponent<SkinnedMeshRenderer>();
            alternateMesh.sharedMesh = mesh;
            var withAlternate = rig.Meshes();
            HoFaceAnimationAssets.Retarget(controller, withAlternate, animator);
            Check(CountShapeCurves(controller, "jawOpen") == 2,
                "同一个键在多个驱动对象上就写多个绑定（" + CountShapeCurves(controller, "jawOpen") + "）");
            withAlternate.Remove(alternateMesh);
            HoFaceAnimationAssets.Retarget(controller, withAlternate, animator);
            Check(CountShapeCurves(controller, "jawOpen") == 1, "重绑跟着驱动对象列表走：去掉就回到一条");
            UnityEngine.Object.DestroyImmediate(alternate);
            // ── 覆盖式装配：真的把文件换掉，但**不改 GUID** ─────────────────────────
            // 改 GUID 的话，场景里引用过这个控制器的地方（窥视对象的 Animator）就全断了。
            int clipsBefore = CountClips(controllerPath);
            string guidBefore = AssetDatabase.AssetPathToGUID(controllerPath);
            controller = HoFaceAnimationAssets.Adopt(source, controllerPath, rig.Meshes(), animator, ClipFolder, true);
            rig.SetFaceController(controller, controllerPath);
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

            Check(outputBindings == 52, "装配后的输出集合 = 控制器里那 52 个（门控已删，不再排除凝视）("
                + outputBindings + " bindings)");

            // ── 轴算术：**代码里的那个纯函数（HoFaceAxis）删掉了**。────────────────────
            // "两根 0~1 的通道合成一根 -1~1 的单轴"现在是中间层的**一行表达式**
            //（`eyeBlinkLeft - eyeWideLeft`，见下面表达式求值器那一段），不再有专门的类型。

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

            // ── 双眼同步（HoFaceEyeSync）**整块删掉了**。────────────────────────────────
            // "把左右合成一个值 / 只留一侧"那点事，按定案属于**混合树**，不该在参数生产这一层做；
            // 面板上的 eyeSync / eyeSyncMix / eyeSyncSingleKey 三个开关也一并退休。
            // 所以这里没有它可测：左右要不要合并、合并成什么，由控制器作品里的树决定。

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
            // 播放会重载脚本域：静态的 rig 一定会没 —— 所以进播放之前把它**落盘**，
            // stage 0 再从这份文件装回来（和面板走同一条路：HoFaceDebugSettings.LoadOrCreate）。
            rig.settingsPath = ValidationSettingsPath;
            rig.Save();
            SessionState.SetBool(PhaseKey, true);
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { Fail(e); }
    }

    /// <summary>
    /// 夹具用的通道配置：**全部 Manual + 五个手动值**。进播放会重载脚本域、而设置文件里**不存通道**
    /// （通道只是过渡期字段），所以 RunBatch 与 stage 0 调的是这同一个函数，免得两处漂掉。
    /// </summary>
    private static void ConfigureFixtureChannels(HoFaceDebugSettings settings)
    {
        foreach (var channel in settings.channels) channel.mode = HoFaceInputMode.Manual;
        Channel(settings, "jawOpen").manual = 0.6f;
        Channel(settings, "mouthClose").manual = 0.25f;
        Channel(settings, "mouthSmileLeft").manual = 0.8f;
        Channel(settings, "eyeBlinkLeft").manual = 0.4f;
        Channel(settings, "eyeLookInLeft").manual = 1f;
    }

    private static HoFaceChannel Channel(HoFaceDebugSettings settings, string name) =>
        settings.channels.Find(c => c.shape == name);

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
        controller.AddParameter(RegionGateParameter, AnimatorControllerParameterType.Float);
        SetDefaultFloat(controller, RegionGateParameter, 1f);
        for (int side = 0; side < 2; side++)
            for (int axis = 0; axis < 2; axis++)
                controller.AddParameter(HoFaceNaming.LidAxis(side, axis == 0), AnimatorControllerParameterType.Float);

        // 树形照旧分成"眼"/"唇"两块 —— 但**不再由我们注入门控参数**：这两块现在直接挂在根下，
        // 要不要开关、什么时候交还，由使用者自己的参数/树决定。
        var root = new BlendTree { name = "DriveTree", blendType = BlendTreeType.Direct };
        AssetDatabase.AddObjectToAsset(root, controller);
        foreach (string group in new[] { "EyeRegion", "LipRegion" })
        {
            var region = new BlendTree { name = group, blendType = BlendTreeType.Direct };
            AssetDatabase.AddObjectToAsset(region, controller);
            foreach (string shape in HoFaceTrackingChannels.Names)
            {
                bool eye = HoFaceTrackingChannels.Region(shape) == HoFaceRegion.Eyelids
                    || HoFaceTrackingChannels.Region(shape) == HoFaceRegion.Gaze
                    || HoFaceTrackingChannels.Region(shape) == HoFaceRegion.Brows;
                if (eye != (group == "EyeRegion")) continue;
                if (shape == "mouthClose") continue;   // 这个键走下面那条"别人的外部片段"
                AttachFlatLeaf(region, SourceShapeClip(controller, shape), "ARKit/" + shape);
            }

            AttachChild(root, region, RegionAlways());
        }

        AttachFlatLeaf(FindTree(root, "LipRegion"), external, "ARKit/mouthClose");

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

    /// <summary>
    /// 夹具自己的"区域总开关"参数：Direct 树的每个子节点都必须挂一个参数，
    /// **这是使用者那一侧的事**（我们的代码不再注入门控了）—— 常量 1 就是"永远算数"。
    /// </summary>
    private const string RegionGateParameter = "Fixture/RegionsOn";

    private static string RegionAlways() => RegionGateParameter;

    /// <summary>把某个 Float 参数的默认值改成 1（夹具的"区域总开关"默认就该是开的）。</summary>
    private static void SetDefaultFloat(AnimatorController controller, string name, float value)
    {
        var all = controller.parameters;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name != name) continue;
            all[i].defaultFloat = value;
            controller.parameters = all;
            return;
        }
    }

    /// <summary>把一棵子树挂到 Direct 树根下，权重挂在夹具自己的常量参数上。</summary>
    private static void AttachChild(BlendTree root, BlendTree child, string parameter)
    {
        root.AddChild(child);
        var children = root.children;
        children[children.Length - 1].directBlendParameter = parameter;
        root.children = children;
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
            // 组件没了，会话不再有人替我们推进：面板那边是 HoFaceDebugHost 在 update 里推，
            // 用例这边就自己推 —— 而且必须**每次 update 都推**，不能只在"断言那一刻"推，
            // 否则 Smooth 这类跟时间走的修饰符量不到东西。
            if (rig != null)
            {
                HoFaceInputHub.Tick(rig);
                HoFaceInputHub.LateTick(rig);
            }
            if (Time.frameCount < frame) return;
            if (stage == 0)
            {
                // 播放重载脚本域 → 静态字段全清空：调试设置**从文件装回来**（通道也再配一遍）。
                // 万一没重载（关掉了 Enter Play Mode 的域重载），rig 还在，那就照用。
                if (rig == null)
                {
                    rig = HoFaceDebugSettings.LoadOrCreate(ValidationSettingsPath);
                    ConfigureFixtureChannels(rig);
                }
                if (rig.TargetAnimator() == null || Time.frameCount < 4) return;
                renderer = rig.TargetAnimator().transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                // 记录接管前的 Animator 状态，作为 hasBoundPlayables 语义的实测证据。
                Debug.Log("HO_BEFORE: hasBoundPlayables=" + rig.TargetAnimator().hasBoundPlayables
                    + " controller=" + (rig.TargetAnimator().runtimeAnimatorController != null));
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
                // 门控删掉之后，凝视键**也归面捕驱动**（这个通道是 Manual=1）→ 100。
                // 要让基础动画拿回某个键，现在的做法是把那个通道设成「交还」（stage 2 验的就是它）。
                Near(Weight("eyeLookInLeft"), 100, "凝视键同样由面捕驱动（门控已删）");
                Near(rig.TargetAnimator().transform.localPosition.x, 2, "body transform animation preserved");
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
                // 只剩 VTS 一条协议了：源条目就是"VTS 手机 + 一个本机端口"，
                // 载荷照官方形状（形态键是 iOS 原始值 0..1），量纲换算归中间层的输入行。
                HoFaceInputHub.ConnectEntries(new System.Collections.Generic.List<HoFaceSourceEntry>
                {
                    new HoFaceSourceEntry { kind = HoFaceSourceKind.VtsIphone, phoneIp = "127.0.0.2", localPort = TestLocalPort }
                });
                sender = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                SendPacket(Shape("JawOpen", 0.9f), Shape("EyeBlinkLeft", 0.1f));
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 3)
            {
                SendPacket(Shape("JawOpen", 0.9f), Shape("EyeBlinkLeft", 0.1f));
                // 收包链路的体检：接收端统计 + 合并后的线名 + 输入行的落点。
                // 只在这两个时刻打，免得每帧刷屏（第 5 帧够收包，第 120 帧够看出"一直没动静"）。
                liveTries++;
                if (liveTries == 5 || liveTries == 120)
                {
                    var receiver = HoFaceInputHub.Sources.Count > 0 ? HoFaceInputHub.Sources[0] : null;
                    var inputs = rig.Inputs();
                    int jawRow = -1;
                    for (int i = 0; i < inputs.Count; i++)
                        if (inputs[i].parameter == "jawOpen") jawRow = i;
                    Debug.Log("HO_LIVE#" + liveTries
                        + ": connected=" + HoFaceInputHub.Connected
                        + " running=" + (receiver != null && receiver.Running)
                        + " port=" + (receiver != null ? receiver.LocalPort : -1)
                        + " packets=" + (receiver != null ? receiver.Packets : -1)
                        + " invalid=" + (receiver != null ? receiver.Invalid : -1)
                        + " rejected=" + (receiver != null ? receiver.Rejected : -1)
                        + " rejectedSrc='" + (receiver != null ? receiver.RejectedSource : "") + "'"
                        + " err='" + (receiver != null ? receiver.Error : "") + "'"
                        + " hasJawWire=" + HoFaceInputHub.Has("JawOpen")
                        + " hubJaw=" + HoFaceInputHub.Input("JawOpen")
                        + " inputRows=" + inputs.Count + " jawRow=" + jawRow
                        + " rowExpr='" + (jawRow >= 0 ? inputs[jawRow].expression : "?") + "'"
                        + " weight=" + Weight("jawOpen"));
                }
                if (Mathf.Abs(Weight("jawOpen") - 90) > 0.2f) return;
                Near(Weight("jawOpen"), 90, "local VTS UDP packet drives actual blend tree");
                stage++; frame = Time.frameCount + 3; return;
            }
            if (stage == 4)
            {
                if (HoFaceInputHub.Now - HoFaceInputHub.LastFrameTime < 0.6) return;
                Near(Weight("jawOpen"), 17, "stale stream releases to base animation");
                HoFaceInputHub.Stop(rig);
                Check(rig.TargetAnimator().runtimeAnimatorController != null, "stop restores original controller");
                // 这条同时是"为什么不能用 hasBoundPlayables 判所有权"的实测证据：
                // 恢复成普通 Animator + Controller 之后它又是 true。
                Check(rig.TargetAnimator().hasBoundPlayables, "plain animator with a controller reports hasBoundPlayables=true");
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
                    // 线名 → 规范名：这份配置是**唯一的映射表**（内置默认表只在"还没指配置文件"时兜底）。
                    inputs = new System.Collections.Generic.List<HoFaceOutput>
                    {
                        new HoFaceOutput { parameter = "jawOpen", expression = "JawOpen", notes = "VTS 手机" },
                        new HoFaceOutput { parameter = "mouthSmileLeft", expression = "MouthSmileLeft", notes = "VTS 手机" },
                        new HoFaceOutput { parameter = "eyeBlinkLeft", expression = "EyeBlinkLeft", notes = "VTS 手机" }
                    },
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
                // 不再是 TextAsset 引用：设置里存的是**路径**，读的是磁盘上那个文件本身
                //（Unity 侧和 Warudo 侧读同一份 *.hoface.json —— 这是"面板里是什么、Warudo 里就是什么"的物理保证）。
                rig.profilePath = profileAssetPath;
                rig.ReloadProfile();
                Check(rig.Middleware != null && rig.Middleware.outputs.Count == 3,
                    "设置对象从配置文件里读到了中间层（" + (rig.Middleware != null ? rig.Middleware.outputs.Count : -1) + " 行）");
                Check(rig.Middleware != null && rig.Middleware.inputs.Count == 3,
                    "输入行也是从配置文件读的（" + (rig.Middleware != null ? rig.Middleware.inputs.Count : -1)
                    + " 行）—— 实时那条链的「线名 → 规范名」就靠它，不许拿内置默认来补");
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
                    // 输入行：**指了配置文件之后，线名 → 规范名就只认这里**（不再拿内置默认来补 ——
                    // 那正是"不做隐式处理"的意思）。所以后面那条"实时整条链"的断言需要它们。
                    inputs = new System.Collections.Generic.List<HoFaceOutput>
                    {
                        new HoFaceOutput { parameter = "jawOpen", expression = "JawOpen", notes = "VTS 手机" },
                        new HoFaceOutput { parameter = "mouthSmileLeft", expression = "MouthSmileLeft", notes = "VTS 手机" },
                        new HoFaceOutput { parameter = "eyeBlinkLeft", expression = "EyeBlinkLeft", notes = "VTS 手机" }
                    },
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

                // 实时输入那条路也在这里铺好：stage 17 会用它验证 VTS UDP → 配置 → 混合树整条链。
                Channel("jawOpen").mode = HoFaceInputMode.Live;
                HoFaceInputHub.ConnectEntries(new System.Collections.Generic.List<HoFaceSourceEntry>
                {
                    new HoFaceSourceEntry { kind = HoFaceSourceKind.VtsIphone, phoneIp = "127.0.0.2", localPort = TestLocalPort }
                });
                sender = new UdpClient(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 0));
                SendPacket(Shape("JawOpen", 0.6f));

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
                SendPacket(Shape("JawOpen", 0.6f));
                if (Mathf.Abs(Weight("jawOpen") - 60f) > 0.6f) return;   // 等它被驱动上来
                Check(true, "配置那条路端到端通了：VTS UDP 0.6 → 表达式 → 曲线(0..100) → 参数 → 混合树 → 60");
                HoFaceInputHub.Stop(rig);
                HoFaceInputHub.Disconnect();
                sender.Close(); sender = null;
                // 原来这里还有一条"禁用组件就立刻收摊"的断言 —— 组件已经不存在了，那条跟着删。
                // 现在收摊只有两条路：用户按停止（HoFaceInputHub.Stop）或退出播放（宿主在 ExitingPlayMode 里收）。
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
    private static GameObject zeroProbeRoot;
    private static SkinnedMeshRenderer zeroProbeRenderer;
    private static Animator zeroProbeAnimator;
    private static float atZeroWeight, controlWeight;
    private static float smoothSampleA;
    /// <summary>中间层配置文件在工程里的路径（写文件用）/ 磁盘全路径（ImportAsset 用）。</summary>
    private static string profileAsset, profileFull;

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

    /// <summary>一包 VTS 载荷里的一对形态键（官方写法 `{"k":…,"v":…}`，值是 iOS 原始值 0..1）。</summary>
    private static string Shape(string wire, float value) =>
        "{\"k\":\"" + wire + "\",\"v\":" + value.ToString("0.####", CultureInfo.InvariantCulture) + "}";

    /// <summary>
    /// 往本机 VTS 监听端口发一包**手机形状**的 JSON。只剩这一条协议了 —— iFacialMocap 的
    /// `键-值|` 文本协议连着接收端一起删掉了。**值不换算**：0.9 就是 0.9，量纲归中间层的输入行。
    /// </summary>
    private static void SendPacket(params string[] shapes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"FaceFound\":true,\"BlendShapes\":[" + string.Join(",", shapes) + "]}");
        sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, TestLocalPort));
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

        // 中间层默认输入行：线名 → 规范名 + 量纲。这两条是"不再有隐式处理"的核心证据。
        var defaults = HoFaceMiddlewareDefaults.Inputs();
        Check(defaults.Count > 52, "内置输入行覆盖两种协议");
        float converted = float.NaN;
        float vtsConverted = float.NaN;
        foreach (var row in defaults)
        {
            if (row.parameter != "jawOpen") continue;
            if (!HoFaceExpression.TryParse(row.expression, out var parsed, out _)) continue;
            if (row.notes == "iFacialMocap") converted = parsed.Evaluate(name => name == "jawOpen" ? 90f : 0f);
            if (row.notes == "VTS 手机") vtsConverted = parsed.Evaluate(name => name == "JawOpen" ? 0.9f : 0f);
        }
        Near(converted, 0.9f, "iFacialMocap 0..100 由输入行换算成 0..1", 0.0001f);
        Near(vtsConverted, 0.9f, "VTS 0..1 由输入行原样通过", 0.0001f);
        Check(HoFaceMiddlewareDefaults.VtsWire("eyeBlinkLeft") == "EyeBlinkLeft", "VTS 线名 = PascalCase");
        Check(HoFaceMiddlewareDefaults.IFacialWire("eyeBlinkLeft") == "eyeBlink_L", "iFacialMocap 线名 = _L 后缀");
        Check(HoFaceMiddlewareDefaults.IFacialWire("mouthLeft") == "mouthLeft", "mouthLeft 不带后缀");

        // ── 接收端的协议解析：**只剩 VTS 一条路**。iFacialMocap 的 `键-值|` 接收端连着那个类一起删了
        //（它的线名映射还留在默认输入行里，但"怎么解一包"已经没有代码了）—— 所以原来那 8 条
        // iFacialMocap 报文断言（值不除 100 / 线名不改 / head_0..5 / leftEye_1 / 缺键不补 0 /
        // NaN 计数 / 未知线名照收 / 只有姿态也算一帧）跟着那份代码一起删掉，没有对象可测了。
        // "接收端只交原样、不换算"这条规矩对 VTS 照样断言；键名与请求报文的逐条覆盖在
        // .research/profile-json-test（离线台架，不用起 Unity）。
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            // VTS 手机：字段名照抄载荷，值不换算。
            using (var vts = new VtsIphoneReceiver())
            {
                bool vtsOk = vts.ParseForTest("{\"Timestamp\":123,\"FaceFound\":true,\"Rotation\":{\"x\":1,\"y\":2,\"z\":3},"
                    + "\"Position\":{\"x\":0.1,\"y\":0.2,\"z\":0.3},\"Hotkey\":4,"
                    + "\"BlendShapes\":[{\"k\":\"EyeBlinkLeft\",\"v\":0.75},{\"k\":\"JawOpen\",\"v\":0.2}],"
                    + "\"EyeLeft\":{\"x\":9,\"y\":8,\"z\":7},\"EyeRight\":{\"x\":6,\"y\":5,\"z\":4},\"Future\":\"ignored\"}", out var v);
                Check(vtsOk, "VTS JSON 包解析（未知字段不炸）");
                Near(v.Get("EyeBlinkLeft"), 0.75f, "VTS 形态键原样（0..1）", 0.0001f);
                Near(v.Get("Rotation_y"), 2f, "VTS 头旋转分量", 0.0001f);
                Near(v.Get("Position_z"), 0.3f, "VTS 头位置分量", 0.0001f);
                Near(v.Get("EyeLeft_x"), 9f, "VTS 左眼分量", 0.0001f);
                Near(v.Get("FaceFound"), 1f, "VTS 有 faceFound 字段");
                Near(v.Get("Hotkey"), 4f, "VTS 有热键字段");
                Check(!vts.ParseForTest("", out _), "空载荷不算一帧");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    // ── ReceiverTests() 与它用的 IFacialEntry() 一起删掉了。──────────────────────────────
    // 那一段验的是"真的起一个 UDP socket、收一包、端口被占时报错、Dispose 之后能重开"，
    // 对象是 iFacialMocap 接收端 —— 那个类已经不存在了（只剩 VTS，而 VTS 是**请求式**的：
    // 它要主动往手机 21412 发续约包才收得到回包，本机自发自收量不到这条链路）。
    // 那几条断言里真正属于我们的部分（来源校验 / 大小上限 / 瞬时错误容忍 / 丢旧帧计数）
    // 现在都在 HoFaceReceiverBase 里，是"收包循环"那段代码的职责，不是协议解析的职责。

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
