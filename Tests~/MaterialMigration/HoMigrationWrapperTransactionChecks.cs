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
using Object=UnityEngine.Object;

// Disposable stock-URP host only. No production model or legacy package is used.
// Two separate Editor processes exercise the actual durable transaction journal.
public static class HoMigrationWrapperTransactionChecks
{
    [Serializable] sealed class Snapshot {public string[] paths,hashes,directories;}
    [Serializable] sealed class Fixture
    {
        public string materialPath,materialGuid,forkPath,modelPath,modelSha256,modelMetaSha256,scenePath,sceneSha256,wrapperPath,backupPath;
        public Snapshot beforeApply;
        public HoMaterialMigration.PreviewReport preview;
    }
    static int checks;
    static string Output=>Environment.GetEnvironmentVariable("HO_MIGRATION_TEST_OUTPUT")??throw new InvalidOperationException("Set HO_MIGRATION_TEST_OUTPUT.");
    static readonly FieldInfo Hook=typeof(HoMaterialMigration).GetField("FaultInjection",BindingFlags.Static|BindingFlags.NonPublic);

    public static void Apply()
    {
        Check(!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p=>p.name=="jp.lilxyzw.liltoon"),"synthetic source fixture requires a separate host without the legacy package");
        string suffix=Environment.GetEnvironmentVariable("HO_REFERENCE_FIXTURE_SUFFIX")??"";
        Check(System.Text.RegularExpressions.Regex.IsMatch(suffix,"^[A-Za-z0-9_-]+$"),"unique explicit private fixture suffix");
        string folder="Assets/WrapperTransactionFixture"+suffix;
        Check(Application.isBatchMode&&!AssetDatabase.IsValidFolder(folder),"fresh private host folder");
        Directory.CreateDirectory(Output);AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        string shaderPath=folder+"/SyntheticLegacy.shader";
        File.WriteAllText(shaderPath,"Shader \"lilToon\" { Properties { _Color(\"Color\",Color)=(1,1,1,1) _MainTex(\"Base\",2D)=\"white\"{} _MainTexHSVG(\"HSVG\",Vector)=(0,1,1,1) _Metallic(\"Metal\",Float)=0 _Smoothness(\"Smooth\",Float)=0.5 _UseReflection(\"Reflection\",Float)=1 _ApplyReflection(\"Environment\",Float)=0 _SpecularStrength(\"Specular\",Float)=1 _Reflectance(\"F0\",Float)=0.04 } SubShader { Pass {} } }");
        AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport);
        string materialPath=folder+"/Original.mat",forkPath=folder+"/Forks/Nested/Eye.mat";
        var source=new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));AssetDatabase.CreateAsset(source,materialPath);AssetDatabase.SaveAssetIfDirty(source);
        string modelPath=folder+"/Shared.obj",mtlPath=folder+"/Shared.mtl";
        File.WriteAllText(mtlPath,"newmtl MatA\nKd 0.8 0.4 0.2\nnewmtl MatB\nKd 0.2 0.4 0.8\n");
        File.WriteAllText(modelPath,"mtllib Shared.mtl\no PartA\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl MatA\nf 1 2 3\no PartB\nv 2 0 0\nv 3 0 0\nv 2 1 0\nusemtl MatB\nf 4 5 6\n");
        AssetDatabase.ImportAsset(mtlPath,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);importer.materialLocation=ModelImporterMaterialLocation.InPrefab;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);foreach(string name in new[]{"MatA","MatB"})importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),source);importer.SaveAndReimport();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var slots=model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials.Select((m,s)=>(renderer:r,material:m,slot:s))).ToArray();
        Check(slots.Length==2&&slots.All(s=>s.material==source),"real imported aliases share one external source material");
        string scenePath=folder+"/Existing.unity";var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(model,scene);EditorSceneManager.SaveScene(scene,scenePath);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var row=new HoMaterialMigrationRow {assetPath=materialPath,sourceGuid=AssetDatabase.AssetPathToGUID(materialPath),sourceLocalId=2100000,
            sourceSha256=Hash(materialPath),sourceMetaSha256=Hash(materialPath+".meta"),sourceShaderPath=shaderPath,sourceShaderGuid=AssetDatabase.AssetPathToGUID(shaderPath),
            sourceShaderSha256=Hash(shaderPath),targetFamily="Cloth",requiresSlotFork=true};
        var plan=new HoMaterialMigrationPlan {projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..")),batchId="WrapperTransaction"+suffix,
            resolveReferenceOwners=true,retainMixedModelAnchors=true,createModelWrappers=true,rows=new[]{row},
            slotActions=slots.Select((s,i)=>new HoMaterialMigrationSlotAction {kind="model-remap-review",containerPath=modelPath,
                containerGuid=AssetDatabase.AssetPathToGUID(modelPath),containerSha256=Hash(modelPath),containerMetaSha256=Hash(modelPath+".meta"),
                rendererPath=Hierarchy(s.renderer.transform),rendererType=s.renderer.GetType().FullName,componentIndex=Array.IndexOf(s.renderer.GetComponents<Renderer>(),s.renderer),slot=s.slot,
                expectedMaterialGuid=row.sourceGuid,expectedMaterialLocalId=2100000,targetSourceGuid=row.sourceGuid,targetFamily=i==0?"Eye":"Cloth",targetAssetPath=i==0?forkPath:materialPath}).ToArray()};
        string planPath=Output+"/plan.json",reportPath=Output+"/preview.json";Write(planPath,plan);
        var sourceBaseline=TakeSnapshot();var preview=HoMaterialMigration.Preview(planPath,folder+"/Preview",reportPath);
        Check(sourceBaseline.paths.Select((p,i)=>File.Exists(p)&&Hash(p)==sourceBaseline.hashes[i]).All(x=>x),"Preview never changes original files");
        Check(preview.referenceOwners.slotOwners.Length==0&&preview.referenceOwners.remaps.Length==0&&preview.wrapperRecipes.Length==1,"native Preview freezes additive wrapper without writable model slots or importer remaps");
        Check(preview.wrapperRecipes[0].bindings.Length==2&&preview.proposals.Length==2,"model-only pending fork and anchor are concrete preview proposals");
        Check(preview.proposals.All(p=>p.blockers.Length==0),"fixture proposal has no shader conversion blockers: "+string.Join(";",preview.proposals.SelectMany(p=>p.blockers)));
        row.reviewed=true;row.acceptFamilyModelChanges=true;foreach(var action in plan.slotActions)action.reviewed=true;Write(planPath,plan);
        string wrapperPath=preview.wrapperRecipes.Single().destinationPath;
        var baseline=TakeSnapshot();

        File.WriteAllText(wrapperPath+".meta","fileFormatVersion: 2\nguid: 740fcb02fe78429583bd9c626b072e6e\n");
        try {RejectBeforeBackup(planPath,reportPath,Output+"/orphan-meta-backup","orphan wrapper metadata",()=>Check(File.Exists(wrapperPath+".meta"),"rejection preserves existing orphan meta"));}
        finally {File.Delete(wrapperPath+".meta");}
        Same(baseline,"orphan meta rejection creates no other file or directory");
        var occupied=new GameObject("Existing user prefab");PrefabUtility.SaveAsPrefabAsset(occupied,wrapperPath);Object.DestroyImmediate(occupied);
        var occupiedState=TakeSnapshot();
        RejectBeforeBackup(planPath,reportPath,Output+"/occupied-wrapper-backup","existing wrapper destination",()=>Same(occupiedState,"occupied prefab and every other file remain untouched"));
        Check(AssetDatabase.DeleteAsset(wrapperPath),"fixture removes only its temporary occupied destination");Same(baseline,"occupied destination cleanup returns exact baseline");
        importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);float originalScale=importer.globalScale;
        try {
            importer.globalScale=originalScale+.375f;EditorUtility.SetDirty(importer);
            RejectBeforeBackup(planPath,reportPath,Output+"/dirty-model-backup","unsaved original model importer",()=>Check(importer!=null&&EditorUtility.IsDirty(importer)&&importer.globalScale==originalScale+.375f,"dirty model rejection retains unsaved native scalar"),"dirty|unsaved");
            Same(baseline,"dirty model rejection creates no derived asset or backup parent");
        }
        finally {importer.globalScale=originalScale;EditorUtility.ClearDirty(importer);}

        var failurePlan=JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));failurePlan.batchId="WrapperFailure"+suffix;
        string failedPlanPath=Output+"/failure-plan.json",failedReportPath=Output+"/failure-preview.json",failedBackup=Output+"/failure-backup";Write(failedPlanPath,failurePlan);
        var failurePreview=HoMaterialMigration.Preview(failedPlanPath,folder+"/FailurePreview",failedReportPath);baseline=TakeSnapshot();bool reached=false;
        Hook.SetValue(null,(Action<string>)(stage=>
        {
            if(stage!="after-model-wrappers")return;
            var wrapper=AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);var fork=AssetDatabase.LoadAssetAtPath<Material>(forkPath);
            reached=wrapper!=null&&fork!=null&&fork.shader.name=="HoShader/Eye URP"&&wrapper.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Contains(fork));
            Check(AssetDatabase.LoadAssetAtPath<Material>(materialPath).shader.name=="HoShader/Cloth URP","fault hook reached an actual anchor material save");
            throw new InvalidOperationException("injected failure after actual model wrapper save");
        }));
        try {Reject(()=>HoMaterialMigration.Apply(failedPlanPath,failedReportPath,failedBackup),"late wrapper failure must roll back");}
        finally {Hook.SetValue(null,null);}
        Check(reached&&ReadJournal(failedBackup).state=="RolledBack","failure reached saved wrapper/fork and durable rollback");
        Same(baseline,"late wrapper rollback restores all files, metas and ancestor directories");
        Check(Hash(modelPath)==plan.slotActions[0].containerSha256&&Hash(modelPath+".meta")==plan.slotActions[0].containerMetaSha256,"late rollback leaves original importer defaults byte exact");

        string backup=Output+"/apply-backup";HoMaterialMigration.Apply(planPath,reportPath,backup);
        var journal=ReadJournal(backup);var finalFork=AssetDatabase.LoadAssetAtPath<Material>(forkPath);var finalSource=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        Check(journal.state=="Verified"&&finalFork!=null&&finalFork.shader.name=="HoShader/Eye URP","model-only pending family creates a real verified dedicated fork");
        Check(finalSource.shader.name=="HoShader/Cloth URP"&&AssetDatabase.AssetPathToGUID(materialPath)==row.sourceGuid,"anchor conversion retains original GUID");
        Check(journal.createdAssetPaths.Contains(wrapperPath)&&journal.created.Any(c=>c.path==wrapperPath)&&journal.created.Any(c=>c.path==wrapperPath+".meta"),"durable journal owns wrapper asset and metadata");
        Check(journal.createdAssetPaths.Contains(folder+"/Forks")&&journal.createdAssetPaths.Contains(folder+"/Forks/Nested"),"durable journal owns all new fork ancestor directories");
        Check(journal.readOnlyDependencies.Any(d=>d.path==modelPath)&&journal.readOnlyDependencies.Any(d=>d.path==modelPath+".meta")&&!journal.originals.Any(o=>o.path.StartsWith(modelPath,StringComparison.Ordinal)),"original model and importer meta remain guarded read-only dependencies");
        Material Target(string guid,string family)=>family=="Eye"?finalFork:finalSource;
        var verified=HoMigrationReferenceOwners.ValidateWrappersAfter(preview.referenceOwners,preview.wrapperRecipes,Target);
        Check(verified.blockers.Length==0&&verified.verifiedWrapperCount==1&&verified.verifiedIntentCount==2&&verified.verifiedSubdivisionCount==1,"applied wrapper verifies every model-only requested family and anchor");
        var fixture=new Fixture {materialPath=materialPath,materialGuid=row.sourceGuid,forkPath=forkPath,modelPath=modelPath,
            modelSha256=plan.slotActions[0].containerSha256,modelMetaSha256=plan.slotActions[0].containerMetaSha256,
            scenePath=scenePath,sceneSha256=sourceBaseline.hashes[Array.IndexOf(sourceBaseline.paths,scenePath)],wrapperPath=wrapperPath,backupPath=backup,beforeApply=baseline,preview=preview};
        ExistingScene(fixture,finalSource);
        Check(Hash(modelPath)==fixture.modelSha256&&Hash(modelPath+".meta")==fixture.modelMetaSha256,"Apply never rewrites original model or its importer default metadata");
        RejectUnsavedImporterRestore(backup,wrapperPath,"new wrapper PrefabImporter");
        RejectUnsavedImporterRestore(backup,forkPath,"new fork NativeFormatImporter");
        Write(Output+"/transaction-fixture.json",fixture);Debug.Log("HO_WRAPPER_TRANSACTION_APPLY_OK checks="+checks);
    }

    public static void Restore()
    {
        var fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(Output+"/transaction-fixture.json"));
        Check(ReadJournal(fixture.backupPath).state=="Verified","new Editor process loads a Verified journal");
        var source=AssetDatabase.LoadAssetAtPath<Material>(fixture.materialPath);var fork=AssetDatabase.LoadAssetAtPath<Material>(fixture.forkPath);
        Material Target(string guid,string family)=>family=="Eye"?fork:source;
        var verified=HoMigrationReferenceOwners.ValidateWrappersAfter(fixture.preview.referenceOwners,fixture.preview.wrapperRecipes,Target);
        Check(verified.blockers.Length==0&&verified.verifiedIntentCount==2&&verified.verifiedSubdivisionCount==1,"fresh process verifies saved wrapper family bindings");
        ExistingScene(fixture,source);
        HoMaterialMigration.Restore(fixture.backupPath);
        Check(ReadJournal(fixture.backupPath).state=="Restored","main Restore persists terminal journal state");
        Same(fixture.beforeApply,"cross-process main Restore recovers complete original file/directory/meta set");
        Check(!File.Exists(fixture.wrapperPath)&&!File.Exists(fixture.wrapperPath+".meta")&&!File.Exists(fixture.forkPath),"main Restore removes wrapper, metadata and model-only family fork");
        source=AssetDatabase.LoadAssetAtPath<Material>(fixture.materialPath);
        Check(source.shader.name=="lilToon"&&AssetDatabase.AssetPathToGUID(fixture.materialPath)==fixture.materialGuid,"native original material and GUID restored after process restart");
        Check(Hash(fixture.modelPath)==fixture.modelSha256&&Hash(fixture.modelPath+".meta")==fixture.modelMetaSha256,"original model and importer metadata unchanged through full transaction");
        ExistingScene(fixture,source);Debug.Log("HO_WRAPPER_TRANSACTION_RESTORE_OK checks="+checks);
    }
    static void ExistingScene(Fixture fixture,Material original)
    {
        Check(Hash(fixture.scenePath)==fixture.sceneSha256,"existing scene bytes are never rewritten for wrapper reuse");
        var scene=EditorSceneManager.OpenPreviewScene(fixture.scenePath);
        try {var root=scene.GetRootGameObjects().Single();Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)==fixture.modelPath,"existing scene still references original model, not new wrapper");Check(root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).All(m=>m==original),"existing scene inherits original-GUID default anchor slots");}
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void RejectUnsavedImporterRestore(string backup,string path,string label)
    {
        var importer=AssetImporter.GetAtPath(path);Check(importer!=null&&!EditorUtility.IsDirty(importer),label+" starts as a clean native importer");
        string original=importer.userData,journal=File.ReadAllText(backup+"/journal.json");var before=TakeSnapshot();
        try
        {
            importer.userData="pending fixture userData for "+label;EditorUtility.SetDirty(importer);
            Check(EditorUtility.IsDirty(importer),label+" has an actual unsaved native setting");
            Reject(()=>HoMaterialMigration.Restore(backup),"Restore protects unsaved "+label,"unsaved importer");
            Check(importer!=null&&importer==AssetImporter.GetAtPath(path)&&EditorUtility.IsDirty(importer)&&importer.userData=="pending fixture userData for "+label,
                "rejected Restore preserves the same native "+label+" object and memory edit");
            Same(before,label+" rejection performs no partial restoration or output deletion");
            Check(File.ReadAllText(backup+"/journal.json")==journal,label+" rejection leaves Verified journal intact");
        }
        finally {if(importer!=null){importer.userData=original;EditorUtility.ClearDirty(importer);}}
    }
    static void RejectBeforeBackup(string plan,string report,string backup,string label,Action validate,string reasonPattern=null){Reject(()=>HoMaterialMigration.Apply(plan,report,backup),label,reasonPattern);Check(!Directory.Exists(backup),label+" rejected before backup creation");validate();}
    static HoMaterialMigration.Journal ReadJournal(string backup)=>JsonUtility.FromJson<HoMaterialMigration.Journal>(File.ReadAllText(backup+"/journal.json"));
    static Snapshot TakeSnapshot()
    {
        string[] paths=Directory.GetFiles("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        return new Snapshot {paths=paths,hashes=paths.Select(Hash).ToArray(),directories=Directory.GetDirectories("Assets","*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal).ToArray()};
    }
    static void Same(Snapshot before,string label){var after=TakeSnapshot();Check(before.paths.SequenceEqual(after.paths)&&before.hashes.SequenceEqual(after.hashes)&&before.directories.SequenceEqual(after.directories),label+"; changed="+string.Join(",",before.paths.Intersect(after.paths).Where(p=>before.hashes[Array.IndexOf(before.paths,p)]!=after.hashes[Array.IndexOf(after.paths,p)]).Take(6))+"; added="+string.Join(",",after.paths.Except(before.paths).Take(6)));}
    static string Hierarchy(Transform value){var names=new List<string>();for(;value!=null;value=value.parent)names.Add(value.name+"["+value.GetSiblingIndex()+"]");names.Reverse();return string.Join("/",names);}
    static void Reject(Action action,string label,string reasonPattern=null){bool rejected=false;try{action();}catch(Exception e){Debug.Log("EXPECTED_WRAPPER_TRANSACTION_REJECTION "+label+": "+e.Message);Check(reasonPattern==null||System.Text.RegularExpressions.Regex.IsMatch(e.Message,reasonPattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase),"rejection reason matches "+label);rejected=true;}Check(rejected,label);}
    static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Wrapper transaction: "+label);}
    static void Write(string path,object value)=>File.WriteAllText(path,JsonUtility.ToJson(value,true));
    static string Hash(string path){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
