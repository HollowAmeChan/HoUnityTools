using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;

namespace HoFace.Transport
{
    public sealed class VtsIphoneOptions
    {
        public string PhoneAddress = "127.0.0.1";
        public int PhonePort = 21412;
        public int LocalPort = 21413;
        public float RequestSeconds = 5;
    }

    public sealed class VtsFrame
    {
        public readonly Dictionary<string, float> Values;
        public readonly bool FaceFound;
        public readonly DateTime ReceivedUtc;
        public VtsFrame(Dictionary<string, float> values, bool faceFound)
        {
            Values = values; FaceFound = faceFound; ReceivedUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Main-thread, non-blocking VTS iPhone UDP transport.
    /// The caller invokes Poll from its own update loop; no UI or core work is performed here.
    /// </summary>
    public sealed class VtsIphoneReceiver : IDisposable
    {
        private UdpClient socket;
        private IPEndPoint destination;
        private DateTime nextRequestUtc;
        private readonly byte[] receiveBuffer = new byte[65535];
        public bool Running => socket != null;
        public int LocalPort { get; private set; }
        public long Packets { get; private set; }
        public long InvalidPackets { get; private set; }
        public string Status { get; private set; } = "未连接";
        public string LastError { get; private set; }
        public event Action<VtsFrame> FrameReceived;
        public event Action<string> StatusChanged;

        public void Start(VtsIphoneOptions options)
        {
            Stop();
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!IPAddress.TryParse(options.PhoneAddress, out IPAddress address))
                throw new FormatException("手机 IPv4 地址无效：" + options.PhoneAddress);
            if (options.PhonePort < 1 || options.PhonePort > 65535)
                throw new ArgumentOutOfRangeException(nameof(options.PhonePort));
            if (options.LocalPort < 1 || options.LocalPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(options.LocalPort));
            destination = new IPEndPoint(address, options.PhonePort);
            socket = new UdpClient(options.LocalPort);
            socket.Client.Blocking = false;
            LocalPort = ((IPEndPoint)socket.Client.LocalEndPoint).Port;
            RequestSeconds = options.RequestSeconds > 0 && options.RequestSeconds <= 10 ? options.RequestSeconds : 5;
            nextRequestUtc = DateTime.MinValue;
            SetStatus("已绑定 " + LocalPort + "，等待手机");
        }

        private float RequestSeconds { get; set; } = 5;

        public void Stop()
        {
            socket?.Close();
            socket?.Dispose();
            socket = null;
            destination = null;
            LocalPort = 0;
            SetStatus("未连接");
        }

        public void Poll()
        {
            if (socket == null) return;
            DateTime now = DateTime.UtcNow;
            if (now >= nextRequestUtc)
            {
                byte[] request = Encoding.UTF8.GetBytes(
                    "{\"messageType\":\"iOSTrackingDataRequest\",\"time\":"
                    + RequestSeconds.ToString("0.##", CultureInfo.InvariantCulture)
                    + ",\"sentBy\":\"HoFaceStudio\",\"ports\":[" + LocalPort + "]}");
                try
                {
                    socket.Send(request, request.Length, destination);
                    nextRequestUtc = now.AddSeconds(1);
                }
                catch (Exception error) { LastError = error.Message; SetStatus("发送请求失败"); return; }
            }

            int count = 0;
            while (socket.Available > 0 && count++ < 16)
            {
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes;
                try { bytes = socket.Receive(ref source); }
                catch (SocketException) { break; }
                if (!HoVtsPacket.TryParse(Encoding.UTF8.GetString(bytes), out Dictionary<string, float> values,
                    out bool faceFound, out string error))
                {
                    InvalidPackets++;
                    LastError = error;
                    continue;
                }
                Packets++;
                SetStatus(faceFound ? "运行中 · " + Packets + " 帧" : "已连接 · 未检测到脸");
                FrameReceived?.Invoke(new VtsFrame(values, faceFound));
            }
        }

        private void SetStatus(string status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }

        public void Dispose() => Stop();
    }
}
