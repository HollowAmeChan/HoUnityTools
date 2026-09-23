using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [FilePath("UserSettings/HoUnityTools/FaceTracking.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class HoFaceConnectionSettings : ScriptableSingleton<HoFaceConnectionSettings>
    {
        public string phoneIp = "192.168.1.100";

        /// <summary>
        /// 用户是否处于"想连着"的状态。**必须持久化**：进播放模式会发生域重载，静态字段会被清掉，
        /// 而这个意图得活着 —— 否则"按 Play"就等于把手机连接丢掉了（以前就是这样）。
        /// </summary>
        public bool wantConnected;

        public void Persist() => Save(true);
    }

    [InitializeOnLoad]
    public static class HoFaceInputHub
    {
        public static readonly float[] Raw = new float[52];
        public static readonly double[] ReceivedAt = new double[52];
        public static readonly IFacialMocapReceiver Receiver = new IFacialMocapReceiver();
        private static readonly Dictionary<HoFaceTrackingDebugger, HoFaceAnimationSession> Sessions = new Dictionary<HoFaceTrackingDebugger, HoFaceAnimationSession>();
        private static readonly Dictionary<HoFaceTrackingDebugger, string> Errors = new Dictionary<HoFaceTrackingDebugger, string>();
        /// <summary>用户自己按了「停止并交还」的 rig：本次 Play 内不再自动拉起（"运行时自动开始"不该跟用户对着干）。</summary>
        private static readonly HashSet<HoFaceTrackingDebugger> UserStopped = new HashSet<HoFaceTrackingDebugger>();
        /// <summary>每个 rig 下一次允许自动重试的时刻（单调时钟）—— 失败后 1 秒一次，不做热循环。</summary>
        private static readonly Dictionary<HoFaceTrackingDebugger, double> NextTry = new Dictionary<HoFaceTrackingDebugger, double>();
        /// <summary>自动重试次数，只用于面板上那句"自动重试中（第 N 次）"。</summary>
        private static readonly Dictionary<HoFaceTrackingDebugger, int> Attempts = new Dictionary<HoFaceTrackingDebugger, int>();
        private static double nextConnectTry;
        public static IFacialMocapPacket LastPacket { get; private set; }
        public static double LastFrameTime { get; private set; }
        /// <summary>本机开始监听、并把握手命令发出去的时刻。用来判断"等待响应"是不是等太久了。</summary>
        public static double ConnectStartedAt { get; private set; }
        public static string ConnectionError { get; private set; } = "";
        public static bool Connected => Receiver.Running;

        static HoFaceInputHub()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            HoFaceTrackingDebugger.EditorTick += Tick;
            HoFaceTrackingDebugger.EditorLateTick += LateTick;
            HoFaceTrackingDebugger.EditorDisabled += rig => DisposeSession(rig);
        }

        public static void Connect(string ip)
        {
            Disconnect(userInitiated: false);
            Array.Clear(Raw, 0, Raw.Length);
            Array.Clear(ReceivedAt, 0, ReceivedAt.Length);
            LastPacket = null;
            LastFrameTime = 0;
            ConnectStartedAt = 0;
            ConnectionError = "";
            try { Receiver.Start(ip); ConnectStartedAt = IFacialMocapReceiver.Now; }
            catch (Exception e) { ConnectionError = "连接失败：" + e.Message + "（检查本机 UDP 49983 是否被占用）"; }
            // 记下"用户想连着"这件事（连 IP 一起存），它是跨域重载活着的那一份状态。
            var settings = HoFaceConnectionSettings.instance;
            settings.phoneIp = ip;
            settings.wantConnected = true;
            settings.Persist();
        }

        public static void Disconnect(bool userInitiated = true)
        {
            if (userInitiated)
            {
                var settings = HoFaceConnectionSettings.instance;
                settings.wantConnected = false;   // 用户主动断开：别再自动接回来
                settings.Persist();
            }

            Receiver.Dispose();
            // Sessions remain available for manual sliders; live channels fade and release.
            for (int i = 0; i < ReceivedAt.Length; i++)
                if (ReceivedAt[i] > 0) ReceivedAt[i] = Math.Min(ReceivedAt[i], IFacialMocapReceiver.Now - 1);
        }

        public static HoFaceAnimationSession Session(HoFaceTrackingDebugger rig) => rig != null && Sessions.TryGetValue(rig, out var session) ? session : null;
        public static string Error(HoFaceTrackingDebugger rig)
        {
            if (rig == null || !Errors.TryGetValue(rig, out string error)) return "";
            bool retrying = rig.startOnPlay && !UserStopped.Contains(rig) && !Sessions.ContainsKey(rig);
            return retrying
                ? error + "（自动重试中，第 " + (Attempts.TryGetValue(rig, out int attempts) ? attempts : 1) + " 次）"
                : error;
        }

        public static void Start(HoFaceTrackingDebugger rig)
        {
            DisposeSession(rig);
            UserStopped.Remove(rig);
            try
            {
                foreach (var pair in Sessions)
                    if (pair.Value.Rig.targetAnimator == rig.targetAnimator) throw new InvalidOperationException("该 Animator 已有一个面捕会话。");
                Sessions.Add(rig, new HoFaceAnimationSession(rig));
                Errors.Remove(rig);
                Attempts.Remove(rig);
            }
            catch (Exception e) { Fail(rig, e.Message); }
        }

        /// <summary>用户自己停的：记下来，本次 Play 内不再自动拉起。</summary>
        public static void Stop(HoFaceTrackingDebugger rig)
        {
            if (ReferenceEquals(rig, null)) return;
            UserStopped.Add(rig);
            Attempts.Remove(rig);
            DisposeSession(rig);
        }

        /// <summary>收摊，但**不代表用户意图** —— 组件被禁用、退出播放、出错都属这一类，之后还能自动拉起来。</summary>
        private static void DisposeSession(HoFaceTrackingDebugger rig)
        {
            if (rig == null) return;
            if (Sessions.TryGetValue(rig, out var session)) { Sessions.Remove(rig); session.Dispose(); }
        }

        /// <summary>驱动挂了：收摊 + 记下原因 + 1 秒后再试（「运行时自动开始」打开时会真的重试）。</summary>
        private static void Fail(HoFaceTrackingDebugger rig, string message)
        {
            if (ReferenceEquals(rig, null)) return;
            DisposeSession(rig);
            Errors[rig] = message;
            Attempts[rig] = Attempts.TryGetValue(rig, out int attempts) ? attempts + 1 : 1;
            NextTry[rig] = IFacialMocapReceiver.Now + 1.0;
        }

        private static void Tick(HoFaceTrackingDebugger rig)
        {
            if (!Application.isPlaying) return;
            UpdateInput();

            // 「运行时自动开始」：进播放就自动驱动；**失败后自动重试**（用户自己停过的不再拉起）。
            if (rig.startOnPlay && !UserStopped.Contains(rig) && !Sessions.ContainsKey(rig)
                && (!NextTry.TryGetValue(rig, out double next) || IFacialMocapReceiver.Now >= next))
            {
                NextTry[rig] = IFacialMocapReceiver.Now + 1.0;
                Start(rig);
            }

            if (!Sessions.TryGetValue(rig, out var session)) return;
            try { session.Tick(Time.deltaTime); }
            catch (Exception e) { Fail(rig, e.Message); }
        }

        private static void LateTick(HoFaceTrackingDebugger rig)
        {
            if (!Application.isPlaying) return;
            if (!Sessions.TryGetValue(rig, out var session)) return;
            try { session.WriteOutputs(); }
            catch (Exception e) { Fail(rig, e.Message); }
        }

        private static void Update()
        {
            UpdateInput();
            var dead = new List<HoFaceTrackingDebugger>();
            foreach (var pair in Sessions)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || !Application.isPlaying) dead.Add(pair.Key);
            foreach (var rig in dead) DisposeSession(rig);   // 不是用户停的：之后还能自动拉回来

            // 连接不跟着播放模式一起断（也兜住接收线程真死掉的情况）—— 见 TryReconnect 的注释。
            TryReconnect();
        }

        /// <summary>
        /// 把手机连接接回来。**为什么需要**：进播放模式会域重载，我们在 ExitingEditMode 主动收掉 socket
        /// （不收的话端口占着、新域绑定不上），而静态字段连同"用户想连着"这个意图一起被清掉 ——
        /// 结果就是"按一下 Play 等于把连接丢了"。所以那个意图改成持久化，进播放后自己接回来。
        /// 同一条路也兜住接收线程真的死掉（非瞬态错误）。
        /// </summary>
        private static void TryReconnect()
        {
            if (Receiver.Running) return;
            if (IFacialMocapReceiver.Now < nextConnectTry) return;
            var settings = HoFaceConnectionSettings.instance;
            if (!settings.wantConnected || string.IsNullOrWhiteSpace(settings.phoneIp)) return;
            nextConnectTry = IFacialMocapReceiver.Now + 2.0;
            Connect(settings.phoneIp.Trim());
        }

        private static void UpdateInput()
        {
            if (!Receiver.TryTake(out var packet, out double timestamp)) return;
            LastPacket = packet;
            LastFrameTime = timestamp;
            for (int i = 0; i < Raw.Length; i++)
                if (packet.Present[i]) { Raw[i] = packet.Values[i]; ReceivedAt[i] = timestamp; }
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
            // （老版本的设置文件里没有 wantConnected 字段，靠这一步就能自动补上，
            // 不用让用户先进去手动连一次。）
            if (Receiver.Running)
            {
                var settings = HoFaceConnectionSettings.instance;
                settings.wantConnected = true;
                settings.Persist();
            }

            Disconnect(userInitiated: false);   // 收 socket 是为了让新域能绑上端口，不代表用户想断开
        }
    }
}
