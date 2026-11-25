using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using Object=UnityEngine.Object;

// Disposable host only. The native ModelImporter injects a small rig and a
// non-render component, proving the wrapper remains a connected model variant.
public sealed class HoMigrationWrapperModelFixtureImporter : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if(!assetPath.StartsWith("Assets/ReferenceOwnerFixtureWrapper",StringComparison.Ordinal)||!assetPath.EndsWith("/WrapperRig.obj",StringComparison.Ordinal))return;
        var rig=new GameObject("Rig").transform;rig.SetParent(root.transform,false);
        var bone=new GameObject("Bone").transform;bone.SetParent(rig,false);bone.localPosition=Vector3.up*.25f;
        var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(.2f,.3f,.4f);collider.size=new Vector3(2,3,4);
        root.AddComponent<Animator>().applyRootMotion=true;
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter=renderer.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
            mesh.bindposes=new[]{bone.worldToLocalMatrix*renderer.transform.localToWorldMatrix};
            mesh.boneWeights=Enumerable.Repeat(new BoneWeight {boneIndex0=0,weight0=1},mesh.vertexCount).ToArray();
            var delta=Enumerable.Repeat(new Vector3(.01f,.02f,0),mesh.vertexCount).ToArray();
            mesh.AddBlendShapeFrame("FixtureShape",100,delta,new Vector3[mesh.vertexCount],new Vector3[mesh.vertexCount]);
            var owner=renderer.gameObject;var materials=renderer.sharedMaterials;
            Object.DestroyImmediate(renderer);
            var skinned=owner.AddComponent<SkinnedMeshRenderer>();skinned.sharedMesh=mesh;skinned.sharedMaterials=materials;
            skinned.bones=new[]{bone};skinned.rootBone=rig;skinned.updateWhenOffscreen=true;skinned.SetBlendShapeWeight(0,37);
            if(filter!=null)Object.DestroyImmediate(filter);
        }
    }
}

