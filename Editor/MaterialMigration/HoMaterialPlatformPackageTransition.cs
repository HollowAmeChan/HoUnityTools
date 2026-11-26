using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hollow.HoUnityTools.Editor.Pmx;
using PackageInfo=UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // Explicit batch entry points only. Installation does not remove packages,
    // rewrite manifests directly, migrate assets or authorize legacy retirement.
    [InitializeOnLoad]
    public static class HoMaterialPlatformPackageTransition
    {
        const string SessionKey="Hollow.Ho.MaterialPlatformPackageTransition.v1";
        const int TimeoutSeconds=600;
        const string LegacyPackage="jp.lilxyzw.liltoon";
        const string LegacyPrefix="Packages/"+LegacyPackage+"/";
        static readonly string[] ModelExtensions={".fbx",".pmx",".pmd",".obj",".dae",".blend",".3ds",".gltf",".glb"};
        static readonly Target[] Targets=
        {
            new Target {name="com.hoshader.cloth-urp",path="D:/Unity_Fork/HoShader/Cloth_URP"},
            new Target {name="com.hoshader.hair-urp",path="D:/Unity_Fork/HoShader/Hair_URP"},
            new Target {name="com.hoshader.skin-urp-stylized",path="D:/Unity_Fork/HoShader/Skin_URP"},
            new Target {name="com.hoshader.environment-urp",path="D:/Unity_Fork/HoShader/Environment_URP"}
        };
        [Serializable] sealed class Target {public string name,path;}
        [Serializable] sealed class Manifest {public string name;}
        [Serializable] public sealed class PackageSnapshot
        {public string name,version,packageId,resolvedPath,source;public bool direct;}
        [Serializable] public sealed class Report
        {
            public int schemaVersion=1,domainReloadResumptions;
            public string operationId,operation,status,projectRoot,startedUtc,completedUtc,error;
            public string[] requestedPackageNames,requestedArguments;
            public PackageSnapshot[] originalDirectPackages,observedPackages,verifiedTargets;
            public bool untargetedDirectPackagesPreserved,legacyRemovalImplemented;
            public string retirementEvidencePath,retirementEvidenceSha256;
        }
        [Serializable] public sealed class FileStamp {public string path,sha256,metaSha256,reviewedSha256;}
        [Serializable] public sealed class PmxProfile {public string path,guid,metaSha256;public int profile;public bool explicitProfile;}
        [Serializable] public sealed class RetirementEvidence
        {
            public int schemaVersion=1,nativeLegacyMaterials,nativeMissingShaderBaseline,nativeErrors,pmxCount,pmxLegacyProfile0Count,
                inspectedPackageMaterialFiles,inspectedPackageModels,inspectedMaterials,inspectedDependencies,inspectedAssetFiles,allAssetsDependencyPaths;
            public string projectRoot,createdUtc,removePackageName,nativeAuditPath,nativeAuditSha256,manifestSha256,packagesLockSha256,registeredPackagesSha256;
            public string[] knownLegacyShaderGuids,violations,allAssetsLegacyDependencies;
            public FileStamp[] generatorSources,pmxSources,defaultMaterials;
            public PmxProfile[] pmxProfiles;
        }
        [Serializable] sealed class PackageSet {public PackageSnapshot[] packages;}
        // These are the exact creation-entry sources reviewed in this transition.
        // Diagnostic legacy-name tests and lilToon.URP.Extensions are intentionally
        // preserved. No substring ban or caller-provided approval bool is used.
        static readonly string[][] ReviewedGenerators=
        {
            new[]{"Assets/00动画/MMD场景/_制作工具/Editor/HoMmdEnvironmentSurface.cs","277151b676f5d6ae795c897b39ec3a1b5839fb4874cf9cb2da30da0094b7ba88"},
            new[]{"Assets/00动画/MMD场景/_制作工具/Editor/HoMmdStageBuilder.cs","4aaf7063d62b020548c5fbae312490242771d1e3afb7e30d286d35c91f9f7d05"},
            new[]{"Assets/00动画/MMD场景/_制作工具/Editor/HoMmdStageAudit.cs","0eb92df573eca3c8cd5402c7221a265b5a1cfc4cb2b634320b1f54dbeb4201d2"},
            new[]{"Assets/00动画/MMD场景/_制作工具/Editor/HoMmdRTShadowDiagnostics.cs","d2d9bc38627ac5ab32ba7759aebb3ae7a836be2bd48890246f454c215e7d6fa0"},
            new[]{"Assets/00动画/MMD场景/_制作工具/Editor/HoLightingAudit.cs","80c47ec862a9cc14abe9de5136d48a50097d8ab29a1d20c8e7f6043d384bca95"}
        };
        static readonly string[][] ReviewedPmxSources=
        {
            new[]{"Packages/com.hollow.hounitytools/Editor/PmxImport/HoPmxImporter.cs","7a62fe6978bab55592613eba2c3e766e97a446fdaad31f11ecc4b3ba94671032"},
            new[]{"Packages/com.hollow.hounitytools/Editor/PmxImport/HoPmxSpecializedMaterials.cs","437952d9423bfcdbdd69d83dc74d46519c5d96b919d28b127ce3d6ecf39a4999"},
            new[]{"Packages/com.hollow.hounitytools/Editor/PmxImport/HoPmxMaterials.cs","d15a28d04249d848e05e9d40875b93458b8cf22801d69d26887f38ec22d85950"}
        };
        [Serializable] sealed class Pending
        {
            public string operationId,operation,projectRoot,reportPath,phase,startedUtc,lastObservation;
            public long deadlineUtcTicks;
            public int resumptions;
            public string retirementEvidencePath,retirementEvidenceSha256;
            public PackageSnapshot[] before=Array.Empty<PackageSnapshot>(),observed=Array.Empty<PackageSnapshot>();
        }
        static Pending pending;
        static ListRequest list;
        static AddAndRemoveRequest add;
        static RemoveRequest remove;
        static double nextListAt;

        static HoMaterialPlatformPackageTransition()
        {
            if(!string.IsNullOrEmpty(SessionState.GetString(SessionKey,"")))EditorApplication.delayCall+=Resume;
        }
        public static void RunInstallTargets()
        {
            try
            {
                string project=RequireInvocationProject(),report=RequireReportPath(Environment.GetEnvironmentVariable("HO_MATERIAL_PLATFORM_PACKAGE_REPORT"));
                if(!string.IsNullOrEmpty(SessionState.GetString(SessionKey,"")))throw new InvalidOperationException("Another package transition is already pending in this Editor session.");
                pending=new Pending {operationId=Guid.NewGuid().ToString("N"),operation="InstallTargets",projectRoot=project,reportPath=report,phase="InspectBefore",
                    startedUtc=DateTime.UtcNow.ToString("O"),deadlineUtcTicks=DateTime.UtcNow.AddSeconds(TimeoutSeconds).Ticks};
                foreach(var target in Targets)
                {
                    string manifest=Path.Combine(target.path,"package.json");
                    if(!File.Exists(manifest)||JsonUtility.FromJson<Manifest>(File.ReadAllText(manifest))?.name!=target.name)
                        throw new InvalidOperationException("The fixed target package path/name does not match: "+target.name+" at "+target.path);
                }
                Persist();Subscribe();
            }
            catch(Exception error){FinishFailure(error);}
        }
        public static void RunRemoveLegacy()
        {
            try
            {
                string project=RequireInvocationProject(),report=RequireReportPath(Environment.GetEnvironmentVariable("HO_MATERIAL_PLATFORM_PACKAGE_REPORT"));
                if(!string.IsNullOrEmpty(SessionState.GetString(SessionKey,"")))throw new InvalidOperationException("A package transition is already pending.");
                pending=new Pending {operationId=Guid.NewGuid().ToString("N"),operation="RemoveLegacy",projectRoot=project,reportPath=report,phase="InspectBefore",startedUtc=DateTime.UtcNow.ToString("O"),deadlineUtcTicks=DateTime.UtcNow.AddSeconds(TimeoutSeconds).Ticks};
                RequireReportPath(report+".native-audit.json");RequireReportPath(report+".retirement-preflight.json");
                Persist();Subscribe();
            }
            catch(Exception error){FinishFailure(error);}
        }
        static void Resume()
        {
            try
            {
                string raw=SessionState.GetString(SessionKey,"");if(string.IsNullOrEmpty(raw))return;
                pending=JsonUtility.FromJson<Pending>(raw);
                if(pending==null||(pending.operation!="InstallTargets"&&pending.operation!="RemoveLegacy")||pending.projectRoot!=RequireInvocationProject()
                    ||pending.reportPath!=RequireReportPath(Environment.GetEnvironmentVariable("HO_MATERIAL_PLATFORM_PACKAGE_REPORT")))
                    throw new InvalidOperationException("The pending package transition does not match this explicitly selected project/report.");
                pending.resumptions++;
                // A Request object cannot survive a domain reload. Never issue
                // Add twice: the package server may still own the first request.
                // A fresh List must prove actual names and resolved paths instead.
                if(pending.phase=="Adding"||pending.phase=="Removing")pending.phase="VerifyAfter";
                if(pending.operation=="RemoveLegacy"&&pending.phase=="VerifyAfter")RequireRetirementEvidence();
                list=null;add=null;remove=null;nextListAt=0;Persist();Subscribe();
            }
            catch(Exception error){FinishFailure(error);}
        }
        static void Subscribe(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        static void Persist()=>SessionState.SetString(SessionKey,JsonUtility.ToJson(pending));
        static void Tick()
        {
            try
            {
                if(pending==null)return;
                if(pending.projectRoot!=RequireInvocationProject())throw new InvalidOperationException("Project authorization changed during package transition.");
                if(DateTime.UtcNow.Ticks>=pending.deadlineUtcTicks)throw new TimeoutException("Package transition timed out. Last observation: "+pending.lastObservation);
                if(pending.operation=="RemoveLegacy"&&(EditorApplication.isCompiling||EditorApplication.isUpdating))return;
                if(pending.phase=="Adding")
                {
                    if(add==null)throw new InvalidOperationException("Lost Add request outside domain-reload recovery.");
                    if(!add.IsCompleted)return;
                    if(add.Status==StatusCode.Failure)throw new InvalidOperationException("Package AddAndRemove failed: "+add.Error?.message);
                    pending.phase="VerifyAfter";add=null;Persist();return;
                }
                if(pending.phase=="Removing")
                {
                    if(remove==null)throw new InvalidOperationException("Lost Remove request outside domain-reload recovery.");
                    if(!remove.IsCompleted)return;
                    if(remove.Status==StatusCode.Failure)throw new InvalidOperationException("Package Remove failed: "+remove.Error?.message);
                    pending.phase="VerifyAfter";remove=null;Persist();return;
                }
                if(pending.phase!="InspectBefore"&&pending.phase!="VerifyAfter")throw new InvalidOperationException("Unknown package transition phase.");
                if(list==null)
                {
                    if(EditorApplication.timeSinceStartup<nextListAt)return;
                    list=Client.List(true,true);return;
                }
                if(!list.IsCompleted)return;
                if(list.Status==StatusCode.Failure)
                {
                    string error=list.Error?.message??"Unknown List failure";list=null;
                    if(pending.phase=="InspectBefore")throw new InvalidOperationException("Could not inspect installed packages: "+error);
                    pending.lastObservation="List during package/domain transition: "+error;nextListAt=EditorApplication.timeSinceStartup+2;Persist();return;
                }
                var observed=list.Result.Select(Snapshot).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray();list=null;
                if(pending.phase=="InspectBefore")
                {
                    pending.before=observed.Where(p=>p.direct).ToArray();pending.observed=observed;
                    if(pending.operation=="InstallTargets")
                    {
                        pending.phase="Adding";Persist();add=Client.AddAndRemove(Targets.Select(t=>"file:"+t.path).ToArray(),Array.Empty<string>());return;
                    }
                    if(!observed.Any(p=>p.name==LegacyPackage))throw new InvalidOperationException("The legacy package is not installed; inspect its earlier retirement report rather than assuming a clean fresh removal.");
                    var retirement=AuditRetirement();
                    pending.retirementEvidencePath=pending.reportPath+".retirement-preflight.json";
                    WriteNew(pending.retirementEvidencePath,retirement);pending.retirementEvidenceSha256=HashFile(pending.retirementEvidencePath);Persist();
                    if(retirement.violations.Length!=0)throw new InvalidOperationException("Retirement preflight blocked; see "+pending.retirementEvidencePath+": "+string.Join("; ",retirement.violations));
                    RequireSamePreflightInputs(retirement);
                    if(DateTime.UtcNow.Ticks>=pending.deadlineUtcTicks)throw new TimeoutException("Retirement audit exceeded the operation deadline; no package was removed.");
                    pending.phase="Removing";Persist();remove=Client.Remove(LegacyPackage);return;
                }
                pending.observed=observed;
                if(pending.operation=="RemoveLegacy")
                {
                    RequireRetirementEvidence();
                    if(observed.Any(p=>p.name==LegacyPackage)){pending.lastObservation="Legacy package is still registered after Remove/domain reload.";nextListAt=EditorApplication.timeSinceStartup+2;Persist();return;}
                    RequireOtherDirectPackages(observed,true);Finish(true,"");return;
                }
                string[] missing=Targets.Where(t=>!observed.Any(p=>p.name==t.name&&SamePath(p.resolvedPath,t.path))).Select(t=>t.name).ToArray();
                if(missing.Length!=0)
                {
                    pending.lastObservation="Targets not yet resolved at fixed paths: "+string.Join(", ",missing);nextListAt=EditorApplication.timeSinceStartup+2;Persist();return;
                }
                RequireOtherDirectPackages(observed,false);
                Finish(true,"");
            }
            catch(Exception error){FinishFailure(error);}
        }
        static RetirementEvidence AuditRetirement()
        {
            RequireCleanScenes();
            string nativePath=RequireReportPath(pending.reportPath+".native-audit.json");
            var result=new RetirementEvidence {projectRoot=pending.projectRoot,createdUtc=DateTime.UtcNow.ToString("O"),removePackageName=LegacyPackage,
                nativeAuditPath=nativePath,manifestSha256=HashFile(Path.Combine(pending.projectRoot,"Packages/manifest.json")),
                packagesLockSha256=HashFile(Path.Combine(pending.projectRoot,"Packages/packages-lock.json")),registeredPackagesSha256=RegisteredPackagesHash()};
            var violations=new List<string>();
            result.generatorSources=ReviewedGenerators.Select(p=>ReviewedFile(p,violations)).ToArray();
            result.pmxSources=ReviewedPmxSources.Select(p=>ReviewedFile(p,violations)).ToArray();
            string[] allPaths=AssetDatabase.GetAllAssetPaths();
            var legacyGuids=new HashSet<string>(allPaths.Where(p=>RemovedPackagePath(p)&&p.EndsWith(".shader",StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.AssetPathToGUID).Where(g=>!string.IsNullOrEmpty(g)),StringComparer.OrdinalIgnoreCase);
            result.knownLegacyShaderGuids=legacyGuids.OrderBy(g=>g,StringComparer.Ordinal).ToArray();
            if(legacyGuids.Count==0)violations.Add("The currently installed legacy shader GUID set could not be inspected.");
            var native=HoNativeMaterialReferenceAudit.Write(nativePath);result.nativeAuditSha256=HashFile(nativePath);
            // Existing missing shaders are a separate baseline (currently 12).
            // They are not rewritten as legacy or as synthetic native errors.
            result.nativeMissingShaderBaseline=native.materials.Count(m=>m.missingShader);result.nativeErrors=native.errors.Count;
            result.nativeLegacyMaterials=native.materials.Count(m=>m.legacy||LegacyName(m.shaderName)||m.shader!=null&&legacyGuids.Contains(m.shader.guid??""));
            if(result.nativeErrors!=0)violations.Add("Fresh native audit has errors: "+result.nativeErrors);
            if(result.nativeLegacyMaterials!=0)violations.Add("Fresh native audit still contains legacy materials: "+result.nativeLegacyMaterials);
            var pmx=new List<PmxProfile>();
            foreach(string path in allPaths.Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&p.EndsWith(".pmx",StringComparison.OrdinalIgnoreCase)))
            {
                var importer=AssetImporter.GetAtPath(path) as HoPmxImporter;
                if(importer==null){violations.Add("PMX importer cannot be typed/audited: "+path);continue;}
                if(EditorUtility.IsDirty(importer))violations.Add("Unsaved PMX importer: "+path);
                string meta=Physical(path+".meta");
                pmx.Add(new PmxProfile {path=path,guid=AssetDatabase.AssetPathToGUID(path),profile=(int)importer.materialProfile,
                    metaSha256=HashFile(meta),explicitProfile=Regex.IsMatch(File.ReadAllText(meta),@"(?m)^  materialProfile:")});
            }
            result.pmxProfiles=pmx.OrderBy(p=>p.path,StringComparer.Ordinal).ToArray();result.pmxCount=pmx.Count;result.pmxLegacyProfile0Count=pmx.Count(p=>p.profile==0);
            if(result.pmxLegacyProfile0Count!=0)violations.Add("Fresh typed PMX legacy profile 0 importers remain (missing serialized fields are still 0): "+result.pmxLegacyProfile0Count);
            // Materials are only one consumer. Imported animation texture keys,
            // ShaderVariantCollections and ScriptableObject caches must also be
            // independent of the removed package. ProjectSettings singletons are
            // outside imported Assets and are cleaned separately after removal.
            string[] assetFiles=allPaths.Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&!AssetDatabase.IsValidFolder(p)).ToArray();
            result.inspectedAssetFiles=assetFiles.Length;
            string[] assetDependencies=assetFiles.Length==0?Array.Empty<string>():AssetDatabase.GetDependencies(assetFiles,true);
            result.allAssetsDependencyPaths=assetDependencies.Length;
            result.allAssetsLegacyDependencies=assetDependencies.Where(RemovedPackagePath).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            foreach(string dependency in result.allAssetsLegacyDependencies)violations.Add("Imported Assets file dependency remains in removed package (including non-material consumers): "+dependency);
            var inspected=new HashSet<string>(StringComparer.Ordinal);var materialIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(string path in native.materials.Select(m=>m.asset?.path).Where(p=>!string.IsNullOrEmpty(p)&&!RemovedPackagePath(p)
                &&(p.StartsWith("Assets/",StringComparison.Ordinal)||p.StartsWith("Packages/",StringComparison.Ordinal))).Distinct())Inspect(path);
            foreach(string path in allPaths.Where(p=>p.StartsWith("Packages/",StringComparison.Ordinal)&&!RemovedPackagePath(p)&&!AssetDatabase.IsValidFolder(p)
                &&(p.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)||ModelExtensions.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase))))
            {
                if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))result.inspectedPackageMaterialFiles++;else result.inspectedPackageModels++;
                Inspect(path);
            }
            const string defaultLit="Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(defaultLit)==null)violations.Add("The explicit pipeline default Lit material could not be inspected.");
            else Inspect(defaultLit);
            result.defaultMaterials=File.Exists(Physical(defaultLit))?new[]{Stamp(defaultLit)}:Array.Empty<FileStamp>();
            foreach(var package in PackageInfo.GetAllRegisteredPackages().Where(p=>p.name!=LegacyPackage))
                if(package.dependencies!=null&&package.dependencies.Any(d=>d.name==LegacyPackage))violations.Add("An installed package still declares the shader package dependency: "+package.name);
            foreach(var target in Targets)
                if(!PackageInfo.GetAllRegisteredPackages().Any(p=>p.name==target.name&&SamePath(p.resolvedPath,target.path)))violations.Add("Required specialized target is not registered at its fixed path: "+target.name);
            RequireCleanScenes();
            result.violations=violations.Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();return result;

            void Inspect(string path)
            {
                if(!inspected.Add(path))return;
                try
                {
                    var importer=AssetImporter.GetAtPath(path);if(importer!=null&&EditorUtility.IsDirty(importer))violations.Add("Unsaved inspected importer: "+path);
                    var loadedMaterials=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToArray();
                    if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)&&loadedMaterials.Length==0)violations.Add("A material file could not be loaded for retirement inspection: "+path);
                    foreach(var material in loadedMaterials)InspectMaterial(material);
                    if(ModelExtensions.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))
                    {
                        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if(model!=null)foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)if(material!=null)InspectMaterial(material);
                    }
                    if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))
                    {
                        string physical=Physical(path);string text=File.ReadAllText(physical);
                        foreach(Match match in Regex.Matches(text,@"m_Shader:\s*\{[^}]*\bguid:\s*([a-fA-F0-9]{32})"))
                            if(legacyGuids.Contains(match.Groups[1].Value))violations.Add("Serialized legacy shader GUID remains: "+path+" -> "+match.Groups[1].Value);
                    }
                    foreach(string dependency in AssetDatabase.GetDependencies(path,true))
                    {
                        result.inspectedDependencies++;
                        if(RemovedPackagePath(dependency))violations.Add("Imported asset depends on the removed package (shader/texture/other): "+path+" -> "+dependency);
                    }
                }
                catch(Exception error){violations.Add("Could not fully inspect material/model: "+path+": "+error.Message);}
            }
            void InspectMaterial(Material material)
            {
                bool persistent=AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string guid,out long id);string key=persistent?guid+":"+id:"nonpersistent:"+material.GetInstanceID();
                if(!materialIds.Add(key))return;result.inspectedMaterials++;
                if(EditorUtility.IsDirty(material))violations.Add("Unsaved inspected material: "+AssetDatabase.GetAssetPath(material));
                string shaderPath=material.shader==null?"":AssetDatabase.GetAssetPath(material.shader),shaderGuid=AssetDatabase.AssetPathToGUID(shaderPath);
                if(material.shader!=null&&LegacyName(material.shader.name)||legacyGuids.Contains(shaderGuid??"")||RemovedPackagePath(shaderPath))
                    violations.Add("Imported material still uses a legacy shader: "+AssetDatabase.GetAssetPath(material)+"#"+id+" -> "+shaderPath);
                string assetPath=AssetDatabase.GetAssetPath(material);
                if(!string.IsNullOrEmpty(assetPath)&&!RemovedPackagePath(assetPath)&&(assetPath.StartsWith("Assets/",StringComparison.Ordinal)||assetPath.StartsWith("Packages/",StringComparison.Ordinal)))Inspect(assetPath);
            }
        }
        static FileStamp ReviewedFile(string[] reviewed,List<string> violations)
        {
            var stamp=Stamp(reviewed[0]);stamp.reviewedSha256=reviewed[1];
            if(stamp.sha256!=stamp.reviewedSha256)violations.Add("The reviewed creation-entry source freeze changed: "+stamp.path);
            return stamp;
        }
        static FileStamp Stamp(string path)=>new() {path=path,sha256=HashFile(Physical(path)),metaSha256=HashFile(Physical(path+".meta"))};
        static void RequireSamePreflightInputs(RetirementEvidence evidence)
        {
            if(evidence.removePackageName!=LegacyPackage||evidence.violations.Length!=0||evidence.nativeErrors!=0||evidence.nativeLegacyMaterials!=0||evidence.pmxLegacyProfile0Count!=0)
                throw new InvalidOperationException("Retirement evidence is not eligible for the fixed removal.");
            if(HashFile(Path.Combine(pending.projectRoot,"Packages/manifest.json"))!=evidence.manifestSha256
                ||HashFile(Path.Combine(pending.projectRoot,"Packages/packages-lock.json"))!=evidence.packagesLockSha256||RegisteredPackagesHash()!=evidence.registeredPackagesSha256)
                throw new InvalidOperationException("Manifest, lock or package registrations changed during the concrete retirement audit.");
            foreach(var file in evidence.generatorSources.Concat(evidence.pmxSources).Concat(evidence.defaultMaterials))
                if(HashFile(Physical(file.path))!=file.sha256||HashFile(Physical(file.path+".meta"))!=file.metaSha256)throw new InvalidOperationException("Frozen retirement input changed: "+file.path);
            foreach(var pmx in evidence.pmxProfiles)
                if(HashFile(Physical(pmx.path+".meta"))!=pmx.metaSha256||(AssetImporter.GetAtPath(pmx.path) as HoPmxImporter)?.materialProfile==HoPmxMaterialProfile.LilToon)
                    throw new InvalidOperationException("PMX settings changed during retirement audit: "+pmx.path);
            if(HashFile(evidence.nativeAuditPath)!=evidence.nativeAuditSha256)throw new InvalidOperationException("Fresh native audit evidence changed.");
        }
        static void RequireRetirementEvidence()
        {
            if(string.IsNullOrEmpty(pending.retirementEvidencePath)||HashFile(pending.retirementEvidencePath)!=pending.retirementEvidenceSha256)
                throw new InvalidOperationException("The operation's concrete retirement evidence changed during removal/domain reload.");
            var evidence=JsonUtility.FromJson<RetirementEvidence>(File.ReadAllText(pending.retirementEvidencePath));
            if(evidence.projectRoot!=pending.projectRoot||evidence.removePackageName!=LegacyPackage||evidence.violations.Length!=0||evidence.nativeLegacyMaterials!=0||evidence.nativeErrors!=0||evidence.pmxLegacyProfile0Count!=0)
                throw new InvalidOperationException("The resumed operation does not have clean concrete retirement evidence.");
            if(HashFile(evidence.nativeAuditPath)!=evidence.nativeAuditSha256)throw new InvalidOperationException("The fresh native retirement audit changed during removal.");
            foreach(var file in evidence.generatorSources.Concat(evidence.pmxSources).Concat(evidence.defaultMaterials))
                if(HashFile(Physical(file.path))!=file.sha256||HashFile(Physical(file.path+".meta"))!=file.metaSha256)throw new InvalidOperationException("A frozen retirement source/default changed during removal: "+file.path);
        }
        static void RequireOtherDirectPackages(PackageSnapshot[] observed,bool removing)
        {
            string[] changed=pending.before.Where(p=>removing?p.name!=LegacyPackage:!Targets.Any(t=>t.name==p.name))
                .Where(p=>!observed.Any(after=>after.name==p.name&&after.direct&&after.packageId==p.packageId
                    &&(!removing||string.IsNullOrEmpty(p.resolvedPath)&&string.IsNullOrEmpty(after.resolvedPath)||SamePath(p.resolvedPath,after.resolvedPath)))).Select(p=>p.name).ToArray();
            if(changed.Length!=0)throw new InvalidOperationException("Untargeted direct packages changed or disappeared (including pipeline/commercial packages): "+string.Join(", ",changed));
        }
        static string RegisteredPackagesHash()=>HashBytes(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PackageSet {packages=PackageInfo.GetAllRegisteredPackages().Select(Snapshot).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray()})));
        static void RequireCleanScenes(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Retirement requires a stable Editor outside Play Mode/import/compilation.");for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene must be resolved before package retirement.");}
        static bool RemovedPackagePath(string path)=>!string.IsNullOrEmpty(path)&&(path=="Packages/"+LegacyPackage||path.StartsWith(LegacyPrefix,StringComparison.Ordinal));
        // Historical Ho producer names (e.g. Hidden/lilToon/URP/MaterialGradient/Demo)
        // belong to the retained pipeline. Package paths/GUIDs catch every actual legacy shader.
        static bool LegacyName(string name)=>name is "lilToon" or "Hidden/lilToonOutline" or "Hidden/lilToonCutout" or "Hidden/lilToonCutoutOutline"
            or "Hidden/lilToonTransparent" or "Hidden/lilToonTransparentOutline" or "Hidden/lilToonOnePassTransparent" or "Hidden/lilToonOnePassTransparentOutline"
            or "Hidden/lilToonTwoPassTransparent" or "Hidden/lilToonTwoPassTransparentOutline";
        static string Physical(string path)
        {
            if(Path.IsPathRooted(path))return path;
            if(path.StartsWith("Assets/",StringComparison.Ordinal))return Path.GetFullPath(Path.Combine(pending.projectRoot,path));
            if(path.StartsWith("Packages/",StringComparison.Ordinal))
            {
                string asset=path.EndsWith(".meta",StringComparison.Ordinal)?path[..^5]:path;var package=PackageInfo.FindForAssetPath(asset);
                if(package==null)throw new InvalidOperationException("No registered package owns retirement input: "+path);
                string prefix="Packages/"+package.name+"/";if(!path.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("Package path ownership differs.");
                return Path.Combine(package.resolvedPath,path.Substring(prefix.Length));
            }
            throw new InvalidOperationException("Unsupported retirement evidence input path: "+path);
        }
        static void WriteNew(string path,object value){RequireReportPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false));writer.Write(JsonUtility.ToJson(value,true));}
        static string HashFile(string path){using var stream=File.OpenRead(path);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static string HashBytes(byte[] bytes){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static PackageSnapshot Snapshot(PackageInfo package)=>new() {name=package.name,version=package.version,packageId=package.packageId,resolvedPath=package.resolvedPath,source=package.source.ToString(),direct=package.isDirectDependency};
        static void FinishFailure(Exception error)
        {
            if(pending!=null)Finish(false,error.ToString());
            else {Debug.LogError("HO_MATERIAL_PLATFORM_PACKAGE_FAILED "+error);EditorApplication.Exit(1);}
        }
        static void Finish(bool success,string error)
        {
            EditorApplication.update-=Tick;
            try
            {
                var report=new Report {operationId=pending.operationId,operation=pending.operation,status=success?"Succeeded":"Failed",projectRoot=pending.projectRoot,
                    startedUtc=pending.startedUtc,completedUtc=DateTime.UtcNow.ToString("O"),error=error,domainReloadResumptions=pending.resumptions,
                    requestedPackageNames=pending.operation=="InstallTargets"?Targets.Select(t=>t.name).ToArray():new[]{LegacyPackage},
                    requestedArguments=pending.operation=="InstallTargets"?Targets.Select(t=>"file:"+t.path).ToArray():new[]{LegacyPackage},
                    originalDirectPackages=pending.before,observedPackages=pending.observed,
                    verifiedTargets=success?pending.observed.Where(p=>Targets.Any(t=>t.name==p.name&&SamePath(p.resolvedPath,t.path))).ToArray():Array.Empty<PackageSnapshot>(),
                    untargetedDirectPackagesPreserved=success,legacyRemovalImplemented=true,retirementEvidencePath=pending.retirementEvidencePath,retirementEvidenceSha256=pending.retirementEvidenceSha256};
                string path=RequireReportPath(pending.reportPath);Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))writer.Write(JsonUtility.ToJson(report,true));
                SessionState.EraseString(SessionKey);pending=null;
                if(success)Debug.Log("HO_MATERIAL_PLATFORM_PACKAGES_VERIFIED operation="+report.operation+" targets="+report.verifiedTargets.Length+" resumed="+report.domainReloadResumptions);
                else Debug.LogError("HO_MATERIAL_PLATFORM_PACKAGE_FAILED "+error);
                EditorApplication.Exit(success?0:1);
            }
            catch(Exception reportError)
            {
                SessionState.EraseString(SessionKey);pending=null;
                Debug.LogError("HO_MATERIAL_PLATFORM_PACKAGE_REPORT_FAILED "+reportError);EditorApplication.Exit(1);
            }
        }
        static string RequireInvocationProject()
        {
            if(!Application.isBatchMode||Environment.GetCommandLineArgs().Any(a=>string.Equals(a,"-quit",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Use the direct Unity Editor binary in batchmode without -quit; asynchronous package requests must remain alive.");
            string requested=Environment.GetEnvironmentVariable("HO_MATERIAL_PLATFORM_PROJECT");
            string current=Canonical(Path.Combine(Application.dataPath,".."));
            if(string.IsNullOrEmpty(requested)||!SamePath(requested,current))throw new InvalidOperationException("HO_MATERIAL_PLATFORM_PROJECT must exactly select the current project: "+current);
            return current;
        }
        static string RequireReportPath(string value)
        {
            if(string.IsNullOrEmpty(value)||!Path.IsPathRooted(value))throw new InvalidOperationException("Set HO_MATERIAL_PLATFORM_PACKAGE_REPORT to a new absolute external report path.");
            string path=Path.GetFullPath(value),project=Canonical(Path.Combine(Application.dataPath,".."));
            if(SamePath(path,project)||Canonical(path).StartsWith(project+Path.DirectorySeparatorChar,PathComparison)||File.Exists(path)||Directory.Exists(path))
                throw new InvalidOperationException("Package report must be a new file outside this project: "+path);
            return path;
        }
        static StringComparison PathComparison=>Application.platform==RuntimePlatform.WindowsEditor?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        static bool SamePath(string a,string b)=>!string.IsNullOrEmpty(a)&&!string.IsNullOrEmpty(b)&&string.Equals(Canonical(a),Canonical(b),PathComparison);
        static string Canonical(string path)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
    }
}
