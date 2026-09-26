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
        /// <summary>「参数输出」那一栏：**默认展开** —— 它是"中间层到底算没算出值"的唯一观察面。</summary>
        private bool outputExpanded = true;
        private Vector2 outputScroll;
        private string outputSearch = "";
        private bool diagnoseExpanded;
        private bool logExpanded;
        private bool hintExpanded;
        private string lastProblem = "";

        /// <summary>
        /// **动态参数 Hub**：在调试对象的子层级里**自动找**一个 <see cref="HoFaceSemanticHub"/>。
        ///
        /// ⚠️ 2026-09-27 起 Hub 是**唯一的组件**：以前旁边还挂一个 `HoFaceSemanticConnector`（"把手"），
        /// 槽表删掉之后它就只剩"再指一次 Hub"，所以整个删了；按名字读/写现在直接长在 Hub 上
        /// （`GetFloat(名字)` / `SetFloat(名字, 值)`）。这也与 Warudo 侧一致：那边写动态参数的节点
        /// 同样是在角色层级里找 Hub。
        ///
        /// ⚠️ **两边都不会自动建**：动态参数是**角色资产的一部分**（挂在哪个物体、写哪些名字，
        /// 都是作者的编排），顺手在场景里塞一个新物体，既不进预制体也不落盘，只会让人困惑。
        ///
        /// **拖动调试对象就够，这一栏不用手填** —— 每帧从那个对象推出来，所以它是"自动寻找"，
        /// 不是一个要维护的第二个引用。
        /// </summary>
        private void DrawSwitchesRow()
        {
            GameObject character = settings.Character();
            HoFaceSemanticHub hub = FindSemanticHub(character);

            // 倒数第二行 = **只有布尔开关 + 一个灰的只读预览**：布尔最左、灰色预览靠右，
            // 文字只留最短的（说明全进 tooltip）—— 2026-09-27 用户定，这一栏之前太挤。
            // 顺序也是用户定的：预览混合树 / 自动驱动 / 写动态参数。
            using (HoConstraintEditorControls.Row(true))
            {
                EditorGUI.BeginChangeCheck();
                bool preview = HoConstraintEditorControls.Toggle("预览混合树", settings.showShadowInHierarchy,
                    "把运行中的影子台 `Ho Face Shadow` 显示到 Hierarchy：选中它 + Animator 窗口 = 看**正在跑的那棵树**"
                    + "（不落盘，停止驱动就没了）。");

                HoConstraintEditorControls.Gap(8.0f);

                // 这一行就是 `startOnPlay` 的开关（**默认开**）。它只做"开始驱动"这一件事，
                // **不会**顺手连手机 —— 连接是显式动作，自动连会在你还没看 IP 是否对的时候先把端口占了。
                bool autoStart = HoConstraintEditorControls.Toggle("自动驱动", settings.startOnPlay,
                    "进播放模式后自动按下「开始驱动」（**不会**自动连手机）。");

                HoConstraintEditorControls.Gap(8.0f);

                bool write = HoConstraintEditorControls.Toggle("写动态参数", settings.writeParameterHub,
                    "把**全部输出行**按名字写进角色 `HoFaceSemanticHub`（Warudo 侧由「HoFace写动态参数」节点写）。");

                if (EditorGUI.EndChangeCheck())
                {
                    settings.showShadowInHierarchy = preview;
                    settings.startOnPlay = autoStart;
                    settings.writeParameterHub = write;
                    HoFaceDebugHost.Save();
                    HoFaceInputHub.Session(settings)?.ApplyShadowVisibility();   // 已经在跑的话立刻生效
                }

                HoConstraintEditorControls.Flex();

                // **灰的引用行**：找到的东西只读地摆出来，让人核对"认的是不是这一个"。
                // 用 DisabledScope 而不是 Label：它长得就是那一栏本来的样子（可拖可点选），
                // 只是不许改 —— 改它没有意义，Hub 是从角色推出来的。
                if (hub != null)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(90.0f), hub,
                            typeof(HoFaceSemanticHub), true);
                    }
                }
                // **黄字**：只有"打开写 Hub 却没有落点"才是问题；关着时它只是可选件没挂，
                // 那就什么都不说 —— 这一栏不留"用户提示小字"（2026-09-27 用户定）。
                else if (settings.writeParameterHub)
                {
                    Warning(character == null
                        ? "先填「调试对象」"
                        : "写 Hub 开着，但这个对象上没有 HoFaceSemanticHub ⇒ 值没地方落");
                }
            }

            // 播放时的**实时读数**：开关打开后中间层算完就写进角色那片 Hub，这里把前几个名字
            // 摊出来 —— 这是"动态参数到底有没有在走"最直接的观察面（Hub 是运行期状态，值不在任何资产里）。
            if (hub != null && Application.isPlaying && settings.writeParameterHub)
            {
                HoConstraintEditorControls.Caption(ChainStatus(hub));
                HoConstraintEditorControls.Caption(SemanticReadout(hub));
                var session = HoFaceInputHub.Session(settings);
                if (session != null && !string.IsNullOrEmpty(session.SemanticStatus))
                    HoConstraintEditorControls.Caption(session.SemanticStatus);
            }
        }

        /// <summary>
        /// **这条链现在走到哪一步了** —— 一行，按顺序念就能查出断在哪：
        /// 会话起没起 → 这一帧写进角色 Hub 几格（= 中间层算完有没有落下去）→ 有没有输入。
        ///
        /// 为什么要这么一句：这些失败**在界面上长得一模一样**（都是"Hub 是空的、脸不动"），
        /// 但一个要按「开始驱动」、一个说明角色上没有 Hub、一个要连手机。分开写清楚，
        /// 就不用靠猜（2026-09-26 现场：用户报"Hub 空、脸不动"，我隔着屏幕没法区分是哪一种）。
        /// </summary>
        private string ChainStatus(HoFaceSemanticHub hub)
        {
            var session = HoFaceInputHub.Session(settings);
            double age = HoFaceInputHub.LastFrameTime > 0 ? HoFaceClock.Now - HoFaceInputHub.LastFrameTime : double.MaxValue;

            string input = !HoFaceInputHub.Connected ? "手机没连"
                : HoFaceInputHub.LastFrameTime == 0 ? "手机连上了但一包没来"
                : age > 1 ? "输入断流 " + age.ToString("F0") + " 秒"
                : "输入在收（" + HoFaceInputHub.SourceCount + " 条源）";

            string publish;
            if (!settings.writeParameterHub) publish = "没写（「写动态参数 Hub」关着，默认就是关的）";
            else if (session == null) publish = "**会话没起**（点「开始驱动」）";
            else if (session.SemanticPublishedCount < 0) publish = "**写不进去**（角色上没有 HoFaceSemanticHub）";
            else if (session.SemanticPublishedCount == 0) publish = "**一行输出都没有**（配置文件没有输出行？）";
            else publish = "写入 " + session.SemanticPublishedCount + " 格";

            string target = hub.Count == 0 ? "角色 Hub 空 ⇒ 一帧都还没写" : "角色 Hub " + hub.Count + " 格";

            return "链路：会话" + (session == null ? " ✗" : " ✓") + " · " + publish + " · " + target + " · " + input;
        }

        /// <summary>
        /// 前几个**有名字**的格的当前值（最多 6 个）。名字与值都来自 Hub（名字 = 中间层输出行的
        /// `parameter`；Hub 现在既是存储、也是按名字读写的入口）。
        /// ⚠️ 一个名字都没有时明说"还没写过"，而不是显示一片 0（那会被误读成"算出来就是 0"）。
        /// </summary>
        private static string SemanticReadout(HoFaceSemanticHub hub)
        {
            if (hub == null || hub.Count == 0) return "（Hub 里还没有格：中间层还没写过）";

            var text = new System.Text.StringBuilder();
            int shown = 0;
            int named = 0;
            for (int i = 0; i < hub.Count; i++)
            {
                string key = hub.NameAt(i);
                if (string.IsNullOrEmpty(key)) continue;
                named++;
                if (shown >= 6) continue;
                if (shown > 0) text.Append("  ·  ");
                text.Append(key).Append('=').Append(hub.ValueAt(i).ToString("0.###"));
                shown++;
            }
            if (named == 0) return "（Hub 里 " + hub.Count + " 格都还没有名字：中间层还没写过）";
            if (named > shown) text.Append("  …（共 ").Append(named).Append(" 个名字）");
            return text.ToString();
        }

        /// <summary>
        /// 那一栏的黄字。`detail` 是这次具体是哪种情况（而不是一句笼统的提示）。
        /// </summary>
        private static void Warning(string detail)
        {
            var style = new GUIStyle(HoConstraintEditorTheme.Caption);
            style.normal.textColor = HoConstraintEditorTheme.WarningColor;
            GUILayout.Label(new GUIContent(detail,
                "在角色的子层级里放一个空物体（约定叫 SemanticHub），挂上 **Ho Face Semantic Hub**"
                + "（一格一个「名字 + 值」，运行期由写的人按名字开出来，不用手填、也没有长度）。\n"
                + "⚠️ 编辑器**不会**替你建 —— 那是角色资产的一部分，该由作者编排；"
                + "Warudo 侧的节点同样只找不建。"), style);
        }

        /// <summary>
        /// 在角色子层级里找 Hub。**找不到返回 null，绝不创建**（见 <see cref="DrawHubRow"/> 的说明）。
        /// `includeInactive: true` —— 它可能挂在一个被禁用的空物体上，那也算找到了。
        /// </summary>
        private static HoFaceSemanticHub FindSemanticHub(GameObject character)
        {
            if (character == null) return null;
            return character.GetComponentInChildren<HoFaceSemanticHub>(true);
        }

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
            DrawObjectSection(environment);      // 一、对象：调试对象 / 控制器 / 配置文件 / 连接
            DrawProfileSection();                // 二、配置详情：这份 profile 吃啥、怎么处理、输出啥
            DrawInputSection();                  // 三、参数输入：VTS 传过来的**全部裸参数**（纯调试）
            DrawOutputSection();                 // 三·五、参数输出：中间层**求出来的值**（纯调试）
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
        /// 四样：**调试对象**（场景里的角色实例）、**面捕控制器**、**配置文件**、
        /// 以及从调试对象推出来的**动态参数 Hub**（只读展示）。
        /// 配置文件是**必须的** —— 没填它，下面三栏全部锁住不让改（配置是这套东西的心脏，
        /// 空着往下调只会得到一堆看不懂的数字）。
        /// </summary>
        private void DrawObjectSection(HoFaceInputEnvironment environment)
        {
            bool connected = HoFaceInputHub.Connected;
            var session = HoFaceInputHub.Session(settings);
            string summary = !settings.HasProfile ? "缺配置文件"
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
                        // ⚠️ 右边**不再**写"未启动 / 等响应 / N 包"小字（2026-09-27 用户定）：
                        // 一行里既有输入框又有状态字，看着挤，而且那个状态在下面「连接」按钮上
                        // 已经有反馈（连上/断开本身就是结果）。只在**真出错**时才留字。
                        if (live != null && !live.Running && !string.IsNullOrEmpty(live.Error))
                            HoConstraintEditorControls.Caption(live.Error);
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("控制器", HoConstraintEditorTheme.LabelWidth,
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
                    HoConstraintEditorControls.Label("配置文件", HoConstraintEditorTheme.LabelWidth,
                        "**必须**。中间层配置（*.hoface.json）—— Unity 侧与 Warudo 侧读的是同一个文件。");
                    // 用**资产选择器**而不是让人手打路径：手打路径是这栏最容易出错的地方
                    // （相对路径的基准、扩展名、拼错一个字母都只是"读不出来"，看不出错在哪）。
                    // 存进 `settings` 的仍然是**路径**（Warudo 那边读的就是文件路径）。
                    TextAsset currentAsset = string.IsNullOrEmpty(settings.profilePath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<TextAsset>(settings.profilePath);
                    EditorGUI.BeginChangeCheck();
                    var pickedAsset = (TextAsset)EditorGUI.ObjectField(
                        HoConstraintEditorControls.NextFlexible(70.0f), currentAsset, typeof(TextAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        settings.profilePath = pickedAsset != null ? AssetDatabase.GetAssetPath(pickedAsset) : "";
                        settings.ReloadProfile();
                        HoFaceDebugHost.Save();
                    }

                    HoConstraintEditorControls.Gap();
                    // 「选…」留着：工程**之外**的文件没有资产可拖（Warudo 只看路径，那种也合法）。
                    if (HoConstraintEditorControls.Button("选…", "选一个工程外的 .hoface.json（工程里的直接拖左边那个框）。", false, 34.0f))
                    {
                        string path = EditorUtility.OpenFilePanel("选中间层配置", Application.dataPath, "json");
                        if (!string.IsNullOrEmpty(path))
                        {
                            settings.profilePath = MakeProjectRelative(path);
                            settings.ReloadProfile();
                            HoFaceDebugHost.Save();
                        }
                    }

                    // 「新建」**不在这里** —— 从零建一份配置是「配置文件」窗口的活
                    // （菜单 `HoUnityTools/面捕/配置文件`）。这一栏只管"用哪一份"。
                    // 两个窗口都能建的话，两份实现会各自漂移，而且这里建出来的还带一整套
                    // 内置默认表 —— 那是"作者的活"，不该由"选文件"顺手替他决定。

                    if (!settings.HasProfile) HoConstraintEditorControls.Caption("必填；空着下面三栏都锁住");
                    else if (settings.Middleware == null) HoConstraintEditorControls.Caption("读不出来：" + settings.ProfileError);
                    // 路径填了、但不在工程里 ⇒ 资产框会显示 None。说清那是正常的，别让人以为丢了。
                    else if (currentAsset == null) HoConstraintEditorControls.CaptionTrim("工程外文件：" + settings.profilePath, 240.0f,
                        "这份配置在工程之外，所以资产框是空的；路径本身有效，Unity 与 Warudo 都按它读。");
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
                    HoConstraintEditorControls.Label("调试对象", HoConstraintEditorTheme.LabelWidth,
                        "场景里的角色实例。**角色上不需要挂任何组件** —— 拿它只是为了读骨架与网格。");
                    var character = settings.Character();
                    var picked = (GameObject)EditorGUI.ObjectField(
                        HoConstraintEditorControls.NextFlexible(90.0f), character, typeof(GameObject), true);
                    if (picked != character) { settings.SetCharacter(picked); HoFaceDebugHost.Save(); }

                    if (character == null && !string.IsNullOrEmpty(settings.characterPath))
                        HoConstraintEditorControls.Caption("按路径找不到：" + settings.characterPath);
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                // ── 最后两行（2026-09-27 用户定）─────────────────────────────────────
                // 倒数第二行 = 布尔开关（预览混合树 / 自动驱动 / 写动态参数 + 灰色的 Hub 目标），
                // 最后一行 = 功能按钮（连接 / 开始驱动），**放大 + 居中**，在这一栏最底下。
                DrawSwitchesRow();

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button(connected ? "断开" : "连接",
                        connected ? "停掉接收端。" : "按上面填的 IP 把接收端拉起来。", !connected, 96.0f))
                    {
                        if (connected) HoFaceInputHub.Disconnect();
                        else HoFaceInputHub.Connect();
                    }

                    HoConstraintEditorControls.Gap(8.0f);

                    // ⚠️ 必须 gate `HasProfile`：没有配置文件 ⇒ 输入/输出行都是**空表**，
                    // 会话能起来但什么都不做（还会占用形态键）。这正是"空 = 空表"那条口径要拦住的东西。
                    bool canDrive = Application.isPlaying && settings != null && settings.HasProfile;
                    using (new EditorGUI.DisabledScope(!canDrive))
                    {
                        if (HoConstraintEditorControls.Button(session == null ? "开始驱动" : "停止并交还",
                            "只在播放模式下有效；停止会把占用的形态键还回去。", session == null, 112.0f))
                        {
                            if (session == null) HoFaceInputHub.Start(settings);
                            else HoFaceInputHub.Stop(settings);
                        }
                    }

                    HoConstraintEditorControls.Flex();
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
            string summary = !settings.HasProfile ? "先把配置文件填上"
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
                    HoConstraintEditorControls.Caption("对象栏里的「配置文件」是必填的；填好之后这里会列出它的全部行。");
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
                    case HoFaceModifierKind.Steps: parts.Add("维持 " + (modifier.steps != null ? modifier.steps.Count : 0) + " 级"); break;
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
        /// <summary>
        /// **纯调试**：中间层**求出来的值** —— 逐输出行摊开（参数名 / 值 / 表达式）。
        ///
        /// ⚠️ **这一栏就是"动态参数"那一份**：这些行会被会话**按名字原样写进角色 Hub**
        /// （`HoFaceAnimationSession.PublishSemantics`；Warudo 侧是「HoFace写动态参数」节点），
        /// 所以"Hub 里是什么"不用另找地方看 —— 就是这里。
        ///
        /// 为什么要单独一栏：脸不动的时候，"配置没求好值"和"求好了但写不进去"是两件事，
        /// 而在界面上**都表现为"什么都没发生"**。这一栏把两者分开：
        ///   · 值恒 0 ⇒ 输入行没给上（看上一栏的手机线名）或表达式有问题；
        ///   · 值在动、但标着 **不在控制器里** ⇒ 中间层是对的，**控制器参数名对不上**
        ///     （`HoFaceAnimationSession` 对不在控制器里的参数是"算得出值、一声不响地不写"）；
        ///   · 值在动、也没标 ⇒ 中间层与控制器都对，问题在更下游（混合树 / 影子台 / 绑定）。
        /// </summary>
        private void DrawOutputSection()
        {
            var session = HoFaceInputHub.Session(settings);
            var rows = settings.Outputs();

            // ⚠️ **没有会话 ≠ 参数不在控制器里**（2026-09-26 修）：会话没起时 `Compiled` 是 null，
            // 那时"查不到这个参数"只说明没人问过控制器，不是"名字对不上"。以前这两件事混在一起，
            // 表现是"没点开始驱动"时那一栏直接报 `不在控制器里 94`（红黄一片），把人往错的方向带。
            bool hasSession = session != null && session.Compiled != null;

            int named = 0, live = 0, missing = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null || string.IsNullOrEmpty(rows[i].parameter)) continue;
                named++;
                bool inController = !hasSession
                    || session.Compiled.floatParameters.Contains(rows[i].parameter);
                if (!inController) missing++;
                float value = session != null ? session.OutputValue(rows[i].parameter) : float.NaN;
                if (!float.IsNaN(value) && Mathf.Abs(value) > 0.0001f) live++;
            }

            string summary = named == 0
                ? "没有输出行"
                : named + " 行 · 非零 " + live
                  + (hasSession
                      ? (missing > 0 ? " · **不在控制器里 " + missing + "**" : "")
                      : " · **会话没起**");

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref outputExpanded, "参数输出", summary,
                hasSession && missing > 0 ? HoConstraintEditorTheme.WarningColor : HoConstraintEditorTheme.AccentDriver))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                if (named == 0)
                {
                    HoConstraintEditorControls.Caption("这份配置一个输出行都没有（或者还没指定配置文件）—— 这一层什么都不写。");
                    return;
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm);
                    outputSearch = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80.0f), outputSearch, HoConstraintEditorTheme.Field);
                    if (hasSession && missing > 0)
                    {
                        HoConstraintEditorControls.Gap();
                        GUILayout.Label(missing + " 行的参数不在控制器里", InlineWarning());
                    }

                    // ── 覆盖的总开关 ──────────────────────────────────────────────
                    // 覆盖 = 直接钉住这一行**写出去的值**（-1 / 0 / 1），走 SetPreview：
                    // 它在所有生产逻辑之后生效，所以影子树与角色 Hub 看到的都是覆盖值。
                    // 按键 / 门控调试（`Ho/Drive/Gate/Expr/*`）就是靠它。
                    int overrides = session != null ? session.PreviewCount : 0;
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button(
                            overrides > 0 ? "清空覆盖（" + overrides + "）" : "清空覆盖",
                            "把这一栏里所有手动覆盖一次清掉（每行右边那四个按钮：不覆盖 / -1 / 0 / 1）",
                            overrides > 0, 120.0f)
                        && session != null)
                    {
                        session.ClearPreviews();
                    }
                }

                if (!hasSession)
                {
                    // 会话没起时这一栏**永远是空的**。这里**只留一句提示、不放按钮** ——
                    // 「开始驱动」的入口在「对象」段（2026-09-27 用户：入口重复了，删掉这行那个按钮）。
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Caption("（进播放模式、并在「对象」段点「开始驱动」后这里才有值；现在只能看名字与表达式）");
                    }
                }

                outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.Height(180.0f));
                using (HoConstraintEditorControls.Row(true))
                {
                    GUI.Label(HoConstraintEditorControls.Next(150.0f), "参数名（写进混合树）", HoConstraintEditorTheme.Caption);
                    HoConstraintEditorControls.Flex();
                    GUI.Label(HoConstraintEditorControls.Next(96.0f), "覆盖", HoConstraintEditorTheme.Caption);
                    GUI.Label(HoConstraintEditorControls.Next(64.0f), "值", HoConstraintEditorTheme.Caption);
                    HoConstraintEditorControls.Flex();
                    GUI.Label(HoConstraintEditorControls.Next(120.0f), "算它的表达式", HoConstraintEditorTheme.Caption);
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                    if (!string.IsNullOrEmpty(outputSearch)
                        && row.parameter.IndexOf(outputSearch, StringComparison.OrdinalIgnoreCase) < 0
                        && (row.expression ?? "").IndexOf(outputSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // 调试覆盖（面板上那四个按钮）：**这一帧写出去的值**（OutputValue 已经把覆盖算进去），
                    // 所以进度条、值、影子树、角色 Hub 看的是同一份。
                    float overrideValue = 0f;
                    bool overridden = session != null && session.TryGetPreview(row.parameter, out overrideValue);
                    float value = session != null ? session.OutputValue(row.parameter) : float.NaN;
                    bool has = !float.IsNaN(value) && session != null;
                    bool inController = !hasSession
                        || session.Compiled.floatParameters.Contains(row.parameter);

                    using (HoConstraintEditorControls.Row(true))
                    {
                        GUI.Label(HoConstraintEditorControls.Next(150.0f), row.parameter,
                            has ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim);

                        Rect bar = HoConstraintEditorControls.NextFlexible(40.0f);
                        if (has && value >= 0f && value <= 1f)
                            HoConstraintEditorControls.Meter(bar, value, 0f, 1f,
                                overridden ? HoConstraintEditorTheme.WarningColor : HoConstraintEditorTheme.AccentDriver);
                        else if (Event.current.type == EventType.Repaint)
                            EditorGUI.DrawRect(bar, HoConstraintEditorTheme.WellColor);

                        // ── 覆盖：不覆盖 / -1 / 0 / 1 ──────────────────────────────────
                        // 按键与门控调试就靠这四个：`-1 / 0 / 1` 直接把这一行**写出去的值**钉住
                        // （走 SetPreview：所有生产逻辑之后生效），「不」把这一行交还给中间层。
                        Rect overrideBlock = HoConstraintEditorControls.Next(96.0f);
                        const float cell = 22.0f;
                        if (HoConstraintEditorControls.SegmentButton(
                            new Rect(overrideBlock.x, overrideBlock.y, cell, overrideBlock.height),
                            "不", !overridden, "不覆盖：这一行按中间层算出来的值走"))
                        {
                            session?.ClearPreview(row.parameter);
                        }
                        if (HoConstraintEditorControls.SegmentButton(
                            new Rect(overrideBlock.x + (cell + 1.0f), overrideBlock.y, cell, overrideBlock.height),
                            "-1", overridden && Mathf.Abs(overrideValue + 1f) < 0.0001f, "把这一行覆盖成 -1"))
                        {
                            session?.SetPreview(row.parameter, -1f);
                        }
                        if (HoConstraintEditorControls.SegmentButton(
                            new Rect(overrideBlock.x + (cell + 1.0f) * 2.0f, overrideBlock.y, cell, overrideBlock.height),
                            "0", overridden && Mathf.Abs(overrideValue) < 0.0001f, "把这一行覆盖成 0"))
                        {
                            session?.SetPreview(row.parameter, 0f);
                        }
                        if (HoConstraintEditorControls.SegmentButton(
                            new Rect(overrideBlock.x + (cell + 1.0f) * 3.0f, overrideBlock.y, cell, overrideBlock.height),
                            "1", overridden && Mathf.Abs(overrideValue - 1f) < 0.0001f, "把这一行覆盖成 1"))
                        {
                            session?.SetPreview(row.parameter, 1f);
                        }

                        GUI.Label(HoConstraintEditorControls.Next(64.0f),
                            new GUIContent(
                                has ? value.ToString("F4") : "—",
                                overridden ? "这一行被调试覆盖盖住了（不是中间层算出来的值）" : null),
                            overridden ? OverrideValue()
                                : (has ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim));

                        GUI.Label(HoConstraintEditorControls.NextFlexible(60.0f), row.expression ?? "",
                            HoConstraintEditorTheme.Caption);

                        if (!inController)
                            GUI.Label(HoConstraintEditorControls.Next(110.0f), "不在控制器里 ⇒ 不写",
                                InlineWarning());
                    }
                }

                EditorGUILayout.EndScrollView();

                if (session != null && session.Compiled != null && session.Compiled.warnings.Count > 0)
                {
                    HoConstraintEditorControls.Caption("编译期提示 " + session.Compiled.warnings.Count + " 条：");
                    for (int i = 0; i < session.Compiled.warnings.Count && i < 4; i++)
                        HoConstraintEditorControls.Caption("  · " + session.Compiled.warnings[i]);
                }
            }
        }

        /// <summary>行内的黄字（那一栏在块里，用不了 <see cref="Warning"/> 的整行布局）。</summary>
        private static GUIStyle InlineWarning()
        {
            if (inlineWarning == null)
            {
                inlineWarning = new GUIStyle(HoConstraintEditorTheme.Caption);
                inlineWarning.normal.textColor = HoConstraintEditorTheme.WarningColor;
                inlineWarning.wordWrap = false;
            }
            return inlineWarning;
        }

        private static GUIStyle inlineWarning;

        /// <summary>被调试覆盖盖住的那个读数（黄字，和普通读数区分开）。</summary>
        private static GUIStyle OverrideValue()
        {
            if (overrideValueStyle == null)
            {
                overrideValueStyle = new GUIStyle(HoConstraintEditorTheme.Value);
                overrideValueStyle.normal.textColor = HoConstraintEditorTheme.WarningColor;
            }
            return overrideValueStyle;
        }

        private static GUIStyle overrideValueStyle;

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
