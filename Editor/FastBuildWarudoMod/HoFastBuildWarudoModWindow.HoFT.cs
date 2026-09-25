#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    /// <summary>
    /// FastBuild Warudo Mod —— **HoFT 页**（面捕）：把一份控制器打成 Warudo 能读的 AssetBundle。
    ///
    /// 【为什么单独一页，而不是塞在面捕那套窗口里】
    /// 它产出的是**给 Warudo 用的文件**（bundle 落点通常是 Warudo 的插件沙箱），
    /// 跟"在工程里装配控制器"是两件事：那一页改工程里的资产，这一页只吐一个文件。
    /// 放在 FastBuild 里也顺手 —— 打包和部署是同一类动作。
    ///
    /// 【和「面捕 / 控制器编辑」的分工】
    /// 控制器编辑：**在工作区里**把片段填进混合树、把形态键曲线重绑到调试对象上（改的是工程资产）。
    /// 这一页：把**已经装配好的**那份控制器 + 它绑定的那套 rig 原样打成一个 bundle（不改任何资产）。
    /// 所以流程是：控制器编辑 → 这一页打包 → 把文件放到 Warudo 的插件沙箱 → 节点上选它。
    /// </summary>
    internal sealed partial class HoFastBuildWarudoModWindow
    {
        private const string HoFTBundlePrefKey = "HoUnityTools.FastBuild.HoFT.BundleName";
        private const string HoFTOutputPrefKey = "HoUnityTools.FastBuild.HoFT.OutputDirectory";

        [SerializeField] private RuntimeAnimatorController hoftController;
        [SerializeField] private GameObject hoftRigPrefab;
        [SerializeField] private string hoftBundleName = string.Empty;
        [SerializeField] private string hoftOutputDirectory = string.Empty;
        [SerializeField] private bool hoftOutputIsCustom;
        [SerializeField] private Vector2 hoftScroll;
        [SerializeField] private string hoftStatus = string.Empty;
        [SerializeField] private bool hoftStatusIsError;

        // ---------------------------------------------------------------------
        // 页面
        // ---------------------------------------------------------------------

        private void DrawHoFTPage()
        {
            hoftScroll = EditorGUILayout.BeginScrollView(hoftScroll);
            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawHoFTHeader();
                    GUILayout.Space(6f);
                    DrawHoFTForm();
                    GUILayout.Space(6f);
                    DrawHoFTActions();
                    DrawHoFTFooter();
                }
                GUILayout.Space(10f);
            }

            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHoFTHeader()
        {
            EditorGUILayout.LabelField("面捕控制器 → AssetBundle", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "把一份控制器（混合树那套）和**它绑定的那套 rig** 打成一个文件，给 Warudo 的「HoFace控制求解」用。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawHoFTForm()
        {
            // ── 控制器 ──────────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("控制器", GUILayout.Width(FormLabelWidth));
                var picked = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                    GUIContent.none, hoftController, typeof(RuntimeAnimatorController), false,
                    GUILayout.MinWidth(160f));
                if (picked != hoftController)
                {
                    hoftController = picked;
                    if (!hoftOutputIsCustom && picked != null)
                    {
                        string path = AssetDatabase.GetAssetPath(picked);
                        if (!string.IsNullOrEmpty(path))
                            hoftOutputDirectory = Path.GetDirectoryName(path).Replace('\\', '/');
                    }
                    if (string.IsNullOrEmpty(hoftBundleName) && picked != null)
                        hoftBundleName = picked.name.ToLowerInvariant() + ".bundle";

                    hoftStatus = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.Empty, GUILayout.Width(FormLabelWidth));
                EditorGUILayout.LabelField("必须是纯 Unity AnimatorController（不接受 OverrideController）。",
                    EditorStyles.miniLabel);
            }

            GUILayout.Space(4f);

            // ── 控制器驱动的预制体 ──────────────────────────────────────────────
            // 为什么让用户指：控制器和 rig 常常不是同一个资产，靠"扫绑定反推"只覆盖顺利情况，
            // 而且反推失败时打出来的是个**空 bundle**（运行时静默采不到东西）。
            // 指定之后我们还会拿它**校验**每条绑定的路径能不能解析到，对不上就点名。
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("绑定预制体", GUILayout.Width(FormLabelWidth));
                var pickedRig = (GameObject)EditorGUILayout.ObjectField(
                    GUIContent.none, hoftRigPrefab, typeof(GameObject), false, GUILayout.MinWidth(160f));
                if (pickedRig != hoftRigPrefab)
                {
                    hoftRigPrefab = pickedRig;
                    hoftStatus = string.Empty;
                    GUI.FocusControl(null);
                }

                using (new EditorGUI.DisabledScope(hoftController == null))
                {
                    if (GUILayout.Button("自动找", GUILayout.Width(64f)))
                    {
                        string note;
                        var guessed = HoFTBundleBuilder.GuessRig(hoftController, out note);
                        if (guessed != null) hoftRigPrefab = guessed;
                        hoftStatus = guessed != null ? "已选：" + note : "自动找失败：" + note;
                        hoftStatusIsError = guessed == null;
                        GUI.FocusControl(null);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.Empty, GUILayout.Width(FormLabelWidth));
                EditorGUILayout.LabelField("**这个控制器真正驱动的那套预制体** —— 它会跟着一起打进 bundle，"
                    + "打包时还会拿它校验每条绑定路径。留空则退回\"按绑定自动找\"。", EditorStyles.wordWrappedMiniLabel);
            }

            GUILayout.Space(4f);

            // ── 输出目录 ────────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("输出目录", GUILayout.Width(FormLabelWidth));
                EditorGUI.BeginChangeCheck();
                string edited = EditorGUILayout.TextField(hoftOutputDirectory);
                if (EditorGUI.EndChangeCheck())
                {
                    hoftOutputDirectory = edited;
                    hoftOutputIsCustom = true;
                }

                if (GUILayout.Button("浏览…", GUILayout.Width(56f)))
                {
                    string start = string.IsNullOrEmpty(hoftOutputDirectory) ? Application.dataPath : hoftOutputDirectory;
                    string picked = EditorUtility.OpenFolderPanel("选输出目录", start, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        hoftOutputDirectory = picked.Replace('\\', '/');
                        hoftOutputIsCustom = true;
                    }
                    GUI.FocusControl(null);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(hoftOutputDirectory)))
                {
                    if (GUILayout.Button("打开", GUILayout.Width(48f))) RevealHoFTOutput();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.Empty, GUILayout.Width(FormLabelWidth));
                EditorGUILayout.LabelField("可以是**工程外**的绝对路径 —— Warudo 的插件沙箱就在工程外（就在那儿选）。",
                    EditorStyles.miniLabel);
            }

            GUILayout.Space(4f);

            // ── 文件名 ──────────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("文件名", GUILayout.Width(FormLabelWidth));
                hoftBundleName = EditorGUILayout.TextField(hoftBundleName);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.Empty, GUILayout.Width(FormLabelWidth));
                EditorGUILayout.LabelField("Warudo 的 `控制器` 下拉列表列的就是沙箱里的 `*.bundle`。",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawHoFTActions()
        {
            using (new EditorGUI.DisabledScope(hoftController == null))
            {
                if (GUILayout.Button("打包", GUILayout.Height(24f)))
                {
                    var result = HoFTBundleBuilder.Build(hoftController, hoftRigPrefab, hoftOutputDirectory, hoftBundleName);
                    hoftStatus = result.message;
                    hoftStatusIsError = !result.ok;
                    if (result.warnings.Count > 0)
                        hoftStatus += "\n⚠ " + string.Join("\n⚠ ", result.warnings.ToArray());
                    if (result.ok) EditorUtility.RevealInFinder(result.bundlePath);
                }
            }

            if (hoftController == null)
            {
                EditorGUILayout.HelpBox("先在上面选一份控制器。控制器还没做？用「面捕 / 控制器编辑」那一页装配一份。",
                    MessageType.Info);
            }

            if (!string.IsNullOrEmpty(hoftStatus))
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.HelpBox(hoftStatus, hoftStatusIsError ? MessageType.Error : MessageType.Info);
            }
        }

        private void DrawHoFTFooter()
        {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("打包之后：把这个文件放到 Warudo 的插件沙箱（和中间层配置同一个目录），"
                + "在「HoFace控制求解」的 `控制器` 下拉里选它，然后按节点上的「重读控制器」。",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("⚠️ AssetBundle 与 Unity 版本绑定：必须在 **2021.3.45f2**（= Warudo 本体版本）里打。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void RevealHoFTOutput()
        {
            string path = hoftOutputDirectory;
            if (string.IsNullOrEmpty(path)) return;

            if (!Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("HoFT", "这个目录还不存在：\n" + path, "好");
                return;
            }
            EditorUtility.RevealInFinder(path);
        }

        // ---------------------------------------------------------------------
        // 记住上次填的（换页/重开窗口都还在）
        // ---------------------------------------------------------------------

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
