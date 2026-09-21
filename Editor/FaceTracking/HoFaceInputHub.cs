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
        private static readonly HashSet<HoFaceTrackingDebugger> AutoStarted = new HashSet<HoFaceTrackingDebugger>();
        public static IFacialMocapPacket LastPacket { get; private set; }
        public static double LastFrameTime { get; private set; }
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
            HoFaceTrackingDebugger.EditorDisabled += Stop;
        }

        public static void Connect(string ip)
        {
            Disconnect();
            Array.Clear(Raw, 0, Raw.Length);
            Array.Clear(ReceivedAt, 0, ReceivedAt.Length);
            LastPacket = null;
            LastFrameTime = 0;
            ConnectionError = "";
            try { Receiver.Start(ip); }
            catch (Exception e) { ConnectionError = "连接失败：" + e.Message + "（检查本机 UDP 49983 是否被占用）"; }
        }

        public static void Disconnect()
        {
            Receiver.Dispose();
            // Sessions remain available for manual sliders; live channels fade and release.
            for (int i = 0; i < ReceivedAt.Length; i++)
                if (ReceivedAt[i] > 0) ReceivedAt[i] = Math.Min(ReceivedAt[i], IFacialMocapReceiver.Now - 1);
        }

        public static HoFaceAnimationSession Session(HoFaceTrackingDebugger rig) => rig != null && Sessions.TryGetValue(rig, out var session) ? session : null;
        public static string Error(HoFaceTrackingDebugger rig) => rig != null && Errors.TryGetValue(rig, out string error) ? error : "";

        public static void Start(HoFaceTrackingDebugger rig)
        {
            Stop(rig);
            try
            {
                foreach (var pair in Sessions)
                    if (pair.Value.Rig.targetAnimator == rig.targetAnimator) throw new InvalidOperationException("该 Animator 已有一个面捕会话。");
                Sessions.Add(rig, new HoFaceAnimationSession(rig));
                Errors.Remove(rig);
            }
            catch (Exception e) { Errors[rig] = e.Message; }
        }

        public static void Stop(HoFaceTrackingDebugger rig)
        {
            if (ReferenceEquals(rig, null)) return;
            AutoStarted.Add(rig);
            if (Sessions.TryGetValue(rig, out var session)) { Sessions.Remove(rig); session.Dispose(); }
        }

        private static void Tick(HoFaceTrackingDebugger rig)
        {
            if (!Application.isPlaying) return;
            UpdateInput();
            if (rig.startOnPlay && AutoStarted.Add(rig)) Start(rig);
            if (!Sessions.TryGetValue(rig, out var session)) return;
            try { session.Tick(Time.deltaTime); }
            catch (Exception e) { Stop(rig); Errors[rig] = e.Message; }
        }

        private static void LateTick(HoFaceTrackingDebugger rig)
        {
            if (!Application.isPlaying) return;
            if (!Sessions.TryGetValue(rig, out var session)) return;
            try { session.WriteOutputs(); }
            catch (Exception e) { Stop(rig); Errors[rig] = e.Message; }
        }

        private static void Update()
        {
            UpdateInput();
            var dead = new List<HoFaceTrackingDebugger>();
            foreach (var pair in Sessions)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || !Application.isPlaying) dead.Add(pair.Key);
            foreach (var rig in dead) Stop(rig);
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
            if (state == PlayModeStateChange.EnteredPlayMode) { AutoStarted.Clear(); Errors.Clear(); }
        }

        private static void Shutdown()
        {
            foreach (var pair in Sessions) pair.Value.Dispose();
            Sessions.Clear();
            AutoStarted.Clear();
            Disconnect();
        }
    }
}
