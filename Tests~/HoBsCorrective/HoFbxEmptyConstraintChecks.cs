#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Editor.RigConstraints;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;
using Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

/// <summary>Empty export snapshots clear managed constraints without blocking BS import.</summary>
public static class HoFbxEmptyConstraintChecks
{
    private const string Folder = "Assets/00动画/角色/羽啾/";
    private static readonly Type Window = typeof(HoFbxConstraintImportUtility).Assembly.GetType(
        "Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow");
    private static readonly Type Mode = Window.GetNestedType("ConstraintMode", BindingFlags.NonPublic);
    private static readonly List<string> Passed = new List<string>();
    private const string Identity = "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]";

    public static void Run()
    { RunChecks(true); }

    public static void RunSynthetic()
    { RunChecks(false); }

    private static void RunChecks(bool character)
    {
        try
        {
            foreach (string file in new[] { "empty_constraint_results.txt", "empty_constraint_error.txt" })
                if (File.Exists(file)) File.Delete(file);
            if (character) AuditMetas();
            foreach (string backend in new[] { "Standard", "HoAux", "Vrc" })
                CheckEmpty(backend);
            CheckRejectedSnapshots();
            CheckSupportedImport();
            if (character) CheckCharacterPipeline();
            File.WriteAllLines("empty_constraint_results.txt", Passed.Concat(new[] { "HOFBX_EMPTY_CONSTRAINT_OK" }));
            Debug.Log("HOFBX_EMPTY_CONSTRAINT_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("empty_constraint_error.txt", error.ToString());
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }

    private static int Apply(TextAsset asset, GameObject rig, string backend, Transform root)
    {
        try
        {
            return (int)Window.GetMethod("ApplyConstraints", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { asset, rig, Enum.Parse(Mode, backend), root });
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
    }
    private static HoConstraintJsonValue Empty()
    {
        var registry = HoConstraintIrAssetLoader.LoadDistributedRegistry();
        var root = HoConstraintJson.Parse("{\"schema\":\"hotools.rig-constraint-ir\",\"schemaVersion\":4,\"armatureName\":\"Rig\",\"exportTime\":\"2026-11-21T00:00:00Z\",\"blenderVersion\":\"4.5.8\",\"captureStage\":\"AUTHORING\",\"registry\":{\"id\":\"hotools.rig-constraints.registry\",\"version\":1,\"sha256\":\"" + registry.Sha256 + "\"},\"sources\":[],\"semantics\":[],\"implementationBindings\":[],\"inputFingerprint\":\"" + new string('0', 64) + "\",\"mchPlans\":[\"Owner\"],\"excludedOwners\":[],\"bindings\":{\"captureStage\":\"POSTPROCESS\",\"boneMap\":[],\"constraintMap\":[],\"bindingContext\":{\"space\":\"BLENDER_ARMATURE\",\"matrixLayout\":\"ROW_MAJOR\",\"fbxBoneAxes\":\"Y_X\",\"objectMatrix\":" + Identity + ",\"bones\":[]}}}");
        foreach (string name in new[] { "Owner", "Target" })
            root.Get("bindings").Get("bindingContext").Get("bones").Items.Add(HoConstraintJson.Parse("{\"boneName\":\"" + name + "\",\"parentBone\":\"\",\"inheritScale\":\"FULL\",\"inheritRotation\":true,\"restMatrix\":" + Identity + ",\"poseBasis\":" + Identity + ",\"connected\":false,\"positionOnly\":false,\"hasPoseAnimation\":false}"));
        root.Properties["sourceContext"] = root.Get("bindings").Get("bindingContext").Clone();
        return root;
    }
    private static string Encode(HoConstraintJsonValue root)
    {
        var document = HoConstraintIrV4Reader.Read(HoConstraintJson.Serialize(root));
        root.Properties["inputFingerprint"] = HoConstraintJsonValue.String(document.ComputeInputFingerprint());
        return HoConstraintJson.Serialize(root);
    }
    private static void AddSource(HoConstraintJsonValue root, string kind, string targetObject = "Rig", bool semantic = false)
    {
        var source = HoConstraintJson.Parse("{\"sourceId\":\"c0\",\"ownerBone\":\"Owner\",\"ownerKind\":\"BONE\",\"captureStage\":\"AUTHORING\",\"constraint\":{\"name\":\"Source\",\"constraintType\":\"" + kind + "\",\"stackIndex\":0,\"targetObjectName\":\"" + targetObject + "\",\"targetBoneName\":\"Target\",\"parameters\":{\"influence\":1,\"owner_space\":\"WORLD\",\"target_space\":\"WORLD\",\"mix_mode\":\"REPLACE\",\"use_x\":true,\"use_y\":true,\"use_z\":true},\"references\":{}}}");
        root.Get("sources").Items.Add(source);
        if (!semantic) return;
        root.Get("semantics").Items.Add(HoConstraintJson.Parse("{\"semanticId\":\"s0\",\"semanticType\":\"COPY_ROTATION\",\"semanticVersion\":1,\"ownerBone\":\"Owner\",\"sourceIds\":[\"c0\"],\"ruleId\":\"group.copy_r\",\"ruleVersion\":1,\"evidence\":\"GENERATED\",\"parameters\":{\"auxType\":\"COPY_R\"}}"));
        root.Get("implementationBindings").Items.Add(HoConstraintJson.Parse("{\"semanticId\":\"s0\",\"backendId\":\"HoAux\",\"implementationId\":\"hoaux.copy_rotation.world\",\"contractVersion\":1,\"selection\":\"CANDIDATE\",\"fallbackPolicy\":\"EXACT_ONLY\"}"));
    }

    private static void CheckEmpty(string backend)
    {
        var container = new GameObject("Empty v4 import /" + backend);
        var rig = Child(container.transform, "Rig");
        var owner = Child(rig.transform, "Owner");
        var managed = owner.AddComponent<RotationConstraint>();
        var marker = owner.AddComponent<HoImportedConstraintMarker>();
        marker.sourceArmature = "Rig"; marker.hostRole = ConstraintHostRole.Bone; marker.Register(managed);
        var manual = Child(rig.transform, "Manual").AddComponent<PositionConstraint>();
        var oldHo = Child(rig.transform, "Old imported rig").AddComponent<HoAuxRig>();
        oldHo.SourceArmature = "Rig";
        var handHo = Child(container.transform, "Manual host").AddComponent<HoAuxRig>();
        handHo.Layers.Add(new HoAuxRig.Layer { operations = new List<HoAuxRig.Operation> { new HoAuxRig.Operation() } });
        var helper = Child(rig.transform, "Generated helper").AddComponent<HoImportedConstraintMarker>();
        helper.hostRole = ConstraintHostRole.GeneratedHelper; helper.sourceArmature = "Rig"; helper.directBackend = "HoAux";
        var vrc = Child(handHo.transform, "Old VRC host").AddComponent<HoImportedConstraintMarker>();
        vrc.hostRole = ConstraintHostRole.VrcConstraintObject; vrc.sourceArmature = "Rig";
        var asset = new TextAsset(Encode(Empty()));
        try
        {
            Check(Apply(asset, rig, backend, handHo.transform) == 0, "Empty import should return zero");
            Check(managed == null && marker == null && oldHo == null && helper == null && vrc == null, "Old generated constraints were not cleared");
            Check(manual != null && handHo != null && handHo.Layers[0].operations.Count == 1 && owner != null, "Manual content was changed");
            Check(Apply(asset, rig, backend, handHo.transform) == 0, "Repeated empty import failed");
            Passed.Add("Empty v4/" + backend + ": cleanup, manual preservation, repeat import");
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(container); }
    }

    private static void CheckRejectedSnapshots()
    {
        foreach (string problem in new[] { "unknown", "malformed", "unsupported" })
        {
            var rig = new GameObject("Rig"); Child(rig.transform, "Owner"); Child(rig.transform, "Target");
            var keep = rig.AddComponent<HoAuxRig>(); keep.SourceArmature = "Rig";
            var config = Empty();
            if (problem == "unknown") AddSource(config, "LIMIT_ROTATION");
            else if (problem == "unsupported") AddSource(config, "COPY_ROTATION", "Other armature", true);
            var asset = new TextAsset(problem == "malformed"
                ? "{\"schema\":\"hotools.rig-constraint-ir\",\"schemaVersion\":4,\"armatureName\":\"Rig\"}"
                : Encode(config));
            try
            {
                bool threw = false;
                try { Apply(asset, rig, "HoAux", rig.transform); }
                catch (Exception error) when (error is InvalidOperationException || error is InvalidDataException || error is FormatException) { threw = true; }
                Check(threw && keep != null, "Nonempty/invalid snapshot bypassed safety: " + problem);
                Passed.Add(problem + ": rejected and preserved previous import");
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(rig); }
        }
    }
    private static void CheckSupportedImport()
    {
        var rig = new GameObject("Rig"); Child(rig.transform, "Owner"); Child(rig.transform, "Target");
        var config = Empty();
        AddSource(config, "COPY_ROTATION", "Rig", true);
        var asset = new TextAsset(Encode(config));
        try
        {
            Check(Apply(asset, rig, "HoAux", rig.transform) == 1 && rig.GetComponent<HoAuxRig>().OperationCount == 1,
                "Supported nonempty constraint import regressed");
            Passed.Add("Nonempty supported constraint still imports");
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); UnityEngine.Object.DestroyImmediate(rig); }
    }

    private static void CheckCharacterPipeline()
    {
        string modelPath = Folder + "羽啾.fbx";
        HoFbxHumanoidImportUtility.Apply(AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "HoFBX/羽啾_humanoid.json"), modelPath);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "HoFBX/羽啾_Armature-Chu2u_constraint.json");
            string name = HoConstraintIrAssetLoader.Load(asset, "HoAux").armatureName;
            var skeleton = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
            Check(HoFbxConstraintImportUtility.ApplyHoAux(asset, skeleton.gameObject, null) == 0, "Real empty constraint import failed");
            Check(HoFbxBsCorrectiveImportUtility.Apply(AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "HoFBX/羽啾_bs_corrective.json"), root) == 60,
                "Empty constraints blocked the following BS import");
            var bs = root.GetComponent<HoBsCorrective>();
            Check(bs.Rules.All(r => r.bone != null && !r.measurementIsMch && HoBsCorrective.ValidateRule(r, out _)), "Real corrective bindings invalid");
            Passed.Add("Current Yuqiu export: Humanoid + empty HoAux + 60 valid direct-bone BS outputs");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void AuditMetas()
    {
        var expected = new Dictionary<string, string> {
            ["Assets/Editor/HoBsCorrectiveEditor.cs"] = "6639db23d58f47a1aad530497a000d9d",
            ["Assets/Editor/HoFbxBsCorrectiveImportUtility.cs"] = "e8e96a440aa44d36ad29d9c529b3138c",
            ["Assets/Editor/HoFbxImportProcessingWindow.BsCorrective.cs"] = "c69651a8f6de4578a3bcf6317c4fd3b5",
            ["Assets/Runtime/HoBsCorrective.cs"] = "62d8aabce487460e8dcf1c1529061f38",
            ["Assets/Runtime/HoBsCorrective.UpdateLoop.cs"] = "1e290905b42547f19e8bee45e5456c8e" };
        foreach (var item in expected)
            Check(AssetDatabase.AssetPathToGUID(item.Key) == item.Value, "Script GUID changed: " + item.Key);
        Passed.Add("Five repaired .meta files imported with their original GUIDs");
    }
}
#endif
