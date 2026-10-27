using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Hollow.HoUnityTools.Editor.RigConstraints;
using lilToon.URP.Extensions.GeometryData;

public static class FbxOutlineValidation
{
    private static readonly List<string> Passed = new List<string>();
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Passed.Add(name);
    }

    public static void Run()
    {
        try
        {
            AlgorithmChecks();
            WriteFixture();
            ImportChecks("Assets/OutlineFixture.fbx");
            if (File.Exists("Assets/Character.fbx")) ImportChecks("Assets/Character.fbx");
            InspectorChecks();
            File.WriteAllText("outline-results.txt", "PASS " + Passed.Count + " checks\n" + string.Join("\n", Passed));
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText("outline-results.txt", "FAIL\n" + exception + "\n" + string.Join("\n", Passed));
            EditorApplication.Exit(1);
        }
    }

    private static void InspectorChecks()
    {
        const string path = "Assets/OutlineFixture.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        string otherData = importer.userData;
        Color[][] original = Meshes(path).Select(m => m.colors).ToArray();
        var editor = UnityEditor.Editor.CreateEditor(importer) as AssetImporterEditor;
        var assetEditor = UnityEditor.Editor.CreateEditor(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        // The Inspector's ActiveEditorTracker normally supplies this association. Standalone
        // CreateEditor has no asset editor, so Unity intentionally skips reimport on Apply.
        typeof(AssetImporterEditor).GetMethod("InternalSetAssetImporterTargetEditor", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(editor, new object[] { assetEditor });
        Check(editor != null && editor.GetType().Name == "ModelImporterEditor", "Native FBX inspector is retained");
        Check(HoFbxOutlineImporterHeader.Supports(editor), "FBX importer header is eligible");
        HoFbxOutlineImporterHeader.UpdateSettings(editor, true, false);
        Check(editor.HasModified() && HoFbxOutlineVertexColorPostprocessor.IsEnabled(importer), "Checkbox enables native Apply/Revert");
        Check(Meshes(path).Select((m, i) => m.colors.SequenceEqual(original[i])).All(v => v), "Checkbox does not reimport before Apply");
        typeof(AssetImporterEditor).GetMethod("ResetValues", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        Check(!editor.HasModified() && importer.userData == otherData, "Native Revert restores checkbox and metadata");
        HoFbxOutlineImporterHeader.UpdateSettings(editor, true, false);
        typeof(AssetImporterEditor).GetMethod("ApplyAndImport", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Check(!editor.HasModified() && HoFbxOutlineVertexColorPostprocessor.IsEnabled(importer) && !HoFbxOutlineVertexColorPostprocessor.GetWeld(importer), "Native Apply persists checkbox / weld selection");
        Check(Meshes(path).All(m =>
        {
            var expected = HoFbxOutlineCorrectionBuilder.Build(m, false);
            return m.colors.Select((c, i) => ((Vector4)c - expected[i]).sqrMagnitude < 1e-10f).All(v => v);
        }), "Native Apply imports baked colors");
        HoFbxOutlineImporterHeader.UpdateSettings(editor, false, null);
        typeof(AssetImporterEditor).GetMethod("ApplyAndImport", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Check(importer.userData == otherData && Meshes(path).Select((m, i) => m.colors.SequenceEqual(original[i])).All(v => v), "Native Apply after disabling restores authored colors");
        UnityEngine.Object.DestroyImmediate(editor);
        UnityEngine.Object.DestroyImmediate(assetEditor);

        AssetDatabase.CopyAsset(path, "Assets/Second.fbx");
        var second = (ModelImporter)AssetImporter.GetAtPath("Assets/Second.fbx");
        second.userData = "Second tool metadata";
        second.SaveAndReimport();
        editor = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { importer, second }) as AssetImporterEditor;
        HoFbxOutlineImporterHeader.UpdateSettings(editor, true, true);
        Check(editor.HasModified() && HoFbxOutlineVertexColorPostprocessor.IsEnabled(importer) && HoFbxOutlineVertexColorPostprocessor.IsEnabled(second), "Multi-selection edits both FBX importers");
        Check(importer.userData.StartsWith(otherData) && second.userData.StartsWith("Second tool metadata"), "Multi-selection preserves distinct userData");
        typeof(AssetImporterEditor).GetMethod("ResetValues", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        Check(importer.userData == otherData && second.userData == "Second tool metadata", "Multi-selection native Revert restores both metadata values");
        UnityEngine.Object.DestroyImmediate(editor);
        var modelEditor = UnityEditor.Editor.CreateEditor(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        Check(!HoFbxOutlineImporterHeader.Supports(modelEditor), "Imported GameObject header gets no duplicate controls");
        UnityEngine.Object.DestroyImmediate(modelEditor);
    }

    private static void Parity(Mesh mesh, string name)
    {
        foreach (bool weld in new[] { true, false })
        {
            Vector4[] actual = HoFbxOutlineCorrectionBuilder.Build(mesh, weld);
            Vector4[] expected = HoGeometryDataOutlineCorrectionBuilder.Build(mesh, weld);
            Check(actual.Length == mesh.vertexCount && actual.Zip(expected, (a, b) => (a - b).sqrMagnitude).All(e => e < 1e-12f),
                name + ": GD parity, weld=" + weld);
            Check(actual.All(v => !float.IsNaN(v.sqrMagnitude) && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1 && v.z >= 0 && v.z <= 1 && v.w > 0 && v.w <= 1),
                name + ": finite RGBA, weld=" + weld);
        }
    }

    private static void AlgorithmChecks()
    {
        var flat = new Mesh
        {
            vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
            normals = Enumerable.Repeat(Vector3.forward, 3).ToArray(),
            tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 3).ToArray(),
            triangles = new[] { 0, 1, 2 },
        };
        Vector4[] plane = HoFbxOutlineCorrectionBuilder.Build(flat);
        Check(plane.All(v => (v - new Vector4(0.5f, 0.5f, 1, 0.5f)).sqrMagnitude < 1e-12f), "Flat surface has expected neutral direction and alpha");
        Parity(flat, "Flat triangle");
        flat.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
        bool rejected = false;
        try { HoFbxOutlineCorrectionBuilder.Build(flat); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unsupported topology is rejected");
        UnityEngine.Object.DestroyImmediate(flat);

        var cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = UnityEngine.Object.Instantiate(cubeObject.GetComponent<MeshFilter>().sharedMesh);
        UnityEngine.Object.DestroyImmediate(cubeObject);
        Parity(cube, "Hard-normal / UV seam cube");
        var malformed=UnityEngine.Object.Instantiate(cube);
        var malformedTangents=malformed.tangents;
        for(int i=0;i<malformedTangents.Length;i++)
        {
            Vector3 t=(malformed.normals[i]*.99f+(Vector3)malformedTangents[i]*.01f).normalized;
            malformedTangents[i]=new Vector4(t.x,t.y,t.z,malformedTangents[i].w);
        }
        malformed.tangents=malformedTangents;
        Check(HoFbxOutlineCorrectionBuilder.RepairTangentFrames(malformed)>0,"Repair almost-parallel imported tangent frames");
        Check(malformed.normals.Zip(malformed.tangents,(n,t)=>Mathf.Abs(Vector3.Dot(n.normalized,((Vector3)t).normalized))<1e-5f).All(v=>v),"Repaired tangent frames are orthogonal");
        Vector4[] repairedColors=HoFbxOutlineCorrectionBuilder.Build(malformed);
        Check(repairedColors.Select((c,i)=>
        {
            Vector3 n=malformed.normals[i].normalized,t=((Vector3)malformed.tangents[i]).normalized;
            Vector3 b=Vector3.Cross(n,t).normalized*malformed.tangents[i].w;
            Vector3 decoded=t*(c.x*2-1)+b*(c.y*2-1)+n*(c.z*2-1);
            return Vector3.Dot(decoded.normalized,malformed.vertices[i].normalized)>.9999f;
        }).All(v=>v),"Repaired RGBA still decodes to the geometric direction");
        Check(HoFbxOutlineCorrectionBuilder.RepairTangentFrames(malformed)==0,"Tangent frame repair is idempotent");
        UnityEngine.Object.DestroyImmediate(malformed);
        Vector4[] encoded = HoFbxOutlineCorrectionBuilder.Build(cube);
        Vector3[] positions = cube.vertices, normals = cube.normals;
        Vector4[] tangents = cube.tangents;
        Check(encoded.Select((v, i) =>
        {
            Vector3 n = normals[i].normalized, t = ((Vector3)tangents[i]).normalized;
            Vector3 b = Vector3.Cross(n, t).normalized * tangents[i].w;
            Vector3 decoded = t * (v.x * 2 - 1) + b * (v.y * 2 - 1) + n * (v.z * 2 - 1);
            return Vector3.Dot(decoded, positions[i].normalized) > 0.9999f && Mathf.Abs(v.w - (1 - 0.5f / Mathf.Sqrt(3))) < 1e-5f;
        }).All(v => v), "Cube decodes to radial directions and analytical convex thickness");
        Vector4[] split = HoFbxOutlineCorrectionBuilder.Build(cube, false);
        Check(encoded.Zip(split, (a, b) => (a - b).sqrMagnitude).Any(e => e > 0.01f), "Weld option changes hard-edge seams");

        var weights = new BoneWeight[cube.vertexCount];
        for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = i % 2, weight0 = 1 };
        cube.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
        cube.boneWeights = weights;
        Parity(cube, "Different seam skin weights");
        var deltas = new Vector3[cube.vertexCount];
        deltas[0] = Vector3.up;
        cube.AddBlendShapeFrame("Seam deformation", 50, deltas, null, null);
        deltas[1] = Vector3.right;
        cube.AddBlendShapeFrame("Seam deformation", 100, deltas, null, null);
        Parity(cube, "Multiple morph frames / protected seams");
        UnityEngine.Object.DestroyImmediate(cube);
    }

    private static Mesh[] Meshes(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name).ToArray();
    private static string Geometry(Mesh mesh)
    {
        // Include topology, normals/tangents, UVs, skinning, bind poses and every morph frame.
        Mesh copy = UnityEngine.Object.Instantiate(mesh);
        copy.colors = Array.Empty<Color>();
        string json = EditorJsonUtility.ToJson(copy);
        UnityEngine.Object.DestroyImmediate(copy);
        return json;
    }

    private static void ImportChecks(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        Check(!HoFbxOutlineVertexColorPostprocessor.IsEnabled(importer), path + ": default off");
        importer.importNormals = ModelImporterNormals.Calculate;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.isReadable = true;
        importer.userData = "Other tool metadata\r\n{\"keep\":true}\n";
        importer.SaveAndReimport();
        Mesh[] original = Meshes(path);
        Check(original.Length > 0, path + ": FBX contains meshes");
        Color[][] colors = original.Select(m => m.colors).ToArray();
        if (path == "Assets/OutlineFixture.fbx")
            Check(colors.All(c => c.Length > 0 && c.All(v => Mathf.Abs(v.a - 0.8f) < 0.01f)), path + ": fixture has original authored RGBA");
        string[] originalGeometry=original.Select(Geometry).ToArray();
        Mesh[] prepared=original.Select(m=>UnityEngine.Object.Instantiate(m)).ToArray();
        for(int i=0;i<prepared.Length;i++){prepared[i].name=original[i].name;HoFbxOutlineCorrectionBuilder.RepairTangentFrames(prepared[i]);}
        string[] geometry = prepared.Select(Geometry).ToArray();
        Vector4[][] expected = prepared.Select(m => HoGeometryDataOutlineCorrectionBuilder.Build(m)).ToArray();
        foreach(Mesh mesh in prepared)UnityEngine.Object.DestroyImmediate(mesh);
        string otherData = importer.userData;
        HoFbxOutlineVertexColorPostprocessor.SetSettings(importer, true, true);
        if (path == "Assets/OutlineFixture.fbx")
        {
            importer.importTangents = ModelImporterTangents.None;
            importer.SaveAndReimport();
            Check(Meshes(path).All(m=>m.colors.All(c=>((Vector4)c-new Vector4(.5f,.5f,1,1)).sqrMagnitude<1e-10f)), path + ": missing tangents replaces authored colors with neutral correction data");
            importer.importTangents = ModelImporterTangents.CalculateMikk;
        }
        importer.SaveAndReimport();
        Mesh[] baked = Meshes(path);
        Check(baked.Length == original.Length && baked.Select((m, i) => Geometry(m) == geometry[i]).All(v => v), path + ": geometry / skin / morphs preserved");
        Check(baked.Select((m, i) => m.colors.Length == expected[i].Length && m.colors.Select((c, v) => ((Vector4)c - expected[i][v]).sqrMagnitude < 1e-10f).All(v => v)).All(v => v), path + ": imported colors equal GD output");
        Check(baked.Any(m => m.colors.Any(c => Mathf.Abs(c.a - 0.5f) < 0.3f)), path + ": alpha was replaced");
        Check(baked.All(m=>m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Color)==UnityEngine.Rendering.VertexAttributeFormat.Float32),path+": correction RGBA is Float32");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Check(HoFbxOutlineVertexColorPostprocessor.IsEnabled(AssetImporter.GetAtPath(path)), path + ": opt-in persists through forced import");
        importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = false;
        importer.SaveAndReimport();
        Check(!Meshes(path)[0].isReadable && !importer.isReadable, path + ": Read/Write stays disabled after baking");
        Check(Meshes(path).Select((m, i) => m.colors.Length == expected[i].Length && m.colors.Select((c, v) => ((Vector4)c - expected[i][v]).sqrMagnitude < 1e-10f).All(v => v)).All(v => v), path + ": Read/Write off still bakes GD colors");
        importer.isReadable = true;
        HoFbxOutlineVertexColorPostprocessor.SetSettings(importer, true, false);
        importer.SaveAndReimport();
        Check(!HoFbxOutlineVertexColorPostprocessor.GetWeld(importer), path + ": split setting persists");
        Check(Meshes(path).All(m =>
        {
            Vector4[] splitExpected = HoFbxOutlineCorrectionBuilder.Build(m, false);
            return m.colors.Select((c, v) => ((Vector4)c - splitExpected[v]).sqrMagnitude < 1e-10f).All(v => v);
        }), path + ": split import uses split builder");
        HoFbxOutlineVertexColorPostprocessor.SetSettings(importer, false, false);
        Check(importer.userData == otherData, path + ": other userData preserved exactly");
        importer.SaveAndReimport();
        Check(Meshes(path).Select((m, i) => m.colors.SequenceEqual(colors[i])).All(v => v), path + ": disabling restores original vertex colors");
        Check(Meshes(path).Select((m,i)=>Geometry(m)==originalGeometry[i]).All(v=>v),path+": disabling restores original geometry / tangents");
    }

    private static void WriteFixture()
    {
        // Small ASCII FBX with an explicit original RGBA layer; no external exporter required.
        string fbx = @"; FBX 7.4.0 project file
FBXHeaderExtension: { FBXHeaderVersion: 1003
 FBXVersion: 7400
}
GlobalSettings: { Version: 1000
 Properties70: {
  P: ""UpAxis"", ""int"", ""Integer"", """",1
  P: ""UpAxisSign"", ""int"", ""Integer"", """",1
  P: ""FrontAxis"", ""int"", ""Integer"", """",2
  P: ""FrontAxisSign"", ""int"", ""Integer"", """",-1
  P: ""CoordAxis"", ""int"", ""Integer"", """",0
  P: ""CoordAxisSign"", ""int"", ""Integer"", """",1
  P: ""UnitScaleFactor"", ""double"", ""Number"", """",100
 }
}
Definitions: { Version: 100
 Count: 2
 ObjectType: ""Geometry"" { Count: 1 }
 ObjectType: ""Model"" { Count: 1 }
}
Objects: {
 Geometry: 1001, ""Geometry::Corner"", ""Mesh"" {
  Vertices: *12 { a: 0,0,0,1,0,0,0,1,0,0,0,1 }
  PolygonVertexIndex: *12 { a: 0,2,-2,0,1,-4,0,3,-3,1,2,-4 }
  LayerElementUV: 0 {
   Version: 101
   Name: ""UVMap""
   MappingInformationType: ""ByPolygonVertex""
   ReferenceInformationType: ""Direct""
   UV: *24 { a: 0,0,0,1,1,0,0,0,1,0,0,1,0,0,1,0,0,1,0,0,1,0,0,1 }
  }
  LayerElementColor: 0 {
   Version: 101
   Name: ""OriginalColors""
   MappingInformationType: ""ByVertice""
   ReferenceInformationType: ""Direct""
   Colors: *16 { a: 0.2,0.4,0.6,0.8,0.2,0.4,0.6,0.8,0.2,0.4,0.6,0.8,0.2,0.4,0.6,0.8 }
  }
  Layer: 0 {
   Version: 100
   LayerElement: { Type: ""LayerElementUV""
    TypedIndex: 0
   }
   LayerElement: { Type: ""LayerElementColor""
    TypedIndex: 0
   }
  }
 }
 Model: 1002, ""Model::Corner"", ""Mesh"" { Version: 232 }
}
Connections: {
 C: ""OO"",1001,1002
 C: ""OO"",1002,0
}
";
        File.WriteAllText("Assets/OutlineFixture.fbx", fbx.Replace("{", "{\n").Replace("}", "\n}\n"));
    }
}
