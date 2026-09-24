using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>一次生成的结果（给窗口直接报出来，不靠 Debug.Log）。</summary>
    public sealed class HoBlendShapeClipBuildReport
    {
        public string folder = "";
        /// <summary>生成/更新过的片段名（= 形态键原名）。</summary>
        public readonly List<string> names = new List<string>();
        /// <summary>键名里有文件名字符、只好改写过文件名的那几个（片段名仍是原名）。</summary>
        public readonly List<string> renamed = new List<string>();
        /// <summary>顺手清掉的旧片段（这次没写到的那些；只有 <c>pruneStale</c> 打开时才有内容）。</summary>
        public readonly List<string> removed = new List<string>();
        public int created, updated, curves;
    }

    /// <summary>
    /// **「形态键动画」**（动画工具页里那一栏）的生成器：给一份网格列表，每个形态键出一份 `<键名>.anim`。
    /// 界面在 <see cref="HoAnimationToolsWindow"/>，这里只有生成逻辑。
    ///
    /// 这是个通用动画工具（不属于面捕）：面捕只是它的头号用户 —— 一份"一个键 0→100"的基础动画
    /// 正好可以直接扔进面捕的动画文件夹里当槽位数据。
    /// 语义：
    /// <list type="bullet">
    /// <item>片段名 = **形态键原名**（文件名里有非法字符时才改写文件名，`clip.name` 仍是原名）；</item>
    /// <item>值 = **100 常量**，不是斜坡 —— 混合树采的是"姿势"，片段里没有时间轴这回事；</item>
    /// <item>**一个片段驱动所有有这个键的网格**，所以多网格模型只需要一份；</item>
    /// <item>重跑是覆盖式的：已有同名片段**保留资产本身**（GUID 不变），只重写曲线。</item>
    /// </list>
    ///
    /// 生成物与模型无关：装配时是按**形态键名**重绑到驱动对象上的，换个模型照样能用。
    /// </summary>
    public static class HoBlendShapeClipBuilder
    {
        /// <summary>
        /// 生成/更新片段。
        /// <paramref name="pruneStale"/> = true 时**顺手清掉这个文件夹里这次没写到的片段**：
        /// 那个文件夹是"产物目录"（生成完整份拷走再用），旧键 / 旧网格留下的片段留着只会让人以为它还生效。
        /// 默认 false —— 删除是破坏性操作，由调用方（面板上的开关 / 脚本）明确决定。
        /// </summary>
        public static HoBlendShapeClipBuildReport Build(IList<SkinnedMeshRenderer> meshes, Transform root,
            string folderPath, bool pruneStale = false)
        {
            if (root == null) throw new InvalidOperationException("先指定根物体 —— 绑定路径以它为根。");
            if (string.IsNullOrEmpty(folderPath)) throw new InvalidOperationException("先指定输出文件夹。");
            if (!AssetDatabase.IsValidFolder(folderPath))
                throw new InvalidOperationException("输出文件夹不在工程里：" + folderPath);

            // 键名 → 有这个键的网格路径们（去重）。多网格共用一个键时，**一份片段写全部**。
            var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (meshes != null)
                foreach (var mesh in meshes)
                {
                    if (mesh == null || mesh.sharedMesh == null) continue;
                    if (mesh.transform != root && !mesh.transform.IsChildOf(root))
                        throw new InvalidOperationException("网格必须在根物体之下：" + mesh.name);
                    string path = AnimationUtility.CalculateTransformPath(mesh.transform, root);
                    for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                    {
                        string shape = mesh.sharedMesh.GetBlendShapeName(i);
                        if (!owners.TryGetValue(shape, out var paths)) owners[shape] = paths = new List<string>();
                        if (!paths.Contains(path)) paths.Add(path);
                    }
                }

            if (owners.Count == 0) throw new InvalidOperationException("这些网格上一个形态键都没有。");

            var report = new HoBlendShapeClipBuildReport { folder = folderPath };
            var curve = AnimationCurve.Constant(0f, 1f / 60f, 100f);
            foreach (var pair in owners)
            {
                string file = SafeFileName(pair.Key);
                if (!string.Equals(file, pair.Key, StringComparison.Ordinal))
                    report.renamed.Add(pair.Key + " → " + file + ".anim");
                string path = folderPath + "/" + file + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                bool created = clip == null;
                if (created) clip = new AnimationClip { frameRate = 60f };
                else clip.ClearCurves();

                clip.name = pair.Key;
                foreach (string owner in pair.Value)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(owner, typeof(SkinnedMeshRenderer),
                        "blendShape." + pair.Key), curve);
                    report.curves++;
                }

                if (created) AssetDatabase.CreateAsset(clip, path);
                else EditorUtility.SetDirty(clip);
                if (created) report.created++; else report.updated++;
                report.names.Add(pair.Key);
            }

            if (pruneStale) PruneStale(folderPath, report);
            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>
        /// 把产物文件夹里**这次没写到的**片段删掉。
        /// 按**片段名**（不是文件名 —— 键名里的非法字符会被 <see cref="SafeFileName"/> 改写成文件名）比对，
        /// 所以只认得出"这个文件夹里、名字不在这次名单里的 `AnimationClip`"。
        ///
        /// ⚠️ 名字必须**在删之前**取好：`DeleteAsset` 会把那个托管包装对象一起销毁，
        /// 之后再读 `clip.name` 就是 `MissingReferenceException`（实测踩到，见
        /// `docs/pitfalls/UNITY_ASSET_PITFALLS.md`）。所以先收集"路径 + 名字"，再统一删。
        /// </summary>
        private static void PruneStale(string folderPath, HoBlendShapeClipBuildReport report)
        {
            var keep = new HashSet<string>(report.names, StringComparer.Ordinal);
            var doomed = new List<(string Path, string Name)>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null && !keep.Contains(clip.name)) doomed.Add((path, clip.name));
            }

            foreach (var entry in doomed)
                if (AssetDatabase.DeleteAsset(entry.Path)) report.removed.Add(entry.Name);
        }

        /// <summary>文件名的安全形式：键名里有 `/`、`:`、空格之类时只改文件名，片段名保持原名。</summary>
        private static string SafeFileName(string shape)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var text = new System.Text.StringBuilder(shape.Length);
            foreach (char c in shape)
                text.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return text.ToString();
        }
    }
}
