using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Armatures;
using Hollow.HoUnityTools.Editor.Armatures;
using Hollow.HoUnityTools.RigConstraints;

public static class HoAuxMergeValidation
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var probe=new GameObject("Merge parity").AddComponent<HoAuxMergeProbe>();
            probe.cases=new[]{Create("Unmerged",false),Create("Merged",true),Create("Wrapper baseline",false,true),Create("Wrapper merged",true,true),Create("Offset baseline",false,false,true),Create("Offset merged",true,false,true)};
            for(int i=0;i<probe.cases.Length;i++)probe.cases[i].referenceIndex=i-i%2;
            probe.output=Path.GetFullPath(Path.Combine(Application.dataPath,"../merge-results.json"));
            foreach(var c in probe.cases)
            {
                foreach(var op in c.rig.DirectOperations)
                {
                    var path=HoAuxRig.GetOperationPath(c.rig.RigRoot,op.owner);
                    if(path!=op.ownerPath)throw new Exception(c.name+" stale owner path: "+op.ownerPath+" -> "+path);
                }
            }
            for(int i=1;i<probe.cases.Length;i+=2)probe.cases[i]=ReloadAndResolve(probe.cases[i]);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/MergeParity.unity");
            EditorApplication.isPlaying=true;
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static HoAuxMergeCase Create(string name,bool merge,bool wrapperSpace=false,bool offsetRoot=false)
    {
        var character=new GameObject(name).transform;var body=Child(character,"Body",Vector3.zero);
        var bodyHips=Child(body,"Hips",Vector3.up);var bodyChest=Child(bodyHips,"Chest",Vector3.up);
        var wrapper=Child(character,"Model",Vector3.zero);
        var source=Child(wrapper,"Outfit",wrapperSpace||offsetRoot?new Vector3(.1f,.2f,-.3f):Vector3.zero);
        if(wrapperSpace||offsetRoot){source.localRotation=Quaternion.Euler(0,20,0);source.localScale=Vector3.one*1.2f;}
        if(!offsetRoot){body.position=source.position;body.rotation=source.rotation;body.localScale=source.localScale;}
        var hips=Child(source,"Hips",Vector3.up);var chest=Child(hips,"Chest",Vector3.up);
        var point=Child(source,"Point",new Vector3(.2f,2,.3f));var copy=Child(source,"Copy",new Vector3(-.2f,2,.3f));
        var aim=Child(source,"Aim",new Vector3(.2f,1,.3f));var up=Child(source,"Up",new Vector3(.5f,3,.3f));var fan=Child(hips,"Fan",new Vector3(.3f,1,0));var mch=Child(hips,"MCH_Chest",Vector3.up);
        Transform space=wrapperSpace?wrapper:source;
        var ho=Child(character,"Constraints",Vector3.zero).gameObject.AddComponent<HoAuxRig>();ho.RigRoot=ho.transform;ho.Mode=HoAuxRig.UpdateMode.Manual;ho.SetDirectSpace(space);
        ho.AddOperation(HoAuxRig.OperationType.Parent,mch,chest);
        var legacy=ho.AddOperation(HoAuxRig.OperationType.Fan,fan,chest,.5f);legacy.sourceSpace=HoAuxRig.Space.Local;legacy.targetSpace=HoAuxRig.Space.Local;ho.CaptureBindPose();
        foreach(var owner in new[]{point,up})
        {
            var op=Direct("ARMATURE",owner);op.positionOnly=true;op.sources.Add(new HoAuxRig.DirectSource{bone=chest,weight=1,inverseBind=(space.worldToLocalMatrix*chest.localToWorldMatrix).inverse});ho.AddDirectOperation(op);
        }
        var cp=Direct("COPY_LOCATION",copy);cp.target=point;ho.AddDirectOperation(cp);
        var look=Direct("AIM",aim);look.target=point;look.up=up;ho.AddDirectOperation(look);
        // These are live edits, not the captured baseline. Merge must not bake them.
        point.localPosition+=new Vector3(.4f,.1f,-.2f);
        if(merge)
        {
            var marker=source.gameObject.AddComponent<HoCombineArmature>();marker.targetArmature=body;marker.characterRoot=character;
            HoCombineArmatureProcessor.ProcessHierarchy(character.gameObject);
        }
        Transform driver=merge?body:wrapperSpace?wrapper:source;
        return new HoAuxMergeCase{name=name,rig=ho,root=driver,chest=merge?bodyChest:chest,point=point,copy=copy,aim=aim,fan=fan,mch=mch,rootPosition=driver.localPosition,rootRotation=driver.localRotation,rootScale=driver.localScale,animateChest=!offsetRoot};
    }
    private static HoAuxMergeCase ReloadAndResolve(HoAuxMergeCase original)
    {
        var character=original.rig.transform.parent.gameObject;
        string rootPath=AnimationUtility.CalculateTransformPath(original.root,character.transform),chestPath=AnimationUtility.CalculateTransformPath(original.chest,character.transform);
        string asset="Assets/"+original.name+".prefab";
        PrefabUtility.SaveAsPrefabAsset(character,asset);UnityEngine.Object.DestroyImmediate(character);
        var clone=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(asset));
        var ho=clone.GetComponentInChildren<HoAuxRig>();ho.enabled=false;
        foreach(var layer in ho.Layers)foreach(var op in layer.operations){op.owner=null;op.target=null;}
        foreach(var op in ho.DirectOperations){op.owner=null;op.target=null;op.up=null;foreach(var source in op.sources)source.bone=null;}
        var so=new SerializedObject(ho);so.FindProperty("directSpace").objectReferenceValue=null;so.ApplyModifiedPropertiesWithoutUndo();ho.enabled=true;
        typeof(HoAuxRig).GetMethod("OnEnable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(ho,null);
        if(ho.OperationCount!=6||ho.DirectOperations.Any(op=>op.owner==null)||(Transform)new SerializedObject(ho).FindProperty("directSpace").objectReferenceValue==null)
            throw new Exception("Merged prefab path restoration failed");
        return new HoAuxMergeCase{name=original.name+" (prefab/path reload)",rig=ho,root=clone.transform.Find(rootPath),chest=clone.transform.Find(chestPath),point=ho.DirectOperations[0].owner,copy=ho.DirectOperations[2].owner,aim=ho.DirectOperations[3].owner,fan=ho.Layers.First(l=>l.operations[0].type==HoAuxRig.OperationType.Fan).operations[0].owner,mch=ho.Layers.First(l=>l.operations[0].type==HoAuxRig.OperationType.Parent).operations[0].owner,rootPosition=original.rootPosition,rootRotation=original.rootRotation,rootScale=original.rootScale,referenceIndex=original.referenceIndex,animateChest=original.animateChest};
    }
    private static HoAuxRig.DirectOperation Direct(string type,Transform owner)
    {return new HoAuxRig.DirectOperation{type=type,owner=owner,bindLocalPosition=owner.localPosition,bindLocalRotation=owner.localRotation,bindLocalScale=owner.localScale};}
    private static Transform Child(Transform parent,string name,Vector3 local)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=local;return t;}
}
