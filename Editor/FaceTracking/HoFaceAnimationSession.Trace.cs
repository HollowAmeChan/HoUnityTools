using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    public sealed partial class HoFaceAnimationSession
    {
        internal event Action<HoFaceAnimationSession, double, float> TraceCompleted;
        internal int TraceRevision { get; private set; }
        internal bool TraceDisposed => disposed;
        private float[] traceExpressions = new float[0];
        private float[] traceCurves = new float[0];

        // Called synchronously after this session's full evaluation; never re-evaluates an expression.
        internal HoFaceTraceFrame CaptureTrace(double now, float deltaTime)
        {
            var frame = new HoFaceTraceFrame {
                clock = now, unityFrame = Time.frameCount, deltaTime = deltaTime,
                connected = HoFaceInputHub.Connected, lastReceivedAt = HoFaceInputHub.LastFrameTime,
                revision = TraceRevision
            };
            var wires = new List<HoFaceTraceValue>();
            foreach (var pair in HoFaceInputHub.MergedValues)
                wires.Add(new HoFaceTraceValue(pair.Key, pair.Value, HoFaceInputHub.ReceivedAt(pair.Key)));
            frame.wires = wires.ToArray();
            var ins = new List<HoFaceTraceInput>();
            for (int i = 0; i < inputRows.Length; i++)
            {
                var row = inputRows[i];
                if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                float effective = Lookup(row.parameter);
                ins.Add(new HoFaceTraceInput {
                    row = i, name = row.parameter, fresh = inputFresh[i],
                    valid = HoFaceTraceValue.Finite(inputValues[i]) && HoFaceTraceValue.Finite(effective),
                    configured = HoFaceTraceValue.Safe(inputValues[i]), effective = HoFaceTraceValue.Safe(effective)
                });
            }
            frame.inputs = ins.ToArray();
            var outs = new List<HoFaceTraceOutput>();
            for (int i = 0; i < outputs.Length; i++)
            {
                var row = outputs[i];
                if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                float published = OutputValue(row.parameter);
                outs.Add(new HoFaceTraceOutput {
                    row = i, name = row.parameter, expressionActive = expressions[i] != null,
                    valid = HoFaceTraceValue.Finite(traceExpressions[i]) && HoFaceTraceValue.Finite(traceCurves[i])
                        && HoFaceTraceValue.Finite(outputValues[i]) && HoFaceTraceValue.Finite(published),
                    expression = HoFaceTraceValue.Safe(traceExpressions[i]), curve = HoFaceTraceValue.Safe(traceCurves[i]),
                    modified = HoFaceTraceValue.Safe(outputValues[i]), published = HoFaceTraceValue.Safe(published)
                });
            }
            frame.outputs = outs.ToArray();
            var pins = new List<HoFaceTraceValue>();
            foreach (var pair in previews) pins.Add(new HoFaceTraceValue(pair.Key, pair.Value));
            frame.overrides = pins.ToArray();
            return frame;
        }
    }
}
