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
        private HoFaceValueSession valueSession;
        private HoFaceIrEvaluator irEvaluator;
        public readonly VtsIphoneReceiver Receiver = new VtsIphoneReceiver();
        public AuthoringMode Mode { get; private set; } = AuthoringMode.Preview;
        public bool HasDocument => valueSession != null && irEvaluator != null;
        public bool Running => Receiver.Running;
        public string TransportStatus => Receiver.Status;
        public long Frames => Receiver.Packets;
        public long InvalidFrames => Receiver.InvalidPackets;
        public IReadOnlyDictionary<string, float> Values => valueSession?.Values;
        public IReadOnlyDictionary<string, float> Weights => irEvaluator?.Values;
        public event Action<VtsFrame> RawFrameForward;
        public event Action<IReadOnlyDictionary<string, float>, IReadOnlyDictionary<string, float>> PreviewUpdated;
        public event Action<string> StatusChanged;

        public AuthoringSession()
        {
            Receiver.FrameReceived += OnFrame;
            Receiver.StatusChanged += status => StatusChanged?.Invoke(status);
        }

        public void Configure(AuthoringDocument document)
        {
            if (document?.Value == null || document.Ir == null) throw new ArgumentException("需要先打开 value 与 IR 文档。");
            valueSession = new HoFaceValueSession(document.Value, 1);
            irEvaluator = new HoFaceIrEvaluator(document.Ir);
            clock.Restart();
            StatusChanged?.Invoke("文档已载入");
        }

        public void SetMode(AuthoringMode mode)
        {
            Mode = mode;
            StatusChanged?.Invoke(mode == AuthoringMode.Preview ? "制作预览：外部求值" : "裸值转发：等待角色桥接");
        }

        public void Start(VtsIphoneOptions options)
        {
            if (!HasDocument) throw new InvalidOperationException("请先打开并校验 value + IR 文档。");
            Receiver.Start(options);
            clock.Restart();
        }

        public void Stop() => Receiver.Stop();

        public void Poll() => Receiver.Poll();

        private void OnFrame(VtsFrame frame)
        {
            if (Mode == AuthoringMode.RawForward)
            {
                RawFrameForward?.Invoke(frame);
                return;
            }
            if (!HasDocument) return;
            double time = clock.Elapsed.TotalSeconds;
            valueSession.Evaluate(frame.Values, time);
            irEvaluator.Evaluate(valueSession.Values);
            PreviewUpdated?.Invoke(valueSession.Values, irEvaluator.Values);
        }

        public void Dispose() => Receiver.Dispose();
    }
}
