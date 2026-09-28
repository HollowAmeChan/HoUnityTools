using System;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [Serializable] public sealed class HoFaceTraceValue
    {
        public string name;
        public float value;
        public bool valid;
        public double receivedAt;
        public HoFaceTraceValue(string name, float value, double receivedAt = 0)
        { this.name = name; this.value = Safe(value); valid = Finite(value); this.receivedAt = receivedAt; }
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static float Safe(float v) => Finite(v) ? v : 0;
    }
    [Serializable] public sealed class HoFaceTraceInput
    {
        public int row;
        public string name;
        public bool fresh, valid;
        public float configured, effective;
    }
    [Serializable] public sealed class HoFaceTraceOutput
    {
        public int row;
        public string name;
        public bool expressionActive, valid;
        public float expression, curve, modified, published;
    }
    [Serializable] public sealed class HoFaceTraceFrame
    {
        public string kind = "sample";
        public int sample, unityFrame, revision;
        public double elapsed, clock, lastReceivedAt;
        public float deltaTime;
        public bool connected;
        public HoFaceTraceValue[] wires, overrides;
        public HoFaceTraceInput[] inputs;
        public HoFaceTraceOutput[] outputs;
    }

    /// <summary>Records completed solves, optionally decimated. Times are actual observations, not a fabricated fixed grid.</summary>
    public sealed class HoFaceTraceRecorder : IDisposable
    {
        [Serializable] private sealed class Header
        {
            public string kind = "header", format = "ho-face-trace", startedUtc, label, profileJson, settingsJson;
            public int version = 1;
            public double intervalSeconds, durationSeconds;
            public string timing = "Completed Unity solves; merged latest input, not every network packet. No interpolation or catch-up samples.";
        }
        [Serializable] private sealed class Footer
        {
            public string kind = "end", reason;
            public int samples;
            public double elapsed;
        }

        private readonly HoFaceAnimationSession session;
        private readonly double start, duration;
        private readonly int revision;
        private readonly HoFaceTraceSchedule schedule;
        private StreamWriter writer;
        public bool Recording => writer != null;
        public string FilePath { get; }
        public int Samples { get; private set; }
        public double Elapsed => Math.Max(0, HoFaceClock.Now - start);

        public HoFaceTraceRecorder(HoFaceAnimationSession session, string label, double intervalSeconds,
            double durationSeconds = 5, string directory = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (durationSeconds <= 0 || durationSeconds > 60 || double.IsNaN(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            schedule = new HoFaceTraceSchedule(intervalSeconds);
            this.session = session;
            duration = durationSeconds;
            revision = session.TraceRevision;
            start = HoFaceClock.Now;
            directory = directory ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/HoFaceTraces"));
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".jsonl");
            try
            {
                writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                writer.WriteLine(JsonUtility.ToJson(new Header {
                    startedUtc = DateTime.UtcNow.ToString("O"), label = label ?? "",
                    intervalSeconds = intervalSeconds, durationSeconds = duration,
                    profileJson = HoFaceProfileJson.Write(session.Settings.Middleware), settingsJson = session.Settings.ToJson()
                }));
                writer.Flush();
                session.TraceCompleted += OnSolved;
                EditorApplication.update += Watch;
                AssemblyReloadEvents.beforeAssemblyReload += OnReload;
                EditorApplication.quitting += OnQuit;
                Debug.Log("[Ho 面捕时序] 开始：" + label + " · 间隔 " + intervalSeconds + "s（0=每次求解）\n" + FilePath);
            }
            catch { writer?.Dispose(); writer = null; throw; }
        }

        private void OnSolved(HoFaceAnimationSession source, double now, float deltaTime)
        {
            if (!Recording) return;
            if (source.TraceRevision != revision) { Stop("configuration-changed"); return; }
            double elapsed = now - start;
            if (elapsed > duration) { Stop("completed"); return; }
            if (!schedule.Accept(elapsed)) return;
            try
            {
                var frame = source.CaptureTrace(now, deltaTime);
                frame.sample = Samples;
                frame.elapsed = elapsed;
                writer.WriteLine(JsonUtility.ToJson(frame));
                Samples++;
                if (Samples % 20 == 0) writer.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("[Ho 面捕时序] 写入失败：" + e.Message);
                Stop("write-error");
            }
        }

        private void Watch()
        {
            if (session.TraceDisposed) Stop("session-stopped");
            else if (Elapsed >= duration) Stop("completed");
        }
        private void OnReload() => Stop("assembly-reload");
        private void OnQuit() => Stop("editor-quitting");

        public void Stop(string reason = "manual-stop")
        {
            if (!Recording) return;
            session.TraceCompleted -= OnSolved;
            EditorApplication.update -= Watch;
            AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
            EditorApplication.quitting -= OnQuit;
            var output = writer;
            writer = null;
            try
            {
                output.WriteLine(JsonUtility.ToJson(new Footer { reason = reason, samples = Samples, elapsed = Elapsed }));
            }
            catch (Exception e) { Debug.LogError("[Ho 面捕时序] 收尾失败：" + e.Message); }
            finally
            {
                try { output.Dispose(); }
                catch (Exception e) { Debug.LogError("[Ho 面捕时序] 保存失败：" + e.Message); }
            }
            Debug.Log("[Ho 面捕时序] 结束：" + reason + " · " + Samples + " 条\n" + FilePath);
        }
        public void Dispose() => Stop();
    }

    /// <summary>No repeated samples to fill gaps. Interval is a minimum separation between observed solves.</summary>
    public sealed class HoFaceTraceSchedule
    {
        private readonly double interval;
        private double last = double.NegativeInfinity;
        public HoFaceTraceSchedule(double intervalSeconds)
        {
            if (intervalSeconds < 0 || double.IsNaN(intervalSeconds) || double.IsInfinity(intervalSeconds))
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            interval = intervalSeconds;
        }
        public bool Accept(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0 || elapsed <= last) return false;
            if (elapsed - last + 1e-9 < interval) return false;
            last = elapsed;
            return true;
        }
    }
}
