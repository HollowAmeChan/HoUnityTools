#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    internal sealed class HoFastBuildWarudoModWindow : EditorWindow
    {
        private const string MenuPath = "Assets/HoUnityTools/FastBuildWarudoMod";
        private const string TopLevelMenuPath = "HoUnityTools/FastBuildWarudoMod";
        private const string WindowTitle = "FastBuild Warudo Mod";
        private const string PendingStateSessionKey = "HoUnityTools.FastBuildWarudoMod.PendingState";
        private const string TemporaryAssetRoot = "Assets/HoFastBuildWarudoModTemp";
        private const string StagedScriptsDirectoryName = "Scripts";
        private const string StagedResourcesDirectoryName = "Resources";
        private const string RuntimeBoneDebugResourceKey = "HoRuntimeDebugLine";
        private const string RuntimeBoneDebugTypeName =
            "Hollow.HoUnityTools.WarudoModUtils.HoRuntimeBoneDebugRenderer";
        private const string FbxSdkRuntimeDefine = "FBXSDK_RUNTIME";
        private const string PendingPhase = "AwaitingCompile";
        private const string BuildingPhase = "Building";

        // 面板统一的行列尺寸：所有列表共用同一组宽度，列才会对齐。
        private const float RowControlHeight = 18f;
        private const float IconButtonWidth = 22f;
        private const float ToggleColumnWidth = 18f;
        private const float FoldColumnWidth = 16f;
        private const float MountColumnWidth = 72f;
        private const float StatusColumnWidth = 58f;
        private const float PathColumnWidth = 170f;
        private const float SourceColumnWidth = 190f;
        private const float FormLabelWidth = 88f;

        /// <summary>工作区操作反馈是瞬时信息，过一段时间自动消失，避免常驻噪音。</summary>
        private const double StatusMessageLifetime = 10.0;

        [Serializable]
        private sealed class ScriptPreview
        {
            public string sourcePath = string.Empty;
            public string typeName = string.Empty;
            public string note = string.Empty;
            public bool copySource;
            public bool removeWhenExcluded;
            public bool hostProvided;
            public int referenceCount;
            public List<string> componentReferences = new List<string>();
            public bool showReferencedAssets;
            public List<RuntimeAssetPreview> referencedAssets = new List<RuntimeAssetPreview>();
        }

        [Serializable]
        private sealed class RuntimeAssetPreview
        {
            public string resourceKey = string.Empty;
            public string assetPath = string.Empty;
            public bool found;
            public bool willCopy;
            public List<string> componentReferences = new List<string>();
        }

        [Serializable]
        private sealed class ScriptMapping
        {
            public string sourcePath = string.Empty;
            public string stagedPath = string.Empty;
            public bool removeFromPrefab;
        }

        /// <summary>
        /// 一个 Warudo 工作区就是 ExportSettings.exportProfiles 里的一项（官方称 Export Profile）。
        /// 这里保存复核后的只读快照，供面板直接绘制。
        /// </summary>
        [Serializable]
        private sealed class WorkspaceEntry
        {
            public int index;
            public string modName = string.Empty;
            public string modAuthor = string.Empty;
            public string modVersion = string.Empty;
            public string modDescription = string.Empty;
            public string modAssetPath = string.Empty;
            public string modExportPath = string.Empty;
            public int referencedModCount;
            public bool isActive;
            public bool isDuplicateName;
            public bool nameValid;
            public bool assetPathValid;
            public bool assetPathExists;
            public bool assetPathUnderAssets;
            public bool assetPathIsAssetsRoot;
            public bool exportPathExists;
            public string statusNote = string.Empty;
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
            public string originalStandaloneDefines = string.Empty;
            public bool managesStandaloneDefines;
            public int activeProfileIndex;
            public bool cleanupTemporaryAssets = true;
            public ScriptMapping[] scripts = Array.Empty<ScriptMapping>();
        }

        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private string sourcePrefabPath = string.Empty;
        [SerializeField] private List<ScriptPreview> scriptPreview = new List<ScriptPreview>();
        [SerializeField] private List<RuntimeAssetPreview> runtimeAssetPreview = new List<RuntimeAssetPreview>();
        [SerializeField] private Vector2 pageScroll;
        [SerializeField] private Vector2 scriptScroll;
        [SerializeField] private Vector2 verificationScroll;
        [SerializeField] private bool copySelectedScripts = true;
        [SerializeField] private bool removeUnsafeComponents = true;
        [SerializeField] private bool cleanupTemporaryAssets = true;
        [SerializeField] private int dependencyCount;
        [SerializeField] private int nonScriptDependencyCount;
        [SerializeField] private int missingScriptCount;
        [SerializeField] private string exportSettingsPath = string.Empty;
        [SerializeField] private List<WorkspaceEntry> workspaceEntries = new List<WorkspaceEntry>();
        [SerializeField] private int activeWorkspaceIndex = -1;
        [SerializeField] private string workspaceStatusMessage = string.Empty;
        [SerializeField] private double workspaceStatusMessageExpiry;
        [SerializeField] private Vector2 workspaceScroll;
        [SerializeField] private bool showWorkspaceDetails = true;
        [SerializeField] private bool workspaceHasDuplicateNames;
        [SerializeField] private string lastBuildStatus = string.Empty;
        [SerializeField] private string dependencyPreviewHash = string.Empty;
        [SerializeField] private string lastArtifactPath = string.Empty;
        [SerializeField] private string lastVerificationSummary = string.Empty;
        [SerializeField] private string lastVerificationReport = string.Empty;
        [SerializeField] private bool lastVerificationHasProblems;
        [SerializeField] private bool showVerificationReport;
        [SerializeField] private List<HoFastBuildExpectedComponent> lastExpectedComponents =
            new List<HoFastBuildExpectedComponent>();

        private GUIStyle panelTitleStyle;
        private GUIStyle panelStatusStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle statusColumnStyle;
        private GUIStyle centeredIconButtonStyle;
        private string lastStampedWorkspaceStatus;
        private bool sdkAvailable;
        private string sdkError = string.Empty;

        private static bool resumeHookInstalled;
        private static readonly Regex ResourcesLoadPattern = new Regex(
            @"Resources\s*\.\s*Load(?:Async)?(?:\s*<[^>]+>)?\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);

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

        [MenuItem(TopLevelMenuPath, false, 40)]
        internal static void ShowWindow()
        {
            HoFastBuildWarudoModWindow window = OpenWindow();

            // 顶栏入口没有 Prefab 上下文。仅在窗口还没选定源 Prefab 时采用当前选择，
            // 避免覆盖正在审查的依赖勾选状态。
            if (string.IsNullOrEmpty(window.sourcePrefabPath))
            {
                string path = GetSelectedPrefabPath();
                if (IsPrefab(path))
                    window.SetSourcePrefab(path);
            }
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

            OpenWindow().SetSourcePrefab(path);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateOpenFromSelection()
        {
            return IsPrefab(GetSelectedPrefabPath());
        }

        private static HoFastBuildWarudoModWindow OpenWindow()
        {
            var window = GetWindow<HoFastBuildWarudoModWindow>(WindowTitle);
            window.minSize = new Vector2(600f, 520f);
            window.Show();
            return window;
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
            EditorGUIUtility.labelWidth = FormLabelWidth + 8f;
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
                        GUILayout.Space(8f);
                        DrawVerificationPanel();
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
                    sdkAvailable ? new Color(0.20f, 0.68f, 0.57f) : new Color(0.88f, 0.42f, 0.28f),
                    "Prefab Icon",
                    "复制当前 Prefab 为 Character 并调用 UMod 官方构建，源资源不会被修改。");

                if (!sdkAvailable)
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
                RefreshWorkspacePreview();
            }
        }

        private void DrawSourcePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "源 Prefab",
                    IsPrefab(sourcePrefabPath) ? "已选择" : "未选择",
                    new Color(0.24f, 0.54f, 0.88f),
                    "GameObject Icon",
                    "FastBuild 会复制这个 Prefab 作为临时 Character 根节点。");
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

                if (!string.IsNullOrEmpty(sourcePrefabPath))
                {
                    GUILayout.Label(
                        new GUIContent(sourcePrefabPath, sourcePrefabPath),
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawExportSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string status;
                if (string.IsNullOrEmpty(exportSettingsPath))
                    status = "未找到";
                else if (workspaceEntries.Count == 0)
                    status = "无工作区";
                else
                    status = workspaceEntries.Count + " 个工作区";

                DrawPanelHeader(
                    "Warudo 工作区",
                    status,
                    new Color(0.20f, 0.68f, 0.57f),
                    "Folder Icon",
                    "工作区就是 UMod ExportSettings 里的 Export Profile，决定 Mod 名称、资产目录和导出目录。",
                    string.IsNullOrEmpty(exportSettingsPath)
                        ? "当前工程没有找到 UMod ExportSettings 资源。"
                        : "ExportSettings：" + exportSettingsPath);

                GUILayout.Space(5f);

                RefreshWorkspaceStatusLifetime();
                if (string.IsNullOrEmpty(exportSettingsPath))
                {
                    EditorGUILayout.HelpBox(
                        "当前工程没有找到 UMod ExportSettings 资源，无法管理工作区。",
                        MessageType.Warning);
                    return;
                }

                DrawWorkspaceToolbar();

                if (HasLiveStatusMessage())
                {
                    EditorGUILayout.HelpBox(workspaceStatusMessage, MessageType.None);
                }

                DrawWorkspaceList();
                DrawWorkspaceDetails();
            }
        }

        private void DrawWorkspaceToolbar()
        {
            bool hasPendingBuild = HasPendingBuildState();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(hasPendingBuild))
                {
                    if (GUILayout.Button("新建工作区", EditorStyles.miniButton, GUILayout.Width(84f)))
                        CreateWorkspace();

                    using (new EditorGUI.DisabledScope(activeWorkspaceIndex < 0 || workspaceEntries.Count <= 1))
                    {
                        if (GUILayout.Button("删除当前", EditorStyles.miniButton, GUILayout.Width(68f)))
                            DeleteActiveWorkspace();
                    }

                    if (workspaceHasDuplicateNames)
                    {
                        if (GUILayout.Button("清理重名", EditorStyles.miniButton, GUILayout.Width(68f)))
                            RemoveDuplicateWorkspaces();
                    }
                }

                if (hasPendingBuild)
                    GUILayout.Label("构建进行中，工作区已锁定", EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                if (DrawIconButton("Refresh", "刷新", "重新读取 UMod ExportSettings 与工作区列表"))
                {
                    SetWorkspaceStatus(string.Empty);
                    RefreshExportSettingsPreview();
                }

                if (DrawIconButton("Settings", "官方设置", "打开 uMod 官方设置窗口，编辑工作区的完整字段"))
                    OpenOfficialExportSettingsWindow();
            }

            RefreshWorkspacePreviewIfDirty();
        }

        private void DrawWorkspaceList()
        {
            if (workspaceEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前 ExportSettings 还没有工作区。工作区决定 Mod 名称、资产目录和导出目录；" +
                    "至少需要一个工作区才能构建。",
                    MessageType.Warning);
                return;
            }

            GUILayout.Space(3f);
            workspaceScroll = EditorGUILayout.BeginScrollView(
                workspaceScroll,
                GUILayout.MinHeight(Mathf.Min(150f, 26f * workspaceEntries.Count)),
                GUILayout.MaxHeight(150f));

            // 先画完整个列表再切换，避免在布局过程中改变控件数量。
            bool locked = HasPendingBuildState();
            int requestedIndex = -1;
            for (int index = 0; index < workspaceEntries.Count; index++)
            {
                if (index > 0)
                    DrawRowSeparator();

                int requested = DrawWorkspaceRow(workspaceEntries[index], locked);
                if (requested >= 0)
                    requestedIndex = requested;
            }

            EditorGUILayout.EndScrollView();

            if (requestedIndex >= 0)
                SetActiveWorkspace(requestedIndex);
        }

        /// <summary>返回需要切换到的目标下标；不需要切换时返回 -1。</summary>
        private int DrawWorkspaceRow(WorkspaceEntry entry, bool locked)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowControlHeight);

            string displayName = !string.IsNullOrEmpty(entry.modName) ? entry.modName : "<未命名>";
            string tooltip =
                "Mod 名称：" + displayName +
                "\nMod 资产目录：" + (string.IsNullOrEmpty(entry.modAssetPath) ? "(未设置)" : entry.modAssetPath) +
                "\n导出目录：" + (string.IsNullOrEmpty(entry.modExportPath) ? "(未设置)" : entry.modExportPath) +
                "\n版本：" + (string.IsNullOrEmpty(entry.modVersion) ? "(未设置)" : entry.modVersion);
            if (!string.IsNullOrEmpty(entry.statusNote))
                tooltip += "\n注意：" + entry.statusNote;

            string statusText = !entry.nameValid
                ? "未命名"
                : entry.isDuplicateName
                    ? "重名"
                    : !entry.assetPathValid
                        ? "目录无效"
                        : "就绪";

            float statusWidth = StatusColumnWidth;
            float pathWidth = Mathf.Min(PathColumnWidth, Mathf.Max(60f, rowRect.width * 0.3f));
            var radioRect = new Rect(rowRect.x, rowRect.y, Mathf.Max(80f, rowRect.width - statusWidth - pathWidth), rowRect.height);
            var pathRect = new Rect(radioRect.xMax, rowRect.y, pathWidth, rowRect.height);
            var statusRect = new Rect(pathRect.xMax, rowRect.y, statusWidth, rowRect.height);

            bool selected;
            using (new EditorGUI.DisabledScope(locked))
                selected = GUI.Toggle(radioRect, entry.isActive, new GUIContent(displayName, tooltip), EditorStyles.radioButton);

            string shortPath = FormatWorkspaceAssetPath(entry.modAssetPath);
            GUI.Label(
                pathRect,
                new GUIContent(TruncateToWidth(shortPath, EditorStyles.miniLabel, pathRect.width), entry.modAssetPath),
                EditorStyles.miniLabel);
            GUI.Label(
                statusRect,
                new GUIContent(TruncateToWidth(statusText, statusColumnStyle, statusRect.width), entry.statusNote ?? string.Empty),
                statusColumnStyle);

            if (selected && !entry.isActive)
                return entry.index;

            return -1;
        }

        /// <summary>列表里显示工程内相对路径，完整路径留在 tooltip 里。</summary>
        private static string FormatWorkspaceAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "—";

            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            string dataPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(dataPath + "/", FileSystemPathComparison))
                return "Assets/" + normalized.Substring(dataPath.Length + 1);

            return normalized;
        }

        private void DrawWorkspaceDetails()
        {
            if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaceEntries.Count)
                return;

            WorkspaceEntry entry = workspaceEntries[activeWorkspaceIndex];
            GUILayout.Space(4f);
            showWorkspaceDetails = EditorGUILayout.Foldout(showWorkspaceDetails, "当前工作区详情");
            if (!showWorkspaceDetails)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Texture2D icon = LoadWorkspaceIcon(activeWorkspaceIndex);
                    if (icon != null)
                    {
                        Rect iconRect = GUILayoutUtility.GetRect(40f, 40f);
                        EditorGUI.DrawPreviewTexture(iconRect, icon);
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawDetailRow("Mod 名称", entry.modName);
                        DrawDetailRow("作者", entry.modAuthor);
                        DrawDetailRow("版本", entry.modVersion);
                    }
                }

                if (!string.IsNullOrEmpty(entry.modDescription))
                    DrawDetailRow("说明", entry.modDescription, entry.modDescription);

                GUILayout.Space(2f);
                DrawWorkspacePathRow(
                    "资产目录",
                    entry.modAssetPath,
                    DescribeWorkspaceAssetPath(entry),
                    entry.assetPathValid ? MessageType.None : MessageType.Warning,
                    entry.assetPathUnderAssets && !entry.assetPathExists && !entry.assetPathIsAssetsRoot,
                    "创建",
                    CreateWorkspaceAssetFolder);

                DrawWorkspacePathRow(
                    "导出目录",
                    entry.modExportPath,
                    DescribeWorkspaceExportPath(entry),
                    entry.exportPathExists ? MessageType.None : MessageType.Warning,
                    entry.exportPathExists,
                    "定位",
                    RevealWorkspaceExportFolder);

                if (entry.referencedModCount > 0)
                    DrawDetailRow("引用 Mod", entry.referencedModCount + " 个");

                // 只报告不通过的项，全部通过时一句话带过。
                string validation = DescribeWorkspaceValidation();
                if (!string.IsNullOrEmpty(validation))
                    EditorGUILayout.LabelField(validation, EditorStyles.miniLabel);
            }
        }

        private void DrawWorkspacePathRow(
            string label,
            string path,
            string description,
            MessageType messageType,
            bool actionEnabled,
            string actionLabel,
            Action action)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(FormLabelWidth));
                GUILayout.Label(
                    new GUIContent(string.IsNullOrEmpty(path) ? "—" : path, string.IsNullOrEmpty(path) ? string.Empty : path),
                    EditorStyles.label);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!actionEnabled))
                {
                    if (GUILayout.Button(actionLabel, EditorStyles.miniButton, GUILayout.Width(48f)))
                        action();
                }
            }

            if (!string.IsNullOrEmpty(description))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (messageType == MessageType.None)
                        EditorGUILayout.LabelField(description, EditorStyles.miniLabel);
                    else
                        EditorGUILayout.HelpBox(description, messageType);
                }
            }
        }

        private static string DescribeWorkspaceAssetPath(WorkspaceEntry entry)
        {
            if (string.IsNullOrEmpty(entry.modAssetPath))
                return "未设置 Mod 资产目录。";
            if (!entry.assetPathExists)
                return "目录不存在：" + entry.modAssetPath;
            if (entry.assetPathIsAssetsRoot)
                return "不能直接使用 Assets 根目录，请为这个工作区建一个子目录。";
            if (!entry.assetPathUnderAssets)
                return "目录不在 Assets 下，UMod 无法把它作为 Mod 工作区。";
            return string.Empty;
        }

        private static string DescribeWorkspaceExportPath(WorkspaceEntry entry)
        {
            if (string.IsNullOrEmpty(entry.modExportPath))
                return "未设置导出目录，构建时不知道要把 .warudo 写到哪里。";
            if (!entry.exportPathExists)
                return "导出目录不存在：" + entry.modExportPath;
            return string.Empty;
        }

        private string DescribeWorkspaceValidation()
        {
            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
                return string.Empty;

            bool? name = InvokeSettingsValidation(settings, "ValidateName");
            bool? assetPath = InvokeSettingsValidation(settings, "ValidateAssetPath");
            bool? version = InvokeSettingsValidation(settings, "ValidateVersion");
            bool? buildAndRun = InvokeSettingsValidation(settings, "ValidateBuildAndRun");
            if (!name.HasValue && !assetPath.HasValue && !version.HasValue)
                return string.Empty;

            var failed = new List<string>();
            if (name == false)
                failed.Add("Mod 名称");
            if (assetPath == false)
                failed.Add("Mod 资产目录");
            if (version == false)
                failed.Add("版本号");
            if (buildAndRun == false)
                failed.Add("运行配置");

            if (failed.Count == 0)
                return "SDK 校验：通过";

            return "SDK 校验不通过：" + string.Join("、", failed.ToArray());
        }

        private static bool? InvokeSettingsValidation(UnityEngine.Object settings, string methodName)
        {
            try
            {
                MethodInfo method = settings.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null || method.ReturnType != typeof(bool))
                    return null;
                return (bool)method.Invoke(settings, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #region 工作区读写

        /// <summary>
        /// 检测工作区列表是否被外部改动（例如在 uMod 官方设置窗口里增删或切换）。
        /// 这里只比较数组长度和活动下标，逐帧读全部字段代价太高；字段内容的改动请用刷新按钮。
        /// </summary>
        private void RefreshWorkspacePreviewIfDirty()
        {
            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
                return;

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray)
                return;

            SerializedProperty activeProperty = serializedSettings.FindProperty("activeProfile");
            int activeValue = activeProperty == null ? -1 : activeProperty.intValue;

            if (profiles.arraySize != workspaceEntries.Count ||
                (workspaceEntries.Count > 0 && activeValue != activeWorkspaceIndex))
            {
                RefreshWorkspacePreview();
            }
        }

        private void RefreshWorkspacePreview()
        {
            workspaceEntries.Clear();
            activeWorkspaceIndex = -1;
            workspaceHasDuplicateNames = false;

            if (string.IsNullOrEmpty(exportSettingsPath))
                return;

            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportSettingsPath);
            if (settings == null)
                return;

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || profiles.arraySize == 0)
                return;

            SerializedProperty activeProperty = serializedSettings.FindProperty("activeProfile");
            int rawActive = activeProperty == null ? 0 : activeProperty.intValue;
            if (rawActive < 0 || rawActive >= profiles.arraySize)
                rawActive = 0;

            var seenNames = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < profiles.arraySize; index++)
            {
                SerializedProperty profile = profiles.GetArrayElementAtIndex(index);
                var entry = new WorkspaceEntry
                {
                    index = index,
                    modName = ReadRelativeString(profile, "modName"),
                    modAuthor = ReadRelativeString(profile, "modAuthor"),
                    modVersion = ReadRelativeString(profile, "modVersion"),
                    modDescription = ReadRelativeString(profile, "modDescription"),
                    modAssetPath = ReadRelativeString(profile, "modAssetPath"),
                    modExportPath = ReadRelativeString(profile, "modExportPath"),
                    isActive = index == rawActive,
                };

                SerializedProperty references = profile.FindPropertyRelative("referencePaths");
                entry.referencedModCount = references != null && references.isArray ? references.arraySize : 0;

                entry.nameValid = !string.IsNullOrEmpty(entry.modName);
                entry.assetPathIsAssetsRoot = IsAssetsRootPath(entry.modAssetPath);
                entry.assetPathExists = !string.IsNullOrEmpty(entry.modAssetPath) &&
                                        Directory.Exists(entry.modAssetPath);
                entry.assetPathUnderAssets = IsPathUnderAssets(entry.modAssetPath);
                entry.assetPathValid = entry.nameValid &&
                                       entry.assetPathExists &&
                                       entry.assetPathUnderAssets &&
                                       !entry.assetPathIsAssetsRoot;
                entry.exportPathExists = !string.IsNullOrEmpty(entry.modExportPath) &&
                                         Directory.Exists(entry.modExportPath);

                if (entry.nameValid)
                {
                    int existingIndex;
                    if (seenNames.TryGetValue(entry.modName, out existingIndex))
                    {
                        entry.isDuplicateName = true;
                        workspaceEntries[existingIndex].isDuplicateName = true;
                        workspaceHasDuplicateNames = true;
                    }
                    else
                    {
                        seenNames.Add(entry.modName, index);
                    }
                }

                entry.statusNote = BuildWorkspaceStatusNote(entry);
                workspaceEntries.Add(entry);
            }

            activeWorkspaceIndex = rawActive;
        }

        /// <summary>行内状态只描述“能不能直接拿去构建”，完整原因放在详情区。</summary>
        private static string BuildWorkspaceStatusNote(WorkspaceEntry entry)
        {
            if (entry.isDuplicateName)
                return "有多个工作区使用同一个 Mod 名称。";
            if (!entry.nameValid)
                return "工作区还没有 Mod 名称。";
            if (!entry.assetPathExists)
                return "Mod 资产目录不存在。";
            if (entry.assetPathIsAssetsRoot)
                return "Mod 资产目录不能是 Assets 根目录。";
            if (!entry.assetPathUnderAssets)
                return "Mod 资产目录不在 Assets 下。";
            return string.Empty;
        }

        private static bool IsAssetsRootPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, Application.dataPath.Replace('\\', '/').TrimEnd('/'),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathUnderAssets(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(assetPath);
                string dataPath = Path.GetFullPath(Application.dataPath);
                return fullPath.StartsWith(dataPath, FileSystemPathComparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ReadRelativeString(SerializedProperty parent, string name)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            return property == null || property.propertyType != SerializedPropertyType.String
                ? string.Empty
                : property.stringValue;
        }

        private static void WriteRelativeString(SerializedProperty parent, string name, string value)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.String)
                property.stringValue = value ?? string.Empty;
        }

        private Texture2D LoadWorkspaceIcon(int index)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportSettingsPath);
            if (settings == null)
                return null;

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || index < 0 || index >= profiles.arraySize)
                return null;

            SerializedProperty icon = profiles.GetArrayElementAtIndex(index).FindPropertyRelative("modIcon");
            return icon == null ? null : icon.objectReferenceValue as Texture2D;
        }

        private static UnityEngine.Object LoadExportSettingsAsset(string path)
        {
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        #endregion

        #region 工作区操作

        private void SetActiveWorkspace(int index)
        {
            if (HasPendingBuildState())
            {
                workspaceStatusMessage = "已有 FastBuild 流程在进行中，暂不能切换工作区。";
                return;
            }

            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
            {
                workspaceStatusMessage = "找不到 ExportSettings，无法切换工作区。";
                return;
            }

            try
            {
                Undo.RecordObject(settings, "切换 Warudo 工作区");
                if (!TryInvokeExportSettingsMethod(settings, "SetActiveExportProfile", new object[] { index }))
                {
                    var serializedSettings = new SerializedObject(settings);
                    SerializedProperty activeProperty = serializedSettings.FindProperty("activeProfile");
                    if (activeProperty == null)
                        throw new InvalidOperationException("ExportSettings 缺少 activeProfile 字段。");
                    activeProperty.intValue = index;
                    serializedSettings.ApplyModifiedProperties();
                }

                CommitExportSettingsChange(settings);
                workspaceStatusMessage = "已切换到工作区：" + DescribeWorkspaceName(index);
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "切换工作区失败：" + GetRootMessage(exception);
                Debug.LogError("[HoUnityTools] 切换 Warudo 工作区失败：" + exception);
            }

            RefreshExportSettingsPreview();
            Repaint();
        }

        private void CreateWorkspace()
        {
            if (HasPendingBuildState())
            {
                workspaceStatusMessage = "已有 FastBuild 流程在进行中，暂不能新建工作区。";
                return;
            }

            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
            {
                workspaceStatusMessage = "找不到 ExportSettings，无法新建工作区。";
                return;
            }

            // 生成唯一名称、继承导出目录都依赖当前列表，先同步一次避免用到过期快照。
            RefreshWorkspacePreview();

            try
            {
                // 新建的工作区继承当前工作区的导出目录与作者：这两项通常与工程环境相关，
                // 而 Mod 名称和资产目录必须由用户为新 Mod 重新指定。
                string inheritedExportPath = string.Empty;
                string inheritedAuthor = string.Empty;
                if (activeWorkspaceIndex >= 0 && activeWorkspaceIndex < workspaceEntries.Count)
                {
                    WorkspaceEntry current = workspaceEntries[activeWorkspaceIndex];
                    inheritedExportPath = current.modExportPath;
                    inheritedAuthor = current.modAuthor;
                }

                Undo.RecordObject(settings, "新建 Warudo 工作区");
                if (!TryInvokeExportSettingsMethod(settings, "CreateNewExportProfile", new object[] { true }))
                {
                    // 回退路径：SDK 未提供或调用失败时直接扩展序列化数组。
                    var serializedSettings = new SerializedObject(settings);
                    SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
                    SerializedProperty activeProperty = serializedSettings.FindProperty("activeProfile");
                    if (profiles == null || !profiles.isArray || activeProperty == null)
                        throw new InvalidOperationException("ExportSettings 缺少 exportProfiles 或 activeProfile 字段。");

                    profiles.arraySize++;
                    activeProperty.intValue = profiles.arraySize - 1;
                    serializedSettings.ApplyModifiedProperties();
                }

                string newName = BuildUniqueWorkspaceName();
                var seedSettings = new SerializedObject(settings);
                SerializedProperty seedProfiles = seedSettings.FindProperty("exportProfiles");
                SerializedProperty seedActive = seedSettings.FindProperty("activeProfile");
                if (seedProfiles == null || !seedProfiles.isArray || seedProfiles.arraySize == 0)
                    throw new InvalidOperationException("新建工作区后 exportProfiles 为空。");

                int newIndex = seedActive == null ? seedProfiles.arraySize - 1 : seedActive.intValue;
                if (newIndex < 0 || newIndex >= seedProfiles.arraySize)
                    newIndex = seedProfiles.arraySize - 1;

                SerializedProperty profile = seedProfiles.GetArrayElementAtIndex(newIndex);
                WriteRelativeString(profile, "modName", newName);
                WriteRelativeString(profile, "modExportPath", inheritedExportPath);
                if (!string.IsNullOrEmpty(inheritedAuthor))
                    WriteRelativeString(profile, "modAuthor", inheritedAuthor);
                WriteRelativeString(profile, "modAssetPath", "Assets/" + newName);
                seedSettings.ApplyModifiedProperties();

                CommitExportSettingsChange(settings);
                workspaceStatusMessage =
                    "已新建并切换到工作区“" + newName + "”。" +
                    "请设置 Mod 资产目录；目录不存在时可以在此面板直接创建。";
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "新建工作区失败：" + GetRootMessage(exception);
                Debug.LogError("[HoUnityTools] 新建 Warudo 工作区失败：" + exception);
            }

            RefreshExportSettingsPreview();
        }

        private void DeleteActiveWorkspace()
        {
            if (HasPendingBuildState())
            {
                workspaceStatusMessage = "已有 FastBuild 流程在进行中，暂不能删除工作区。";
                return;
            }

            // 删除操作依赖活动下标，先用磁盘上的真实状态同步一次。
            RefreshWorkspacePreview();
            if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaceEntries.Count)
                return;

            if (workspaceEntries.Count <= 1)
            {
                workspaceStatusMessage = "至少要保留一个工作区；如果确实要清空，请使用 uMod 官方设置窗口。";
                return;
            }

            WorkspaceEntry entry = workspaceEntries[activeWorkspaceIndex];
            string displayName = string.IsNullOrEmpty(entry.modName) ? "<未命名>" : entry.modName;
            if (!EditorUtility.DisplayDialog(
                    WindowTitle,
                    "删除工作区“" + displayName + "”？\n\n只会从 ExportSettings 里移除这条配置，" +
                    "不会删除磁盘上的 Mod 资产目录或已构建的 .warudo。",
                    "删除",
                    "取消"))
            {
                return;
            }

            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
            {
                workspaceStatusMessage = "找不到 ExportSettings，无法删除工作区。";
                return;
            }

            try
            {
                Undo.RecordObject(settings, "删除 Warudo 工作区");
                if (!TryInvokeExportSettingsMethod(settings, "DeleteExportProfile", new object[] { entry.index }))
                {
                    var serializedSettings = new SerializedObject(settings);
                    SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
                    SerializedProperty activeProperty = serializedSettings.FindProperty("activeProfile");
                    if (profiles == null || !profiles.isArray || activeProperty == null)
                        throw new InvalidOperationException("ExportSettings 缺少 exportProfiles 或 activeProfile 字段。");

                    profiles.DeleteArrayElementAtIndex(entry.index);
                    // SDK 在删除后会把活动工作区复位到第一项，这里保持一致。
                    activeProperty.intValue = 0;
                    serializedSettings.ApplyModifiedProperties();
                }

                CommitExportSettingsChange(settings);
                workspaceStatusMessage = "已删除工作区：“" + displayName + "”。";
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "删除工作区失败：" + GetRootMessage(exception);
                Debug.LogError("[HoUnityTools] 删除 Warudo 工作区失败：" + exception);
            }

            RefreshExportSettingsPreview();
        }

        private void RemoveDuplicateWorkspaces()
        {
            if (HasPendingBuildState())
            {
                workspaceStatusMessage = "已有 FastBuild 流程在进行中，暂不能修改工作区。";
                return;
            }

            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
            {
                workspaceStatusMessage = "找不到 ExportSettings。";
                return;
            }

            try
            {
                Undo.RecordObject(settings, "清理重复 Warudo 工作区");
                if (!TryInvokeExportSettingsMethod(settings, "RemoveDuplicateProfiles", null))
                    throw new NotSupportedException("当前 SDK 未提供 RemoveDuplicateProfiles。");

                CommitExportSettingsChange(settings);
                workspaceStatusMessage = "已按 Mod 名称去重，保留每组的第一项。";
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "清理重复工作区失败：" + GetRootMessage(exception);
                Debug.LogError("[HoUnityTools] 清理重复 Warudo 工作区失败：" + exception);
            }

            RefreshExportSettingsPreview();
        }

        private void CreateWorkspaceAssetFolder()
        {
            if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaceEntries.Count)
                return;

            WorkspaceEntry entry = workspaceEntries[activeWorkspaceIndex];
            if (string.IsNullOrEmpty(entry.modAssetPath))
            {
                workspaceStatusMessage = "请先填写 Mod 资产目录。";
                return;
            }

            try
            {
                EnsureAssetFolder(ToAssetRelativePath(entry.modAssetPath));
                AssetDatabase.Refresh();
                workspaceStatusMessage = "已创建目录：" + entry.modAssetPath;
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "创建目录失败：" + GetRootMessage(exception);
            }

            RefreshExportSettingsPreview();
        }

        /// <summary>把 Assets 下的绝对路径折叠回 "Assets/..." 形式，便于走 AssetDatabase 创建。</summary>
        private static string ToAssetRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string normalized = path.Replace('\\', '/').TrimEnd('/');
            string dataPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(dataPath, FileSystemPathComparison))
            {
                string relative = normalized.Substring(dataPath.Length).TrimStart('/');
                return string.IsNullOrEmpty(relative) ? "Assets" : "Assets/" + relative;
            }

            return normalized;
        }

        private void RevealWorkspaceExportFolder()
        {
            if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaceEntries.Count)
                return;

            WorkspaceEntry entry = workspaceEntries[activeWorkspaceIndex];
            if (string.IsNullOrEmpty(entry.modExportPath) || !Directory.Exists(entry.modExportPath))
            {
                workspaceStatusMessage = "导出目录不存在，无法定位。";
                return;
            }

            EditorUtility.RevealInFinder(entry.modExportPath);
        }

        private void OpenOfficialExportSettingsWindow()
        {
            try
            {
                Type windowType = FindLoadedType("UMod.Exporter.SettingsWindow");
                MethodInfo method = windowType == null
                    ? null
                    : windowType.GetMethod(
                        "ShowWindow",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(bool), typeof(int) },
                        null);
                if (method == null)
                {
                    workspaceStatusMessage = "当前 SDK 未提供官方设置窗口入口。";
                    return;
                }

                // openTab 0 = Mod 页，工作区切换和新建都在这里。
                method.Invoke(null, new object[] { false, 0 });
            }
            catch (Exception exception)
            {
                workspaceStatusMessage = "打开官方设置窗口失败：" + GetRootMessage(exception);
                Debug.LogWarning("[HoUnityTools] 打开 uMod 官方设置窗口失败：" + exception);
            }
        }

        /// <summary>调用 SDK 公开的 ExportSettings 方法；不可用时返回 false 让调用方走序列化回退。</summary>
        private static bool TryInvokeExportSettingsMethod(
            UnityEngine.Object settings,
            string methodName,
            object[] arguments)
        {
            MethodInfo method = settings.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                        return false;
                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length != (arguments == null ? 0 : arguments.Length))
                        return false;
                    for (int index = 0; index < parameters.Length; index++)
                    {
                        if (arguments[index] == null ||
                            !parameters[index].ParameterType.IsInstanceOfType(arguments[index]))
                            return false;
                    }

                    return true;
                });

            if (method == null)
                return false;

            try
            {
                method.Invoke(settings, arguments);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        /// <summary>
        /// 落盘并让 UMod 重新加载引用程序集。官方设置窗口在切换工作区后也会调用
        /// ReferenceAssemblyLoader.LoadReferencedAssemblies(false)，这里保持一致。
        /// </summary>
        private static void CommitExportSettingsChange(UnityEngine.Object settings)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            try
            {
                Type loaderType = FindLoadedType("UMod.BuildEngine.ReferenceAssemblyLoader");
                MethodInfo method = loaderType == null
                    ? null
                    : loaderType.GetMethod(
                        "LoadReferencedAssemblies",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(bool) },
                        null);
                if (method != null)
                    method.Invoke(null, new object[] { false });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[HoUnityTools] 重新加载引用程序集失败：" + GetRootMessage(exception));
            }
        }

        private string BuildUniqueWorkspaceName()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorkspaceEntry entry in workspaceEntries)
            {
                if (!string.IsNullOrEmpty(entry.modName))
                    used.Add(entry.modName);
            }

            const string baseName = "MOD_New";
            if (!used.Contains(baseName))
                return baseName;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = baseName + suffix;
                if (!used.Contains(candidate))
                    return candidate;
            }

            return baseName + "_" + DateTime.Now.ToString("HHmmss");
        }

        private string DescribeWorkspaceName(int index)
        {
            if (index < 0 || index >= workspaceEntries.Count)
                return "(未知)";

            string name = workspaceEntries[index].modName;
            return string.IsNullOrEmpty(name) ? "<未命名>" : name;
        }

        #endregion

        private void DrawDependencyPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int runtimeAssetCount = runtimeAssetPreview == null ? 0 : runtimeAssetPreview.Count;
                string dependencyStatus = scriptPreview.Count + " 个脚本";
                if (runtimeAssetCount > 0)
                    dependencyStatus += " / " + runtimeAssetCount + " 个脚本资源";

                DrawPanelHeader(
                    "依赖审查",
                    dependencyStatus,
                    new Color(0.62f, 0.45f, 0.84f),
                    "FilterByType",
                    "此处只管理需要单独编译成运行时程序的 C# 源码；" +
                    "UMod 会自动按 Prefab 引用收集其它资源（本 Prefab 共 " + dependencyCount +
                    " 项依赖，其中非脚本 " + nonScriptDependencyCount + " 项）。");

                GUILayout.Space(5f);

                if (missingScriptCount > 0)
                    EditorGUILayout.HelpBox("Prefab 中存在 Missing Script，请先修复后再构建。", MessageType.Error);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全选运行时", EditorStyles.miniButton, GUILayout.Width(84f)))
                        SetRuntimeScriptSelection(true);
                    if (GUILayout.Button("全部不复制", EditorStyles.miniButton, GUILayout.Width(84f)))
                        SetRuntimeScriptSelection(false);

                    GUILayout.FlexibleSpace();
                    if (DrawIconButton("Refresh", "刷新", "重新扫描 Prefab 依赖"))
                        RefreshDependencyPreview();
                }

                GUILayout.Space(4f);
                scriptScroll = EditorGUILayout.BeginScrollView(scriptScroll, GUILayout.MinHeight(170f), GUILayout.MaxHeight(310f));
                if (scriptPreview.Count == 0)
                {
                    EditorGUILayout.HelpBox("当前 Prefab 没有扫描到可定位源码的 MonoBehaviour。", MessageType.Warning);
                }
                else
                {
                    for (int index = 0; index < scriptPreview.Count; index++)
                    {
                        if (index > 0)
                            DrawRowSeparator();
                        DrawScriptPreviewRow(scriptPreview[index]);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawRuntimeAssetPreviewInlineRow(RuntimeAssetPreview item)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowControlHeight);

            Rect nameRect;
            Rect fileRect;
            Rect referenceRect;
            ComputeSubRowColumns(
                rowRect,
                FoldColumnWidth + ToggleColumnWidth + ToggleColumnWidth,
                out nameRect,
                out fileRect,
                out referenceRect);

            string displayName = item.found ? item.resourceKey : item.resourceKey + "（未找到）";
            string reference = item.componentReferences == null || item.componentReferences.Count == 0
                ? "脚本字符串引用"
                : item.componentReferences[0];
            if (item.componentReferences != null && item.componentReferences.Count > 1)
                reference += " +" + (item.componentReferences.Count - 1);
            string fileName = item.found ? Path.GetFileName(item.assetPath) : "缺少资源";

            GUI.Label(
                nameRect,
                new GUIContent(TruncateToWidth(displayName, EditorStyles.miniLabel, nameRect.width), BuildRuntimeAssetTooltip(item)),
                EditorStyles.miniLabel);
            GUI.Label(
                fileRect,
                new GUIContent(
                    TruncateToWidth(fileName, EditorStyles.miniLabel, fileRect.width),
                    item.found ? item.assetPath : "没有在工程的 Resources 目录里找到这个资源"),
                EditorStyles.miniLabel);
            GUI.Label(
                referenceRect,
                new GUIContent(TruncateToWidth(reference, EditorStyles.miniLabel, referenceRect.width), reference),
                EditorStyles.miniLabel);
        }

        private static string BuildRuntimeAssetTooltip(RuntimeAssetPreview item)
        {
            var builder = new StringBuilder();
            builder.Append("资源路径：");
            builder.Append(item.found ? item.assetPath : "未找到");
            builder.Append("\n构建复制：");
            builder.Append(item.willCopy ? "是" : "否（请勾选引用它的脚本）");
            if (item.componentReferences != null && item.componentReferences.Count > 0)
            {
                builder.Append("\n\n组件引用：");
                for (int i = 0; i < item.componentReferences.Count && i < 24; i++)
                {
                    builder.Append("\n");
                    builder.Append(item.componentReferences[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 列表行的列布局。显式算 rect 而不是靠 GUILayout 的隐式间距：
        /// 自带 Editor 控件（例如 Foldout）会在布局里吃掉额外宽度，导致列错位。
        /// </summary>
        private static void ComputeListColumns(
            Rect rowRect,
            float leftOffset,
            out Rect nameRect,
            out Rect mountRect,
            out Rect sourceRect)
        {
            float available = Mathf.Max(120f, rowRect.width - leftOffset);
            float sourceWidth = Mathf.Min(SourceColumnWidth, available * 0.42f);
            float mountWidth = Mathf.Min(MountColumnWidth, available * 0.18f);
            float nameWidth = Mathf.Max(40f, available - sourceWidth - mountWidth);

            nameRect = new Rect(rowRect.x + leftOffset, rowRect.y, nameWidth, rowRect.height);
            mountRect = new Rect(nameRect.xMax, rowRect.y, mountWidth, rowRect.height);
            sourceRect = new Rect(mountRect.xMax, rowRect.y, sourceWidth, rowRect.height);
        }

        /// <summary>
        /// 子行把最长的“被谁引用”放在最后一列，让它吃掉剩余宽度并贴右边缘截断，
        /// 而不是挤在中间列里和下一列叠在一起。
        /// </summary>
        private static void ComputeSubRowColumns(
            Rect rowRect,
            float leftOffset,
            out Rect nameRect,
            out Rect fileRect,
            out Rect referenceRect)
        {
            float available = Mathf.Max(120f, rowRect.width - leftOffset);
            float referenceWidth = Mathf.Min(SourceColumnWidth + MountColumnWidth, available * 0.5f);
            float fileWidth = Mathf.Min(SourceColumnWidth, available * 0.3f);
            float nameWidth = Mathf.Max(40f, available - fileWidth - referenceWidth);

            nameRect = new Rect(rowRect.x + leftOffset, rowRect.y, nameWidth, rowRect.height);
            fileRect = new Rect(nameRect.xMax, rowRect.y, fileWidth, rowRect.height);
            referenceRect = new Rect(fileRect.xMax, rowRect.y, referenceWidth, rowRect.height);
        }

        /// <summary>
        /// 按实际字宽截断并加省略号。用 CalcSize 而不是给 GUIStyle 开 clipping：
        /// 带裁剪的样式副本在深色主题下会把文字画成黑色。
        /// </summary>
        private static string TruncateToWidth(string value, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(value) || style == null || width <= 0f)
                return value ?? string.Empty;

            if (style.CalcSize(new GUIContent(value)).x <= width)
                return value;

            const string ellipsis = "…";
            float ellipsisWidth = style.CalcSize(new GUIContent(ellipsis)).x;
            if (ellipsisWidth > width)
                return string.Empty;

            int low = 0;
            int high = value.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                float candidate = style.CalcSize(new GUIContent(value.Substring(0, mid))).x;
                if (candidate + ellipsisWidth <= width)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low <= 0 ? ellipsis : value.Substring(0, low) + ellipsis;
        }

        private void DrawScriptPreviewRow(ScriptPreview item)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, RowControlHeight);
            string tooltip = BuildScriptTooltip(item);
            bool hasReferencedAssets = item.referencedAssets != null && item.referencedAssets.Count > 0;

            float leftOffset = FoldColumnWidth + ToggleColumnWidth;
            var foldRect = new Rect(rowRect.x, rowRect.y, FoldColumnWidth, rowRect.height);
            var toggleRect = new Rect(rowRect.x + FoldColumnWidth, rowRect.y, ToggleColumnWidth, rowRect.height);

            Rect nameRect;
            Rect mountRect;
            Rect sourceRect;
            ComputeListColumns(rowRect, leftOffset, out nameRect, out mountRect, out sourceRect);

            if (hasReferencedAssets)
                item.showReferencedAssets = GUI.Toggle(foldRect, item.showReferencedAssets, GUIContent.none, EditorStyles.foldout);

            using (new EditorGUI.DisabledScope(
                       !copySelectedScripts || item.removeWhenExcluded || item.hostProvided))
            {
                item.copySource = GUI.Toggle(toggleRect, item.copySource, GUIContent.none, EditorStyles.toggle);
            }

            string displayName = string.IsNullOrEmpty(item.typeName)
                ? Path.GetFileNameWithoutExtension(item.sourcePath)
                : item.typeName;
            GUI.Label(
                nameRect,
                new GUIContent(TruncateToWidth(displayName, EditorStyles.boldLabel, nameRect.width), tooltip),
                EditorStyles.boldLabel);

            string mount = item.referenceCount > 0 ? item.referenceCount + " 处挂载" : "—";
            GUI.Label(
                mountRect,
                new GUIContent(TruncateToWidth(mount, statusColumnStyle, mountRect.width), tooltip),
                statusColumnStyle);

            string sourceStatus = item.hostProvided
                ? "宿主 / " + Path.GetFileName(item.sourcePath)
                : Path.GetFileName(item.sourcePath);
            GUI.Label(
                sourceRect,
                new GUIContent(TruncateToWidth(sourceStatus, EditorStyles.miniLabel, sourceRect.width), item.sourcePath),
                EditorStyles.miniLabel);

            if (item.showReferencedAssets && item.referencedAssets != null)
            {
                for (int i = 0; i < item.referencedAssets.Count; i++)
                    DrawRuntimeAssetPreviewInlineRow(item.referencedAssets[i]);
            }
        }

        private static string BuildScriptTooltip(ScriptPreview item)
        {
            var builder = new StringBuilder(item.note ?? string.Empty);
            if (item.componentReferences == null || item.componentReferences.Count == 0)
                return builder.ToString();

            if (builder.Length > 0)
                builder.Append("\n\n");
            builder.Append("组件引用：");
            int visibleCount = Mathf.Min(item.componentReferences.Count, 24);
            for (int i = 0; i < visibleCount; i++)
            {
                builder.Append("\n");
                builder.Append(item.componentReferences[i]);
            }

            if (item.componentReferences.Count > visibleCount)
            {
                builder.Append("\n… 其余 ");
                builder.Append(item.componentReferences.Count - visibleCount);
                builder.Append(" 个引用未显示");
            }

            return builder.ToString();
        }

        private void DrawBuildOptionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "构建选项",
                    cleanupTemporaryAssets ? "自动清理" : "保留临时目录",
                    new Color(0.91f, 0.65f, 0.25f),
                    "Settings",
                    "这些选项只影响 FastBuild 的临时副本和收尾动作，不改变源 Prefab。");
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

        private void DrawVerificationPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool hasResult = !string.IsNullOrEmpty(lastVerificationReport);
                DrawPanelHeader(
                    "产物复核",
                    hasResult ? (lastVerificationHasProblems ? "需要确认" : "已确认") : "未执行",
                    hasResult
                        ? (lastVerificationHasProblems
                            ? new Color(0.88f, 0.42f, 0.28f)
                            : new Color(0.20f, 0.68f, 0.57f))
                        : new Color(0.55f, 0.57f, 0.60f),
                    "TestPassed",
                    "构建结束后直接读取 .warudo，确认每个组件的程序集链接和运行时类型。",
                    string.IsNullOrEmpty(lastArtifactPath) ? "还没有可复核的产物。" : "上次产物：" + lastArtifactPath);

                if (!hasResult && string.IsNullOrEmpty(lastArtifactPath))
                    return;

                GUILayout.Space(5f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (hasResult)
                    {
                        EditorGUILayout.HelpBox(
                            lastVerificationSummary,
                            lastVerificationHasProblems ? MessageType.Warning : MessageType.Info);
                    }
                    else
                    {
                        GUILayout.Label("上次产物还在，可以重新复核。", EditorStyles.miniLabel);
                    }

                    if (DrawIconButton("Refresh", "复核", "重新读取产物并复核组件挂载"))
                        ReverifyLastArtifact();
                }

                if (!hasResult)
                    return;

                showVerificationReport = EditorGUILayout.Foldout(showVerificationReport, "详细复核结果");
                if (!showVerificationReport)
                    return;

                GUILayout.Space(3f);
                using (var scroll = new EditorGUILayout.ScrollViewScope(verificationScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(280f)))
                {
                    verificationScroll = scroll.scrollPosition;
                    EditorGUILayout.TextArea(
                        lastVerificationReport,
                        EditorStyles.wordWrappedMiniLabel,
                        GUILayout.ExpandHeight(true));
                }
            }
        }

        /// <summary>
        /// 面板标题栏：左侧色条 + 图标 + 标题，右侧状态。标题与状态都可以带 tooltip，
        /// 这样面板里就不需要再画常驻的说明文字。
        /// </summary>
        private void DrawPanelHeader(
            string title,
            string status,
            Color accent,
            string iconName = null,
            string tooltip = null,
            string statusTooltip = null)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.82f, 0.83f, 0.85f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);

            float offset = rect.x + 12f;
            Texture icon = GetEditorIcon(iconName);
            if (icon != null)
            {
                var iconRect = new Rect(offset, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                offset += 20f;
            }

            float statusWidth = 104f;
            var titleRect = new Rect(offset, rect.y, rect.width - (offset - rect.x) - statusWidth, rect.height);
            var statusRect = new Rect(rect.xMax - statusWidth - 6f, rect.y, statusWidth, rect.height);
            GUI.Label(titleRect, new GUIContent(title, tooltip ?? string.Empty), panelTitleStyle);
            GUI.Label(statusRect, new GUIContent(status, statusTooltip ?? string.Empty), panelStatusStyle);
        }

        private static Texture GetEditorIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            GUIContent content = EditorGUIUtility.IconContent(iconName);
            return content == null ? null : content.image;
        }

        /// <summary>统一的方形图标按钮；图标不可用时退回文字，并自动放宽到能放下文字。</summary>
        private bool DrawIconButton(string iconName, string fallbackText, string tooltip)
        {
            GUIContent content = string.IsNullOrEmpty(iconName) ? null : EditorGUIUtility.IconContent(iconName);
            if (content != null && content.image != null)
            {
                content = new GUIContent(content.image, tooltip);
                return GUILayout.Button(
                    content,
                    centeredIconButtonStyle,
                    GUILayout.Width(IconButtonWidth),
                    GUILayout.Height(RowControlHeight));
            }

            var fallback = new GUIContent(fallbackText, tooltip);
            float width = Mathf.Max(
                IconButtonWidth,
                centeredIconButtonStyle.CalcSize(fallback).x + 8f);
            return GUILayout.Button(
                fallback,
                centeredIconButtonStyle,
                GUILayout.Width(width),
                GUILayout.Height(RowControlHeight));
        }

        /// <summary>列表行之间的细分隔线，比给每一行套 HelpBox 更轻。</summary>
        private static void DrawRowSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            if (Event.current.type != EventType.Repaint)
                return;

            Color color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.07f)
                : new Color(0f, 0f, 0f, 0.09f);
            EditorGUI.DrawRect(rect, color);
        }

        /// <summary>详情区的“标签 : 值”行，标签列固定宽度保证多行对齐。</summary>
        private static void DrawDetailRow(string label, string value, string tooltip = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(FormLabelWidth));
                GUILayout.Label(
                    new GUIContent(string.IsNullOrEmpty(value) ? "—" : value, tooltip ?? string.Empty),
                    EditorStyles.label);
            }
        }

        private bool HasLiveStatusMessage()
        {
            return !string.IsNullOrEmpty(workspaceStatusMessage) &&
                   EditorApplication.timeSinceStartup < workspaceStatusMessageExpiry;
        }

        private void SetWorkspaceStatus(string message)
        {
            workspaceStatusMessage = message ?? string.Empty;
            lastStampedWorkspaceStatus = null;
            workspaceStatusMessageExpiry = EditorApplication.timeSinceStartup + StatusMessageLifetime;
        }

        /// <summary>
        /// 工作区反馈是瞬时信息：消息一变就重新计时，超时后自行消失，不在面板里常驻。
        /// </summary>
        private void RefreshWorkspaceStatusLifetime()
        {
            if (string.Equals(lastStampedWorkspaceStatus, workspaceStatusMessage, StringComparison.Ordinal))
                return;

            lastStampedWorkspaceStatus = workspaceStatusMessage;
            workspaceStatusMessageExpiry = EditorApplication.timeSinceStartup + StatusMessageLifetime;
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

            if (statusColumnStyle == null)
            {
                // 刻意不设置 clipping：带裁剪的副本在深色主题下会把文字画成黑色，
                // 列宽已经留足，超出部分由 tooltip 兜底。
                statusColumnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 2, 0, 0)
                };
            }

            if (centeredIconButtonStyle == null)
            {
                centeredIconButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(1, 1, 1, 1)
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

            // GUIStyle 副本会固定住创建时的文字颜色；换肤或 EditorStyles 重建后副本就会停留在旧颜色
            // （浅色主题的黑色文字在深色主题里几乎不可见）。这里每帧从当前 EditorStyles 重新同步。
            SyncStyleTextColor(panelTitleStyle, EditorStyles.boldLabel);
            SyncStyleTextColor(panelStatusStyle, EditorStyles.miniLabel);
            SyncStyleTextColor(statusColumnStyle, EditorStyles.miniLabel);
            SyncStyleTextColor(centeredIconButtonStyle, EditorStyles.miniButton);
        }

        private static void SyncStyleTextColor(GUIStyle target, GUIStyle source)
        {
            if (target == null || source == null)
                return;

            Color color = source.normal.textColor;
            target.normal.textColor = color;
            target.hover.textColor = color;
            target.active.textColor = color;
            target.focused.textColor = color;
            target.onNormal.textColor = color;
            target.onHover.textColor = color;
            target.onActive.textColor = color;
            target.onFocused.textColor = color;
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
            if (runtimeAssetPreview == null)
                runtimeAssetPreview = new List<RuntimeAssetPreview>();
            runtimeAssetPreview.Clear();
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
                    bool hostProvided = IsHostProvidedRuntimeScript(path, typeName);
                    row = new ScriptPreview
                    {
                        sourcePath = path,
                        typeName = typeName,
                        copySource = !unsafeForRuntime && !hostProvided && ShouldCopyScriptByDefault(path),
                        removeWhenExcluded = unsafeForRuntime,
                        hostProvided = hostProvided,
                        note = GetScriptNote(path, typeName, unsafeForRuntime),
                    };
                    bool previousValue;
                    if (!hostProvided && previousSelection != null &&
                        previousSelection.TryGetValue(path, out previousValue))
                        row.copySource = previousValue;
                    rows.Add(path, row);
                }

                row.referenceCount++;
                row.componentReferences.Add(
                    GetTransformPath(prefab.transform, behaviour.transform) +
                    " / " +
                    behaviour.GetType().Name);
            }

            scriptPreview = rows.Values
                .OrderByDescending(item => item.copySource)
                .ThenBy(item => item.typeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            RefreshReferencedAssetPreview();
            dependencyPreviewHash = AssetDatabase.GetAssetDependencyHash(sourcePrefabPath).ToString();
        }

        private void RefreshReferencedAssetPreview()
        {
            if (runtimeAssetPreview == null)
                runtimeAssetPreview = new List<RuntimeAssetPreview>();
            runtimeAssetPreview.Clear();

            var rows = new Dictionary<string, RuntimeAssetPreview>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptPreview script in scriptPreview)
            {
                if (script == null || string.IsNullOrEmpty(script.sourcePath))
                    continue;

                string absoluteSource = AssetPathToAbsolute(script.sourcePath);
                if (!File.Exists(absoluteSource))
                    continue;

                string sourceText = File.ReadAllText(absoluteSource, Encoding.UTF8);
                MatchCollection matches = ResourcesLoadPattern.Matches(sourceText);
                foreach (Match match in matches)
                {
                    string resourceKey = NormalizeResourceKey(match.Groups[1].Value);
                    if (string.IsNullOrEmpty(resourceKey))
                        continue;

                    RuntimeAssetPreview asset;
                    if (!rows.TryGetValue(resourceKey, out asset))
                    {
                        string assetPath = FindResourceAsset(resourceKey);
                        asset = new RuntimeAssetPreview
                        {
                            resourceKey = resourceKey,
                            assetPath = assetPath,
                            found = !string.IsNullOrEmpty(assetPath),
                        };
                        rows.Add(resourceKey, asset);
                    }

                    if (script.referencedAssets == null)
                        script.referencedAssets = new List<RuntimeAssetPreview>();
                    if (!script.referencedAssets.Contains(asset))
                        script.referencedAssets.Add(asset);
                    asset.willCopy |= script.copySource && !script.removeWhenExcluded;
                    if (script.componentReferences == null)
                        continue;

                    for (int i = 0; i < script.componentReferences.Count; i++)
                    {
                        string componentReference = script.componentReferences[i];
                        if (!asset.componentReferences.Contains(componentReference))
                            asset.componentReferences.Add(componentReference);
                    }
                }
            }

            runtimeAssetPreview = rows.Values
                .OrderBy(item => item.resourceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            var parts = new List<string>();
            Transform current = target;
            while (current != null)
            {
                parts.Add(current.name);
                if (current == root)
                    break;
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
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
            if (IsWarudoSupportedClothType(typeName))
                return "Warudo 宿主已提供 MC1/MC2 运行时：保留 Prefab 组件引用，不复制或重新编译源码。";
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

        private static bool IsHostProvidedRuntimeScript(string path, string typeName)
        {
            string normalized = NormalizeAssetPath(path);
            return normalized.StartsWith("Packages/app.warudo.modtool/", StringComparison.OrdinalIgnoreCase) ||
                   IsWarudoSupportedClothType(typeName);
        }

        private static bool IsWarudoSupportedClothType(string typeName)
        {
            return HasTypeNamespace(typeName, "MagicaCloth") ||
                   HasTypeNamespace(typeName, "MagicaCloth2");
        }

        private static bool HasTypeNamespace(string typeName, string namespaceName)
        {
            return string.Equals(typeName, namespaceName, StringComparison.Ordinal) ||
                   (!string.IsNullOrEmpty(typeName) &&
                    typeName.StartsWith(namespaceName + ".", StringComparison.Ordinal));
        }

        private void SetRuntimeScriptSelection(bool selected)
        {
            foreach (ScriptPreview item in scriptPreview)
            {
                if (!item.removeWhenExcluded && !item.hostProvided)
                    item.copySource = selected;
            }
        }

        private void RefreshExportSettingsPreview()
        {
            exportSettingsPath = FindExportSettingsAssetPath();
            RefreshWorkspacePreview();
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
                ApplyTemporaryPlayerSettings(state);

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
                originalStandaloneDefines = GetStandaloneDefineSymbols(),
                managesStandaloneDefines = true,
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
                {
                    Dictionary<string, string> stagedScripts = StageSelectedScripts(temporaryRoot);
                    foreach (ScriptPreview item in scriptPreview)
                    {
                        if (item == null || !item.copySource || item.removeWhenExcluded || item.hostProvided)
                            continue;

                        string sourcePath = NormalizeAssetPath(item.sourcePath);
                        string stagedPath;
                        if (!stagedScripts.TryGetValue(sourcePath, out stagedPath))
                            throw new InvalidOperationException("未找到组件脚本的临时副本：" + sourcePath);

                        mappings.Add(new ScriptMapping
                        {
                            sourcePath = sourcePath,
                            stagedPath = stagedPath,
                            removeFromPrefab = false,
                        });
                    }

                    StageReferencedRuntimeAssets(temporaryRoot);
                }

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

        private Dictionary<string, string> StageSelectedScripts(string temporaryRoot)
        {
            string scriptsRoot = temporaryRoot + "/" + StagedScriptsDirectoryName;
            EnsureAssetFolder(scriptsRoot);
            List<string> sourcePaths = CollectRuntimeSourceClosure();
            var stagedScripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var componentSourcePaths = new HashSet<string>(
                scriptPreview
                    .Where(item => item != null && item.copySource && !item.removeWhenExcluded &&
                                   !item.hostProvided && !string.IsNullOrEmpty(item.sourcePath))
                    .Select(item => NormalizeAssetPath(item.sourcePath)),
                StringComparer.OrdinalIgnoreCase);
            int scriptIndex = 0;

            foreach (string sourcePath in sourcePaths)
            {
                string absoluteSource = AssetPathToAbsolute(sourcePath);
                if (!File.Exists(absoluteSource))
                    throw new FileNotFoundException("找不到运行时脚本源码", absoluteSource);

                string baseName = Path.GetFileName(sourcePath);
                string scriptFolderName = scriptIndex.ToString("D3") + "_" +
                                          SanitizeFileName(Path.GetFileNameWithoutExtension(baseName));
                scriptIndex++;
                string stagedFolder = scriptsRoot + "/" + scriptFolderName;
                EnsureAssetFolder(stagedFolder);
                string stagedPath = stagedFolder + "/" + baseName;
                string absoluteDestination = AssetPathToAbsolute(stagedPath);
                string sourceText = File.ReadAllText(absoluteSource, Encoding.UTF8);
                sourceText = sourceText.Replace("\r\n", "\n").Replace("\r", "\n");
                string normalizedSource = sourceText.Replace("\n", "\r\n");
                string wrappedSource = componentSourcePaths.Contains(sourcePath)
                    ? "#pragma warning disable 0436\r\n" + normalizedSource + "\r\n"
                    : "#if !UNITY_EDITOR\r\n" + normalizedSource + "\r\n#endif\r\n";
                File.WriteAllText(absoluteDestination, wrappedSource, new UTF8Encoding(false));
                WriteFreshMetaFile(absoluteDestination + ".meta");
                stagedScripts[sourcePath] = stagedPath;
            }

            return stagedScripts;
        }

        private List<string> CollectRuntimeSourceClosure()
        {
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblyRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ScriptPreview item in scriptPreview)
            {
                if (item == null || !item.copySource || item.removeWhenExcluded || item.hostProvided ||
                    string.IsNullOrEmpty(item.sourcePath))
                    continue;

                string sourcePath = NormalizeAssetPath(item.sourcePath);
                if (IsHostProvidedRuntimeScript(sourcePath, item.typeName))
                    continue;
                sourcePaths.Add(sourcePath);

                string assemblyRoot = FindRuntimeAssemblyRoot(sourcePath);
                if (!string.IsNullOrEmpty(assemblyRoot))
                    assemblyRoots.Add(assemblyRoot);
            }

            foreach (string assemblyRoot in assemblyRoots)
            {
                string absoluteRoot = AssetPathToAbsolute(assemblyRoot);
                if (!Directory.Exists(absoluteRoot))
                    continue;

                string[] files = Directory.GetFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i].Substring(absoluteRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar).Length + 1);
                    string candidate = NormalizeAssetPath(assemblyRoot + "/" + relative);
                    if (string.IsNullOrEmpty(candidate) ||
                        candidate.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        candidate.EndsWith("/Runtime/BoneRenderer/HoBoneRenderer.cs", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(candidate), "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string typeName = GetScriptTypeName(candidate);
                    if (IsUnsafeRuntimeScript(candidate, typeName) ||
                        IsHostProvidedRuntimeScript(candidate, typeName))
                        continue;

                    sourcePaths.Add(candidate);
                }
            }

            return sourcePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string FindRuntimeAssemblyRoot(string sourcePath)
        {
            string normalized = NormalizeAssetPath(sourcePath);
            string directory = NormalizeAssetPath(Path.GetDirectoryName(normalized));
            while (!string.IsNullOrEmpty(directory) &&
                   !string.Equals(directory, "Assets", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(directory, "Packages", StringComparison.OrdinalIgnoreCase))
            {
                string absoluteDirectory = AssetPathToAbsolute(directory);
                if (Directory.Exists(absoluteDirectory))
                {
                    string[] asmdefs = Directory.GetFiles(absoluteDirectory, "*.asmdef", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < asmdefs.Length; i++)
                    {
                        string asmdefText = File.ReadAllText(asmdefs[i], Encoding.UTF8);
                        if (asmdefText.IndexOf("\"Editor\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            asmdefText.IndexOf("\"includePlatforms\"", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        return directory;
                    }
                }

                string parent = NormalizeAssetPath(Path.GetDirectoryName(directory));
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                    break;
                directory = parent;
            }

            return string.Empty;
        }

        private static string GetScriptTypeName(string assetPath)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            Type type = script == null ? null : script.GetClass();
            return type == null ? string.Empty : type.FullName;
        }

        private void StageReferencedRuntimeAssets(string temporaryRoot)
        {
            var stagedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string stagedDebugShaderPath = string.Empty;
            List<string> sourcePaths = CollectRuntimeSourceClosure();
            foreach (string sourcePath in sourcePaths)
            {
                string absoluteSource = AssetPathToAbsolute(sourcePath);
                if (!File.Exists(absoluteSource))
                    continue;

                string sourceText = File.ReadAllText(absoluteSource, Encoding.UTF8);
                MatchCollection matches = ResourcesLoadPattern.Matches(sourceText);
                foreach (Match match in matches)
                {
                    string resourceKey = NormalizeResourceKey(match.Groups[1].Value);
                    if (string.IsNullOrEmpty(resourceKey))
                        continue;

                    string resourcePath = FindResourceAsset(resourceKey);
                    if (string.IsNullOrEmpty(resourcePath))
                    {
                        Debug.LogWarning(
                            "[HoUnityTools] FastBuild 无法定位脚本资源：Resources.Load(\"" +
                            resourceKey + "\")，脚本：" + sourcePath);
                        continue;
                    }

                    StageResourceAssetRecursive(resourcePath, temporaryRoot, stagedAssets);
                    if (string.Equals(resourceKey, RuntimeBoneDebugResourceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        stagedDebugShaderPath = NormalizeAssetPath(temporaryRoot).TrimEnd('/') + "/" +
                                                StagedResourcesDirectoryName + "/" +
                                                GetResourcesRelativePath(resourcePath);
                    }
                }
            }

            if (!string.IsNullOrEmpty(stagedDebugShaderPath))
                StageRuntimeDebugMaterial(temporaryRoot, stagedDebugShaderPath);
        }

        private static void StageRuntimeDebugMaterial(string temporaryRoot, string stagedShaderPath)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(stagedShaderPath);
            if (shader == null)
            {
                Debug.LogWarning("[HoUnityTools] FastBuild 无法为 HoRuntimeDebugLine 创建材质：" + stagedShaderPath);
                return;
            }

            string materialPath = NormalizeAssetPath(temporaryRoot).TrimEnd('/') + "/" +
                                  StagedResourcesDirectoryName + "/HoRuntimeDebugLine.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
                return;

            EnsureAssetFolder(NormalizeAssetPath(Path.GetDirectoryName(materialPath)));
            var material = new Material(shader)
            {
                name = "Ho Runtime Debug Line Material"
            };
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void StageResourceAssetRecursive(
            string sourcePath,
            string temporaryRoot,
            HashSet<string> stagedAssets)
        {
            string normalizedSourcePath = NormalizeAssetPath(sourcePath);
            if (!stagedAssets.Add(normalizedSourcePath) || !IsResourcesAsset(normalizedSourcePath))
                return;

            string relativePath = GetResourcesRelativePath(normalizedSourcePath);
            string stagedPath = NormalizeAssetPath(temporaryRoot).TrimEnd('/') + "/" +
                                StagedResourcesDirectoryName + "/" + relativePath;
            string absoluteSource = AssetPathToAbsolute(normalizedSourcePath);
            string absoluteDestination = AssetPathToAbsolute(stagedPath);
            string destinationDirectory = NormalizeAssetPath(Path.GetDirectoryName(stagedPath));
            EnsureAssetFolder(destinationDirectory);
            if (!AssetDatabase.CopyAsset(normalizedSourcePath, stagedPath))
            {
                File.Copy(absoluteSource, absoluteDestination, true);
                AssetDatabase.ImportAsset(stagedPath, ImportAssetOptions.ForceSynchronousImport);
            }

            string[] dependencies = AssetDatabase.GetDependencies(normalizedSourcePath, true);
            foreach (string dependency in dependencies)
            {
                string normalizedDependency = NormalizeAssetPath(dependency);
                if (!string.Equals(normalizedDependency, normalizedSourcePath, StringComparison.OrdinalIgnoreCase) &&
                    IsResourcesAsset(normalizedDependency))
                {
                    StageResourceAssetRecursive(normalizedDependency, temporaryRoot, stagedAssets);
                }
            }
        }

        private static string FindResourceAsset(string resourceKey)
        {
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < assetPaths.Length; i++)
            {
                string candidate = NormalizeAssetPath(assetPaths[i]);
                if (candidate.StartsWith(TemporaryAssetRoot + "/", StringComparison.OrdinalIgnoreCase) ||
                    !IsResourcesAsset(candidate))
                    continue;

                string candidateKey = GetResourcesRelativePath(candidate);
                int extensionIndex = candidateKey.LastIndexOf('.');
                if (extensionIndex > 0)
                    candidateKey = candidateKey.Substring(0, extensionIndex);

                if (string.Equals(candidateKey, resourceKey, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return string.Empty;
        }

        private static bool IsResourcesAsset(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                ? normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0
                : false;
        }

        private static string GetResourcesRelativePath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            int markerIndex = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return string.Empty;

            return normalized.Substring(markerIndex + "/Resources/".Length);
        }

        private static string NormalizeResourceKey(string value)
        {
            string key = NormalizeAssetPath(value).Trim('/');
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            int extensionIndex = key.LastIndexOf('.');
            if (extensionIndex > key.LastIndexOf('/'))
                key = key.Substring(0, extensionIndex);
            return key;
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
                ValidateTemporaryPlayerSettings(state);
                state.phase = BuildingPhase;
                WriteBuildState(state);
                ApplyTemporaryExportSettings(state);

                object result = InvokeOfficialBuild(state.exportSettingsPath);
                string resultSummary = ValidateBuildResult(result);
                Debug.Log("[HoUnityTools] FastBuild Warudo Mod 完成。" + resultSummary);

                // 产物复核必须在清理临时目录之前执行：期望组件来自实际提交给 UMod 的
                // 临时 Character.prefab，临时目录删除后就无法再取得这份基准。
                HoFastBuildArtifactVerification verification = RunArtifactVerification(state, result);
                resultMessage = "Warudo Mod 构建成功。\n" + resultSummary +
                                (verification == null ? string.Empty : "\n" + verification.summary);
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

            if (!TryRestoreBuildEnvironment(state))
            {
                EditorUtility.DisplayDialog(
                    WindowTitle,
                    resultMessage + "\n\n恢复构建环境失败，临时状态已保留，未执行清理。",
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

        #region 产物复核

        /// <summary>
        /// 构建结束后复核产物：从临时 Character.prefab 收集“应当出现”的组件，
        /// 再直接读 .warudo 字节确认这些组件真的被链接进了 Mod。
        /// 这里捕获所有异常，避免复核本身破坏恢复和清理流程。
        /// </summary>
        private static HoFastBuildArtifactVerification RunArtifactVerification(BuildState state, object buildResult)
        {
            try
            {
                List<HoFastBuildExpectedComponent> expected = CollectExpectedComponents(state);
                if (expected == null)
                    expected = new List<HoFastBuildExpectedComponent>();

                string artifactPath = ResolveArtifactPath(state, buildResult);
                string modName = ReadActiveModName(state.exportSettingsPath);
                HoFastBuildArtifactVerification verification =
                    HoFastBuildArtifactVerifier.Verify(artifactPath, expected, modName);

                PublishVerification(verification, artifactPath, expected);
                return verification;
            }
            catch (Exception exception)
            {
                Debug.LogError("[HoUnityTools] FastBuild 产物复核未能执行：" + GetRootMessage(exception));
                return null;
            }
        }

        /// <summary>
        /// 期望组件必须来自 UMod 真正打包的那个 Prefab：临时副本已经完成脚本重绑和
        /// 编辑器组件移除，因此它就是产物内容的准确基准。
        /// </summary>
        private static List<HoFastBuildExpectedComponent> CollectExpectedComponents(BuildState state)
        {
            var expected = new List<HoFastBuildExpectedComponent>();
            if (!IsPrefab(state.temporaryPrefabPath))
            {
                Debug.LogWarning("[HoUnityTools] 临时 Character.prefab 不存在，本次复核只能检查产物容器结构。");
                return expected;
            }

            var stagedToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptMapping mapping in state.scripts ?? Array.Empty<ScriptMapping>())
            {
                if (mapping.removeFromPrefab || string.IsNullOrEmpty(mapping.stagedPath))
                    continue;
                stagedToSource[NormalizeAssetPath(mapping.stagedPath)] = NormalizeAssetPath(mapping.sourcePath);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(state.temporaryPrefabPath);
            try
            {
                int missingScripts = 0;
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                    foreach (MonoBehaviour behaviour in child.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour == null)
                            continue;

                        Type behaviourType = behaviour.GetType();
                        if (behaviourType == null || string.IsNullOrEmpty(behaviourType.FullName))
                            continue;

                        MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                        string scriptPath = script == null
                            ? string.Empty
                            : NormalizeAssetPath(AssetDatabase.GetAssetPath(script));

                        string sourcePath = string.Empty;
                        bool staged = !string.IsNullOrEmpty(scriptPath) &&
                                      stagedToSource.TryGetValue(scriptPath, out sourcePath);
                        if (!staged)
                            sourcePath = scriptPath;

                        expected.Add(new HoFastBuildExpectedComponent
                        {
                            transformPath = GetTransformPath(root.transform, child),
                            typeName = behaviourType.FullName,
                            sourcePath = sourcePath ?? string.Empty,
                            sourceAssembly = behaviourType.Assembly == null
                                ? string.Empty
                                : behaviourType.Assembly.GetName().Name,
                            stagedRuntimeScript = staged,
                            hostProvided = !staged && IsHostProvidedRuntimeScript(sourcePath, behaviourType.FullName),
                        });
                    }
                }

                if (missingScripts > 0)
                {
                    Debug.LogWarning(
                        "[HoUnityTools] 临时 Character.prefab 中有 " + missingScripts +
                        " 个 Missing Script，复核无法覆盖这些组件。");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return expected;
        }

        private static string ResolveArtifactPath(BuildState state, object buildResult)
        {
            string builtFile = buildResult == null
                ? string.Empty
                : Convert.ToString(GetMemberValue(buildResult, "BuiltModFile"));
            if (!string.IsNullOrEmpty(builtFile) && File.Exists(builtFile))
                return Path.GetFullPath(builtFile);

            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(state.exportSettingsPath);
            if (settings == null)
                return builtFile;

            string modName;
            string modExportPath;
            int profileIndex;
            if (!TryReadActiveExportProfile(settings, out modName, out modExportPath, out profileIndex))
                return builtFile;

            if (!string.IsNullOrEmpty(modExportPath) && !string.IsNullOrEmpty(modName))
            {
                string candidate = Path.Combine(modExportPath, modName + ".warudo");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return FindNewestArtifact(modExportPath);
        }

        private static string FindNewestArtifact(string exportDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(exportDirectory) || !Directory.Exists(exportDirectory))
                    return string.Empty;

                return Directory.GetFiles(exportDirectory, "*.warudo", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string ReadActiveModName(string settingsAssetPath)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(settingsAssetPath);
            if (settings == null)
                return string.Empty;

            string modName;
            string modExportPath;
            int profileIndex;
            return TryReadActiveExportProfile(settings, out modName, out modExportPath, out profileIndex)
                ? modName
                : string.Empty;
        }

        private static void PublishVerification(
            HoFastBuildArtifactVerification verification,
            string artifactPath,
            List<HoFastBuildExpectedComponent> expected)
        {
            if (verification == null)
                return;

            Debug.Log(verification.report);
            if (verification.missingCount > 0)
            {
                Debug.LogError(
                    "[HoUnityTools] FastBuild 产物复核发现 " + verification.missingCount +
                    " 个组件在产物中缺失，运行时会出现 Missing Script。");
            }
            else if (verification.reviewCount > 0)
            {
                Debug.LogWarning(
                    "[HoUnityTools] FastBuild 产物复核有 " + verification.reviewCount +
                    " 个组件需要人工确认，请查看上面的组件复核列表。");
            }

            WriteVerificationReport(verification);

            foreach (HoFastBuildWarudoModWindow window in Resources.FindObjectsOfTypeAll<HoFastBuildWarudoModWindow>())
            {
                window.lastArtifactPath = artifactPath ?? string.Empty;
                window.lastVerificationSummary = verification.summary;
                window.lastVerificationReport = verification.report;
                window.lastVerificationHasProblems = verification.HasProblems;
                window.lastExpectedComponents = expected ?? new List<HoFastBuildExpectedComponent>();
                window.Repaint();
            }
        }

        private static void WriteVerificationReport(HoFastBuildArtifactVerification verification)
        {
            try
            {
                string directory = StateDirectory;
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    Path.Combine(directory, "last-verification.txt"),
                    verification.report,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[HoUnityTools] 无法写入产物复核报告：" + exception.Message);
            }
        }

        /// <summary>重新复核上一次产物；期望组件已随窗口序列化，无需重新构建。</summary>
        private void ReverifyLastArtifact()
        {
            if (string.IsNullOrEmpty(lastArtifactPath))
            {
                lastVerificationSummary = "还没有可复核的产物，请先执行一次构建。";
                lastVerificationReport = string.Empty;
                lastVerificationHasProblems = false;
                return;
            }

            RefreshExportSettingsPreview();
            HoFastBuildArtifactVerification verification = HoFastBuildArtifactVerifier.Verify(
                lastArtifactPath,
                lastExpectedComponents,
                ReadActiveModName(exportSettingsPath));

            lastVerificationSummary = verification.summary;
            lastVerificationReport = verification.report;
            lastVerificationHasProblems = verification.HasProblems;

            Debug.Log(verification.report);
            if (verification.missingCount > 0)
                Debug.LogError("[HoUnityTools] 复核发现 " + verification.missingCount + " 个组件缺失。");
            else if (verification.reviewCount > 0)
                Debug.LogWarning("[HoUnityTools] 复核有 " + verification.reviewCount + " 个组件需要人工确认。");
        }

        #endregion

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
                Material runtimeDebugMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    NormalizeAssetPath(state.temporaryAssetRoot).TrimEnd('/') +
                    "/" + StagedResourcesDirectoryName + "/HoRuntimeDebugLine.mat");
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null)
                        continue;

                    if (runtimeDebugMaterial != null &&
                        string.Equals(behaviour.GetType().FullName, RuntimeBoneDebugTypeName, StringComparison.Ordinal))
                    {
                        var materialSerializedObject = new SerializedObject(behaviour);
                        SerializedProperty materialProperty = materialSerializedObject.FindProperty("debugMaterial");
                        if (materialProperty != null && materialProperty.objectReferenceValue == null)
                        {
                            materialProperty.objectReferenceValue = runtimeDebugMaterial;
                            materialSerializedObject.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }

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

                    MonoScript stagedScript = AssetDatabase.LoadAssetAtPath<MonoScript>(mapping.stagedPath);
                    if (stagedScript == null)
                        throw new InvalidOperationException("无法加载临时组件脚本：" + mapping.stagedPath);

                    var scriptSerializedObject = new SerializedObject(behaviour);
                    SerializedProperty scriptProperty = scriptSerializedObject.FindProperty("m_Script");
                    if (scriptProperty == null)
                        throw new InvalidOperationException("组件缺少 m_Script 字段：" + behaviour.GetType().FullName);

                    scriptProperty.objectReferenceValue = stagedScript;
                    scriptSerializedObject.ApplyModifiedPropertiesWithoutUndo();
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

        private static void ApplyTemporaryPlayerSettings(BuildState state)
        {
            if (!state.managesStandaloneDefines)
                return;

            string currentDefines = GetStandaloneDefineSymbols();
            if (!string.Equals(currentDefines, state.originalStandaloneDefines, StringComparison.Ordinal))
                throw new InvalidOperationException("Standalone Define Symbols 在构建准备期间发生变化，请重新开始 FastBuild。");

            string temporaryDefines = RemoveScriptingDefine(
                state.originalStandaloneDefines,
                FbxSdkRuntimeDefine);
            if (string.Equals(temporaryDefines, state.originalStandaloneDefines, StringComparison.Ordinal))
                return;

            SetStandaloneDefineSymbols(temporaryDefines);
            Debug.Log("[HoUnityTools] FastBuild 构建期间暂时移除 Standalone Define：" + FbxSdkRuntimeDefine);
        }

        private static void ValidateTemporaryPlayerSettings(BuildState state)
        {
            if (!state.managesStandaloneDefines)
                return;

            string expectedDefines = RemoveScriptingDefine(
                state.originalStandaloneDefines,
                FbxSdkRuntimeDefine);
            string currentDefines = GetStandaloneDefineSymbols();
            if (!string.Equals(currentDefines, expectedDefines, StringComparison.Ordinal))
                throw new InvalidOperationException("Standalone Define Symbols 在等待构建期间发生变化，请重新开始 FastBuild。");
        }

        private static string RemoveScriptingDefine(string defines, string defineToRemove)
        {
            if (string.IsNullOrEmpty(defines))
                return string.Empty;

            return string.Join(
                ";",
                defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrEmpty(value) &&
                                    !string.Equals(value, defineToRemove, StringComparison.Ordinal))
                    .ToArray());
        }

        // NamedBuildTarget 版 API 需要 Unity 2021.2+，新版 Unity 会对旧重载报 CS0618，这里统一走新 API。
        private static string GetStandaloneDefineSymbols()
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
#endif
        }

        private static void SetStandaloneDefineSymbols(string defines)
        {
            string value = defines ?? string.Empty;
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, value);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, value);
#endif
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
            string ignoredName;
            string ignoredExportPath;
            return TryReadActiveExportProfile(settings, out ignoredName, out ignoredExportPath, out modAssetPath,
                out activeProfileIndex);
        }

        private static bool TryReadActiveExportProfile(
            UnityEngine.Object settings,
            out string modName,
            out string modExportPath,
            out int activeProfileIndex)
        {
            string ignoredAssetPath;
            return TryReadActiveExportProfile(settings, out modName, out modExportPath, out ignoredAssetPath,
                out activeProfileIndex);
        }

        private static bool TryReadActiveExportProfile(
            UnityEngine.Object settings,
            out string modName,
            out string modExportPath,
            out string modAssetPath,
            out int activeProfileIndex)
        {
            modName = string.Empty;
            modExportPath = string.Empty;
            modAssetPath = string.Empty;
            activeProfileIndex = 0;
            if (settings == null)
                return false;

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
            SerializedProperty nameProperty = profile.FindPropertyRelative("modName");
            if (nameProperty != null)
                modName = nameProperty.stringValue;
            SerializedProperty exportProperty = profile.FindPropertyRelative("modExportPath");
            if (exportProperty != null)
                modExportPath = exportProperty.stringValue;
            return true;
        }

        private static void FinishBuildState(BuildState state, bool removeTemporaryAssets)
        {
            if (TryRestoreBuildEnvironment(state))
                CompleteBuildCleanup(state, removeTemporaryAssets);
        }

        private static bool TryRestoreBuildEnvironment(BuildState state)
        {
            try
            {
                RestoreExportSettings(state);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[HoUnityTools] 恢复 FastBuild 构建环境失败：" + GetRootMessage(exception));
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
            RestorePlayerSettings(state);

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

        private static void RestorePlayerSettings(BuildState state)
        {
            if (!state.managesStandaloneDefines)
                return;

            string currentDefines = GetStandaloneDefineSymbols();
            if (string.Equals(currentDefines, state.originalStandaloneDefines, StringComparison.Ordinal))
                return;

            SetStandaloneDefineSymbols(state.originalStandaloneDefines ?? string.Empty);
            Debug.Log("[HoUnityTools] 已恢复 Standalone Define Symbols。");
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
