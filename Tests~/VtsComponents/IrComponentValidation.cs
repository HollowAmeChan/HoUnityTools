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
