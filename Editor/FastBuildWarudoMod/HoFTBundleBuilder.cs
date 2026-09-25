#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    /// <summary>
    /// FastBuild Warudo Mod —— **HoFT 页**：把一份控制器打成 Warudo 能读的 AssetBundle。
    ///
    /// 【为什么需要这个】
    /// Warudo 的「HoFace控制求解」节点要跑的是**真的 `AnimatorController`**（混合树那套），
    /// 可运行时读不了 `.controller`（编辑器格式，靠 GUID/fileID 引用别的资源）。唯一能装它的容器是
    /// **AssetBundle**，而且包里必须带**控制器绑定的那套 rig** —— 运行时**枚举不了一个 `AnimationClip`
    /// 的绑定**（`AnimationUtility` 是编辑器专属），所以没有原配层级就采不到任何输出。
    ///
    /// 【它怎么知道要打哪套 rig】
    /// **主要靠用户指定**（那一栏就是干这个的）：控制器驱动的预制体是哪个，作者最清楚。
    /// 指定的同时会做一次**绑定校验** —— 把每条曲线的 `path` 在这份预制体里 `Transform.Find` 一遍，
    /// 解析不到的**点名列出来**。这一步值钱，因为运行时 `GetBlendShapeWeight` 是按名字取权重的：
    /// 控制器写了 `blendShape.foo` 而网格上没有 `foo`，Unity **不报错**，那一格永远 0。
    /// 留空时才退回"扫绑定反推"：把每条 clip 的绑定路径第一段当 rig 根，去找它。
    /// 这条路只在控制器与 rig 恰好放在一起时才对，而且一条都解析不到时会静默变成空 bundle。
    /// ⚠️ 两种方式都**验不了形态键名字**（打包时能枚举，但我不做：那要读网格的 blendShape，
    /// 而真正的运行时判据在 Warudo 侧的自检行 `shapes[…]` 里）。
    ///
    /// 【为什么把 rig 复制出来一份再打】
    /// 直接打**场景里的对象**是打不了的 —— `BuildAssetBundle` 只收**资产路径**（`assets/...`）。
    /// 所以先把绑到的 rig 预制体 `Instantiate` 一份到临时目录（`PrefabUtility.SaveAsPrefabAsset`），
    /// 打完删掉。**不动用户原来的预制体**（它的 GUID/内容都不变）。
    ///
    /// 【⚠️ 版本绑定】AssetBundle 只在**与播放器同版本**的编辑器里打得开。Warudo 本体是
    /// **2021.3.45f2**，所以这个页面必须在那个编辑器里用。版本不对会在 Warudo 侧报"打不开 bundle"。
    /// </summary>
    internal static class HoFTBundleBuilder
    {
        /// <summary>临时 rig 预制体的落点（打完就删；放包外，别脏了工程）。</summary>
        private const string TempFolder = "Assets/HoFTBundleTemp";

        /// <summary>一次打包的结果（不管成没成，都带人能看懂的原因）。</summary>
        internal sealed class Result
        {
            public bool ok;
            public string message;
            public string bundlePath;
            public string rigPath;
            public int clipCount;
            public readonly List<string> warnings = new List<string>();
        }

        /// <summary>
        /// 打一个 bundle。`outputDirectory` 可以是工程外（绝对路径）—— Warudo 的插件沙箱就在工程外。
        ///
        /// <paramref name="rigPrefab"/> = **这个控制器真正驱动的那套预制体**。
        ///   · 给了（正常情况）：它就是打进 bundle 的 rig，同时用来**校验**每条绑定的路径能不能解析到；
        ///   · 留空：退回"扫控制器里每条 clip 的绑定、反推 rig"。这条路只在控制器与 rig 恰好放在一起时才对，
        ///     而且一条绑定都解析不到时会**静默变成空 bundle** —— 所以**优先让用户选**。
        /// </summary>
        internal static Result Build(RuntimeAnimatorController controller, GameObject rigPrefab,
            string outputDirectory, string bundleFileName)
        {
            var result = new Result();

            if (controller == null)
            {
                result.message = "没选控制器。";
                return result;
            }
            if (controller is AnimatorOverrideController)
            {
                result.message = "这是 AnimatorOverrideController —— 不接。请用纯 Unity AnimatorController"
                    + "（OverrideController 要它底下的那份真控制器才能枚举 clip）。";
                return result;
            }
            if (string.IsNullOrEmpty(outputDirectory))
            {
                result.message = "没指定输出目录。";
                return result;
            }
            if (string.IsNullOrEmpty(bundleFileName))
            {
                result.message = "没填 bundle 文件名。";
                return result;
            }

            var animator = controller as AnimatorController;
            if (animator == null)
            {
                result.message = "控制器不是纯 Unity AnimatorController（拿不到它的层与 clip）。";
                return result;
            }

            var clips = Clips(animator);
            result.clipCount = clips.Count;
            if (clips.Count == 0)
            {
                result.message = "这份控制器里一条动画片段都没有 —— 打出去也采不到任何东西。"
                    + "先在工作区里把片段填进混合树/状态（「面捕 / 控制器编辑」那一页做这件事）。";
                return result;
            }
            if (clips.Count == 1 && clips[0] == null)
            {
                result.message = "控制器有动画状态，但 motion 不是 AnimationClip（可能是空的混合树）。";
                return result;
            }

            // ── ① 定 rig ──────────────────────────────────────────────────────────
            GameObject rigSource = rigPrefab;
            if (rigSource == null)
            {
                try
                {
                    rigSource = FindRig(controller, clips, result);
                }
                catch (Exception e)
                {
                    result.message = "找 rig 时出错：" + e.Message;
                    return result;
                }
                if (rigSource == null) return result;      // FindRig 已经填了 message
                result.warnings.Add("rig 是**扫绑定反推**出来的（" + rigSource.name + "）。"
                    + "要是打出来的 bundle 采不到东西，就在上面把「控制器绑定的预制体」手动选上。");
            }
            else
            {
                result.rigPath = AssetDatabase.GetAssetPath(rigSource);
                VerifyBindings(controller, clips, rigSource, result);
            }

            // ── ② 复制一份 rig 到临时目录 ──────────────────────────────────────────
            try
            {
                Directory.CreateDirectory(TempFolder);
                string tempPath = TempFolder + "/" + Sanitize(rigSource.name) + ".prefab";
                AssetDatabase.DeleteAsset(tempPath);

                var instance = UnityEngine.Object.Instantiate(rigSource);
                instance.name = rigSource.name;
                PrefabUtility.SaveAsPrefabAsset(instance, tempPath);
                UnityEngine.Object.DestroyImmediate(instance);
                AssetDatabase.SaveAssets();

                result.rigPath = tempPath;
            }
            catch (Exception e)
            {
                result.message = "复制 rig 失败：" + e.Message;
                return result;
            }

            // ── ③ 打 bundle ───────────────────────────────────────────────────────
            // ChunkBasedCompression（LZ4）：Warudo 侧走 `AssetBundle.LoadFromMemory`，这份读得了。
            // **别用默认的 LZMA** —— 那份要整段解压，内存路线更容易炸。
            string bundlePath = null;
            try
            {
                Directory.CreateDirectory(outputDirectory);

                var builds = new[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = bundleFileName,
                        assetNames = new[] { result.rigPath, AssetDatabase.GetAssetPath(controller) }
                    }
                };

                var manifest = BuildPipeline.BuildAssetBundles(
                    outputDirectory,
                    builds,
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    EditorUserBuildSettings.activeBuildTarget);

                if (manifest == null)
                {
                    result.message = "BuildAssetBundles 返回了 null（打包失败，看 Console）。";
                    return result;
                }

                bundlePath = Path.Combine(outputDirectory, bundleFileName);
                if (!File.Exists(bundlePath))
                {
                    result.message = "打包跑完了，但输出目录里没有 " + bundleFileName + " —— 看 Console 里的报错。";
                    return result;
                }
            }
            catch (Exception e)
            {
                result.message = "打包失败：" + e.Message;
                return result;
            }
            finally
            {
                CleanupTemp(result);
            }

            long size = 0;
            try { size = new FileInfo(bundlePath).Length; } catch { }

            result.ok = true;
            result.bundlePath = bundlePath.Replace('\\', '/');
            result.message = "打包完成：" + result.bundlePath
                + "（" + (size / 1024.0).ToString("0.0") + " KB · " + clips.Count + " 条片段"
                + " · rig = " + rigSource.name + "）";
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 校验：用户指定的预制体到底接不接得住这份控制器
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 把每条绑定的路径在**指定的预制体**里解析一遍，把解析不到的**点名**。
        ///
        /// 【为什么不把"解析不到"当错误直接拦下】
        /// 运行时 `GetBlendShapeWeight` 是**按名字**取形状权重的：控制器里写了 `blendShape.foo`
        /// 而这个网格上没有 `foo`，Unity **不报错**，那一格就永远是 0。
        /// 所以"路径/形状对不上"是这张表最该提前告诉用户的事 —— 但它不一定致命
        /// （比如控制器里有用不上的历史绑定），所以报成**警告**，由用户决定要不要继续。
        ///
        /// 判据故意粗：只看**路径能不能解析到对象**（`Transform.Find` 一条链）。
        /// 形状名对不对**查不了** —— 那需要在打包时枚举网格的 blendShape，而运行时那条路我们走不通
        /// （见文件头：运行时枚举不了 clip 绑定，这正是必须带 rig 的原因）。形状名只能等 Warudo 侧的自检报。
        /// </summary>
        private static void VerifyBindings(RuntimeAnimatorController controller, List<AnimationClip> clips,
            GameObject rigPrefab, Result result)
        {
            var animator = rigPrefab.GetComponent<Animator>();
            if (animator == null)
                result.warnings.Add("你选的预制体上没有 Animator —— 控制器的绑定路径是相对 **Animator 所在对象** 算的，"
                    + "没有它运行时也不会有 Animator（我们会在影子上补一个）。");

            var unresolved = new List<string>();
            int total = 0;

            foreach (var clip in clips)
            {
                if (clip == null) continue;

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    total++;
                    if (!Resolves(rigPrefab.transform, binding.path))
                        AddOnce(unresolved, binding.path, clip.name);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    total++;
                    if (!Resolves(rigPrefab.transform, binding.path))
                        AddOnce(unresolved, binding.path, clip.name);
                }
            }

            if (unresolved.Count == 0)
            {
                result.warnings.Add("绑定校验：全部 " + total + " 条路径都能在这份预制体里解析到 ✓"
                    + "（⚠️ 只验路径，**验不了形态键名字** —— 那个只能等 Warudo 侧的自检报）");
                return;
            }

            result.warnings.Add("绑定校验：有 " + unresolved.Count + " 条路径在这份预制体里**解析不到**"
                + "（共 " + total + " 条）—— 运行时会静默采不到那些曲线，先确认预制体选对了：");
            for (int i = 0; i < unresolved.Count && i < 6; i++)
                result.warnings.Add("    ✗ " + unresolved[i]);
            if (unresolved.Count > 6) result.warnings.Add("    …还有 " + (unresolved.Count - 6) + " 条");
        }

        /// <summary>空路径 = 绑在根自己身上；否则按 `/` 一层层 `Transform.Find`。</summary>
        private static bool Resolves(Transform root, string path)
        {
            if (root == null) return false;
            if (string.IsNullOrEmpty(path)) return true;
            return root.Find(path) != null;
        }

        private static void AddOnce(List<string> into, string path, string clipName)
        {
            string entry = "「" + (string.IsNullOrEmpty(path) ? "(根)" : path) + "」  （片段 " + clipName + "）";
            if (!into.Contains(entry)) into.Add(entry);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 找 rig（只在用户没指定时用）
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>拿到对象所在的**最外层预制体资产路径**（对象本身是预制体资产、或场景里的预制体实例都能处理）。</summary>
        private static string PrefabPathOf(UnityEngine.Object source)
        {
            if (source == null) return null;

            var go = source as GameObject;
            if (go == null)
            {
                var component = source as Component;
                go = component != null ? component.gameObject : null;
            }
            if (go == null) return null;

            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (instanceRoot != null)
            {
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
                if (asset != null) return AssetDatabase.GetAssetPath(asset);
            }

            // 已经是预制体资产，或者根本不是预制体实例：直接问路径（后者会拿到场景路径，下面按空处理）
            string path = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return path;
            if (!string.IsNullOrEmpty(path) && !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                && path.StartsWith("Assets/", StringComparison.Ordinal)) return path;

            return null;
        }

        /// <summary>
        /// 自动找 rig（给 UI 的「自动」按钮用）：扫绑定反推。
        /// 找不到/歧义时返回 null，原因写进 <paramref name="note"/>。
        /// </summary>
        internal static GameObject GuessRig(RuntimeAnimatorController controller, out string note)
        {
            note = null;
            var animator = controller as AnimatorController;
            if (animator == null) { note = "不是纯 Unity AnimatorController。"; return null; }

            var result = new Result();
            var clips = Clips(animator);
            if (clips.Count == 0) { note = "控制器里没有动画片段。"; return null; }

            var rig = FindRig(controller, clips, result);
            if (rig == null) { note = result.message; return null; }

            note = "按绑定反推： " + rig.name;
            return rig;
        }

        /// <summary>
        /// 从所有 clip 的曲线绑定里找出**被绑到的那套 rig 的根预制体**。
        /// 一套控制器绑到多个根时点名报错（结果放 <paramref name="result"/>，返回 null）。
        /// </summary>
        private static GameObject FindRig(RuntimeAnimatorController controller, List<AnimationClip> clips, Result result)
        {
            var roots = new List<UnityEngine.Object>();

            foreach (var clip in clips)
            {
                if (clip == null) continue;

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    AddRoot(roots, AnimatorRootFor(controller), binding.path);
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    AddRoot(roots, AnimatorRootFor(controller), binding.path);
            }

            if (roots.Count == 0)
            {
                result.message = "这份控制器的片段里**没有找到任何绑定的对象** —— 曲线是空的，或者绑的路径解析不到。"
                    + "打出去会是一个采不到东西的 bundle。先在「控制器编辑」页把片段填好、"
                    + "把形态键曲线重绑到调试对象上。";
                return null;
            }

            if (roots.Count > 1)
            {
                var names = new StringBuilder();
                foreach (var root in roots)
                {
                    if (names.Length > 0) names.Append("、");
                    names.Append(root.name);
                }
                result.message = "这份控制器跨了多套 rig（" + names + "）—— 一次只能打一套。"
                    + "请把控制器拆成「一份控制器一套 rig」再打。";
                return null;
            }

            string path = PrefabPathOf(roots[0]);
            if (string.IsNullOrEmpty(path))
            {
                result.message = "绑到的那套对象（" + roots[0].name + "）**不是工程里的预制体**"
                    + "（可能是场景实例、运行时生成的东西）。请先把 rig 存成预制体再打。";
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                result.message = "绑到的那套对象在 " + path + "，但那儿读不出 GameObject。";
                return null;
            }

            return asset;
        }

        /// <summary>
        /// 把绑定的 `path`（相对 Animator 根的层级路径）解析成"那一套 rig 的根对象"。
        /// `path` 为空 = 绑在根自己身上；否则**第一段就是 rig 根的名字**。
        /// </summary>
        private static void AddRoot(List<UnityEngine.Object> into, Transform animatorRoot, string path)
        {
            if (animatorRoot == null) return;

            if (string.IsNullOrEmpty(path))
            {
                if (!into.Contains(animatorRoot.gameObject)) into.Add(animatorRoot.gameObject);
                return;
            }

            int slash = path.IndexOf('/');
            string first = slash < 0 ? path : path.Substring(0, slash);

            foreach (var child in animatorRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != first) continue;
                if (!into.Contains(child.gameObject)) into.Add(child.gameObject);
                return;
            }
        }

        /// <summary>控制器自己那条预制体（绑定的路径就是相对它的）。找不到就 null（后面会报"解析不到"）。</summary>
        private static Transform AnimatorRootFor(RuntimeAnimatorController controller)
        {
            string path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(path)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null ? prefab.transform : null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 杂项
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>控制器用到的所有片段（含混合树子节点；去重）。</summary>
        private static List<AnimationClip> Clips(AnimatorController controller)
        {
            var clips = new List<AnimationClip>();
            if (controller == null) return clips;

            foreach (var layer in controller.layers)
                Collect(layer.stateMachine, clips);

            return clips.Distinct().ToList();
        }

        private static void Collect(AnimatorStateMachine machine, List<AnimationClip> into)
        {
            if (machine == null) return;

            foreach (var state in machine.states)
                Collect(state.state != null ? state.state.motion : null, into);

            foreach (var child in machine.stateMachines)
                Collect(child.stateMachine, into);
        }

        private static void Collect(Motion motion, List<AnimationClip> into)
        {
            if (motion == null) return;

            if (motion is AnimationClip clip)
            {
                if (!into.Contains(clip)) into.Add(clip);
                return;
            }

            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    Collect(child.motion, into);
        }

        /// <summary>文件名里不能有的字符换成下划线（预制体名字可能带空格之类）。</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "HoFTRig";
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return builder.ToString();
        }

        private static void CleanupTemp(Result result)
        {
            try
            {
                if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            }
            catch (Exception e)
            {
                result.warnings.Add("临时目录没删掉（" + TempFolder + "）：" + e.Message);
            }
        }
    }
}
#endif
