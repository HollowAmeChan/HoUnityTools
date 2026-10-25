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
        string reportPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../results.json"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            ConstraintChecks.Run();
            ConstraintChecks.Case("Temporary editor preview",()=>{
                var source=ConstraintChecks.Obj("source",Vector3.right*9);var target=ConstraintChecks.Obj("target",Vector3.one);var c=ConstraintChecks.Constraint<HoPositionConstraint>(target,source);ConstraintChecks.Activate(c);c.PreviewInEditMode=true;
                HoTransformConstraintPreview.Tick();ConstraintChecks.Near(target.position,source.position,"Edit preview follows");
                source.position=Vector3.right*10;HoTransformConstraintPreview.Tick();ConstraintChecks.Near(target.position,source.position,"Edit preview updates source");
                target.rotation=Quaternion.Euler(10,20,30);var q=target.rotation;c.PreviewInEditMode=false;HoTransformConstraintPreview.Tick();
                ConstraintChecks.Near(target.position,Vector3.one,"Preview off restores author pose");ConstraintChecks.Near(target.rotation,q,"Preview restores owned channels only");
                c.PreviewInEditMode=true;HoTransformConstraintPreview.Tick();c.enabled=false;HoTransformConstraintPreview.Tick();ConstraintChecks.Near(target.position,Vector3.one,"Disable restores preview");
                c.enabled=true;HoTransformConstraintPreview.Tick();HoTransformConstraintPreview.RestoreAll();ConstraintChecks.Near(target.position,Vector3.one,"Reload/play restore hook");
                using(var inspector=new EditorScope(UnityEditor.Editor.CreateEditor(c)))ConstraintChecks.Check(inspector.Value is HoTransformConstraintEditor,"Custom inspector loads");
            });
            ConstraintChecks.Case("Explicit target edit Undo",()=>{
                var target=ConstraintChecks.Obj("target",Vector3.one);var source=ConstraintChecks.Obj("source",Vector3.one*9);var c=ConstraintChecks.Constraint<HoPositionConstraint>(target,source);ConstraintChecks.Activate(c);
                Undo.RecordObject(target,"Constraint sample");ConstraintChecks.Check(c.EvaluateNow(),"Manual sample succeeds");Undo.FlushUndoRecordObjects();
                ConstraintChecks.Near(target.position,Vector3.one*9,"Manual sample writes target");Undo.PerformUndo();ConstraintChecks.Near(target.position,Vector3.one,"Manual sample Undo restores target");Undo.PerformRedo();ConstraintChecks.Near(target.position,Vector3.one*9,"Manual sample Redo");Undo.ClearAll();
            });
            var prefabRoot=new GameObject("Serializable external constraint");
            var targetNode=new GameObject("target").transform;targetNode.SetParent(prefabRoot.transform);targetNode.localPosition=new Vector3(2,3,4);
            var sourceNode=new GameObject("source").transform;sourceNode.SetParent(prefabRoot.transform);sourceNode.localPosition=new Vector3(10,20,30);
            var host=new GameObject("external host");host.transform.SetParent(prefabRoot.transform);var component=host.AddComponent<HoPositionConstraint>();component.TargetTransform=targetNode;component.Sources.Add(new HoTransformConstraintSource{transform=sourceNode});ConstraintChecks.Activate(component,true);component.PreviewInEditMode=true;
            HoTransformConstraintPreview.Tick();sourceNode.localPosition+=Vector3.one;HoTransformConstraintPreview.Tick();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/PreviewScene.unity");
            ConstraintChecks.Near(targetNode.localPosition,new Vector3(2,3,4),"Scene save restores author position");
            PrefabUtility.SaveAsPrefabAsset(prefabRoot,"Assets/Constraint.prefab");UnityEngine.Object.DestroyImmediate(prefabRoot);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Constraint.prefab");var stored=prefab.GetComponentInChildren<HoPositionConstraint>();
            ConstraintChecks.Check(stored.HasRestPose&&stored.TargetTransform!=null&&stored.Sources[0].transform!=null,"Prefab target/source/binding survive serialization");
            var assetPose=stored.TargetTransform.localPosition;HoTransformConstraintPreview.Tick();ConstraintChecks.Near(stored.TargetTransform.localPosition,assetPose,"Persistent prefab asset is not previewed");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);var instanced=instance.GetComponentInChildren<HoPositionConstraint>();
            ConstraintChecks.Check(instanced.TargetTransform.IsChildOf(instance.transform)&&instanced.Sources[0].transform.IsChildOf(instance.transform),"Prefab instance references remap");
            ConstraintChecks.Eval(instanced);ConstraintChecks.Near(instanced.TargetTransform.localPosition,new Vector3(3,4,5),"Serialized offset evaluates in prefab instance");
            HoTransformConstraintPreview.RestoreAll();UnityEngine.Object.DestroyImmediate(instance);
            File.WriteAllText(reportPath,JsonUtility.ToJson(ConstraintChecks.Report,true));
            if(ConstraintChecks.Report.failures.Count!=0){EditorApplication.Exit(1);return;}
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var probe=new GameObject("Play mode integration").AddComponent<ConstraintPlayProbe>();probe.reportPath=reportPath;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/PlayValidation.unity");EditorApplication.isPlaying=true;
        }
        catch(Exception error)
        {ConstraintChecks.Report.failures.Add(error.ToString());File.WriteAllText(reportPath,JsonUtility.ToJson(ConstraintChecks.Report,true));Debug.LogException(error);EditorApplication.Exit(1);}
    }
    private sealed class EditorScope:IDisposable
    {public UnityEditor.Editor Value;public EditorScope(UnityEditor.Editor value){Value=value;}public void Dispose(){UnityEngine.Object.DestroyImmediate(Value);}}
}
