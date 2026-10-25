using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Editor.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;
using Hollow.HoUnityTools.RigConstraints;
using UnityEngine.Animations;

public static class V3Validation
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh();
            var configAsset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/original_constraint_ir.json");
            var config=JsonUtility.FromJson<ConstraintConfig>(configAsset.text);
            var window=typeof(HoDirectConstraintImporter).Assembly.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow");
            var validate=window.GetMethod("TryValidateConstraintConfig",BindingFlags.NonPublic|BindingFlags.Static);
            object[] args={config,null};if(!(bool)validate.Invoke(null,args))throw new Exception("v3 validator: "+args[1]);
            if(HoDirectConstraintImporter.Build(config).Count!=11)throw new Exception("Expected 9 direct + 2 Aim operations");
            var legacy=new ConstraintConfig {schema="hotools.rig-constraint-ir",schemaVersion=2,armatureName="Legacy",mchEnabledBones=new System.Collections.Generic.List<string>(),mchBindings=new System.Collections.Generic.List<MchBindingInfo>(),auxBones=new System.Collections.Generic.List<AuxBoneInfo>(),knownConstraints=new System.Collections.Generic.List<KnownConstraintInfo>(),unknownConstraints=new System.Collections.Generic.List<UnknownConstraintInfo>()};
            args=new object[]{legacy,null};if(!(bool)validate.Invoke(null,args))throw new Exception("v2 compatibility broken");
            var bad=JsonUtility.FromJson<ConstraintConfig>(configAsset.text);bad.aimBindings[0].upAxis=bad.aimBindings[0].aimAxis;
            args=new object[]{bad,null};if((bool)validate.Invoke(null,args))throw new Exception("Bad Aim accepted");
            bad=JsonUtility.FromJson<ConstraintConfig>(configAsset.text);bad.knownConstraints.Add(bad.knownConstraints[0]);
            args=new object[]{bad,null};if((bool)validate.Invoke(null,args))throw new Exception("Duplicate classification accepted");
            var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/Fixture/ChestAddon.fbx");
            importer.animationType=ModelImporterAnimationType.Generic;importer.optimizeGameObjects=false;importer.importAnimation=false;importer.isReadable=true;importer.SaveAndReimport();
            StructuralChecks(configAsset,config,window);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var go=new GameObject("IR v3 production pipeline parity");var runner=go.AddComponent<V3RuntimeProbe>();
            runner.model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/ChestAddon.fbx");
            runner.fixture=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/samples.json");runner.ir=configAsset;
            runner.outputPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../v3-runtime-results.json"));
            EditorSceneManager.SaveScene(go.scene,"Assets/Fixture/V3Parity.unity");
            Debug.Log("IR_V3_EDITOR_VALIDATION_OK");EditorApplication.EnterPlaymode();
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }

    private static object Apply(Type window,TextAsset asset,GameObject rig,string backend,Transform root)
    {
        var mode=window.GetNestedType("ConstraintMode",BindingFlags.NonPublic);
        return window.GetMethod("ApplyConstraints",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{asset,rig,Enum.Parse(mode,backend),root});
    }
    private static void StructuralChecks(TextAsset asset,ConstraintConfig config,Type window)
    {
        var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/ChestAddon.fbx");
        var rig=(GameObject)PrefabUtility.InstantiatePrefab(model);
        Apply(window,asset,rig,"HoAux",rig.transform);
        var ho=rig.GetComponent<HoAuxRig>();
        if(ho.DirectOperations.Count!=11)throw new Exception("Ho operation count");
        PrefabUtility.SaveAsPrefabAsset(rig,"Assets/Fixture/HoV3.prefab");
        var saved=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/HoV3.prefab");
        var clone=(GameObject)PrefabUtility.InstantiatePrefab(saved);
        var cloned=clone.GetComponent<HoAuxRig>();
        foreach(var op in cloned.DirectOperations)
        {
            op.owner=null;op.target=null;op.up=null;
            foreach(var source in op.sources)source.bone=null;
        }
        var serialized=new SerializedObject(cloned);serialized.FindProperty("directSpace").objectReferenceValue=null;serialized.ApplyModifiedPropertiesWithoutUndo();
        typeof(HoAuxRig).GetMethod("ResolveDirectReferences",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(cloned,null);
        if(cloned.DirectOperations.Any(o=>o.owner==null||(o.type!="ARMATURE"&&o.target==null)||(o.type=="AIM"&&o.up==null)||o.sources.Any(s=>s.bone==null)))throw new Exception("Serialized reference paths did not resolve");
        UnityEngine.Object.DestroyImmediate(clone);UnityEngine.Object.DestroyImmediate(rig);

        rig=(GameObject)PrefabUtility.InstantiatePrefab(model);
        Apply(window,asset,rig,"Standard",rig.transform);
        int before=rig.GetComponentsInChildren<HoImportedConstraintMarker>(true).Length;
        var invalid=JsonUtility.FromJson<ConstraintConfig>(asset.text);
        foreach(var k in invalid.knownConstraints.Where(k=>k.relationType=="DIRECT_CONSTRAINT"))k.constraint.parameters.owner_space="CUSTOM";
        invalid.aimBindings.Clear();invalid.knownConstraints.RemoveAll(k=>k.relationType=="AIM_BINDING");
        bool refused=false;
        try{Apply(window,new TextAsset(JsonUtility.ToJson(invalid)),rig,"Standard",rig.transform);}catch(TargetInvocationException){refused=true;}
        if(!refused||before!=rig.GetComponentsInChildren<HoImportedConstraintMarker>(true).Length)throw new Exception("Invalid import cleared prior data");
        Apply(window,asset,rig,"Standard",rig.transform);
        if(before!=rig.GetComponentsInChildren<HoImportedConstraintMarker>(true).Length)throw new Exception("Standard reimport leaked helpers");
        UnityEngine.Object.DestroyImmediate(rig);

        if(!HoVrcConstraintBridge.IsSdkAvailable){Debug.Log("VRC_API_TEST_SKIPPED_NO_SDK_OR_SHIM");return;}
        var container=new GameObject("VRC structural root");rig=(GameObject)PrefabUtility.InstantiatePrefab(model);rig.transform.SetParent(container.transform,true);
        var hostRoot=new GameObject("Hosts").transform;hostRoot.SetParent(container.transform,false);
        Apply(window,asset,rig,"Vrc",hostRoot);
        int helpers=rig.GetComponentsInChildren<HoImportedConstraintMarker>(true).Count(m=>m.hostRole==ConstraintHostRole.GeneratedHelper);
        Apply(window,asset,rig,"Vrc",hostRoot);
        if(helpers!=rig.GetComponentsInChildren<HoImportedConstraintMarker>(true).Count(m=>m.hostRole==ConstraintHostRole.GeneratedHelper))throw new Exception("VRC reimport leaked helpers");
        var components=hostRoot.GetComponentsInChildren<Component>(true).Where(c=>c!=null&&c.GetType().Name.StartsWith("VRC")&&c.GetType().Name.EndsWith("Constraint")).ToList();
        bool unequal=false,aimDependency=false,partial=false;
        foreach(var c in components)
        {
            var so=new SerializedObject(c);
            if(so.FindProperty("TargetTransform").objectReferenceValue==null||!so.FindProperty("Locked").boolValue||!so.FindProperty("IsActive").boolValue)throw new Exception("VRC target/activation lost");
            var list=so.FindProperty("Sources");
            float w0=list.FindPropertyRelative("source0").FindPropertyRelative("Weight").floatValue;
            if(c.GetType().Name=="VRCParentConstraint"&&Mathf.Abs(w0-.625f)<1e-6f)unequal=true;
            if(c.GetType().Name=="VRCAimConstraint"&&list.FindPropertyRelative("source1").FindPropertyRelative("Weight").floatValue==0)aimDependency=true;
            if(c.GetType().Name=="VRCRotationConstraint"&&so.FindProperty("GlobalWeight").floatValue==.5f)partial=true;
        }
        if(!unequal||!aimDependency||!partial)throw new Exception("VRC weight or Aim dependency mapping lost");
        var keep=components[0].gameObject;new GameObject("User child").transform.SetParent(keep.transform,false);
        var manual=keep.AddComponent(HoVrcConstraintBridge.ResolveType(HoVrcConstraintBridge.RotationConstraintTypeName));
        Apply(window,asset,rig,"Vrc",hostRoot);
        if(keep==null||manual==null||keep.transform.childCount!=1)throw new Exception("VRC cleanup deleted user content");
        var fault=components[0].GetType().BaseType.GetField("ThrowOnZero",BindingFlags.Public|BindingFlags.Static);
        if(fault!=null)
        {
            int previous=container.GetComponentsInChildren<HoImportedConstraintMarker>(true).Length;
            bool rolledBack=false;fault.SetValue(null,true);
            try{Apply(window,asset,rig,"Vrc",hostRoot);}catch(TargetInvocationException){rolledBack=true;}
            finally{fault.SetValue(null,false);}
            if(!rolledBack||previous!=container.GetComponentsInChildren<HoImportedConstraintMarker>(true).Length||keep==null||manual==null)
                throw new Exception("Unexpected SDK failure did not roll back the previous import");
        }
        var overflowHost=new GameObject("Overflow probe");
        var sources=Enumerable.Range(1,20).Select(i=>new ConstraintSource {sourceTransform=rig.transform,weight=i/210f}).ToList();
        var offsets=Enumerable.Range(1,20).Select(i=>Vector3.one*i).ToList();
        var overflow=HoVrcConstraintBridge.AddDirectConstraint(overflowHost,"Parent",rig.transform,sources,offsets,offsets,null);
        var data=new SerializedObject(overflow).FindProperty("Sources");
        if(data.FindPropertyRelative("totalLength").intValue!=20||data.FindPropertyRelative("overflowList").arraySize!=4||Mathf.Abs(data.FindPropertyRelative("overflowList").GetArrayElementAtIndex(3).FindPropertyRelative("Weight").floatValue-20/210f)>1e-7f)throw new Exception("VRC overflow weight serialization lost");
        UnityEngine.Object.DestroyImmediate(overflowHost);UnityEngine.Object.DestroyImmediate(container);
        Debug.Log("VRC_API_STRUCTURAL_OK (shim run is not a real SDK/runtime test)");
    }
}
