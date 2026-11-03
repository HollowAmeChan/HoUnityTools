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
        private HoFaceIrEvaluator irEvaluator;
        private HoFaceRecordingWriter recording;
        private List<HoFaceRecordedFrame> replayFrames;
        private int replayIndex;
        private bool replayPlaying;
        private double replayOffset;
        public readonly VtsIphoneReceiver Receiver = new VtsIphoneReceiver();
        public readonly HoFaceRawForwardSender RawForward = new HoFaceRawForwardSender();
        public readonly HoFacePreviewSender Preview = new HoFacePreviewSender();
        public AuthoringMode Mode { get; private set; } = AuthoringMode.Preview;
        public string RawForwardAddress { get; set; } = "127.0.0.1";
        public int RawForwardPort { get; set; } = 21415;
        public bool HasDocument => valueSession != null && irEvaluator != null;
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
        public IReadOnlyDictionary<string, float> Values => valueSession?.Values;
        public IReadOnlyDictionary<string, float> Weights => irEvaluator?.Values;
        public event Action<VtsFrame> RawFrameForward;
        public event Action<IReadOnlyDictionary<string, float>, IReadOnlyDictionary<string, float>, bool> PreviewUpdated;
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
            irEvaluator = new HoFaceIrEvaluator(document.Ir);
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
            if (!HasDocument) throw new InvalidOperationException("请先打开并校验 value + IR 文档。");
            Receiver.Start(options);
            clock.Restart();
        }

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
            if (record && recording != null)
                recording.Write(new HoFaceRecordedFrame { time = time, tracked = frame.FaceFound, values = new Dictionary<string, float>(frame.Values) });
            if (Mode == AuthoringMode.RawForward)
            {
                if (Preview.Running) StopPreviewOutput();
                if (!RawForward.Running) RawForward.Start(RawForwardAddress, RawForwardPort);
                RawForward.Send(frame.Values, frame.FaceFound, time);
                RawFrameForward?.Invoke(frame);
                return;
            }
            if (RawForward.Running) StopRawOutput();
            if (!HasDocument) return;
            valueSession.Evaluate(frame.Values, time);
            irEvaluator.Evaluate(valueSession.Values);
            PreviewUpdated?.Invoke(valueSession.Values, irEvaluator.Values, frame.FaceFound);
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
            valueSession?.Evaluator.Parameters.Clear();
            irEvaluator?.Values.Clear();
            irEvaluator?.UnclampedValues.Clear();
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
        }
        public void PlayReplay()
        {
            if (replayFrames == null || replayFrames.Count == 0) throw new InvalidOperationException("没有可播放的录制。");
            if (replayIndex >= replayFrames.Count) replayIndex = 0;
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
