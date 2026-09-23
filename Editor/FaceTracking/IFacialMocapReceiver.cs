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
        private string rejectedSource = "";
        private string error = "";
        private long packets, invalid, replaced, rejected, recoveries;

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        public bool Running => worker != null && worker.IsAlive && !stopping;
        public string Error { get { lock (sync) return error; } }
        public string Sender { get { lock (sync) return sender; } }
        /// <summary>最近一个"来源和填的手机 IP 不一致"的包来自哪里。填错 IP 时这是最直接的线索。</summary>
        public string RejectedSource { get { lock (sync) return rejectedSource; } }
        public long Packets => Interlocked.Read(ref packets);
        public long Invalid => Interlocked.Read(ref invalid);
        public long Replaced => Interlocked.Read(ref replaced);
        public long Rejected => Interlocked.Read(ref rejected);
        /// <summary>扛过去的瞬时 socket 错误次数。> 0 说明网络曾经抖过，但接收没死。</summary>
        public long Recoveries => Interlocked.Read(ref recoveries);

        public void Start(string phoneIp)
        {
            Dispose();
            if (!IPAddress.TryParse(phoneIp, out phone) || phone.AddressFamily != AddressFamily.InterNetwork || phone.Equals(IPAddress.Any) || phone.Equals(IPAddress.Broadcast))
                throw new ArgumentException("请输入手机的 IPv4 地址。");
            stopping = false;
            packets = invalid = replaced = rejected = recoveries = 0;
            lock (sync) { pending = null; error = sender = rejectedSource = ""; }
            try
            {
                socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.ExclusiveAddressUse = true;
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                socket.Client.ReceiveTimeout = 250;
                DisableUdpConnectionReset(socket);
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
                    if (!endpoint.Address.Equals(phone))
                    {
                        // 记下来源：填错手机 IP 时，"有包但被来源校验拒了"和"一个包都没来"
                        // 是两种完全不同的故障，面板必须能分开说。
                        lock (sync) rejectedSource = endpoint.Address.ToString();
                        Interlocked.Increment(ref rejected);
                        continue;
                    }
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
                    if (stopping) break;
                    if (e.SocketErrorCode == SocketError.TimedOut) continue;
                    if (IsTransient(e.SocketErrorCode))
                    {
                        // **以前这里是 break** —— 一次瞬时错误就把接收线程打死，表现就是"跑着跑着面捕就断了"，
                        // 而且 socket 还开着、面板写着断开，只能手动重连。网络抖一下不该是终点。
                        Interlocked.Increment(ref recoveries);
                        lock (sync) error = e.SocketErrorCode + "：" + e.Message;
                        Thread.Sleep(50);
                        continue;
                    }

                    if (!stopping) lock (sync) error = e.SocketErrorCode + "：" + e.Message;
                    break;
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { if (!stopping) lock (sync) error = e.Message; break; }
            }
        }

        /// <summary>这些 socket 错误是"网络抖了一下"，不是"这条路走不通了"：继续收，不要退出。</summary>
        private static bool IsTransient(SocketError code) => code == SocketError.ConnectionReset
            || code == SocketError.NetworkReset || code == SocketError.Interrupted
            || code == SocketError.HostUnreachable || code == SocketError.NetworkUnreachable
            || code == SocketError.MessageSize || code == SocketError.WouldBlock
            || code == SocketError.NoBufferSpaceAvailable;

        /// <summary>
        /// Windows 上 UDP 收到 ICMP 端口不可达时，会把**下一次** Receive 变成 ConnectionReset
        /// （典型触发：握手包发给了一个还没在听的手机端口）。SIO_UDP_CONNRESET 关掉这个行为。
        /// 非 Windows 或调用失败都直接忽略 —— 上面的瞬态处理已经能兜住。
        /// </summary>
        private static void DisableUdpConnectionReset(UdpClient client)
        {
            try { client.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); }
            catch { }
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
