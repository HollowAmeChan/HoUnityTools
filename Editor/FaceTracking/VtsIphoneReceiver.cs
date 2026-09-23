using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **VTubeStudio iPhone App** 的接收端（官方 "3rd Party PC Clients"，JSON 协议）。
    ///
    /// 官方说明与载荷定义：<https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest>
    /// （README 原话："Apps like VSeeFace and VBridger use this." —— 这条就是 VB 走的那条。）
    ///
    /// 流程：
    /// 1. 手机端打开 `3rd Party PC Clients`，它在 iPhone 上开 UDP 监听（**默认 21412**）；
    /// 2. 我们往 `手机:21412` 发 JSON `{"messageType":"iOSTrackingDataRequest","time":秒,"sentBy":"名字","ports":[本机端口]}`，
    ///    `time` 只允许 **0.5–10 秒**，所以要每秒续一次（见 <see cref="OnTick"/>）；
    /// 3. 手机按帧回 JSON：时间戳 + `FaceFound` + 52 个形态键 + 头旋转/位置 + 左右眼旋转 + 屏幕热键。
    ///
    /// 本接收端**只交原样**：字段名照抄载荷里的名字（`Rotation` → `Rotation_x/y/z`），形态键数值也不做换算。
    /// 哪一段是"角度"、哪一段是"位置"、要不要 `* 0.0174533`，全写在中间层的输入行里。
    /// </summary>
    public sealed class VtsIphoneReceiver : HoFaceReceiverBase
    {
        /// <summary>iPhone 侧监听端口（官方默认值；App 界面上显示的那个）。</summary>
        public const int PhonePort = 21412;

        /// <summary>本机默认监听端口。**故意避开 49983**：这样 iFacialMocap 和 VTS 两条可以同时连着。</summary>
        public const int DefaultPort = 49984;

        /// <summary>官方允许 0.5–10 秒；我们要 5 秒、每秒续一次。</summary>
        public const float RequestSeconds = 5f;

        private const double RenewSeconds = 1.0;
        private const string AppName = "HoUnityTools";

        private double lastRequest;

        public override string DisplayName => "VTS 手机（3rd Party PC Clients）";
        public override string Hint => "VTS 手机版设置第一页底部打开「3rd Party PC Clients」（默认端口 " + PhonePort + "）；"
            + "我们是请求式收包，每秒向手机续一次 " + RequestSeconds.ToString("F0") + " 秒。";
        public override string Id => "vts-iphone";
        public override string Protocol => "VTS iPhone";
        public override int DefaultLocalPort => DefaultPort;

        protected override void OnStarted(UdpClient client, IPAddress phone)
        {
            lastRequest = 0;
            SendRequest(client, phone);   // 先要一次，别等第一个周期
        }

        protected override void OnTick(UdpClient client, IPAddress phone)
        {
            if (Now - lastRequest >= RenewSeconds) SendRequest(client, phone);
        }

        /// <summary>要数据。每次只买 <see cref="RequestSeconds"/> 秒，所以要定期续（官方要求）。</summary>
        private void SendRequest(UdpClient client, IPAddress phone)
        {
            var request = new Request
            {
                messageType = "iOSTrackingDataRequest",
                time = RequestSeconds,
                sentBy = AppName,
                ports = new[] { LocalPort }
            };
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            client.Send(payload, payload.Length, new IPEndPoint(phone, PhonePort));
            CountRequest();
            lastRequest = Now;
        }

        [Serializable]
        private sealed class Request
        {
            public string messageType;
            public float time;
            public string sentBy;
            public int[] ports;
        }

        protected override bool TryParsePacket(string text, out HoFaceInputPacket packet)
        {
            packet = null;
            if (!HoFaceInputPacket.TryParseJson(text, out var parsed, out var data)) return false;
            packet = parsed;

            if (data.BlendShapes != null)
            {
                for (int i = 0; i < data.BlendShapes.Count; i++)
                {
                    var entry = data.BlendShapes[i];
                    if (entry == null || string.IsNullOrEmpty(entry.k)) { packet.InvalidCount++; continue; }
                    packet.Set(entry.k, entry.v);   // 线名照原样（PascalCase），值照原样
                }
            }

            WriteVector(data.Rotation, "Rotation", packet);
            WriteVector(data.Position, "Position", packet);
            WriteVector(data.EyeLeft, "EyeLeft", packet);
            WriteVector(data.EyeRight, "EyeRight", packet);
            packet.Set("FaceFound", data.FaceFound ? 1f : 0f);
            packet.Set("Hotkey", data.Hotkey);
            packet.Set("Timestamp", data.Timestamp);
            return packet.EntryCount > 0;
        }

        /// <summary>`Rotation` → `Rotation_x/y/z`：分量名照 Unity 的 Vector3 字段名。</summary>
        private static void WriteVector(Vector3 source, string name, HoFaceInputPacket packet)
        {
            packet.Set(name + "_x", source.x);
            packet.Set(name + "_y", source.y);
            packet.Set(name + "_z", source.z);
        }
    }
}
