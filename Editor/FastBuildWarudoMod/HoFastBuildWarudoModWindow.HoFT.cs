#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    /// <summary>HoFT export page: select a controller, let the page infer the rig, export one bundle.</summary>
    internal sealed partial class HoFastBuildWarudoModWindow
    {
        private const string HoFTBundlePrefKey = "HoUnityTools.FastBuild.HoFT.BundleName";
        private const string HoFTOutputPrefKey = "HoUnityTools.FastBuild.HoFT.OutputDirectory";

        [SerializeField] private RuntimeAnimatorController hoftController;
        [SerializeField] private GameObject hoftRigPrefab;
        [SerializeField] private string hoftBundleName = string.Empty;
        [SerializeField] private string hoftOutputDirectory = string.Empty;
        [SerializeField] private bool hoftOutputIsCustom;
        [SerializeField] private bool hoftShowAdvanced;
        [SerializeField] private Vector2 hoftScroll;
        [SerializeField] private string hoftStatus = string.Empty;
        [SerializeField] private bool hoftStatusIsError;

        private void DrawHoFTPage()
        {
            hoftScroll = EditorGUILayout.BeginScrollView(hoftScroll);
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawHoFTHeader();
                    GUILayout.Space(8f);
                    DrawHoFTMain();
                    GUILayout.Space(8f);
                    DrawHoFTActions();
                }
                GUILayout.Space(12f);
            }
            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHoFTHeader()
        {
            EditorGUILayout.LabelField("导出面捕控制器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选一份控制器，自动识别绑定预制体，点一次导出。", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawHoFTMain()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("控制器", GUILayout.Width(FormLabelWidth));
                var picked = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                    GUIContent.none, hoftController, typeof(RuntimeAnimatorController), false, GUILayout.MinWidth(180f));
                if (picked != hoftController)
                {
                    hoftController = picked;
                    PrepareHoFTDefaults();
                    GUI.FocusControl(null);
                }
            }

            if (hoftController == null)
            {
                EditorGUILayout.HelpBox("先选择 AnimatorController。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("自动导出配置", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("绑定预制体", hoftRigPrefab != null ? hoftRigPrefab.name : "自动识别失败，将在导出时重试");
                EditorGUILayout.LabelField("输出文件", string.IsNullOrEmpty(hoftOutputDirectory)
                    ? "未设置输出目录"
                    : Path.Combine(hoftOutputDirectory, string.IsNullOrEmpty(hoftBundleName) ? "(自动命名)" : hoftBundleName).Replace('\\', '/'));
                EditorGUILayout.LabelField("导出内容", "控制器 + 绑定预制体 + 动画片段");
            }

            hoftShowAdvanced = EditorGUILayout.Foldout(hoftShowAdvanced, "高级设置", true);
            if (!hoftShowAdvanced) return;

            using (new EditorGUI.IndentLevelScope())
            {
                hoftRigPrefab = (GameObject)EditorGUILayout.ObjectField("绑定预制体", hoftRigPrefab, typeof(GameObject), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    hoftOutputDirectory = EditorGUILayout.TextField("输出目录", hoftOutputDirectory);
                    if (GUILayout.Button("浏览…", GUILayout.Width(58f)))
                    {
                        string start = string.IsNullOrEmpty(hoftOutputDirectory) ? Application.dataPath : hoftOutputDirectory;
                        string picked = EditorUtility.OpenFolderPanel("选择 AssetBundle 输出目录", start, string.Empty);
                        if (!string.IsNullOrEmpty(picked))
                        {
                            hoftOutputDirectory = picked.Replace('\\', '/');
                            hoftOutputIsCustom = true;
                        }
                        GUI.FocusControl(null);
                    }
                }
                hoftBundleName = EditorGUILayout.TextField("文件名", hoftBundleName);
                EditorGUILayout.LabelField("目标文件名必须以 .bundle 结尾；输出目录可直接指向 Warudo 插件沙箱。", EditorStyles.miniLabel);
            }
        }

        private void DrawHoFTActions()
        {
            using (new EditorGUI.DisabledScope(hoftController == null))
            {
                if (GUILayout.Button("一键导出 AssetBundle", GUILayout.Height(30f)))
                {
                    PrepareHoFTDefaults();
                    var result = HoFTBundleBuilder.Build(hoftController, hoftRigPrefab, hoftOutputDirectory, hoftBundleName);
                    hoftStatus = result.message;
                    hoftStatusIsError = !result.ok;
                    if (result.warnings.Count > 0)
                        hoftStatus += "\n" + string.Join("\n", result.warnings.ToArray());
                    if (result.ok)
                    {
                        SaveHoFTPreferences();
                        EditorUtility.RevealInFinder(result.bundlePath);
                    }
                }
            }

            if (!string.IsNullOrEmpty(hoftStatus))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(hoftStatus, hoftStatusIsError ? MessageType.Error : MessageType.Info);
            }
        }

        private void PrepareHoFTDefaults()
        {
            if (hoftController == null) return;

            if (string.IsNullOrEmpty(hoftBundleName))
                hoftBundleName = hoftController.name.ToLowerInvariant() + ".bundle";
            if (!hoftBundleName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                hoftBundleName += ".bundle";

            if (!hoftOutputIsCustom && string.IsNullOrEmpty(hoftOutputDirectory))
            {
                string controllerPath = AssetDatabase.GetAssetPath(hoftController);
                if (!string.IsNullOrEmpty(controllerPath))
                    hoftOutputDirectory = Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            }

            string note;
            var guessed = HoFTBundleBuilder.GuessRig(hoftController, out note);
            if (guessed != null) hoftRigPrefab = guessed;
            hoftStatus = guessed != null ? "已自动识别绑定预制体：" + guessed.name : string.Empty;
            hoftStatusIsError = false;
            GUI.FocusControl(null);
        }

        private void LoadHoFTPreferences()
        {
            if (string.IsNullOrEmpty(hoftBundleName))
                hoftBundleName = EditorPrefs.GetString(HoFTBundlePrefKey, string.Empty);
            if (string.IsNullOrEmpty(hoftOutputDirectory))
            {
                hoftOutputDirectory = EditorPrefs.GetString(HoFTOutputPrefKey, string.Empty);
                hoftOutputIsCustom = !string.IsNullOrEmpty(hoftOutputDirectory);
            }
        }

        private void SaveHoFTPreferences()
        {
            EditorPrefs.SetString(HoFTBundlePrefKey, hoftBundleName ?? string.Empty);
            EditorPrefs.SetString(HoFTOutputPrefKey, hoftOutputDirectory ?? string.Empty);
        }
    }
}
#endif
