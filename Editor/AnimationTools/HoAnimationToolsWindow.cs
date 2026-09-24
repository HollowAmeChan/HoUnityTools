using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// **动画工具**：两个折叠栏装在一起 ——
    /// ① **形态键动画**：给一份根物体 + 它下面要处理的网格，每个形态键出一份 `<键名>.anim`
    ///    （值 100 常量，一个片段写所有有这个键的网格）。面捕只是它的头号用户：那批片段正好当槽位数据。
    /// ② **轨道处理**（旧的「动画处理」）：把一份 `.anim` 里除了 Float 曲线以外的曲线块删掉。
    ///
    /// 【为什么不各留一个窗口】
    /// 两个都是"拿资产、点一下、出一个结果"的小工具，各自一个菜单入口意味着每次要在菜单里找两处。
    /// 合成一页之后入口只有一个（`HoUnityTools/动画工具`），下面按功能分折叠栏 —— 折叠栏是**职责**的边界，
    /// 不是两套代码：形态键那段的生成逻辑还在 <see cref="HoBlendShapeClipBuilder"/>，
    /// 轨道处理在 <see cref="HoAnimationClipProcessor"/>，这一页只管画界面。
    ///
    /// 【列表为什么要自己滚】
    /// 网格列表天然会长（一个角色几十个 SMR），折叠栏里再叠一层页面滚动会很难用：
    /// 所以**列表自己有界高 + 自己滚**（<see cref="ListHeight"/>），页面本身不滚。
    /// </summary>
    internal sealed class HoAnimationToolsWindow : EditorWindow
    {
        // 形态键动画那一段的状态：按"根物体 + 相对路径"存，关掉窗口再打开不用重新拖一遍。
        private const string RootKey = "Ho.BlendShape.ClipBuilder.Root";
        private const string MeshesKey = "Ho.BlendShape.ClipBuilder.Meshes";
        private const string FolderKey = "Ho.BlendShape.ClipBuilder.Folder";

        private GameObject root;
        private readonly List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        private string folder = "Assets/HoUnityTools/BlendShapeClips";
        private string report = "";
        private bool reportIsError;
        private readonly List<string> reportNames = new List<string>();

        /// <summary>轨道处理那段的目标剪辑（窗口自己的序列化字段，域重载后还在）。</summary>
        [SerializeField] private AnimationClip targetClip;
        private string clipReport = "";
        private bool clipReportIsError;

        private bool shapesExpanded = true;
        private bool clipExpanded;
        private Vector2 pageScroll;
        private Vector2 meshScroll;
        private Vector2 resultScroll;

        /// <summary>根物体存的是"哪一侧的什么东西"：工程资产存资产路径，场景对象存 `scene:&lt;层级路径&gt;`。</summary>
        private const string ScenePrefix = "scene:";

        [MenuItem("HoUnityTools/动画工具", false, 5)]
        private static void Open()
        {
            var window = GetWindow<HoAnimationToolsWindow>(false, "动画工具", true);
            window.minSize = new Vector2(430.0f, 360.0f);
            window.Show();
        }

        private void OnEnable()
        {
            folder = EditorPrefs.GetString(FolderKey, folder);
            root = ResolveRoot(EditorPrefs.GetString(RootKey, ""));
            RestoreMeshes();
        }

        /// <summary>
        /// 把存下来的根物体找回来。**场景对象也支持**：按层级路径在当前场景里找
        /// （跟面捕那个"调试对象"同一套做法）。不然拖一个场景里的角色进来，
        /// 关个窗口 / 重载一次脚本域就全没了 —— 等于逼大家只能拖预制件。
        /// </summary>
        private static GameObject ResolveRoot(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return null;
            if (!saved.StartsWith(ScenePrefix, StringComparison.Ordinal))
                return AssetDatabase.LoadAssetAtPath<GameObject>(saved);
            return FindInScene(saved.Substring(ScenePrefix.Length));
        }

        /// <summary>场景里的对象 → `A/B/C` 这种层级路径。</summary>
        private static string HierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static GameObject FindInScene(string hierarchyPath)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(hierarchyPath)) return null;
            string[] parts = hierarchyPath.Split('/');
            foreach (var candidate in scene.GetRootGameObjects())
            {
                if (candidate.name != parts[0]) continue;
                Transform node = candidate.transform;
                bool ok = true;
                for (int i = 1; i < parts.Length; i++)
                {
                    node = node.Find(parts[i]);
                    if (node == null) { ok = false; break; }
                }

                if (ok) return node.gameObject;
            }

            return null;
        }

        private void OnGUI()
        {
            HoConstraintEditorControls.Title("动画工具",
                string.IsNullOrEmpty(folder) ? "未指定输出文件夹" : folder,
                (KeyCount() + " 个键", KeyCount() > 0),
                (meshes.Count + " 个网格", meshes.Count > 0));

            // 整页一个滚动区：第一栏撑满窗口时，鼠标滚轮照样能滚到下面那一栏。
            // 网格列表自己有独立的滚动区（鼠标停在列表上时滚的是列表），这是 IMGUI 下嵌套滚动区的常规做法。
            pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
            DrawShapeSection();
            DrawClipSection();
            EditorGUILayout.EndScrollView();
        }

        // ══════════════════════════════════════════════════════════════
        // 一、形态键动画
        // ══════════════════════════════════════════════════════════════
        private void DrawShapeSection()
        {
            string summary = meshes.Count + " 个网格 · " + KeyCount() + " 个键";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref shapesExpanded, "形态键动画", summary,
                    HoConstraintEditorTheme.AccentMesh))
                return;

            using (HoConstraintEditorControls.Card())
            {
                DrawShapeInputs();
                DrawShapeButtons();
                DrawShapeReport();
                DrawMeshList();
                DrawResultList();
            }
        }

        /// <summary>第一排：根物体 + 输出文件夹。</summary>
        private void DrawShapeInputs()
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("根物体", HoConstraintEditorTheme.LabelWidth,
                    "绑定路径以它为根（片段里的 `Body`、`Root/Body` 这种相对路径就是这么算出来的）。"
                    + "**预制件或场景里的对象都行** —— 两种都会记住（资产记路径，场景对象记层级路径）。"
                    + "注意：片段写的是**这个根下面的相对路径**，以后它要跟同一套层级对上才有用。");
                var pickedRoot = (GameObject)EditorGUI.ObjectField(
                    HoConstraintEditorControls.NextFlexible(90.0f), root, typeof(GameObject), false);
                if (pickedRoot != root)
                {
                    root = pickedRoot;
                    meshes.Clear();
                    SaveMeshes();
                }

                HoConstraintEditorControls.Gap();
                HoConstraintEditorControls.Label("输出文件夹", HoConstraintEditorTheme.LabelWidth,
                    "片段写到这里，文件名就是键名。重跑是覆盖式的：同名片段保留资产本身（GUID 不变）只重写曲线。");
                var current = string.IsNullOrEmpty(folder) ? null : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
                var pickedFolder = (DefaultAsset)EditorGUI.ObjectField(
                    HoConstraintEditorControls.NextFlexible(90.0f), current, typeof(DefaultAsset), false);
                if (pickedFolder != current)
                {
                    folder = pickedFolder != null ? AssetDatabase.GetAssetPath(pickedFolder) : "";
                    EditorPrefs.SetString(FolderKey, folder);
                }

                HoConstraintEditorControls.Flex();
            }
        }

        /// <summary>第二排：动作按钮。</summary>
        private void DrawShapeButtons()
        {
            using (HoConstraintEditorControls.Row(true))
            {
                using (new EditorGUI.DisabledScope(!CanBuild))
                {
                    if (HoConstraintEditorControls.Button("生成",
                        "每个形态键一份 `<键名>.anim`：值 100 常量，一个片段写所有有这个键的网格。", true))
                        Build();
                }

                HoConstraintEditorControls.Gap();
                if (HoConstraintEditorControls.Button("新建文件夹…", "在工程里新建一个输出文件夹。"))
                {
                    string created = EditorUtility.SaveFolderPanel("形态键动画放哪", "Assets", "BlendShapeClips");
                    if (!string.IsNullOrEmpty(created) && created.StartsWith(Application.dataPath, StringComparison.Ordinal))
                    {
                        folder = "Assets" + created.Substring(Application.dataPath.Length);
                        EditorPrefs.SetString(FolderKey, folder);
                    }
                }

                HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(root == null))
                {
                    if (HoConstraintEditorControls.Button("按根物体填充", "把根物体下所有网格放进来（替换现有列表）。"))
                    {
                        meshes.Clear();
                        meshes.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                        SaveMeshes();
                    }
                }

                HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(meshes.Count == 0))
                {
                    if (HoConstraintEditorControls.Button("清空", "清空网格列表（不动已经生成出来的片段）。"))
                    {
                        meshes.Clear();
                        SaveMeshes();
                    }
                }

                HoConstraintEditorControls.Flex();
            }
        }

        private void DrawShapeReport()
        {
            if (!string.IsNullOrEmpty(report))
                EditorGUILayout.HelpBox(report, reportIsError ? MessageType.Warning : MessageType.Info);
        }

        /// <summary>第三排往下：网格列表（自己滚，所以再长也不会把窗口撑爆）。</summary>
        private void DrawMeshList()
        {
            int unrelated = UnrelatedCount();
            if (unrelated > 0)
            {
                EditorGUILayout.HelpBox("有 " + unrelated + " 个网格**不在根物体之下**，生成会被拒（“网格必须在根物体之下”）。"
                    + "根物体与网格要来自同一侧：都是工程资产，或者都是场景里的对象。", MessageType.Warning);
            }

            if (meshes.Count == 0)
            {
                HoConstraintEditorControls.Caption(KeyCount() == 0
                    ? "还没有网格 —— 先填根物体（预制件或场景对象），再点「按根物体填充」。"
                    : "列表是空的。");
            }
            else
            {
                meshScroll = EditorGUILayout.BeginScrollView(meshScroll, GUILayout.Height(MeshListHeight()));
                for (int i = 0; i < meshes.Count; i++)
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        var picked = (SkinnedMeshRenderer)EditorGUI.ObjectField(
                            HoConstraintEditorControls.NextFlexible(120.0f), meshes[i], typeof(SkinnedMeshRenderer), true);
                        if (picked != meshes[i]) { meshes[i] = picked; SaveMeshes(); }

                        HoConstraintEditorControls.Gap(4.0f);
                        if (HoConstraintEditorControls.IconButton("✕", "从列表里去掉。"))
                        {
                            meshes.RemoveAt(i);
                            SaveMeshes();
                            break;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            using (HoConstraintEditorControls.Row(true))
            {
                if (HoConstraintEditorControls.Button("加一个网格", "手动往列表里加一条，再把网格拖进去。"))
                {
                    meshes.Add(null);
                    SaveMeshes();
                }

                HoConstraintEditorControls.Gap(6.0f);
                HoConstraintEditorControls.Caption(KeyCount() == 0
                    ? "还没有采集到形态键 —— 先填根物体与网格。"
                    : "会出 " + KeyCount() + " 份片段：" + string.Join("、", PreviewKeys(8)));
                HoConstraintEditorControls.Flex();
            }
        }

        private void DrawResultList()
        {
            if (reportIsError || reportNames.Count == 0) return;

            HoConstraintEditorControls.Separator(3.0f, 3.0f);
            HoConstraintEditorControls.Caption("生成结果（" + reportNames.Count + " 份）");
            resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.Height(ResultListHeight()));
            foreach (string name in reportNames) HoConstraintEditorControls.Caption(name);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 网格列表的高度：**按条数给，封顶 200**。封顶是为了让整页通常能同时看见两个折叠栏；
        /// 条数多的时候列表自己出一根滚动条，剩下的交给整页那个滚动区。
        /// </summary>
        private float MeshListHeight() => Mathf.Clamp(
            meshes.Count * HoConstraintEditorTheme.RowHeight + 4.0f,
            HoConstraintEditorTheme.RowHeight + 4.0f, 200.0f);

        /// <summary>生成结果名单的高度：同样按条数给、封顶 150。</summary>
        private float ResultListHeight() => Mathf.Clamp(
            reportNames.Count * 13.0f + 6.0f, 20.0f, 150.0f);

        /// <summary>列表里有多少个网格其实不在根物体之下（生成会被拒，早点说）。</summary>
        private int UnrelatedCount()
        {
            if (root == null) return 0;
            int count = 0;
            foreach (var mesh in meshes)
                if (mesh != null && mesh.transform != root.transform && !mesh.transform.IsChildOf(root.transform)) count++;
            return count;
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

        // ══════════════════════════════════════════════════════════════
        // 二、轨道处理（旧的「动画处理」）
        // ══════════════════════════════════════════════════════════════
        private void DrawClipSection()
        {
            string summary = targetClip == null ? "未指定剪辑" : targetClip.name;
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref clipExpanded, "轨道处理", summary,
                    HoConstraintEditorTheme.AccentRules))
                return;

            using (HoConstraintEditorControls.Card())
            {
                HoConstraintEditorControls.Caption(
                    "把一份 `.anim` 里除了 **Float 曲线**以外的曲线块整块删掉（位置 / 旋转 / 缩放 / 根运动…）。"
                    + "面捕的槽位片段只要形态键曲线，别的一起导出会多出没用的轨道。");

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("目标动画剪辑", HoConstraintEditorTheme.LabelWidth,
                        "工程里的 `.anim`。处理会**直接改写磁盘上的文件**，没有备份也没有 Undo。");
                    var picked = (AnimationClip)EditorGUI.ObjectField(
                        HoConstraintEditorControls.NextFlexible(120.0f), targetClip, typeof(AnimationClip), false);
                    if (picked != targetClip) { targetClip = picked; clipReport = ""; }

                    HoConstraintEditorControls.Gap();
                    using (new EditorGUI.DisabledScope(targetClip == null))
                    {
                        if (HoConstraintEditorControls.Button("仅保留 Float 曲线",
                            "会先弹一次确认；确认后不可撤销。", true, 132.0f))
                            ProcessClip();
                    }

                    HoConstraintEditorControls.Flex();
                }

                if (!string.IsNullOrEmpty(clipReport))
                    EditorGUILayout.HelpBox(clipReport, clipReportIsError ? MessageType.Warning : MessageType.Info);
            }
        }

        private void ProcessClip()
        {
            string name = AssetDatabase.GetAssetPath(targetClip);
            if (!EditorUtility.DisplayDialog("确认操作",
                "确定要从 " + System.IO.Path.GetFileName(name) + " 中永久删除所有非 Float 曲线吗？此操作不可撤销。",
                "是，删除", "否，取消"))
                return;

            var result = HoAnimationClipProcessor.Process(targetClip);
            clipReportIsError = !result.Ok;
            clipReport = result.Message;
        }

        // ══════════════════════════════════════════════════════════════
        // 网格列表的持久化与预览
        // ══════════════════════════════════════════════════════════════
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
            // 工程资产（预制件）存资产路径；**场景里的对象（含预制件实例）存层级路径**，两种都能找回来。
            // 判据用 `scene.IsValid()` 而不是 `GetAssetPath`：预制件实例的 GetAssetPath 会返回它引用的
            // 那个预制件资产 —— 那样下次打开会悄悄换成"资产那一侧"，跟用户拖进来的不是同一个东西。
            string rootKey = "";
            if (root != null)
                rootKey = root.scene.IsValid()
                    ? ScenePrefix + HierarchyPath(root)
                    : AssetDatabase.GetAssetPath(root);

            EditorPrefs.SetString(RootKey, rootKey);
            EditorPrefs.SetString(FolderKey, folder);
            var text = new System.Text.StringBuilder();
            foreach (var mesh in meshes)
                if (mesh != null && root != null && mesh.transform.IsChildOf(root.transform))
                    text.Append(AnimationUtility.CalculateTransformPath(mesh.transform, root.transform)).Append('\n');
            EditorPrefs.SetString(MeshesKey, text.ToString());
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
