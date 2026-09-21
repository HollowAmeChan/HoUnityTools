using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public sealed class IFacialMocapReceiver : IDisposable
    {
        public const int Port = 49983;
        public const string StartCommand = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";
        private readonly object sync = new object();
        private UdpClient socket;
        private Thread worker;
        private volatile bool stopping;
        private IPAddress phone;
        private IFacialMocapPacket pending;
        private double pendingTime;
        private string sender = "";
        private string error = "";
        private long packets, invalid, replaced, rejected;

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        public bool Running => worker != null && worker.IsAlive && !stopping;
        public string Error { get { lock (sync) return error; } }
        public string Sender { get { lock (sync) return sender; } }
        public long Packets => Interlocked.Read(ref packets);
        public long Invalid => Interlocked.Read(ref invalid);
        public long Replaced => Interlocked.Read(ref replaced);
        public long Rejected => Interlocked.Read(ref rejected);

        public void Start(string phoneIp)
        {
            Dispose();
            if (!IPAddress.TryParse(phoneIp, out phone) || phone.AddressFamily != AddressFamily.InterNetwork || phone.Equals(IPAddress.Any) || phone.Equals(IPAddress.Broadcast))
                throw new ArgumentException("请输入手机的 IPv4 地址。");
            stopping = false;
            packets = invalid = replaced = rejected = 0;
            lock (sync) { pending = null; error = sender = ""; }
            try
            {
                socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.ExclusiveAddressUse = true;
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                socket.Client.ReceiveTimeout = 250;
                // Listen before handshake. Replies go to PC:49983, not an ephemeral send port.
                worker = new Thread(Receive) { IsBackground = true, Name = "Ho iFacialMocap UDP" };
                worker.Start();
                byte[] command = Encoding.UTF8.GetBytes(StartCommand);
                socket.Send(command, command.Length, new IPEndPoint(phone, Port));
            }
            catch { Dispose(); throw; }
        }

        public bool TryTake(out IFacialMocapPacket packet, out double timestamp)
        {
            lock (sync)
            {
                packet = pending; timestamp = pendingTime; pending = null;
                return packet != null;
            }
        }

        private void Receive()
        {
            while (!stopping)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = socket.Receive(ref endpoint);
                    if (!endpoint.Address.Equals(phone)) { Interlocked.Increment(ref rejected); continue; }
                    if (data.Length > 16384 || !IFacialMocapPacket.TryParse(Encoding.UTF8.GetString(data), out var packet))
                    { Interlocked.Increment(ref invalid); continue; }
                    Interlocked.Increment(ref packets);
                    lock (sync)
                    {
                        if (pending != null) Interlocked.Increment(ref replaced);
                        pending = packet; pendingTime = Now; sender = endpoint.ToString();
                    }
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.TimedOut) continue;
                    if (!stopping) lock (sync) error = e.Message;
                    break;
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { if (!stopping) lock (sync) error = e.Message; break; }
            }
        }

        public void Dispose()
        {
            stopping = true;
            socket?.Close();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(1000);
            worker = null;
            socket = null;
            lock (sync) pending = null;
        }
    }
}
