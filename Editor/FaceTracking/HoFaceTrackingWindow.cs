using System;
using System.Net;
using System.Net.Sockets;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public sealed class HoFaceTrackingWindow : EditorWindow
    {
        private Vector2 scroll;
        private string search = "";
        private string localIps = "";
        private HoFaceTrackingDebugger rig;
        private double lastRepaint, lastRateTime;
        private long lastPackets;
        private float packetRate;

        [MenuItem("HoUnityTools/面捕调试")]
        public static void ShowWindow() => GetWindow<HoFaceTrackingWindow>("Ho 面捕调试");

        private void OnEnable()
        {
            minSize = new Vector2(400, 440);
            EditorApplication.update += Refresh;
            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (address.AddressFamily == AddressFamily.InterNetwork) localIps += (localIps.Length == 0 ? "" : " / ") + address;
            }
            catch { localIps = "未获取到网卡地址"; }
        }
        private void OnDisable() => EditorApplication.update -= Refresh;
        private void Refresh()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRateTime >= 1)
            {
                long packets = HoFaceInputHub.Receiver.Packets;
                packetRate = Mathf.Max(0, (float)((packets - lastPackets) / (now - lastRateTime)));
                lastPackets = packets;
                lastRateTime = now;
            }
            if (now - lastRepaint < 0.1) return;
            lastRepaint = now;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("iFacialMocap 直连", EditorStyles.boldLabel);
            var settings = HoFaceConnectionSettings.instance;
            using (new EditorGUI.DisabledScope(HoFaceInputHub.Connected))
            {
                EditorGUI.BeginChangeCheck();
                settings.phoneIp = EditorGUILayout.TextField("手机 IPv4", settings.phoneIp);
                if (EditorGUI.EndChangeCheck()) settings.Persist();
            }
            EditorGUILayout.LabelField("电脑 IP", localIps, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(HoFaceInputHub.Connected ? "断开手机" : "连接手机", GUILayout.Height(28)))
            {
                if (HoFaceInputHub.Connected) HoFaceInputHub.Disconnect(); else HoFaceInputHub.Connect(settings.phoneIp.Trim());
            }
            double age = IFacialMocapReceiver.Now - HoFaceInputHub.LastFrameTime;
            string status = !HoFaceInputHub.Connected ? "已停止" : HoFaceInputHub.LastFrameTime == 0 ? "等待手机响应" : age > 1 ? "长时间未更新" : "正在接收";
            EditorGUILayout.LabelField(status + " · UDP 49983 · " + packetRate.ToString("F0") + " 包/秒");
            EditorGUILayout.LabelField("来源", HoFaceInputHub.Receiver.Sender);
            string error = string.IsNullOrEmpty(HoFaceInputHub.Receiver.Error) ? HoFaceInputHub.ConnectionError : HoFaceInputHub.Receiver.Error;
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox("手机和电脑需能通过局域网互通，App 保持前台。先看参数，再进入 Play Mode 驱动角色。关闭此窗口不停止连接；退出播放或重编译会断开。", MessageType.Info);

            rig = (HoFaceTrackingDebugger)EditorGUILayout.ObjectField("角色组件", rig, typeof(HoFaceTrackingDebugger), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("使用当前选择"))
                {
                    var go = Selection.activeGameObject;
                    if (go != null) rig = go.GetComponentInParent<HoFaceTrackingDebugger>() ?? go.GetComponentInChildren<HoFaceTrackingDebugger>();
                }
                if (GUILayout.Button("为所选角色添加组件") && Selection.activeGameObject != null)
                {
                    var go = Selection.activeGameObject;
                    rig = go.GetComponent<HoFaceTrackingDebugger>() ?? Undo.AddComponent<HoFaceTrackingDebugger>(go);
                    Selection.activeGameObject = rig.gameObject;
                }
            }
            if (rig != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("查看角色配置")) Selection.activeGameObject = rig.gameObject;
                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        if (GUILayout.Button(HoFaceInputHub.Session(rig) == null ? "开始驱动" : "停止驱动"))
                        { if (HoFaceInputHub.Session(rig) == null) HoFaceInputHub.Start(rig); else HoFaceInputHub.Stop(rig); }
                }
                if (!string.IsNullOrEmpty(HoFaceInputHub.Error(rig))) EditorGUILayout.HelpBox(HoFaceInputHub.Error(rig), MessageType.Error);
            }

            EditorGUILayout.Space();
            search = EditorGUILayout.TextField("参数筛选", search);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < HoFaceTrackingChannels.Names.Length; i++)
            {
                string name = HoFaceTrackingChannels.Names[i];
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool received = HoFaceInputHub.ReceivedAt[i] > 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(name, GUILayout.Width(170));
                    EditorGUILayout.LabelField(received ? (HoFaceInputHub.Raw[i] * 100f).ToString("F1") + " → " + HoFaceInputHub.Raw[i].ToString("F3") : "未收到");
                    if (received) EditorGUILayout.LabelField((IFacialMocapReceiver.Now - HoFaceInputHub.ReceivedAt[i]).ToString("F1") + "s", GUILayout.Width(45));
                }
            }
            var packet = HoFaceInputHub.LastPacket;
            if (packet != null)
            {
                EditorGUILayout.LabelField("本包字段", packet.ShapeCount + " 有效 / " + packet.UnknownCount + " 未知 / " + packet.InvalidCount + " 无效");
                EditorGUILayout.LabelField("手机姿态", "只监视，不驱动头部与眼骨");
                DrawPose("Head", packet.Head);
                DrawPose("LeftEye", packet.LeftEye);
                DrawPose("RightEye", packet.RightEye);
            }
            EditorGUILayout.LabelField("无效包 / 其他来源 / 跳过旧帧", HoFaceInputHub.Receiver.Invalid + " / " + HoFaceInputHub.Receiver.Rejected + " / " + HoFaceInputHub.Receiver.Replaced);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawPose(string label, float[] values)
        {
            if (values == null) return;
            EditorGUILayout.LabelField(label, string.Join(", ", Array.ConvertAll(values, v => v.ToString("F2"))), EditorStyles.wordWrappedMiniLabel);
        }
    }
}
