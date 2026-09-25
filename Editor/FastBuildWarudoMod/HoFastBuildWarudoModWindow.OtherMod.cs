#if UNITY_EDITOR
// =============================================================================
// FastBuild Warudo Mod —— 「其他 Mod」页
//
// 面板节奏与角色页完全一致：
//     来源 → Warudo 工作区 → 内容检查 → 一个大构建按钮 → 产物复核
//
// 与角色页真正的差异只有一条：其他 Mod 不需要临时副本。
// 资产目录是用户已经准备好的文件夹，脚本早就被 Unity 导入并进了 .csproj，
// 所以这里直接构建，不触发域重载。
//
// 关于"类型"：.warudo 包里**不存类型**（modinfo.dat 与 sharedassets.meta 里都没有类型字段）。
// 类型只是两条约定：产物落在哪个目录、目录里的入口资产叫什么名字。
// 因此这里从目录内容**自动识别**类型，识别不出来才让用户选。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    internal sealed partial class HoFastBuildWarudoModWindow
    {
        private enum ModBuildPage
        {
            Character,
            OtherMod,
            HoFT,
        }

        /// <summary>决定产物落点目录与入口资产命名。不写进包，只是构建期约定。</summary>
        private enum OtherModKind
        {
            Unknown,
            Prop,
            Particle,
            Environment,
            CharacterAnimation,
            Plugin,
        }

        private sealed class OtherModKindInfo
        {
            public OtherModKind kind;
            public string label;
            public string dataFolder;
            public string rootAssetName;
            public string rootAssetType;
        }

        private static readonly OtherModKindInfo[] OtherModKinds =
        {
            new OtherModKindInfo { kind = OtherModKind.Prop, label = "道具", dataFolder = "Props",
                rootAssetName = "Prop", rootAssetType = "t:GameObject" },
            new OtherModKindInfo { kind = OtherModKind.Particle, label = "粒子", dataFolder = "Particles",
                rootAssetName = "Particle", rootAssetType = "t:GameObject" },
            new OtherModKindInfo { kind = OtherModKind.Environment, label = "环境", dataFolder = "Environments",
                rootAssetName = "Environment", rootAssetType = "t:SceneAsset" },
            new OtherModKindInfo { kind = OtherModKind.CharacterAnimation, label = "角色动画",
                dataFolder = "CharacterAnimations", rootAssetName = "Animation", rootAssetType = "t:AnimationClip" },
            new OtherModKindInfo { kind = OtherModKind.Plugin, label = "插件", dataFolder = "Plugins",
                rootAssetName = string.Empty, rootAssetType = string.Empty },
        };

        private static readonly string[] AllWarudoModFolders =
        {
            "Characters", "CharacterAnimations", "Environments", "Props", "Particles", "Plugins",
        };

        [SerializeField] private ModBuildPage currentPage = ModBuildPage.Character;

        [SerializeField] private string otherModFolderPath = string.Empty;
        [SerializeField] private bool otherModKindIsAuto = true;
        [SerializeField] private OtherModKind otherModKindOverride = OtherModKind.Prop;
        [SerializeField] private string otherModName = string.Empty;
        /// <summary>用户手动改过名字。没改过时名字跟随资产目录，换目录就自动更新。</summary>
        [SerializeField] private bool otherModNameIsCustom;
        [SerializeField] private string otherModAuthor = string.Empty;
        [SerializeField] private string otherModVersion = "1.0.0";
        [SerializeField] private string otherModDescription = string.Empty;
        [SerializeField] private bool otherModAutoExportPath = true;
        [SerializeField] private string otherModExportPath = string.Empty;
        [SerializeField] private bool otherModShowDetails;
        [SerializeField] private bool otherModShowScripts;
        [SerializeField] private Vector2 otherModScroll;
        /// <summary>上一次看到的"活动工作区"下标。用来发现用户在「Warudo 工作区」面板里切了工作区。</summary>
        [SerializeField] private int otherModLastActiveWorkspaceIndex = -2;
        [SerializeField] private Vector2 otherModScriptScroll;

        // ---------------------------------------------------------------------
        // 页签
        // ---------------------------------------------------------------------

        private void DrawBuildPageToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                int selected = GUILayout.Toolbar(
                    (int)currentPage,
                    new[] { "角色 Mod", "其他 Mod", "HoFT" },
                    EditorStyles.toolbarButton,
                    GUILayout.Width(360f));
                GUILayout.FlexibleSpace();

                if (selected != (int)currentPage)
                {
                    currentPage = (ModBuildPage)selected;
                    if (sdkAvailable)
                        RefreshExportSettingsPreview();
                    if (currentPage == ModBuildPage.HoFT)
                        LoadHoFTPreferences();
                    else
                        SaveHoFTPreferences();
                    GUI.FocusControl(null);
                }
            }
        }

        // ---------------------------------------------------------------------
        // 页面
        // ---------------------------------------------------------------------

        private void DrawOtherModPage()
        {
            // 在「Warudo 工作区」面板里切工作区 = 切"要构建哪个 Mod"。
            // 不把这一步接起来的话，用户切到 Plugins 工作区，页面上的资产目录还停在 Props，
            // 构建出来的自然还是 Props.warudo。
            SyncOtherModFromActiveWorkspace();

            // 一次算清，下面各面板只读（并且带缓存，见 GetOtherModContext）。
            OtherModContext context = GetOtherModContext();

            otherModScroll = EditorGUILayout.BeginScrollView(otherModScroll);
            GUILayout.Space(8f);
            DrawWindowHeader(sdkAvailable, sdkError);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUI.DisabledScope(!sdkAvailable))
                    {
                        DrawOtherModSourcePanel(context);
                        GUILayout.Space(8f);
                        DrawExportSettingsPanel();
                        GUILayout.Space(8f);
                        DrawOtherModCheckPanel(context);
                        GUILayout.Space(12f);
                        DrawOtherModBuildButton(context);
                        GUILayout.Space(8f);
                        DrawVerificationPanel();
                    }
                }
                GUILayout.Space(10f);
            }

            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>来源面板：资产目录 + 类型 + Mod 信息。对应角色页的「源 Prefab」。</summary>
        private void DrawOtherModSourcePanel(OtherModContext context)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "Mod 资产目录",
                    context.hasFolder ? context.kindInfo.label : "未选择",
                    new Color(0.24f, 0.54f, 0.88f),
                    // 图标名必须是 Unity 内置图标名，写错会在 Console 刷
                    // "Unable to load the icon: 'xxx'"。本工程已验证可用的只有：
                    // "Prefab Icon" / "Folder Icon" / "GameObject Icon" / "Settings" /
                    // "TestPassed" / "FilterByType"。
                    "Folder Icon",
                    "把这个文件夹直接构建成 .warudo。文件夹里的一切都会被打包。");

                GUILayout.Space(5f);

                var current = string.IsNullOrEmpty(otherModFolderPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<DefaultAsset>(otherModFolderPath);
                var picked = (DefaultAsset)EditorGUILayout.ObjectField(
                    "资产目录", current, typeof(DefaultAsset), false);
                string pickedPath = picked == null ? string.Empty : AssetDatabase.GetAssetPath(picked);
                if (!string.Equals(pickedPath, otherModFolderPath, StringComparison.Ordinal))
                {
                    otherModFolderPath = pickedPath;
                    otherModKindIsAuto = true;               // 换目录就重新探测
                    otherModExportPath = string.Empty;
                    // 名字跟随目录，除非用户自己改过。
                    if (!otherModNameIsCustom)
                        otherModName = string.IsNullOrEmpty(pickedPath) ? string.Empty : Path.GetFileName(pickedPath);
                }

                if (!context.hasFolder)
                {
                    GUILayout.Space(4f);
                    EditorGUILayout.HelpBox(
                        "把 Mod 资产目录拖到这里。它必须位于 Assets 下，不能是 Assets 根目录，" +
                        "也不能放在 Unity 忽略的目录（以 . 开头或以 ~ 结尾）。",
                        MessageType.Info);
                    return;
                }

                // 类型：默认自动识别，识别不出来或用户想改时才手动选。
                GUILayout.Space(3f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (otherModKindIsAuto)
                    {
                        EditorGUILayout.LabelField("类型", context.kindInfo.label + context.kindEvidence);
                        if (context.kindInfo.kind == OtherModKind.Unknown &&
                            GUILayout.Button("手动指定类型…", GUILayout.Width(118f)))
                            otherModKindIsAuto = false;
                    }
                    else
                    {
                        int index = Array.FindIndex(OtherModKinds, info => info.kind == otherModKindOverride);
                        int selected = EditorGUILayout.Popup(
                            "类型",
                            index < 0 ? 0 : index,
                            OtherModKinds.Select(info => info.label).ToArray());
                        otherModKindOverride = OtherModKinds[Mathf.Clamp(selected, 0, OtherModKinds.Length - 1)].kind;
                        if (GUILayout.Button("自动", EditorStyles.miniButton, GUILayout.Width(48f)))
                            otherModKindIsAuto = true;
                    }
                }

                // 名字跟随目录：没手动改过就保持同步。
                // 这一条同时会纠正上次会话残留的旧名字（否则不换目录它永远不会更新）。
                if (!otherModNameIsCustom)
                {
                    string expected = Path.GetFileName(otherModFolderPath);
                    if (!string.Equals(otherModName, expected, StringComparison.Ordinal))
                        otherModName = expected;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    otherModName = EditorGUILayout.TextField("名称", otherModName);
                    if (EditorGUI.EndChangeCheck())
                        otherModNameIsCustom = true;

                    if (otherModNameIsCustom &&
                        GUILayout.Button(
                            new GUIContent("跟随目录", "改回按资产目录名自动命名"),
                            EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        otherModNameIsCustom = false;
                        otherModName = Path.GetFileName(otherModFolderPath);
                    }
                }
                DrawDetailRow("导出到", string.IsNullOrEmpty(context.exportPath) ? "（推不出来）" : context.exportPath);

                otherModShowDetails = EditorGUILayout.Foldout(otherModShowDetails, "更多信息", true);
                if (otherModShowDetails)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        otherModAuthor = EditorGUILayout.TextField("作者", otherModAuthor);
                        otherModVersion = EditorGUILayout.TextField("版本", otherModVersion);
                        otherModDescription = EditorGUILayout.TextField("描述", otherModDescription);
                        otherModAutoExportPath = EditorGUILayout.ToggleLeft(
                            "自动推导导出目录（跟活动工作区）", otherModAutoExportPath);
                        using (new EditorGUI.DisabledScope(otherModAutoExportPath))
                        {
                            otherModExportPath = EditorGUILayout.TextField("导出目录", otherModExportPath);
                        }
                    }
                }
            }
        }

        /// <summary>内容检查：正常情况下只说一句话，有问题才展开。</summary>
        private void DrawOtherModCheckPanel(OtherModContext context)
        {
            if (!context.hasFolder)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader(
                    "内容检查",
                    context.errors.Count > 0 ? "有问题" : "正常",
                    context.errors.Count > 0 ? new Color(0.88f, 0.42f, 0.28f) : new Color(0.20f, 0.68f, 0.57f),
                    "TestPassed",
                    "UMod 只编译工程 .csproj 收录的脚本；工作区里的每个 .cs 都会进 Mod 程序集。");

                GUILayout.Space(5f);

                foreach (string error in context.errors)
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                foreach (string warning in context.warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);

                if (context.errors.Count == 0 && context.warnings.Count == 0)
                {
                    string scripts = context.scripts.Count == 0
                        ? "无脚本"
                        : context.scripts.Count + " 个脚本";
                    EditorGUILayout.LabelField(
                        scripts + " · " + context.rootAssetSummary + " · 工作区 " + context.profileStatus,
                        EditorStyles.miniLabel);
                }

                if (context.scripts.Count > 0)
                {
                    otherModShowScripts = EditorGUILayout.Foldout(otherModShowScripts, "会进包的脚本", true);
                    if (otherModShowScripts)
                    {
                        using (new EditorGUI.IndentLevelScope())
                        using (var scroll = new EditorGUILayout.ScrollViewScope(
                                   otherModScriptScroll, GUILayout.MinHeight(60f), GUILayout.MaxHeight(160f)))
                        {
                            otherModScriptScroll = scroll.scrollPosition;
                            foreach (string script in context.scripts)
                                EditorGUILayout.LabelField(script, EditorStyles.miniLabel);
                        }
                    }
                }
            }
        }

        /// <summary>与角色页同款：一个全宽主按钮。</summary>
        private void DrawOtherModBuildButton(OtherModContext context)
        {
            bool hasPendingBuild = HasPendingBuildState();

            using (new EditorGUI.DisabledScope(!context.canBuild || hasPendingBuild))
            {
                if (GUILayout.Button("构建 Warudo Mod", primaryButtonStyle))
                {
                    BuildOtherMod();
                    GUIUtility.ExitGUI();
                }
            }

            if (hasPendingBuild)
                EditorGUILayout.HelpBox("已有一个 FastBuild 流程正在等待脚本编译或构建完成。", MessageType.Warning);
            else if (!context.canBuild && context.hasFolder && context.errors.Count == 0)
                EditorGUILayout.HelpBox("导出目录推不出来，请在「更多信息」里手动指定。", MessageType.Warning);
        }

        // ---------------------------------------------------------------------
        // 一帧一次的上下文
        // ---------------------------------------------------------------------

        private sealed class OtherModContext
        {
            public bool hasFolder;
            public OtherModKindInfo kindInfo;
            public List<OtherModKindInfo> detectedKinds = new List<OtherModKindInfo>();
            public string kindEvidence = string.Empty;
            public string exportPath = string.Empty;
            public string profileStatus = string.Empty;
            public string rootAssetSummary = string.Empty;
            public List<string> scripts = new List<string>();
            /// <summary>类型是**真的从入口资产探测出来**的。手动指定、或按工作区推断出来的都不算。</summary>
            public bool kindDetectedFromEntry;
            public readonly List<string> errors = new List<string>();
            public readonly List<string> warnings = new List<string>();
            public bool canBuild;
        }

        private OtherModContext BuildOtherModContext()
        {
            var context = new OtherModContext();
            context.hasFolder = !string.IsNullOrEmpty(otherModFolderPath) && AssetDatabase.IsValidFolder(otherModFolderPath);
            context.scripts = context.hasFolder ? CollectOtherModScripts(otherModFolderPath) : new List<string>();

            // 类型：自动探测优先。同一目录里出现多个类别的入口资产，就是"一坨东西放一起"，
            // 那只有一个会生效 —— 必须当场拦下来，不能等装进 Warudo 才发现没反应。
            List<OtherModKindInfo> detected = DetectOtherModKinds(context);
            context.detectedKinds = detected;

            if (!otherModKindIsAuto && otherModKindOverride != OtherModKind.Unknown)
            {
                context.kindInfo = Info(otherModKindOverride);
                context.kindEvidence = "（手动指定）";
            }
            else if (detected.Count > 0)
            {
                context.kindInfo = detected[0];
                context.kindDetectedFromEntry = true;
                context.kindEvidence = "（自动识别）";
            }
            else
            {
                // 目录里没有可识别的入口资产。退一步：用**当前活动工作区的导出目录**推断。
                // 工作区本来就是一个类别一个，导出目录末段就是类别名，这个信息足够可靠，
                // 不该逼用户去点「手动指定类型」。
                OtherModKindInfo inferred = InferKindFromActiveWorkspace();
                context.kindInfo = inferred ?? Info(OtherModKind.Unknown);
                context.kindEvidence = inferred != null
                    ? "（按工作区导出目录推断：" + inferred.label + "）"
                    : "（未识别）";
            }

            context.exportPath = ResolveOtherModExportPath(context.kindInfo);

            if (!context.hasFolder)
                return context;

            // --- 目录合法性 ---
            if (!otherModFolderPath.StartsWith("Assets/", StringComparison.Ordinal))
                context.errors.Add("资产目录必须位于 Assets 下。");
            else if (string.Equals(otherModFolderPath, "Assets", StringComparison.Ordinal))
                context.errors.Add("资产目录不能是 Assets 根目录，否则整个工程都会被打包。");
            else if (IsUnityIgnoredFolder(otherModFolderPath))
                context.errors.Add("资产目录位于 Unity 忽略的目录（以 . 开头或以 ~ 结尾），Prefab 与脚本不会被导入。");

            if (string.IsNullOrWhiteSpace(otherModName))
                context.errors.Add("Mod 名称为空。");
            else if (otherModName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                context.errors.Add("Mod 名称里有非法字符。");

            if (string.IsNullOrEmpty(context.exportPath))
                context.errors.Add("导出目录为空。");

            // --- 类型约定 ---
            if (detected.Count > 1)
            {
                context.errors.Add(
                    "这个目录里有 " + detected.Count + " 个类别的入口资产：" +
                    string.Join(" / ", detected.Select(info => info.label + "（" + info.rootAssetName + "）")) +
                    "。\n一个 .warudo 只会被落点目录对应的那一个加载器认领，其余的静默不生效（不报错）。" +
                    "\n请按类别拆成不同目录，分别构建。" +
                    (otherModKindIsAuto ? string.Empty : "（当前手动指定为「" + context.kindInfo.label + "」）"));
            }

            if (context.kindInfo.kind == OtherModKind.Unknown)
            {
                context.errors.Add(
                    "看不出这是哪一类 Mod。目录里需要有 Prop/Particle 预制体、Environment 场景、" +
                    "Animation 动画，或一个带 [PluginType] 的类；也可以手动选类型。");
                context.rootAssetSummary = "入口资产未识别";
            }
            else if (context.kindInfo.kind == OtherModKind.Plugin)
            {
                bool hasPlugin = context.scripts.Any(LooksLikePlugin);
                context.rootAssetSummary = hasPlugin ? "插件入口 [PluginType]" : "缺 [PluginType]";
                if (!hasPlugin)
                    context.errors.Add("插件 Mod 需要资产目录里有继承 Plugin 且带 [PluginType] 的类。");
            }
            else
            {
                string found = FindRootAsset(otherModFolderPath, context.kindInfo.rootAssetName,
                    context.kindInfo.rootAssetType);
                context.rootAssetSummary = string.IsNullOrEmpty(found)
                    ? "缺 " + context.kindInfo.rootAssetName
                    : Path.GetFileName(found);
                if (string.IsNullOrEmpty(found))
                {
                    // 类型是真从入口资产探测出来的 -> 现在却找不到它，属于异常，拦住。
                    // 类型是手动指定或按工作区推断的 -> 允许没有约定名，
                    // 只警告放行（用户可能正是在验证"入口名到底是不是硬约定"）。
                    if (context.kindDetectedFromEntry)
                    {
                        context.errors.Add(
                            context.kindInfo.label + " Mod 需要名为 " + context.kindInfo.rootAssetName +
                            " 的入口资产（" + context.kindInfo.rootAssetType + "）。");
                    }
                    else
                    {
                        context.warnings.Add(
                            "目录里没有名为 " + context.kindInfo.rootAssetName + " 的入口资产（" +
                            context.kindInfo.rootAssetType + "）。已按类型「" + context.kindInfo.label +
                            "」继续构建，但**这样构建出来的包 Warudo 用不了** —— " +
                            "入口资产名是硬约定（道具实测：换成别的名字后不能正常使用并且报错）。" +
                            "只有做这类验证实验时才该这么构建。");
                    }
                }
            }

            // --- 脚本工程收录（UMod 的判据）---
            if (context.scripts.Count > 0 && !IsFolderCoveredByProjectFiles(otherModFolderPath))
                context.errors.Add(
                    "资产目录里的脚本没有被工程根目录的任何 .csproj 收录，UMod 会跳过脚本编译，" +
                    "产物不会有 assemblymodules.dat。请 Regenerate project files 后重试。");

            // --- 工作区 ---
            context.profileStatus = DescribeOtherModProfile();

            // 这里**不能**调 UMod 的 ValidateName / ValidateAssetPath / ValidateVersion：
            // 它们只校验「活动工作区」，想在绘制期用就必须先 SetActiveExportProfile，
            // 而那会把用户在工作区列表里的选择每帧顶回去（表现为"看着切了其实没切"）。
            // 这些校验改到 ApplyOtherModProfile 里做 —— 真正要写入和构建的时候。

            context.canBuild = context.errors.Count == 0 && !string.IsNullOrEmpty(context.exportPath);
            return context;
        }

        // ---------------------------------------------------------------------
        // 上下文缓存
        //
        // BuildOtherModContext 里有多次 AssetDatabase.FindAssets（探测入口资产），
        // 五万级资源的工程里每帧跑一遍会把面板拖到没反应。
        // 所以：输入没变就复用，输入变了立刻重算，另外最多每 0.5 秒兜底刷一次
        // （这样新加的资产不用手动刷新也会出现）。
        // ---------------------------------------------------------------------

        private OtherModContext otherModContextCache;
        private string otherModContextSignature = string.Empty;
        private double otherModContextNextRefresh;

        private OtherModContext GetOtherModContext()
        {
            string signature = string.Join("|",
                otherModFolderPath,
                otherModName,
                otherModKindIsAuto ? "auto" : otherModKindOverride.ToString(),
                otherModAutoExportPath ? otherModExportPath : "auto-export",
                exportSettingsPath,
                workspaceEntries.Count.ToString(),
                activeWorkspaceIndex.ToString());

            bool inputsChanged = !string.Equals(signature, otherModContextSignature, StringComparison.Ordinal);
            bool throttleExpired = EditorApplication.timeSinceStartup >= otherModContextNextRefresh;

            if (!inputsChanged && !throttleExpired && otherModContextCache != null)
                return otherModContextCache;

            otherModContextSignature = signature;
            otherModContextNextRefresh = EditorApplication.timeSinceStartup + 0.5;
            otherModContextCache = BuildOtherModContext();
            return otherModContextCache;
        }

        /// <summary>
        /// 从目录内容推断 Mod 类型，**返回全部命中项**（不是第一个）。
        /// 命中的项多于一个，说明用户把不同类别的东西放在了同一个目录里，
        /// 而 Warudo 只会认领落点目录对应的那一个，其余静默失效。
        ///
        /// 判据来自 Warudo 自己的加载方式：入口资产按固定名字找，插件看 [PluginType]。
        /// </summary>
        private List<OtherModKindInfo> DetectOtherModKinds(OtherModContext context)
        {
            var matched = new List<OtherModKindInfo>();
            if (!context.hasFolder)
                return matched;

            foreach (OtherModKindInfo info in OtherModKinds.Where(item => !string.IsNullOrEmpty(item.rootAssetName)))
            {
                if (!string.IsNullOrEmpty(FindRootAsset(otherModFolderPath, info.rootAssetName, info.rootAssetType)))
                    matched.Add(info);
            }

            // 插件是另一套机制（看 [PluginType]），独立判定：
            // 如果它和某个入口资产同时出现，也属于"不同类别混在一起"。
            if (context.scripts.Any(LooksLikePlugin))
                matched.Add(Info(OtherModKind.Plugin));

            return matched;
        }

        /// <summary>
        /// 用户在「Warudo 工作区」面板里换了活动工作区时，把它的资产目录装进本页。
        /// 只在**下标真的变了**的那一帧动作，所以不会跟用户手填的目录打架：
        /// 构建时 ApplyOtherModProfile 也会 SetActiveExportProfile，
        /// 但那时的目录已经一致，判等会直接跳过。
        /// </summary>
        private void SyncOtherModFromActiveWorkspace()
        {
            if (activeWorkspaceIndex == otherModLastActiveWorkspaceIndex)
                return;

            // 工作区列表还没读出来时先别记下标，否则会漏掉第一次同步。
            if (workspaceEntries.Count == 0)
                return;

            otherModLastActiveWorkspaceIndex = activeWorkspaceIndex;

            if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaceEntries.Count)
                return;

            WorkspaceEntry entry = workspaceEntries[activeWorkspaceIndex];
            if (entry == null || string.IsNullOrEmpty(entry.modAssetPath))
                return;

            string assetPath = ToRelativeAssetPath(entry.modAssetPath);   // 统一成 Assets/...
            if (!AssetDatabase.IsValidFolder(assetPath))
                return;

            if (string.Equals(ToCanonicalAssetPath(assetPath), ToCanonicalAssetPath(otherModFolderPath),
                    StringComparison.OrdinalIgnoreCase))
                return;   // 已经是同一条，别动用户正在填的东西

            otherModFolderPath = assetPath;
            otherModKindIsAuto = true;
            otherModExportPath = string.Empty;

            // 工作区名和目录名一致时继续"名字跟随目录"；用户起过别名就当作手动命名保留。
            otherModName = entry.modName;
            otherModNameIsCustom = !string.Equals(entry.modName, Path.GetFileName(assetPath),
                StringComparison.Ordinal);
        }

        private static OtherModKindInfo Info(OtherModKind kind)
        {
            OtherModKindInfo info = OtherModKinds.FirstOrDefault(item => item.kind == kind);
            return info ?? new OtherModKindInfo
            {
                kind = OtherModKind.Unknown,
                label = "未识别",
                dataFolder = string.Empty,
                rootAssetName = string.Empty,
                rootAssetType = string.Empty,
            };
        }

        // ---------------------------------------------------------------------
        // 校验小工具
        // ---------------------------------------------------------------------

        private static bool InvokeValidator(UnityEngine.Object settings, string methodName)
        {
            try
            {
                return InvokeOtherModMethod(settings, methodName, null) is bool flag && flag;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 把资产目录统一成绝对路径再比较。
        ///
        /// 工作区里的 modAssetPath 有两种写法并存：官方「New Mod」向导写的是相对路径
        /// （Assets/...），而本项目其它工作区可能是绝对路径（D:/.../Assets/...）。
        /// 直接比字符串永远不相等，会让每次构建都误判成"新工作区"而重复创建。
        /// </summary>
        private static string ToCanonicalAssetPath(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (normalized.Length == 0)
                return string.Empty;

            string projectRoot = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');
            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return projectRoot + "/Assets";
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return projectRoot + "/" + normalized;

            return normalized;
        }

        /// <summary>反过来：把绝对路径还原成 "Assets/..."，用于和 .csproj 里的路径比对。</summary>
        private static string ToRelativeAssetPath(string path)
        {
            string canonical = ToCanonicalAssetPath(path);
            if (canonical.Length == 0)
                return string.Empty;

            string projectRoot = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');
            return canonical.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? canonical.Substring(projectRoot.Length + 1)
                : canonical;
        }

        private static bool IsUnityIgnoredFolder(string assetFolder)
        {
            foreach (string segment in assetFolder.Split('/'))
            {
                if (segment.StartsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith("~", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string FindRootAsset(string folder, string assetName, string typeFilter)
        {
            foreach (string guid in AssetDatabase.FindAssets(typeFilter, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), assetName, StringComparison.Ordinal))
                    return path;
            }
            return string.Empty;
        }

        /// <summary>资产目录里所有 .cs 的资产相对路径（Assets/...）。</summary>
        private static List<string> CollectOtherModScripts(string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return result;

            string absolute = Path.GetFullPath(folder);
            if (!Directory.Exists(absolute))
                return result;

            string projectRoot = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');
            foreach (string file in Directory.GetFiles(absolute, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(projectRoot.Length + 1);
                result.Add(normalized);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static readonly Regex PluginPattern = new Regex(@"\[\s*PluginType", RegexOptions.Compiled);

        private static readonly Regex LineCommentPattern = new Regex(@"//[^\n]*", RegexOptions.Compiled);

        private static readonly Regex BlockCommentPattern =
            new Regex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// 判定工作区里有没有插件入口。
        /// .cs 在 Unity 里是 MonoScript 而不是 TextAsset，所以按源码文本读磁盘文件；
        /// 判定前先去掉注释，免得文档注释里写个 [PluginType] 就被误判成插件。
        /// </summary>
        private static bool LooksLikePlugin(string assetPath)
        {
            try
            {
                string absolute = Path.GetFullPath(assetPath);
                if (!File.Exists(absolute))
                    return false;

                string text = BlockCommentPattern.Replace(File.ReadAllText(absolute), " ");
                text = LineCommentPattern.Replace(text, " ");
                return PluginPattern.IsMatch(text);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// UMod 靠工程根目录 .csproj 里的 &lt;Compile Include="Assets\..."&gt; 决定编译哪些脚本。
        /// </summary>
        private static bool IsFolderCoveredByProjectFiles(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder))
                return false;

            // .csproj 里写的是 "Assets\..." 形式，所以这里的判据也必须用相对形式。
            string marker = ToRelativeAssetPath(assetFolder).TrimEnd('/') + "/";
            string[] projects;
            try
            {
                projects = Directory.GetFiles(ProjectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (string project in projects)
            {
                string text;
                try
                {
                    text = File.ReadAllText(project).Replace('\\', '/');
                }
                catch (Exception)
                {
                    continue;
                }

                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------
        // 工作区（Export Profile）
        // ---------------------------------------------------------------------

        /// <summary>
        /// 按**资产目录**定位工作区。
        /// 目录才是身份：一个目录一条工作区；名称只是标签，面板里随时可改（改完写回这条）。
        /// 早先这里还要求名称也相等，结果官方向导写的相对路径 / 本页写的绝对路径一混，
        /// 就永远匹配不上、每次构建都新建一条。
        /// </summary>
        private int FindOtherModProfileIndex(UnityEngine.Object settings, string assetFolder)
        {
            var profiles = GetMemberValue(settings, "ExportProfiles") as Array;
            if (profiles == null)
                return -1;

            string canonical = ToCanonicalAssetPath(assetFolder);
            if (string.IsNullOrEmpty(canonical))
                return -1;

            for (int index = 0; index < profiles.Length; index++)
            {
                object profile = profiles.GetValue(index);
                var path = GetMemberValue(profile, "ModAssetsPath") as string;
                if (string.Equals(ToCanonicalAssetPath(path), canonical, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return -1;
        }

        /// <summary>UMod 不允许同名工作区。除了 exceptIndex，还有谁叫这个名字？</summary>
        private int FindOtherModProfileIndexByName(UnityEngine.Object settings, string modName, int exceptIndex)
        {
            var profiles = GetMemberValue(settings, "ExportProfiles") as Array;
            if (profiles == null)
                return -1;

            for (int index = 0; index < profiles.Length; index++)
            {
                if (index == exceptIndex)
                    continue;

                if (string.Equals(GetMemberValue(profiles.GetValue(index), "ModName") as string, modName,
                        StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }

        private string DescribeOtherModProfile()
        {
            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            int index = settings == null ? -1 : FindOtherModProfileIndex(settings, otherModFolderPath);
            return index < 0 ? "尚未建立" : "已存在";
        }

        /// <summary>
        /// 找到或新建本 Mod 的工作区并写入当前配置，然后设为活动工作区。
        /// 构建时自动执行，用户不需要单独点一次。
        /// </summary>
        private bool ApplyOtherModProfile(OtherModContext context, out string error)
        {
            error = string.Empty;
            UnityEngine.Object settings = LoadExportSettingsAsset(exportSettingsPath);
            if (settings == null)
            {
                error = "找不到 UMod ExportSettings 资源。";
                return false;
            }

            string assetFolder = ToCanonicalAssetPath(otherModFolderPath);

            // 目录是身份：这条目录已经有工作区了就直接用它，名字改掉就好（等于给它改名）。
            int index = FindOtherModProfileIndex(settings, otherModFolderPath);
            if (index < 0)
            {
                // 只有 UMod 的硬约束才拦：不允许两条工作区重名。
                int nameOwner = FindOtherModProfileIndexByName(settings, otherModName, -1);
                if (nameOwner >= 0)
                {
                    error = "已有同名工作区「" + otherModName + "」，UMod 不允许重名 —— " +
                            "请换个名字，或先删掉那条工作区。";
                    return false;
                }

                if (!TryInvokeExportSettingsMethod(settings, "CreateNewExportProfile", new object[] { true }))
                {
                    error = "调用 CreateNewExportProfile 失败。";
                    return false;
                }
                index = (int)GetMemberValue(settings, "ActiveExportProfileIndex");
            }
            else
            {
                TryInvokeExportSettingsMethod(settings, "SetActiveExportProfile", new object[] { index });
            }

            var profiles = GetMemberValue(settings, "ExportProfiles") as Array;
            if (profiles == null || index < 0 || index >= profiles.Length)
            {
                error = "定位工作区失败。";
                return false;
            }

            object profile = profiles.GetValue(index);
            WriteOtherModMember(profile, "ModName", otherModName);
            if (!string.IsNullOrEmpty(otherModAuthor))
                WriteOtherModMember(profile, "ModAuthor", otherModAuthor);
            if (!string.IsNullOrEmpty(otherModVersion))
                WriteOtherModMember(profile, "ModVersion", otherModVersion);
            WriteOtherModMember(profile, "ModDescription", otherModDescription ?? string.Empty);
            WriteOtherModMember(profile, "ModAssetsPath", assetFolder);
            WriteOtherModMember(profile, "ModExportPath", context.exportPath);

            CommitExportSettingsChange(settings);
            RefreshExportSettingsPreview();

            if (!InvokeValidator(settings, "ValidateRequiredValues"))
            {
                // 这里才是唯一该问 UMod 的地方：刚写完、马上要构建，问一次不打扰用户。
                var failed = new List<string>();
                if (!InvokeValidator(settings, "ValidateName")) failed.Add("名称");
                if (!InvokeValidator(settings, "ValidateAssetPath")) failed.Add("资产目录");
                if (!InvokeValidator(settings, "ValidateVersion")) failed.Add("版本");

                error = "UMod 校验未通过" +
                        (failed.Count > 0 ? "：" + string.Join("、", failed) : string.Empty) +
                        "。请在「Warudo 工作区」面板里检查这条工作区。";
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------------
        // 构建
        // ---------------------------------------------------------------------

        private void BuildOtherMod()
        {
            lastVerificationReport = string.Empty;
            lastVerificationSummary = string.Empty;

            try
            {
                if (HasPendingBuildState())
                    throw new InvalidOperationException("已有未完成的角色 Mod FastBuild，请等它恢复完成后再构建其他 Mod。");

                OtherModContext context = BuildOtherModContext();
                if (!context.canBuild)
                    throw new InvalidOperationException("构建前校验未通过：\n· " +
                                                        string.Join("\n· ", context.errors));

                if (!ApplyOtherModProfile(context, out string profileError))
                    throw new InvalidOperationException(profileError);

                Directory.CreateDirectory(context.exportPath);

                object result = InvokeOfficialBuild(exportSettingsPath);
                if (result == null)
                    throw new InvalidOperationException("UMod 构建入口返回了 null。");

                bool successful = GetMemberValue(result, "Successful") is bool flag && flag;
                var error = Convert.ToString(GetMemberValue(result, "ErrorMessage"));
                if (!successful)
                    throw new InvalidOperationException("UMod 构建失败：" + (error ?? "(没有错误信息)"));

                lastExpectedComponents = new List<HoFastBuildExpectedComponent>();
                string artifactPath = ResolveArtifactPath(
                    new BuildState { exportSettingsPath = exportSettingsPath }, result);
                lastArtifactPath = artifactPath;

                if (string.IsNullOrEmpty(artifactPath) || !File.Exists(artifactPath))
                    throw new InvalidOperationException("构建报告成功，但找不到产物文件。");

                bool hasScripts = context.scripts.Count > 0;
                lastExpectCompiledScripts = hasScripts;

                HoFastBuildArtifactVerification verification =
                    HoFastBuildArtifactVerifier.Verify(artifactPath, hasScripts);
                lastVerificationReport = verification.report;
                lastVerificationSummary = verification.summary;
                lastVerificationHasProblems = verification.incomplete ||
                                              verification.missingEntries.Count > 0 ||
                                              !string.IsNullOrEmpty(verification.error);
                showVerificationReport = lastVerificationHasProblems;

                // 注意：不能用 entryInventory 判等 —— 那里的字符串形如
                // "assemblymodules.dat 94,281 B"，带长度后缀，直接比名字永远不成立。
                bool hasAssembly = verification.hasAssemblyModule;

                if (hasScripts && !hasAssembly)
                {
                    lastVerificationHasProblems = true;
                    lastVerificationSummary = "产物里没有 assemblymodules.dat —— 脚本没有被 UMod 编进 Mod。";
                }
            }
            catch (Exception exception)
            {
                lastVerificationHasProblems = true;
                lastVerificationSummary = "构建失败：\n" + GetRootMessage(exception);
                Debug.LogException(exception);
            }
            finally
            {
                RefreshExportSettingsPreview();
            }
        }

        /// <summary>从活动工作区的导出目录反推 Warudo 数据目录，再补类型子目录。</summary>
        private string ResolveOtherModExportPath(OtherModKindInfo kindInfo)
        {
            if (!otherModAutoExportPath)
                return otherModExportPath;

            string baseExport = GetActiveWorkspaceExportPath();
            string dataFolder = kindInfo == null ? string.Empty : kindInfo.dataFolder;

            // 类别没定下来时，别让「导出目录为空」把构建挡住 ——
            // 活动工作区自己的导出目录就是最合理的落点。
            if (string.IsNullOrEmpty(dataFolder))
                return string.IsNullOrEmpty(baseExport) ? otherModExportPath : baseExport;

            if (string.IsNullOrEmpty(baseExport))
                return string.Empty;

            baseExport = baseExport.Replace('\\', '/').TrimEnd('/');
            int lastSlash = baseExport.LastIndexOf('/');
            if (lastSlash < 0)
                return string.Empty;

            string leaf = baseExport.Substring(lastSlash + 1);
            string dataRoot = AllWarudoModFolders.Contains(leaf, StringComparer.OrdinalIgnoreCase)
                ? baseExport.Substring(0, lastSlash)
                : baseExport;

            return dataRoot + "/" + dataFolder;
        }

        /// <summary>当前活动工作区的导出目录；没有活动工作区时返回空串。</summary>
        private string GetActiveWorkspaceExportPath()
        {
            foreach (WorkspaceEntry entry in workspaceEntries)
            {
                if (entry.isActive)
                    return entry.modExportPath ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// 目录里没有任何可识别的入口资产时，用活动工作区的**导出目录末段**推断类别
        /// （工作区就是一个类别一个，末段就是类别名）。推断不出来返回 null。
        /// </summary>
        private OtherModKindInfo InferKindFromActiveWorkspace()
        {
            string baseExport = GetActiveWorkspaceExportPath();
            if (string.IsNullOrEmpty(baseExport))
                return null;

            baseExport = baseExport.Replace('\\', '/').TrimEnd('/');
            int lastSlash = baseExport.LastIndexOf('/');
            if (lastSlash < 0)
                return null;
            string leaf = baseExport.Substring(lastSlash + 1);

            foreach (OtherModKindInfo info in OtherModKinds)
            {
                if (!string.IsNullOrEmpty(info.dataFolder) &&
                    string.Equals(info.dataFolder, leaf, StringComparison.OrdinalIgnoreCase))
                    return info;
            }
            return null;
        }

        // ---------------------------------------------------------------------
        // 反射小工具
        // ---------------------------------------------------------------------

        private static void WriteOtherModMember(object target, string name, object value)
        {
            Type type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            Debug.LogWarning("[HoUnityTools] ExportProfile 上没有可写的 " + name + " 字段。");
        }

        /// <summary>带返回值的反射调用（TryInvokeExportSettingsMethod 会丢掉返回值）。</summary>
        private static object InvokeOtherModMethod(object target, string methodName, object[] arguments)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                        return false;
                    ParameterInfo[] parameters = candidate.GetParameters();
                    int expected = arguments == null ? 0 : arguments.Length;
                    return parameters.Length == expected;
                });

            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.Invoke(target, arguments);
        }
    }
}
#endif
