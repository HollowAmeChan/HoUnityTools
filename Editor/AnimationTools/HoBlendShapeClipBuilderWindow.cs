using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// 「形态键基础动画」小窗口：给一份根物体与它下面要处理的网格，每个形态键出一份 `<键名>.anim`。
    ///
    /// 通用工具 —— 面捕只是头号用户（那批片段正好能当它的"槽位数据"）。
    /// 与装配那条链是**解耦**的：这里只管把基础动画造出来放进一个文件夹。
    /// </summary>
    internal sealed class HoBlendShapeClipBuilderWindow : EditorWindow
    {
        private const string RootKey = "Ho.BlendShape.ClipBuilder.Root";
        private const string MeshesKey = "Ho.BlendShape.ClipBuilder.Meshes";
        private const string FolderKey = "Ho.BlendShape.ClipBuilder.Folder";

        private GameObject root;
        private readonly List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        private string folder = "Assets/HoUnityTools/BlendShapeClips";
        private string report = "";
        private bool reportIsError;
        private bool inputExpanded = true;
        private bool outputExpanded = true;
        private bool resultExpanded = true;
        private Vector2 scroll;
        private readonly List<string> reportNames = new List<string>();

        [MenuItem("HoUnityTools/形态键基础动画")]
        private static void Open()
        {
            var window = GetWindow<HoBlendShapeClipBuilderWindow>(false, "形态键基础动画", true);
            window.minSize = new Vector2(380.0f, 320.0f);
            window.Show();
        }

        private void OnEnable()
        {
            folder = EditorPrefs.GetString(FolderKey, folder);
            string rootPath = EditorPrefs.GetString(RootKey, "");
            if (!string.IsNullOrEmpty(rootPath)) root = AssetDatabase.LoadAssetAtPath<GameObject>(rootPath);
            RestoreMeshes();
        }

        /// <summary>网格列表按"根物体 + 相对路径"存，关掉窗口再打开不用重新拖一遍。</summary>
        private void RestoreMeshes()
        {
            meshes.Clear();
            string saved = EditorPrefs.GetString(MeshesKey, "");
            if (root == null || string.IsNullOrEmpty(saved)) return;
            foreach (string path in saved.Split('\n'))
            {
                if (string.IsNullOrEmpty(path)) continue;
                var node = root.transform.Find(path);
                var mesh = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                if (mesh != null) meshes.Add(mesh);
            }
        }

        private void SaveMeshes()
        {
            EditorPrefs.SetString(RootKey, root != null ? AssetDatabase.GetAssetPath(root) : "");
            EditorPrefs.SetString(FolderKey, folder);
            var text = new System.Text.StringBuilder();
            foreach (var mesh in meshes)
                if (mesh != null && root != null && mesh.transform.IsChildOf(root.transform))
                    text.Append(AnimationUtility.CalculateTransformPath(mesh.transform, root.transform)).Append('\n');
            EditorPrefs.SetString(MeshesKey, text.ToString());
        }

        private void OnGUI()
        {
            HoConstraintEditorControls.Title("形态键基础动画",
                string.IsNullOrEmpty(folder) ? "未指定输出文件夹" : folder,
                (KeyCount() + " 个键", KeyCount() > 0),
                (meshes.Count + " 个网格", meshes.Count > 0));

            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref inputExpanded, "输入",
                meshes.Count + " 个网格 · " + KeyCount() + " 个键", HoConstraintEditorTheme.AccentMesh))
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("根物体", HoConstraintEditorTheme.LabelWidth,
                        "绑定路径以它为根（预制件、或角色根都行）。曲线就按这个层级写。");
                    var picked = (GameObject)EditorGUILayout.ObjectField(root, typeof(GameObject), false);
                    if (picked != root)
                    {
                        root = picked;
                        meshes.Clear();
                        SaveMeshes();
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("网格", HoConstraintEditorTheme.LabelWidth,
                        "要采集哪些网格上的形态键。键名会去重：每个键一份片段，一个片段写所有有这个键的网格。");
                    if (HoConstraintEditorControls.Button("按根物体填充", "把根物体下所有网格放进来。"))
                    {
                        meshes.Clear();
                        if (root != null) meshes.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                        SaveMeshes();
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("清空")) { meshes.Clear(); SaveMeshes(); }
                    HoConstraintEditorControls.Flex();
                }

                for (int i = 0; i < meshes.Count; i++)
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        var picked = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(meshes[i], typeof(SkinnedMeshRenderer), true);
                        if (picked != meshes[i]) { meshes[i] = picked; SaveMeshes(); }
                        if (HoConstraintEditorControls.IconButton("✕", "从列表里去掉。"))
                        {
                            meshes.RemoveAt(i);
                            SaveMeshes();
                            break;
                        }

                        HoConstraintEditorControls.Flex();
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("加一个网格")) { meshes.Add(null); SaveMeshes(); }
                    HoConstraintEditorControls.Gap(6.0f);
                    HoConstraintEditorControls.Caption(KeyCount() == 0
                        ? "还没有采集到形态键 —— 先填根物体与网格。"
                        : "会出 " + KeyCount() + " 份片段：" + string.Join("、", PreviewKeys(8)));
                    HoConstraintEditorControls.Flex();
                }
            }

            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref outputExpanded, "输出",
                string.IsNullOrEmpty(folder) ? "未指定" : folder, HoConstraintEditorTheme.AccentBlink))
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("输出文件夹", HoConstraintEditorTheme.LabelWidth,
                        "片段写到这里，文件名就是键名。重跑是覆盖式的：同名片段保留资产本身（GUID 不变）只重写曲线。");
                    var current = string.IsNullOrEmpty(folder) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
                    var picked = (DefaultAsset)EditorGUILayout.ObjectField(current, typeof(DefaultAsset), false);
                    if (picked != current)
                    {
                        folder = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                        EditorPrefs.SetString(FolderKey, folder);
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    using (new EditorGUI.DisabledScope(!CanBuild))
                    {
                        if (HoConstraintEditorControls.Button("生成",
                            "每个形态键一份 `<键名>.anim`：值 100 常量，一个片段写所有有这个键的网格。", true))
                            Build();
                    }

                    HoConstraintEditorControls.Gap(6.0f);
                    if (HoConstraintEditorControls.Button("新建文件夹…", "在工程里新建一个输出文件夹。"))
                    {
                        string created = EditorUtility.SaveFolderPanel("形态键基础动画放哪", "Assets", "BlendShapeClips");
                        if (!string.IsNullOrEmpty(created) && created.StartsWith(Application.dataPath, StringComparison.Ordinal))
                        {
                            folder = "Assets" + created.Substring(Application.dataPath.Length);
                            EditorPrefs.SetString(FolderKey, folder);
                        }
                    }

                    HoConstraintEditorControls.Flex();
                }

                if (!string.IsNullOrEmpty(report))
                    EditorGUILayout.HelpBox(report, reportIsError ? MessageType.Warning : MessageType.Info);
            }

            if (reportIsError || reportNames.Count == 0) return;
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref resultExpanded, "生成结果",
                reportNames.Count + " 份片段", HoConstraintEditorTheme.AccentOutput))
            using (HoConstraintEditorControls.Card())
            {
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120.0f));
                foreach (string name in reportNames) HoConstraintEditorControls.Caption(name);
                EditorGUILayout.EndScrollView();
            }
        }

        private bool CanBuild => root != null && meshes.Count > 0 && !string.IsNullOrEmpty(folder)
            && AssetDatabase.IsValidFolder(folder) && KeyCount() > 0;

        private void Build()
        {
            try
            {
                var built = HoBlendShapeClipBuilder.Build(meshes, root.transform, folder);
                SaveMeshes();
                reportIsError = false;
                report = "生成 " + built.created + " 个 / 更新 " + built.updated + " 个 · 曲线 " + built.curves
                    + " 条 → " + built.folder
                    + (built.renamed.Count > 0 ? "（文件名被改写过：" + string.Join("、", built.renamed) + "）" : "");
                reportNames.Clear();
                reportNames.AddRange(built.names);
            }
            catch (Exception e)
            {
                reportIsError = true;
                report = e.Message;
                reportNames.Clear();
            }
        }

        /// <summary>去重后的键名（面板上的实时预览与生成用的是同一套判定）。</summary>
        private List<string> Keys()
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.sharedMesh == null) continue;
                for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                {
                    string shape = mesh.sharedMesh.GetBlendShapeName(i);
                    if (seen.Add(shape)) keys.Add(shape);
                }
            }

            return keys;
        }

        private int KeyCount() => Keys().Count;

        private IEnumerable<string> PreviewKeys(int max)
        {
            var keys = Keys();
            for (int i = 0; i < keys.Count && i < max; i++) yield return keys[i];
            if (keys.Count > max) yield return "…";
        }
    }
}
