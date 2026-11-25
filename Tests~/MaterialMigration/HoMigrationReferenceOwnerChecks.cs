using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using Hollow.HoUnityTools.Editor.Pmx;
using Object=UnityEngine.Object;

// Private host only. These tests exercise persistent IDs, real importer remaps,
// exact metadata rollback and native prefab inheritance; no rendering is claimed.
public static class HoMigrationReferenceOwnerChecks
{
    static string Folder
    {
        get
        {
            string suffix=Environment.GetEnvironmentVariable("HO_REFERENCE_FIXTURE_SUFFIX")??"";
            if(!System.Text.RegularExpressions.Regex.IsMatch(suffix,"^[A-Za-z0-9_-]*$"))throw new InvalidOperationException("Invalid private fixture suffix.");
            return "Assets/ReferenceOwnerFixture"+suffix;
        }
    }
    static int checks;
    public static void Run()
    {
        Check(Application.isBatchMode&&!AssetDatabase.IsValidFolder(Folder),"fresh private host");
        AssetDatabase.CreateFolder("Assets",Path.GetFileName(Folder));
        var original=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var target=new Material(original);
        string sourcePath=Folder+"/Original.mat",targetPath=Folder+"/Eye.mat";
        AssetDatabase.CreateAsset(original,sourcePath);AssetDatabase.CreateAsset(target,targetPath);AssetDatabase.SaveAssets();
        string sourceGuid=AssetDatabase.AssetPathToGUID(sourcePath);
        var row=new HoMaterialMigrationRow {assetPath=sourcePath,sourceGuid=sourceGuid,sourceLocalId=2100000,targetFamily="Cloth"};
        Material Target(string guid,string family){Check(guid==sourceGuid,"target resolver source identity");return family=="Eye"?target:original;}
        PrefabOwnership(row,original,target,Target);
        ModelRemaps(row,original,target,Target);
        PmxRemaps(row,original,target,Target);
        Debug.Log("HO_REFERENCE_OWNERS_OK checks="+checks);
    }

