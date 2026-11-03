using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HoFace.Transport
{
    /// <summary>Unity authoring adapter transport. Warudo Mod does not consume this envelope.</summary>
    public sealed class HoFaceRawForwardSender : IDisposable
    {
        private UdpClient socket;
        private IPEndPoint destination;
        private long sequence;
        private readonly JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
        public bool Running => socket != null;
        public string Status { get; private set; } = "Unity 原始输入未连接";
        public void Start(string address = "127.0.0.1", int port = 21415)
        {
            Stop();
            if (!IPAddress.TryParse(address, out IPAddress ip)) throw new FormatException("Unity 原始输入地址无效：" + address);
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            socket = new UdpClient();
            destination = new IPEndPoint(ip, port);
            sequence = 0;
            Status = "Unity 原始输入发送 " + destination;
        }
        public void Send(IReadOnlyDictionary<string, float> values, bool tracked, double time)
        {
            if (socket == null) return;
            var packet = new RawPacket
            {
                type = "hoface.raw",
                version = 1,
                sequence = ++sequence,
                time = time,
                tracked = tracked,
                values = values == null ? new Dictionary<string, float>() : new Dictionary<string, float>(values)
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, json));
            socket.Send(bytes, bytes.Length, destination);
        }
        public void Stop()
        {
            socket?.Close(); socket?.Dispose(); socket = null; destination = null;
            Status = "Unity 原始输入未连接";
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
