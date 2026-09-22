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
                        if (!Allowed(rig, shape) || (outputFilter != null && !outputFilter(shape))) continue;
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
        /// <summary>果冻那个"带物理的参数"的名字（由 C# 生产，生成的控制器里会先建出来）。</summary>
        public const string JellyParameterName = "Ho/Jelly";

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
                // 果冻参数先建出来（暂时还没有东西消费它）：下一步的果冻动画层会按它取姿势，
                // 在那之前它至少可以被写、被观察，整条链路是通的。
                controller.AddParameter(JellyParameterName, AnimatorControllerParameterType.Float);

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

            // 复用已有片段：这样反复应用不会把子资产越堆越多。
            var existing = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            string path = AssetDatabase.GetAssetPath(controller);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && clip.name != "EDIT_THIS_Empty") existing[clip.name] = clip;

            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in controller.parameters) parameters.Add(p.name);

            tree.children = new ChildMotion[0];
            foreach (var pair in groups)
            {
                string parameter = "ARKit/" + pair.Key;
                if (parameters.Add(parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);

                if (!existing.TryGetValue(pair.Key, out var clip))
                {
                    // 一个键只需要一个"满值"片段：权重由参数给，参数为 0 时它贡献 0。
                    clip = new AnimationClip { name = pair.Key, frameRate = 60f };
                    foreach (var binding in pair.Value)
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, 100f));
                    AssetDatabase.AddObjectToAsset(clip, controller);
                }

                tree.AddChild(clip);
                var children = tree.children;
                children[children.Length - 1].directBlendParameter = parameter;
                tree.children = children;
            }
        }
    }
}