    static void PrefabOwnership(HoMaterialMigrationRow row,Material original,Material target,Func<string,string,Material> resolve)
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=GameObject.CreatePrimitive(PrimitiveType.Cube);root.name="Shared";root.GetComponent<Renderer>().sharedMaterial=original;
        string prefabPath=Folder+"/Shared.prefab",scenePath=Folder+"/Inherited.unity",retainPath=Folder+"/Retain.unity";
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);Object.DestroyImmediate(root);
        PrefabUtility.InstantiatePrefab(prefab,scene);EditorSceneManager.SaveScene(scene,scenePath);
        scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(prefab,scene);EditorSceneManager.SaveScene(scene,retainPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var plan=Plan(row,Action(prefabPath,"Shared[0]",original,"Eye"),Action(scenePath,"Shared[0]",original,"Eye"),Action(retainPath,"Shared[0]",original,"Cloth"));
        var bytes=new[]{prefabPath,prefabPath+".meta",scenePath,scenePath+".meta",retainPath,retainPath+".meta"}.ToDictionary(p=>p,File.ReadAllBytes);
        var report=HoMigrationReferenceOwners.Resolve(plan,true);
        Check(report.blockers.Length==0,string.Join(";",report.blockers));
        Check(report.intents.Length==3&&report.slotOwners.Length==2,"three expanded intents reduce to two authoring owners");
        Check(report.intents[1].ownerId==report.intents[0].ownerId,"same-target inherited scene folds to prefab owner");
        Check(report.intents[2].ownerId!=report.intents[0].ownerId,"opposite retain-anchor is preserved as scene owner");
        Check(bytes.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"Resolve is byte-read-only");
        HoMigrationReferenceOwners.ValidateBefore(plan,report);
        foreach(var action in plan.slotActions)action.reviewed=true;row.reviewed=true;row.acceptFamilyModelChanges=true;
        HoMigrationReferenceOwners.ValidateBefore(plan,report);
        Check(true,"review flags do not invalidate ownership evidence");
        var edited=JsonUtility.FromJson<HoMigrationReferenceOwners.Report>(JsonUtility.ToJson(report));edited.retainMixedModelAnchors=false;
        Reject(()=>HoMigrationReferenceOwners.ValidateBefore(plan,edited),"owner policy is hash bound");
        foreach(var action in HoMigrationReferenceOwners.ExecutableSlotActions(report).OrderBy(a=>a.containerPath.EndsWith(".prefab")?0:1))SetSlot(action,resolve(action.targetSourceGuid,action.targetFamily));
        var verified=HoMigrationReferenceOwners.ValidateAfter(report,resolve);
        Check(verified.blockers.Length==0&&verified.verifiedIntentCount==3&&verified.declaredModelDefaultExceptions==0,"all expanded intents verified after owner-only writes");
        Inspect(scenePath,r=>Check(!(PrefabUtility.GetPropertyModifications(r.gameObject)??Array.Empty<PropertyModification>()).Any(p=>p.propertyPath.StartsWith("m_Materials",StringComparison.Ordinal)),"inherited scene gains no redundant material override"));
        Inspect(retainPath,r=>Check(r.sharedMaterial==original&&(PrefabUtility.GetPropertyModifications(r.gameObject)??Array.Empty<PropertyModification>()).Any(p=>p.propertyPath.StartsWith("m_Materials",StringComparison.Ordinal)&&p.objectReference==original),"retain-anchor scene has necessary original-GUID override"));
        foreach(var pair in bytes)File.WriteAllBytes(pair.Key,pair.Value);
        foreach(string path in new[]{prefabPath,scenePath,retainPath})AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        HoMigrationReferenceOwners.ValidateBefore(plan,report);
        Check(true,"native ownership survives exact source restoration");
    }

    static void ModelRemaps(HoMaterialMigrationRow row,Material original,Material target,Func<string,string,Material> resolve)
    {
        string model=Folder+"/TwoParts.obj",mtl=Folder+"/TwoParts.mtl";
        File.WriteAllText(mtl,"newmtl MatA\nKd 0.8 0.4 0.2\nnewmtl MatB\nKd 0.2 0.4 0.8\n");
        File.WriteAllText(model,"mtllib TwoParts.mtl\no PartA\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl MatA\nf 1 2 3\no PartB\nv 2 0 0\nv 3 0 0\nv 2 1 0\nusemtl MatB\nf 4 5 6\n");
        AssetDatabase.ImportAsset(model,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(model);importer.materialLocation=ModelImporterMaterialLocation.InPrefab;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        var renderers=AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponentsInChildren<Renderer>(true);
        Check(renderers.Length>=1,"native ModelImporter mesh imported");
        var allSlots=renderers.SelectMany(r=>r.sharedMaterials.Select((m,s)=>(renderer:r,material:m,slot:s))).ToArray();
        var embedded=allSlots.First();
        Identity(embedded.material,out string embeddedGuid,out long embeddedId);
        var ids=HoMigrationReferenceOwners.Identifiers(importer,embeddedGuid,embeddedId);
        Check(ids.Length==1&&!ids[0].hadExplicitRemap&&ids[0].provenance.StartsWith("ModelImporter.sourceMaterials",StringComparison.Ordinal),"unmapped native sourceMaterials exact embedded identifier");
        var actions=allSlots.Where(s=>s.material==embedded.material).Select(s=>Action(model,Hierarchy(s.renderer.transform),s.material,"Eye",s.slot)).ToArray();
        var plan=Plan(row,actions);var report=HoMigrationReferenceOwners.Resolve(plan);
        Check(report.blockers.Length==0&&report.remaps.Length==1,"unmapped native ModelImporter key resolves without instance IDs");
        var before=File.ReadAllBytes(model+".meta");HoMigrationReferenceOwners.ValidateBefore(plan,report);
        HoMigrationReferenceOwners.ApplyRemaps(report,resolve);
        Check(HoMigrationReferenceOwners.ValidateAfter(report,resolve).blockers.Length==0,"real model importer AddRemap changes native slots");
        HoMigrationReferenceOwners.RestoreRemapMetadata(report,_=>before);
        Check(before.SequenceEqual(File.ReadAllBytes(model+".meta")),"absent remap restored by exact original importer metadata");

        // Both exact source identifiers map to one external material. Uniform
        // requests move all aliases together; conflicting roles must not pick one.
        importer=(ModelImporter)AssetImporter.GetAtPath(model);
        var importedMaterials=AssetDatabase.LoadAllAssetsAtPath(model).OfType<Material>().ToArray();
        Check(importedMaterials.Length==2,"two independent source materials for alias test");
        foreach(var material in importedMaterials)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),material.name),original);
        importer.SaveAndReimport();
        allSlots=AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials.Select((m,s)=>(renderer:r,material:m,slot:s))).ToArray();
        actions=allSlots.Select(s=>Action(model,Hierarchy(s.renderer.transform),original,"Eye",s.slot)).ToArray();
        plan=Plan(row,actions);report=HoMigrationReferenceOwners.Resolve(plan);
        Check(report.remaps.Length==2&&report.remaps.All(r=>r.identifier.hadExplicitRemap&&r.identifier.provenance=="GetExternalObjectMap"),"all real external source aliases retained rather than selecting by target GUID");
        before=File.ReadAllBytes(model+".meta");HoMigrationReferenceOwners.ApplyRemaps(report,resolve);
        Check(HoMigrationReferenceOwners.ValidateAfter(report,resolve).blockers.Length==0,"uniform alias remaps verify every expanded model intent");
        HoMigrationReferenceOwners.RestoreRemapMetadata(report,_=>before);
        actions[1].targetFamily="Cloth";actions[1].targetAssetPath=row.assetPath;
        plan=Plan(row,actions);var strict=HoMigrationReferenceOwners.Resolve(plan);
        Check(strict.blockers.Length>0,"mixed alias family model defaults require explicit policy");
        report=HoMigrationReferenceOwners.Resolve(plan,true);
        Check(report.blockers.Length==0&&report.remaps.Length==0&&report.modelDefaults.Length==actions.Length&&report.wrappers.Length==1,"explicit model anchor policy preserves all requested subdivisions as pending wrapper decisions");
        Check(report.wrappers[0].status=="NotCreated","wrapper suggestion is not an implemented asset");
        var anchored=HoMigrationReferenceOwners.ValidateAfter(report,resolve);
        Check(anchored.blockers.Length==0&&anchored.verifiedIntentCount==actions.Length&&anchored.declaredModelDefaultExceptions>0,"anchor verification exposes unsatisfied subdivision count");
    }

    static void PmxRemaps(HoMaterialMigrationRow row,Material original,Material target,Func<string,string,Material> resolve)
    {
        string path=Folder+"/TwoMaterials.pmx";WritePmx(path);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(HoPmxImporter)AssetImporter.GetAtPath(path);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),HoPmxImporter.MaterialIdentifier(0)),original);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),HoPmxImporter.MaterialIdentifier(1)),original);importer.SaveAndReimport();
        var renderers=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true).OrderBy(r=>r.name,StringComparer.Ordinal).ToArray();
        Check(renderers.Length==2,"PMX per-index renderers imported");
        var plan=Plan(row,Action(path,Hierarchy(renderers[0].transform),original,"Eye"),Action(path,Hierarchy(renderers[1].transform),original,"Cloth"));
        var report=HoMigrationReferenceOwners.Resolve(plan);
        Check(report.blockers.Length==0&&report.remaps.Length==1,"PMX report/mesh identity disambiguates shared external material per index");
        Check(report.remaps[0].identifier.sourceName=="PMX_Material_0000"&&report.remaps[0].identifier.pmxMaterialIndex==0&&report.remaps[0].identifier.sourceMeshLocalId!=0,"PMX remap keeps exact typed identifier and persistent mesh evidence");
        byte[] before=File.ReadAllBytes(path+".meta");HoMigrationReferenceOwners.ValidateBefore(plan,report);HoMigrationReferenceOwners.ApplyRemaps(report,resolve);
        Check(HoMigrationReferenceOwners.ValidateAfter(report,resolve).blockers.Length==0,"PMX index0 fork and index1 retained anchor verify independently");
        HoMigrationReferenceOwners.RestoreRemapMetadata(report,_=>before);
        Check(before.SequenceEqual(File.ReadAllBytes(path+".meta")),"PMX importer metadata restored byte for byte");
    }

    static HoMaterialMigrationPlan Plan(HoMaterialMigrationRow row,params HoMaterialMigrationSlotAction[] actions)=>new() {projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),batchId="OwnerChecks",rows=new[]{row},slotActions=actions};
    static HoMaterialMigrationSlotAction Action(string path,string hierarchy,Material expected,string family,int slot=0)
    {
        Identity(expected,out string guid,out long id);
        return new HoMaterialMigrationSlotAction {kind=path.EndsWith(".obj")||path.EndsWith(".pmx")?"model-remap-review":family=="Cloth"?"retain-anchor":"assign-fork",containerPath=path,
            containerGuid=AssetDatabase.AssetPathToGUID(path),containerSha256=Hash(path),containerMetaSha256=Hash(path+".meta"),rendererPath=hierarchy,
            rendererType=typeof(MeshRenderer).FullName,componentIndex=0,slot=slot,expectedMaterialGuid=guid,expectedMaterialLocalId=id,targetSourceGuid=AssetDatabase.AssetPathToGUID(Folder+"/Original.mat"),
            targetFamily=family,targetAssetPath=Folder+(family=="Eye"?"/Eye.mat":"/Original.mat")};
    }
    static void SetSlot(HoMaterialMigrationSlotAction action,Material target)
    {
        if(action.containerPath.EndsWith(".prefab"))
        {var root=PrefabUtility.LoadPrefabContents(action.containerPath);try{var renderer=root.GetComponentsInChildren<Renderer>(true).Single();renderer.sharedMaterial=target;PrefabUtility.SaveAsPrefabAsset(root,action.containerPath);}finally{PrefabUtility.UnloadPrefabContents(root);}return;}
        var scene=EditorSceneManager.OpenScene(action.containerPath,OpenSceneMode.Additive);
        try{var renderer=scene.GetRootGameObjects().Single().GetComponent<Renderer>();renderer.sharedMaterial=target;PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
    static void Inspect(string path,Action<Renderer> visit){var scene=EditorSceneManager.OpenPreviewScene(path);try{visit(scene.GetRootGameObjects().Single().GetComponent<Renderer>());}finally{EditorSceneManager.ClosePreviewScene(scene);}}
    static string Hierarchy(Transform value){var names=new List<string>();for(;value!=null;value=value.parent)names.Add(value.name+"["+value.GetSiblingIndex()+"]");names.Reverse();return string.Join("/",names);}
    static void Identity(Object obj,out string guid,out long id){if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out guid,out id))throw new Exception("Persistent ID required.");}
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label);}
    static void Check(bool value,string label){if(!value)throw new InvalidOperationException("Owner checks: "+label);checks++;}
    static string Hash(string path){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static void WritePmx(string path)
    {
        using var writer=new BinaryWriter(File.Create(path));writer.Write(Encoding.ASCII.GetBytes("PMX "));writer.Write(2f);writer.Write(new byte[]{8,1,0,1,1,1,1,1,1});
        Text("Owner checks");Text("");Text("");Text("");writer.Write(3);
        foreach(var p in new[]{Vector3.zero,Vector3.right,Vector3.up})
        {writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write(0f);writer.Write(0f);writer.Write(1f);writer.Write(p.x);writer.Write(p.y);writer.Write((byte)0);writer.Write((sbyte)-1);writer.Write(1f);}
        writer.Write(6);writer.Write(new byte[]{0,1,2,0,1,2});writer.Write(0);writer.Write(2);
        foreach(string name in new[]{"cloth_a","cloth_b"}){Text(name);Text("");foreach(float v in new[]{1f,1f,1f,1f,.04f,.04f,.04f,32f,.2f,.2f,.2f})writer.Write(v);writer.Write((byte)12);foreach(float v in new[]{0f,0f,0f,1f,0f})writer.Write(v);writer.Write((sbyte)-1);writer.Write((sbyte)-1);writer.Write((byte)0);writer.Write((byte)1);writer.Write((byte)0);Text("");writer.Write(3);}
        for(int i=0;i<5;i++)writer.Write(0);
        void Text(string value){byte[] bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
    }
}
