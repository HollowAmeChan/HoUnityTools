using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Editor.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    internal static class HoVtsDebugHost
    {
        public static HoVtsDebugSession Session { get; private set; }
        public static HoVtsTraceRecorder Recorder { get; private set; }
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
            Recorder?.Stop();
            try { Recorder = new HoVtsTraceRecorder(Session, interval, seconds); Error = null; }
            catch (Exception error) { Error = error.Message; }
        }
        public static void Stop()
        {
            Recorder?.Stop("session-stopped");
            Session?.Dispose();
            Recorder = null; Session = null; Error = null;
        }
    }

    /// <summary>Component-mode view rendered inside the existing HoFaceTrackingWindow.</summary>
    internal static class HoVtsComponentDebugView
    {
        private static int tab;
        private static string search = string.Empty;
        private static bool autoStart = true;
        private static float recordSeconds = 5f, recordInterval;
        private static int isolationChoice;
        private static Vector2 scroll;
        private static bool componentsExpanded = true;
        private static bool profileExpanded = true, diagnoseExpanded;

        public static void Draw(HoFaceDebugSettings settings, GameObject initialTarget = null)
        {
            var target = initialTarget != null ? initialTarget : settings.Character();
            using (var page = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = page.scrollPosition;
                HoConstraintEditorControls.Title("Ho VTS 角色调试", target == null ? "未选择对象" : target.name,
                    ("输入", HoFaceInputHub.Connected), ("会话", HoVtsDebugHost.Running));
                using (HoConstraintEditorControls.Card())
                {
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("调试对象", HoConstraintEditorTheme.LabelWidth,
                            "只选择角色对象；规则、控制器和姿态引用从角色组件读取。");
                        EditorGUI.BeginChangeCheck();
                        var picked = (GameObject)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(90), target, typeof(GameObject), true);
                        if (EditorGUI.EndChangeCheck())
                        {
                            HoVtsDebugHost.Stop(); settings.SetCharacter(picked); settings.Save();
                            target = picked;
                        }
                    }
                }
                var receivers = target != null ? target.GetComponentsInChildren<HoVtsParameterReceiver>(true) : new HoVtsParameterReceiver[0];
                var receiver = receivers.Length == 1 ? receivers[0] : null;
                using (HoConstraintEditorControls.Card())
                using (HoConstraintEditorControls.Row(true))
                {
                    autoStart = HoConstraintEditorControls.Toggle("自动驱动", autoStart, "进入播放自动开始组件会话。");
                    HoConstraintEditorControls.Gap(8);
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || receiver == null || HoVtsDebugHost.Running))
                        if (HoConstraintEditorControls.Button("开始调试", "开始角色组件调试会话。", true, 90)) HoVtsDebugHost.Start(target);
                    HoConstraintEditorControls.Gap(6);
                    using (new EditorGUI.DisabledScope(!HoVtsDebugHost.Running))
                        if (HoConstraintEditorControls.Button("停止并交还", "释放输入和写入对象。", false, 90)) HoVtsDebugHost.Stop();
                    HoConstraintEditorControls.Gap(6);
                    using (new EditorGUI.DisabledScope(receiver == null))
                        if (HoConstraintEditorControls.Button("编辑角色规则", "打开组件规则草稿编辑器。", false, 90)) HoFaceProfileWindow.Open(receiver);
                }
                if (target != null && receiver == null)
                    EditorGUILayout.HelpBox(receivers.Length == 0 ? "对象上没有 HoVtsParameterReceiver" : "对象下有多个接收器。", MessageType.Warning);
                if (!string.IsNullOrEmpty(HoVtsDebugHost.Error)) EditorGUILayout.HelpBox(HoVtsDebugHost.Error, MessageType.Error);
                DrawInputConnection();
                if (receiver == null) return;
                HoConstraintEditorControls.Pill(!receiver.HasFrame ? "等待输入" : receiver.InputExpired ? "输入超时" : receiver.Tracked ? "追踪中" : "未追踪", receiver.Tracked);
                componentsExpanded = HoConstraintEditorSectionGui.DrawSectionHeader(ref componentsExpanded, "角色组件", "自动发现", HoConstraintEditorTheme.AccentDriver);
                if (componentsExpanded) DrawComponents(target, receiver);
                DrawProfile(receiver);
                DrawRecordControls();
                tab = GUILayout.Toolbar(tab, new[] { "原始输入", "规则输入", "规则输出" });
                search = EditorGUILayout.TextField("筛选", search);
                var session = HoVtsDebugHost.Running && HoVtsDebugHost.Session.Target == target ? HoVtsDebugHost.Session : null;
                using (new EditorGUI.DisabledScope(session == null)) if (GUILayout.Button("清除全部临时覆盖")) session?.ClearPins();
                if (tab == 2) DrawIsolation(session, receiver);
                DrawValues(session, receiver);
                DrawDiagnose(target, receiver);
            }
        }

        private static void DrawProfile(HoVtsParameterReceiver receiver)
        {
            string summary = receiver.Middleware == null ? "规则未加载" : "输入行 " + receiver.Middleware.inputs.Count + " · 输出行 " + receiver.Middleware.outputs.Count;
            profileExpanded = EditorGUILayout.Foldout(profileExpanded, "配置详情 · " + summary, true);
            if (!profileExpanded || receiver.Middleware == null) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("输入行", EditorStyles.miniBoldLabel);
                foreach (var row in receiver.Middleware.inputs) DrawProfileRow(row);
                EditorGUILayout.LabelField("输出行", EditorStyles.miniBoldLabel);
                foreach (var row in receiver.Middleware.outputs) DrawProfileRow(row);
            }
        }

        private static void DrawProfileRow(HoFaceOutput row)
        {
            if (row == null) return;
            string mods = row.modifiers == null || row.modifiers.Count == 0 ? "—" : row.modifiers.Count + " 个修饰符";
            EditorGUILayout.LabelField(row.parameter ?? "", (row.expression ?? "") + "  ·  " + mods, EditorStyles.miniLabel);
        }

        private static void DrawDiagnose(GameObject target, HoVtsParameterReceiver receiver)
        {
            diagnoseExpanded = HoConstraintEditorSectionGui.DrawSectionHeader(ref diagnoseExpanded, "排查", "授权 · 端口 · 包统计", HoConstraintEditorTheme.AccentDebug);
            if (!diagnoseExpanded) return;
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("防火墙", HoConstraintEditorTheme.LabelWidthSm);
                    if (!HoFaceFirewall.Supported) HoConstraintEditorControls.Caption("当前系统不支持自动检查");
                    else
                    {
                        HoConstraintEditorControls.Pill(HoFaceFirewall.Exists ? "已放行" : "未放行", HoFaceFirewall.Exists);
                        HoConstraintEditorControls.Gap(6);
                        using (new EditorGUI.DisabledScope(HoFaceFirewall.Busy))
                            if (HoConstraintEditorControls.Button("授予权限", "为当前 Unity 编辑器放行入站 UDP。", true, 72)) HoFaceFirewall.Grant();
                        using (new EditorGUI.DisabledScope(!HoFaceFirewall.Exists || HoFaceFirewall.Busy))
                            if (HoConstraintEditorControls.Button("撤销", "撤销当前规则。", false, 42)) HoFaceFirewall.Revoke();
                    }
                }
                if (!string.IsNullOrEmpty(HoFaceFirewall.Status)) HoConstraintEditorControls.Caption(HoFaceFirewall.Status);
                if (!string.IsNullOrEmpty(HoFaceFirewall.LastLog)) HoConstraintEditorControls.CaptionTrim(HoFaceFirewall.LastLog, 420);
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("输入源", HoConstraintEditorTheme.LabelWidthSm);
                    HoConstraintEditorControls.Caption(HoFaceInputHub.Connected ? "已连接 · " + HoFaceInputHub.SourceCount + " 条源" : "未连接");
                    HoConstraintEditorControls.Flex();
                    long packets = 0, invalid = 0, rejected = 0;
                    foreach (var source in HoFaceInputHub.Sources) { packets += source.Packets; invalid += source.Invalid; rejected += source.Rejected; }
                    HoConstraintEditorControls.CaptionTrim("包 " + packets + " · 无效 " + invalid + " · 其他来源 " + rejected + " · " + HoFaceInputHub.LastFrameTime.ToString("F3"), 260);
                }
                double age = HoFaceInputHub.LastFrameTime > 0 ? HoFaceClock.Now - HoFaceInputHub.LastFrameTime : double.PositiveInfinity;
                HoConstraintEditorControls.Caption("最近输入：" + (double.IsInfinity(age) ? "从未收到" : age.ToString("F2") + " 秒前"));
                HoConstraintEditorControls.Caption("接收器：" + (receiver.HasFrame ? receiver.Source : "没有当前帧") + " · 原始键/处理后 " + receiver.RawCount + "/" + receiver.ValueCount);
                if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
                if (target != null)
                    foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true))
                        if (controller.ParameterReceiver == receiver) HoConstraintEditorControls.Caption("绑定动画属性：" + controller.BindingCount);
            }
        }

        private static void DrawInputConnection()
        {
            var environment = HoFaceInputEnvironment.instance;
            if (environment.sources.Count == 0) return;
            var entry = environment.sources[0];
            using (new EditorGUILayout.HorizontalScope())
            {
                entry.phoneIp = EditorGUILayout.TextField("手机 IPv4", entry.phoneIp);
                entry.localPort = EditorGUILayout.IntField("端口", entry.localPort, GUILayout.Width(120));
                if (GUILayout.Button(HoFaceInputHub.Connected ? "断开" : "连接", GUILayout.Width(48)))
                {
                    if (HoFaceInputHub.Connected) HoFaceInputHub.Disconnect(); else HoFaceInputHub.Connect();
                }
            }
            if (!string.IsNullOrEmpty(HoFaceInputHub.ConnectionError)) EditorGUILayout.HelpBox(HoFaceInputHub.ConnectionError, MessageType.Warning);
        }

        private static void DrawRecordControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("录样 秒/间隔", GUILayout.Width(88));
                recordSeconds = EditorGUILayout.FloatField(recordSeconds, GUILayout.Width(48));
                recordInterval = EditorGUILayout.FloatField(recordInterval, GUILayout.Width(48));
                bool active = HoVtsDebugHost.Recorder != null && HoVtsDebugHost.Recorder.Recording;
                using (new EditorGUI.DisabledScope(!HoVtsDebugHost.Running))
                    if (GUILayout.Button(active ? "停止录样" : "开始录样"))
                    {
                        if (active) HoVtsDebugHost.Recorder.Stop(); else HoVtsDebugHost.Record(recordSeconds, recordInterval);
                    }
                if (HoVtsDebugHost.Recorder != null) GUILayout.Label(HoVtsDebugHost.Recorder.Samples + " 帧", GUILayout.Width(55));
            }
        }

        private static void DrawValues(HoVtsDebugSession session, HoVtsParameterReceiver receiver)
        {
            var raw = new Dictionary<string, float>(); receiver.CopyRawTo(raw);
            if (tab == 0)
            {
                foreach (var pair in raw) if (Matches(pair.Key)) EditorGUILayout.LabelField(pair.Key, pair.Value.ToString("G7"));
                return;
            }
            var rows = tab == 1 ? receiver.Middleware?.inputs : receiver.Middleware?.outputs;
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; if (row == null || !Matches(row.parameter)) continue;
                float a, b; bool fresh = false;
                bool found = tab == 1 ? receiver.TryReadInputRow(i, out a, out b, out fresh) : receiver.TryReadOutputRow(i, out a, out b);
                using (new EditorGUILayout.HorizontalScope())
                {
                    receiver.TryReadRuleStages(tab == 1, i, out float expression, out float curve, out _);
                    GUILayout.Label(new GUIContent(row.parameter, "表达式 " + expression + " → 曲线 " + curve), GUILayout.ExpandWidth(true));
                    GUILayout.Label(found ? a.ToString("G5") : "—", GUILayout.Width(64));
                    GUILayout.Label(found ? b.ToString("G5") : "—", GUILayout.Width(64));
                    DrawPin(session, row.parameter, found ? b : row.defaultValue);
                }
            }
        }

        private static void DrawPin(HoVtsDebugSession session, string name, float value)
        {
            var pins = session == null ? null : tab == 1 ? session.InputPins : session.OutputPins;
            float pinned = 0; bool active = pins != null && pins.TryGetValue(name, out pinned); if (active) value = pinned;
            bool next = GUILayout.Toggle(active, "覆盖", GUILayout.Width(47));
            if (next)
            {
                float edited = EditorGUILayout.FloatField(value, GUILayout.Width(60));
                if (session != null) { if (tab == 1) session.PinInput(name, edited); else session.PinOutput(name, edited); }
            }
            else if (active && session != null) { if (tab == 1) session.ReleaseInput(name); else session.ReleaseOutput(name); }
        }

        private static void DrawIsolation(HoVtsDebugSession session, HoVtsParameterReceiver receiver)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                isolationChoice = EditorGUILayout.Popup(isolationChoice, new[] { "当前筛选输出", "嘴", "眉", "脸颊", "眼睑", "眼球" });
                var selected = new List<string>(); var scope = new List<string>();
                var region = new[] { HoFaceRegion.All, HoFaceRegion.Mouth, HoFaceRegion.Brows, HoFaceRegion.Cheeks, HoFaceRegion.Eyelids, HoFaceRegion.Gaze }[isolationChoice];
                foreach (var row in receiver.Middleware.outputs)
                {
                    if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                    scope.Add(row.parameter); string shape = row.parameter.StartsWith("ARKit/", StringComparison.Ordinal) ? row.parameter.Substring(6) : row.parameter;
                    int index = HoFaceTrackingChannels.IndexOf(shape);
                    if (isolationChoice == 0 ? Matches(row.parameter) : index >= 0 && (HoFaceTrackingChannels.Region(HoFaceTrackingChannels.Names[index]) & region) != 0) selected.Add(row.parameter);
                }
                using (new EditorGUI.DisabledScope(session == null || selected.Count == 0)) if (GUILayout.Button("仅此范围")) session.IsolateOutputs(selected, scope);
                using (new EditorGUI.DisabledScope(session == null || session.IsolationOverrides.Count == 0)) if (GUILayout.Button("恢复")) session.RestoreIsolation();
            }
        }
        private static void DrawComponents(GameObject target, HoVtsParameterReceiver receiver)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("接收器", receiver, typeof(HoVtsParameterReceiver), true);
                foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true)) if (controller.ParameterReceiver == receiver) EditorGUILayout.LabelField("控制器", controller.Status);
                foreach (var pose in target.GetComponentsInChildren<HoVtsLookAt>(true)) if (pose.ParameterReceiver == receiver) EditorGUILayout.LabelField("姿态", pose.Status);
            }
        }
        private static bool Matches(string name) => string.IsNullOrEmpty(search) || (name ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
