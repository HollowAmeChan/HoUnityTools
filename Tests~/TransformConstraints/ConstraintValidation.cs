using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.TransformConstraints;
using Hollow.HoUnityTools.Editor.TransformConstraints;

public static class ConstraintValidation
{
    public static void Run()
    {
        string reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../results.json"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CheckEditMode<HoPositionConstraint>();
            CheckEditMode<HoRotationConstraint>();
            CheckEditMode<HoScaleConstraint>();
            CheckEditMode<HoParentConstraint>();
            CheckEditMode<HoAimConstraint>();
            var prefab = PreparePrefab();
            File.WriteAllText(reportPath, JsonUtility.ToJson(ConstraintChecks.Report, true));
            if (ConstraintChecks.Report.failures.Count != 0)
            {
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var probe = new GameObject("Play mode integration").AddComponent<ConstraintPlayProbe>();
            probe.reportPath = reportPath;
            probe.prefab = prefab;
            var sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.sceneConstraint = sceneInstance.GetComponentInChildren<HoPositionConstraint>();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/PlayValidation.unity");
            EditorApplication.isPlaying = true;
        }
        catch (Exception error)
        {
            ConstraintChecks.Report.failures.Add(error.ToString());
            File.WriteAllText(reportPath, JsonUtility.ToJson(ConstraintChecks.Report, true));
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckEditMode<T>() where T : HoTransformConstraint
    {
        ConstraintChecks.Case(typeof(T).Name + " is runtime-only", () =>
        {
            var source = ConstraintChecks.Obj("source", new Vector3(8, 9, 10));
            source.rotation = Quaternion.Euler(14, 28, 42);
            source.localScale = new Vector3(3, 4, 5);
            var target = ConstraintChecks.Obj("target", Vector3.one);
            var constraint = ConstraintChecks.Constraint<T>(target, source);

            Undo.RecordObject(constraint, "Bind constraint");
            ConstraintChecks.Activate(constraint);
            Undo.FlushUndoRecordObjects();
            ConstraintChecks.Near(target.localPosition, Vector3.one, "Binding may read the editor pose");
            Undo.PerformUndo();
            ConstraintChecks.Check(!constraint.HasRestPose && !constraint.ConstraintActive, "Binding can be undone");
            Undo.PerformRedo();
            ConstraintChecks.Check(constraint.HasRestPose && constraint.ConstraintActive, "Binding can be redone");
            Undo.ClearAll();

            target.SetLocalPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(7, 17, 27));
            target.localScale = new Vector3(2, 2, 2);
            var authored = HoTransformLocalPose.Read(target);
            ConstraintChecks.Check(!constraint.EvaluateNow(), "Component evaluation rejects Edit Mode");
            HoTransformConstraintScheduler.Evaluate(new[] { constraint });
            constraint.RestoreRestPose();
            ConstraintChecks.Near(target.localPosition, authored.position, "Edit Mode preserves position");
            ConstraintChecks.Near(target.localRotation, authored.rotation, "Edit Mode preserves rotation");
            ConstraintChecks.Near(target.localScale, authored.scale, "Edit Mode preserves scale");
            ConstraintChecks.Check(!typeof(T).IsDefined(typeof(ExecuteAlways), true), "Component is not registered for editor execution");

            var inspector = UnityEditor.Editor.CreateEditor(constraint);
            try
            {
                ConstraintChecks.Check(inspector is HoTransformConstraintEditor, "Custom inspector loads");
                ConstraintChecks.Check(inspector.serializedObject.FindProperty("previewInEditMode") == null, "No edit preview setting");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inspector);
            }
        });
    }

    private static GameObject PreparePrefab()
    {
        var root = new GameObject("Serializable external constraint");
        var target = new GameObject("target").transform;
        target.SetParent(root.transform);
        target.localPosition = new Vector3(2, 3, 4);
        var source = new GameObject("source").transform;
        source.SetParent(root.transform);
        source.localPosition = new Vector3(10, 20, 30);
        var host = new GameObject("external host");
        host.transform.SetParent(root.transform);
        var component = host.AddComponent<HoPositionConstraint>();
        component.TargetTransform = target;
        component.Sources.Add(new HoTransformConstraintSource { transform = source });
        ConstraintChecks.Activate(component, true);
        source.localPosition += Vector3.one;
        HoTransformConstraintScheduler.Evaluate(new[] { component });

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/AuthoringScene.unity");
        ConstraintChecks.Near(target.localPosition, new Vector3(2, 3, 4), "Scene saves the authored pose");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Constraint.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Constraint.prefab");
        var stored = prefab.GetComponentInChildren<HoPositionConstraint>();
        ConstraintChecks.Check(stored.HasRestPose && stored.TargetTransform != null && stored.Sources[0].transform != null,
            "Prefab target, source and binding survive serialization");
        ConstraintChecks.Near(stored.TargetTransform.localPosition, new Vector3(2, 3, 4), "Prefab keeps the authored pose");
        return prefab;
    }
}
