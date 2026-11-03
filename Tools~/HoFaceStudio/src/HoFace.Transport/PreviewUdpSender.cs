using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HoFace.Transport
{
    public sealed class PreviewUdpOptions
    {
        public string Address = "127.0.0.1";
        public int Port = 21414;
    }

    public sealed class HoFacePreviewSender : IDisposable
    {
        private UdpClient socket;
        private IPEndPoint destination;
        private long sequence;
        private Dictionary<string, float> lastValues;
        private Dictionary<string, float> lastWeights;
        private double lastTime;
        private bool lastTracked;
        private readonly JsonSerializerOptions json = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true };
        public bool Running => socket != null;
        public long SentFrames { get; private set; }
        public string Status { get; private set; } = "预览未连接";
        public void Start(PreviewUdpOptions options = null)
        {
            Stop();
            options = options ?? new PreviewUdpOptions();
            if (!IPAddress.TryParse(options.Address, out IPAddress address))
                throw new FormatException("预览地址无效：" + options.Address);
            if (options.Port < 1 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            socket = new UdpClient();
            destination = new IPEndPoint(address, options.Port);
            sequence = 0; SentFrames = 0; Status = "预览发送 " + destination;
        }
        public void Send(IReadOnlyDictionary<string, float> values, IReadOnlyDictionary<string, float> weights,
            bool tracked, double time)
        {
            if (socket == null) return;
            lastValues = values == null ? new Dictionary<string, float>() : new Dictionary<string, float>(values);
            lastWeights = weights == null ? new Dictionary<string, float>() : new Dictionary<string, float>(weights);
            lastTime = time;
            lastTracked = tracked;
            var packet = new PreviewPacket
            {
                type = "hoface.preview",
                version = 2,
                sequence = ++sequence,
                time = time,
                tracked = tracked,
                values = lastValues,
                weights = lastWeights
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, json));
            socket.Send(bytes, bytes.Length, destination);
            SentFrames++;
        }
        public void Stop()
        {
            if (socket != null && lastTracked)
                Send(lastValues, lastWeights, false, lastTime);
            socket?.Close(); socket?.Dispose(); socket = null; destination = null;
            lastValues = null; lastWeights = null; lastTime = 0; lastTracked = false;
            Status = "预览未连接";
        }
        public void Dispose() => Stop();
        private sealed class PreviewPacket
        {
            public string type;
            public int version;
            public long sequence;
            public double time;
            public bool tracked;
            public Dictionary<string, float> values, weights;
        }
    }
}
