using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEngine;

/// <summary>Runs only in the marked disposable test project, after the real consumer LateUpdates.</summary>
[DefaultExecutionOrder(31900)]
public sealed class HoVtsJellyEyeFrameProbe : MonoBehaviour
{
    public HoVtsParameterReceiver receiver;
    public HoVtsIrController ir;
    public HoVtsJellyEye[] layers;
    public SkinnedMeshRenderer face;
    public Action<bool, string> Assert;
    public Action<Exception> Failed;
    public Action Completed;

    private readonly Dictionary<string, float> values = new Dictionary<string, float>
        { { "BlinkL", 0 }, { "BlinkR", 0 }, { "LookLeft", 0 }, { "LookRight", 0 }, { "LookUp", 0 }, { "LookDown", 0 } };
    private int phase, startedFrame;
    private double started, phaseStarted;
    private ulong previousRevision;
    private bool published, finished, peakSeen;

    private void Update()
    {
        if (finished) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (started == 0) { started = phaseStarted = now; startedFrame = Time.frameCount; }
            if (now - started > 20) throw new Exception("Real-frame jelly validation timed out in phase " + phase);
            published = false;
            if (phase == 0 || phase == 1 || phase == 2 || phase == 3 || phase == 5 || phase == 6)
            {
                float blink = phase == 1 || phase == 2 || phase == 6 ? 1 : 0;
                values["BlinkL"] = values["BlinkR"] = blink;
                receiver.ApplyRawFrame(values, true, now, "jelly-frame-probe");
                published = true;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void LateUpdate()
    {
        if (finished) return;
        try
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - phaseStarted;
            if (published)
            {
                Assert(ir.HasOutputFrame && ir.OutputRevision == previousRevision + 1,
                    "automatic IR evaluates once before all three jelly consumers in this actual frame");
                Assert(ir.TryGetFinalWeight("BlinkL", out float weight) && Mathf.Abs(weight - values["BlinkL"] * 100) < 0.01f,
                    "current-frame final snapshot is available before render");
            }
            previousRevision = ir.OutputRevision;
            if (phase == 0 && elapsed > 0.1) Next(1);
            else if (phase == 1)
            {
                peakSeen |= Weight("Small") > 0.1f && Weight("H1Small") > 0.1f && Weight("H2Small") > 0.1f;
                if (elapsed > 0.3)
                {
                    Assert(peakSeen, "all three components visibly react through their real automatic LateUpdate");
                    Next(2);
                }
            }
            else if (phase == 2 && elapsed > 2)
            {
                Assert(Neutral(), "held nonzero real-frame input converges to each captured neutral");
                Next(3);
            }
            else if (phase == 3) Next(4);
            else if (phase == 4 && receiver.InputExpired && elapsed > 1.5)
            {
                Assert(!ir.HasOutputFrame && !receiver.Tracked, "automatic receiver timeout invalidates and releases IR");
                Assert(Neutral(), "automatic jelly consumer recenters after real receiver timeout");
                Assert(!HoFaceOutputOwnership.IsReserved(face, Index("Small"))
                    && !HoFaceOutputOwnership.IsReserved(face, Index("H1Small"))
                    && !HoFaceOutputOwnership.IsReserved(face, Index("H2Small")), "all lost-input layers hand back their output reservations");
                Next(5);
            }
            else if (phase == 5 && elapsed > 0.1) Next(6);
            else if (phase == 6 && Weight("Small") > 0.1f && Weight("H1Small") > 0.1f)
            {
                layers[0].enabled = false;
                Assert(Mathf.Abs(Weight("Small")) < 0.01f && !HoFaceOutputOwnership.IsReserved(face, Index("Small")),
                    "real OnDisable restores the pupil and releases its keys immediately");
                Assert(Weight("H1Small") > 0.1f, "disabling the pupil preserves another layer's current output");
                layers[1].enabled = false;
                Assert(Mathf.Abs(Weight("H1Small")) < 0.01f, "real OnDisable independently restores the first highlight");
                ir.enabled = false;
                Assert(!ir.HasOutputFrame, "disabling actual IR immediately invalidates its output snapshot");
                Next(7);
            }
            else if (phase == 7 && elapsed > 1.5)
            {
                Assert(Mathf.Abs(Weight("H2Small")) < 0.01f && !HoFaceOutputOwnership.IsReserved(face, Index("H2Small")),
                    "remaining live consumer settles and releases after source OnDisable");
                Assert(Time.frameCount > startedFrame + 10, "test exercised actual separate Unity render frames");
                finished = true; Completed();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private bool Neutral()
    {
        foreach (string prefix in new[] { "", "H1", "H2" })
        foreach (string key in new[] { "Right", "Left", "Up", "Down", "Large", "Small", "Tall", "Flat" })
            if (Mathf.Abs(Weight(prefix + key) - (prefix == "" && key == "Left" ? 7 : 0)) > 0.01f) return false;
        return true;
    }
    private int Index(string key) => face.sharedMesh.GetBlendShapeIndex(key);
    private float Weight(string key) => face.GetBlendShapeWeight(Index(key));
    private void Next(int next) { phase = next; phaseStarted = Time.realtimeSinceStartupAsDouble; }
    private void Fail(Exception error) { finished = true; Failed(error); }
}
