using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public static class HoVtsDebugHost
    {
        public static HoVtsDebugSession Session { get; private set; }
        public static string Error { get; private set; }
        public static HoVtsTraceRecorder Recorder { get; private set; }
        public static void Record(double seconds, double interval)
        {
            Recorder?.Stop();
            try { Recorder = new HoVtsTraceRecorder(Session, interval, seconds); Error = null; }
            catch (Exception error) { Error = error.Message; }
        }
        public static bool Running => Session != null && !Session.Disposed;
        public static bool Start(GameObject target)
        {
            if (Running && Session.Target == target) return true;
            Stop();
            try { Session = new HoVtsDebugSession(target); Error = null; return true; }
            catch (Exception error) { Error = error.Message; return false; }
        }
        public static void Stop() { Recorder?.Stop("session-stopped"); Session?.Dispose(); Session = null; Error = null; }
    }

    public sealed class HoVtsDebugWindow : EditorWindow
    {
        [SerializeField] private GameObject target;
        [SerializeField] private bool autoStart = true;
        private readonly Dictionary<string, float> raw = new Dictionary<string, float>(StringComparer.Ordinal);
        private Vector2 scroll;
        private string search = "";
        private int tab;
        private bool componentsExpanded;
        private double nextRepaint;
        private float recordSeconds = 5, recordInterval;

        [MenuItem("HoUnityTools/面捕/调试面板", false, 10)]
        public static void Open() => GetWindow<HoVtsDebugWindow>("Ho VTS 调试");

        private void OnEnable()
        {
            minSize = new Vector2(460, 380);
            if (HoVtsDebugHost.Running) target = HoVtsDebugHost.Session.Target;
            EditorApplication.update += Refresh;
            EditorApplication.playModeStateChanged += PlayState;
        }
        private void OnDisable()
        {
            EditorApplication.update -= Refresh;
            EditorApplication.playModeStateChanged -= PlayState;
            // The host owns the session; closing this view does not stop the character.
        }
        private void Refresh()
        {
            if (EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + .1;
            Repaint();
        }
        private void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && autoStart && target != null) HoVtsDebugHost.Start(target);
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (GameObject)EditorGUILayout.ObjectField("调试对象", target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                bool restart = HoVtsDebugHost.Running;
                HoVtsDebugHost.Stop(); target = picked;
                if (restart && target != null && EditorApplication.isPlaying) HoVtsDebugHost.Start(target);
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
            DrawConnection();
            if (target != null && receiver == null)
                EditorGUILayout.HelpBox(receivers.Length == 0 ? "对象上没有 HoVtsParameterReceiver" : "存在多个接收器，请缩小调试对象范围。", MessageType.Warning);
            if (!string.IsNullOrEmpty(HoVtsDebugHost.Error)) EditorGUILayout.HelpBox(HoVtsDebugHost.Error, MessageType.Error);
            if (receiver != null)
            {
                EditorGUILayout.LabelField("角色状态", !receiver.isActiveAndEnabled ? "组件未启用" : !receiver.HasFrame ? "等待输入"
                    : receiver.InputExpired ? "输入超时 · 已回中性" : receiver.Tracked ? "追踪中" : "未追踪 / 手动调试");
                if (!string.IsNullOrEmpty(receiver.RuleError)) EditorGUILayout.HelpBox(receiver.RuleError, MessageType.Error);
                componentsExpanded = EditorGUILayout.Foldout(componentsExpanded, "角色组件", true);
                if (componentsExpanded) DrawComponents(receiver);
            }
            tab = GUILayout.Toolbar(tab, new[] { "原始输入", "规则输入", "规则输出" });
            search = EditorGUILayout.TextField("筛选", search);
            var session = HoVtsDebugHost.Running && HoVtsDebugHost.Session.Target == target ? HoVtsDebugHost.Session : null;
            using (new EditorGUI.DisabledScope(session == null))
                if (GUILayout.Button("清除全部临时覆盖")) session?.ClearPins();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("录样 秒 / 间隔", GUILayout.Width(95));
                recordSeconds = EditorGUILayout.FloatField(recordSeconds, GUILayout.Width(48));
                recordInterval = EditorGUILayout.FloatField(recordInterval, GUILayout.Width(48));
                bool recording = HoVtsDebugHost.Recorder != null && HoVtsDebugHost.Recorder.Recording;
                using (new EditorGUI.DisabledScope(session == null))
                    if (GUILayout.Button(recording ? "停止录样" : "开始录样"))
                    {
                        if (recording) HoVtsDebugHost.Recorder.Stop(); else HoVtsDebugHost.Record(recordSeconds, recordInterval);
                    }
                if (HoVtsDebugHost.Recorder != null && GUILayout.Button("定位文件")) EditorUtility.RevealInFinder(HoVtsDebugHost.Recorder.FilePath);
            }
            if (HoVtsDebugHost.Recorder != null) EditorGUILayout.LabelField("录样", HoVtsDebugHost.Recorder.Samples + " 帧 · "
                + (HoVtsDebugHost.Recorder.Recording ? "进行中" : HoVtsDebugHost.Recorder.StopReason));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0)
            {
                raw.Clear();
                if (session != null) receiver.CopyRawTo(raw);
                else foreach (var pair in HoFaceInputHub.MergedValues) raw[pair.Key] = pair.Value;
                foreach (var pair in raw) if (Matches(pair.Key))
                    EditorGUILayout.LabelField(pair.Key, pair.Value.ToString("G7"));
            }
            else if (receiver != null)
            {
                var rows = tab == 1 ? receiver.Middleware?.inputs : receiver.Middleware?.outputs;
                EditorGUILayout.LabelField(tab == 1 ? "参数                           配置值 / 有效值" : "参数                           求值 / 发布值", EditorStyles.miniLabel);
                if (rows != null) for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row == null || string.IsNullOrEmpty(row.parameter) || !Matches(row.parameter)) continue;
                    float configured, effective;
                    bool present = tab == 1 ? receiver.TryReadInputRow(i, out configured, out effective, out _)
                        : receiver.TryReadOutputRow(i, out configured, out effective);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        receiver.TryReadRuleStages(tab == 1, i, out float expression, out float curve, out _);
                        GUILayout.Label(new GUIContent(row.parameter, row.expression + "\n表达式 " + expression + " → 曲线 " + curve
                            + " → 修饰 " + configured + " → 发布 " + effective), GUILayout.MinWidth(140), GUILayout.ExpandWidth(true));
                        GUILayout.Label(present ? configured.ToString("G5") : "—", GUILayout.Width(62));
                        GUILayout.Label(present ? effective.ToString("G5") : "—", GUILayout.Width(62));
                        DrawPin(session, row.parameter, present ? effective : row.defaultValue);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Matches(string key) => string.IsNullOrEmpty(search) || key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        private void DrawPin(HoVtsDebugSession session, string name, float value)
        {
            var pins = session != null ? tab == 1 ? session.InputPins : session.OutputPins : null;
            float pinnedValue = 0;
            bool pinned = pins != null && pins.TryGetValue(name, out pinnedValue);
            if (pinned) value = pinnedValue;
            using (new EditorGUI.DisabledScope(session == null))
            {
                bool next = GUILayout.Toggle(pinned, "覆盖", GUILayout.Width(47));
                if (next)
                {
                    EditorGUI.BeginChangeCheck();
                    float edited = EditorGUILayout.FloatField(value, GUILayout.Width(60));
                    bool changed = EditorGUI.EndChangeCheck();
                    if ((!pinned || changed) && session != null)
                    {
                        if (tab == 1) session.PinInput(name, edited); else session.PinOutput(name, edited);
                    }
                }
                else
                {
                    GUILayout.Space(60);
                    if (pinned) { if (tab == 1) session.ReleaseInput(name); else session.ReleaseOutput(name); }
                }
            }
        }

        private void DrawConnection()
        {
            var environment = HoFaceInputEnvironment.instance;
            if (environment.sources.Count == 0) return;
            var entry = environment.sources[0];
            using (new EditorGUI.DisabledScope(HoFaceInputHub.Connected))
            {
                EditorGUI.BeginChangeCheck();
                entry.phoneIp = EditorGUILayout.TextField("手机 IPv4", entry.phoneIp);
                entry.localPort = EditorGUILayout.IntField("本机 UDP 端口", entry.localPort);
                if (EditorGUI.EndChangeCheck()) environment.Persist();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(HoFaceInputHub.Connected ? "输入已连接" : "输入未连接");
                if (GUILayout.Button(HoFaceInputHub.Connected ? "断开输入" : "连接输入"))
                {
                    if (HoFaceInputHub.Connected) HoFaceInputHub.Disconnect(); else HoFaceInputHub.Connect();
                }
            }
            if (!string.IsNullOrEmpty(HoFaceInputHub.ConnectionError)) EditorGUILayout.HelpBox(HoFaceInputHub.ConnectionError, MessageType.Warning);
        }

        private void DrawComponents(HoVtsParameterReceiver receiver)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("参数接收器", receiver, typeof(HoVtsParameterReceiver), true);
                foreach (var controller in target.GetComponentsInChildren<HoVtsController>(true))
                    if (controller.ParameterReceiver == receiver)
                    {
                        EditorGUILayout.ObjectField("表情控制器", controller.Controller, typeof(RuntimeAnimatorController), false);
                        EditorGUILayout.LabelField(controller.Status);
                    }
                foreach (var pose in target.GetComponentsInChildren<HoVtsLookAt>(true))
                    if (pose.ParameterReceiver == receiver) EditorGUILayout.LabelField("头眼姿态", pose.Status);
            }
        }
    }
}
