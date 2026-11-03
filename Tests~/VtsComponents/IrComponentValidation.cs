using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
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
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            string json = "{\"format\":\"ho-face-ir\",\"version\":1,\"id\":\"ir-test\",\"displayName\":\"IR test\",\"root\":\"root\","
                + "\"parameters\":[{\"name\":\"Mouth/Open\",\"defaultValue\":0,\"min\":0,\"max\":1}],"
                + "\"targets\":[{\"id\":\"jaw\",\"slot\":\"Face\",\"shape\":\"Jaw\",\"min\":0,\"max\":1}],"
                + "\"nodes\":["
                + "{\"id\":\"rest\",\"name\":\"Rest\",\"type\":\"pose\",\"weights\":[]},"
                + "{\"id\":\"open\",\"name\":\"Open\",\"type\":\"pose\",\"weights\":[{\"target\":\"jaw\",\"value\":1}]},"
                + "{\"id\":\"root\",\"name\":\"Root\",\"type\":\"blend1d\",\"parameterX\":\"Mouth/Open\",\"children\":["
                + "{\"node\":\"rest\",\"threshold\":0,\"weight\":1},{\"node\":\"open\",\"threshold\":1,\"weight\":1}]}]}";
            var ir = root.AddComponent<HoVtsIrController>();
            ir.ParameterReceiver = receiver;
            ir.OutputRoot = root.transform;
            ir.IrAsset = new TextAsset(json);
            ir.AutoRun = false;
            ir.WeightScale = 100;

            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 1 } }, true, 1, "ir-test");
            Check(ir.EvaluateNow(), "IR component evaluates a processed parameter");
            Near(renderer.GetBlendShapeWeight(0), 100, "IR component writes final Unity weight");
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "Raw", 0 } }, false, 2, "ir-test");
            Check(!ir.EvaluateNow(), "IR component stops after tracking loss");
            Near(renderer.GetBlendShapeWeight(0), 0, "IR component restores baseline on release");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("HO_VTS_IR_COMPONENT PASS assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Near(float actual, float expected, string label) =>
        Check(Mathf.Abs(actual - expected) < .01f, label + ": " + actual + " vs " + expected);
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
}
