using System;
using System.Net;
using System.Net.Sockets;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 面捕调试的全局面板。
    ///
    /// 排版规则照着仓库既有的设计令牌走（<see cref="HoConstraintEditorTheme"/> /
    /// <see cref="HoConstraintEditorControls"/>）：分区可折叠、卡片 + 行栅格、**说明文字一律进 tooltip**。
    /// 面板常态只留三类东西 —— 状态胶囊、当前该点的按钮、参数读数。
    /// 「可能的故障原因」这类话只在真的出问题时才出现，而且先给一行结论，清单收在「排查」里。
    /// </summary>
    public sealed class HoFaceTrackingWindow : EditorWindow
    {
        /// <summary>「配置详情」与「参数输入」各自一份筛选/滚动位置 —— 共用一个的话，一个栏里的筛选会顺手把另一栏也滤掉。</summary>
        private Vector2 profileScroll;
        private string profileSearch = "";
        private Vector2 inputScroll;
        private string inputSearch = "";
        private readonly System.Collections.Generic.List<string> localAddresses = new System.Collections.Generic.List<string>();
        private string localIps = "";
        /// <summary>
        /// 设置对象由宿主持有（角色上不挂组件之后它就是**全局状态**），窗口只是它的一个视图。
        /// 所以这里是只读的：窗口不再"选一个组件"，而是编辑宿主那一份设置。
        /// </summary>
        private static HoFaceDebugSettings settings { get { return HoFaceDebugHost.Settings; } }
        private double lastRepaint, lastRateTime;
        private long lastPackets;
        private float packetRate;

        // 分区展开状态：默认只展开"必须动手填"的那块（配置），其余一律收起。
        // 排查区虽然收起，但出现新问题时会自动弹开一次（见 DrawDiagnoseSection）。
        private bool configExpanded = true;
        private bool profileExpanded = true;
        private bool parametersExpanded;
        private bool diagnoseExpanded;
        private bool logExpanded;
        private bool hintExpanded;
        private string lastProblem = "";

        /// <summary>把绝对路径尽量转成工程相对路径（`Assets/...`），这样设置文件里存的是可移植路径。</summary>
        private static string MakeProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;
            return path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        [MenuItem("HoUnityTools/面捕/调试面板", false, 10)]
        public static void ShowWindow() => GetWindow<HoFaceTrackingWindow>("Ho 面捕调试");

        private void OnEnable()
        {
            minSize = new Vector2(420, 380);
            EditorApplication.update += Refresh;
            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (address.AddressFamily == AddressFamily.InterNetwork) localAddresses.Add(address.ToString());
                localIps = localAddresses.Count == 0 ? "—" : string.Join(" / ", localAddresses);
            }
            catch { localIps = "—"; }
            HoFaceFirewall.Refresh();
        }

        private void OnDisable() => EditorApplication.update -= Refresh;

        private void Refresh()
        {
            // UAC 弹窗是异步的：每帧问一次提权进程结束没有，结束了才更新状态。
            HoFaceFirewall.Poll();
            double now = HoFaceClock.Now;
            if (now - lastRateTime >= 1)
            {
                long packets = TotalPackets();
                packetRate = Mathf.Max(0, (float)((packets - lastPackets) / (now - lastRateTime)));
                lastPackets = packets;
                lastRateTime = now;
            }

            if (now - lastRepaint < 0.1) return;
            lastRepaint = now;
            Repaint();
        }

        private static long TotalPackets()
        {
            long total = 0;
            foreach (var source in HoFaceInputHub.Sources) total += source.Packets;
            return total;
        }

        /// <summary>第 i 条源对应的活体接收端（还没连上时返回 null）。</summary>
        private static IHoFaceInputReceiver FindSource(int index) =>
            index >= 0 && index < HoFaceInputHub.Sources.Count ? HoFaceInputHub.Sources[index] : null;

        private static string SourceTooltip(HoFaceSourceEntry entry) =>
            HoFaceReceiverFactory.Hint(entry.kind) + "\n"
            + "勾掉 = 这条不拉起（顺序不变）。";

        // ══════════════════════════════════════════════════════════════
        // 主入口
        // ══════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            var environment = HoFaceInputEnvironment.instance;
            DrawTitle();
            // 四栏，竖排（这套布局是单列分节；要真并排得先给 HoConstraintEditorControls 加列支持）。
            DrawObjectSection(environment);      // 一、对象：调试对象 / 混合树控制器 / 配置文件对象 / 连接
            DrawProfileSection();                // 二、配置详情：这份 profile 吃啥、怎么处理、输出啥
            DrawInputSection();                  // 三、参数输入：VTS 传过来的**全部裸参数**（纯调试）
            DrawDiagnoseSection(environment);    // 四、排查：权限、端口、连接、包统计、问题
        }

        private void DrawTitle()
        {
            bool connected = HoFaceInputHub.Connected;
            double age = HoFaceInputHub.LastFrameTime > 0 ? HoFaceClock.Now - HoFaceInputHub.LastFrameTime : double.MaxValue;
            string state = !connected ? "已停止"
                : HoFaceInputHub.LastFrameTime == 0 ? "等待响应"
                : age > 1 ? "已断流" : "接收中";
            bool healthy = connected && HoFaceInputHub.LastFrameTime != 0 && age <= 1;

            string right = HoFaceInputHub.SourceCount + " 条源"
                + (connected ? " · " + packetRate.ToString("F0") + " 包/秒" : "");
            // 只有在"连上了但一个包都没有"时多给一个胶囊 —— 这是真会挡路的当前状态，不是背景说明。
            bool blocked = connected && HoFaceInputHub.LastFrameTime == 0 && HoFaceFirewall.Supported && !HoFaceFirewall.Exists;
            if (blocked) HoConstraintEditorControls.Title("Ho 面捕调试", right, (state, healthy), ("防火墙未放行", false));
            else HoConstraintEditorControls.Title("Ho 面捕调试", right, (state, healthy));
        }

        // ══════════════════════════════════════════════════════════════
        // 一、对象栏：三样东西，全部由面板持有（角色上不挂任何组件）
        // ══════════════════════════════════════════════════════════════
        /// <summary>
        /// 三样：**调试对象**（场景里的角色实例）、**面捕混合树控制器**、**配置文件对象**。
        /// 最后一样是**必须的** —— 没填它，下面三栏全部锁住不让改（配置是这套东西的心脏，
        /// 空着往下调只会得到一堆看不懂的数字）。
        /// </summary>
        private void DrawObjectSection(HoFaceInputEnvironment environment)
        {
            bool connected = HoFaceInputHub.Connected;
            var session = HoFaceInputHub.Session(settings);
            string summary = !settings.HasProfile ? "缺配置文件对象"
                : settings.FaceController() == null ? "缺控制器"
                : settings.Character() == null ? "缺调试对象"
                : session != null ? "驱动中" : "就绪";
            if (session != null && connected) summary += " · 接收中";

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref configExpanded, "对象", summary, HoConstraintEditorTheme.AccentDriver))
            {
                // 收起也要报错：真问题不能被折叠藏起来。
                DrawProblems(session);
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                // ── 输入源：有序列表（**顺序 = 优先级**）──────────────────────────────
                // 合并在 Hub 里按"线名"逐个做：某个线名取**第一个还新鲜、且这一帧带来了它**的源。
                // 现在只有 VTS 一条，留着列表是为了以后加设备时不动结构。
                for (int i = 0; i < environment.sources.Count; i++)
                {
                    var entry = environment.sources[i];
                    if (entry == null) continue;
                    var live = FindSource(i);
                    using (HoConstraintEditorControls.Row())
                    {
                        // 现在只有一种协议、一条源，所以不写"优先级 N"了 —— 那是多设备才有的概念。
                        HoConstraintEditorControls.Label("手机 IP", HoConstraintEditorTheme.LabelWidth, SourceTooltip(entry));
                        using (new EditorGUI.DisabledScope(connected))
                        {
                            HoConstraintEditorControls.Gap(6.0f);
                            string edited = EditorGUI.TextField(HoConstraintEditorControls.Next(96.0f), entry.phoneIp, HoConstraintEditorTheme.Field);
                            if (edited != entry.phoneIp) { entry.phoneIp = edited; environment.Persist(); }

                            HoConstraintEditorControls.Gap(6.0f);
                            int port = EditorGUI.IntField(HoConstraintEditorControls.Next(56.0f), entry.localPort, HoConstraintEditorTheme.Field);
                            if (port != entry.localPort) { entry.localPort = Mathf.Clamp(port, 1024, 65535); environment.Persist(); }

                            HoConstraintEditorControls.Gap(6.0f);
                            EditorGUI.BeginChangeCheck();
                            bool enabled = HoConstraintEditorControls.Toggle("启用", entry.enabled,
                                "关掉就不启动这一路接收端（比断开更彻底）。");
                            if (EditorGUI.EndChangeCheck()) { entry.enabled = enabled; environment.Persist(); }
                        }

                        HoConstraintEditorControls.Flex();
                        HoConstraintEditorControls.Caption(live == null ? "未启动"
                            : live.Running ? (live.LastFrameTime > 0 ? live.Packets + " 包" : "等响应") : "已停");
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("混合树控制器", HoConstraintEditorTheme.LabelWidth,
                        "会话真正跑的那份控制器。在「控制器编辑」页里原地装配；你也可以自己改完指到这里。");
                    var current = settings.FaceController();
                    var picked = (RuntimeAnimatorController)EditorGUI.ObjectField(
                        HoConstraintEditorControls.NextFlexible(90.0f), current, typeof(RuntimeAnimatorController), false);
                    if (picked != current)
                    {
                        settings.SetFaceController(picked, picked != null ? AssetDatabase.GetAssetPath(picked) : "");
                        HoFaceDebugHost.Save();
                    }
                    if (current == null) HoConstraintEditorControls.Caption("未指定 —— 会话跑不起来");
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("配置文件对象", HoConstraintEditorTheme.LabelWidth,
                        "**必须**。中间层配置（*.hoface.json）—— Unity 侧与 Warudo 侧读的是同一个文件。");
                    string editedPath = EditorGUI.TextField(
                        HoConstraintEditorControls.NextFlexible(70.0f), settings.profilePath, HoConstraintEditorTheme.Field);
                    if (editedPath != settings.profilePath)
                    {
                        settings.profilePath = editedPath;
                        settings.ReloadProfile();
                        HoFaceDebugHost.Save();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("选…", "选一个现有的 .hoface.json。", false, 34.0f))
                    {
                        string path = EditorUtility.OpenFilePanel("选中间层配置", Application.dataPath, "json");
                        if (!string.IsNullOrEmpty(path))
                        {
                            settings.profilePath = MakeProjectRelative(path);
                            settings.ReloadProfile();
                            HoFaceDebugHost.Save();
                        }
                    }

                    if (HoConstraintEditorControls.Button("新建", "在工程里写一份内置默认配置。", false, 40.0f))
                    {
                        string path = EditorUtility.SaveFilePanelInProject(
                            "新建中间层配置", "ho-2d.hoface.json", "json", "写一份内置默认");
                        if (!string.IsNullOrEmpty(path))
                        {
                            System.IO.File.WriteAllText(path, HoFaceProfile.WriteDefaults());
                            AssetDatabase.Refresh();
                            settings.profilePath = path;
                            settings.ReloadProfile();
                            HoFaceDebugHost.Save();
                        }
                    }

                    if (!settings.HasProfile) HoConstraintEditorControls.Caption("必填；空着下面三栏都锁住");
                    else if (settings.Middleware == null) HoConstraintEditorControls.Caption("读不出来：" + settings.ProfileError);
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("电脑 IPv4", HoConstraintEditorTheme.LabelWidth, "手机 App 里如果要填 PC 地址，填这里其中一个。");
                    HoConstraintEditorControls.Caption(localIps);
                    HoConstraintEditorControls.Flex();
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                // ── 连接：**归第一栏**（"连哪台手机"是对象的事；参数栏纯粹用来看值）──────
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("连接", HoConstraintEditorTheme.LabelWidth, "按上面那份源列表把接收端拉起来。");
                    if (HoConstraintEditorControls.Button(connected ? "断开" : "连接", null, !connected, 60.0f))
                    {
                        if (connected) HoFaceInputHub.Disconnect();
                        else HoFaceInputHub.Connect();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("恢复默认源", "回到默认的 VTS 手机一条。", !connected, 84.0f))
                    {
                        environment.sources = HoFaceInputEnvironment.Default();
                        environment.Persist();
                    }

                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(connected
                        ? HoFaceInputHub.SourceCount + " 条源 · " + packetRate.ToString("F0") + " 包/秒"
                        : "未连接");
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("调试对象", HoConstraintEditorTheme.LabelWidth,
                        "场景里的角色实例。**角色上不需要挂任何组件** —— 拿它只是为了读骨架与网格。");
                    var character = settings.Character();
                    var picked = (GameObject)EditorGUI.ObjectField(
                        HoConstraintEditorControls.NextFlexible(90.0f), character, typeof(GameObject), true);
                    if (picked != character) { settings.SetCharacter(picked); HoFaceDebugHost.Save(); }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("当前选择", "用当前选中的物体当调试对象。"))
                    {
                        if (Selection.activeGameObject != null) { settings.SetCharacter(Selection.activeGameObject); HoFaceDebugHost.Save(); }
                    }

                    if (character == null && !string.IsNullOrEmpty(settings.characterPath))
                        HoConstraintEditorControls.Caption("按路径找不到：" + settings.characterPath);
                }

                using (HoConstraintEditorControls.Row())
                {
                    using (new EditorGUI.DisabledScope(!Application.isPlaying || settings == null))
                    {
                        if (HoConstraintEditorControls.Button(session == null ? "开始驱动" : "停止并交还", "播放模式下面捕才真正驱动混合树。停止会把占用的形态键还回去。", session == null, 84.0f))
                        {
                            if (session == null) HoFaceInputHub.Start(settings);
                            else HoFaceInputHub.Stop(settings);
                        }
                    }

                    HoConstraintEditorControls.Gap();
                    var character = settings.Character();
                    if (character != null && HoConstraintEditorControls.Button("选中", "在层级里选中这个角色。", false, 44.0f))
                    {
                        Selection.activeGameObject = character;
                    }

                    HoConstraintEditorControls.Flex();
                    if (session != null) HoConstraintEditorControls.Caption(session.StateSummary);
                    else if (!Application.isPlaying) HoConstraintEditorControls.Caption("进播放模式后可驱动");
                    else if (settings == null) HoConstraintEditorControls.Caption("先指定组件");
                }
            }

            DrawProblems(session);
        }

        /// <summary>真问题才用框：端口占用、socket 异常、会话报错、绑定缺失。这些和折叠状态无关，永远显示。</summary>
        private void DrawProblems(HoFaceAnimationSession session)
        {
            string error = SourceError();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

            string sessionError = HoFaceInputHub.Error(settings);
            if (!string.IsNullOrEmpty(sessionError)) EditorGUILayout.HelpBox(sessionError, MessageType.Error);

            var compiled = session?.Compiled;
            if (compiled != null && compiled.warnings.Count > 0)
            {
                EditorGUILayout.HelpBox("有 " + compiled.warnings.Count + " 个绑定没找到（例如网格上没这个键）：\n"
                    + string.Join("\n", compiled.warnings.GetRange(0, Mathf.Min(5, compiled.warnings.Count))), MessageType.Warning);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 二、配置详情栏：这份 profile 吃啥、怎么处理、输出啥
        // ══════════════════════════════════════════════════════════════
        /// <summary>
        /// 把配置文件逐行摊开：`名字 = 曲线(表达式) + 有序修饰符` —— 也就是 VBridger 的那一层。
        /// **只读**：改配置去改那个 `.json`（面板不画曲线编辑器，那是文件自己的事）。
        /// </summary>
        private void DrawProfileSection()
        {
            var middleware = settings.Middleware;
            string summary = !settings.HasProfile ? "先把配置文件对象填上"
                : middleware == null ? "读不出来"
                : "输入行 " + middleware.inputs.Count + " · 输出行 " + middleware.outputs.Count;

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref profileExpanded, "配置详情", summary, HoConstraintEditorTheme.AccentOutput))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                if (!settings.HasProfile)
                {
                    HoConstraintEditorControls.Caption("对象栏里的「配置文件对象」是必填的；填好之后这里会列出它的全部行。");
                    return;
                }

                if (middleware == null)
                {
                    EditorGUILayout.HelpBox(settings.ProfileError ?? "配置读不出来。", MessageType.Error);
                    return;
                }

                if (!string.IsNullOrEmpty(settings.ProfileError))
                    EditorGUILayout.HelpBox(settings.ProfileError, MessageType.Warning);

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm);
                    profileSearch = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80.0f), profileSearch, HoConstraintEditorTheme.Field);
                }

                profileScroll = EditorGUILayout.BeginScrollView(profileScroll, GUILayout.Height(Mathf.Max(160.0f, position.height - 420.0f)));

                HoConstraintEditorControls.Caption("输入行 —— 手机线名 → 规范名（改名与量纲在这一层）");
                DrawProfileHeader("规范名", "曲线", "修饰符");
                foreach (var row in middleware.inputs) DrawProfileRow(row);

                HoConstraintEditorControls.Separator(4.0f, 4.0f);

                HoConstraintEditorControls.Caption("输出行 —— 规范名 → 输出（控制器参数名 / 保留名）");
                DrawProfileHeader("输出", "曲线", "修饰符");
                foreach (var row in middleware.outputs) DrawProfileRow(row);

                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawProfileHeader(string name, string curve, string modifiers)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                GUI.Label(HoConstraintEditorControls.Next(130.0f), name, HoConstraintEditorTheme.Caption);
                HoConstraintEditorControls.Flex();
                GUI.Label(HoConstraintEditorControls.Next(56.0f), curve, HoConstraintEditorTheme.Caption);
                GUI.Label(HoConstraintEditorControls.Next(84.0f), modifiers, HoConstraintEditorTheme.Caption);
            }
        }

        private void DrawProfileRow(HoFaceOutput row)
        {
            if (row == null) return;
            string name = row.parameter ?? "";
            string expression = row.expression ?? "";
            if (!string.IsNullOrEmpty(profileSearch)
                && name.IndexOf(profileSearch, StringComparison.OrdinalIgnoreCase) < 0
                && expression.IndexOf(profileSearch, StringComparison.OrdinalIgnoreCase) < 0) return;

            using (HoConstraintEditorControls.Row(true))
            {
                GUI.Label(HoConstraintEditorControls.Next(130.0f), name, HoConstraintEditorTheme.Value);
                GUI.Label(HoConstraintEditorControls.NextFlexible(120.0f), expression, EditorStyles.label);

                string curve = row.curve == null || row.curve.length == 0 ? "—" : row.curve.length + " 点";
                GUI.Label(HoConstraintEditorControls.Next(56.0f), curve, HoConstraintEditorTheme.LabelDim);
                GUI.Label(HoConstraintEditorControls.Next(84.0f), ModifierText(row), HoConstraintEditorTheme.LabelDim);
            }
        }

        private static string ModifierText(HoFaceOutput row)
        {
            if (row.modifiers == null || row.modifiers.Count == 0) return "—";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var modifier in row.modifiers)
            {
                if (modifier == null) continue;
                switch (modifier.kind)
                {
                    case HoFaceModifierKind.Smooth: parts.Add("平滑 " + modifier.seconds.ToString("0.##") + "s"); break;
                    case HoFaceModifierKind.Delay: parts.Add("延迟 " + modifier.seconds.ToString("0.##") + "s"); break;
                    case HoFaceModifierKind.Steps: parts.Add("分档 " + (modifier.steps != null ? modifier.steps.Count : 0) + " 档"); break;
                }
            }
            return parts.Count == 0 ? "—" : string.Join(" → ", parts.ToArray());
        }

        // ══════════════════════════════════════════════════════════════
        // 三、参数输入栏：VTS 传过来的**全部裸参数**
        // ══════════════════════════════════════════════════════════════
        /// <summary>
        /// **纯调试**：只看 VTS 传过来的裸值。名字就是手机发来的样子，不做规范名、不做量纲、
        /// 不画曲线、没有模式与覆盖。连接那些操作在**对象栏**（那是"连哪台手机"的事）。
        /// </summary>
        private void DrawInputSection()
        {
            int count = HoFaceInputHub.MergedValues.Count;
            string summary = HoFaceInputHub.Connected
                ? (count > 0 ? count + " 个线名" : "等待响应")
                : "未连接";

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref parametersExpanded, "参数输入", summary, HoConstraintEditorTheme.AccentDriver))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm);
                    inputSearch = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80.0f), inputSearch, HoConstraintEditorTheme.Field);
                }

                if (count == 0)
                {
                    HoConstraintEditorControls.Caption(HoFaceInputHub.Connected
                        ? "还没收到包 —— 看第四栏「排查」。"
                        : "没连上。手机那边打开「3rd Party PC Clients」，在第一栏填手机 IP 再点连接。");
                    return;
                }

                inputScroll = EditorGUILayout.BeginScrollView(inputScroll, GUILayout.Height(Mathf.Max(140.0f, position.height - 420.0f)));
                using (HoConstraintEditorControls.Row(true))
                {
                    GUI.Label(HoConstraintEditorControls.Next(140.0f), "线名（手机原样）", HoConstraintEditorTheme.Caption);
                    HoConstraintEditorControls.Flex();
                    GUI.Label(HoConstraintEditorControls.Next(64.0f), "值", HoConstraintEditorTheme.Caption);
                    GUI.Label(HoConstraintEditorControls.Next(56.0f), "距上帧", HoConstraintEditorTheme.Caption);
                }

                var names = new System.Collections.Generic.List<string>(HoFaceInputHub.MergedValues.Keys);
                names.Sort(StringComparer.Ordinal);
                double now = HoFaceClock.Now;
                foreach (string wire in names)
                {
                    if (!string.IsNullOrEmpty(inputSearch) && wire.IndexOf(inputSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    float value = HoFaceInputHub.MergedValues[wire];
                    double at = HoFaceInputHub.ReceivedAt(wire);
                    double age = at > 0 ? now - at : double.MaxValue;
                    bool fresh = age <= Mathf.Max(0.1f, settings.staleSeconds);

                    using (HoConstraintEditorControls.Row(true))
                    {
                        GUI.Label(HoConstraintEditorControls.Next(140.0f), wire,
                            fresh ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim);

                        Rect bar = HoConstraintEditorControls.NextFlexible(40.0f);
                        // 0..1 的画条（形态键就是这个量纲）；头姿、毫秒时间戳这些超范围的只显示数字。
                        if (value >= 0f && value <= 1f)
                            HoConstraintEditorControls.Meter(bar, value, 0f, 1f, HoConstraintEditorTheme.AccentDriver);
                        else if (Event.current.type == EventType.Repaint)
                            EditorGUI.DrawRect(bar, HoConstraintEditorTheme.WellColor);

                        GUI.Label(HoConstraintEditorControls.Next(64.0f), value.ToString("F4"),
                            fresh ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim);
                        GUI.Label(HoConstraintEditorControls.Next(56.0f),
                            at <= 0 ? "—" : age.ToString("F1") + "s", HoConstraintEditorTheme.LabelDim);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 排查（默认收起）
        // ══════════════════════════════════════════════════════════════
        private void DrawDiagnoseSection(HoFaceInputEnvironment environment)
        {
            // 平时收起；一旦出现**新的**问题就自己弹开一次，之后不再和用户较劲（用户收起就是收起）。
            string problem = ProblemKey();
            if (problem != lastProblem)
            {
                lastProblem = problem;
                if (!string.IsNullOrEmpty(problem)) diagnoseExpanded = true;
            }

            string summary = DiagnoseSummary();
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref diagnoseExpanded, "排查", summary, HoConstraintEditorTheme.AccentDebug)) return;

            using (HoConstraintEditorControls.Card())
            {
                DrawFirewallRow();
                DrawWaitingHint();
                DrawPacketStats();
                DrawDebugDetails();
            }
        }

        /// <summary>当前有没有"值得一提的问题"。空串 = 没事，排查区就安静待着。</summary>
        private string ProblemKey()
        {
            string error = SourceError();
            if (!string.IsNullOrEmpty(error)) return "error";
            if (!HoFaceInputHub.Connected) return "";
            if (HoFaceInputHub.LastFrameTime != 0) return "";
            long rejected = TotalRejected();
            if (rejected > 0 && !string.IsNullOrEmpty(RejectedFrom())) return "rejected:" + RejectedFrom();
            return HoFaceClock.Now - HoFaceInputHub.ConnectStartedAt >= 3 ? "silent" : "";
        }

        /// <summary>收起状态下也要能看出"现在有没有事"。</summary>
        private string DiagnoseSummary()
        {
            if (!HoFaceInputHub.Connected) return "未连接";
            long rejected = TotalRejected();
            if (rejected > 0 && HoFaceInputHub.LastFrameTime == 0) return "来源不符 ×" + rejected;
            if (HoFaceInputHub.LastFrameTime != 0) return "正常";
            return "等了 " + (HoFaceClock.Now - HoFaceInputHub.ConnectStartedAt).ToString("F0") + " 秒";
        }

        private void DrawFirewallRow()
        {
            if (!HoFaceFirewall.Supported) return;
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("防火墙", HoConstraintEditorTheme.LabelWidthSm, "手机的 UDP 回包是入站流量，Windows 防火墙按程序放行；Unity.exe 没有入站许可就永远收不到。");
                HoConstraintEditorControls.Pill(HoFaceFirewall.Exists ? "已放行" : "未放行", HoFaceFirewall.Exists);
                HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(HoFaceFirewall.Busy))
                {
                    if (HoConstraintEditorControls.Button("授予权限", "改防火墙需要管理员，会弹一次 UAC。放行这个 Unity.exe 的入站 UDP（源端口见上面各条）。"))
                    {
                        HoFaceFirewall.Grant();
                    }

                    using (new EditorGUI.DisabledScope(!HoFaceFirewall.Exists))
                    {
                        if (HoConstraintEditorControls.Button("撤销", null, false, 44.0f)) HoFaceFirewall.Revoke();
                    }
                }

                HoConstraintEditorControls.Flex();
            }

            // 状态用一行小字，不用 HelpBox —— 这是"刚才那次操作的结果"，不是常驻说明。
            if (!string.IsNullOrEmpty(HoFaceFirewall.Status))
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(HoFaceFirewall.Status);
                }
            }

            if (!string.IsNullOrEmpty(HoFaceFirewall.LastLog))
            {
                logExpanded = HoConstraintEditorControls.InlineFoldout(logExpanded, "提权脚本日志", "失败原因一般就在这里。可直接选中复制。");
                if (logExpanded) EditorGUILayout.TextArea(HoFaceFirewall.LastLog, GUILayout.MinHeight(90.0f));
            }
        }

        /// <summary>
        /// 收不到包时给**一行结论 + 一个按钮**，把清单收进折叠里。
        /// 以前这里是一整段四条排查说明常驻显示，正常用的时候太吵。
        /// </summary>
        private void DrawWaitingHint()
        {
            if (!HoFaceInputHub.Connected || HoFaceInputHub.LastFrameTime != 0) return;

            long rejected = TotalRejected();
            string rejectedFrom = RejectedFrom();
            if (rejected > 0 && !string.IsNullOrEmpty(rejectedFrom))
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Caption("收到 " + rejected + " 个包，来源是 " + rejectedFrom + "（与填写的不一致）");
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("改成这个地址", "填错手机 IP 时最直接的线索。", true))
                    {
                        var environment = HoFaceInputEnvironment.instance;
                        foreach (var entry in environment.sources)
                            if (entry != null && entry.enabled) entry.phoneIp = rejectedFrom;   // 全部启用中的源一起改（通常就是同一台手机）
                        environment.Persist();
                    }
                }

                return;
            }

            if (HoFaceClock.Now - HoFaceInputHub.ConnectStartedAt < 3) return;

            using (HoConstraintEditorControls.Row())
            {
                if (HoFaceFirewall.Supported && !HoFaceFirewall.Exists)
                {
                    HoConstraintEditorControls.Caption("3 秒没收到包，最可能是防火墙没放行");
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("授予权限", "改防火墙需要管理员，会弹一次 UAC。", true)) HoFaceFirewall.Grant();
                }
                else
                {
                    hintExpanded = HoConstraintEditorControls.InlineFoldout(hintExpanded, "3 秒没收到包");
                }
            }

            if (!hintExpanded || !HoFaceFirewall.Exists) return;
            string subnet = SameSubnetMatch(FirstPhoneIp());
            using (HoConstraintEditorControls.Indent())
            {
                HoConstraintEditorControls.Caption("· 手机与电脑同网段：手机 " + FirstPhoneIp() + "／本机 " + (subnet ?? localIps));
                foreach (var entry in HoFaceInputEnvironment.instance.sources)
                {
                    if (entry == null || !entry.enabled) continue;
                    HoConstraintEditorControls.Caption("· " + HoFaceReceiverFactory.DisplayName(entry.kind) + "："
                        + HoFaceReceiverFactory.Hint(entry.kind));
                }

                HoConstraintEditorControls.Caption("· 确认 Wi-Fi 没被蜂窝或 VPN 分流");
            }
        }

        /// <summary>环境里第一条启用源的手机地址（面板上"手机与电脑同网段"那行用它）。</summary>
        private static string FirstPhoneIp()
        {
            foreach (var entry in HoFaceInputEnvironment.instance.sources)
                if (entry != null && entry.enabled) return entry.phoneIp;
            return "—";
        }

        /// <summary>任一源报的错（没有就退回连接期的错误）。</summary>
        private static string SourceError()
        {
            foreach (var source in HoFaceInputHub.Sources)
                if (!string.IsNullOrEmpty(source.Error)) return source.Error;
            return HoFaceInputHub.ConnectionError;
        }

        private static long TotalRejected()
        {
            long total = 0;
            foreach (var source in HoFaceInputHub.Sources) total += source.Rejected;
            return total;
        }

        /// <summary>最近一个来源不符的地址（填错手机 IP 时最直接的线索）。</summary>
        private static string RejectedFrom()
        {
            foreach (var source in HoFaceInputHub.Sources)
                if (source.Rejected > 0 && !string.IsNullOrEmpty(source.RejectedSource)) return source.RejectedSource;
            return "";
        }

        private void DrawPacketStats()
        {
            using (HoConstraintEditorControls.Row(true))
            {
                long packets = 0, invalid = 0, rejected = 0, replaced = 0, requests = 0;
                foreach (var source in HoFaceInputHub.Sources)
                {
                    packets += source.Packets; invalid += source.Invalid;
                    rejected += source.Rejected; replaced += source.Replaced; requests += source.Requests;
                }

                HoConstraintEditorControls.Caption("包 " + packets + "　无效 " + invalid + "　其他来源 " + rejected
                    + "　丢旧帧 " + replaced + (requests > 0 ? "　请求 " + requests : ""));
            }

            foreach (var source in HoFaceInputHub.Sources)
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label(source.DisplayName, HoConstraintEditorTheme.LabelWidthSm, source.Hint);
                    double age = source.LastFrameTime > 0 ? HoFaceClock.Now - source.LastFrameTime : double.MaxValue;
                    HoConstraintEditorControls.Caption(age > 1
                        ? (source.LastFrameTime > 0 ? "断流 " + age.ToString("F1") + " 秒" : "还没收到包")
                        : "接收中");
                    HoConstraintEditorControls.Flex();
                    if (source.LastFrameTime > 0) HoConstraintEditorControls.Caption("来源 " + source.Sender);
                }
            }

            var merged = HoFaceInputHub.MergedValues;
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Caption("合并后有 " + merged.Count + " 个线名"
                    + (merged.ContainsKey("FaceFound") ? "　脸在 " + (merged["FaceFound"] > 0.5f ? "是" : "否") : "")
                    + (merged.ContainsKey("Hotkey") && merged["Hotkey"] > 0 ? "　热键 " + merged["Hotkey"].ToString("F0") : ""));
            }

            // 姿态显示用**输入行算出来的规范值**（面板与表达式看到的是同一个数）。
            var session = HoFaceInputHub.Session(settings);
            if (session == null) return;
            DrawPose(session, "头姿", "headRotX", "headRotY", "headRotZ");
            DrawPose(session, "头位", "headPosX", "headPosY", "headPosZ");
            DrawPose(session, "左眼", "eyeLeftX", "eyeLeftY", "eyeLeftZ");
            DrawPose(session, "右眼", "eyeRightX", "eyeRightY", "eyeRightZ");
        }

        /// <summary>
        /// 姿态监视：**输入行算出来的规范值**（单位由你在配置里怎么换算决定 —— 我们不做隐式换算）。
        /// 没有对应输入行时显示「—」：那说明这份配置没把这个名字从线名映射过来。
        /// </summary>
        private void DrawPose(HoFaceAnimationSession session, string label, params string[] names)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label(label, HoConstraintEditorTheme.LabelWidthSm,
                    "输入行算出的规范值（`" + names[0] + "` 等）。量纲由配置里的输入行决定。");
                string text = "";
                foreach (string name in names)
                {
                    float value = session.MiddlewareInput(name);
                    text += name.Substring(name.Length - 1) + " " + (float.IsNaN(value) ? "—" : value.ToString("F2")) + "  ";
                }

                HoConstraintEditorControls.Caption(text.TrimEnd());
            }
        }

        /// <summary>手机长什么样、原值/路由值分别是什么 —— 平时不看，放折叠里。</summary>
        private void DrawDebugDetails()
        {
            var session = HoFaceInputHub.Session(settings);
            if (session?.Compiled == null) return;
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Caption("输出绑定 " + session.Compiled.bindings.Count + " 个");
            }
        }

        /// <summary>手机 IP 是否和某个本机地址同 /24；返回那个本机地址，否则 null。</summary>
        private string SameSubnetMatch(string phoneIp)
        {
            if (!IPAddress.TryParse(phoneIp, out var phone) || phone.AddressFamily != AddressFamily.InterNetwork) return null;
            byte[] target = phone.GetAddressBytes();
            foreach (string candidate in localAddresses)
            {
                if (!IPAddress.TryParse(candidate, out var local)) continue;
                byte[] bytes = local.GetAddressBytes();
                if (bytes[0] == target[0] && bytes[1] == target[1] && bytes[2] == target[2]) return candidate;
            }

            return null;
        }
    }
}
