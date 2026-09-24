using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;

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
    /// ⚠️ **协议细节（请求包的构造、载荷的解析）全在 <see cref="HoVtsPacket"/>**，这个类只管 socket 与节奏。
    /// 为什么必须这样：这里原来自己写了一套 `JsonUtility.FromJson&lt;VtsTrackingData&gt;`，
    /// 而载荷里的 `BlendShapes` 是 `List&lt;嵌套类&gt;` —— **正是 Warudo 那边静默丢掉 52 个形态键的那个模式**
    /// （见 Runtime/FaceTracking/HoJson.cs 记的三次事故）。搬家之后 Unity 侧与 Warudo 侧读的是同一份解析器、
    /// 同一份离线测试（`.research/profile-json-test`，87 条）。
    ///
    /// 本接收端**只交原样**：字段名照抄载荷里的名字（`Rotation` → `Rotation_x/y/z`），形态键数值也不做换算。
    /// 哪一段是"角度"、哪一段是"位置"、要不要 `* 0.0174533`，全写在中间层的输入行里。
    /// </summary>
    public sealed class VtsIphoneReceiver : HoFaceReceiverBase
    {
        /// <summary>iPhone 侧监听端口（官方默认值；App 界面上显示的那个）。</summary>
        public const int PhonePort = 21412;

        /// <summary>本机默认监听端口。</summary>
        public const int DefaultPort = 49984;

        /// <summary>官方允许 0.5–10 秒；我们要 5 秒、每秒续一次。定义在 <see cref="HoVtsPacket"/>。</summary>
        public const float RequestSeconds = HoVtsPacket.RequestSeconds;

        private const double RenewSeconds = 1.0;

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
            byte[] payload = Encoding.UTF8.GetBytes(HoVtsPacket.BuildRequest(LocalPort));
            client.Send(payload, payload.Length, new IPEndPoint(phone, PhonePort));
            CountRequest();
            lastRequest = Now;
        }

        protected override bool TryParsePacket(string text, out HoFaceInputPacket packet)
        {
            packet = new HoFaceInputPacket();

            bool faceFound;
            string error;
            var values = default(System.Collections.Generic.Dictionary<string, float>);
            if (!HoVtsPacket.TryParse(text, out values, out faceFound, out error)) return false;
            if (values == null) return false;

            foreach (var pair in values) packet.Set(pair.Key, pair.Value);

            // ⚠️ 这里不再逐条统计 InvalidCount：认不出的形态键条目由 HoVtsPacket 直接跳过，
            // 它只报"整包能不能解析"。整包坏掉会返回 false（走坏帧计数），这才是真正需要盯的那个数。
            return packet.EntryCount > 0;
        }
    }
}
