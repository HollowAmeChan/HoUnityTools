using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>Debug input adapter for an already configured character; components own all solving.</summary>
    public sealed class HoVtsDebugSession : IDisposable
    {
        private static readonly HashSet<HoVtsParameterReceiver> owners = new HashSet<HoVtsParameterReceiver>();
        private readonly string source = "UnityDebug/" + Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, float> raw = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> inputPins = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> outputPins = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly bool automaticInput;
        private double lastSourceTime = -1;
        private int lastUnityFrame = -1;
        private bool pinsChanged;
        public GameObject Target { get; }
        public HoVtsParameterReceiver Receiver { get; }
        public bool Disposed { get; private set; }
        public string SourceId => source;
        public IReadOnlyDictionary<string, float> InputPins => inputPins;
        public IReadOnlyDictionary<string, float> OutputPins => outputPins;
        public void ReleaseInput(string name) { inputPins.Remove(name); pinsChanged = true; }
        public void ReleaseOutput(string name) { outputPins.Remove(name); pinsChanged = true; }

        public HoVtsDebugSession(GameObject target, bool automaticInput = true)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("请进入播放模式后开始角色调试。");
            if (target == null || EditorUtility.IsPersistent(target)) throw new InvalidOperationException("请选择场景中的调试对象。");
            var receivers = target.GetComponentsInChildren<HoVtsParameterReceiver>(true);
            if (receivers.Length != 1) throw new InvalidOperationException(receivers.Length == 0 ? "对象上没有 HoVtsParameterReceiver" : "对象下有多个参数接收器，请缩小选择范围。");
            Receiver = receivers[0];
            if (!Receiver.isActiveAndEnabled) throw new InvalidOperationException("参数接收器未启用。");
            if (Receiver.HasFrame) throw new InvalidOperationException("角色已有输入，请先停止原输入再开始调试。");
            if (!owners.Add(Receiver)) throw new InvalidOperationException("这个角色已在调试中。");
            Target = target;
            this.automaticInput = automaticInput;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.playModeStateChanged += PlayState;
        }

        public void PinInput(string name, float value) { Pin(inputPins, name, value); }
        public void PinOutput(string name, float value) { Pin(outputPins, name, value); }
        private void Pin(Dictionary<string, float> pins, string name, float value)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(HoVtsDebugSession));
            if (string.IsNullOrEmpty(name) || float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("覆盖名称和值必须有效。");
            pins[name] = value; pinsChanged = true;
        }
        public void ClearPins() { inputPins.Clear(); outputPins.Clear(); pinsChanged = true; }

        public void Feed(Dictionary<string, float> values, bool tracked, double now)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(HoVtsDebugSession));
            if (Receiver == null || !Receiver.isActiveAndEnabled) throw new InvalidOperationException("参数接收器不可用。");
            if (Receiver.HasFrame && Receiver.Source != source)
            {
                Dispose();
                throw new InvalidOperationException("角色输入已由其他来源接管，调试已停止。");
            }
            Receiver.SetDebugOverrides(inputPins, outputPins, source);
            Receiver.ApplyRawFrame(values, tracked, now, source);
            pinsChanged = false;
        }

        private void Update()
        {
            if (Disposed) return;
            if (Target == null || Receiver == null || !Receiver.isActiveAndEnabled) { Dispose(); return; }
            if (!automaticInput || !EditorApplication.isPlaying || lastUnityFrame == Time.frameCount) return;
            lastUnityFrame = Time.frameCount;
            double stamp = HoFaceInputHub.LastFrameTime;
            bool newPacket = stamp > 0 && stamp != lastSourceTime;
            if (!newPacket && !pinsChanged) return;
            if (!newPacket && Receiver.HasFrame)
            {
                if (Receiver.Source != source) { Dispose(); return; }
                Receiver.SetDebugOverrides(inputPins, outputPins, source);
                Receiver.Tick(Time.realtimeSinceStartupAsDouble);
                pinsChanged = false;
                return;
            }
            if (newPacket)
            {
                raw.Clear();
                foreach (var pair in HoFaceInputHub.MergedValues) raw[pair.Key] = pair.Value;
                lastSourceTime = stamp;
            }
            bool fresh = HoFaceInputHub.Connected && stamp > 0 && HoFaceClock.Now - stamp <= Receiver.InputTimeoutSeconds;
            bool tracked = fresh && raw.TryGetValue("FaceFound", out float face) && face > .5f;
            // Network timestamps use Stopwatch; receiver lifecycle uses Unity's local clock.
            Feed(raw, tracked, Time.realtimeSinceStartupAsDouble);
        }

        private void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Dispose(); }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.playModeStateChanged -= PlayState;
            owners.Remove(Receiver);
            if (Receiver != null)
            {
                Receiver.ClearDebugOverrides();
                if (Receiver.Source == source)
                {
                    Receiver.ClearFrame();
                    if (Target != null)
                    {
                        foreach (var controller in Target.GetComponentsInChildren<HoVtsController>(true))
                            if (controller.ParameterReceiver == Receiver) controller.Stop();
                        foreach (var pose in Target.GetComponentsInChildren<HoVtsLookAt>(true))
                            if (pose.ParameterReceiver == Receiver) pose.Stop();
                    }
                }
            }
        }
    }
}
