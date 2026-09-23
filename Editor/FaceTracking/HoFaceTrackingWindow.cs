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
        private static readonly string[] Modes = { "实时", "手动", "保持", "中性", "交还" };

        private Vector2 scroll;
        private string search = "";
        private readonly System.Collections.Generic.List<string> localAddresses = new System.Collections.Generic.List<string>();
        private string localIps = "";
        private HoFaceTrackingDebugger rig;
        private double lastRepaint, lastRateTime;
        private long lastPackets;
        private float packetRate;

        // 分区展开状态：默认只展开"必须动手填"的那块（配置），其余一律收起。
        // 排查区虽然收起，但出现新问题时会自动弹开一次（见 DrawDiagnoseSection）。
        private bool configExpanded = true;
        private bool parametersExpanded;
        private bool diagnoseExpanded;
        private bool logExpanded;
        private bool hintExpanded;
        private string lastProblem = "";

        [MenuItem("HoUnityTools/面捕调试")]
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
            double now = EditorApplication.timeSinceStartup;
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
            DrawConfigSection(environment);
            DrawParameterSection();
            DrawDiagnoseSection(environment);
        }

        private void DrawTitle()
        {
            bool connected = HoFaceInputHub.Connected;
            double age = HoFaceInputHub.LastFrameTime > 0 ? IFacialMocapReceiver.Now - HoFaceInputHub.LastFrameTime : double.MaxValue;
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
        // 配置（手机连接 + 角色，合成一块）
        // ══════════════════════════════════════════════════════════════
        /// <summary>
        /// 连接手机和挑角色本来是两块，但它们属于同一件事 —— "把输入接到这个角色上"。
        /// 拆成两块会让首次使用的人在两处来回找，所以合成一块，也是唯一默认展开的分区。
        /// </summary>
        private void DrawConfigSection(HoFaceInputEnvironment environment)
        {
            bool connected = HoFaceInputHub.Connected;
            var session = HoFaceInputHub.Session(rig);
            string summary = connected
                ? (HoFaceInputHub.LastFrameTime == 0 ? "等待响应" : packetRate.ToString("F0") + " 包/秒")
                : "未连接";
            if (session != null) summary += " · 驱动中";

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref configExpanded, "配置", summary, HoConstraintEditorTheme.AccentDriver))
            {
                // 收起也要报错：真问题不能被折叠藏起来。
                DrawProblems(session);
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                // ── 输入环境：有序源列表（**顺序 = 优先级**）────────────────────────────
                // 合并在 Hub 里按"线名"逐个做：某个线名取**第一个还新鲜、且这一帧带来了它**的源。
                // 端口故意各用各的（iFacialMocap 固定 49983、VTS 默认 49984），所以两条能同时连着。
                for (int i = 0; i < environment.sources.Count; i++)
                {
                    var entry = environment.sources[i];
                    if (entry == null) continue;
                    var live = FindSource(i);
                    using (HoConstraintEditorControls.Row())
                    {
                        HoConstraintEditorControls.Label("优先级 " + i, HoConstraintEditorTheme.LabelWidthSm,
                            "越靠前优先级越高：某个线名同时有多个源在发时，取最前面那条。");
                        using (new EditorGUI.DisabledScope(connected))
                        {
                            EditorGUI.BeginChangeCheck();
                            bool enabled = HoConstraintEditorControls.Toggle(
                                HoFaceReceiverFactory.DisplayName(entry.kind), entry.enabled, SourceTooltip(entry));
                            if (EditorGUI.EndChangeCheck()) { entry.enabled = enabled; environment.Persist(); }

                            HoConstraintEditorControls.Gap(6.0f);
                            string edited = EditorGUI.TextField(HoConstraintEditorControls.Next(96.0f), entry.phoneIp, HoConstraintEditorTheme.Field);
                            if (edited != entry.phoneIp) { entry.phoneIp = edited; environment.Persist(); }

                            if (!HoFaceReceiverFactory.FixedPort(entry.kind))
                            {
                                HoConstraintEditorControls.Gap(4.0f);
                                int port = EditorGUI.IntField(HoConstraintEditorControls.Next(56.0f), entry.localPort, HoConstraintEditorTheme.Field);
                                if (port != entry.localPort) { entry.localPort = Mathf.Clamp(port, 1024, 65535); environment.Persist(); }
                            }
                        }

                        HoConstraintEditorControls.Flex();
                        HoConstraintEditorControls.Caption(live == null ? "未启动"
                            : live.Running ? (live.LastFrameTime > 0 ? live.Packets + " 包" : "等响应") : "已停");
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("连接", HoConstraintEditorTheme.LabelWidth, "按上面这份列表把源拉起来。");
                    if (HoConstraintEditorControls.Button(connected ? "断开全部" : "连接", null, !connected, 76.0f))
                    {
                        if (connected) HoFaceInputHub.Disconnect();
                        else HoFaceInputHub.Connect();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("恢复默认源", "回到「VTS 手机在前、iFacialMocap 在后」的默认列表。", !connected, 84.0f))
                    {
                        environment.sources = HoFaceInputEnvironment.Default();
                        environment.Persist();
                    }
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("电脑 IPv4", HoConstraintEditorTheme.LabelWidth, "手机 App 里如果要填 PC 地址，填这里其中一个。");
                    HoConstraintEditorControls.Caption(localIps);
                    HoConstraintEditorControls.Flex();
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("调试组件", HoConstraintEditorTheme.LabelWidth, "挂在角色上的 HoFaceTrackingDebugger。");
                    rig = (HoFaceTrackingDebugger)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(90.0f), rig, typeof(HoFaceTrackingDebugger), true);
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("当前选择", "用当前选中的物体（含父/子级）里的组件。"))
                    {
                        var go = Selection.activeGameObject;
                        if (go != null) rig = go.GetComponentInParent<HoFaceTrackingDebugger>() ?? go.GetComponentInChildren<HoFaceTrackingDebugger>();
                    }

                    using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
                    {
                        if (HoConstraintEditorControls.Button("添加", "给当前选中的物体加一个调试组件。", false, 44.0f) && Selection.activeGameObject != null)
                        {
                            var go = Selection.activeGameObject;
                            rig = go.GetComponent<HoFaceTrackingDebugger>() ?? Undo.AddComponent<HoFaceTrackingDebugger>(go);
                            Selection.activeGameObject = rig.gameObject;
                        }
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    using (new EditorGUI.DisabledScope(!Application.isPlaying || rig == null))
                    {
                        if (HoConstraintEditorControls.Button(session == null ? "开始驱动" : "停止并交还", "播放模式下面捕才真正驱动混合树。停止会把占用的形态键还回去。", session == null, 84.0f))
                        {
                            if (session == null) HoFaceInputHub.Start(rig);
                            else HoFaceInputHub.Stop(rig);
                        }
                    }

                    HoConstraintEditorControls.Gap();
                    if (rig != null && HoConstraintEditorControls.Button("选中", "在层级里选中这个角色。", false, 44.0f))
                    {
                        Selection.activeGameObject = rig.gameObject;
                    }

                    HoConstraintEditorControls.Flex();
                    if (session != null) HoConstraintEditorControls.Caption(session.StateSummary);
                    else if (!Application.isPlaying) HoConstraintEditorControls.Caption("进播放模式后可驱动");
                    else if (rig == null) HoConstraintEditorControls.Caption("先指定组件");
                }
            }

            DrawProblems(session);
        }

        /// <summary>真问题才用框：端口占用、socket 异常、会话报错、绑定缺失。这些和折叠状态无关，永远显示。</summary>
        private void DrawProblems(HoFaceAnimationSession session)
        {
            string error = SourceError();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

            string sessionError = HoFaceInputHub.Error(rig);
            if (!string.IsNullOrEmpty(sessionError)) EditorGUILayout.HelpBox(sessionError, MessageType.Error);

            var compiled = session?.Compiled;
            if (compiled != null && compiled.warnings.Count > 0)
            {
                EditorGUILayout.HelpBox("有 " + compiled.warnings.Count + " 个绑定没找到（例如网格上没这个键）：\n"
                    + string.Join("\n", compiled.warnings.GetRange(0, Mathf.Min(5, compiled.warnings.Count))), MessageType.Warning);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 参数
        // ══════════════════════════════════════════════════════════════
        private void DrawParameterSection()
        {
            // "收到几路"按**输入行**算：某个形态键有对应输入行、且这一帧真的被喂上了值，才算收到。
            int received = 0;
            var live = HoFaceInputHub.Session(rig);
            if (live != null)
                foreach (string name in HoFaceTrackingChannels.Names)
                    if (!float.IsNaN(live.MiddlewareInput(name))) received++;

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref parametersExpanded,
                "参数",
                HoFaceInputHub.Connected ? "已收到 " + received + " / " + HoFaceTrackingChannels.Names.Length : "未连接",
                HoConstraintEditorTheme.AccentOutput))
            {
                return;
            }

            var session = HoFaceInputHub.Session(rig);
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm);
                    search = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80.0f), search, HoConstraintEditorTheme.Field);
                }

                DrawParameterHeader();
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(120.0f, position.height - 300.0f)));
                for (int i = 0; i < HoFaceTrackingChannels.Names.Length; i++)
                {
                    DrawParameterRow(i, session);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawParameterHeader()
        {
            using (HoConstraintEditorControls.Row(true))
            {
                GUI.Label(HoConstraintEditorControls.Next(104.0f), "形态键", HoConstraintEditorTheme.Caption);
                GUI.Label(HoConstraintEditorControls.Next(38.0f), "模式", HoConstraintEditorTheme.Caption);
                GUI.Label(HoConstraintEditorControls.Next(46.0f), "原值", HoConstraintEditorTheme.Caption);
                HoConstraintEditorControls.Flex();
                GUI.Label(HoConstraintEditorControls.Next(54.0f), "面捕输入", HoConstraintEditorTheme.Caption);
            }
        }

        /// <summary>
        /// 一行一个键：名称 / 模式 / 原值 / 横条 / 面捕输入。
        /// 以前每个键占三行（粗体名 + 模式 + Controller 值），52 个键就是一面墙 —— 这里压成一行。
        /// </summary>
        private void DrawParameterRow(int index, HoFaceAnimationSession session)
        {
            string name = HoFaceTrackingChannels.Names[index];
            if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return;

            // 「规范值」= 输入行算出来的值（改名与量纲在配置里做完）；没有对应输入行就是「—」。
            float canonical = session != null ? session.MiddlewareInput(name) : float.NaN;
            bool received = !float.IsNaN(canonical);
            float raw = received ? canonical : 0.0f;
            var channel = FindChannel(name);
            int mode = channel != null ? (int)channel.mode : 0;

            using (HoConstraintEditorControls.Row(true))
            {
                GUI.Label(HoConstraintEditorControls.Next(104.0f), name, received ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim);
                GUI.Label(HoConstraintEditorControls.Next(38.0f), Modes[Mathf.Clamp(mode, 0, Modes.Length - 1)], HoConstraintEditorTheme.LabelDim);
                GUI.Label(HoConstraintEditorControls.Next(46.0f), received ? (raw * 100.0f).ToString("F1") : "—", HoConstraintEditorTheme.Value);

                Rect bar = HoConstraintEditorControls.NextFlexible(40.0f);
                if (received) HoConstraintEditorControls.Meter(bar, raw, 0.0f, 1.0f, HoConstraintEditorTheme.AccentDriver);
                else if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(bar, HoConstraintEditorTheme.WellColor);

                float effective = session != null ? session.Effective[index] : float.NaN;
                GUI.Label(
                    HoConstraintEditorControls.Next(54.0f),
                    float.IsNaN(effective) ? "—" : effective.ToString("F3"),
                    session != null ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim);
            }
        }

        private HoFaceChannel FindChannel(string shape)
        {
            if (rig == null) return null;
            foreach (var channel in rig.channels)
                if (channel != null && channel.shape == shape) return channel;
            return null;
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
            return IFacialMocapReceiver.Now - HoFaceInputHub.ConnectStartedAt >= 3 ? "silent" : "";
        }

        /// <summary>收起状态下也要能看出"现在有没有事"。</summary>
        private string DiagnoseSummary()
        {
            if (!HoFaceInputHub.Connected) return "未连接";
            long rejected = TotalRejected();
            if (rejected > 0 && HoFaceInputHub.LastFrameTime == 0) return "来源不符 ×" + rejected;
            if (HoFaceInputHub.LastFrameTime != 0) return "正常";
            return "等了 " + (IFacialMocapReceiver.Now - HoFaceInputHub.ConnectStartedAt).ToString("F0") + " 秒";
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

            if (IFacialMocapReceiver.Now - HoFaceInputHub.ConnectStartedAt < 3) return;

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
                    double age = source.LastFrameTime > 0 ? IFacialMocapReceiver.Now - source.LastFrameTime : double.MaxValue;
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
            var session = HoFaceInputHub.Session(rig);
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
            var session = HoFaceInputHub.Session(rig);
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
