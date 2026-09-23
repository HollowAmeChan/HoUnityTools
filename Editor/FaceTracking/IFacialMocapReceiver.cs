using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **iFacialMocap / Facemotion3d** 的接收端（文本协议）。
    ///
    /// 官方开发者文档：<https://www.ifacialmocap.com/for-developer/>
    /// 每帧是一串以 `|` 分隔的字段：
    /// · 形态键：`名称-数值`（v1）或 `名称&amp;数值`（发过 `|sendDataVersion=v2` 之后）；
    /// · `=head#` 后跟 **6 个数**：欧拉角 X/Y/Z、位置 X/Y/Z（官方："Angle-related data is sent in degrees"）；
    /// · `leftEye#` / `rightEye#` 各 3 个数；
    /// · 形态键数值是 **0..100**（VTS 手机那条是 0..1 —— 差别写在中间层的输入行里，不在这里）。
    ///
    /// 本接收端**只交原样**：线名就是协议里的名字、数值就是原值，姿态按 `字段_下标` 摊平。
    /// 改名、量纲、哪一段是"位置"全由中间层配置决定（见 <see cref="HoFaceInputPacket"/> 的注释）。
    /// </summary>
    public sealed class IFacialMocapReceiver : HoFaceReceiverBase
    {
        /// <summary>手机端固定端口：握手包发到它，回包也回到本机这个端口（官方 Python 例子）。</summary>
        public const int Port = 49983;

        /// <summary>官方的握手口令；不发它手机不会回包。</summary>
        public const string StartCommand = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";

        public override string DisplayName => "iFacialMocap";
        public override string Hint => "App「iFacialMocap」在手机上打开即可（PC 端由我们主动握手）；本机端口固定 " + Port + "。";
        public override string Id => "ifacialmocap";
        public override string Protocol => "iFacialMocap";
        public override int DefaultLocalPort => Port;
        public override bool FixedLocalPort => true;

        protected override void OnStarted(UdpClient client, IPAddress phone)
        {
            // 先监听再握手（回包回到本机 49983，不是临时端口）。
            byte[] command = Encoding.UTF8.GetBytes(StartCommand);
            client.Send(command, command.Length, new IPEndPoint(phone, Port));
            CountRequest();
        }

        protected override bool TryParsePacket(string text, out HoFaceInputPacket packet)
        {
            packet = new HoFaceInputPacket();
            if (string.IsNullOrEmpty(text) || text.Length > MaxPayload) return false;

            string[] fields = text.Split('|');
            foreach (string raw in fields)
            {
                string field = raw.Trim();
                if (field.Length == 0) continue;

                if (field.StartsWith("=head#", StringComparison.Ordinal)) { Pose(field, 6, "head", packet); continue; }
                if (field.StartsWith("head#", StringComparison.Ordinal)) { Pose(field, 6, "head", packet); continue; }
                if (field.StartsWith("leftEye#", StringComparison.Ordinal)) { Pose(field, 3, "leftEye", packet); continue; }
                if (field.StartsWith("rightEye#", StringComparison.Ordinal)) { Pose(field, 3, "rightEye", packet); continue; }

                // v2 用 `&` 当分隔符（因为 Facemotion3d 会发负值，`-` 会歧义）；v1 用 `-`。
                int separator = field.IndexOf('&');
                if (separator < 0) separator = field.IndexOf('-');
                if (separator <= 0) { packet.InvalidCount++; continue; }
                if (!TryNumber(field.Substring(separator + 1), out float value)) { packet.InvalidCount++; continue; }
                packet.Set(field.Substring(0, separator), value);
            }

            return packet.EntryCount > 0;
        }

        /// <summary>`字段名#a,b,c…` → `字段名_0..n`。数量不符或数值不合法都记一次无效。</summary>
        private static void Pose(string field, int count, string name, HoFaceInputPacket packet)
        {
            string[] parts = field.Substring(field.IndexOf('#') + 1).Split(',');
            if (parts.Length != count) { packet.InvalidCount++; return; }
            for (int i = 0; i < count; i++)
            {
                if (!TryNumber(parts[i], out float value)) { packet.InvalidCount++; return; }
                packet.Set(name + "_" + i, value);
            }
        }

        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
