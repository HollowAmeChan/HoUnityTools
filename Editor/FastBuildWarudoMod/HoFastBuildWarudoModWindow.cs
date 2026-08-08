#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    internal sealed class HoFastBuildWarudoModWindow : EditorWindow
    {
        private const string MenuPath = "Assets/HoUnityTools/FastBuildWarudoMod";
        private const string WindowTitle = "FastBuild Warudo Mod";
        private const string PendingStateSessionKey = "HoUnityTools.FastBuildWarudoMod.PendingState";
        private const string TemporaryAssetRoot = "Assets/HoFastBuildWarudoModTemp";
        private const string StagedScriptsDirectoryName = "Scripts";
        private const string PendingPhase = "AwaitingCompile";
        private const string BuildingPhase = "Building";

        [Serializable]
        private sealed class ScriptPreview
        {
            public string sourcePath = string.Empty;
            public string typeName = string.Empty;
            public string note = string.Empty;
            public bool copySource;
            public bool removeWhenExcluded;
            public int referenceCount;
        }

        [Serializable]
        private sealed class ScriptMapping
        {
            public string sourcePath = string.Empty;
            public bool removeFromPrefab;
        }

        [Serializable]
        private sealed class BuildState
        {
            public string phase = string.Empty;
            public string stateFilePath = string.Empty;
            public string temporaryAssetRoot = string.Empty;
            public string temporaryPrefabPath = string.Empty;
            public string exportSettingsPath = string.Empty;
            public string originalModAssetPath = string.Empty;
            public int activeProfileIndex;
            public bool cleanupTemporaryAssets = true;
            public ScriptMapping[] scripts = Array.Empty<ScriptMapping>();
        }

        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private string sourcePrefabPath = string.Empty;
        [SerializeField] private List<ScriptPreview> scriptPreview = new List<ScriptPreview>();
        [SerializeField] private Vector2 pageScroll;
        [SerializeField] private Vector2 scriptScroll;
        [SerializeField] private bool copySelectedScripts = true;
        [SerializeField] private bool removeUnsafeComponents = true;
        [SerializeField] private bool cleanupTemporaryAssets = true;
        [SerializeField] private int dependencyCount;
        [SerializeField] private int nonScriptDependencyCount;
        [SerializeField] private int missingScriptCount;
        [SerializeField] private string exportSettingsPath = string.Empty;
        [SerializeField] private string activeModAssetPath = string.Empty;
        [SerializeField] private string lastBuildStatus = string.Empty;
        [SerializeField] private string dependencyPreviewHash = string.Empty;

        private GUIStyle panelTitleStyle;
        private GUIStyle panelStatusStyle;
        private GUIStyle primaryButtonStyle;
        private bool sdkAvailable;
        private string sdkError = string.Empty;

        private static bool resumeHookInstalled;

        [InitializeOnLoadMethod]
        private static void InitializePendingBuildResume()
        {
            EditorApplication.delayCall += RecoverPendingBuild;
        }

        private static void RecoverPendingBuild()
        {
            string stateFilePath = SessionState.GetString(PendingStateSessionKey, string.Empty);
            if (string.IsNullOrEmpty(stateFilePath))
            {
                string stateDirectory = StateDirectory;
                if (Directory.Exists(stateDirectory))
                {
                    stateFilePath = Directory.GetFiles(stateDirectory, "*.json", SearchOption.TopDirectoryOnly)
                        .Where(IsSafeStateFilePath)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(stateFilePath))
                    {
                        SessionState.SetString(PendingStateSessionKey, stateFilePath);
                        Debug.Log("[HoUnityTools] 已从 Library 恢复未完成的 FastBuild：" + stateFilePath);
                    }
                }
            }

            SchedulePendingBuildResume();
        }

        [MenuItem(MenuPath, false, 2010)]
        private static void OpenFromSelection()
        {
            string path = GetSelectedPrefabPath();
            if (!IsPrefab(path))
            {
                EditorUtility.DisplayDialog(WindowTitle, "请先在 Project 窗口中选择一个 Prefab 资源。", "确定");
                return;
            }

            var window = GetWindow<HoFastBuildWarudoModWindow>(WindowTitle);
            window.minSize = new Vector2(600f, 520f);
            window.SetSourcePrefab(path);
            window.Show();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateOpenFromSelection()
        {
            return IsPrefab(GetSelectedPrefabPath());
        }

        private void OnEnable()
        {
            if (sourcePrefab == null && IsPrefab(sourcePrefabPath))
                sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);

            RefreshSdkStatus();
            if (sourcePrefab != null && scriptPreview.Count == 0)
                RefreshDependencyPreview();
        }

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 132f;
            EnsureStyles();
            SynchronizeSourcePrefab();
            pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
            GUILayout.Space(8f);

            DrawWindowHeader(sdkAvailable, sdkError);
            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUI.DisabledScope(!sdkAvailable))
                    {
                        DrawSourcePanel();
                        GUILayout.Space(8f);
                        DrawExportSettingsPanel();
                        GUILayout.Space(8f);
                        DrawDependencyPanel();
                        GUILayout.Space(8f);
                        DrawBuildOptionsPanel();
                        GUILayout.Space(12f);
                        DrawBuildButton();
                    }
                }
                GUILayout.Space(10f);
            }

            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawWindowHeader(bool sdkAvailable, string sdkError)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "FastBuild Warudo Mod",
                    sdkAvailable ? "SDK 已就绪" : "SDK 不可用",
                    sdkAvailable ? new Color(0.20f, 0.68f, 0.57f) : new Color(0.88f, 0.42f, 0.28f));
                if (sdkAvailable)
                {
                    EditorGUILayout.LabelField(
                        "复制当前 Prefab 为 Character 并调用 UMod 官方构建，源资源不会被修改。",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        sdkError + "\n当前仓库未安装 Warudo SDK，FastBuild 面板已禁用。",
                        MessageType.Warning);
                    if (GUILayout.Button("重新检测 SDK", GUILayout.Width(96f)))
                        RefreshSdkStatus();
                }
            }
        }

        private void RefreshSdkStatus()
        {
            sdkAvailable = TryValidateOfficialBuildApi(out sdkError);
            if (sdkAvailable)
                RefreshExportSettingsPreview();
            else
            {
                exportSettingsPath = string.Empty;
                activeModAssetPath = string.Empty;
            }
        }

        private void DrawSourcePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "源 Prefab",
                    IsPrefab(sourcePrefabPath) ? "已选择" : "未选择",
                    new Color(0.24f, 0.54f, 0.88f));
                GUILayout.Space(5f);
                EditorGUI.BeginChangeCheck();
                GameObject nextPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Prefab",
                    sourcePrefab,
                    typeof(GameObject),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    string path = nextPrefab == null ? string.Empty : AssetDatabase.GetAssetPath(nextPrefab);
                    if (nextPrefab == null || IsPrefab(path))
                        SetSourcePrefab(path);
                    else
                        EditorUtility.DisplayDialog(WindowTitle, "这里只能选择 Project 中的 Prefab 资源。", "确定");
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(sourcePrefabPath)))
                    EditorGUILayout.SelectableLabel(sourcePrefabPath, EditorStyles.textField, GUILayout.Height(18f));
            }
        }

        private void DrawExportSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "Warudo 工作区",
                    string.IsNullOrEmpty(exportSettingsPath) ? "未找到" : "已连接",
                    new Color(0.20f, 0.68f, 0.57f));
                GUILayout.Space(5f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUIContent refreshContent = EditorGUIUtility.IconContent("Refresh");
                    refreshContent.tooltip = "重新读取 UMod ExportSettings";
                    if (GUILayout.Button(refreshContent, GUILayout.Width(30f), GUILayout.Height(19f)))
                        RefreshExportSettingsPreview();
                }

                EditorGUILayout.LabelField("ExportSettings", string.IsNullOrEmpty(exportSettingsPath) ? "未找到" : exportSettingsPath);
                EditorGUILayout.LabelField("Mod 目录", string.IsNullOrEmpty(activeModAssetPath) ? "未读取" : activeModAssetPath);
            }
        }

        private void DrawDependencyPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "依赖审查",
                    scriptPreview.Count + " 个脚本",
                    new Color(0.62f, 0.45f, 0.84f));
                GUILayout.Space(5f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    GUIContent refreshContent = EditorGUIUtility.IconContent("Refresh");
                    refreshContent.tooltip = "重新扫描 Prefab 依赖";
                    if (GUILayout.Button(refreshContent, GUILayout.Width(30f), GUILayout.Height(19f)))
                        RefreshDependencyPreview();
                }

                EditorGUILayout.LabelField(
                    "资源概览",
                    string.Format("依赖 {0} | 非脚本 {1} | 脚本 {2} | Missing {3}",
                        dependencyCount,
                        nonScriptDependencyCount,
                        scriptPreview.Count,
                        missingScriptCount));
                if (missingScriptCount > 0)
                    EditorGUILayout.HelpBox("Prefab 中存在 Missing Script，请先修复后再构建。", MessageType.Error);
                EditorGUILayout.LabelField(
                    "普通资源由 UMod 按 Prefab 引用收集；此处只管理需要单独编译的 C# 源码。",
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全选运行时", GUILayout.Width(92f)))
                        SetRuntimeScriptSelection(true);
                    if (GUILayout.Button("全部不复制", GUILayout.Width(92f)))
                        SetRuntimeScriptSelection(false);
                }

                GUILayout.Space(4f);
                scriptScroll = EditorGUILayout.BeginScrollView(scriptScroll, GUILayout.MinHeight(170f), GUILayout.MaxHeight(310f));
                if (scriptPreview.Count == 0)
                {
                    EditorGUILayout.HelpBox("当前 Prefab 没有扫描到可定位源码的 MonoBehaviour。", MessageType.Warning);
                }
                else
                {
                    foreach (ScriptPreview item in scriptPreview)
                        DrawScriptPreviewRow(item);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawScriptPreviewRow(ScriptPreview item)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(!copySelectedScripts || item.removeWhenExcluded))
                    item.copySource = EditorGUILayout.Toggle(item.copySource, GUILayout.Width(18f));

                string displayName = string.IsNullOrEmpty(item.typeName)
                    ? Path.GetFileNameWithoutExtension(item.sourcePath)
                    : item.typeName;
                GUIContent typeContent = new GUIContent(displayName, item.note);
                EditorGUILayout.LabelField(typeContent, EditorStyles.boldLabel, GUILayout.MinWidth(180f));
                EditorGUILayout.LabelField("引用 " + item.referenceCount, GUILayout.Width(56f));
                EditorGUILayout.LabelField(Path.GetFileName(item.sourcePath), EditorStyles.miniLabel);
            }
        }

        private void DrawBuildOptionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "构建选项",
                    cleanupTemporaryAssets ? "自动清理" : "保留临时目录",
                    new Color(0.91f, 0.65f, 0.25f));
                GUILayout.Space(5f);
                GUIContent copyContent = new GUIContent(
                    "复制已勾选脚本",
                    "复制源码会暂时进入 Unity 编译列表，之后由 UMod 按完整类型名连接 Prefab。");
                GUIContent removeContent = new GUIContent(
                    "移除编辑器组件",
                    "从临时 Character.prefab 移除明确只适用于编辑器的组件。");
                GUIContent cleanupContent = new GUIContent(
                    "构建完成后清理临时目录",
                    "关闭后仍可在 Library/HoFastBuildWarudoMod 恢复未完成流程。");
                copySelectedScripts = EditorGUILayout.ToggleLeft(copyContent, copySelectedScripts);
                removeUnsafeComponents = EditorGUILayout.ToggleLeft(removeContent, removeUnsafeComponents);
                cleanupTemporaryAssets = EditorGUILayout.ToggleLeft(cleanupContent, cleanupTemporaryAssets);
            }
        }

        private void DrawBuildButton()
        {
            bool hasPendingBuild = HasPendingBuildState();
            bool canBuild = IsPrefab(sourcePrefabPath) &&
                            sdkAvailable &&
                            missingScriptCount == 0 &&
                            !hasPendingBuild &&
                            !string.IsNullOrEmpty(exportSettingsPath);

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                if (GUILayout.Button("构建 Warudo Mod", primaryButtonStyle))
                    BeginBuild();
            }

            if (hasPendingBuild)
                EditorGUILayout.HelpBox("已有一个 FastBuild 流程正在等待脚本编译或构建完成。", MessageType.Warning);
            else if (!string.IsNullOrEmpty(lastBuildStatus))
                EditorGUILayout.HelpBox(lastBuildStatus, MessageType.Info);
        }

        private void DrawPanelHeader(string title, string status, Color accent)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.82f, 0.83f, 0.85f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            Rect titleRect = new Rect(rect.x + 12f, rect.y, rect.width - 120f, rect.height);
            Rect statusRect = new Rect(rect.xMax - 104f, rect.y, 94f, rect.height);
            GUI.Label(titleRect, title, panelTitleStyle);
            GUI.Label(statusRect, status, panelStatusStyle);
        }

        private void EnsureStyles()
        {
            if (panelTitleStyle == null)
            {
                panelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            if (panelStatusStyle == null)
            {
                panelStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            if (primaryButtonStyle == null)
            {
                primaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 36f,
                    fontStyle = FontStyle.Bold,
                    fontSize = 13
                };
            }
        }

        private void SetSourcePrefab(string path)
        {
            sourcePrefabPath = IsPrefab(path) ? path : string.Empty;
            sourcePrefab = string.IsNullOrEmpty(sourcePrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            lastBuildStatus = string.Empty;
            RefreshDependencyPreview();
            Repaint();
        }

        private void SynchronizeSourcePrefab()
        {
            if (sourcePrefab != null)
            {
                string objectPath = AssetDatabase.GetAssetPath(sourcePrefab);
                if (!IsPrefab(objectPath))
                {
                    sourcePrefab = null;
                    sourcePrefabPath = string.Empty;
                    RefreshDependencyPreview();
                }
                else if (!string.Equals(sourcePrefabPath, objectPath, StringComparison.OrdinalIgnoreCase))
                {
                    sourcePrefabPath = objectPath;
                    RefreshDependencyPreview();
                }
                return;
            }

            if (!string.IsNullOrEmpty(sourcePrefabPath) && !IsPrefab(sourcePrefabPath))
            {
                sourcePrefabPath = string.Empty;
                RefreshDependencyPreview();
            }
        }

        private void RefreshDependencyPreview(bool preserveSelection = false)
        {
            Dictionary<string, bool> previousSelection = null;
            if (preserveSelection)
            {
                previousSelection = scriptPreview
                    .Where(item => item != null && !string.IsNullOrEmpty(item.sourcePath))
                    .ToDictionary(item => item.sourcePath, item => item.copySource, StringComparer.OrdinalIgnoreCase);
            }

            scriptPreview.Clear();
            dependencyCount = 0;
            nonScriptDependencyCount = 0;
            missingScriptCount = 0;
            dependencyPreviewHash = string.Empty;

            if (!IsPrefab(sourcePrefabPath))
                return;

            string[] dependencies = AssetDatabase.GetDependencies(sourcePrefabPath, true);
            dependencyCount = dependencies.Length;
            nonScriptDependencyCount = dependencies.Count(path =>
                !string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            if (prefab == null)
                return;

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);

            var rows = new Dictionary<string, ScriptPreview>(StringComparer.OrdinalIgnoreCase);
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                string path = script == null ? string.Empty : AssetDatabase.GetAssetPath(script);
                if (string.IsNullOrEmpty(path) ||
                    !string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                ScriptPreview row;
                if (!rows.TryGetValue(path, out row))
                {
                    Type scriptType = script.GetClass();
                    string typeName = scriptType == null ? string.Empty : scriptType.FullName;
                    bool unsafeForRuntime = IsUnsafeRuntimeScript(path, typeName);
                    row = new ScriptPreview
                    {
                        sourcePath = path,
                        typeName = typeName,
                        copySource = !unsafeForRuntime && ShouldCopyScriptByDefault(path),
                        removeWhenExcluded = unsafeForRuntime,
                        note = GetScriptNote(path, typeName, unsafeForRuntime),
                    };
                    bool previousValue;
                    if (previousSelection != null && previousSelection.TryGetValue(path, out previousValue))
                        row.copySource = previousValue;
                    rows.Add(path, row);
                }

                row.referenceCount++;
            }

            scriptPreview = rows.Values
                .OrderByDescending(item => item.copySource)
                .ThenBy(item => item.typeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            dependencyPreviewHash = AssetDatabase.GetAssetDependencyHash(sourcePrefabPath).ToString();
        }

        private static bool IsUnsafeRuntimeScript(string path, string typeName)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(
                       typeName,
                       "Hollow.HoUnityTools.BoneRendering.HoBoneRenderer",
                       StringComparison.Ordinal);
        }

        private static string GetScriptNote(string path, string typeName, bool unsafeForRuntime)
        {
            if (unsafeForRuntime)
            {
                if (string.Equals(typeName, "Hollow.HoUnityTools.BoneRendering.HoBoneRenderer", StringComparison.Ordinal))
                    return "HoBoneRenderer 含编辑器侧可视化逻辑，默认不复制，并建议从临时 Prefab 移除。";
                return "该脚本位于 Editor 目录，不能作为 Warudo 运行时组件打包。";
            }

            if (string.Equals(typeName, "Hollow.HoUnityTools.RigConstraints.HoAuxRig", StringComparison.Ordinal))
                return "HoAuxRig 运行时脚本可独立复制；UMod 会按完整类型名链接现有组件。";
            if (path.StartsWith("Packages/app.warudo.modtool/", StringComparison.OrdinalIgnoreCase))
                return "Warudo SDK 自带脚本：默认保留现有引用，不复制进角色 Mod。";
            if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return "外部包运行时脚本：默认不复制；启用前请确认它的源码依赖也能由 UMod 编译。";
            return "项目运行时脚本：勾选后会生成独立临时副本。";
        }

        private static bool ShouldCopyScriptByDefault(string path)
        {
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("Packages/com.hollow.hounitytools/", StringComparison.OrdinalIgnoreCase);
        }

        private void SetRuntimeScriptSelection(bool selected)
        {
            foreach (ScriptPreview item in scriptPreview)
            {
                if (!item.removeWhenExcluded)
                    item.copySource = selected;
            }
        }

        private void RefreshExportSettingsPreview()
        {
            exportSettingsPath = FindExportSettingsAssetPath();
            activeModAssetPath = string.Empty;
            if (string.IsNullOrEmpty(exportSettingsPath))
                return;

            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportSettingsPath);
            if (settings == null)
                return;

            int ignoredIndex;
            TryReadActiveModAssetPath(settings, out activeModAssetPath, out ignoredIndex);
        }

        private void BeginBuild()
        {
            if (HasPendingBuildState())
                throw new InvalidOperationException("已有未完成的 FastBuild，请等待恢复流程完成。");

            SynchronizeSourcePrefab();
            if (!IsDependencyPreviewCurrent())
                RefreshDependencyPreview(true);

            RefreshExportSettingsPreview();
            if (!IsPrefab(sourcePrefabPath))
                throw new InvalidOperationException("源 Prefab 无效。");
            if (string.IsNullOrEmpty(exportSettingsPath))
                throw new InvalidOperationException("找不到 UMod ExportSettings 资源。");

            BuildState state = null;
            try
            {
                state = CreateStagedBuildState();
                WriteBuildState(state);
                SessionState.SetString(PendingStateSessionKey, state.stateFilePath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                lastBuildStatus = "临时目录已生成，正在等待脚本编译后调用 UMod 官方构建 API。";
                SchedulePendingBuildResume();
            }
            catch (Exception exception)
            {
                if (state != null)
                    FinishBuildState(state, true);
                Debug.LogException(exception);
                lastBuildStatus = "准备构建失败：\n" + GetRootMessage(exception);
                EditorUtility.DisplayDialog(WindowTitle, lastBuildStatus, "确定");
            }
        }

        private bool IsDependencyPreviewCurrent()
        {
            if (!IsPrefab(sourcePrefabPath) || string.IsNullOrEmpty(dependencyPreviewHash))
                return false;

            return string.Equals(
                dependencyPreviewHash,
                AssetDatabase.GetAssetDependencyHash(sourcePrefabPath).ToString(),
                StringComparison.Ordinal);
        }

        private BuildState CreateStagedBuildState()
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportSettingsPath);
            if (settings == null)
                throw new InvalidOperationException("无法加载 ExportSettings：" + exportSettingsPath);

            string originalModPath;
            int activeProfileIndex;
            if (!TryReadActiveModAssetPath(settings, out originalModPath, out activeProfileIndex))
                throw new InvalidOperationException("ExportSettings 中找不到活动配置的 modAssetPath。");

            string buildId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string temporaryRoot = TemporaryAssetRoot + "/" + buildId;
            string temporaryPrefabPath = temporaryRoot + "/Character.prefab";
            string stateFilePath = Path.Combine(StateDirectory, buildId + ".json");
            var state = new BuildState
            {
                phase = PendingPhase,
                stateFilePath = stateFilePath,
                temporaryAssetRoot = temporaryRoot,
                temporaryPrefabPath = temporaryPrefabPath,
                exportSettingsPath = exportSettingsPath,
                originalModAssetPath = originalModPath,
                activeProfileIndex = activeProfileIndex,
                cleanupTemporaryAssets = cleanupTemporaryAssets,
            };

            try
            {
                EnsureAssetFolder(temporaryRoot);
                if (!AssetDatabase.CopyAsset(sourcePrefabPath, temporaryPrefabPath))
                    throw new InvalidOperationException("无法复制源 Prefab 到临时目录。");

                var mappings = new List<ScriptMapping>();
                if (copySelectedScripts)
                    StageSelectedScripts(temporaryRoot);

                if (removeUnsafeComponents)
                {
                    foreach (ScriptPreview item in scriptPreview)
                    {
                        if (!item.removeWhenExcluded)
                            continue;
                        mappings.Add(new ScriptMapping
                        {
                            sourcePath = item.sourcePath,
                            removeFromPrefab = true,
                        });
                    }
                }

                state.scripts = mappings.ToArray();
                return state;
            }
            catch
            {
                if (IsSafeTemporaryAssetPath(temporaryRoot))
                    AssetDatabase.DeleteAsset(temporaryRoot);
                throw;
            }
        }

        private void StageSelectedScripts(string temporaryRoot)
        {
            string scriptsRoot = temporaryRoot + "/" + StagedScriptsDirectoryName;
            EnsureAssetFolder(scriptsRoot);
            int scriptIndex = 0;

            foreach (ScriptPreview item in scriptPreview)
            {
                if (!item.copySource || item.removeWhenExcluded)
                    continue;

                string absoluteSource = AssetPathToAbsolute(item.sourcePath);
                if (!File.Exists(absoluteSource))
                    throw new FileNotFoundException("找不到脚本源码", absoluteSource);

                string baseName = Path.GetFileName(item.sourcePath);
                string scriptFolderName = scriptIndex.ToString("D3") + "_" +
                                          SanitizeFileName(Path.GetFileNameWithoutExtension(baseName));
                scriptIndex++;
                string stagedFolder = scriptsRoot + "/" + scriptFolderName;
                EnsureAssetFolder(stagedFolder);
                string stagedPath = stagedFolder + "/" + baseName;
                string absoluteDestination = AssetPathToAbsolute(stagedPath);
                string sourceText = File.ReadAllText(absoluteSource, Encoding.UTF8);
                sourceText = sourceText.Replace("\r\n", "\n").Replace("\r", "\n");
                string wrappedSource = "#if !UNITY_EDITOR\r\n" +
                                       sourceText.Replace("\n", "\r\n") +
                                       "\r\n#endif\r\n";
                File.WriteAllText(absoluteDestination, wrappedSource, new UTF8Encoding(false));
                WriteFreshMetaFile(absoluteDestination + ".meta");
            }
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
                builder.Append(invalidCharacters.Contains(character) ? '_' : character);
            return builder.Length == 0 ? "Script" : builder.ToString();
        }

        private static void WriteFreshMetaFile(string metaPath)
        {
            string content = "fileFormatVersion: 2\n" +
                             "guid: " + Guid.NewGuid().ToString("N") + "\n" +
                             "MonoImporter:\n" +
                             "  externalObjects: {}\n" +
                             "  serializedVersion: 2\n" +
                             "  defaultReferences: []\n" +
                             "  executionOrder: 0\n" +
                             "  icon: {instanceID: 0}\n" +
                             "  userData: \n" +
                             "  assetBundleName: \n" +
                             "  assetBundleVariant: \n";
            File.WriteAllText(metaPath, content, new UTF8Encoding(false));
        }

        private static void SchedulePendingBuildResume()
        {
            if (string.IsNullOrEmpty(SessionState.GetString(PendingStateSessionKey, string.Empty)))
                return;
            if (resumeHookInstalled)
                return;

            resumeHookInstalled = true;
            EditorApplication.update += ResumePendingBuild;
        }

        private static void ResumePendingBuild()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= ResumePendingBuild;
            resumeHookInstalled = false;

            string stateFilePath = SessionState.GetString(PendingStateSessionKey, string.Empty);
            if (string.IsNullOrEmpty(stateFilePath))
                return;

            BuildState state = null;
            bool stateValidated = false;
            string resultMessage = string.Empty;
            try
            {
                state = ReadBuildState(stateFilePath);
                if (state == null)
                    throw new InvalidOperationException("无法读取 FastBuild 临时状态。");
                ValidateBuildState(state, stateFilePath);
                stateValidated = true;

                if (string.Equals(state.phase, BuildingPhase, StringComparison.Ordinal))
                    throw new InvalidOperationException("上一次构建在 UMod 调用期间发生了域重载，已停止重复构建。");
                if (!string.Equals(state.phase, PendingPhase, StringComparison.Ordinal))
                    throw new InvalidOperationException("未知的 FastBuild 状态：" + state.phase);

                PrepareStagedPrefab(state);
                state.phase = BuildingPhase;
                WriteBuildState(state);
                ApplyTemporaryExportSettings(state);

                object result = InvokeOfficialBuild(state.exportSettingsPath);
                string resultSummary = ValidateBuildResult(result);
                Debug.Log("[HoUnityTools] FastBuild Warudo Mod 完成。" + resultSummary);
                resultMessage = "Warudo Mod 构建成功。\n" + resultSummary;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                resultMessage = "FastBuild 失败：\n" + GetRootMessage(exception);
            }

            UpdateOpenWindowStatus(resultMessage);

            if (!stateValidated)
            {
                QuarantinePendingState(stateFilePath);
                EditorUtility.DisplayDialog(WindowTitle, resultMessage, "确定");
                return;
            }

            if (!TryRestoreExportSettings(state))
            {
                EditorUtility.DisplayDialog(
                    WindowTitle,
                    resultMessage + "\n\n恢复 ExportSettings 失败，临时状态已保留，未执行清理。",
                    "确定");
                return;
            }

            try
            {
                EditorUtility.DisplayDialog(WindowTitle, resultMessage, "确定");
            }
            finally
            {
                CompleteBuildCleanup(state, state.cleanupTemporaryAssets);
            }
        }

        private static void UpdateOpenWindowStatus(string status)
        {
            foreach (HoFastBuildWarudoModWindow window in Resources.FindObjectsOfTypeAll<HoFastBuildWarudoModWindow>())
            {
                window.lastBuildStatus = status;
                window.Repaint();
            }
        }

        private static void PrepareStagedPrefab(BuildState state)
        {
            if (!IsPrefab(state.temporaryPrefabPath))
                throw new InvalidOperationException("临时 Character.prefab 已丢失：" + state.temporaryPrefabPath);

            var mappings = new Dictionary<string, ScriptMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptMapping mapping in state.scripts ?? Array.Empty<ScriptMapping>())
                mappings[mapping.sourcePath] = mapping;

            GameObject root = PrefabUtility.LoadPrefabContents(state.temporaryPrefabPath);
            try
            {
                root.name = "Character";
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null)
                        continue;

                    MonoScript currentScript = MonoScript.FromMonoBehaviour(behaviour);
                    string currentPath = currentScript == null
                        ? string.Empty
                        : AssetDatabase.GetAssetPath(currentScript);

                    ScriptMapping mapping;
                    if (string.IsNullOrEmpty(currentPath) || !mappings.TryGetValue(currentPath, out mapping))
                        continue;

                    if (mapping.removeFromPrefab)
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour, true);
                        continue;
                    }

                    // UMod links its compiled script by full type name. The original
                    // MonoScript reference must remain intact on the staged prefab.
                }

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, state.temporaryPrefabPath);
                if (savedPrefab == null)
                    throw new InvalidOperationException("无法保存临时 Character.prefab。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
        }

        private static object InvokeOfficialBuild(string settingsAssetPath)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(settingsAssetPath);
            if (settings == null)
                throw new InvalidOperationException("无法加载 UMod ExportSettings：" + settingsAssetPath);

            Type toolsType = FindLoadedType("UMod.BuildEngine.ModToolsUtil");
            if (toolsType == null)
                throw new InvalidOperationException("未加载 UMod.BuildEngine.ModToolsUtil，请确认 Warudo Mod SDK 已导入当前工程。");

            MethodInfo method = toolsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(candidate => string.Equals(candidate.Name, "StartBuild", StringComparison.Ordinal))
                .Where(candidate =>
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    return parameters.Length >= 1 &&
                           parameters.Length <= 2 &&
                           parameters[0].ParameterType.IsInstanceOfType(settings) &&
                           parameters.Skip(1).All(parameter =>
                               parameter.HasDefaultValue || typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
                })
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();
            if (method == null)
                throw new MissingMethodException("未找到兼容的 ModToolsUtil.StartBuild(ExportSettings) 方法。");

            ParameterInfo[] methodParameters = method.GetParameters();
            object[] arguments = new object[methodParameters.Length];
            arguments[0] = settings;
            for (int index = 1; index < arguments.Length; index++)
            {
                arguments[index] = methodParameters[index].HasDefaultValue
                    ? methodParameters[index].DefaultValue
                    : null;
            }

            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void ApplyTemporaryExportSettings(BuildState state)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(state.exportSettingsPath);
            if (settings == null)
                throw new InvalidOperationException("无法加载 UMod ExportSettings：" + state.exportSettingsPath);

            string currentModPath;
            int currentProfileIndex;
            if (!TryReadActiveModAssetPath(settings, out currentModPath, out currentProfileIndex) ||
                currentProfileIndex != state.activeProfileIndex ||
                !string.Equals(
                    NormalizePath(currentModPath),
                    NormalizePath(state.originalModAssetPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("活动 UMod ExportProfile 在构建等待期间发生变化，请重新开始 FastBuild。");

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || state.activeProfileIndex < 0 ||
                state.activeProfileIndex >= profiles.arraySize)
                throw new InvalidOperationException("活动 UMod ExportProfile 已发生变化。");

            SerializedProperty profile = profiles.GetArrayElementAtIndex(state.activeProfileIndex);
            SerializedProperty pathProperty = profile.FindPropertyRelative("modAssetPath");
            if (pathProperty == null)
                throw new InvalidOperationException("ExportProfile 中找不到 modAssetPath。");

            string absoluteTemporaryRoot;
            if (!TryGetSafeTemporaryAbsolutePath(state.temporaryAssetRoot, out absoluteTemporaryRoot))
                throw new InvalidOperationException("FastBuild 临时目录越过了安全边界。");

            pathProperty.stringValue = NormalizePath(absoluteTemporaryRoot);
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static string ValidateBuildResult(object result)
        {
            if (result == null)
                return "UMod 未返回结果对象，请查看 Build.log。";

            object successfulValue = GetMemberValue(result, "Successful");
            if (successfulValue is bool && !(bool)successfulValue)
            {
                string error = Convert.ToString(GetMemberValue(result, "ErrorMessage"));
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "UMod 构建失败。" : error);
            }

            string builtFile = Convert.ToString(GetMemberValue(result, "BuiltModFile"));
            return string.IsNullOrEmpty(builtFile)
                ? "请查看 UMod Build.log 获取输出路径。"
                : "输出：" + builtFile;
        }

        private static object GetMemberValue(object target, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(memberName, flags);
            return field == null ? null : field.GetValue(target);
        }

        private static bool TryValidateOfficialBuildApi(out string error)
        {
            Type settingsType = FindLoadedType("UMod.ModTools.Export.ExportSettings");
            if (settingsType == null)
            {
                error = "未检测到 UMod ExportSettings。请先导入 Warudo Mod SDK。";
                return false;
            }

            Type toolsType = FindLoadedType("UMod.BuildEngine.ModToolsUtil");
            if (toolsType == null || !toolsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(method =>
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        return string.Equals(method.Name, "StartBuild", StringComparison.Ordinal) &&
                               parameters.Length >= 1 && parameters.Length <= 2 &&
                               parameters[0].ParameterType == settingsType;
                    }))
            {
                error = "未检测到 UMod 官方 StartBuild API。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static string FindExportSettingsAssetPath()
        {
            string[] guids = AssetDatabase.FindAssets("t:ExportSettings");
            if (guids.Length == 0)
                guids = AssetDatabase.FindAssets("ExportSettings");

            var exactMatches = new List<string>();
            var namedFallbacks = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                    continue;

                if (asset.GetType().FullName == "UMod.ModTools.Export.ExportSettings")
                    exactMatches.Add(path);
                else if (string.Equals(
                             Path.GetFileNameWithoutExtension(path),
                             "ExportSettings",
                             StringComparison.OrdinalIgnoreCase))
                    namedFallbacks.Add(path);
            }

            if (exactMatches.Count == 1)
                return exactMatches[0];
            if (exactMatches.Count > 1)
            {
                Debug.LogError("[HoUnityTools] 找到多个 UMod ExportSettings，无法安全判断活动工作区。");
                return string.Empty;
            }
            if (namedFallbacks.Count == 1)
                return namedFallbacks[0];
            return string.Empty;
        }

        private static bool TryReadActiveModAssetPath(
            UnityEngine.Object settings,
            out string modAssetPath,
            out int activeProfileIndex)
        {
            modAssetPath = string.Empty;
            activeProfileIndex = 0;

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty activeProfile = serializedSettings.FindProperty("activeProfile");
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || profiles.arraySize == 0)
                return false;

            if (activeProfile != null &&
                (activeProfile.intValue < 0 || activeProfile.intValue >= profiles.arraySize))
                return false;

            activeProfileIndex = activeProfile == null ? 0 : activeProfile.intValue;
            SerializedProperty profile = profiles.GetArrayElementAtIndex(activeProfileIndex);
            SerializedProperty pathProperty = profile.FindPropertyRelative("modAssetPath");
            if (pathProperty == null)
                return false;

            modAssetPath = pathProperty.stringValue;
            return true;
        }

        private static void FinishBuildState(BuildState state, bool removeTemporaryAssets)
        {
            if (TryRestoreExportSettings(state))
                CompleteBuildCleanup(state, removeTemporaryAssets);
        }

        private static bool TryRestoreExportSettings(BuildState state)
        {
            try
            {
                RestoreExportSettings(state);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[HoUnityTools] 恢复 ExportSettings 失败：" + GetRootMessage(exception));
                if (IsSafeStateFilePath(state.stateFilePath))
                    SessionState.SetString(PendingStateSessionKey, state.stateFilePath);
                Debug.LogError("[HoUnityTools] 已保留 FastBuild 状态与临时目录，等待下次域重载重试恢复。");
                return false;
            }
        }

        private static void CompleteBuildCleanup(BuildState state, bool removeTemporaryAssets)
        {
            ClearPendingState(state.stateFilePath);

            string absoluteTemporaryPath;
            if (removeTemporaryAssets &&
                TryGetSafeTemporaryAbsolutePath(state.temporaryAssetRoot, out absoluteTemporaryPath))
            {
                AssetDatabase.DeleteAsset(state.temporaryAssetRoot);
                if (Directory.Exists(absoluteTemporaryPath))
                    Directory.Delete(absoluteTemporaryPath, true);
                DeleteStagingRootWhenEmpty();
                AssetDatabase.Refresh();
            }
            else if (!removeTemporaryAssets)
            {
                Debug.Log("[HoUnityTools] 已保留 FastBuild 临时目录：" + state.temporaryAssetRoot);
            }
        }

        private static void RestoreExportSettings(BuildState state)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(state.exportSettingsPath);
            if (settings == null)
                throw new InvalidOperationException("无法加载原 ExportSettings：" + state.exportSettingsPath);

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || state.activeProfileIndex < 0 ||
                state.activeProfileIndex >= profiles.arraySize)
                throw new InvalidOperationException("原 ExportProfile 已不存在，无法恢复 modAssetPath。");

            SerializedProperty profile = profiles.GetArrayElementAtIndex(state.activeProfileIndex);
            SerializedProperty pathProperty = profile.FindPropertyRelative("modAssetPath");
            if (pathProperty == null)
                throw new InvalidOperationException("原 ExportProfile 中找不到 modAssetPath。");

            pathProperty.stringValue = state.originalModAssetPath;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void DeleteStagingRootWhenEmpty()
        {
            string absoluteRoot = AssetPathToAbsolute(TemporaryAssetRoot);
            if (!Directory.Exists(absoluteRoot) || Directory.GetDirectories(absoluteRoot).Length != 0)
                return;

            string[] remainingFiles = Directory.GetFiles(absoluteRoot)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (remainingFiles.Length == 0)
                AssetDatabase.DeleteAsset(TemporaryAssetRoot);
        }

        private static void ClearPendingState(string stateFilePath)
        {
            SessionState.EraseString(PendingStateSessionKey);
            if (string.IsNullOrEmpty(stateFilePath) || !File.Exists(stateFilePath))
                return;

            if (!IsSafeStateFilePath(stateFilePath))
            {
                Debug.LogWarning("[HoUnityTools] 拒绝删除工作目录之外的状态文件：" + stateFilePath);
                return;
            }

            try
            {
                File.Delete(stateFilePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[HoUnityTools] 无法删除 FastBuild 状态文件：" + exception.Message);
            }
        }

        private static void QuarantinePendingState(string stateFilePath)
        {
            SessionState.EraseString(PendingStateSessionKey);
            if (!IsSafeStateFilePath(stateFilePath) || !File.Exists(stateFilePath))
                return;

            try
            {
                string quarantinePath = stateFilePath + ".invalid";
                if (File.Exists(quarantinePath))
                    quarantinePath += "." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Move(stateFilePath, quarantinePath);
                Debug.LogError("[HoUnityTools] FastBuild 状态校验失败，已保留为：" + quarantinePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[HoUnityTools] 无法隔离损坏的 FastBuild 状态：" + exception.Message);
            }
        }

        private static void WriteBuildState(BuildState state)
        {
            if (state == null || !IsSafeStateFilePath(state.stateFilePath))
                throw new InvalidOperationException("FastBuild 状态文件路径不在允许的 Library 工作目录内。");

            string directory = Path.GetDirectoryName(state.stateFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(state.stateFilePath, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
        }

        private static BuildState ReadBuildState(string stateFilePath)
        {
            if (!IsSafeStateFilePath(stateFilePath) || !File.Exists(stateFilePath))
                return null;
            return JsonUtility.FromJson<BuildState>(File.ReadAllText(stateFilePath, Encoding.UTF8));
        }

        private static void ValidateBuildState(BuildState state, string expectedStateFilePath)
        {
            if (!AreSameFullPath(state.stateFilePath, expectedStateFilePath))
                throw new InvalidOperationException("FastBuild 状态文件身份不匹配。");

            string ignored;
            if (!TryGetSafeTemporaryAbsolutePath(state.temporaryAssetRoot, out ignored))
                throw new InvalidOperationException("FastBuild 临时目录越过了安全边界。");

            string expectedPrefabPath = NormalizeAssetPath(state.temporaryAssetRoot).TrimEnd('/') + "/Character.prefab";
            if (!string.Equals(
                    NormalizeAssetPath(state.temporaryPrefabPath),
                    expectedPrefabPath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("FastBuild 临时 Prefab 路径无效。");

            string currentSettingsPath = FindExportSettingsAssetPath();
            if (string.IsNullOrEmpty(currentSettingsPath) ||
                !string.Equals(
                    NormalizeAssetPath(state.exportSettingsPath),
                    NormalizeAssetPath(currentSettingsPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FastBuild ExportSettings 身份不匹配。");
        }

        private static bool IsSafeStateFilePath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                string stateDirectory = StateDirectory;
                return string.Equals(
                    Path.GetDirectoryName(fullPath),
                    stateDirectory,
                    FileSystemPathComparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasPendingBuildState()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingStateSessionKey, string.Empty)))
                return true;

            try
            {
                return Directory.Exists(StateDirectory) &&
                       Directory.GetFiles(StateDirectory, "*.json", SearchOption.TopDirectoryOnly)
                           .Any(IsSafeStateFilePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool AreSameFullPath(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), FileSystemPathComparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath).TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            string[] segments = normalized.Split('/');
            if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
                throw new ArgumentException("临时目录必须位于 Assets 下。", nameof(assetPath));

            string current = "Assets";
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static bool IsSafeTemporaryAssetPath(string path)
        {
            string ignored;
            return TryGetSafeTemporaryAbsolutePath(path, out ignored);
        }

        private static bool TryGetSafeTemporaryAbsolutePath(string path, out string absolutePath)
        {
            absolutePath = string.Empty;
            string normalized = NormalizeAssetPath(path).TrimEnd('/');
            if (Path.IsPathRooted(normalized) ||
                !normalized.StartsWith(TemporaryAssetRoot + "/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(segment => segment == "." || segment == ".." || segment.Length == 0))
                return false;

            try
            {
                string root = Path.GetFullPath(AssetPathToAbsolute(TemporaryAssetRoot))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(AssetPathToAbsolute(normalized));
                if (!candidate.StartsWith(root, FileSystemPathComparison) ||
                    string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), FileSystemPathComparison))
                    return false;
                absolutePath = candidate;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static StringComparison FileSystemPathComparison
        {
            get
            {
#if UNITY_EDITOR_WIN
                return StringComparison.OrdinalIgnoreCase;
#else
                return StringComparison.Ordinal;
#endif
            }
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (Path.IsPathRooted(normalized))
                return Path.GetFullPath(normalized);

            if (normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                UnityEditor.PackageManager.PackageInfo package =
                    UnityEditor.PackageManager.PackageInfo.FindForAssetPath(normalized);
                if (package == null || string.IsNullOrEmpty(package.resolvedPath) ||
                    string.IsNullOrEmpty(package.assetPath))
                    throw new InvalidOperationException("无法解析 Package 源码路径：" + assetPath);

                string relativePath = normalized.Substring(package.assetPath.Length).TrimStart('/');
                return Path.GetFullPath(Path.Combine(
                    package.resolvedPath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            return Path.GetFullPath(Path.Combine(ProjectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        private static string StateDirectory
        {
            get { return Path.GetFullPath(Path.Combine(ProjectRoot, "Library", "HoFastBuildWarudoMod")); }
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string NormalizeAssetPath(string path)
        {
            return NormalizePath(path ?? string.Empty);
        }

        private static string GetSelectedPrefabPath()
        {
            return Selection.activeObject == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(Selection.activeObject);
        }

        private static bool IsPrefab(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   string.Equals(Path.GetExtension(path), ".prefab", StringComparison.OrdinalIgnoreCase) &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        private static string GetRootMessage(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;
            return current.Message;
        }
    }
}
#endif
