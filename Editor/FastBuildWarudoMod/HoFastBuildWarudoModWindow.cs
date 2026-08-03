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
            public string stagedPath = string.Empty;
            public bool removeFromPrefab;
        }

        [Serializable]
        private sealed class BuildState
        {
            public string phase = string.Empty;
            public string stateFilePath = string.Empty;
            public string sourcePrefabPath = string.Empty;
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
        [SerializeField] private string previewMessage = string.Empty;

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
            window.minSize = new Vector2(680f, 560f);
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

            RefreshExportSettingsPreview();
            if (sourcePrefab != null && scriptPreview.Count == 0)
                RefreshDependencyPreview();
        }

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 132f;
            pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
            GUILayout.Space(8f);

            DrawSourcePanel();
            GUILayout.Space(8f);
            DrawExportSettingsPanel();
            GUILayout.Space(8f);
            DrawDependencyPanel();
            GUILayout.Space(8f);
            DrawBuildOptionsPanel();
            GUILayout.Space(12f);
            DrawBuildButton();

            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSourcePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("构建对象", EditorStyles.boldLabel);
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

                EditorGUILayout.HelpBox(
                    "源 Prefab 不会被修改。构建时会在 Assets 下创建临时副本，并固定命名为 Character.prefab。",
                    MessageType.Info);
            }
        }

        private void DrawExportSettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Warudo / UMod", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("刷新", GUILayout.Width(62f)))
                        RefreshExportSettingsPreview();
                }

                EditorGUILayout.LabelField("ExportSettings", string.IsNullOrEmpty(exportSettingsPath) ? "未找到" : exportSettingsPath);
                EditorGUILayout.LabelField("当前 Mod 目录", string.IsNullOrEmpty(activeModAssetPath) ? "未读取" : activeModAssetPath);

                string apiError;
                bool apiReady = TryValidateOfficialBuildApi(out apiError);
                EditorGUILayout.HelpBox(
                    apiReady
                        ? "已检测到 UMod 官方构建入口：ModToolsUtil.StartBuild。"
                        : apiError,
                    apiReady ? MessageType.Info : MessageType.Error);
            }
        }

        private void DrawDependencyPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("依赖审查", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("重新扫描", GUILayout.Width(78f)))
                        RefreshDependencyPreview();
                }

                EditorGUILayout.LabelField(
                    "资源概览",
                    string.Format("总依赖 {0}，非脚本资源 {1}，组件脚本 {2}，丢失脚本 {3}",
                        dependencyCount,
                        nonScriptDependencyCount,
                        scriptPreview.Count,
                        missingScriptCount));
                if (missingScriptCount > 0)
                    EditorGUILayout.HelpBox("Prefab 中存在 Missing Script，请先修复后再构建。", MessageType.Error);
                EditorGUILayout.HelpBox(
                    "材质、贴图、网格等普通资源仍由 UMod Linker 按 Prefab 引用收集；这里只管理需要交给 UMod 单独编译的 C# 源码。",
                    MessageType.None);
                EditorGUILayout.HelpBox(
                    "脚本预览以直接挂载的 MonoBehaviour 为入口；基类、partial 文件和辅助源码需要单独确认。UMod 编译失败时不会产出成功包。",
                    MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("选择运行时脚本"))
                        SetRuntimeScriptSelection(true);
                    if (GUILayout.Button("全部不复制"))
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

        private static void DrawScriptPreviewRow(ScriptPreview item)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(item.removeWhenExcluded))
                        item.copySource = EditorGUILayout.Toggle(item.copySource, GUILayout.Width(18f));

                    EditorGUILayout.LabelField(
                        string.IsNullOrEmpty(item.typeName) ? Path.GetFileNameWithoutExtension(item.sourcePath) : item.typeName,
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("引用 " + item.referenceCount, GUILayout.Width(52f));
                }

                EditorGUILayout.SelectableLabel(item.sourcePath, EditorStyles.miniLabel, GUILayout.Height(16f));
                if (!string.IsNullOrEmpty(item.note))
                    EditorGUILayout.HelpBox(item.note, item.removeWhenExcluded ? MessageType.Warning : MessageType.None);
            }
        }

        private void DrawBuildOptionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("临时构建设置", EditorStyles.boldLabel);
                copySelectedScripts = EditorGUILayout.ToggleLeft("复制勾选的脚本到临时 Mod 目录", copySelectedScripts);
                removeUnsafeComponents = EditorGUILayout.ToggleLeft("从临时 Prefab 移除明确不适合运行时的组件", removeUnsafeComponents);
                cleanupTemporaryAssets = EditorGUILayout.ToggleLeft("构建完成后自动删除临时目录", cleanupTemporaryAssets);

                EditorGUILayout.HelpBox(
                    "复制源码会短暂进入 Unity 的运行时编译列表；域重载后 UMod 再按完整类型名连接到 Prefab。活动 Mod 目录在结束后始终恢复。",
                    MessageType.Info);
            }
        }

        private void DrawBuildButton()
        {
            bool hasPendingBuild = HasPendingBuildState();
            string apiError;
            bool canBuild = IsPrefab(sourcePrefabPath) &&
                            missingScriptCount == 0 &&
                            !hasPendingBuild &&
                            TryValidateOfficialBuildApi(out apiError) &&
                            !string.IsNullOrEmpty(exportSettingsPath);

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                if (GUILayout.Button("Build Warudo Mod", GUILayout.Height(34f)))
                    BeginBuild();
            }

            if (hasPendingBuild)
                EditorGUILayout.HelpBox("已有一个 FastBuild 流程正在等待脚本编译或构建完成。", MessageType.Warning);
            else if (!string.IsNullOrEmpty(previewMessage))
                EditorGUILayout.HelpBox(previewMessage, MessageType.Info);
        }

        private void SetSourcePrefab(string path)
        {
            sourcePrefabPath = IsPrefab(path) ? path : string.Empty;
            sourcePrefab = string.IsNullOrEmpty(sourcePrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            RefreshDependencyPreview();
            Repaint();
        }

        private void RefreshDependencyPreview()
        {
            scriptPreview.Clear();
            dependencyCount = 0;
            nonScriptDependencyCount = 0;
            missingScriptCount = 0;
            previewMessage = string.Empty;

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
                    rows.Add(path, row);
                }

                row.referenceCount++;
            }

            scriptPreview = rows.Values
                .OrderByDescending(item => item.copySource)
                .ThenBy(item => item.typeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            previewMessage = "依赖预览已更新。";
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

            string ignored;
            int ignoredIndex;
            TryReadActiveModAssetPath(settings, out activeModAssetPath, out ignoredIndex, out ignored);
        }

        private void BeginBuild()
        {
            if (HasPendingBuildState())
                throw new InvalidOperationException("已有未完成的 FastBuild，请等待恢复流程完成。");

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
                previewMessage = "临时目录已生成，正在等待脚本编译后调用 UMod 官方构建 API。";
                SchedulePendingBuildResume();
            }
            catch (Exception exception)
            {
                if (state != null)
                    FinishBuildState(state, true);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(WindowTitle, "准备构建失败：\n" + GetRootMessage(exception), "确定");
            }
        }

        private BuildState CreateStagedBuildState()
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportSettingsPath);
            if (settings == null)
                throw new InvalidOperationException("无法加载 ExportSettings：" + exportSettingsPath);

            string originalModPath;
            string ignoredPropertyPath;
            int activeProfileIndex;
            if (!TryReadActiveModAssetPath(settings, out originalModPath, out activeProfileIndex, out ignoredPropertyPath))
                throw new InvalidOperationException("ExportSettings 中找不到活动配置的 modAssetPath。");

            string buildId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string temporaryRoot = TemporaryAssetRoot + "/" + buildId;
            string temporaryPrefabPath = temporaryRoot + "/Character.prefab";
            string stateFilePath = Path.Combine(StateDirectory, buildId + ".json");
            var state = new BuildState
            {
                phase = PendingPhase,
                stateFilePath = stateFilePath,
                sourcePrefabPath = sourcePrefabPath,
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
                    StageSelectedScripts(temporaryRoot, mappings);

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

        private void StageSelectedScripts(string temporaryRoot, List<ScriptMapping> mappings)
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
                string wrappedSource = "#if !UNITY_EDITOR\n" + sourceText + "\n#endif\n";
                File.WriteAllText(absoluteDestination, wrappedSource, new UTF8Encoding(false));
                WriteFreshMetaFile(absoluteDestination + ".meta");

                mappings.Add(new ScriptMapping
                {
                    sourcePath = item.sourcePath,
                    stagedPath = stagedPath,
                    removeFromPrefab = false,
                });
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

                    var serializedBehaviour = new SerializedObject(behaviour);
                    SerializedProperty scriptProperty = serializedBehaviour.FindProperty("m_Script");
                    MonoScript currentScript = scriptProperty == null
                        ? null
                        : scriptProperty.objectReferenceValue as MonoScript;
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
            out int activeProfileIndex,
            out string serializedPropertyPath)
        {
            modAssetPath = string.Empty;
            activeProfileIndex = 0;
            serializedPropertyPath = string.Empty;

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty activeProfile = serializedSettings.FindProperty("activeProfile");
            SerializedProperty profiles = serializedSettings.FindProperty("exportProfiles");
            if (profiles == null || !profiles.isArray || profiles.arraySize == 0)
                return false;

            activeProfileIndex = activeProfile == null
                ? 0
                : Mathf.Clamp(activeProfile.intValue, 0, profiles.arraySize - 1);
            SerializedProperty profile = profiles.GetArrayElementAtIndex(activeProfileIndex);
            SerializedProperty pathProperty = profile.FindPropertyRelative("modAssetPath");
            if (pathProperty == null)
                return false;

            modAssetPath = pathProperty.stringValue;
            serializedPropertyPath = pathProperty.propertyPath;
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
