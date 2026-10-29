using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>Records observed component solves, including autonomous ticks; never re-evaluates rules.</summary>
    public sealed class HoVtsTraceRecorder : IDisposable
    {
        [Serializable] public sealed class Row
        {
            public int row;
            public string name;
            public bool fresh, expressionActive, valid;
            public float expression, curve, modified, published;
        }
        [Serializable] public sealed class Frame
        {
            public string kind = "sample", source;
            public int sample, unityFrame, revision;
            public double elapsed, clock, lastReceivedAt;
            public float deltaTime;
            public bool tracked, inputExpired;
            public HoFaceTraceValue[] wires, inputOverrides, outputOverrides;
            public Row[] inputs, outputs;
        }
        [Serializable] private sealed class Header
        {
            public string kind = "header", format = "ho-vts-trace", startedUtc, profileJson, target;
            public int version = 1;
            public double intervalSeconds, durationSeconds;
        }
        [Serializable] private sealed class Footer
        {
            public string kind = "end", reason;
            public int samples;
        }
        private readonly HoVtsDebugSession session;
        private readonly HoVtsParameterReceiver receiver;
        private readonly HoFaceTraceSchedule schedule;
        private readonly int revision;
        private readonly double start, duration;
        private StreamWriter writer;
        public string FilePath { get; }
        public int Samples { get; private set; }
        public bool Recording => writer != null;
        public string StopReason { get; private set; }

        public HoVtsTraceRecorder(HoVtsDebugSession session, double intervalSeconds = 0, double durationSeconds = 5, string directory = null)
        {
            if (session == null || session.Disposed) throw new ArgumentException("请先开始角色调试。");
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            schedule = new HoFaceTraceSchedule(intervalSeconds);
            this.session = session; receiver = session.Receiver;
            revision = receiver.RuleRevision; start = Time.realtimeSinceStartupAsDouble; duration = durationSeconds;
            directory = directory ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/HoVtsTraces"));
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            try
            {
                writer.WriteLine(JsonUtility.ToJson(new Header { startedUtc = DateTime.UtcNow.ToString("O"),
                    profileJson = HoFaceProfile.Write(receiver.Middleware), target = session.Target.name,
                    intervalSeconds = intervalSeconds, durationSeconds = durationSeconds }));
                writer.Flush();
                receiver.Evaluated += OnSolved;
                EditorApplication.update += Watch;
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                EditorApplication.quitting += Dispose;
            }
            catch { writer.Dispose(); writer = null; throw; }
        }
        private void OnSolved(HoVtsParameterReceiver source, double now, float dt)
        {
            if (!Recording) return;
            if (session.Disposed) { Stop("session-stopped"); return; }
            if (source.Source != session.SourceId) { Stop("input-source-changed"); return; }
            if (revision != source.RuleRevision) { Stop("configuration-changed"); return; }
            double elapsed = now - start;
            if (elapsed > duration) { Stop("completed"); return; }
            if (!schedule.Accept(elapsed)) return;
            try
            {
                var values = new Dictionary<string, float>(); receiver.CopyRawTo(values);
                var frame = new Frame { sample = Samples, unityFrame = Time.frameCount, revision = revision,
                    elapsed = elapsed, clock = now, deltaTime = dt, lastReceivedAt = receiver.LastFrameTime,
                    tracked = receiver.Tracked, inputExpired = receiver.InputExpired, source = receiver.Source,
                    wires = Values(values), inputOverrides = Values(session.InputPins), outputOverrides = Values(session.OutputPins),
                    inputs = Rows(true), outputs = Rows(false) };
                writer.WriteLine(JsonUtility.ToJson(frame));
                Samples++;
                if (Samples % 20 == 0) writer.Flush();
            }
            catch (Exception error) { Debug.LogError("[Ho VTS 录样] " + error.Message); Stop("write-error"); }
        }
        private Row[] Rows(bool input)
        {
            var rows = input ? receiver.Middleware.inputs : receiver.Middleware.outputs;
            var result = new List<Row>();
            if (rows != null) for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                float modified, published; bool fresh = false;
                bool found = input ? receiver.TryReadInputRow(i, out modified, out published, out fresh)
                    : receiver.TryReadOutputRow(i, out modified, out published);
                if (!found) continue;
                receiver.TryReadRuleStages(input, i, out float expression, out float curve, out bool active);
                result.Add(new Row { row = i, name = rows[i].parameter, fresh = fresh, expressionActive = active,
                    valid = HoFaceTraceValue.Finite(expression) && HoFaceTraceValue.Finite(curve)
                        && HoFaceTraceValue.Finite(modified) && HoFaceTraceValue.Finite(published),
                    expression = HoFaceTraceValue.Safe(expression), curve = HoFaceTraceValue.Safe(curve),
                    modified = HoFaceTraceValue.Safe(modified), published = HoFaceTraceValue.Safe(published) });
            }
            return result.ToArray();
        }
        private static HoFaceTraceValue[] Values(IReadOnlyDictionary<string, float> values)
        {
            var result = new List<HoFaceTraceValue>();
            foreach (var pair in values) result.Add(new HoFaceTraceValue(pair.Key, pair.Value));
            return result.ToArray();
        }
        private void Watch()
        {
            if (session.Disposed || receiver == null) Stop("session-stopped");
            else if (receiver.RuleRevision != revision) Stop("configuration-changed");
            else if (Time.realtimeSinceStartupAsDouble - start >= duration) Stop("completed");
        }
        public void Stop(string reason = "manual-stop")
        {
            if (!Recording) return;
            receiver.Evaluated -= OnSolved;
            EditorApplication.update -= Watch;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            var output = writer; writer = null; StopReason = reason;
            try { output.WriteLine(JsonUtility.ToJson(new Footer { reason = reason, samples = Samples })); }
            catch (Exception error) { Debug.LogError("[Ho VTS 录样] " + error.Message); }
            finally
            {
                try { output.Dispose(); }
                catch (Exception error) { Debug.LogError("[Ho VTS 录样] " + error.Message); }
            }
        }
        public void Dispose() => Stop("disposed");
    }
}
