using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Transport
{
    /// <summary>Unity authoring adapter transport. Warudo Mod does not consume this envelope.</summary>
    public sealed class HoFaceRawForwardSender : IDisposable
    {
        private UdpClient socket;
        private IPEndPoint destination;
        private long sequence;
        private long lastAckedSequence;
        private DateTime lastAckUtc;
        private readonly JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
        public bool Running => socket != null;
        public string Status { get; private set; } = "Unity 原始输入未连接";
        public long SentFrames { get; private set; }
        public long LastAckedSequence => lastAckedSequence;
        public event Action<string> StatusChanged;
        public void Start(string address = "127.0.0.1", int port = 21415)
        {
            Stop();
            if (!IPAddress.TryParse(address, out IPAddress ip)) throw new FormatException("Unity 原始输入地址无效：" + address);
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new FormatException("Unity 原始输入仅支持 IPv4 loopback。");
            if (!IPAddress.IsLoopback(ip)) throw new FormatException("Unity 原始输入只能发送到本机 loopback。");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            socket.Client.Blocking = false;
            destination = new IPEndPoint(ip, port);
            lastAckedSequence = 0;
            lastAckUtc = DateTime.MinValue;
            SentFrames = 0;
            SetStatus("等待 Unity 接收器");
        }
        public void Send(IReadOnlyDictionary<string, float> values, bool tracked, double time)
        {
            if (socket == null) return;
            long nextSequence = ++sequence;
            var packet = new RawPacket
            {
                type = "hoface.raw",
                version = 1,
                sequence = nextSequence,
                time = time,
                tracked = tracked,
                values = values == null ? new Dictionary<string, float>() : new Dictionary<string, float>(values)
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, json));
            socket.Send(bytes, bytes.Length, destination);
            SentFrames++;
        }
        public void Poll()
        {
            if (socket == null) return;
            int count = 0;
            while (socket.Available > 0 && count++ < 16)
            {
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes;
                try { bytes = socket.Receive(ref source); }
                catch (SocketException) { break; }
                if (!source.Address.Equals(destination.Address) || source.Port != destination.Port)
                    continue;
                if (!HoFaceRawFrameJson.TryParseAck(Encoding.UTF8.GetString(bytes), out HoFaceRawAck ack, out _))
                    continue;
                if (ack.Sequence > sequence) continue;
                if (ack.Sequence < lastAckedSequence) continue;
                if (!ack.Accepted)
                {
                    SetStatus(string.IsNullOrEmpty(ack.Status) ? "Unity 拒绝 raw 帧" : "Unity：" + ack.Status);
                    continue;
                }
                lastAckedSequence = ack.Sequence;
                lastAckUtc = DateTime.UtcNow;
                SetStatus("Unity 接收器已连接");
            }
            if (lastAckUtc != DateTime.MinValue && DateTime.UtcNow - lastAckUtc > TimeSpan.FromSeconds(2))
                SetStatus("等待 Unity 接收器");
        }
        private void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value;
            StatusChanged?.Invoke(value);
        }
        public void Stop()
        {
            socket?.Close(); socket?.Dispose(); socket = null; destination = null;
            lastAckedSequence = 0;
            lastAckUtc = DateTime.MinValue;
            SentFrames = 0;
            SetStatus("Unity 原始输入未连接");
        }
        public void Dispose() => Stop();
        private sealed class RawPacket
        {
            public string type;
            public int version;
            public long sequence;
            public double time;
            public bool tracked;
            public Dictionary<string, float> values;
        }
    }
}
