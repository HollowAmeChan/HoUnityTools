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
        private readonly System.Collections.Generic.List<string> localAddresses = new System.Collections.Generic.List<string>();
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
                    if (address.AddressFamily == AddressFamily.InterNetwork) localAddresses.Add(address.ToString());
                localIps = localAddresses.Count == 0 ? "未获取到网卡地址" : string.Join(" / ", localAddresses);
            }
            catch { localIps = "未获取到网卡地址"; }
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
            DrawFirewallRow();
            DrawWaitingDiagnostics(settings.phoneIp);
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

        /// <summary>
        /// Windows 防火墙那一行。手机的 UDP 回包是入站流量，而防火墙是按程序放行的 ——
        /// 没有这条规则就永远收不到。改规则需要管理员，所以按钮的作用是「把 UAC 叫出来 +
        /// 把命令写对」，不是绕过系统授权。
        /// </summary>
        private void DrawFirewallRow()
        {
            if (!HoFaceFirewall.Supported) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("防火墙入站规则", HoFaceFirewall.Exists ? "已配置" : "未配置", GUILayout.Width(240));
                using (new EditorGUI.DisabledScope(HoFaceFirewall.Busy))
                {
                    if (GUILayout.Button("授予权限（会弹 UAC）")) HoFaceFirewall.Grant();
                    using (new EditorGUI.DisabledScope(!HoFaceFirewall.Exists))
                        if (GUILayout.Button("撤销", GUILayout.Width(50))) HoFaceFirewall.Revoke();
                }
            }
            if (!string.IsNullOrEmpty(HoFaceFirewall.Status))
                EditorGUILayout.HelpBox(HoFaceFirewall.Status, MessageType.Info);
        }

        /// <summary>
        /// 「等待手机响应」这一句话把三种完全不同的故障混在一起了：手机根本没发、包在路上被丢了、
        /// 或者包来了但来源 IP 不对被我们拒了。这里按手上的证据分开说，并给出能直接执行的下一步。
        /// </summary>
        private void DrawWaitingDiagnostics(string phoneIp)
        {
            if (!HoFaceInputHub.Connected || HoFaceInputHub.LastFrameTime != 0) return;

            long rejected = HoFaceInputHub.Receiver.Rejected;
            string rejectedFrom = HoFaceInputHub.Receiver.RejectedSource;
            if (rejected > 0 && !string.IsNullOrEmpty(rejectedFrom))
            {
                EditorGUILayout.HelpBox(
                    "收到了 " + rejected + " 个来自 " + rejectedFrom + " 的数据包，但和你填的 " + phoneIp
                    + " 不一致，已被来源校验拒收。\n把「手机 IPv4」改成 " + rejectedFrom + " 就能连上；"
                    + "如果那不是你的手机，说明局域网里还有另一台设备在发面捕数据。",
                    MessageType.Warning);
                return;
            }

            double waited = IFacialMocapReceiver.Now - HoFaceInputHub.ConnectStartedAt;
            if (waited < 3)
            {
                EditorGUILayout.LabelField("已等待 " + waited.ToString("F1") + " 秒…", EditorStyles.miniLabel);
                return;
            }

            var text = new System.Text.StringBuilder();
            text.AppendLine("UDP 49983 已在监听 " + waited.ToString("F0") + " 秒，一个包都没收到。"
                + "端口是通的、握手命令也发出去了，所以断点在「手机 → 电脑」这一侧。按可能性排查：");
            text.AppendLine();
            string rule = "Unity " + Application.unityVersion + " Editor";
            text.AppendLine("① 最可能：Windows 防火墙拦了 Unity 的入站 UDP。别的程序（播放器、面捕桥接程序）"
                + "能直连，是因为它们各自有一条针对自己的入站「允许」规则 —— 防火墙是按程序放行的，"
                + "而 Unity.exe 的允许规则只覆盖 Domain，在「公用」网络上还额外有一条「阻止」规则。"
                + "点上面那个「授予权限」按钮即可，它只放行这个 Unity.exe 的 UDP " + IFacialMocapReceiver.Port
                + "，并且随时可以「撤销」。");
            text.AppendLine("不想用按钮、要手动执行的话是这两条，缺一不可 —— 只禁掉阻止规则的话，"
                + "公用网络没有命中任何规则，默认入站仍然是拒绝：");
            text.AppendLine("Get-NetFirewallRule -DisplayName '" + rule
                + "' -Direction Inbound | Where-Object Action -eq 'Block' | Disable-NetFirewallRule");
            text.AppendLine("New-NetFirewallRule -DisplayName '" + HoFaceFirewall.RuleName
                + "' -Direction Inbound -Action Allow -Protocol UDP -LocalPort " + IFacialMocapReceiver.Port
                + " -Profile Any -Program '" + EditorApplication.applicationPath + "'");
            text.AppendLine();

            string local = SameSubnetMatch(phoneIp);
            if (local != null)
            {
                text.AppendLine("② 网段对得上（本机 " + local + "，手机 " + phoneIp + "），先跳过。");
            }
            else if (localAddresses.Count > 0)
            {
                text.AppendLine("② 网段对不上：" + phoneIp + " 和本机 " + string.Join("、", localAddresses)
                    + " 不在同一个 /24。手机很可能连的不是这个 Wi-Fi。");
            }
            else
            {
                text.AppendLine("② 拿不到本机网卡地址，跳过网段检查。");
            }
            text.AppendLine();
            text.AppendLine("③ 手机侧：iFacialMocap 要停在前台；App 里如果填了「PC 的 IP」，要填 "
                + (local ?? localIps) + "，填成别的机器就会发到别处；"
                + "确认 Wi-Fi 没走蜂窝或 VPN 分流。");
            text.AppendLine();
            text.AppendLine("④ 其它程序抢占：如果网段和防火墙都没问题，确认没有别的面捕桥接程序在收同一份数据。");
            EditorGUILayout.HelpBox(text.ToString(), MessageType.Warning);
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

        private static void DrawPose(string label, float[] values)
        {
            if (values == null) return;
            EditorGUILayout.LabelField(label, string.Join(", ", Array.ConvertAll(values, v => v.ToString("F2"))), EditorStyles.wordWrappedMiniLabel);
        }
    }
}
