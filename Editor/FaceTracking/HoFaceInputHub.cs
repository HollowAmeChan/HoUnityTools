using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 面捕输入的**汇聚点**：把环境里那几条源拉起来，每帧挑出"该信谁"，并把结果交给会话。
    ///
    /// 合并规则（用户定的）：**按环境顺序，第一个"还新鲜、且这一帧带来了这个名字"的源胜** ——
    /// 逐个**线名**判，不按整包判。于是"VTS 手机先、iFacialMocap 后"这种配置下，
    /// 只开 iFacialMocap 时行为完全不变；两条都活着时优先用排在前面的那条。
    ///
    /// 这里**不认识任何协议**：源交上来的是"线名 → 原值"，合并也是按线名做的。
    /// 线名怎么变成规范名、量纲要乘多少，全在中间层配置的输入行里（见 <see cref="HoFaceInputPacket"/>）。
    /// </summary>
    [InitializeOnLoad]
    public static class HoFaceInputHub
    {
        /// <summary>多久没收到包就认为这条源"不新鲜"了（秒）。</summary>
        private const double FreshSeconds = 1.0;

        private static readonly List<IHoFaceInputReceiver> Receivers = new List<IHoFaceInputReceiver>();
        private static readonly List<HoFaceSourceEntry> Entries = new List<HoFaceSourceEntry>();
        private static readonly List<HoFaceInputPacket> Latest = new List<HoFaceInputPacket>();
        private static readonly List<double> LatestTime = new List<double>();

        /// <summary>合并后的输入：线名 → 原值（每帧重建）。</summary>
        private static readonly Dictionary<string, float> Merged = new Dictionary<string, float>(StringComparer.Ordinal);
        /// <summary>每个线名最后一次被"新鲜源"提供的时刻（会话判断断流用）。</summary>
        private static readonly Dictionary<string, double> MergedAt = new Dictionary<string, double>(StringComparer.Ordinal);

        private static readonly Dictionary<HoFaceDebugSettings, HoFaceAnimationSession> Sessions = new Dictionary<HoFaceDebugSettings, HoFaceAnimationSession>();
        private static readonly Dictionary<HoFaceDebugSettings, string> Errors = new Dictionary<HoFaceDebugSettings, string>();
        /// <summary>用户自己按了「停止并交还」的 settings：本次 Play 内不再自动拉起。</summary>
        private static readonly HashSet<HoFaceDebugSettings> UserStopped = new HashSet<HoFaceDebugSettings>();
        /// <summary>每个 settings 下一次允许自动重试的时刻（单调时钟）—— 失败后 1 秒一次，不做热循环。</summary>
        private static readonly Dictionary<HoFaceDebugSettings, double> NextTry = new Dictionary<HoFaceDebugSettings, double>();
        private static readonly Dictionary<HoFaceDebugSettings, int> Attempts = new Dictionary<HoFaceDebugSettings, int>();
        private static double nextConnectTry;

        public static double LastFrameTime { get; private set; }
        public static double ConnectStartedAt { get; private set; }
        public static string ConnectionError { get; private set; } = "";

        /// <summary>环境里至少有一条源在跑，就算"连着"。</summary>
        public static bool Connected => Receivers.Any(receiver => receiver.Running);

        public static int SourceCount => Receivers.Count;

        public static IReadOnlyList<IHoFaceInputReceiver> Sources => Receivers;

        /// <summary>合并后的线名取值；查不到返回 0。</summary>
        public static float Input(string wire) => wire != null && Merged.TryGetValue(wire, out float value) ? value : 0f;

        public static bool Has(string wire) => wire != null && Merged.ContainsKey(wire);

        /// <summary>这个名字最后一次被新鲜源提供的时刻（0 = 从来没来过）。</summary>
        public static double ReceivedAt(string wire) => wire != null && MergedAt.TryGetValue(wire, out double time) ? time : 0;

        /// <summary>合并表的只读视图（面板/调试用）。</summary>
        public static IReadOnlyDictionary<string, float> MergedValues => Merged;

        static HoFaceInputHub()
        {
            // 输入侧**自己驱动自己**：收包与重连不该依赖窗口开着、也不依赖进了播放模式
            //（面板上"连上手机、看原始值"这一步就在编辑模式里做）。
            // 会话那一侧由 HoFaceDebugHost 驱动，两者互不重叠。
            EditorApplication.update += HostUpdate;
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        }

        /// <summary>
        /// 按环境（工程设置里那份有序列表）拉起所有启用的源。
        /// 传 <paramref name="phoneIpOverride"/> 时只改**第一条**源的 IP —— 面板上那个"手机 IPv4"框就是在做这件事，
        /// 别的源保持它们各自配置里的地址（不同源可以是不同手机）。
        /// </summary>
        public static void Connect(string phoneIpOverride = null)
        {
            var environment = HoFaceInputEnvironment.instance;
            if (!string.IsNullOrWhiteSpace(phoneIpOverride) && environment.sources.Count > 0)
            {
                environment.sources[0].phoneIp = phoneIpOverride.Trim();
                environment.Persist();
            }

            ConnectEntries(environment.sources);
            environment.wantConnected = true;
            environment.Persist();
        }

        /// <summary>
        /// 用**给定的一组条目**启动（不动工程设置）。验证用例用它，避免测试去改用户的设置文件。
        /// </summary>
        public static void ConnectEntries(List<HoFaceSourceEntry> entries)
        {
            Disconnect(userInitiated: false);
            Merged.Clear();
            MergedAt.Clear();
            Latest.Clear();
            LatestTime.Clear();
            LastFrameTime = 0;
            ConnectStartedAt = 0;
            ConnectionError = "";

            var failures = new List<string>();
            if (entries != null)
                foreach (var entry in entries)
                {
                    if (entry == null || !entry.enabled) continue;
                    var receiver = HoFaceReceiverFactory.Create(entry.kind);
                    Entries.Add(entry);
                    try { receiver.Start(entry); }
                    catch (Exception e) { failures.Add(receiver.DisplayName + "：" + e.Message); }
                    Receivers.Add(receiver);
                    Latest.Add(null);
                    LatestTime.Add(0);
                }

            ConnectStartedAt = Now;
            if (failures.Count > 0)
                ConnectionError = "连接失败：" + string.Join("；", failures) + "（检查本机 UDP 端口是否被占用）";
        }

        public static void Disconnect(bool userInitiated = true)
        {
            if (userInitiated)
            {
                var environment = HoFaceInputEnvironment.instance;
                environment.wantConnected = false;   // 用户主动断开：别再自动接回来
                environment.Persist();
            }

            foreach (var receiver in Receivers) receiver.Dispose();
            Receivers.Clear();
            Entries.Clear();
            Latest.Clear();
            LatestTime.Clear();
            Merged.Clear();
            MergedAt.Clear();
        }

        public static HoFaceAnimationSession Session(HoFaceDebugSettings settings) => settings != null && Sessions.TryGetValue(settings, out var session) ? session : null;

        public static string Error(HoFaceDebugSettings settings)
        {
            if (settings == null || !Errors.TryGetValue(settings, out string error)) return "";
            bool retrying = settings.startOnPlay && !UserStopped.Contains(settings) && !Sessions.ContainsKey(settings);
            return retrying
                ? error + "（自动重试中，第 " + (Attempts.TryGetValue(settings, out int attempts) ? attempts : 1) + " 次）"
                : error;
        }

        public static void Start(HoFaceDebugSettings settings)
        {
            DisposeSession(settings);
            UserStopped.Remove(settings);
            try
            {
                foreach (var pair in Sessions)
                    if (pair.Value.Settings.TargetAnimator() == settings.TargetAnimator()) throw new InvalidOperationException("该 Animator 已有一个面捕会话。");
                Sessions.Add(settings, new HoFaceAnimationSession(settings));
                Errors.Remove(settings);
                Attempts.Remove(settings);
            }
            catch (Exception e) { Fail(settings, e.Message); }
        }

        /// <summary>用户自己停的：记下来，本次 Play 内不再自动拉起。</summary>
        public static void Stop(HoFaceDebugSettings settings)
        {
            if (ReferenceEquals(settings, null)) return;
            UserStopped.Add(settings);
            Attempts.Remove(settings);
            DisposeSession(settings);
        }

        /// <summary>收摊，但**不代表用户意图** —— 组件被禁用、退出播放、出错都属这一类，之后还能自动拉起来。</summary>
        private static void DisposeSession(HoFaceDebugSettings settings)
        {
            if (settings == null) return;
            if (Sessions.TryGetValue(settings, out var session)) { Sessions.Remove(settings); session.Dispose(); }
        }

        /// <summary>驱动挂了：收摊 + 记下原因 + 1 秒后再试（「运行时自动开始」打开时会真的重试）。</summary>
        private static void Fail(HoFaceDebugSettings settings, string message)
        {
            if (ReferenceEquals(settings, null)) return;
            DisposeSession(settings);
            Errors[settings] = message;
            Attempts[settings] = Attempts.TryGetValue(settings, out int attempts) ? attempts + 1 : 1;
            NextTry[settings] = Now + 1.0;
        }

        public static void Tick(HoFaceDebugSettings settings)
        {
            if (!Application.isPlaying) return;
            UpdateInput();

            // 「运行时自动开始」：进播放就自动驱动；**失败后自动重试**（用户自己停过的不再拉起）。
            if (settings.startOnPlay && !UserStopped.Contains(settings) && !Sessions.ContainsKey(settings)
                && (!NextTry.TryGetValue(settings, out double next) || Now >= next))
            {
                NextTry[settings] = Now + 1.0;
                Start(settings);
            }

            if (!Sessions.TryGetValue(settings, out var session)) return;
            try { session.Tick(Time.deltaTime); }
            catch (Exception e) { Fail(settings, e.Message); }
        }

        public static void LateTick(HoFaceDebugSettings settings)
        {
            if (!Application.isPlaying) return;
            if (!Sessions.TryGetValue(settings, out var session)) return;
            try { session.WriteOutputs(); }
            catch (Exception e) { Fail(settings, e.Message); }
        }

        public static void HostUpdate()
        {
            UpdateInput();
            var dead = new List<HoFaceDebugSettings>();
            // 以前这里还看"组件是不是启用的" —— 现在没有组件了，会话的存活只由播放模式决定，
            // 启不启用由宿主（面板/菜单）说了算。
            foreach (var pair in Sessions)
                if (pair.Key == null || !Application.isPlaying) dead.Add(pair.Key);
            foreach (var settings in dead) DisposeSession(settings);   // 不是用户停的：之后还能自动拉回来

            // 连接不跟着播放模式一起断（也兜住接收线程真死掉的情况）—— 见 TryReconnect 的注释。
            TryReconnect();
        }

        /// <summary>
        /// 把连接接回来。**为什么需要**：进播放模式会域重载，我们在 ExitingEditMode 主动收掉 socket
        /// （不收的话端口占着、新域绑定不上），而静态字段连同"用户想连着"这个意图一起被清掉 ——
        /// 结果就是"按一下 Play 等于把连接丢了"。所以那个意图改成持久化，进播放后自己接回来。
        /// 同一条路也兜住接收线程真的死掉（非瞬态错误）。
        /// </summary>
        private static void TryReconnect()
        {
            if (Connected) return;
            // **刚连上就别动它**：Thread.IsAlive 在 Start() 之后有一瞬间还是 false，
            // 这段宽限期避免把刚建好的接收线程掐掉重来（重来会丢包，表现成"跑着跑着断了"）。
            if (ConnectStartedAt > 0 && Now - ConnectStartedAt < 3.0) return;
            if (Now < nextConnectTry) return;
            var environment = HoFaceInputEnvironment.instance;
            if (!environment.wantConnected) return;
            nextConnectTry = Now + 2.0;
            Connect();
        }

        /// <summary>
        /// 每帧把各源的"最新一包"收进来，再按顺序合成 <see cref="Merged"/>。
        /// 逐线名判：**排在前面的新鲜源先占位**，后面的源只能补前面没有的名字。
        /// </summary>
        private static void UpdateInput()
        {
            var environment = HoFaceInputEnvironment.instance;
            bool missing = Receivers.Count == 0 && environment.wantConnected;
            if (missing) return;

            double now = Now;
            for (int i = 0; i < Receivers.Count; i++)
            {
                if (Receivers[i].TryTake(out var packet, out double timestamp))
                {
                    Latest[i] = packet;
                    LatestTime[i] = timestamp;
                    LastFrameTime = timestamp;
                }
            }

            Merged.Clear();
            for (int i = 0; i < Receivers.Count; i++)
            {
                var packet = Latest[i];
                if (packet == null) continue;
                if (now - LatestTime[i] > FreshSeconds) continue;   // 这条源不新鲜了，让位给后面的
                foreach (var pair in packet.Values)
                    if (!Merged.ContainsKey(pair.Key)) { Merged[pair.Key] = pair.Value; MergedAt[pair.Key] = LatestTime[i]; }
            }
        }

        private static void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode) Shutdown();
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                UserStopped.Clear();
                Errors.Clear();
                NextTry.Clear();
                Attempts.Clear();
                TryReconnect();   // 域重载把连接收掉了：进播放立刻接回来，别让用户看到"未连接"
            }
        }

        private static void Shutdown()
        {
            foreach (var pair in Sessions) pair.Value.Dispose();
            Sessions.Clear();
            UserStopped.Clear();
            NextTry.Clear();
            Attempts.Clear();

            // 迁移/兜底：如果收摊这一刻**还连着**，就把"用户想连着"记下来。
            if (Receivers.Any(receiver => receiver.Running))
            {
                var environment = HoFaceInputEnvironment.instance;
                environment.wantConnected = true;
                environment.Persist();
            }

            Disconnect(userInitiated: false);   // 收 socket 是为了让新域能绑上端口，不代表用户想断开
        }

        /// <summary>共享时钟：接收端、会话与面板都用它算"多久没收到包了"（唯一定义在 <see cref="HoFaceClock"/>）。</summary>
        public static double Now => HoFaceClock.Now;
    }
}
