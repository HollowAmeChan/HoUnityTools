using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

public static class HoVtsIrComponentValidation
{
    private static int assertions;
    public static void RunBatch()
    {
        try
        {
            var root = new GameObject("IrCharacter");
            var receiver = root.AddComponent<HoVtsParameterReceiver>();
            var rules = new HoFaceMiddleware();
            rules.outputs.Add(new HoFaceOutput { parameter = "Mouth/Open", expression = "Raw" });
            receiver.SetMiddleware(rules);
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Jaw", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            mesh.AddBlendShapeFrame("Unrelated", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.SetBlendShapeWeight(0, 17);
            renderer.SetBlendShapeWeight(1, 53);
            var secondary = new GameObject("FaceSecondary");
            secondary.transform.SetParent(root.transform, false);
            var secondaryMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            secondaryMesh.AddBlendShapeFrame("Jaw", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            var secondaryRenderer = secondary.AddComponent<SkinnedMeshRenderer>();
            secondaryRenderer.sharedMesh = secondaryMesh;
            secondaryRenderer.SetBlendShapeWeight(0, 29);
            var unlisted = new GameObject("UnlistedFace");
            unlisted.transform.SetParent(root.transform, false);
            var unlistedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            unlistedMesh.AddBlendShapeFrame("Jaw", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            var unlistedRenderer = unlisted.AddComponent<SkinnedMeshRenderer>();
            unlistedRenderer.sharedMesh = unlistedMesh;
            unlistedRenderer.SetBlendShapeWeight(0, 41);
            string json = "{\"format\":\"ho-face-ir\",\"version\":2,\"id\":\"ir-test\",\"displayName\":\"IR test\",\"root\":\"root\","
                + "\"parameters\":[{\"name\":\"Mouth/Open\",\"defaultValue\":0,\"min\":0,\"max\":1}],"
                + "\"targets\":[{\"id\":\"Jaw\",\"min\":0,\"max\":1},{\"id\":\"MissingOnTheseMeshes\",\"min\":0,\"max\":1}],"
                + "\"nodes\":["
                + "{\"id\":\"rest\",\"name\":\"Rest\",\"type\":\"pose\",\"weights\":[]},"
                + "{\"id\":\"open\",\"name\":\"Open\",\"type\":\"pose\",\"weights\":[{\"target\":\"Jaw\",\"value\":1}]},"
                + "{\"id\":\"root\",\"name\":\"Root\",\"type\":\"blend1d\",\"parameterX\":\"Mouth/Open\",\"children\":["
                + "{\"node\":\"rest\",\"threshold\":0,\"weight\":1},{\"node\":\"open\",\"threshold\":1,\"weight\":1}]}]}";
            var ir = root.AddComponent<HoVtsIrController>();
            ir.ParameterReceiver = receiver;
            ir.TargetMeshes.Add(renderer);
            ir.TargetMeshes.Add(secondaryRenderer);
            ir.IrAsset = new TextAsset(json);
            ir.AutoRun = false;
            ir.WeightScale = 100;

            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 1, "ir-test");
            Check(ir.EvaluateNow(), "IR component evaluates a processed parameter");
            Near(renderer.GetBlendShapeWeight(0), 100, "IR component writes first listed mesh");
            Near(secondaryRenderer.GetBlendShapeWeight(0), 100, "IR target fans out to another listed mesh");
            Near(unlistedRenderer.GetBlendShapeWeight(0), 41, "IR component leaves unlisted meshes untouched");
            Near(renderer.GetBlendShapeWeight(1), 53, "IR component leaves unrelated keys in listed meshes untouched");
            face.name = "RenamedFace";
            Check(ir.EvaluateNow(), "object names do not affect explicit renderer binding");
            ir.TargetMeshes.Remove(secondaryRenderer);
            Check(ir.EvaluateNow(), "mesh list edit rebuilds owned writers");
            Near(secondaryRenderer.GetBlendShapeWeight(0), 29, "removing a mesh releases its original baseline");
            Near(renderer.GetBlendShapeWeight(0), 100, "retained listed mesh keeps receiving output");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 0 } }, false, 2, "ir-test");
            Check(!ir.EvaluateNow(), "IR component stops after tracking loss");
            Near(renderer.GetBlendShapeWeight(0), 17, "IR component restores first listed mesh baseline");
            Near(secondaryRenderer.GetBlendShapeWeight(0), 29, "IR component restores second listed mesh baseline");
            Near(unlistedRenderer.GetBlendShapeWeight(0), 41, "IR component never owns unlisted mesh values");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", .5f } }, true, 3, "ir-test");
            Check(ir.EvaluateNow(), "IR resumes with original renderer baseline intact");
            ir.TargetMeshes.Clear();
            Check(!ir.EvaluateNow(), "empty mesh list does not fall back to scene scanning");
            Near(renderer.GetBlendShapeWeight(0), 17, "clearing the list releases owned values");
            UnityEngine.Object.DestroyImmediate(root);
            ValidateFinalOutputSnapshot();
            ValidateCharacterSetup();
            ValidateHoFaceStudioRawInput();
            Debug.Log("HO_VTS_IR_COMPONENT PASS assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateFinalOutputSnapshot()
    {
        var root = new GameObject("FinalIrOutput");
        try
        {
            var receiver = root.AddComponent<HoVtsParameterReceiver>();
            var middleware = new HoFaceMiddleware();
            middleware.outputs.Add(new HoFaceOutput { parameter = "Drive", expression = "Raw" });
            receiver.SetMiddleware(middleware);
            var renderer = AddMesh(root, "FinalFace", "Shape");
            renderer.SetBlendShapeWeight(0, 12);
            var ir = root.AddComponent<HoVtsIrController>();
            ir.AutoRun = false;
            ir.ParameterReceiver = receiver;
            ir.TargetMeshes.Add(renderer);
            ir.IrAsset = new TextAsset("{\"format\":\"ho-face-ir\",\"version\":2,\"id\":\"final\","
                + "\"root\":\"root\",\"parameters\":[{\"name\":\"Drive\",\"min\":0,\"max\":1}],"
                + "\"targets\":[{\"id\":\"Shape\",\"min\":0,\"max\":0.8},{\"id\":\"Missing\",\"min\":0,\"max\":1}],"
                + "\"nodes\":[{\"id\":\"pose\",\"type\":\"pose\",\"weights\":[{\"target\":\"Shape\",\"value\":2}]},"
                + "{\"id\":\"root\",\"type\":\"direct\",\"children\":[{\"node\":\"pose\",\"weightParameter\":\"Drive\",\"weight\":1}]}]}");
            ir.WeightScale = 125;
            Check(!ir.HasOutputFrame && !ir.TryGetFinalWeight("Shape", out _), "no final output before the first completed write");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 1);
            Check(ir.EvaluateNow(), "final snapshot evaluation succeeds");
            Check(ir.TryGetFinalWeight("Shape", out float final) && ir.HasOutputFrame, "completed write exposes final output");
            Near(final, 100, "final output includes target clamp and Unity weight scale");
            Near(final, renderer.GetBlendShapeWeight(0), "snapshot matches the value actually written");
            Check(!ir.TryGetFinalWeight("Missing", out _) && !ir.TryGetFinalWeight(null, out _),
                "unbound and empty targets do not invent final output");
            Check(ir.IsTargetBound(renderer, 0) && HoFaceOutputOwnership.IsReservedBy(renderer, 0, ir),
                "IR publishes binding ownership and reserves actual output keys");
            ulong revision = ir.OutputRevision, bindingRevision = ir.BindingRevision;
            var copy = new Dictionary<string, float> { { "Old", 9 } };
            ir.CopyFinalWeightsTo(copy);
            Check(copy.Count == 1 && copy.ContainsKey("Shape"), "copy replaces caller contents with bound final values");
            copy["Shape"] = -99;
            renderer.SetBlendShapeWeight(0, 45);
            for (int i = 0; i < 3; i++)
            {
                Check(ir.TryGetFinalWeight("Shape", out final), "multiple consumers can read the same output");
                Near(final, 100, "reads use the upstream snapshot, not downstream mesh mutations");
            }
            Check(ir.OutputRevision == revision && ir.BindingRevision == bindingRevision,
                "readers never evaluate or rebuild IR");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 0 } }, true, 2);
            Check(!ir.HasOutputFrame && !ir.TryGetFinalWeight("Shape", out _), "new receiver evaluation invalidates old output");
            ir.CopyFinalWeightsTo(copy);
            Check(copy.Count == 0, "copy never returns a stale frame");
            Check(ir.EvaluateNow() && ir.OutputRevision == revision + 1, "only an explicit evaluation advances the output revision");
            Check(ir.TryGetFinalWeight("Shape", out final), "explicit zero output remains a valid result");
            Near(final, 0, "zero output is cached rather than treated as a missing key");
            ir.Release();
            Near(renderer.GetBlendShapeWeight(0), 12, "release restores the original pre-IR value");
            Check(!ir.HasOutputFrame && !HoFaceOutputOwnership.IsReserved(renderer, 0), "release invalidates output and hands back ownership");
            renderer.SetBlendShapeWeight(0, 24);
            Check(ir.EvaluateNow(), "IR can reacquire released keys");
            ir.Release();
            Near(renderer.GetBlendShapeWeight(0), 24, "reacquisition captures the new external baseline");

            var competitor = new object();
            HoFaceOutputOwnership.Reserve(renderer, 0, competitor);
            Check(!ir.EvaluateNow() && !ir.HasOutputFrame, "an occupied target rejects the whole output frame");
            Near(renderer.GetBlendShapeWeight(0), 24, "failed IR write leaves the competing writer unchanged");
            Check(HoFaceOutputOwnership.IsReservedBy(renderer, 0, competitor), "failed cleanup does not release another owner");
            HoFaceOutputOwnership.Release(competitor);
            Check(ir.EvaluateNow(), "IR recovers after an ownership conflict is removed");
            renderer.SetBlendShapeWeight(0, 36);
            ir.Release();
            Near(renderer.GetBlendShapeWeight(0), 36, "release preserves a newer external write");
            Check(ir.EvaluateNow(), "IR resumes after external handoff");
            ir.WeightScale = 50;
            Check(!ir.HasOutputFrame && !ir.TryGetFinalWeight("Shape", out _), "changing the weight scale invalidates the old units");
            Check(ir.EvaluateNow(), "IR writes using the new scale");
            receiver.ClearFrame();
            Check(!ir.HasOutputFrame && !ir.TryGetFinalWeight("Shape", out _), "cleared receiver makes the snapshot unreadable immediately");
            Check(!ir.EvaluateNow() && !HoFaceOutputOwnership.IsReserved(renderer, 0), "tracking loss releases IR reservations");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 3);
            Check(ir.EvaluateNow(), "IR resumes after tracking loss");
            ir.enabled = false;
            Check(!ir.HasOutputFrame && !ir.EvaluateNow(), "disabled IR cannot expose or manually write a frame");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateHoFaceStudioRawInput()
    {
        var root = new GameObject("HoFaceStudioRawInput");
        var receiver = root.AddComponent<HoVtsParameterReceiver>();
        var adapter = root.AddComponent<HoFaceStudioRawInput>();
        adapter.AutoStart = false;
        adapter.ParameterReceiver = receiver;
        adapter.LocalPort = FreePort();
        adapter.StartListening();
        Check(adapter.Status.StartsWith("监听 loopback:"), "raw adapter binds only to loopback");

        using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            sender.Client.ReceiveTimeout = 1000;
            var destination = new IPEndPoint(IPAddress.Loopback, adapter.LocalPort);
            HoFaceRawAck first = SendRawFrame(adapter, sender, destination, 1, 10, true, .61f);
            Check(first.Accepted && first.Sequence == 1, "Unity acknowledges accepted raw frame");
            Check(receiver.Tracked && receiver.Source == "HoFaceStudio/UnityRaw", "raw adapter claims receiver while tracked");
            Check(receiver.TryGetRaw("JawOpen", out float firstJaw), "raw adapter publishes source parameter");
            Near(firstJaw, .61f, "raw adapter source parameter value");

            HoFaceRawAck rewound = SendRawFrame(adapter, sender, destination, 2, .5, true, .24f);
            Check(rewound.Accepted && rewound.Sequence == 2, "raw adapter accepts replay time seek");
            Check(receiver.TryGetRaw("JawOpen", out float rewoundJaw), "raw adapter updates after replay seek");
            Near(rewoundJaw, .24f, "replay seek parameter value");

            HoFaceRawAck lost = SendRawFrame(adapter, sender, destination, 3, .1, false, .8f);
            Check(lost.Accepted && !receiver.Tracked && !receiver.HasFrame, "raw tracking loss releases receiver data");

            SendRawFrame(adapter, sender, destination, 4, .2, true, .45f);
            Check(receiver.Tracked, "raw adapter resumes ownership after tracking returns");
            adapter.StopListening();
            Check(!receiver.Tracked && !receiver.HasFrame && adapter.Status == "未启动",
                "stopping raw adapter releases its receiver ownership");
        }
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void ValidateCharacterSetup()
    {
        var root = new GameObject("SetupCharacter");
        var firstRenderer = AddMesh(root, "FaceA", "JawOpen");
        var secondRenderer = AddMesh(root, "FaceB", "MouthSmileLeft");
        AddMesh(root, "NoBlendShapes", null);

        HoVtsIrController controller = HoVtsCharacterSetup.Create(root);
        var host = root.transform.Find("HoVTS");
        var receiver = host != null ? host.GetComponent<HoVtsParameterReceiver>() : null;
        var pose = host != null ? host.Find("HoVTSLookAt")?.GetComponent<HoVtsLookAt>() : null;
        Check(host != null && controller != null && controller.gameObject == host.Find("HoVTS面捕").gameObject,
            "one-click setup creates the face IR component on its own child");
        Check(controller.ParameterReceiver == receiver, "one-click setup connects the IR component to the role receiver");
        Check(controller.TargetMeshes.Count == 2 && controller.TargetMeshes.Contains(firstRenderer)
            && controller.TargetMeshes.Contains(secondRenderer), "one-click setup fills only mesh renderers with shape keys");
        Check(pose != null && pose.ParameterReceiver == receiver && !pose.AutoRun && !pose.enabled,
            "one-click setup creates a separate disabled LookAt consumer");
        Check(root.GetComponentInChildren<HoVtsController>(true) == null,
            "one-click setup no longer creates the legacy Animator controller component");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static SkinnedMeshRenderer AddMesh(GameObject root, string name, string shapeKey)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        if (!string.IsNullOrEmpty(shapeKey))
            mesh.AddBlendShapeFrame(shapeKey, 100, new Vector3[3], new Vector3[3], new Vector3[3]);
        var renderer = child.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        return renderer;
    }

    private static HoFaceRawAck SendRawFrame(HoFaceStudioRawInput adapter, UdpClient sender,
        IPEndPoint destination, long sequence, double time, bool tracked, float jawOpen)
    {
        string json = "{\"type\":\"hoface.raw\",\"version\":1,\"sequence\":" + sequence
            + ",\"time\":" + time.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"tracked\":" + (tracked ? "true" : "false")
            + ",\"values\":{\"JawOpen\":" + jawOpen.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        sender.Send(bytes, bytes.Length, destination);
        adapter.SendMessage("Update");
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        string ackJson = Encoding.UTF8.GetString(sender.Receive(ref endpoint));
        if (!HoFaceRawFrameJson.TryParseAck(ackJson, out HoFaceRawAck ack, out string error))
            throw new FormatException(error);
        return ack;
    }

    private static int FreePort()
    {
        using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            return ((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }

    private static void Near(float actual, float expected, string label) =>
        Check(Mathf.Abs(actual - expected) < .01f, label + ": " + actual + " vs " + expected);
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
}
