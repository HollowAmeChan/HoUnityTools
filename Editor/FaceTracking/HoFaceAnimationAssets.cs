using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>控制器资产的实况（面板的「控制器结构」就是这么读出来的，不是从配置推断）。</summary>
    public sealed class HoFaceAssetInfo
    {
        public int layers, states, clips, blendCurves, otherCurves;
        /// <summary>控制器会写的形态键里，**列出的驱动对象身上真的存在**的那些。</summary>
        public readonly List<string> shapes = new List<string>();
        /// <summary>控制器会写、但列出的驱动对象身上没有的键 —— 这些格子在模型上是空的。</summary>
        public readonly List<string> missing = new List<string>();
        /// <summary>不是形态键的曲线条数（源里带过来的层级/属性动画，按原路径保留）。</summary>
        public readonly List<string> otherKinds = new List<string>();
    }

    /// <summary>
    /// 面捕控制器资产的**搬运**：把一份现成的混合树文件搬到角色上。
    ///
    /// 这里已经没有"生成树"这件事了 —— 控制器是**作品**（Jerry 的 vrc-common、我们自己编的 ho-2d-test1…），
    /// 它在 Unity 的混合树编辑器里被编出来，自带片段与动画。初始化唯一要做的处理是
    /// **把动画驱动的对象换成这台角色的网格**（<see cref="Adopt"/>）：
    ///
    /// <list type="number">
    /// <item>整份文件复制到目标路径（层、状态、参数、子树、子片段全跟着走）；</item>
    /// <item>把每条形态键曲线重绑到"驱动对象列表"里真正有这个键的网格上；</item>
    /// <item>源里引用的**外部**片段先复制成本文件的子资产再改 —— 绝不改动别人共享的 .anim。</item>
    /// </list>
    ///
    /// **为什么不再有模板/规格那套东西**：那等于在代码里重新发明一遍混合树编辑器，还要把
    /// "轴的值怎么算"这种中间层的事塞进控制器。控制器不需要声明它吃什么样的参数，它只等着被喂
    /// （见 docs/FACE_TRACKING_WORKFLOW.md）。
    /// </summary>
    public static class HoFaceAnimationAssets
    {
        public static bool Allowed(HoFaceTrackingDebugger rig, string shape)
        {
            if ((rig.outputRegions & HoFaceTrackingChannels.Region(shape)) == 0) return false;
            foreach (var channel in rig.channels)
                if (channel != null && channel.shape == shape) return channel.mode != HoFaceInputMode.Release;
            return false;
        }

        /// <summary>
        /// 复制一份源控制器到目标路径，并把它的形态键曲线重绑到 <paramref name="meshes"/> 上。
        /// 目标文件已存在时**保留它的 GUID**（外部引用，比如窥视对象的 Animator，不会断）。
        /// 源与目标是同一个文件时跳过复制，就地重绑。
        /// </summary>
        public static AnimatorController Adopt(RuntimeAnimatorController source, string targetPath,
            IList<SkinnedMeshRenderer> meshes, Animator root, bool overwrite = false)
        {
            if (source == null) throw new InvalidOperationException("请先指定源控制器 —— 要搬运的那份混合树文件。");
            if (root == null) throw new InvalidOperationException("请先指定角色 Animator。");
            if (string.IsNullOrEmpty(targetPath)) throw new InvalidOperationException("目标路径不能为空。");
            if (!(source is AnimatorController))
                throw new InvalidOperationException("源控制器必须是纯 Unity AnimatorController（不接受 OverrideController）。");
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException("源控制器必须是工程里的资产。");

            if (!string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
            {
                bool exists = AssetDatabase.LoadMainAssetAtPath(targetPath) != null;
                if (exists && !overwrite) throw new InvalidOperationException("目标资产已存在，请换路径或确认覆盖。");
                string guid = exists ? AssetDatabase.AssetPathToGUID(targetPath) : null;
                if (exists) AssetDatabase.DeleteAsset(targetPath);
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                    throw new InvalidOperationException("复制失败：" + sourcePath + " → " + targetPath);
                if (!string.IsNullOrEmpty(guid)) RestoreGuid(targetPath, guid);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetPath);
            if (controller == null) throw new InvalidOperationException("复制出来的文件不是 AnimatorController：" + targetPath);
            Retarget(controller, meshes, root);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// 把控制器里所有形态键曲线重绑到驱动对象列表上。**幂等**：反复调用结果一致。
        ///
        /// 规则只有一条：曲线写某个键 → 列出的网格里谁有这个键，就写到谁身上（可能有多个）。
        /// **列出的网格里谁都没有这个键时，那条曲线原样留着**（不写、也不删）—— 那是"这台模型没做
        /// 这个键"，不是错误；作者摆的那一格数据要留住，以后把网格加进驱动对象列表再重绑就能接上。
        /// 于是 <see cref="Inspect"/> 能如实报出"哪些格子现在落不到任何网格上"。
        /// 非形态键曲线（层级/属性动画）同样按原路径保留。
        /// </summary>
        public static void Retarget(AnimatorController controller, IList<SkinnedMeshRenderer> meshes, Animator root)
        {
            if (controller == null) throw new InvalidOperationException("先指定面部控制器。");
            if (root == null) throw new InvalidOperationException("先指定角色 Animator。");

            var owners = ShapeOwners(meshes, root);
            if (owners.Count == 0)
                throw new InvalidOperationException("驱动对象列表里一个形态键都没有 —— 先在上面把要驱动的网格加进来。");

            string path = AssetDatabase.GetAssetPath(controller);
            var replace = new Dictionary<AnimationClip, AnimationClip>();
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip == null) continue;
                AnimationClip target = clip;
                // 外部片段（别人的 .anim）必须**先复制成本文件的子资产再改**，否则就是在改共享资产。
                if (!string.Equals(AssetDatabase.GetAssetPath(clip), path, StringComparison.Ordinal))
                {
                    if (replace.ContainsKey(clip)) continue;
                    target = Object.Instantiate(clip);
                    target.name = clip.name;
                    target.hideFlags = HideFlags.HideAndDontSave;
                    AssetDatabase.AddObjectToAsset(target, controller);
                    replace[clip] = target;
                }

                RewriteClip(target, owners);
            }

            if (replace.Count > 0) Repoint(controller, replace);
        }

        /// <summary>驱动对象列表 → 键名 → (网格, 下标, 相对 Animator 根的路径)。</summary>
        private static Dictionary<string, List<(SkinnedMeshRenderer mesh, int index, string path)>> ShapeOwners(
            IList<SkinnedMeshRenderer> meshes, Animator root)
        {
            var owners = new Dictionary<string, List<(SkinnedMeshRenderer, int, string)>>(StringComparer.Ordinal);
            if (meshes == null) return owners;
            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;
                if (mesh.transform != root.transform && !mesh.transform.IsChildOf(root.transform))
                    throw new InvalidOperationException("驱动对象必须在角色 Animator 之下：" + mesh.name);
                if (mesh.sharedMesh == null) continue;
                string path = AnimationUtility.CalculateTransformPath(mesh.transform, root.transform);
                for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                {
                    string shape = mesh.sharedMesh.GetBlendShapeName(i);
                    if (!owners.TryGetValue(shape, out var list)) owners[shape] = list = new List<(SkinnedMeshRenderer, int, string)>();
                    list.Add((mesh, i, path));
                }
            }

            return owners;
        }

        /// <summary>把一条片段里"有归属"的形态键曲线摊到驱动对象上；没归属的与其余曲线一个字节不动。</summary>
        private static void RewriteClip(AnimationClip clip,
            Dictionary<string, List<(SkinnedMeshRenderer mesh, int index, string path)>> owners)
        {
            var curves = new List<(EditorCurveBinding binding, AnimationCurve curve, string shape,
                List<(SkinnedMeshRenderer mesh, int index, string path)> owners)>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) continue;
                string shape = binding.propertyName.Substring("blendShape.".Length);
                if (!owners.TryGetValue(shape, out var list)) continue;   // 这台模型没这个键：原样留着
                curves.Add((binding, AnimationUtility.GetEditorCurve(clip, binding), shape, list));
            }

            if (curves.Count == 0) return;
            foreach (var pair in curves) AnimationUtility.SetEditorCurve(clip, pair.binding, null);
            foreach (var pair in curves)
                foreach (var owner in pair.owners)
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(owner.path, typeof(SkinnedMeshRenderer), "blendShape." + pair.shape),
                        pair.curve);
        }

        /// <summary>把树/状态里指向外部片段的引用换成我们复制出来的那份。</summary>
        private static void Repoint(AnimatorController controller, Dictionary<AnimationClip, AnimationClip> replace)
        {
            foreach (var layer in controller.layers) RepointMachine(layer.stateMachine, replace);
        }

        private static void RepointMachine(AnimatorStateMachine machine, Dictionary<AnimationClip, AnimationClip> replace)
        {
            if (machine == null) return;
            foreach (var child in machine.states) child.state.motion = RepointMotion(child.state.motion, replace);
            foreach (var child in machine.stateMachines) RepointMachine(child.stateMachine, replace);
        }

        private static Motion RepointMotion(Motion motion, Dictionary<AnimationClip, AnimationClip> replace)
        {
            if (motion is BlendTree tree)
            {
                var children = tree.children;
                for (int i = 0; i < children.Length; i++) children[i].motion = RepointMotion(children[i].motion, replace);
                tree.children = children;
                return tree;
            }

            if (motion is AnimationClip clip && replace.TryGetValue(clip, out var found)) return found;
            return motion;
        }

        /// <summary>
        /// 把 <paramref name="guid"/> 塞回刚复制出来的 .meta：不这么做的话 CopyAsset 会给文件一个新 GUID，
        /// 场景里引用过这个控制器的地方（窥视对象的 Animator）就全断了。
        /// </summary>
        private static void RestoreGuid(string assetPath, string guid)
        {
            string meta = assetPath + ".meta";
            if (!File.Exists(meta)) return;
            string current = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(current) || current == guid) return;
            File.WriteAllText(meta, File.ReadAllText(meta).Replace("guid: " + current, "guid: " + guid));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// 控制器资产的实况：几层几个状态、会写哪些键、哪些键**这台模型没有**
        /// （曲线留着没删，但那些格子落不到任何网格上）。
        /// </summary>
        public static HoFaceAssetInfo Inspect(RuntimeAnimatorController controller, IList<SkinnedMeshRenderer> meshes)
        {
            var info = new HoFaceAssetInfo();
            if (!(controller is AnimatorController animator)) return info;

            info.layers = animator.layers.Length;
            foreach (var layer in animator.layers) CountStates(layer.stateMachine, ref info.states);

            var onMesh = new HashSet<string>(StringComparer.Ordinal);
            if (meshes != null)
                foreach (var mesh in meshes)
                {
                    if (mesh == null || mesh.sharedMesh == null) continue;
                    for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++) onMesh.Add(mesh.sharedMesh.GetBlendShapeName(i));
                }

            var shapes = new HashSet<string>(StringComparer.Ordinal);
            var missing = new HashSet<string>(StringComparer.Ordinal);
            var other = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<AnimationClip>();
            foreach (AnimationClip clip in animator.animationClips)
            {
                if (clip == null || !seen.Add(clip)) continue;
                info.clips++;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(SkinnedMeshRenderer)
                        && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        info.blendCurves++;
                        string shape = binding.propertyName.Substring("blendShape.".Length);
                        (onMesh.Contains(shape) ? shapes : missing).Add(shape);
                    }
                    else
                    {
                        info.otherCurves++;
                        other.Add(binding.type.Name + "." + binding.propertyName);
                    }
                }
            }

            info.shapes.AddRange(shapes.OrderBy(s => s, StringComparer.Ordinal));
            info.missing.AddRange(missing.OrderBy(s => s, StringComparer.Ordinal));
            info.otherKinds.AddRange(other.OrderBy(s => s, StringComparer.Ordinal));
            return info;
        }

        private static void CountStates(AnimatorStateMachine machine, ref int count)
        {
            if (machine == null) return;
            count += machine.states.Length;
            foreach (var child in machine.stateMachines) CountStates(child.stateMachine, ref count);
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
                        // 只收我们有通道、且这个区域被打开的标准 ARKit 键。控制器是搬来的，可能还写了
                        // 别的键（别人的模型专有键），那些不归我们驱动 —— 不猜、也不写。
                        if (!Allowed(rig, shape) || (outputFilter != null && !outputFilter(shape))) continue;
                        string path = curve.path;
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
    }
}
