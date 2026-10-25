using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.Armatures;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

public static class V3HoMergeSetup
{
    public static void Prepare(Func<TextAsset,GameObject,Transform,int> import,bool useUserOutfit=false)
    {
        var asset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/full_ir.json");
        if(asset==null)return;
        var outfitAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/FullOutfit.fbx");
        if(useUserOutfit&&outfitAsset==null)return;
        if(useUserOutfit)
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath("Assets/Fixture/FullOutfit.fbx");importer.animationType=ModelImporterAnimationType.Generic;importer.optimizeGameObjects=false;importer.importAnimation=false;importer.SaveAndReimport();
        }
        var config=JsonUtility.FromJson<ConstraintConfig>(asset.text);
        var root=new GameObject(useUserOutfit?"WTD Miko into main Swimsuit parity":"Real WTD merge parity");var probe=root.AddComponent<V3HoMergeProbe>();probe.routes=new V3HoMergeRoute[2];
        for(int route=0;route<2;route++)
        {
            var character=new GameObject(route==0?"Reference":"Merged").transform;character.SetParent(root.transform,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/FullModel.fbx"));model.transform.SetParent(character,false);
            var animator=model.GetComponent<Animator>();if(animator!=null)UnityEngine.Object.DestroyImmediate(animator);
            var map=model.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
            Transform skeleton=map[config.bindingContext.bones[0].boneName].parent;
            while(config.bindingContext.bones.Any(b=>map.ContainsKey(b.boneName)&&!map[b.boneName].IsChildOf(skeleton)))skeleton=skeleton.parent;
            var host=new GameObject("External HoAux").transform;host.SetParent(character,false);
            if(import(asset,model,host)!=110)throw new Exception("Full WTD import count mismatch");
            var ho=host.GetComponent<HoAuxRig>();ho.Mode=HoAuxRig.UpdateMode.Manual;
            var r=new V3HoMergeRoute{rig=ho,root=model.transform};
            r.inputs=new[]{"hips","chest","breast_L","breast_R"}.Select(n=>map[n]).ToArray();
            r.inputRotations=r.inputs.Select(t=>t.localRotation).ToArray();
            r.owners=ho.Layers.SelectMany(l=>l.operations).Select(op=>op.owner).Concat(ho.DirectOperations.Select(op=>op.owner)).Distinct().OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
            if(route==1)
            {
                Transform sourceRoot=skeleton,body;
                if(useUserOutfit)
                {
                    var outfit=(GameObject)PrefabUtility.InstantiatePrefab(outfitAsset);outfit.transform.SetParent(character,false);
                    var otherAnimator=outfit.GetComponent<Animator>();if(otherAnimator!=null)UnityEngine.Object.DestroyImmediate(otherAnimator);
                    sourceRoot=outfit.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Armature-Miko");body=skeleton;
                }
                else
                {
                    var names=new HashSet<string>(config.mchBindings.Select(b=>b.sourceBone));
                    foreach(var entry in config.knownConstraints.Where(k=>k.constraint.constraintType=="ARMATURE"))
                        foreach(var source in entry.constraint.references.targets)names.Add(source.properties.subtarget);
                    body=CloneBody(skeleton,character,names,true);r.root=body;
                }
                var marker=sourceRoot.gameObject.AddComponent<HoCombineArmature>();marker.targetArmature=body;marker.characterRoot=root.transform;marker.processAutomatically=true;
            }
            r.rootPosition=r.root.position;r.rootRotation=r.root.rotation;r.rootScale=r.root.localScale;probe.routes[route]=r;
        }
    }
    private static Transform CloneBody(Transform original,Transform parent,HashSet<string> names,bool isRoot=false)
    {
        if(!isRoot&&!original.GetComponentsInChildren<Transform>(true).Any(t=>names.Contains(t.name)))return null;
        var copy=new GameObject(isRoot?"Body":original.name).transform;copy.SetParent(parent,false);
        copy.localPosition=original.localPosition;copy.localRotation=original.localRotation;copy.localScale=original.localScale;
        if(isRoot){copy.position=original.position;copy.rotation=original.rotation;copy.localScale=original.lossyScale;}
        foreach(Transform child in original)CloneBody(child,copy,names);
        return copy;
    }
}
