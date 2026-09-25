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

    /// <summary>
    /// 模板里的一个**动画槽位**：树/状态引用的那个片段，以及它最后被填成了什么。
    /// 「槽位名」就是模板里那个片段的名字；装配时按这个名字去动画文件夹找同名 `.anim`。
    /// </summary>
    public sealed class HoFaceClipSlot
    {
        /// <summary>槽位名（模板里那个片段的名字）。</summary>
        public string name;
        /// <summary>文件夹里同名的片段。找到就**由它顶替**模板自带的那份。</summary>
        public AnimationClip fromFolder;
        /// <summary>模板自带的片段（可能只是一份写着键的占位）。</summary>
        public AnimationClip fromTemplate;
        /// <summary>被引用了几处（同一个姿势片段常被多棵树共用）。</summary>
        public int uses;

        public AnimationClip Resolved => fromFolder != null ? fromFolder : fromTemplate;

        /// <summary>这份槽位最终会不会真的写形态键。</summary>
        public bool Writes => Resolved != null && HoFaceAnimationAssets.HasShapeCurves(Resolved);

        /// <summary>面板上的一句话状态。</summary>
        public string Status => fromFolder != null ? "文件夹" : Writes ? "模板自带" : "缺";
    }

    /// <summary>控制器资产的实况（「控制器编辑」页的「详情」栏就是这么读出来的，不是从配置推断）。</summary>
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
    /// 面捕控制器的**装配**：模板（树形）+ 动画文件夹（数据）+ 驱动对象（写谁）→ 一份属于这台角色的控制器。
    ///
    /// 三件事分开是有意的：
    /// <list type="bullet">
    /// <item><b>混合树模板</b> = 一份完整 `.controller`：树形、坐标、门控、参数都在里面，作者在混合树编辑器里编。
    /// 它引用的每个片段就是一个**槽位**，按名字对应动画文件夹里的一份 `.anim`。</item>
    /// <item><b>动画文件夹</b> = 现成的片段（`<键名>.anim` 之类）。**它跟模型无关** —— 装配时按形态键名重绑，
    /// 所以同一份动画可以用在任何模型上。文件夹里没有的槽位就保留模板自带的那份。</item>
    /// <item><b>驱动对象</b> = 这台角色上要驱动的网格。</item>
    /// </list>
    ///
    /// <see cref="Adopt"/> 就是这三样合起来：
    /// <list type="number">
    /// <item>整份模板复制到目标路径（层、状态、参数、子树、子片段全跟着走）；</item>
    /// <item>按槽位名把文件夹里的片段填进去，剩下的槽位用模板自带的；</item>
    /// <item>把每条形态键曲线重绑到驱动对象里真正有这个键的网格上；</item>
    /// <item>外部片段（别人的 `.anim`）先复制成本文件的子资产再改 —— 绝不改动共享资产。</item>
    /// </list>
    ///
    /// **代码不生成树、也不生成姿势**：树是模板作者的，姿势是动画作者的，这里只做装配。
    /// </summary>
    public static class HoFaceAnimationAssets
    {
        public static bool Allowed(HoFaceDebugSettings settings, string shape)
        {
            // 这里**不看区域开关**了：哪些键算数由使用者自己的混合树决定（我们不注入门控）。
            foreach (var channel in settings.channels)
                if (channel != null && channel.shape == shape) return channel.mode != HoFaceInputMode.Release;
            return false;
        }

        /// <summary>
        /// 复制模板到目标路径，填入动画文件夹里的片段，再把形态键曲线重绑到 <paramref name="meshes"/> 上。
        /// 目标文件已存在时**保留它的 GUID**（外部引用，比如窥视对象的 Animator，不会断）。
        /// 模板与目标是同一个文件时跳过复制，就地填与重绑。
        /// </summary>
        public static AnimatorController Adopt(RuntimeAnimatorController template, string targetPath,
            IList<SkinnedMeshRenderer> meshes, Animator root, string animationFolder = null, bool overwrite = false)
        {
            if (template == null) throw new InvalidOperationException("请先指定混合树模板。");
            if (root == null) throw new InvalidOperationException("请先指定角色 Animator。");
            if (string.IsNullOrEmpty(targetPath)) throw new InvalidOperationException("目标路径不能为空。");
            if (!(template is AnimatorController))
                throw new InvalidOperationException("模板必须是纯 Unity AnimatorController（不接受 OverrideController）。");
            string sourcePath = AssetDatabase.GetAssetPath(template);
            if (string.IsNullOrEmpty(sourcePath))
                throw new InvalidOperationException("模板必须是工程里的资产。");

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
            ResolveClips(controller, animationFolder);
            Retarget(controller, meshes, root);
            PruneUnusedClips(controller, targetPath);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// 控制器里的所有槽位，以及每个槽位在"这份文件夹 + 这份控制器"下会被填成什么。
        /// 「控制器编辑」页的「详情」栏读的就是它 —— **从资产读，不是从配置推断**。
        /// </summary>
        public static List<HoFaceClipSlot> Slots(RuntimeAnimatorController template, string animationFolder)
        {
            var slots = new List<HoFaceClipSlot>();
            if (!(template is AnimatorController controller)) return slots;
            var folder = FolderIndex(animationFolder);
            var at = new Dictionary<string, HoFaceClipSlot>(StringComparer.Ordinal);
            ForEachClip(controller, clip =>
            {
                if (!at.TryGetValue(clip.name, out var slot))
                {
                    folder.TryGetValue(clip.name, out var fromFolder);
                    at[clip.name] = slot = new HoFaceClipSlot { name = clip.name, fromFolder = fromFolder, fromTemplate = clip };
                    slots.Add(slot);
                }

                slot.uses++;
                return clip;
            });
            return slots;
        }

        /// <summary>按槽位名把动画文件夹里的片段填进树与状态。返回填了几个槽位。</summary>
        public static int ResolveClips(AnimatorController controller, string animationFolder)
        {
            if (controller == null) return 0;
            var folder = FolderIndex(animationFolder);
            if (folder.Count == 0) return 0;
            var filled = new HashSet<string>(StringComparer.Ordinal);
            ForEachClip(controller, clip =>
            {
                if (!folder.TryGetValue(clip.name, out var replacement) || replacement == null || replacement == clip) return clip;
                filled.Add(clip.name);
                return replacement;
            });
            return filled.Count;
        }

        /// <summary>动画文件夹的索引：文件名去扩展名优先，片段名兜底（键名里有 `/` 之类时文件名会被改写）。</summary>
        private static Dictionary<string, AnimationClip> FolderIndex(string animationFolder)
        {
            var index = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(animationFolder) || !AssetDatabase.IsValidFolder(animationFolder)) return index;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { animationFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;
                index[Path.GetFileNameWithoutExtension(path)] = clip;
                if (!index.ContainsKey(clip.name)) index[clip.name] = clip;
            }

            return index;
        }

        /// <summary>一条片段里有没有形态键曲线（没有的话，这个槽位填了也什么都不写）。</summary>
        public static bool HasShapeCurves(AnimationClip clip)
        {
            if (clip == null) return false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type == typeof(SkinnedMeshRenderer)
                    && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 树与状态里引用着的每个片段都过一遍 <paramref name="resolve"/>，返回替换后的引用。
        /// 填动画、换副本、清理都走这一个遍历，免得三处各写一遍递归。
        /// </summary>
        private static void ForEachClip(AnimatorController controller, Func<AnimationClip, AnimationClip> resolve)
        {
            foreach (var layer in controller.layers) ForEachClip(layer.stateMachine, resolve);
        }

        private static void ForEachClip(AnimatorStateMachine machine, Func<AnimationClip, AnimationClip> resolve)
        {
            if (machine == null) return;
            foreach (var child in machine.states) child.state.motion = ForEachClip(child.state.motion, resolve);
            foreach (var child in machine.stateMachines) ForEachClip(child.stateMachine, resolve);
        }

        private static Motion ForEachClip(Motion motion, Func<AnimationClip, AnimationClip> resolve)
        {
            if (motion is BlendTree tree)
            {
                var children = tree.children;
                for (int i = 0; i < children.Length; i++) children[i].motion = ForEachClip(children[i].motion, resolve);
                tree.children = children;
                return tree;
            }

            if (motion is AnimationClip clip) return resolve(clip);
            return motion;
        }

        /// <summary>填完动画后，模板里那些**已经没人引用**的片段不必跟着走（只删本文件的子资产）。</summary>
        private static void PruneUnusedClips(AnimatorController controller, string path)
        {
            var used = new HashSet<AnimationClip>();
            ForEachClip(controller, clip => { used.Add(clip); return clip; });
            var dead = new List<Object>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !used.Contains(clip)) dead.Add(clip);
            foreach (var asset in dead) Object.DestroyImmediate(asset, true);
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
                    // 复制出来的这份要**作为子资产落进本文件**，所以不能带 HideAndDontSave
                    //（Unity 会报 "kDontSaveInEditor … persistent" 断言）。
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
        private static void Repoint(AnimatorController controller, Dictionary<AnimationClip, AnimationClip> replace) =>
            ForEachClip(controller, clip => replace.TryGetValue(clip, out var found) ? found : clip);

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

        public static HoFaceCompiledController Compile(HoFaceDebugSettings settings, Func<string, bool> outputFilter = null)
        {
            if (settings.TargetAnimator() == null) throw new InvalidOperationException("请指定角色 Animator。");
            if (!(settings.FaceController() is AnimatorController source))
                throw new InvalidOperationException("请选择纯 Unity AnimatorController。首版不接 VRC 行为或嵌套 OverrideController。");
            foreach (var layer in source.layers)
            {
                if (layer.syncedLayerIndex >= 0) throw new InvalidOperationException("首版暂不支持同步图层。");
                ValidateMachine(layer.stateMachine);
            }
            var parameters = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            foreach (var p in source.parameters) parameters[p.name] = p.type;
            var usedShapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in settings.channels)
            {
                if (channel == null || HoFaceTrackingChannels.IndexOf(channel.shape) < 0)
                    throw new InvalidOperationException("输入通道必须使用标准 ARKit 键名。");
                if (!usedShapes.Add(channel.shape)) throw new InvalidOperationException("重复的输入通道：" + channel.shape);
            }

            // 参数名现在由**中间层配置**声明（控制器只是等着被喂）。这里只做实事的检查：
            // 同名参数被两行写 = 后写者覆盖前者（说出来，不拦）；类型不是 Float 时 SetFloat 会失败。
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var output in settings.Outputs())
            {
                if (output == null || string.IsNullOrWhiteSpace(output.parameter)) continue;
                if (!written.Add(output.parameter))
                    Debug.LogWarning("[Ho 面捕] 中间层里有两行写同一个参数：" + output.parameter + "（后一行会覆盖前一行）");
                if (parameters.TryGetValue(output.parameter, out var type) && type != AnimatorControllerParameterType.Float)
                    Debug.LogWarning("[Ho 面捕] 参数不是 Float，写不进去：" + output.parameter);
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
                        if (!Allowed(settings, shape) || (outputFilter != null && !outputFilter(shape))) continue;
                        string path = curve.path;
                        Transform node = path.Length == 0 ? settings.TargetAnimator().transform : settings.TargetAnimator().transform.Find(path);
                        var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                        {
                            string warning = path + " / " + shape + "：未找到绑定";
                            if (!result.warnings.Contains(warning)) result.warnings.Add(warning);
                            continue;
                        }
                        if (renderer.GetComponentInParent<Animator>() != settings.TargetAnimator())
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
            // ⚠️ **任何 `StateMachineBehaviour` 都不接受**（状态机上的、状态上的，一视同仁）。
            //
            // 为什么：控制器的 Behaviour 会在**影子台**上真的跑起来，而影子台只是一台
            // "照原样跑出一帧姿态"的机器 —— 别人的 Behaviour 可能建对象、改全局、读盘，那些副作用
            // 我们既没承诺过也不想要。
            //
            // ⚠️ **曾经放开过唯一一个**（2026-09-26 上午 → 下午就删了）：我们自己的「语义写手」
            // `HoFaceSemanticWriterBehaviour`（挂在状态上、按表达式把 Animator 参数写进影子 Hub）。
            // 删掉它的理由：那些值**中间层本来就算得出来**（它就是写参数的那个人），让控制器再算一遍
            // = 两份真相 + 一个只在 bundle 里跑、编辑器里看不见的写者。现在中间层算完直接写角色上的 Hub
            // （Unity 侧 `HoFaceAnimationSession.PublishSemantics`，Warudo 侧「HoFace写动态参数」节点）。
            // 于是这条校验又回到"一律拒绝"，也不再需要"只放过某一个"这种例外。
            if (machine.behaviours.Length != 0)
                throw new InvalidOperationException("不支持 StateMachineBehaviour（状态机上）：" + machine.name
                    + " —— 面捕的树只有「参数 + 树」，不要挂行为（见 docs/FACE_TRACKING_DYNAMIC_PARAMETERS.md §5）");
            foreach (var child in machine.states)
            {
                foreach (var behaviour in child.state.behaviours)
                    if (behaviour != null)
                        throw new InvalidOperationException("面部状态不能带 Behaviour：" + child.state.name
                            + " / " + behaviour.GetType().Name
                            + "（面捕的树只有「参数 + 树」；动态参数由中间层算完直接写 Hub）");
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
