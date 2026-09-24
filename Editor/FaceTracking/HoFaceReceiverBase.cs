using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Hollow.HoUnityTools.FaceTracking;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **一路设备输入源**。所有设备（iFacialMocap / VTS 手机 / 以后的 FaceMotion3D、录制回放…）都实现这一组成员，
    /// 形状固定，只有"怎么解析一包"和"手机端要做什么"两处是按协议写的。
    ///
    /// 为什么强制统一形状：VBridger 那边每个协议各写各的 handler，量纲、改名、丢帧策略散在五处
    /// （见 `docs/archive/VBRIDGER_IO_VOCABULARY.md` 与反编译 L11705 / L12019 / L12148 / L12161），
    /// 结果就是"换个源要读五个地方"。我们让差异**只出现在 <see cref="HoFaceReceiverBase.TryParsePacket"/> 一处**，
    /// 其余（socket、线程、统计、丢包容忍、来源校验）都由基类提供。
    /// </summary>
    public interface IHoFaceInputReceiver : IDisposable
    {
        /// <summary>面板上的名字（也是配置里的标识）。</summary>
        string DisplayName { get; }

        /// <summary>一句话说清"手机那头要做什么"（面板提示用）。</summary>
        string Hint { get; }

        /// <summary>本机监听端口（面板显示、防火墙放行都用它）。</summary>
        int LocalPort { get; }

        /// <summary>当前设置下这个源的实例 id（配置里用它对应到具体条目）。</summary>
        string Id { get; }

        bool Running { get; }
        string Error { get; }
        /// <summary>最近一包的来源（手机 IP:端口）。</summary>
        string Sender { get; }
        /// <summary>最近一个"来源不符"的包从哪来（填错 IP 时最直接的线索）。</summary>
        string RejectedSource { get; }

        long Packets { get; }
        long Invalid { get; }
        long Replaced { get; }
        long Rejected { get; }
        long Recoveries { get; }
        /// <summary>主动发出去的请求/握手次数（有的协议是"请求式"的，用它区分"没发出去"和"发了没人回"）。</summary>
        long Requests { get; }
        /// <summary>最近一次收到**有效**包的时刻（单调时钟，秒）。0 = 还一个都没收到。</summary>
        double LastFrameTime { get; }

        void Start(HoFaceSourceEntry entry);
        bool TryTake(out HoFaceInputPacket packet, out double timestamp);
    }

    /// <summary>
    /// 输入源的基类：**把"怎么收"的公共部分固化成形状**，子类只实现协议本身。
    ///
    /// 基类负责（子类不要再写一遍）：
    /// · 校验 IPv4、建 socket、绑定端口、关掉 Windows 的 UDP 连接重置、开后台线程；
    /// · 收包循环：来源校验（只收手机那个 IP）、大小上限、瞬时错误容忍、丢旧帧计数；
    /// · 统计（Packets/Invalid/Replaced/Rejected/Recoveries）与"最新一包"的交接（<see cref="TryTake"/>）；
    /// · 子类钩子：<see cref="OnStarted"/>（握手 / 首个请求）、<see cref="OnTick"/>（周期续约）、
    ///   <see cref="TryParsePacket"/>（**唯一的协议差异点**）。
    ///
    /// 子类负责：
    /// · <see cref="TryParsePacket"/>：把这包字节解析成**规范名**的 <see cref="HoFaceInputPacket"/>；
    ///   线名改名、量纲换算、姿态分量顺序都在这里，**并且必须写注释说明依据**（官方文档/实测）。
    /// </summary>
    public abstract class HoFaceReceiverBase : IHoFaceInputReceiver
    {
        /// <summary>大于这个长度的包直接判为无效（正常帧都在几 KB 以内）。</summary>
        protected const int MaxPayload = 16384;
        private const int ReceiveTimeoutMs = 250;

        private readonly object sync = new object();
        private UdpClient socket;
        private Thread worker;
        private volatile bool stopping;
        private IPAddress phone;
        private int localPort;
        private HoFaceInputPacket pending;
        private double pendingTime;
        private string sender = "";
        private string rejectedSource = "";
        private string error = "";
        private long packets, invalid, replaced, rejected, recoveries, requests;
        private double lastFrameTime;

        public abstract string DisplayName { get; }
        public abstract string Hint { get; }
        public abstract string Id { get; }

        /// <summary>协议名（用于错误信息与面板分组）。</summary>
        public abstract string Protocol { get; }

        /// <summary>本机默认监听端口 —— 各协议**故意不同**，好让两条能同时连着。</summary>
        public abstract int DefaultLocalPort { get; }

        /// <summary>固定端口（比如 iFacialMocap 必须 49983）时返回真，此时配置里的端口字段会被忽略。</summary>
        public virtual bool FixedLocalPort => false;

        public int LocalPort { get { lock (sync) return localPort; } }
        public bool Running => worker != null && worker.IsAlive && !stopping;
        public string Error { get { lock (sync) return error; } }
        public string Sender { get { lock (sync) return sender; } }
        public string RejectedSource { get { lock (sync) return rejectedSource; } }
        public long Packets => Interlocked.Read(ref packets);
        public long Invalid => Interlocked.Read(ref invalid);
        public long Replaced => Interlocked.Read(ref replaced);
        public long Rejected => Interlocked.Read(ref rejected);
        public long Recoveries => Interlocked.Read(ref recoveries);
        public long Requests => Interlocked.Read(ref requests);
        public double LastFrameTime { get { lock (sync) return lastFrameTime; } }

        /// <summary>
        /// 共享时钟：接收端（**后台线程**给包打时间戳）、会话（判"这一包新不新鲜"）、面板（包速率）都读它。
        /// **唯一定义在 <see cref="HoFaceClock"/>** —— 这里只是别名，不许另起一个实现：
        /// 两个时钟只要差一个恒定偏移，会话那边的差值就永远越界，表现是"包到了、合并里也有，通道就是不写"。
        /// </summary>
        public static double Now => HoFaceClock.Now;

        public void Start(HoFaceSourceEntry entry)
        {
            Dispose();
            if (entry == null) throw new ArgumentException("缺少这个输入源的配置条目。");
            if (!IPAddress.TryParse(entry.phoneIp, out phone) || phone.AddressFamily != AddressFamily.InterNetwork
                || phone.Equals(IPAddress.Any) || phone.Equals(IPAddress.Broadcast))
                throw new ArgumentException("请输入手机的 IPv4 地址。");
            int port = FixedLocalPort ? DefaultLocalPort : entry.localPort;
            if (port < 1024 || port > 65535) throw new ArgumentException("本机监听端口要在 1024–65535 之间。");

            stopping = false;
            packets = invalid = replaced = rejected = recoveries = requests = 0;
            lock (sync)
            {
                pending = null;
                error = sender = rejectedSource = "";
                localPort = port;
                lastFrameTime = 0;
            }

            try
            {
                socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Client.ExclusiveAddressUse = true;
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                socket.Client.ReceiveTimeout = ReceiveTimeoutMs;
                DisableUdpConnectionReset(socket);
                worker = new Thread(Receive) { IsBackground = true, Name = "Ho " + Protocol + " UDP" };
                worker.Start();
                OnStarted(socket, phone);
            }
            catch { Dispose(); throw; }
        }

        public bool TryTake(out HoFaceInputPacket packet, out double timestamp)
        {
            lock (sync)
            {
                packet = pending; timestamp = pendingTime; pending = null;
                return packet != null;
            }
        }

        /// <summary>把一包文本解析成**规范名**的统一帧。<b>这是子类唯一的协议差异点。</b></summary>
        protected abstract bool TryParsePacket(string text, out HoFaceInputPacket packet);

        /// <summary>给验证用例直接喂一包字符串（正常路径是接收线程调 <see cref="TryParsePacket"/>）。</summary>
        public bool ParseForTest(string text, out HoFaceInputPacket packet) => TryParsePacket(text, out packet);

        /// <summary>socket 建好之后立刻做的事（发握手串 / 发第一条请求）。</summary>
        protected virtual void OnStarted(UdpClient client, IPAddress phoneAddress) { }

        /// <summary>收包循环每次迭代都会调一次（用来做"请求式"协议的周期续约）。</summary>
        protected virtual void OnTick(UdpClient client, IPAddress phoneAddress) { }

        /// <summary>记一次主动发送（握手也算），面板用它区分"没发出去"和"发出去没人回"。</summary>
        protected void CountRequest() => Interlocked.Increment(ref requests);

        /// <summary>
        /// 这些 socket 错误是"网络抖了一下"，不是"这条路走不通了"：继续收，不要退出。
        /// （曾经的教训：一次瞬时错误就 break，表现成"跑着跑着面捕断了"，socket 还开着。）
        /// </summary>
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
        internal static void DisableUdpConnectionReset(UdpClient client)
        {
            try { client.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); }
            catch { }
        }

        private void Receive()
        {
            while (!stopping)
            {
                try
                {
                    OnTick(socket, phone);

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

                    if (data.Length > MaxPayload || !TryParsePacket(Encoding.UTF8.GetString(data), out var packet) || packet == null)
                    { Interlocked.Increment(ref invalid); continue; }
                    Interlocked.Increment(ref packets);
                    lock (sync)
                    {
                        if (pending != null) Interlocked.Increment(ref replaced);
                        pending = packet;
                        pendingTime = Now;
                        lastFrameTime = pendingTime;
                        sender = endpoint.ToString();
                    }
                }
                catch (SocketException e)
                {
                    if (stopping) break;
                    if (e.SocketErrorCode == SocketError.TimedOut) continue;
                    if (IsTransient(e.SocketErrorCode))
                    {
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
