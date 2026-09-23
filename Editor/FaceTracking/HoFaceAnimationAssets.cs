using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public sealed class HoFaceBinding
    {
        public SkinnedMeshRenderer renderer;
        public int index;
        public string path, shape;
        public float initial;
    }

    public sealed class HoFaceCompiledController : IDisposable
    {
        public AnimatorOverrideController controller;
        public readonly List<AnimationClip> clips = new List<AnimationClip>();
        public readonly List<HoFaceBinding> bindings = new List<HoFaceBinding>();
        public readonly List<string> warnings = new List<string>();
        /// <summary>基础 Controller 上的 Float 参数名。AnimatorOverrideController 自己不暴露 parameters。</summary>
        public readonly HashSet<string> floatParameters = new HashSet<string>(StringComparer.Ordinal);
        public void Dispose()
        {
            if (controller != null) Object.DestroyImmediate(controller);
            foreach (var clip in clips) if (clip != null) Object.DestroyImmediate(clip);
            clips.Clear();
        }
    }

    public static class HoFaceAnimationAssets
    {
        public static bool Allowed(HoFaceTrackingDebugger rig, string shape)
        {
            if ((rig.outputRegions & HoFaceTrackingChannels.Region(shape)) == 0) return false;
            foreach (var channel in rig.channels)
                if (channel != null && channel.shape == shape) return channel.mode != HoFaceInputMode.Release;
            return false;
        }

        public static HoFaceCompiledController Compile(HoFaceTrackingDebugger rig, Func<string, bool> outputFilter = null)
        {
            if (rig.targetAnimator == null) throw new InvalidOperationException("请指定角色 Animator。");
            if (!(rig.faceController is AnimatorController source))
                throw new InvalidOperationException("请选择纯 Unity AnimatorController。首版不接 VRC 行为或嵌套 OverrideController。");
            foreach (var layer in source.layers)
            {
                if (layer.syncedLayerIndex >= 0) throw new InvalidOperationException("首版暂不支持同步图层。");
                ValidateMachine(layer.stateMachine);
            }
            var parameters = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            foreach (var p in source.parameters) parameters[p.name] = p.type;
            var usedParameters = new HashSet<string>(StringComparer.Ordinal);
            var usedShapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in rig.channels)
            {
                if (channel == null || HoFaceTrackingChannels.IndexOf(channel.shape) < 0)
                    throw new InvalidOperationException("参数映射必须使用标准 ARKit 键名。");
                if (!usedShapes.Add(channel.shape)) throw new InvalidOperationException("重复形态键映射：" + channel.shape);
                if (string.IsNullOrWhiteSpace(channel.parameter)) throw new InvalidOperationException("参数名不能为空：" + channel.shape);
                if (!usedParameters.Add(channel.parameter)) throw new InvalidOperationException("多个通道映射到同一参数：" + channel.parameter);
                if (parameters.TryGetValue(channel.parameter, out var type) && type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException("面捕参数必须是 Float：" + channel.parameter);
            }

            var result = new HoFaceCompiledController();
            try
            {
                foreach (var p in source.parameters)
                    if (p.type == AnimatorControllerParameterType.Float) result.floatParameters.Add(p.name);
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                var seen = new HashSet<AnimationClip>();
                var bound = new HashSet<(SkinnedMeshRenderer, int)>();
                foreach (AnimationClip clip in source.animationClips)
                {
                    if (!seen.Add(clip)) continue;
                    if (AnimationUtility.GetAnimationEvents(clip).Length != 0 || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0 || clip.humanMotion)
                        throw new InvalidOperationException("面部片段含事件、对象曲线或 Humanoid 动画：" + clip.name);
                    var copy = Object.Instantiate(clip);
                    copy.name = clip.name + " (Ho Preview)";
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    result.clips.Add(copy);
                    var curves = AnimationUtility.GetCurveBindings(clip);
                    foreach (var curve in curves) AnimationUtility.SetEditorCurve(copy, curve, null);
                    foreach (var curve in curves)
                    {
                        if (curve.type != typeof(SkinnedMeshRenderer) || !curve.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                            throw new InvalidOperationException("首版面部控制器只允许形态键曲线：" + clip.name + " / " + curve.propertyName);
                        string shape = curve.propertyName.Substring("blendShape.".Length);
                        // **ARKit 的键走输入门控，非 ARKit 的键直接放行。**
                        // 门控管的是"我们写哪些参数"，不是"控制器能动哪些键"。用户的果冻混合树
                        // （或任何自己加的树）驱动的是自己的键，如果在这里被 `Allowed` 过滤掉，
                        // 影子台上算出来的姿势就永远抄不回真模型 —— 表现是"果冻层看着在跑，
                        // 脸上一点动静没有"。这一条是果冻改走混合树之后才暴露出来的。
                        if (HoFaceTrackingChannels.IndexOf(shape) >= 0
                            && (!Allowed(rig, shape) || (outputFilter != null && !outputFilter(shape)))) continue;
                        string path = Remap(rig, curve.path);
                        Transform node = path.Length == 0 ? rig.targetAnimator.transform : rig.targetAnimator.transform.Find(path);
                        var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                        {
                            string warning = path + " / " + shape + "：未找到绑定";
                            if (!result.warnings.Contains(warning)) result.warnings.Add(warning);
                            continue;
                        }
                        if (renderer.GetComponentInParent<Animator>() != rig.targetAnimator)
                            throw new InvalidOperationException("目标 Mesh 位于另一个 Animator 下：" + path);
                        var binding = curve;
                        binding.path = path;
                        AnimationUtility.SetEditorCurve(copy, binding, AnimationUtility.GetEditorCurve(clip, curve));
                        int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
                        if (bound.Add((renderer, index))) result.bindings.Add(new HoFaceBinding
                        {
                            renderer = renderer, index = index, path = path, shape = shape, initial = renderer.GetBlendShapeWeight(index)
                        });
                    }
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(clip, copy));
                }
                result.controller = new AnimatorOverrideController(source) { hideFlags = HideFlags.HideAndDontSave };
                result.controller.ApplyOverrides(overrides);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static string Remap(HoFaceTrackingDebugger rig, string path)
        {
            string result = path;
            int longest = -1;
            foreach (var mapping in rig.pathRemaps)
            {
                if (mapping == null || mapping.target == null) continue;
                string prefix = mapping.sourcePath ?? "";
                if (path != prefix && !(prefix.Length > 0 && path.StartsWith(prefix + "/", StringComparison.Ordinal))) continue;
                if (prefix.Length <= longest) continue;
                if (!mapping.target.transform.IsChildOf(rig.targetAnimator.transform)) throw new InvalidOperationException("重映射目标必须位于 Animator 根下。");
                string target = AnimationUtility.CalculateTransformPath(mapping.target.transform, rig.targetAnimator.transform);
                result = target + path.Substring(prefix.Length);
                longest = prefix.Length;
            }
            return result;
        }

        private static void ValidateMachine(AnimatorStateMachine machine)
        {
            if (machine.behaviours.Length != 0) throw new InvalidOperationException("不支持 StateMachineBehaviour：" + machine.name);
            foreach (var child in machine.states)
            {
                if (child.state.behaviours.Length != 0)
                    throw new InvalidOperationException("面部状态不能带 Behaviour：" + child.state.name);
                // Direct 树靠"权重和不足 1 时那部分与基准值混合"工作。写默认值关掉时，那个基准值取的是
                // "当前值"且永不复位——实测会逐帧发散（0.6 的输入 → 98.98 → 246.28 → 1059.33）。
                // 这个组合直接拒绝，不留给运气。
                if (!child.state.writeDefaultValues && HasDirectTree(child.state.motion))
                    throw new InvalidOperationException(
                        "Direct 混合树必须把 Write Defaults 打开，否则输出会发散：" + child.state.name
                        + "（见 docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md 的判别性实验）");
            }
            foreach (var child in machine.stateMachines) ValidateMachine(child.stateMachine);
        }

        /// <summary>状态的动作里（含嵌套）有没有 Direct 混合树。</summary>
        private static bool HasDirectTree(Motion motion)
        {
            if (!(motion is BlendTree tree)) return false;
            if (tree.blendType == BlendTreeType.Direct) return true;
            foreach (var child in tree.children)
                if (HasDirectTree(child.motion)) return true;
            return false;
        }

        /// <summary>生成的驱动层名字。以 <see cref="LayerPrefix"/> 开头 = 归本工具管，重新应用时会被重写。</summary>
        public const string LayerPrefix = "Ho/";
        public const string DriveLayerName = "Ho/00 Drive";
        /// <summary>留给用户手工加逻辑的层。<b>应用改动时永不触碰它。</b></summary>
        public const string EditLayerName = "Ho/99 (EDIT THIS)";

        /// <summary>
        /// 两个**区域门控**参数。区域子树挂在驱动层根树上，权重就是它 —— 于是"这块驱动算不算数"
        /// 是一个**参数**，可以被任何东西驱动（我们自己的会话、用户的层、以后的菜单），
        /// 而不是只能靠重新生成控制器来切。
        ///
        /// 名字统一由 <see cref="HoFaceNaming"/> 给（<c>Ho/Drive/Gate/Eye</c> / <c>…/Lip</c>）。
        /// </summary>
        public static string EyeGateName => HoFaceNaming.Gate(HoFaceGate.Eye);
        public static string LipGateName => HoFaceNaming.Gate(HoFaceGate.Lip);

        public static string GateParameterName(HoFaceGate gate) => HoFaceNaming.Gate(gate);

        public static string RegionTreeName(HoFaceGate gate) => HoFaceNaming.RegionTree(gate);

        /// <summary>
        /// 初始化：产出**一个完整文件**。每个 ARKit 键一个片段、驱动段是一棵 Direct 树，
        /// 另加一个空的 <see cref="EditLayerName"/> 作为用户的扩展点。
        ///
        /// 为什么不是"一层一个键"：那是早期"接管 Animator"时代的绕法，理由是"Direct 树会归一化、
        /// 各通道互相削弱"——**实测否掉了**。同一个 Direct 树里 jawOpen=0.6 与 mouthSmileLeft=0.8
        /// 同时给，得到的就是精确的 60 / 80（见 docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md）。
        ///
        /// 唯一的前提是 **Write Defaults 要开**：Direct 树里权重和不足 1 的那部分 (1-Σw) 会和
        /// 基准值混合；WD 关时基准值取"当前值"且永不复位，实测会发散（98.98 → 246.28 → 1059.33）。
        /// 面部控制器现在跑在隔离的影子台上，没有别的写入者，所以 WD 开在这里完全无害。
        ///
        /// 参考实现（Jerry 的 ARKit 模板）也是这个形状：1 个驱动层 + 一棵 Direct 树。
        /// </summary>
        public static AnimatorController Generate(Animator animator, string assetPath, bool overwrite = false)
        {
            if (animator == null) throw new InvalidOperationException("请先指定 Animator。");
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                // 「初始化」是破坏性的，所以覆盖必须由调用方**显式**确认（面板上已经弹过确认框），
                // 这里再拦一道只是防手滑：默认不允许覆盖。
                if (!overwrite) throw new InvalidOperationException("目标资产已存在，请选择新路径（或确认覆盖）。");
                AssetDatabase.DeleteAsset(assetPath);
            }
            AnimatorController controller = null;
            try
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
                var layers = controller.layers;
                layers[0].name = DriveLayerName;
                controller.layers = layers;

                var tree = new BlendTree { name = DriveLayerName + " Tree", blendType = BlendTreeType.Direct };
                AssetDatabase.AddObjectToAsset(tree, controller);
                var drive = controller.layers[0].stateMachine.AddState("驱动");
                // 见方法注释：Direct 树必须配 Write Defaults 开，否则 (1-Σw) 会与当前值反复混合。
                drive.writeDefaultValues = true;
                drive.motion = tree;
                PopulateDriveTree(controller, animator, tree);
                // 果冻那两个参数（Ho/JellyX · Ho/JellyY）**不再产出**：果冻已搬到独立的
                // HoSpringConstraint，直接读写形态键，不再借道 Animator 参数（见 19.2 / 20 节）。

                // 扩展点：空层 + 空片段，用户可以在这里加自己的树/耦合。应用改动时保留。
                controller.AddLayer(EditLayerName);
                var withEdit = controller.layers;
                int editIndex = withEdit.Length - 1;
                var editLayer = withEdit[editIndex];
                editLayer.defaultWeight = 1f;
                withEdit[editIndex] = editLayer;
                controller.layers = withEdit;
                var edit = editLayer.stateMachine.AddState("你的逻辑");
                edit.writeDefaultValues = true;
                var empty = new AnimationClip { name = "EDIT_THIS_Empty" };
                AssetDatabase.AddObjectToAsset(empty, controller);
                edit.motion = empty;

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                return controller;
            }
            catch { if (controller != null) AssetDatabase.DeleteAsset(assetPath); throw; }
        }

        /// <summary>
        /// 应用改动：**就地手术**。只重写 <see cref="DriveLayerName"/> 那一段，
        /// <see cref="EditLayerName"/> 与其它任何层一个字节都不动。
        ///
        /// 这是"组件 = 控制器的修改脚本"这句话的落点：反复应用不会吃掉用户的手工逻辑。
        /// </summary>
        public static void Apply(AnimatorController controller, Animator animator)
        {
            if (controller == null) throw new InvalidOperationException("先指定面部控制器。");
            if (animator == null) throw new InvalidOperationException("先指定角色 Animator。");
            var tree = FindDriveTree(controller);
            if (tree == null)
                throw new InvalidOperationException(
                    "这个控制器里没有 " + DriveLayerName + " 段，看来不是本工具初始化的。请先用「初始化控制器」产出一个。");
            PopulateDriveTree(controller, animator, tree);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        /// <summary>本工具管得着的层（带前缀，且不是用户的扩展点）。</summary>
        public static bool IsManagedLayer(string layerName) =>
            !string.IsNullOrEmpty(layerName)
            && layerName.StartsWith(LayerPrefix, StringComparison.Ordinal)
            && layerName != EditLayerName;

        private static BlendTree FindDriveTree(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.name != DriveLayerName) continue;
                foreach (var state in layer.stateMachine.states)
                    if (state.state.motion is BlendTree tree) return tree;
            }

            return null;
        }

        /// <summary>
        /// 按角色上真实存在的 ARKit 键重建 Direct 树的子节点。
        /// 片段按名字复用（模型没变时就是同一批），参数缺了就补 —— 幂等，反复应用结果一致。
        /// </summary>
        private static void PopulateDriveTree(AnimatorController controller, Animator animator, BlendTree tree)
        {
            var groups = new Dictionary<string, List<EditorCurveBinding>>(StringComparer.Ordinal);
            foreach (var mesh in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.sharedMesh == null || mesh.GetComponentInParent<Animator>() != animator) continue;
                foreach (string shape in HoFaceTrackingChannels.Names)
                {
                    if (mesh.sharedMesh.GetBlendShapeIndex(shape) < 0) continue;
                    if (!groups.TryGetValue(shape, out var list)) groups[shape] = list = new List<EditorCurveBinding>();
                    list.Add(EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(mesh.transform, animator.transform), typeof(SkinnedMeshRenderer), "blendShape." + shape));
                }
            }

            if (groups.Count == 0) throw new InvalidOperationException("Animator 子级没有匹配标准 ARKit 名称的形态键。");

            string path = AssetDatabase.GetAssetPath(controller);
            var existing = IndexClips(path);

            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in controller.parameters) parameters.Add(p.name);
            EnsureGate(controller, parameters, EyeGateName);
            EnsureGate(controller, parameters, LipGateName);
            foreach (string axis in LidAxisNames()) EnsureFloat(controller, parameters, axis);

            // 旧结构先记下来：重建后没被用上的要销毁（区域子树 + 眼睑 2D 树），
            // 否则每应用一次就多留一棵孤儿树。
            //
            // 判据是**结构**，不是名字：驱动树下面的一切都是我们生成的（用户自己的逻辑在
            // (EDIT THIS) 层里），所以旧子节点连同**它们自己的子片段**一起清 —— 而且只清
            // **本控制器文件里的子资产**（GetAssetPath == path），外部资产一根都不碰。
            // 名字不参与归属判断，于是"改名"（比如这次给六个格子换命名）不会漏清理。
            var stale = new HashSet<Object>();
            foreach (var child in tree.children)
            {
                if (child.motion == null || AssetDatabase.GetAssetPath(child.motion) != path) continue;
                stale.Add(child.motion);
                if (child.motion is BlendTree oldTree)
                    foreach (var sub in oldTree.children)
                        if (sub.motion != null && AssetDatabase.GetAssetPath(sub.motion) == path)
                            stale.Add(sub.motion);
            }

            tree.children = new ChildMotion[0];
            var keep = new HashSet<Object> { tree };
            var arkitParameters = new HashSet<string>(StringComparer.Ordinal);

            // ── 眼睑：一棵 2D 树（开合 × 眯眼），照参考实现实测的五个姿势 ──────────────
            // 「眯眼」那一格**自带 blink 90** —— 这就是它不出叠加的原因：树是插值、权重和恒为 1，
            // 合成结果永远不超过最强的那个姿势；而"每个键一个直通叶子"会相加。
            // 权重直接用区域门控（跟参考实现一样，靠共用门控隐式归组），所以不需要"恒为 1"的参数。
            // 眼睑：一棵 3×2 的 2D 树（开合 × 眯眼）—— **树交给 Kit 造**，生成器只提供它不可能知道的三件事：
            // 片段叫什么（名字要编码方阵与刻度，不能写死成 "i x j"）、这一格摆什么值、
            // 片段已存在时怎么复用（幂等：反复应用不该每次堆一堆子资产）。
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "Left" : "Right";
                if (!HasAny(groups, LidShape(suffix, "eyeBlink"), LidShape(suffix, "eyeWide"), LidShape(suffix, "eyeSquint")))
                    continue;

                var lid = HoFaceBlendTreeKit.Grid2D(controller, animator, HoFaceNaming.LidTree(side),
                    LidAxisName(side, true), LidAxisName(side, false),
                    LidGridX, LidGridY, null,
                    (clip, x, row) =>
                    {
                        keep.Add(clip);
                        WriteLidPose(clip, groups, suffix, LidPoses[x, row]);
                    },
                    (x, row) => HoFaceNaming.LidCell(side, x, LidRowStep(row)),
                    clipName => existing.TryGetValue(clipName, out var found) ? found : null);
                keep.Add(lid);
                AddChild(tree, lid, EyeGateName);
            }

            // ── 其余：按区域分两棵 Direct 子树，里面还是"一键一叶子"的直通 ─────────────
            foreach (HoFaceGate gate in new[] { HoFaceGate.Eye, HoFaceGate.Lip })
            {
                var shapes = new List<string>();
                foreach (string shape in HoFaceTrackingChannels.Names)
                    if (groups.ContainsKey(shape) && HoFaceTrackingChannels.Gate(shape) == gate && !IsLidShape(shape))
                        shapes.Add(shape);
                if (shapes.Count == 0) continue;

                var region = new BlendTree { name = RegionTreeName(gate), blendType = BlendTreeType.Direct };
                AssetDatabase.AddObjectToAsset(region, controller);
                keep.Add(region);

                foreach (string shape in shapes)
                {
                    string parameter = "ARKit/" + shape;
                    arkitParameters.Add(parameter);
                    AnimationClip clip = existing.TryGetValue(shape, out var found) ? found : CreateShapeClip(controller, shape, groups[shape]);
                    keep.Add(clip);
                    AddChild(region, clip, parameter);
                }

                AddChild(tree, region, GateParameterName(gate));
            }

            foreach (string shape in HoFaceTrackingChannels.Names)
                if (groups.ContainsKey(shape)) arkitParameters.Add("ARKit/" + shape);
            foreach (string parameter in arkitParameters)
                if (parameters.Add(parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);

            // 参数改名后要清旧的：驱动层参数现在统一收在 Ho/Drive 下（门控 + 眼睑两根轴），
            // 历史名字（Ho/Gate/*、Ho/LidLeft.X|Y，以及已撤销的 Ho/JellyX|Y）留着只会变成
            // 没人写也没人读的僵尸参数。只动 Ho/ 命名空间 —— ARKit/ 是输入通道，不碰。
            var wanted = new HashSet<string>(StringComparer.Ordinal) { EyeGateName, LipGateName };
            foreach (string axis in LidAxisNames()) wanted.Add(axis);
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
            {
                string name = controller.parameters[i].name;
                if (name.StartsWith(LayerPrefix, StringComparison.Ordinal) && !wanted.Contains(name))
                    controller.RemoveParameter(i);
            }

            foreach (var old in stale)
                if (!keep.Contains(old)) UnityEngine.Object.DestroyImmediate(old, true);

            // 已经不再被任何树引用的旧直通片段（眼睑那六个键）——它们是我们的命名约定，
            // 清掉免得子资产越堆越多。
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip) || keep.Contains(clip)) continue;
                if (Array.IndexOf(HoFaceTrackingChannels.Names, clip.name) < 0) continue;
                UnityEngine.Object.DestroyImmediate(clip, true);
            }
        }

        /// <summary>眼睑方阵 X 轴上的三个格点：睁大(−1) / 中性(0) / 闭(+1)。</summary>
        private static readonly float[] LidGridX = { -1f, 0f, 1f };

        /// <summary>
        /// 眼睑方阵 Y 轴上**摆了姿势的**两个格点：不眯(0) / 眯满(+1)。
        /// 中间那档（半眯 0.5）没摆 —— 方阵不必铺满，空着的那行交给树插值。
        /// </summary>
        private static readonly float[] LidGridY = { 0f, 1f };

        /// <summary>
        /// 摆了的第 <paramref name="row"/> 行在方阵里是第几刻度。**第 1 行是 `Y2` 而不是 `Y1`** ——
        /// Y 轴三档里中间那档（半眯）没摆姿势，所以第二个摆了的格点落在刻度 2 上。名字必须说真话。
        /// </summary>
        private static int LidRowStep(int row) => row == 0 ? 0 : 2;

        /// <summary>
        /// 眼睑方阵里摆了的六格：<c>LidPoses[X 刻度 0..2, 行 0..1]</c>
        /// （行 0 = `Y0` 不眯、行 1 = `Y2` 眯满；`Y1` 半眯没摆）。
        ///
        /// 前五格的数值来自参考实现五个片段的实测值（见文档 21.1）；**`A3X2Y2` 闭+眯 是我们补的** ——
        /// 参考实现没有这一格，但它的参数范围可能让那个角到不了，而我们的两根轴是独立参数、**真的会到**
        /// （"眨满 + 眯眼"就是过眨眼的工况）。这一格的含义是**眨满时眯眼还剩多少** —— 一次纯粹的艺术决定：
        /// 默认 `blink 100 + squint 0`，两键加和正好 100，不再过闭合。
        /// </summary>
        private static readonly (float Blink, float Wide, float Squint)[,] LidPoses =
        {
            { (0f, 100f, 0f), (0f, 100f, 100f) },   // X0：睁大 / 睁大+眯
            { (0f, 0f, 0f), (90f, 0f, 100f) },      // X1：中性 / 眯（眯自带 blink 90）
            { (100f, 0f, 0f), (100f, 0f, 0f) }      // X2：闭 / 闭+眯
        };

        /// <summary>眼睑的三/六个键归 2D 树管，不再作为直通叶子。</summary>
        private static bool IsLidShape(string shape) =>
            shape == "eyeBlinkLeft" || shape == "eyeBlinkRight" || shape == "eyeWideLeft" || shape == "eyeWideRight"
            || shape == "eyeSquintLeft" || shape == "eyeSquintRight";

        private static string LidShape(string suffix, string prefix) => prefix + suffix;

        private static string LidTreeName(int side) => HoFaceNaming.LidTree(side);

        /// <summary>眼睑 2D 的两根轴：开合（-1 睁大 / +1 闭）与眯眼（0~1）。由会话生产。</summary>
        public static string LidAxisName(int side, bool horizontal) => HoFaceNaming.LidAxis(side, horizontal);

        private static IEnumerable<string> LidAxisNames()
        {
            for (int side = 0; side < 2; side++)
            {
                yield return LidAxisName(side, true);
                yield return LidAxisName(side, false);
            }
        }

        private static bool HasAny(Dictionary<string, List<EditorCurveBinding>> groups, params string[] shapes)
        {
            foreach (string shape in shapes)
                if (groups.ContainsKey(shape)) return true;
            return false;
        }

        private static void WriteLidPose(AnimationClip clip, Dictionary<string, List<EditorCurveBinding>> groups,
            string suffix, (float Blink, float Wide, float Squint) pose)
        {
            WriteShape(clip, groups, "eyeBlink" + suffix, pose.Blink);
            WriteShape(clip, groups, "eyeWide" + suffix, pose.Wide);
            WriteShape(clip, groups, "eyeSquint" + suffix, pose.Squint);
        }

        private static void WriteShape(AnimationClip clip, Dictionary<string, List<EditorCurveBinding>> groups, string shape, float value)
        {
            if (!groups.TryGetValue(shape, out var bindings)) return;
            foreach (var binding in bindings)
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        private static AnimationClip CreateShapeClip(AnimatorController controller, string shape, List<EditorCurveBinding> bindings)
        {
            var clip = new AnimationClip { name = shape, frameRate = 60f };
            foreach (var binding in bindings)
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, 100f));
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        private static void AddChild(BlendTree tree, Motion motion, string parameter)
        {
            tree.AddChild(motion);
            var children = tree.children;
            children[children.Length - 1].directBlendParameter = parameter;
            tree.children = children;
        }

        private static Dictionary<string, AnimationClip> IndexClips(string path)
        {
            var existing = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && clip.name != "EDIT_THIS_Empty") existing[clip.name] = clip;
            return existing;
        }

        private static void EnsureFloat(AnimatorController controller, HashSet<string> parameters, string name)
        {
            if (parameters.Add(name)) controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        /// <summary>门控/轴参数：缺就补，并且**默认 1**（单独打开这个资产时不是一片死脸）。</summary>
        private static void EnsureGate(AnimatorController controller, HashSet<string> parameters, string name)
        {
            if (!parameters.Add(name)) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
            var all = controller.parameters;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != name) continue;
                all[i].defaultFloat = 1.0f;
                controller.parameters = all;
                return;
            }
        }
    }
}
