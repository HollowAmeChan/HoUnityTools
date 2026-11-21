#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;
using Hollow.HoUnityTools.RigConstraints.Import;
using Hollow.HoUnityTools.Editor.RigConstraints;
using Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR;

public static class UnityImportValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
    private static string Encode(HoConstraintJsonValue root)
    {
        root.Properties["inputFingerprint"] = HoConstraintJsonValue.String(new string('0', 64));
        var document = HoConstraintIrV4Reader.Read(HoConstraintJson.Serialize(root));
        root.Properties["inputFingerprint"] = HoConstraintJsonValue.String(document.ComputeInputFingerprint()); return HoConstraintJson.Serialize(root);
    }
    public static void Run()
    {
        var failures = new List<string>();
        try { Validate(); }
        catch (Exception error) { failures.Add(error.ToString()); Debug.LogException(error); }
        File.WriteAllText("v4-import-results.json", "{\"checks\":" + checks + ",\"failures\":[" + string.Join(",", failures.Select(x => "\"" + x.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"").ToArray()) + "]}");
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }
    private static void Validate()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/toy.v4.json"); Check(asset != null, "Missing fixture");
        var rig = new GameObject("Rig");
        foreach (string name in new[] { "Owner", "Target", "Other", "MCH_Target" }) { var bone = new GameObject(name).transform; bone.SetParent(rig.transform, false); }
        EditorSceneManager.SaveScene(rig.scene, "Assets/Fixture/Validation.unity");
        var original = HoConstraintIrAssetLoader.Load(asset, "HoAux"); Check(original.sourceContext.Document.Root.Get("schemaVersion").Int32() == 4, "Source version lost");
        original.knownConstraints.Clear(); Check(HoConstraintIrAssetLoader.Load(asset, "HoAux").knownConstraints.Count == 1, "Mutable loader state leaked");
        int first = HoFbxConstraintImportUtility.ApplyHoAux(asset, rig, rig.transform); Check(first == 1, "World copyR v4 import did not reach runtime");
        var ho = rig.GetComponent<HoAuxRig>(); Check(ho != null && ho.DirectOperations.Count == 1 && ho.ExporterVersion == "4", "Runtime/source metadata missing");
        var report = HoConstraintIrImportReports.instance.Find(rig); Check(report != null && report.sourceSchemaVersion == 4 && report.selectedImplementationsJson.Contains("hoaux.copy_rotation.world"), "Report did not retain selected implementation");
        Check(report.authoritativeSourceJson.Contains("\"schemaVersion\":4"), "Authoritative document not persisted");
        Check(rig.GetComponents<Component>().All(x => x.GetType().Name != "HoConstraintIrImportReport"), "Source JSON report leaked onto runtime object");
        HoFbxConstraintImportUtility.ApplyHoAux(asset, rig, rig.transform); Check(rig.GetComponents<HoAuxRig>().Length == 1 && rig.GetComponent<HoAuxRig>().DirectOperations.Count == 1, "Repeat import duplicated operations");
        var invalid = HoConstraintJson.Parse(asset.text); invalid.Get("registry").Properties["sha256"] = HoConstraintJsonValue.String(new string('0', 64));
        var previousHo = rig.GetComponent<HoAuxRig>(); var previousReport = HoConstraintIrImportReports.instance.Find(rig).inputFingerprint;
        Reject(() => HoFbxConstraintImportUtility.ApplyHoAux(new TextAsset(Encode(invalid)), rig, rig.transform), "Registry mismatch accepted"); Check(rig.GetComponent<HoAuxRig>() == previousHo && HoConstraintIrImportReports.instance.Find(rig).inputFingerprint == previousReport, "Failed preflight changed existing assembly/report");
        var unsupported = HoConstraintJson.Parse(asset.text); var p = unsupported.Get("sources").Items[0].Get("constraint").Get("parameters"); p.Properties["owner_space"] = HoConstraintJsonValue.String("LOCAL"); p.Properties["target_space"] = HoConstraintJsonValue.String("LOCAL");
        unsupported.Get("implementationBindings").Items[0].Properties["implementationId"] = HoConstraintJsonValue.String("hoaux.copy_rotation.local");
        Reject(() => HoFbxConstraintImportUtility.ApplyHoAux(new TextAsset(Encode(unsupported)), rig, rig.transform), "Unsupported-only import cleared assembly"); Check(rig.GetComponent<HoAuxRig>() == previousHo, "Unsupported import removed prior runtime");
        var objectOnly = HoConstraintJson.Parse(asset.text); objectOnly.Get("sources").Items[0].Properties["ownerKind"] = HoConstraintJsonValue.String("OBJECT");
        Reject(() => HoFbxConstraintImportUtility.ApplyHoAux(new TextAsset(Encode(objectOnly)), rig, rig.transform), "Object-only import cleared prior assembly"); Check(rig.GetComponent<HoAuxRig>() == previousHo, "Object owner damaged bone runtime");
        var prefab = PrefabUtility.SaveAsPrefabAsset(rig, "Assets/Fixture/Imported.prefab"); Check(prefab.GetComponent<HoAuxRig>().DirectOperations.Count == 1, "Prefab runtime serialization failed");
        var serializedPrefab = File.ReadAllText("Assets/Fixture/Imported.prefab"); Check(!serializedPrefab.Contains("authoritativeSourceJson") && !serializedPrefab.Contains("selectedImplementationsJson"), "Audit payload leaked into prefab/build input");
        var goodConfig = HoConstraintIrAssetLoader.Load(asset, "HoAux");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); HoConstraintIrImportReports.instance.Remove(rig); Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); Check(HoConstraintIrImportReports.instance.Find(rig) != null, "Report undo did not restore prior import evidence");
        var type = typeof(HoFbxConstraintImportUtility).Assembly.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow");
        var apply = type.GetMethod("ApplyConstraints", BindingFlags.NonPublic | BindingFlags.Static); var mode = type.GetNestedType("ConstraintMode", BindingFlags.NonPublic);
        Reject(() => apply.Invoke(null, new object[] { asset, rig, Enum.Parse(mode, "Standard"), rig.transform }), "HoAux-only binding unexpectedly executed on Standard");
        Check(rig.GetComponent<HoAuxRig>() == previousHo, "Unsupported Standard mode removed prior HoAux");
        var standardRoot = HoConstraintJson.Parse(asset.text); standardRoot.Get("implementationBindings").Items[0].Properties["backendId"] = HoConstraintJsonValue.String("Standard"); standardRoot.Get("implementationBindings").Items[0].Properties["implementationId"] = HoConstraintJsonValue.String("standard.copy_rotation.world");
        int standard = (int)apply.Invoke(null, new object[] { new TextAsset(Encode(standardRoot)), rig, Enum.Parse(mode, "Standard"), rig.transform });
        Check(standard == 1 && rig.GetComponent<HoAuxRig>() == null && rig.transform.Find("Owner").GetComponent<RotationConstraint>() != null, "v4 Standard implementation did not replace prior backend");
        ValidateOperatorFixtures();
    }
    private static void ValidateOperatorFixtures()
    {
        if (!Directory.Exists("Assets/Fixture/Operator")) return;
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/Operator/ordinary_v4.fbx");
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/Operator/HoFBX/ordinary_v4_HoFbx_ordinary_v4_constraint.json");
        Check(imported != null && json != null, "Missing real HoFBX operator artifacts");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(imported); var source = HoConstraintIrAssetLoader.Load(json, "HoAux");
        var skeleton = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == source.armatureName);
        Check(skeleton != null, "Real FBX armature root could not be resolved");
        int count = HoFbxConstraintImportUtility.ApplyHoAux(json, skeleton.gameObject, skeleton);
        Check(count == 6, "Real operator v4 expected six available operations (local copyR remains planned), received " + count);
        var runtime = skeleton.GetComponent<HoAuxRig>(); Check(runtime != null && runtime.OperationCount == 6, "Real operator HoAux operation count differs");
        Check(source.sourceContext.UnsupportedConstraints.Count == 1, "Planned local copyR was not independently reported unsupported");
        Check(HoConstraintIrImportReports.instance.Find(skeleton.gameObject).authoritativeSourceJson.Contains("EXPORT_GENERATED"), "Real MCH provenance lost");
        int again = HoFbxConstraintImportUtility.ApplyHoAux(json, skeleton.gameObject, skeleton); Check(again == count && skeleton.GetComponent<HoAuxRig>().OperationCount == count, "Real operator reimport grew operations");
        var projected = HoConstraintIrAssetLoader.Load(json, "HoAux"); var binding = projected.mchBindings.Single();
        Check(binding.sourceBone != binding.mchBone && binding.constraint.targetBoneName == binding.sourceBone, "MCH final source was remapped to itself");
        var empty = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/Operator/HoFBX/empty_v4_HoFbx_empty_v4_constraint.json");
        Check(HoFbxConstraintImportUtility.ApplyHoAux(empty, skeleton.gameObject, skeleton) == 0 && skeleton.GetComponent<HoAuxRig>() == null, "Explicit empty v4 did not clear previous import safely");
        string path = "Assets/Fixture/Operator/HoFBX/compat_v3_HoFbx_compat_v3_constraint.json";
        var legacy = HoConstraintIrAssetLoader.Load(AssetDatabase.LoadAssetAtPath<TextAsset>(path), "HoAux");
        Check(legacy.schemaVersion == 3 && legacy.sourceContext == null, "v3 compatibility was reinterpreted through v4");
    }
}
#endif
