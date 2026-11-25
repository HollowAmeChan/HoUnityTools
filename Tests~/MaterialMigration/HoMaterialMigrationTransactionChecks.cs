using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Object = UnityEngine.Object;

// Disposable host only. The fixture creates source and reference assets.
public static class HoMaterialMigrationTransactionChecks
{
    private static int checks;
    public static void Setup() { PlayerSettings.colorSpace = ColorSpace.Linear;Debug.Log("HO_MIGRATION_LINEAR_READY"); }
    public static void Run()
    {
        const string folder = "Assets/HoMigrationTransactionFixture";
        Require(Application.isBatchMode && !AssetDatabase.IsValidFolder(folder), "fresh disposable host");
        string output = Environment.GetEnvironmentVariable("HO_MIGRATION_TEST_OUTPUT");
        Require(!string.IsNullOrEmpty(output), "external test output");Directory.CreateDirectory(output);
        AssetDatabase.CreateFolder("Assets", "HoMigrationTransactionFixture");
        string texturePath=folder+"/SourceBase.asset";
        var sourceTexture=new Texture2D(2,2,TextureFormat.RGBA32,false,true) {name="Source base for HSVG"};
        sourceTexture.SetPixels(new[] {new Color(.2f,.4f,.6f,1),new Color(.7f,.3f,.1f,1),new Color(.1f,.8f,.2f,1),new Color(.6f,.2f,.8f,1)});
        sourceTexture.Apply();AssetDatabase.CreateAsset(sourceTexture,texturePath);AssetDatabase.SaveAssetIfDirty(sourceTexture);
        var source = new Material(Shader.Find("lilToon")) { name = "Original source identity" };
        source.SetTexture("_MainTex",sourceTexture);
        source.SetColor("_Color", new Color(.8f,.6f,.4f,1));source.SetFloat("_Metallic",0);source.SetFloat("_Smoothness",.6f);
        source.SetFloat("_UseSSS",0);source.SetFloat("_UseReflection",1);source.SetFloat("_ApplyReflection",0);
        source.SetVector("_MainTexHSVG",new Vector4(.05f,.9f,.8f,1.2f));
        string materialPath = folder + "/Original.mat";AssetDatabase.CreateAsset(source, materialPath);AssetDatabase.SaveAssetIfDirty(source);
        string guid = AssetDatabase.AssetPathToGUID(materialPath);byte[] metaBefore = File.ReadAllBytes(materialPath+".meta");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("FixtureRoot");var anchor = GameObject.CreatePrimitive(PrimitiveType.Cube);anchor.name="Anchor";anchor.transform.SetParent(root.transform);
        var eye = GameObject.CreatePrimitive(PrimitiveType.Cube);eye.name="Eye";eye.transform.SetParent(root.transform);
        anchor.GetComponent<Renderer>().sharedMaterial=source;eye.GetComponent<Renderer>().sharedMaterial=source;
        string prefabPath=folder+"/Shared.prefab",scenePath=folder+"/Shared.unity",retainScenePath=folder+"/RetainAnchor.unity";
        // New nested fork parents must also disappear on restore/rollback.
        string forkPath=folder+"/Forks/Nested/Eye.mat";
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);Object.DestroyImmediate(root);
        PrefabUtility.InstantiatePrefab(prefab,scene);EditorSceneManager.SaveScene(scene,scenePath);
        scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(prefab,scene);EditorSceneManager.SaveScene(scene,retainScenePath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        string shaderPath=AssetDatabase.GetAssetPath(source.shader);var info=PackageInfo.FindForAssetPath(shaderPath);
        string shaderFile=Path.Combine(info.resolvedPath,shaderPath.Substring(("Packages/"+info.name+"/").Length));
        var row=new HoMaterialMigrationRow {assetPath=materialPath,sourceGuid=guid,sourceLocalId=2100000,sourceSha256=Hash(materialPath),sourceMetaSha256=Hash(materialPath+".meta"),
            sourceShaderGuid=AssetDatabase.AssetPathToGUID(shaderPath),sourceShaderPath=shaderPath,sourceShaderSha256=Hash(shaderFile),targetFamily="Cloth",requiresSlotFork=true};
        var plan=new HoMaterialMigrationPlan {projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),batchId="TransactionFixture",rows=new[]{row},
            slotActions=new[]{Action(prefabPath),Action(scenePath),Action(retainScenePath,"retain-anchor","Cloth")}};
        var originals=new[]{materialPath,materialPath+".meta",texturePath,texturePath+".meta",prefabPath,prefabPath+".meta",scenePath,scenePath+".meta",retainScenePath,retainScenePath+".meta"}.ToDictionary(p=>p,File.ReadAllBytes);
        string planPath=Path.Combine(output,"plan.json"),reportPath=Path.Combine(output,"preview.json");Write(planPath,plan);
        var conflicting=JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));
        conflicting.slotActions[1].targetFamily="Cloth";
        string conflictPlan=Path.Combine(output,"conflicting-fork-plan.json");Write(conflictPlan,conflicting);
        Reject(()=>HoMaterialMigration.Preview(conflictPlan,folder+"/ConflictingPreview",Path.Combine(output,"conflicting-preview.json")),"one fork path cannot represent Eye and Cloth");
        Require(!AssetDatabase.IsValidFolder(folder+"/ConflictingPreview"),"ambiguous fork rejected before preview asset creation");
        var fileAncestor=JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));
        foreach(var action in fileAncestor.slotActions.Where(a=>a.kind=="assign-fork"))action.targetAssetPath=materialPath+"/Eye.mat";
        string ancestorPlan=Path.Combine(output,"file-ancestor-plan.json");Write(ancestorPlan,fileAncestor);
        var beforeAncestorRejection=SnapshotAssets();
        Reject(()=>HoMaterialMigration.Preview(ancestorPlan,folder+"/FileAncestorPreview",Path.Combine(output,"file-ancestor-preview.json")),
            "an existing material cannot be a fork parent directory");
        Require(File.Exists(materialPath)&&File.Exists(materialPath+".meta")&&originals[materialPath].SequenceEqual(File.ReadAllBytes(materialPath))
            &&metaBefore.SequenceEqual(File.ReadAllBytes(materialPath+".meta")),"invalid fork ancestor never deletes or changes the existing material/meta");
        Require(!AssetDatabase.IsValidFolder(folder+"/FileAncestorPreview")&&!File.Exists(Path.Combine(output,"file-ancestor-preview.json")),
            "file ancestor rejected before preview/report creation");
        RequireSnapshot(beforeAncestorRejection,"file ancestor rejection leaves every asset, directory and meta untouched");
        var report=HoMaterialMigration.Preview(planPath,folder+"/Preview",reportPath);
        Require(report.proposals.Length==2,"anchor and Eye fork previews");
        Require(originals.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"preview leaves all source/reference bytes unchanged");
        foreach(var proposal in report.proposals) Require(proposal.blockers.Length==0,string.Join("; ",proposal.blockers));
        Require(report.proposals.All(p=>p.resources.Any(r=>r.assetPath.EndsWith(".recipe.json",StringComparison.Ordinal))),"persistent source texture actually participates in an HSVG bake for both proposals");
        var beforeApply=SnapshotAssets();
        string backup=Path.Combine(output,"backup");
        Reject(()=>HoMaterialMigration.Apply(planPath,reportPath,backup),"unreviewed batch rejected");Require(!Directory.Exists(backup),"rejection precedes backup/mutation");
        RequireSnapshot(beforeApply,"unreviewed rejection leaves complete Assets unchanged");
        row.reviewed=true;row.acceptFamilyModelChanges=true;foreach(var action in plan.slotActions)action.reviewed=true;Write(planPath,plan);

        // This must fail for stale bake inputs, not for missing review flags.
        sourceTexture.SetPixels(Enumerable.Repeat(new Color(.9f,.1f,.4f,1),4).ToArray());sourceTexture.Apply();
        EditorUtility.SetDirty(sourceTexture);AssetDatabase.SaveAssetIfDirty(sourceTexture);
        Require(!originals[texturePath].SequenceEqual(File.ReadAllBytes(texturePath)),"source texture edit was saved to actual asset bytes");
        var editedTextureState=SnapshotAssets();string staleBackup=Path.Combine(output,"stale-texture-backup");
        Reject(()=>HoMaterialMigration.Apply(planPath,reportPath,staleBackup),"preview rejects changed source texture used by HSVG");
        Require(!Directory.Exists(staleBackup),"stale texture rejection precedes backup creation");
        RequireSnapshot(editedTextureState,"stale texture rejection changes no other asset or source bytes");
        File.WriteAllBytes(texturePath,originals[texturePath]);
        AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        RequireSnapshot(beforeApply,"restored texture returns complete Assets to the reviewed preview baseline");
        HoMaterialMigration.Apply(planPath,reportPath,backup);
        source=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        Require(AssetDatabase.AssetPathToGUID(materialPath)==guid,"original GUID retained");
        Require(source.shader.name=="HoShader/Cloth URP","anchor uses dedicated shader");
        var recipe=HoMigrationTextureBaker.ReadRecipe((Texture2D)source.GetTexture("_BaseMap"));
        Require(recipe!=null&&recipe.outputGuid==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source.GetTexture("_BaseMap"))),"final derived recipe names the copied texture GUID");
        Require(metaBefore.SequenceEqual(File.ReadAllBytes(materialPath+".meta")),"original material meta exact bytes retained");
        Require(AssetDatabase.LoadAssetAtPath<Material>(forkPath).shader.name=="HoShader/Eye URP","fork is a real Eye material");
        Require(AssetDatabase.AssetPathToGUID(forkPath)!=guid,"fork has independent GUID");
        var loaded=PrefabUtility.LoadPrefabContents(prefabPath);
        try { Require(loaded.transform.Find("Anchor").GetComponent<Renderer>().sharedMaterial==source,"anchor prefab slot preserved");
            Require(loaded.transform.Find("Eye").GetComponent<Renderer>().sharedMaterial==AssetDatabase.LoadAssetAtPath<Material>(forkPath),"fork prefab slot reassigned"); }
        finally { PrefabUtility.UnloadPrefabContents(loaded); }
        var preview=EditorSceneManager.OpenPreviewScene(scenePath);
        try { var instance=preview.GetRootGameObjects().Single();Require(instance.transform.Find("Eye").GetComponent<Renderer>().sharedMaterial==AssetDatabase.LoadAssetAtPath<Material>(forkPath),"inherited scene resolves new Eye fork");
            Require(!(PrefabUtility.GetPropertyModifications(instance)??Array.Empty<PropertyModification>()).Any(p=>p.propertyPath.StartsWith("m_Materials",StringComparison.Ordinal)),"inherited assignment does not add redundant scene material override"); }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        preview=EditorSceneManager.OpenPreviewScene(retainScenePath);
        try
        {
            var instance=preview.GetRootGameObjects().Single();var retained=instance.transform.Find("Eye").GetComponent<Renderer>().sharedMaterial;
            Require(retained==source&&retained.shader.name=="HoShader/Cloth URP","retain-anchor scene keeps original GUID/Cloth despite upstream Eye fork");
            Require(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(retained))==guid,"retained scene reference preserves original material GUID");
            Require((PrefabUtility.GetPropertyModifications(instance)??Array.Empty<PropertyModification>()).Any(p=>p.propertyPath.StartsWith("m_Materials",StringComparison.Ordinal)&&p.objectReference==source),"retain-anchor adds the necessary material override");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        Require(!originals[retainScenePath].SequenceEqual(File.ReadAllBytes(retainScenePath)),"necessary retain-anchor override was saved to scene bytes");

        // Verified on disk is not permission to discard later unsaved asset edits.
        // Restore must reject before writing or importing anything, and the edited
        // native objects must retain those edits until the fixture clears them.
        var beforeUnsavedEdits=SnapshotAssets();
        Require(JsonUtility.FromJson<HoMaterialMigration.Journal>(File.ReadAllText(Path.Combine(backup,"journal.json"))).state=="Verified",
            "unsaved-edit tests begin from a Verified transaction");
        RejectUnsavedMaterialRestore(backup,source,beforeUnsavedEdits,"original material");
        RejectUnsavedMaterialRestore(backup,AssetDatabase.LoadAssetAtPath<Material>(forkPath),beforeUnsavedEdits,"fork material");
        var derivedTexture=source.GetTexture("_BaseMap") as Texture2D;
        Require(derivedTexture!=null&&AssetDatabase.GetAssetPath(derivedTexture).StartsWith("Assets/HoMaterialMigration/TransactionFixture/",StringComparison.Ordinal)
            &&!EditorUtility.IsDirty(derivedTexture),"texture protection uses the actual clean migration-owned derived asset");
        FilterMode originalFilter=derivedTexture.filterMode;
        FilterMode editedFilter=originalFilter==FilterMode.Point?FilterMode.Trilinear:FilterMode.Point;
        string journalBeforeUnsavedTexture=File.ReadAllText(Path.Combine(backup,"journal.json"));
        try
        {
            derivedTexture.filterMode=editedFilter;EditorUtility.SetDirty(derivedTexture);
            Require(EditorUtility.IsDirty(derivedTexture)&&derivedTexture.filterMode==editedFilter,"derived texture edit exists only in memory");
            Reject(()=>HoMaterialMigration.Restore(backup),"restore protects an unsaved derived texture");
            Require(derivedTexture!=null&&EditorUtility.IsDirty(derivedTexture)&&derivedTexture.filterMode==editedFilter,
                "rejected restore retains the in-memory derived texture edit and native object");
            RequireSnapshot(beforeUnsavedEdits,"unsaved texture rejection performs no disk restoration or owned deletion");
            Require(File.ReadAllText(Path.Combine(backup,"journal.json"))==journalBeforeUnsavedTexture,"unsaved texture rejection leaves the Verified journal untouched");
        }
        finally
        {
            if(derivedTexture!=null){derivedTexture.filterMode=originalFilter;EditorUtility.ClearDirty(derivedTexture);}
        }
        RequireSnapshot(beforeUnsavedEdits,"clearing only fixture memory edits never overwrites current disk assets");
        byte[] completed=File.ReadAllBytes(materialPath);source.name="Post migration user edit";EditorUtility.SetDirty(source);AssetDatabase.SaveAssetIfDirty(source);
        var userEditedState=SnapshotAssets();
        Reject(()=>HoMaterialMigration.Restore(backup),"restore protects post-migration edit");
        RequireSnapshot(userEditedState,"rejected restore performs no partial restoration");
        File.WriteAllBytes(materialPath,completed);AssetDatabase.ImportAsset(materialPath,ImportAssetOptions.ForceUpdate);
        HoMaterialMigration.Restore(backup);
        Require(originals.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"restore returns exact source/material/reference/meta bytes");
        Require(!File.Exists(forkPath)&&!Directory.Exists("Assets/HoMaterialMigration/TransactionFixture"),"restore removes only owned fork/resources");
        RequireSnapshot(beforeApply,"successful restore leaves no new parent folder, meta, resource or fork anywhere in Assets");
        Require(AssetDatabase.LoadAssetAtPath<Material>(materialPath).shader.name=="lilToon","restored native object uses legacy input shader");
        // A managed exception after an actual material save exercises the rollback, rather than only preflight rejection.
        plan.batchId="FailureFixture";string failurePlan=Path.Combine(output,"failure-plan.json"),failureReport=Path.Combine(output,"failure-preview.json");Write(failurePlan,plan);
        HoMaterialMigration.Preview(failurePlan,folder+"/FailurePreview",failureReport);
        var beforeMaterialFailure=SnapshotAssets();bool reachedMaterialSave=false;
        var hook=typeof(HoMaterialMigration).GetField("FaultInjection",BindingFlags.Static|BindingFlags.NonPublic);
        hook.SetValue(null,(Action<string>)(stage=>
        {
            if(!stage.StartsWith("after-material:",StringComparison.Ordinal))return;
            reachedMaterialSave=!originals[materialPath].SequenceEqual(File.ReadAllBytes(materialPath));
            throw new InvalidOperationException("injected write failure after material save");
        }));
        string failureBackup=Path.Combine(output,"failure-backup");
        try { Reject(()=>HoMaterialMigration.Apply(failurePlan,failureReport,failureBackup),"exception after real write rolls back"); }
        finally { hook.SetValue(null,null); }
        Require(reachedMaterialSave,"failure fixture reached an actual material save");
        Require(originals.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"failure rollback restores all original bytes");
        Require(!Directory.Exists("Assets/HoMaterialMigration/FailureFixture")&&!File.Exists(forkPath),"failure rollback cleans owned assets");
        Require(File.ReadAllText(Path.Combine(failureBackup,"journal.json")).Contains("RolledBack"),"durable journal records rollback");
        RequireSnapshot(beforeMaterialFailure,"material-save rollback restores complete file and directory set");

        // Fail only after prefab + both scene decisions have actually been saved.
        plan.batchId="ReferenceFailureFixture";
        string referencePlan=Path.Combine(output,"reference-failure-plan.json"),referenceReport=Path.Combine(output,"reference-failure-preview.json");
        Write(referencePlan,plan);HoMaterialMigration.Preview(referencePlan,folder+"/ReferenceFailurePreview",referenceReport);
        var beforeReferenceFailure=SnapshotAssets();bool reachedReferences=false,referencesWereWritten=false;
        hook.SetValue(null,(Action<string>)(stage=>
        {
            if(stage!="after-references")return;
            reachedReferences=true;
            referencesWereWritten=File.Exists(forkPath)
                &&!originals[materialPath].SequenceEqual(File.ReadAllBytes(materialPath))
                &&!originals[prefabPath].SequenceEqual(File.ReadAllBytes(prefabPath))
                &&!originals[retainScenePath].SequenceEqual(File.ReadAllBytes(retainScenePath));
            throw new InvalidOperationException("injected failure after references saved");
        }));
        string referenceBackup=Path.Combine(output,"reference-failure-backup");
        try {Reject(()=>HoMaterialMigration.Apply(referencePlan,referenceReport,referenceBackup),"exception after reference saves rolls back");}
        finally {hook.SetValue(null,null);}
        Require(reachedReferences&&referencesWereWritten,"late failure reached real material/fork/prefab/retain-scene writes");
        Require(originals.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"late rollback restores material, texture, prefab, both scenes and every meta byte");
        RequireSnapshot(beforeReferenceFailure,"reference-save rollback leaves no new file, parent directory or meta anywhere in Assets");
        Require(File.ReadAllText(Path.Combine(referenceBackup,"journal.json")).Contains("RolledBack"),"late failure journal records rollback");
        Debug.Log("HO_MATERIAL_MIGRATION_TRANSACTIONS_OK "+checks);

        HoMaterialMigrationSlotAction Action(string path,string kind="assign-fork",string family="Eye")=>new() {kind=kind,containerPath=path,containerGuid=AssetDatabase.AssetPathToGUID(path),containerSha256=Hash(path),containerMetaSha256=Hash(path+".meta"),
            rendererPath=prefab.name+"[0]/Eye[1]",rendererType=typeof(MeshRenderer).FullName,componentIndex=0,slot=0,expectedMaterialGuid=guid,expectedMaterialLocalId=2100000,
            targetSourceGuid=guid,targetFamily=family,targetAssetPath=kind=="retain-anchor"?materialPath:forkPath};
    }
    private sealed class AssetSnapshot
    {
        public Dictionary<string,string> files;
        public string[] directories;
    }
    private static void RejectUnsavedMaterialRestore(string backup,Material material,AssetSnapshot before,string label)
    {
        Require(material!=null&&material.HasProperty("_BaseColor")&&!EditorUtility.IsDirty(material),label+" starts as a clean verified native object");
        Color original=material.GetColor("_BaseColor");
        Color edited=new Color(original.r+.137f,original.g+.271f,original.b+.419f,original.a*.61f);
        string journalBefore=File.ReadAllText(Path.Combine(backup,"journal.json"));
        try
        {
            material.SetColor("_BaseColor",edited);EditorUtility.SetDirty(material);
            Require(EditorUtility.IsDirty(material)&&material.GetColor("_BaseColor")==edited,label+" edit exists only in memory");
            Reject(()=>HoMaterialMigration.Restore(backup),"restore protects an unsaved "+label);
            Require(material!=null&&EditorUtility.IsDirty(material)&&material.GetColor("_BaseColor")==edited,
                "rejected restore retains the in-memory "+label+" edit and native object");
            RequireSnapshot(before,"unsaved "+label+" rejection performs no disk restoration or owned deletion");
            Require(File.ReadAllText(Path.Combine(backup,"journal.json"))==journalBefore,"unsaved "+label+" rejection leaves the Verified journal untouched");
        }
        finally
        {
            if(material!=null){material.SetColor("_BaseColor",original);EditorUtility.ClearDirty(material);}
        }
    }
    private static AssetSnapshot SnapshotAssets()=>new()
    {
        files=Directory.GetFiles("Assets","*",SearchOption.AllDirectories).ToDictionary(p=>p.Replace('\\','/'),Hash,StringComparer.Ordinal),
        directories=Directory.GetDirectories("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal).ToArray()
    };
    private static void RequireSnapshot(AssetSnapshot before,string label)
    {
        var after=SnapshotAssets();
        string[] added=after.files.Keys.Except(before.files.Keys,StringComparer.Ordinal).ToArray();
        string[] removed=before.files.Keys.Except(after.files.Keys,StringComparer.Ordinal).ToArray();
        string[] changed=before.files.Keys.Intersect(after.files.Keys,StringComparer.Ordinal).Where(p=>before.files[p]!=after.files[p]).ToArray();
        string[] newDirectories=after.directories.Except(before.directories,StringComparer.Ordinal).ToArray();
        string[] lostDirectories=before.directories.Except(after.directories,StringComparer.Ordinal).ToArray();
        Require(added.Length==0&&removed.Length==0&&changed.Length==0&&newDirectories.Length==0&&lostDirectories.Length==0,
            label+" added="+string.Join(",",added.Take(8))+" removed="+string.Join(",",removed.Take(8))+" changed="+string.Join(",",changed.Take(8))
            +" newDirs="+string.Join(",",newDirectories.Take(8))+" lostDirs="+string.Join(",",lostDirectories.Take(8)));
    }
    private static void Reject(Action action,string label) { bool rejected=false;try{action();}catch{rejected=true;}Require(rejected,label); }
    private static void Require(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException("Migration transaction: "+label);}
    private static void Write(string path,object data)=>File.WriteAllText(path,JsonUtility.ToJson(data,true));
    private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
