using System;
using System.Collections.Generic;
using System.Diagnostics;
using HoFace.Transport;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public enum AuthoringMode { Preview, RawForward }

    public sealed class AuthoringSession : IDisposable
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Stopwatch replayClock = new Stopwatch();
        private HoFaceValueSession valueSession;
        private HoFaceValueSession effectiveValueSession;
        private HoFaceIrEvaluator irEvaluator;
        private HoFaceIrEvaluator liveIrEvaluator;
        private HoFaceRecordingWriter recording;
        private List<HoFaceRecordedFrame> replayFrames;
        private int replayIndex;
        private bool replayPlaying;
        private double replayOffset;
        private double lastProcessedTime = -1;
        private readonly Dictionary<string, float> rawOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> controlOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> valueOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> irOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly VtsIphoneReceiver Receiver = new VtsIphoneReceiver();
        public readonly HoFaceRawForwardSender RawForward = new HoFaceRawForwardSender();
        public readonly HoFacePreviewSender Preview = new HoFacePreviewSender();
        public AuthoringMode Mode { get; private set; } = AuthoringMode.Preview;
        public string RawForwardAddress { get; set; } = "127.0.0.1";
        public int RawForwardPort { get; set; } = 21415;
        public bool HasDocument => valueSession != null && effectiveValueSession != null && irEvaluator != null && liveIrEvaluator != null;
        public bool Running => Receiver.Running;
        public string TransportStatus => Receiver.Status;
        public string PreviewStatus => Preview.Status;
        public long Frames => Receiver.Packets;
        public long InvalidFrames => Receiver.InvalidPackets;
        public string RawForwardStatus => RawForward.Status;
        public bool Recording => recording != null;
        public bool ReplayLoaded => replayFrames != null;
        public bool ReplayPlaying => replayPlaying;
        public int RecordedFrames => recording?.Frames ?? 0;
        public int ReplayIndex => replayIndex;
        public IReadOnlyDictionary<string, float> Values => effectiveValueSession?.Values;
        public IReadOnlyDictionary<string, float> LiveValues => valueSession?.LiveValues;
        public IReadOnlyDictionary<string, float> Weights => irEvaluator?.Values;
        public IReadOnlyDictionary<string, float> LiveWeights => liveIrEvaluator?.Values;
        public IReadOnlyDictionary<string, float> UnclampedWeights => irEvaluator?.UnclampedValues;
        public IReadOnlyDictionary<string, float> LiveUnclampedWeights => liveIrEvaluator?.UnclampedValues;
        public IReadOnlyList<float> GetEffectiveChildWeights(string nodeId) =>
            irEvaluator == null ? Array.Empty<float>() : irEvaluator.CopyChildWeights(nodeId);
        public IReadOnlyDictionary<string, float> RawOverrides => rawOverrides;
        public IReadOnlyDictionary<string, float> ControlOverrides => controlOverrides;
        public IReadOnlyList<ControlInputDefinition> ControlInputs { get; private set; } = Array.Empty<ControlInputDefinition>();
        public IReadOnlyDictionary<string, float> ValueOverrides => valueOverrides;
        public IReadOnlyDictionary<string, float> IrOverrides => irOverrides;
        public AuthoringMonitorFrame LatestMonitorFrame { get; private set; }
        public event Action<VtsFrame> RawFrameForward;
        public event Action<IReadOnlyDictionary<string, float>, IReadOnlyDictionary<string, float>, bool> PreviewUpdated;
        public event Action<AuthoringMonitorFrame> MonitorUpdated;
        public event Action OutputCleared;
        public event Action<string> StatusChanged;

        public AuthoringSession()
        {
            Receiver.FrameReceived += OnFrame;
            Receiver.StatusChanged += status => StatusChanged?.Invoke(status);
        }

        public void Configure(AuthoringDocument document)
        {
            if (document?.Value == null || document.Ir == null) throw new ArgumentException("需要先打开 value 与 IR 文档。");
            if (HasDocument && Preview.Running)
                Preview.Send(Values, Weights, false, clock.Elapsed.TotalSeconds);
            valueSession = new HoFaceValueSession(document.Value, 1);
            effectiveValueSession = new HoFaceValueSession(document.Value, 1);
            ControlInputs = InputMonitorSources.Controls(document.Value);
            var validControls = new HashSet<string>();
            foreach (var control in ControlInputs) validControls.Add(control.Key);
            foreach (var key in new List<string>(controlOverrides.Keys))
                if (!validControls.Contains(key)) controlOverrides.Remove(key);
            // Retire older raw-stage pins for configured non-phone sources.
            foreach (var control in ControlInputs)
                foreach (var source in control.Sources)
                    if (!InputMonitorSources.IsPhoneField(source)) rawOverrides.Remove(source);
            effectiveValueSession.Evaluator.SetOverrides(controlOverrides, valueOverrides);
            irEvaluator = new HoFaceIrEvaluator(document.Ir);
            liveIrEvaluator = new HoFaceIrEvaluator(document.Ir);
            LatestMonitorFrame = null;
            lastProcessedTime = -1;
            clock.Restart();
            StatusChanged?.Invoke("文档已载入");
        }

        public void SetMode(AuthoringMode mode)
        {
            if (Mode == mode) return;
            ReleaseActiveOutput();
            Mode = mode;
            ClearOutputs();
            if (Mode == AuthoringMode.RawForward) RawForward.Start(RawForwardAddress, RawForwardPort);
            StatusChanged?.Invoke(mode == AuthoringMode.Preview ? "制作预览：外部求值" : "裸值转发：等待角色桥接");
        }

        public void Start(VtsIphoneOptions options)
        {
            if (Mode != AuthoringMode.RawForward && !HasDocument)
                throw new InvalidOperationException("权重预览需要先打开并校验 value + IR 文档；原始转发模式可直接连接。");
            Receiver.Start(options);
            clock.Restart();
        }

        public void SetMonitorOverride(MonitorStage stage, string parameter, float? value)
        {
            if (string.IsNullOrWhiteSpace(parameter)) throw new ArgumentException("参数名不能为空。", nameof(parameter));
            if (value.HasValue && (float.IsNaN(value.Value) || float.IsInfinity(value.Value)))
                throw new ArgumentOutOfRangeException(nameof(value), "临时覆盖必须是有限数值。");
            var overrides = OverridesFor(stage);
            if (stage == MonitorStage.Control && !HasControl(parameter))
                throw new ArgumentException("没有该控制输入：" + parameter, nameof(parameter));
            if (value.HasValue) overrides[parameter] = value.Value;
            else overrides.Remove(parameter);
            if (stage == MonitorStage.Control) RefreshControlOverrides();
        }

        public void ClearMonitorOverrides(MonitorStage? stage = null)
        {
            if (!stage.HasValue || stage == MonitorStage.Raw) rawOverrides.Clear();
            if (!stage.HasValue || stage == MonitorStage.Control) controlOverrides.Clear();
            if (!stage.HasValue || stage == MonitorStage.Value) valueOverrides.Clear();
            if (!stage.HasValue || stage == MonitorStage.Ir) irOverrides.Clear();
            if (!stage.HasValue || stage == MonitorStage.Control) RefreshControlOverrides();
        }

        private bool HasControl(string parameter)
        {
            foreach (var control in ControlInputs)
                if (control.Key == parameter) return true;
            return false;
        }

        private Dictionary<string, float> OverridesFor(MonitorStage stage) => stage switch
        {
            MonitorStage.Raw => rawOverrides,
            MonitorStage.Control => controlOverrides,
            MonitorStage.Value => valueOverrides,
            MonitorStage.Ir => irOverrides,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        private static void ApplyOverrides(Dictionary<string, float> values, IReadOnlyDictionary<string, float> overrides)
        {
            foreach (var pair in overrides) values[pair.Key] = pair.Value;
        }

        private static Dictionary<string, float> Copy(IReadOnlyDictionary<string, float> values) =>
            values == null ? new Dictionary<string, float>(StringComparer.Ordinal) :
                new Dictionary<string, float>(values, StringComparer.Ordinal);

        public void Stop()
        {
            if (Mode == AuthoringMode.RawForward) StopRawOutput();
            else if (Preview.Running) Preview.Send(Values, Weights, false, clock.Elapsed.TotalSeconds);
            ClearOutputs();
            Receiver.Stop();
        }

        public void Poll()
        {
            Receiver.Poll();
            RawForward.Poll();
            if (!replayPlaying || replayFrames == null) return;
            double time = replayOffset + replayClock.Elapsed.TotalSeconds;
            while (replayIndex < replayFrames.Count && replayFrames[replayIndex].time <= time)
            {
                var frame = replayFrames[replayIndex++];
                ProcessFrame(new VtsFrame(new Dictionary<string, float>(frame.values), frame.tracked), frame.time, false);
            }
            if (replayIndex >= replayFrames.Count) PauseReplay();
        }

        private void OnFrame(VtsFrame frame) => ProcessFrame(frame, clock.Elapsed.TotalSeconds, true);

        private void ProcessFrame(VtsFrame frame, double time, bool record)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) return;
            if (lastProcessedTime >= 0 && time < lastProcessedTime)
            {
                // A reconnect, replay seek, or document reload can restart the
                // host clock. Start a fresh stateful value evaluation instead of
                // letting one stale frame terminate the Studio dispatcher.
                valueSession?.Reset();
                effectiveValueSession?.Reset();
                effectiveValueSession?.Evaluator.SetOverrides(controlOverrides, valueOverrides);
                lastProcessedTime = -1;
            }
            lastProcessedTime = time;
            if (record && recording != null)
                recording.Write(new HoFaceRecordedFrame { time = time, tracked = frame.FaceFound, values = new Dictionary<string, float>(frame.Values) });
            var raw = Copy(frame.Values);
            var effectiveRaw = Copy(raw);
            ApplyOverrides(effectiveRaw, rawOverrides);
            if (Mode == AuthoringMode.RawForward)
            {
                if (Preview.Running) StopPreviewOutput();
                if (!RawForward.Running) RawForward.Start(RawForwardAddress, RawForwardPort);
                RawForward.Send(effectiveRaw, frame.FaceFound, time);
                var monitor = EvaluateMonitor(raw, effectiveRaw, frame.FaceFound, time);
                LatestMonitorFrame = monitor;
                MonitorUpdated?.Invoke(monitor);
                RawFrameForward?.Invoke(new VtsFrame(effectiveRaw, frame.FaceFound));
                return;
            }
            if (RawForward.Running) StopRawOutput();
            if (!HasDocument)
            {
                LatestMonitorFrame = new AuthoringMonitorFrame(raw, effectiveRaw, null, null, null, null, frame.FaceFound, time);
                MonitorUpdated?.Invoke(LatestMonitorFrame);
                return;
            }
            var result = EvaluateMonitorStages(raw, effectiveRaw, frame.FaceFound, time);
            LatestMonitorFrame = result;
            MonitorUpdated?.Invoke(result);
            PreviewUpdated?.Invoke(result.ValuesEffective, result.WeightsEffective,
                frame.FaceFound || rawOverrides.Count > 0 || controlOverrides.Count > 0 || valueOverrides.Count > 0 || irOverrides.Count > 0);
        }

        private AuthoringMonitorFrame EvaluateMonitor(
            IReadOnlyDictionary<string, float> raw,
            IReadOnlyDictionary<string, float> effectiveRaw,
            bool tracked,
            double time) => HasDocument
                ? EvaluateMonitorStages(raw, effectiveRaw, tracked, time)
                : new AuthoringMonitorFrame(raw, effectiveRaw, null, null, null, null, tracked, time);

        private AuthoringMonitorFrame EvaluateMonitorStages(
            IReadOnlyDictionary<string, float> raw,
            IReadOnlyDictionary<string, float> effectiveRaw,
            bool tracked,
            double time)
        {
            valueSession.Evaluator.SetOverrides(null, null);
            valueSession.Evaluate(new Dictionary<string, float>(raw, StringComparer.Ordinal), time);
            var liveValues = Copy(valueSession.LiveValues);
            effectiveValueSession.Evaluator.SetOverrides(controlOverrides, valueOverrides);
            effectiveValueSession.Evaluate(new Dictionary<string, float>(effectiveRaw, StringComparer.Ordinal), time);
            var effectiveValues = Copy(effectiveValueSession.Values);

            liveIrEvaluator.Evaluate(liveValues);
            irEvaluator.Evaluate(effectiveValues, irOverrides);
            var liveWeights = Copy(liveIrEvaluator.Values);
            var effectiveWeights = Copy(irEvaluator.Values);
            return CreateMonitorFrame(raw, effectiveRaw, liveValues, effectiveValues,
                liveWeights, effectiveWeights, tracked, time);
        }

        private AuthoringMonitorFrame CreateMonitorFrame(
            IReadOnlyDictionary<string, float> raw, IReadOnlyDictionary<string, float> effectiveRaw,
            IReadOnlyDictionary<string, float> liveValues, IReadOnlyDictionary<string, float> effectiveValues,
            IReadOnlyDictionary<string, float> liveWeights, IReadOnlyDictionary<string, float> effectiveWeights,
            bool tracked, double time)
        {
            var controls = new Dictionary<string, float>(StringComparer.Ordinal);
            var effective = new Dictionary<string, float>(StringComparer.Ordinal);
            var fresh = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var control in ControlInputs)
            {
                if (!valueSession.Evaluator.TryReadInput(control.Index, out float source, out _, out bool received)) continue;
                controls[control.Key] = source;
                fresh[control.Key] = received;
                effectiveValueSession.Evaluator.TryReadInput(control.Index, out _, out float result, out _);
                effective[control.Key] = result;
            }
            return new AuthoringMonitorFrame(raw, effectiveRaw, liveValues, effectiveValues, liveWeights,
                effectiveWeights, tracked, time, controls, effective, fresh);
        }

        private void RefreshControlOverrides()
        {
            if (!HasDocument) return;
            var previous = LatestMonitorFrame;
            var raw = Copy(previous?.Raw);
            var effectiveRaw = Copy(raw);
            ApplyOverrides(effectiveRaw, rawOverrides);
            double time = lastProcessedTime < 0 ? 0 : lastProcessedTime;
            // A UI pin is not a phone frame: do not advance session clocks,
            // recording, random frame indices, or the raw forwarding socket.
            if (previous == null) valueSession.Evaluator.Evaluate(raw, 0, time);
            effectiveValueSession.Evaluator.SetOverrides(controlOverrides, valueOverrides);
            effectiveValueSession.Evaluator.Evaluate(effectiveRaw, 0, time);
            liveIrEvaluator.Evaluate(valueSession.LiveValues);
            irEvaluator.Evaluate(effectiveValueSession.Values, irOverrides);
            LatestMonitorFrame = CreateMonitorFrame(raw, effectiveRaw, valueSession.LiveValues, effectiveValueSession.Values,
                liveIrEvaluator.Values, irEvaluator.Values, previous?.Tracked ?? false, time);
            MonitorUpdated?.Invoke(LatestMonitorFrame);
            if (Mode == AuthoringMode.Preview)
                PreviewUpdated?.Invoke(LatestMonitorFrame.ValuesEffective, LatestMonitorFrame.WeightsEffective,
                    LatestMonitorFrame.Tracked || controlOverrides.Count > 0 || rawOverrides.Count > 0 ||
                    valueOverrides.Count > 0 || irOverrides.Count > 0);
        }

        private void ReleaseActiveOutput()
        {
            if (Mode == AuthoringMode.RawForward) StopRawOutput();
            else if (Preview.Running) StopPreviewOutput();
        }

        private void StopRawOutput()
        {
            if (!RawForward.Running) return;
            RawForward.Send(null, false, clock.Elapsed.TotalSeconds);
            RawForward.Stop();
        }

        private void StopPreviewOutput()
        {
            if (!Preview.Running) return;
            Preview.Send(Values, Weights, false, clock.Elapsed.TotalSeconds);
            Preview.Stop();
        }

        private void ClearOutputs()
        {
            LatestMonitorFrame = null;
            valueSession?.Evaluator.Parameters.Clear();
            valueSession?.Evaluator.LiveParameters.Clear();
            effectiveValueSession?.Evaluator.Parameters.Clear();
            effectiveValueSession?.Evaluator.LiveParameters.Clear();
            irEvaluator?.Values.Clear();
            irEvaluator?.LiveValues.Clear();
            irEvaluator?.UnclampedValues.Clear();
            liveIrEvaluator?.Values.Clear();
            liveIrEvaluator?.LiveValues.Clear();
            liveIrEvaluator?.UnclampedValues.Clear();
            OutputCleared?.Invoke();
        }

        public void StartRecording(string path, string valuePath, string irPath)
        {
            StopRecording();
            recording = new HoFaceRecordingWriter(path, valuePath, irPath);
        }
        public void StopRecording() { recording?.Dispose(); recording = null; }
        public void OpenReplay(string path)
        {
            replayFrames = HoFaceRecordingReader.Read(path);
            replayIndex = 0; replayPlaying = false; replayOffset = 0;
            ResetReplayEvaluation();
        }
        public void PlayReplay()
        {
            if (replayFrames == null || replayFrames.Count == 0) throw new InvalidOperationException("没有可播放的录制。");
            if (Receiver.Running) Stop();
            if (replayIndex >= replayFrames.Count) replayIndex = 0;
            if (replayIndex == 0) ResetReplayEvaluation();
            replayPlaying = true;
            replayClock.Restart();
            replayOffset = replayIndex < replayFrames.Count ? replayFrames[replayIndex].time : 0;
        }
        public void PauseReplay() { replayPlaying = false; replayClock.Stop(); }
        public void SeekReplay(double time)
        {
            if (replayFrames == null) throw new InvalidOperationException("没有打开录制。");
            bool wasPlaying = replayPlaying;
            double target = Math.Max(0, time);
            replayIndex = 0;
            while (replayIndex < replayFrames.Count && replayFrames[replayIndex].time < target) replayIndex++;
            replayOffset = target;
            if (wasPlaying) replayClock.Restart();
        }

        private void ResetReplayEvaluation()
        {
            valueSession?.Reset();
            effectiveValueSession?.Reset();
            effectiveValueSession?.Evaluator.SetOverrides(controlOverrides, valueOverrides);
            lastProcessedTime = -1;
            ClearOutputs();
        }

        public void Dispose()
        {
            StopRecording();
            ReleaseActiveOutput();
            ClearOutputs();
            Receiver.Dispose();
            Preview.Dispose();
            RawForward.Dispose();
        }
    }
}