public static class HoMigrationWrapperChecks
{
    [Serializable] sealed class SavedFixture
    {
        public HoMigrationReferenceOwners.Report report;
        public HoMigrationReferenceOwners.WrapperRecipe[] recipes;
        public string sourcePath,targetPath;
        public string[] originalPaths,originalHashes;
        public string destinationAssetSha256,destinationMetaSha256;
    }
    static int checks;
    static string Folder=>"Assets/ReferenceOwnerFixture"+Environment.GetEnvironmentVariable("HO_REFERENCE_FIXTURE_SUFFIX");
    static string Output=>Environment.GetEnvironmentVariable("HO_MIGRATION_TEST_OUTPUT")??throw new InvalidOperationException("Set external HO_MIGRATION_TEST_OUTPUT.");
    public static void Create()
    {
        Check((Environment.GetEnvironmentVariable("HO_REFERENCE_FIXTURE_SUFFIX")??"").StartsWith("Wrapper",StringComparison.Ordinal),"explicit private wrapper fixture root");
        HoMigrationReferenceOwnerChecks.Run();Directory.CreateDirectory(Output);
        AssetDatabase.ImportAsset(Folder+"/TwoParts.mtl",ImportAssetOptions.ForceSynchronousImport);
        string sourcePath=Folder+"/Original.mat",targetPath=Folder+"/Eye.mat",modelPath=Folder+"/WrapperRig.obj";
        var source=AssetDatabase.LoadAssetAtPath<Material>(sourcePath);var target=AssetDatabase.LoadAssetAtPath<Material>(targetPath);
        File.Copy(Folder+"/TwoParts.obj",modelPath);AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);importer.materialLocation=ModelImporterMaterialLocation.InPrefab;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
        foreach(string name in new[]{"MatA","MatB"})importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),source);importer.SaveAndReimport();
        var root=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);var renderers=root.GetComponentsInChildren<Renderer>(true);
        Check(renderers.Length>=1&&renderers.All(r=>r is SkinnedMeshRenderer)&&renderers.Sum(r=>r.sharedMaterials.Length)==2,"actual native ModelImporter rig fixture imported");
        Check(root.GetComponent<BoxCollider>()!=null&&root.GetComponent<Animator>()!=null,"source non-renderer components exist");
        var row=new HoMaterialMigrationRow {assetPath=sourcePath,sourceGuid=AssetDatabase.AssetPathToGUID(sourcePath),targetFamily="Cloth"};
        string pmxPath=Folder+"/TwoMaterials.pmx";
        var pmxActions=AssetDatabase.LoadAssetAtPath<GameObject>(pmxPath).GetComponentsInChildren<Renderer>(true)
            .Select((r,i)=>Action(pmxPath,r,source,row.sourceGuid,i==0?"Eye":"Cloth",i==0?targetPath:sourcePath)).ToArray();
        var pmxPlan=new HoMaterialMigrationPlan {projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),rows=new[]{row},slotActions=pmxActions};
        var pmxReport=HoMigrationReferenceOwners.Resolve(pmxPlan,true);
        Check(pmxReport.blockers.Length==0&&pmxReport.remaps.Length==1&&pmxReport.wrappers.Length==0,"real PMX per-index source identifiers remain precise and do not invent a wrapper");
        Check(HoMigrationReferenceOwners.PrepareWrappers(pmxReport).Length==0,"PMX exact-remap-only report has no wrapper recipes");
        var actions=renderers.SelectMany(r=>r.sharedMaterials.Select((m,s)=>(renderer:r,slot:s))).Select((value,i)=>Action(modelPath,value.renderer,source,row.sourceGuid,i==0?"Eye":"Cloth",i==0?targetPath:sourcePath,value.slot)).ToArray();
        var plan=new HoMaterialMigrationPlan {projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),rows=new[]{row},slotActions=actions};
        var report=HoMigrationReferenceOwners.Resolve(plan,true);
        Check(report.blockers.Length==0&&report.wrappers.Length==1&&report.remaps.Length==0,"mixed actual model keeps original defaults and requests one wrapper");
        var recipes=HoMigrationReferenceOwners.PrepareWrappers(report);
        Check(recipes.Length==1&&recipes[0].bindings.Length==2&&recipes[0].intentIndices.Length==2,"wrapper recipe retains both pending role and anchor intentions");
        Check(recipes[0].bindings.All(b=>!b.relativeRendererPath.StartsWith(root.name+"[",StringComparison.Ordinal)),"selectors exclude the source root name");
        var snapshot=Snapshot();var saved=new SavedFixture {report=report,recipes=recipes,sourcePath=sourcePath,targetPath=targetPath,
            originalPaths=snapshot.Keys.ToArray(),originalHashes=snapshot.Values.ToArray()};
        HoMigrationReferenceOwners.ValidateWrappersBefore(report,recipes);
        var tampered=JsonUtility.FromJson<HoMigrationReferenceOwners.WrapperRecipe>(JsonUtility.ToJson(recipes[0]));tampered.bindings[0].targetFamily="Skin";
        Reject(()=>HoMigrationReferenceOwners.ValidateWrappersBefore(report,new[]{tampered}),"mutated frozen wrapper family rejected");
        Same(snapshot,"prepare and tamper rejection leave every original file unchanged");
        string destination=recipes[0].destinationPath;
        File.WriteAllText(destination+".meta","fileFormatVersion: 2\nguid: 4d15d201cc2a4f1f9784646f175d5a9d\n");
        try {Reject(()=>HoMigrationReferenceOwners.ValidateWrappersBefore(report,recipes),"orphan destination meta rejected");}
        finally {File.Delete(destination+".meta");}
        bool tracked=false;
        Material Target(string guid,string family){Check(guid==row.sourceGuid,"wrapper target resolver source identity");return family=="Eye"?target:source;}
        HoMigrationReferenceOwners.CreateWrappers(report,recipes,Target,path=>
        {
            Check(path==destination&&!File.Exists(path)&&!File.Exists(path+".meta"),"journal tracks exact destination before prefab/meta write");tracked=true;
        });
        Check(tracked&&File.Exists(destination)&&File.Exists(destination+".meta"),"new prefab and metadata are created only after tracking");
        SameOriginals(saved,"wrapper creation leaves all original asset/meta bytes intact");
        var validation=HoMigrationReferenceOwners.ValidateWrappersAfter(report,recipes,Target);
        Check(validation.blockers.Length==0&&validation.verifiedWrapperCount==1&&validation.verifiedIntentCount==2&&validation.verifiedSubdivisionCount==1,"saved wrapper verifies every requested role including original anchor");
        var wrapper=AssetDatabase.LoadAssetAtPath<GameObject>(destination);
        Check(wrapper.name!=root.name,"renamed saved root is valid without rootName or GlobalObjectId assumptions");
        Check(wrapper.GetComponent<BoxCollider>().center==new Vector3(.2f,.3f,.4f)&&wrapper.GetComponent<Animator>().applyRootMotion,"non-material source component settings survive saving");
        Check(wrapper.GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>r.bones.Length==1&&r.rootBone!=null&&r.sharedMesh.blendShapeCount==1&&r.GetBlendShapeWeight(0)==37),"persistent mesh, bones, rootBone and blendshape state survive saving");
        int extraTracked=0;Reject(()=>HoMigrationReferenceOwners.CreateWrappers(report,recipes,Target,_=>extraTracked++),"existing wrapper path is never overwritten");
        Check(extraTracked==0,"existing path rejected before journal ownership callback");
        saved.destinationAssetSha256=Hash(destination);saved.destinationMetaSha256=Hash(destination+".meta");
        File.WriteAllText(Output+"/wrapper-fixture.json",JsonUtility.ToJson(saved,true));
        Debug.Log("HO_WRAPPER_CREATE_OK checks="+checks);
    }
    public static void ReloadAndRestore()
    {
        var saved=JsonUtility.FromJson<SavedFixture>(File.ReadAllText(Output+"/wrapper-fixture.json"));
        var source=AssetDatabase.LoadAssetAtPath<Material>(saved.sourcePath);var target=AssetDatabase.LoadAssetAtPath<Material>(saved.targetPath);
        Material Target(string guid,string family)=>family=="Eye"?target:source;
        var validation=HoMigrationReferenceOwners.ValidateWrappersAfter(saved.report,saved.recipes,Target);
        Check(validation.blockers.Length==0&&validation.verifiedWrapperCount==1&&validation.verifiedIntentCount==2&&validation.verifiedSubdivisionCount==1,"new process reload verifies frozen recipes and all role slots");
        string destination=saved.recipes.Single().destinationPath;
        Check(Hash(destination)==saved.destinationAssetSha256&&Hash(destination+".meta")==saved.destinationMetaSha256,"saved wrapper files are stable across Editor processes");
        byte[] prefabBytes=File.ReadAllBytes(destination);
        var contents=PrefabUtility.LoadPrefabContents(destination);
        try {contents.GetComponent<BoxCollider>().center+=Vector3.one;PrefabUtility.SaveAsPrefabAsset(contents,destination);}
        finally {PrefabUtility.UnloadPrefabContents(contents);}
        Check(HoMigrationReferenceOwners.ValidateWrappersAfter(saved.report,saved.recipes,Target).blockers.Length>0,"unexpected non-material component override is detected after reload");
        File.WriteAllBytes(destination,prefabBytes);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        Check(HoMigrationReferenceOwners.ValidateWrappersAfter(saved.report,saved.recipes,Target).blockers.Length==0,"exact wrapper restoration recovers role and structure verification");
        Check(AssetDatabase.DeleteAsset(destination)&&!File.Exists(destination)&&!File.Exists(destination+".meta"),"caller-owned rollback removes only tracked wrapper and its meta");
        SameOriginals(saved,"cross-process wrapper removal leaves all original asset/meta bytes intact");
        var expected=saved.originalPaths.OrderBy(p=>p,StringComparer.Ordinal);var actual=Snapshot().Keys.OrderBy(p=>p,StringComparer.Ordinal);
        Check(expected.SequenceEqual(actual),"cross-process restore leaves no new file or meta");
        Debug.Log("HO_WRAPPER_RELOAD_RESTORE_OK checks="+checks);
    }
    static HoMaterialMigrationSlotAction Action(string path,Renderer renderer,Material expected,string sourceGuid,string family,string target,int slot=0)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(expected,out string guid,out long id);
        return new HoMaterialMigrationSlotAction {kind="model-remap-review",containerPath=path,containerGuid=AssetDatabase.AssetPathToGUID(path),containerSha256=Hash(path),containerMetaSha256=Hash(path+".meta"),
            rendererPath=Hierarchy(renderer.transform),rendererType=renderer.GetType().FullName,componentIndex=Array.IndexOf(renderer.GetComponents<Renderer>(),renderer),slot=slot,
            expectedMaterialGuid=guid,expectedMaterialLocalId=id,targetSourceGuid=sourceGuid,targetFamily=family,targetAssetPath=target};
    }
    static string Hierarchy(Transform value){var names=new List<string>();for(;value!=null;value=value.parent)names.Add(value.name+"["+value.GetSiblingIndex()+"]");names.Reverse();return string.Join("/",names);}
    static Dictionary<string,string> Snapshot()=>Directory.GetFiles("Assets","*",SearchOption.AllDirectories).ToDictionary(p=>p.Replace('\\','/'),Hash,StringComparer.Ordinal);
    static void Same(Dictionary<string,string> before,string label){var after=Snapshot();Check(before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out string hash)&&hash==p.Value),label);}
    static void SameOriginals(SavedFixture saved,string label)=>Check(saved.originalPaths.Select((p,i)=>File.Exists(p)&&Hash(p)==saved.originalHashes[i]).All(x=>x),label);
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch(Exception e){Debug.Log("EXPECTED_WRAPPER_REJECTION "+label+": "+e.Message);rejected=true;}Check(rejected,label);}
    static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Wrapper checks: "+label);}
    static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
