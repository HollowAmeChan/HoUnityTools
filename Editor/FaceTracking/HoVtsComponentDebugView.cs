using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
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

        public static void Draw(HoFaceDebugSettings settings)
        {
            var target = settings.Character();
            using (var page = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = page.scrollPosition;
                EditorGUILayout.LabelField("Ho VTS 角色调试", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var picked = (GameObject)EditorGUILayout.ObjectField("调试对象", target, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                {
                    HoVtsDebugHost.Stop(); settings.SetCharacter(picked); settings.Save();
                    target = picked;
                }
                var receivers = target != null ? target.GetComponentsInChildren<HoVtsParameterReceiver>(true) : new HoVtsParameterReceiver[0];
                var receiver = receivers.Length == 1 ? receivers[0] : null;
                using (new EditorGUILayout.HorizontalScope())
                {
                    autoStart = EditorGUILayout.ToggleLeft("进入播放自动开始", autoStart, GUILayout.Width(155));
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || receiver == null || HoVtsDebugHost.Running))
                        if (GUILayout.Button("开始调试")) HoVtsDebugHost.Start(target);
                    using (new EditorGUI.DisabledScope(!HoVtsDebugHost.Running))
                        if (GUILayout.Button("停止并交还")) HoVtsDebugHost.Stop();
                    using (new EditorGUI.DisabledScope(receiver == null))
                        if (GUILayout.Button("编辑角色规则")) HoFaceProfileWindow.Open(receiver);
                }
                if (target != null && receiver == null)
                    EditorGUILayout.HelpBox(receivers.Length == 0 ? "对象上没有 HoVtsParameterReceiver" : "对象下有多个接收器。", MessageType.Warning);
                if (!string.IsNullOrEmpty(HoVtsDebugHost.Error)) EditorGUILayout.HelpBox(HoVtsDebugHost.Error, MessageType.Error);
                DrawInputConnection();
                if (receiver == null) return;
                EditorGUILayout.LabelField("状态", !receiver.HasFrame ? "等待输入" : receiver.InputExpired ? "输入超时 · 已回中性" : receiver.Tracked ? "追踪中" : "未追踪 / 手动调试");
                componentsExpanded = EditorGUILayout.Foldout(componentsExpanded, "角色组件", true);
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
            diagnoseExpanded = EditorGUILayout.Foldout(diagnoseExpanded, "排查", true);
            if (!diagnoseExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("输入源", HoFaceInputHub.Connected ? "已连接 · " + HoFaceInputHub.SourceCount + " 条源" : "未连接");
                double age = HoFaceInputHub.LastFrameTime > 0 ? HoFaceClock.Now - HoFaceInputHub.LastFrameTime : double.PositiveInfinity;
                EditorGUILayout.LabelField("最近输入", double.IsInfinity(age) ? "从未收到" : age.ToString("F2") + " 秒前");
                EditorGUILayout.LabelField("接收器", receiver.HasFrame ? receiver.Source : "没有当前帧");
                EditorGUILayout.LabelField("原始键 / 处理后", receiver.RawCount + " / " + receiver.ValueCount);
                if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
                if (target != null)
                    foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true))
                        if (controller.ParameterReceiver == receiver) EditorGUILayout.LabelField("绑定动画属性", controller.BindingCount.ToString());
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
