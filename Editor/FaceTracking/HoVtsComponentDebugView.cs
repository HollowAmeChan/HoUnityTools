using System;
using System.Collections.Generic;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Editor.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 角色组件模式的内容渲染器。它由 HoFaceTrackingWindow 调用，不创建第二个窗口；
    /// 区块、间距、按钮和旧面板保持同一套设计。
    /// </summary>
    internal static class HoVtsDebugHost
    {
        public static HoVtsDebugSession Session { get; private set; }
        public static HoVtsTraceRecorder Recorder { get; private set; }
        public static string LastTraceFile { get; private set; }
        public static int LastTraceSamples { get; private set; }
        public static string Error { get; private set; }
        public static bool Running => Session != null && !Session.Disposed;

        public static bool Start(GameObject target)
        {
            if (Running && Session.Target == target) return true;
            Stop();
            try { Session = new HoVtsDebugSession(target); Error = null; return true; }
            catch (Exception error) { Error = error.Message; return false; }
        }

        public static void Record(double seconds, double interval)
        {
            Recorder?.Stop("replaced");
            if (Session == null || Session.Disposed) { Error = "请先开始角色调试。"; return; }
            try { Recorder = new HoVtsTraceRecorder(Session, interval, seconds); Error = null; }
            catch (Exception error) { Error = error.Message; }
        }

        public static void StopTrace()
        {
            if (Recorder == null) return;
            Recorder.Stop("manual-stop");
            LastTraceFile = Recorder.FilePath;
            LastTraceSamples = Recorder.Samples;
            Recorder = null;
        }

        public static void Stop()
        {
            HoVtsQuickRangeRecorder.Stop(false);
            if (Recorder != null)
            {
                Recorder.Stop("session-stopped");
                LastTraceFile = Recorder.FilePath;
                LastTraceSamples = Recorder.Samples;
            }
            Recorder = null;
            Session?.Dispose();
            Session = null;
            Error = null;
        }
    }

    /// <summary>与旧面板“记 5 秒”一致的区间统计，结果写 Console，便于直接复制。</summary>
    internal static class HoVtsQuickRangeRecorder
    {
        private static HoVtsDebugSession session;
        private static readonly List<string> names = new List<string>();
        private static readonly List<float> min = new List<float>();
        private static readonly List<float> max = new List<float>();
        private static readonly List<double> sum = new List<double>();
        private static readonly List<int> count = new List<int>();
        private static bool inputs;
        private static double started;
        private static int samples;
        public static bool Recording => session != null;
        public static double Remaining => Math.Max(0, 5.0 - (EditorApplication.timeSinceStartup - started));

        public static void Start(HoVtsDebugSession source, IList<HoFaceOutput> rows, bool input)
        {
            Stop(false);
            if (source == null || source.Disposed || rows == null) return;
            session = source; inputs = input;
            names.Clear(); min.Clear(); max.Clear(); sum.Clear(); count.Clear(); samples = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null || string.IsNullOrEmpty(rows[i].parameter)) continue;
                names.Add(rows[i].parameter); min.Add(float.PositiveInfinity); max.Add(float.NegativeInfinity); sum.Add(0); count.Add(0);
            }
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        public static void Stop(bool print)
        {
            if (session == null) return;
            EditorApplication.update -= Tick;
            if (print) Print();
            session = null;
        }

        private static void Tick()
        {
            if (session == null || session.Disposed) { Stop(false); return; }
            var rows = inputs ? session.Receiver.Middleware?.inputs : session.Receiver.Middleware?.outputs;
            for (int i = 0; i < names.Count; i++)
            {
                int rowIndex = FindRow(rows, names[i]);
                if (rowIndex < 0) continue;
                float value; bool found;
                if (inputs) found = session.Receiver.TryReadInputRow(rowIndex, out _, out value, out _);
                else found = session.Receiver.TryReadOutputRow(rowIndex, out _, out value);
                if (!found || float.IsNaN(value) || float.IsInfinity(value)) continue;
                if (value < min[i]) min[i] = value; if (value > max[i]) max[i] = value;
                sum[i] += value; count[i]++;
            }
            samples++;
            if (EditorApplication.timeSinceStartup - started >= 5.0) Stop(true);
        }

        private static int FindRow(List<HoFaceOutput> rows, string name)
        {
            if (rows == null) return -1;
            for (int i = 0; i < rows.Count; i++) if (rows[i] != null && rows[i].parameter == name) return i;
            return -1;
        }

        private static void Print()
        {
            var order = new List<int>();
            for (int i = 0; i < names.Count; i++)
            {
                if (count[i] == 0) continue;
                float span = max[i] - min[i], average = (float)(sum[i] / count[i]);
                if (span > 0.01f || Mathf.Abs(average) > 0.01f) order.Add(i);
            }
            order.Sort((a, b) => (max[b] - min[b]).CompareTo(max[a] - min[a]));
            var text = new StringBuilder();
            text.Append("[Ho 面捕统计] ").Append(inputs ? "配置输入行" : "配置输出行")
                .Append("  窗口 5.0s · 采样 ").Append(samples).Append(" 帧 · 动过 ")
                .Append(order.Count).Append(" / ").Append(names.Count).Append(" 行\n参数名  min  avg  max  波动");
            foreach (int i in order)
            {
                float average = (float)(sum[i] / count[i]);
                text.Append('\n').Append(names[i]).Append("  ").Append(min[i].ToString("F4")).Append("  ")
                    .Append(average.ToString("F4")).Append("  ").Append(max[i].ToString("F4")).Append("  ")
                    .Append((max[i] - min[i]).ToString("F4"));
            }
            text.Append("\n[Ho 面捕统计] 完（没动的行已省略；整段复制即可）");
            Debug.Log(text.ToString());
        }
    }

    /// <summary>把角色组件渲染到旧的 Ho 面捕调试窗口中。</summary>
    internal static class HoVtsComponentDebugView
    {
        private static int tab;
        private static string rawSearch = string.Empty;
        private static string profileSearch = string.Empty;
        private static string inputSearch = string.Empty;
        private static string outputSearch = string.Empty;
        private static bool autoStart = true;
        private static bool objectExpanded = true, profileExpanded = true, traceExpanded = true;
        private static bool inputExpanded = true, configInputExpanded = true, outputExpanded = true, diagnoseExpanded, logExpanded, hintExpanded;
        private static float traceSeconds = 5f;
        private static int traceInterval;
        private static Vector2 pageScroll;
        private static readonly string[] traceIntervals = { "每次求解", "0.02 秒", "0.05 秒", "0.2 秒" };
        private static readonly double[] traceIntervalSeconds = { 0, 0.02, 0.05, 0.2 };

        public static void Draw(HoFaceDebugSettings settings, GameObject initialTarget = null)
        {
            var target = initialTarget != null ? initialTarget : settings.Character();
            using (var scroll = new EditorGUILayout.ScrollViewScope(pageScroll))
            {
                pageScroll = scroll.scrollPosition;
                DrawTitle(target);
                DrawObjectSection(settings, target, ref target);
                var receiver = FindReceiver(target, out var receivers);
                if (receiver == null)
                {
                    if (target != null) EditorGUILayout.HelpBox(receivers.Length == 0 ? "对象下没有 HoVts 参数接收器。" : "对象下有多个接收器，请缩小调试对象范围。", MessageType.Warning);
                    DrawDiagnose(target, null);
                    return;
                }
                DrawProfileSection(receiver);
                DrawTraceSection();
                using (HoConstraintEditorControls.Card())
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("快捷观察", HoConstraintEditorTheme.LabelWidthSm,
                        "保留旧面板的快速切换；选「全部」时下面的所有诊断区块都会显示。");
                    tab = GUILayout.Toolbar(tab, new[] { "全部", "原始输入", "规则输入", "规则输出" }, EditorStyles.toolbarButton);
                }
                if (tab == 0 || tab == 1) DrawRawInputSection(settings);
                if (tab == 0 || tab == 2) DrawConfigInputSection(receiver);
                if (tab == 0 || tab == 3) DrawOutputSection(target, receiver);
                DrawDiagnose(target, receiver);
            }
        }

        private static void DrawTitle(GameObject target)
        {
            bool connected = HoFaceInputHub.Connected, active = HoVtsDebugHost.Running, tracked = false;
            var receiver = FindReceiver(target, out _);
            if (receiver != null) tracked = receiver.Tracked && !receiver.InputExpired;
            string state = active ? "调试中" : !connected ? "已停止" : receiver == null ? "待配置" : receiver.HasFrame ? (tracked ? "接收中" : "未追踪") : "等待响应";
            HoConstraintEditorControls.Title("Ho 面捕调试", target == null ? "未选择对象" : target.name, (state, active || tracked), (HoFaceInputHub.SourceCount + " 条源", connected));
        }

        private static void DrawObjectSection(HoFaceDebugSettings settings, GameObject target, ref GameObject targetRef)
        {
            var receiver = FindReceiver(target, out var receivers);
            bool connected = HoFaceInputHub.Connected;
            string summary = receiver == null ? "缺 VTS 接收器" : HoVtsDebugHost.Running ? "驱动中" : receiver.HasFrame ? "角色已接收" : "就绪";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref objectExpanded, "对象", summary, HoConstraintEditorTheme.AccentDriver)) return;
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("调试对象", HoConstraintEditorTheme.LabelWidth, "选择角色实例；规则、控制器和头眼姿态都从角色上的组件自动发现。");
                    var picked = (GameObject)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(90), target, typeof(GameObject), true);
                    if (picked != target)
                    {
                        HoVtsDebugHost.Stop(); settings.SetCharacter(picked); settings.Save(); targetRef = picked; GUIUtility.ExitGUI();
                    }
                }
                var environment = HoFaceInputEnvironment.instance;
                for (int i = 0; i < environment.sources.Count; i++)
                {
                    var entry = environment.sources[i]; if (entry == null) continue;
                    using (HoConstraintEditorControls.Row())
                    {
                        HoConstraintEditorControls.Label("手机 IP", HoConstraintEditorTheme.LabelWidth, HoFaceReceiverFactory.Hint(entry.kind));
                        using (new EditorGUI.DisabledScope(connected))
                        {
                            string ip = EditorGUI.TextField(HoConstraintEditorControls.Next(120), entry.phoneIp, HoConstraintEditorTheme.Field);
                            if (ip != entry.phoneIp) { entry.phoneIp = ip; environment.Persist(); }
                            HoConstraintEditorControls.Gap(6);
                            int port = EditorGUI.IntField(HoConstraintEditorControls.Next(58), entry.localPort, HoConstraintEditorTheme.Field);
                            if (port != entry.localPort) { entry.localPort = Mathf.Clamp(port, 1024, 65535); environment.Persist(); }
                            HoConstraintEditorControls.Gap(6);
                            bool enabled = HoConstraintEditorControls.Toggle("启用", entry.enabled, "关掉就不启动这一路接收端。");
                            if (enabled != entry.enabled) { entry.enabled = enabled; environment.Persist(); }
                        }
                        HoConstraintEditorControls.Flex(); if (HoFaceInputHub.Connected && HoFaceInputHub.LastFrameTime > 0) HoConstraintEditorControls.Caption("接收中");
                    }
                }
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("电脑 IPv4", HoConstraintEditorTheme.LabelWidth);
                    HoConstraintEditorControls.Caption(string.Join(" / ", GetLocalAddresses())); HoConstraintEditorControls.Flex();
                }
                HoConstraintEditorControls.Separator(3, 3);
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("角色组件", HoConstraintEditorTheme.LabelWidth, "这些组件必须挂在角色预制件或其 HoVTS 子物体上。");
                    HoConstraintEditorControls.Caption(receiver == null ? "未找到" : "已找到接收器" + (receivers.Length > 1 ? "（多个）" : "")); HoConstraintEditorControls.Flex();
                    using (new EditorGUI.DisabledScope(receiver == null || receiver.ProfileAsset == null))
                        if (HoConstraintEditorControls.Button("编辑角色规则", "打开并直接编辑当前角色引用的 JSON 规则文件。", false, 104)) HoFaceProfileWindow.Open(receiver);
                    if (receiver != null && receiver.ProfileAsset == null)
                    {
                        if (HoConstraintEditorControls.Button("创建规则 JSON", "把旧内嵌规则迁移成可复用 JSON，并自动接到组件。", false, 104))
                        {
                            try { HoVtsRuleAuthoring.CreateProfileFromInline(receiver); }
                            catch (Exception error) { Debug.LogError("[Ho 面捕] 创建规则 JSON 失败：" + error.Message); }
                        }
                    }
                }
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("规则 JSON", HoConstraintEditorTheme.LabelWidth, "组件只引用这份可复用文件；规则编辑器会直接保存它。");
                    var currentProfile = receiver != null ? receiver.ProfileAsset : null;
                    EditorGUI.BeginChangeCheck();
                    var pickedProfile = (TextAsset)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(90), currentProfile, typeof(TextAsset), false);
                    if (EditorGUI.EndChangeCheck() && receiver != null)
                    {
                        try { HoVtsRuleAuthoring.AssignProfile(receiver, pickedProfile); }
                        catch (Exception error) { Debug.LogError("[Ho 面捕] 指定规则 JSON 失败：" + error.Message); }
                    }
                    HoConstraintEditorControls.Flex();
                    using (new EditorGUI.DisabledScope(receiver == null || receiver.ProfileAsset == null))
                        if (HoConstraintEditorControls.Button("打开编辑器", "切换到当前角色引用的 JSON 文件。", false, 90)) HoFaceProfileWindow.Open(receiver);
                }
                if (receiver != null) DrawComponentSummary(target, receiver);
                HoConstraintEditorControls.Separator(3, 3);
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button(connected ? "断开" : "连接", connected ? "停掉接收端。" : "按上面填的 IP 拉起接收端。", !connected, 96))
                    {
                        if (connected) HoFaceInputHub.Disconnect(); else HoFaceInputHub.Connect();
                    }
                    HoConstraintEditorControls.Gap(8);
                    bool canStart = EditorApplication.isPlaying && receiver != null && !receiver.HasFrame;
                    using (new EditorGUI.DisabledScope(!canStart || HoVtsDebugHost.Running))
                        if (HoConstraintEditorControls.Button("开始调试", "没有外部输入时，用手机源驱动当前角色并开放临时覆盖。", true, 104)) HoVtsDebugHost.Start(target);
                    using (new EditorGUI.DisabledScope(!HoVtsDebugHost.Running))
                        if (HoConstraintEditorControls.Button("停止并交还", "清除临时覆盖并把写入交还角色组件。", false, 104)) HoVtsDebugHost.Stop();
                    HoConstraintEditorControls.Flex();
                }
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("姿态校准", HoConstraintEditorTheme.LabelWidthSm,
                        "保持正面和自然表情，采样三秒头部、头位和双眼姿态作为中性偏移。");
                    using (new EditorGUI.DisabledScope(!connected || HoFaceInputHub.CalibrationActive))
                        if (HoConstraintEditorControls.Button("校准 3 秒", "开始三秒姿态中性校准；不会修改表情规则或输入范围。", true, 96))
                            HoFaceInputHub.BeginCalibration(3.0);
                    HoConstraintEditorControls.Gap(8);
                    HoConstraintEditorControls.Caption(HoFaceInputHub.CalibrationStatus);
                    HoConstraintEditorControls.Flex();
                }
                using (HoConstraintEditorControls.Row(true))
                {
                    autoStart = HoConstraintEditorControls.Toggle("进入播放自动开始", autoStart, "播放时如果角色没有任何输入，自动建立一个可覆盖的调试会话。");
                    HoConstraintEditorControls.Flex();
                    if (autoStart && EditorApplication.isPlaying && receiver != null && !receiver.HasFrame && !HoVtsDebugHost.Running) HoVtsDebugHost.Start(target);
                }
            }
            if (!string.IsNullOrEmpty(HoVtsDebugHost.Error)) EditorGUILayout.HelpBox(HoVtsDebugHost.Error, MessageType.Error);
        }

        private static void DrawComponentSummary(GameObject target, HoVtsParameterReceiver receiver)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("接收状态", HoConstraintEditorTheme.LabelWidthSm);
                HoConstraintEditorControls.Pill(!receiver.HasFrame ? "等待输入" : receiver.InputExpired ? "输入超时" : receiver.Tracked ? "追踪中" : "未追踪", receiver.Tracked && !receiver.InputExpired);
                HoConstraintEditorControls.Gap(8); HoConstraintEditorControls.Caption("原始键 " + receiver.RawCount + " · 处理后 " + receiver.ValueCount); HoConstraintEditorControls.Flex(); HoConstraintEditorControls.Caption(receiver.HasFrame ? receiver.Source : "没有当前帧");
            }
            foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true))
                if (controller.ParameterReceiver == receiver) using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label("表情控制器", HoConstraintEditorTheme.LabelWidthSm, "角色上的独立 Animator 控制槽。"); HoConstraintEditorControls.Caption(controller.Status + " · 绑定 " + controller.BindingCount); }
            foreach (var pose in target.GetComponentsInChildren<HoVtsLookAt>(true))
                if (pose.ParameterReceiver == receiver) using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label("头眼姿态", HoConstraintEditorTheme.LabelWidthSm, "独立的 LookAt / 姿态组件。"); HoConstraintEditorControls.Caption(pose.Status); }
        }

        private static void DrawProfileSection(HoVtsParameterReceiver receiver)
        {
            var middleware = receiver.Middleware;
            string summary = middleware == null ? "规则未加载" : "输入行 " + middleware.inputs.Count + " · 输出行 " + middleware.outputs.Count;
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref profileExpanded, "配置详情", summary, HoConstraintEditorTheme.AccentOutput)) return;
            using (HoConstraintEditorControls.Card())
            {
                if (middleware == null) { HoConstraintEditorControls.Caption("角色没有规则。请点对象栏的「编辑角色规则」。"); return; }
                using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm); profileSearch = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80), profileSearch, HoConstraintEditorTheme.Field); }
                HoConstraintEditorControls.Caption("输入行 —— 手机线名 → 规范名"); DrawProfileHeader(); foreach (var row in middleware.inputs) DrawProfileRow(row);
                HoConstraintEditorControls.Separator(4, 4); HoConstraintEditorControls.Caption("输出行 —— 规范名 → 控制器参数"); DrawProfileHeader(); foreach (var row in middleware.outputs) DrawProfileRow(row);
            }
        }

        private static void DrawProfileHeader()
        {
            using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), "规范名 / 输出", HoConstraintEditorTheme.Caption); HoConstraintEditorControls.Flex(); GUI.Label(HoConstraintEditorControls.Next(56), "曲线", HoConstraintEditorTheme.Caption); GUI.Label(HoConstraintEditorControls.Next(96), "修饰符", HoConstraintEditorTheme.Caption); }
        }

        private static void DrawProfileRow(HoFaceOutput row)
        {
            if (row == null || string.IsNullOrEmpty(row.parameter)) return;
            if (!Matches(row.parameter, profileSearch) && !Matches(row.expression, profileSearch)) return;
            using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), row.parameter, HoConstraintEditorTheme.Value); GUI.Label(HoConstraintEditorControls.NextFlexible(100), string.IsNullOrEmpty(row.expression) ? "默认值" : row.expression, HoConstraintEditorTheme.Caption); GUI.Label(HoConstraintEditorControls.Next(56), row.curve == null ? "—" : row.curve.length + " 点", HoConstraintEditorTheme.LabelDim); GUI.Label(HoConstraintEditorControls.Next(96), ModifierText(row), HoConstraintEditorTheme.LabelDim); }
        }

        private static string ModifierText(HoFaceOutput row)
        {
            if (row.modifiers == null || row.modifiers.Count == 0) return "—";
            var values = new List<string>();
            foreach (var modifier in row.modifiers)
            {
                if (modifier == null) continue;
                switch (modifier.kind) { case HoFaceModifierKind.Smooth: values.Add("平滑 " + modifier.seconds.ToString("0.##") + "s"); break; case HoFaceModifierKind.Delay: values.Add("延迟 " + modifier.seconds.ToString("0.##") + "s"); break; case HoFaceModifierKind.Steps: values.Add("维持 " + (modifier.steps == null ? 0 : modifier.steps.Count) + " 级"); break; }
            }
            return values.Count == 0 ? "—" : string.Join(" → ", values.ToArray());
        }

        private static void DrawTraceSection()
        {
            bool active = HoVtsDebugHost.Recorder != null && HoVtsDebugHost.Recorder.Recording;
            string summary = active ? HoVtsDebugHost.Recorder.Samples + " 条" : "输入、规则阶段与输出同步记录";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref traceExpanded, "时序录制", summary, HoConstraintEditorTheme.AccentDriver)) return;
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("时长", 64); traceSeconds = Mathf.Max(.1f, EditorGUI.FloatField(HoConstraintEditorControls.Next(58), traceSeconds, HoConstraintEditorTheme.Field)); HoConstraintEditorControls.Gap(6); HoConstraintEditorControls.Label("间隔", 42); traceInterval = EditorGUI.Popup(HoConstraintEditorControls.Next(92), traceInterval, traceIntervals); HoConstraintEditorControls.Flex();
                    using (new EditorGUI.DisabledScope(HoVtsDebugHost.Session == null)) if (HoConstraintEditorControls.Button(active ? "停止并保存" : "录时序", "记录真实观测到的输入、规则阶段和最终输出。", true, 104)) { if (active) HoVtsDebugHost.StopTrace(); else HoVtsDebugHost.Record(traceSeconds, traceIntervalSeconds[Mathf.Clamp(traceInterval, 0, traceIntervalSeconds.Length - 1)]); }
                }
                using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Caption(HoVtsDebugHost.LastTraceFile == null ? "尚无录制文件" : "已保存 " + HoVtsDebugHost.LastTraceSamples + " 条"); HoConstraintEditorControls.Flex(); using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(HoVtsDebugHost.LastTraceFile) || !System.IO.File.Exists(HoVtsDebugHost.LastTraceFile))) if (HoConstraintEditorControls.Button("定位录制文件", "在资源管理器中打开录制文件。", false, 110)) EditorUtility.RevealInFinder(HoVtsDebugHost.LastTraceFile); }
            }
        }

        private static void DrawRawInputSection(HoFaceDebugSettings settings)
        {
            int count = HoFaceInputHub.MergedValues.Count; string summary = !HoFaceInputHub.Connected ? "未连接" : count == 0 ? "等待响应" : count + " 个线名";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref inputExpanded, "接收器输入行", summary, HoConstraintEditorTheme.AccentDriver)) return;
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm); rawSearch = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80), rawSearch, HoConstraintEditorTheme.Field); }
                if (count == 0) { HoConstraintEditorControls.Caption("还没收到包 —— 看「排查」那一栏。"); return; }
                using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), "线名（手机原样）", HoConstraintEditorTheme.Caption); HoConstraintEditorControls.Flex(); GUI.Label(HoConstraintEditorControls.Next(64), "值", HoConstraintEditorTheme.Caption); GUI.Label(HoConstraintEditorControls.Next(56), "距上帧", HoConstraintEditorTheme.Caption); }
                var names = new List<string>(HoFaceInputHub.MergedValues.Keys); names.Sort(StringComparer.Ordinal); double now = HoFaceClock.Now;
                foreach (string name in names)
                {
                    if (!Matches(name, rawSearch)) continue;
                    float value = HoFaceInputHub.MergedValues[name]; double at = HoFaceInputHub.ReceivedAt(name), age = at > 0 ? now - at : double.MaxValue; bool fresh = age <= Mathf.Max(.1f, settings.staleSeconds);
                    using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), name, fresh ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim); DrawMeter(value, fresh); GUI.Label(HoConstraintEditorControls.Next(64), value.ToString("F4"), fresh ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim); GUI.Label(HoConstraintEditorControls.Next(56), at <= 0 ? "—" : age.ToString("F1") + "s", HoConstraintEditorTheme.LabelDim); }
                }
            }
        }

        private static void DrawConfigInputSection(HoVtsParameterReceiver receiver)
        {
            var session = CurrentSession(receiver); var rows = receiver.Middleware?.inputs; int named = rows == null ? 0 : CountNamed(rows); string summary = named == 0 ? "没有输入行" : named + " 行" + (session == null ? " · 会话没起" : "");
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref configInputExpanded, "配置输入行", summary, HoConstraintEditorTheme.AccentDriver)) return;
            using (HoConstraintEditorControls.Card())
            {
                if (rows == null || named == 0) { HoConstraintEditorControls.Caption("角色规则没有输入行。"); return; }
                DrawValueToolbar(session, rows, true); DrawValueHeader("规范名（表达式读它）", "读哪根线（表达式）");
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; if (row == null || !Matches(row.parameter, inputSearch)) continue;
                    bool has = receiver.TryReadInputRow(i, out _, out float value, out _); float pinned = 0; bool overridden = session != null && session.InputPins.TryGetValue(row.parameter, out pinned);
                    using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), row.parameter, has ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim); DrawMeter(has ? value : row.defaultValue, has); DrawVtsOverrideButtons(session, row.parameter, true, overridden, pinned); GUI.Label(HoConstraintEditorControls.Next(64), overridden ? pinned.ToString("F4") : has ? value.ToString("F4") : "—", overridden ? WarningStyle() : HoConstraintEditorTheme.Value); GUI.Label(HoConstraintEditorControls.NextFlexible(70), string.IsNullOrEmpty(row.expression) ? "= " + row.defaultValue.ToString("0.###") : row.expression, HoConstraintEditorTheme.Caption); }
                }
            }
        }

        private static void DrawOutputSection(GameObject target, HoVtsParameterReceiver receiver)
        {
            var session = CurrentSession(receiver); var rows = receiver.Middleware?.outputs; int named = rows == null ? 0 : CountNamed(rows); int bindings = 0;
            if (target != null) foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true)) if (controller.ParameterReceiver == receiver) bindings += controller.BindingCount;
            string summary = named == 0 ? "没有输出行" : named + " 行" + (session == null ? " · 会话没起" : " · 绑定 " + bindings);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref outputExpanded, "配置输出行", summary, HoConstraintEditorTheme.AccentDriver)) return;
            using (HoConstraintEditorControls.Card())
            {
                if (rows == null || named == 0) { HoConstraintEditorControls.Caption("角色规则没有输出行。"); return; }
                DrawValueToolbar(session, rows, false); DrawValueHeader("参数名（写进控制器）", "算它的表达式");
                DrawIsolationToolbar(session, receiver);
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; if (row == null || !Matches(row.parameter, outputSearch)) continue;
                    bool has = receiver.TryReadOutputRow(i, out _, out float value); float pinned = 0; bool overridden = session != null && session.OutputPins.TryGetValue(row.parameter, out pinned);
                    using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), row.parameter, has ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.LabelDim); DrawMeter(has ? value : row.defaultValue, has); DrawVtsOverrideButtons(session, row.parameter, false, overridden, pinned); GUI.Label(HoConstraintEditorControls.Next(64), overridden ? pinned.ToString("F4") : has ? value.ToString("F4") : "—", overridden ? WarningStyle() : HoConstraintEditorTheme.Value); GUI.Label(HoConstraintEditorControls.NextFlexible(70), row.expression ?? string.Empty, HoConstraintEditorTheme.Caption); }
                }
            }
        }

        private static void DrawValueToolbar(HoVtsDebugSession session, IList<HoFaceOutput> rows, bool input)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm); string search = input ? inputSearch : outputSearch; search = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80), search, HoConstraintEditorTheme.Field); if (input) inputSearch = search; else outputSearch = search; HoConstraintEditorControls.Flex();
                bool record = HoVtsQuickRangeRecorder.Recording;
                if (HoConstraintEditorControls.Button(record ? "记录中 " + HoVtsQuickRangeRecorder.Remaining.ToString("F1") + "s" : "记 5 秒", "做住一个动作，统计每行 min / avg / max / 波动。", session != null, 112)) { if (record) HoVtsQuickRangeRecorder.Stop(false); else HoVtsQuickRangeRecorder.Start(session, rows, input); }
                int overrides = session == null ? 0 : input ? session.InputPins.Count : session.OutputPins.Count;
                if (HoConstraintEditorControls.Button(overrides > 0 ? "清空覆盖（" + overrides + "）" : "清空覆盖", "清除这一栏的临时覆盖。", overrides > 0, 112)) session?.ClearPins();
            }
        }

        private static void DrawValueHeader(string name, string expression)
        {
            using (HoConstraintEditorControls.Row(true)) { GUI.Label(HoConstraintEditorControls.Next(150), name, HoConstraintEditorTheme.Caption); HoConstraintEditorControls.Flex(); GUI.Label(HoConstraintEditorControls.Next(96), "覆盖", HoConstraintEditorTheme.Caption); GUI.Label(HoConstraintEditorControls.Next(64), "值", HoConstraintEditorTheme.Caption); HoConstraintEditorControls.Flex(); GUI.Label(HoConstraintEditorControls.Next(110), expression, HoConstraintEditorTheme.Caption); }
        }

        private static int isolationChoice;
        private static void DrawIsolationToolbar(HoVtsDebugSession session, HoVtsParameterReceiver receiver)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("输出隔离", HoConstraintEditorTheme.LabelWidthSm,
                    "临时只保留一个脸部区域，快速判断是规则、控制器还是下游写入出了问题。");
                isolationChoice = EditorGUI.Popup(HoConstraintEditorControls.Next(110), isolationChoice,
                    new[] { "当前筛选输出", "嘴", "眉", "脸颊", "眼睑", "眼球" });
                var selected = new List<string>();
                var scope = new List<string>();
                var region = new[] { HoFaceRegion.All, HoFaceRegion.Mouth, HoFaceRegion.Brows, HoFaceRegion.Cheeks, HoFaceRegion.Eyelids, HoFaceRegion.Gaze }[Mathf.Clamp(isolationChoice, 0, 5)];
                var rows = receiver.Middleware?.outputs;
                if (rows != null)
                    foreach (var row in rows)
                    {
                        if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                        scope.Add(row.parameter);
                        string shape = row.parameter.StartsWith("ARKit/", StringComparison.Ordinal) ? row.parameter.Substring(6) : row.parameter;
                        int index = HoFaceTrackingChannels.IndexOf(shape);
                        if (isolationChoice == 0 ? Matches(row.parameter, outputSearch) : index >= 0 && (HoFaceTrackingChannels.Region(HoFaceTrackingChannels.Names[index]) & region) != 0)
                            selected.Add(row.parameter);
                    }
                HoConstraintEditorControls.Flex();
                using (new EditorGUI.DisabledScope(session == null || selected.Count == 0))
                    if (HoConstraintEditorControls.Button("仅此范围", "临时把其他输出钉在默认值。", true, 76)) session.IsolateOutputs(selected, scope);
                using (new EditorGUI.DisabledScope(session == null || session.IsolationOverrides.Count == 0))
                    if (HoConstraintEditorControls.Button("恢复", "恢复全部输出。", false, 48)) session.RestoreIsolation();
            }
        }

        private static void DrawVtsOverrideButtons(HoVtsDebugSession session, string name, bool input, bool overridden, float value)
        {
            Rect block = HoConstraintEditorControls.Next(96); const float cell = 22;
            if (HoConstraintEditorControls.SegmentButton(new Rect(block.x, block.y, cell, block.height), "不", !overridden, "交还给角色规则")) { if (input) session?.ReleaseInput(name); else session?.ReleaseOutput(name); }
            if (HoConstraintEditorControls.SegmentButton(new Rect(block.x + 23, block.y, cell, block.height), "-1", overridden && Mathf.Abs(value + 1) < .0001f, "覆盖成 -1")) SetVtsPin(session, name, input, -1);
            if (HoConstraintEditorControls.SegmentButton(new Rect(block.x + 46, block.y, cell, block.height), "0", overridden && Mathf.Abs(value) < .0001f, "覆盖成 0")) SetVtsPin(session, name, input, 0);
            if (HoConstraintEditorControls.SegmentButton(new Rect(block.x + 69, block.y, cell, block.height), "1", overridden && Mathf.Abs(value - 1) < .0001f, "覆盖成 1")) SetVtsPin(session, name, input, 1);
        }

        private static void SetVtsPin(HoVtsDebugSession session, string name, bool input, float value) { if (session == null) return; if (input) session.PinInput(name, value); else session.PinOutput(name, value); }

        private static void DrawDiagnose(GameObject target, HoVtsParameterReceiver receiver)
        {
            string summary = !HoFaceInputHub.Connected ? "未连接" : HoFaceInputHub.LastFrameTime == 0 ? "等待响应" : "正常";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref diagnoseExpanded, "排查", summary, HoConstraintEditorTheme.AccentDebug)) return;
            using (HoConstraintEditorControls.Card())
            {
                DrawFirewallRow(); DrawWaitingHint(); DrawPacketStats(receiver);
                if (!string.IsNullOrEmpty(HoFaceInputHub.ConnectionError)) EditorGUILayout.HelpBox(HoFaceInputHub.ConnectionError, MessageType.Error);
                foreach (var source in HoFaceInputHub.Sources)
                    if (!string.IsNullOrEmpty(source.Error)) EditorGUILayout.HelpBox(source.Error, MessageType.Error);
                if (receiver != null)
                {
                    if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
                    HoConstraintEditorControls.Caption("接收器：" + (receiver.HasFrame ? receiver.Source : "没有当前帧") + " · 原始键/处理后 " + receiver.RawCount + "/" + receiver.ValueCount);
                }
                if (!string.IsNullOrEmpty(HoVtsDebugHost.Error)) EditorGUILayout.HelpBox(HoVtsDebugHost.Error, MessageType.Error);
                if (target != null && receiver != null)
                {
                    foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true)) if (controller.ParameterReceiver == receiver) HoConstraintEditorControls.Caption("表情控制器：" + controller.Status + " · 绑定 " + controller.BindingCount);
                    foreach (var pose in target.GetComponentsInChildren<HoVtsLookAt>(true)) if (pose.ParameterReceiver == receiver) HoConstraintEditorControls.Caption("头眼姿态：" + pose.Status);
                }
            }
        }

        private static void DrawFirewallRow()
        {
            if (!HoFaceFirewall.Supported) return;
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("防火墙", HoConstraintEditorTheme.LabelWidthSm, "手机 UDP 回包属于入站流量；没有 Unity 入站许可就收不到。"); HoConstraintEditorControls.Pill(HoFaceFirewall.Exists ? "已放行" : "未放行", HoFaceFirewall.Exists); HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(HoFaceFirewall.Busy)) { if (HoConstraintEditorControls.Button("授予权限", "改防火墙会弹一次 UAC。", true)) HoFaceFirewall.Grant(); using (new EditorGUI.DisabledScope(!HoFaceFirewall.Exists)) if (HoConstraintEditorControls.Button("撤销", null, false, 44)) HoFaceFirewall.Revoke(); }
                HoConstraintEditorControls.Flex();
            }
            if (!string.IsNullOrEmpty(HoFaceFirewall.Status)) HoConstraintEditorControls.Caption(HoFaceFirewall.Status);
            if (!string.IsNullOrEmpty(HoFaceFirewall.LastLog)) { logExpanded = HoConstraintEditorControls.InlineFoldout(logExpanded, "提权脚本日志", "失败原因一般在这里。"); if (logExpanded) EditorGUILayout.TextArea(HoFaceFirewall.LastLog, GUILayout.MinHeight(90)); }
        }

        private static void DrawWaitingHint()
        {
            if (!HoFaceInputHub.Connected || HoFaceInputHub.LastFrameTime != 0) return;
            long rejected = TotalRejected(); string source = RejectedSource();
            if (rejected > 0 && !string.IsNullOrEmpty(source))
            {
                using (HoConstraintEditorControls.Row()) { HoConstraintEditorControls.Caption("收到 " + rejected + " 个包，来源是 " + source + "（与填写的不一致）"); HoConstraintEditorControls.Gap(); if (HoConstraintEditorControls.Button("改成这个地址", "把启用中的手机源改成最近收到包的地址。", true)) { foreach (var entry in HoFaceInputEnvironment.instance.sources) if (entry != null && entry.enabled) entry.phoneIp = source; HoFaceInputEnvironment.instance.Persist(); } }
                return;
            }
            if (HoFaceClock.Now - HoFaceInputHub.ConnectStartedAt < 3) return;
            using (HoConstraintEditorControls.Row()) { if (HoFaceFirewall.Supported && !HoFaceFirewall.Exists) { HoConstraintEditorControls.Caption("3 秒没收到包，最可能是防火墙没放行"); HoConstraintEditorControls.Gap(); if (HoConstraintEditorControls.Button("授予权限", "改防火墙会弹一次 UAC。", true)) HoFaceFirewall.Grant(); } else hintExpanded = HoConstraintEditorControls.InlineFoldout(hintExpanded, "3 秒没收到包"); }
            if (!hintExpanded || !HoFaceFirewall.Exists) return;
            using (HoConstraintEditorControls.Indent()) { HoConstraintEditorControls.Caption("· 检查手机与电脑是否同网段"); HoConstraintEditorControls.Caption("· 确认 VTS App 已打开 3rd Party PC Clients"); HoConstraintEditorControls.Caption("· 确认 Wi-Fi 没被蜂窝网络或 VPN 分流"); }
        }

        private static void DrawPacketStats(HoVtsParameterReceiver receiver)
        {
            long packets = 0, invalid = 0, rejected = 0, replaced = 0, requests = 0;
            foreach (var source in HoFaceInputHub.Sources) { packets += source.Packets; invalid += source.Invalid; rejected += source.Rejected; replaced += source.Replaced; requests += source.Requests; }
            using (HoConstraintEditorControls.Row(true)) HoConstraintEditorControls.Caption("包 " + packets + "　无效 " + invalid + "　其他来源 " + rejected + "　丢旧帧 " + replaced + (requests > 0 ? "　请求 " + requests : ""));
            foreach (var source in HoFaceInputHub.Sources) using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label(source.DisplayName, HoConstraintEditorTheme.LabelWidthSm, source.Hint); double age = source.LastFrameTime > 0 ? HoFaceClock.Now - source.LastFrameTime : double.MaxValue; HoConstraintEditorControls.Caption(age > 1 ? (source.LastFrameTime > 0 ? "断流 " + age.ToString("F1") + " 秒" : "还没收到包") : "接收中"); HoConstraintEditorControls.Flex(); if (source.LastFrameTime > 0) HoConstraintEditorControls.Caption("来源 " + source.Sender); }
            var merged = HoFaceInputHub.MergedValues; HoConstraintEditorControls.Caption("合并后有 " + merged.Count + " 个线名" + (merged.ContainsKey("FaceFound") ? "　脸在 " + (merged["FaceFound"] > .5f ? "是" : "否") : ""));
            if (receiver == null) return;
            DrawPose(receiver, "头姿", "headRotX", "headRotY", "headRotZ"); DrawPose(receiver, "头位", "headPosX", "headPosY", "headPosZ"); DrawPose(receiver, "左眼", "eyeLeftX", "eyeLeftY", "eyeLeftZ"); DrawPose(receiver, "右眼", "eyeRightX", "eyeRightY", "eyeRightZ");
        }

        private static void DrawPose(HoVtsParameterReceiver receiver, string label, params string[] names)
        {
            using (HoConstraintEditorControls.Row(true)) { HoConstraintEditorControls.Label(label, HoConstraintEditorTheme.LabelWidthSm, "从配置输入行读出的规范头姿/眼姿值。"); var text = new StringBuilder(); foreach (string name in names) { float value = FindInput(receiver, name); text.Append(name.Substring(name.Length - 1)).Append(" ").Append(float.IsNaN(value) ? "—" : value.ToString("F2")).Append("  "); } HoConstraintEditorControls.Caption(text.ToString().TrimEnd()); }
        }

        private static float FindInput(HoVtsParameterReceiver receiver, string name)
        {
            var rows = receiver.Middleware?.inputs; if (rows == null) return float.NaN;
            for (int i = 0; i < rows.Count; i++) if (rows[i] != null && rows[i].parameter == name && receiver.TryReadInputRow(i, out _, out float value, out _)) return value;
            return float.NaN;
        }

        private static HoVtsDebugSession CurrentSession(HoVtsParameterReceiver receiver) => HoVtsDebugHost.Running && HoVtsDebugHost.Session.Receiver == receiver ? HoVtsDebugHost.Session : null;
        private static HoVtsParameterReceiver FindReceiver(GameObject target, out HoVtsParameterReceiver[] receivers) { receivers = target == null ? new HoVtsParameterReceiver[0] : target.GetComponentsInChildren<HoVtsParameterReceiver>(true); return receivers.Length == 1 ? receivers[0] : null; }
        private static int CountNamed(List<HoFaceOutput> rows) { int count = 0; foreach (var row in rows) if (row != null && !string.IsNullOrEmpty(row.parameter)) count++; return count; }
        private static bool Matches(string value, string query) => string.IsNullOrEmpty(query) || (value ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        private static void DrawMeter(float value, bool active) { Rect bar = HoConstraintEditorControls.NextFlexible(40); if (active && value >= 0 && value <= 1) HoConstraintEditorControls.Meter(bar, value, 0, 1, HoConstraintEditorTheme.AccentDriver); else if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(bar, HoConstraintEditorTheme.WellColor); }
        private static GUIStyle WarningStyle() { var style = new GUIStyle(HoConstraintEditorTheme.Value); style.normal.textColor = HoConstraintEditorTheme.WarningColor; return style; }
        private static long TotalRejected() { long total = 0; foreach (var source in HoFaceInputHub.Sources) total += source.Rejected; return total; }
        private static string RejectedSource() { foreach (var source in HoFaceInputHub.Sources) if (source.Rejected > 0 && !string.IsNullOrEmpty(source.RejectedSource)) return source.RejectedSource; return string.Empty; }
        private static IEnumerable<string> GetLocalAddresses()
        {
            try { var result = new List<string>(); foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())) if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) result.Add(address.ToString()); return result.Count == 0 ? new[] { "—" } : result; }
            catch { return new[] { "—" }; }
        }
    }
}
