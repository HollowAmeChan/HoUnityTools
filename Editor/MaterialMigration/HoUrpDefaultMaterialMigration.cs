using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    /// <summary>Explicit, single-asset repair of the reviewed URP package default.
    /// Never runs on import/startup. Does not broaden the Assets-only family migration.
    /// RunApply writes this material only; RunVerify performs no import, save or file write.</summary>
    public static class HoUrpDefaultMaterialMigration
    {
        public const string MaterialAssetPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";
        private const string StockShaderPath = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";
        private const string MaterialGuid = "31321ba15b8f8eb4c954353edc038b1d";
        private const string LegacyShaderGuid = "df12117ecd77c31469c224178886498e";
        private const string StockShaderGuid = "933532a4fcc9baf4fa0491de14d08ed7";
        private const string BeforeSha = "3bacd77e892e3300b7ef4cf3832537d2968d2cb1129ff3dfcc1b8b3a935e1042";
        private const string MetaSha = "a96f8b5c6b76d1fefe7b4cd7d75120b07cb97338810776d2161d291eb2886ea8";
        private const string StockShaderSha = "e081d792dffe8c5d94ddb681a61b7ab6474880e605ba97116f202ad6f5b3befd";
        private static readonly Color ApprovedColor = new(1f,.96540874f,.96540874f,1f);

        [Serializable] public sealed class Report
        {
            public int version = 1;
            public string owner = "HoUnityTools.HoUrpDefaultMaterialMigration";
            public string state, projectRoot, packageRoot, materialPath, materialGuid, materialName;
            public string beforeSha256, metaSha256, afterSha256, sourceShaderGuid, targetShaderGuid, targetShaderDependencyHash;
            public string expectedNativeState, backupDirectory, reportPath, error;
            public string policy = "Keep live pink base color, metallic=1, smoothness=1, direct GGX on, probe/sky specular off. Adopt stock URP Lit; no legacy Ho material controls or unused saved textures are reactivated. Effective opaque queue 2000, backface culling, native shadow reception; preserve original GUID/fileID/name/GI/instancing and exact meta. Stock BRDF/GSAA and Ho integration are explicit model differences.";
        }
        [Serializable] private sealed class NativeState
        {
            public string shaderGuid, name, renderType;
            public int rawQueue, queue, giFlags;
            public bool instancing, doubleSidedGI;
            public string[] keywords, passStates;
            public Input[] inputs;
        }
        [Serializable] private sealed class Input
        {
            public string name, type, textureGuid;
            public long textureLocalId;
            public float scalar;
            public Vector4 colorOrVector;
            public Vector2 scale, offset;
        }
        private sealed class Context
        {
            internal string project, package, file, meta, report, backup, journal;
        }

        public static void RunApply()
        {
            var context = ReadContext();
            Require(!File.Exists(context.report) && !Directory.Exists(context.report), "Use a new external report file.");
            Require(!Directory.Exists(context.backup) && !File.Exists(context.backup), "Use a new external backup directory.");
            Require(!Inside(context.report, context.backup), "Keep the report outside the new backup directory.");
            var source = RequireOriginal(context);
            var shader = RequireStockShader(context);
            Material proposed = null;
            try
            {
                proposed = BuildNative(source, shader);
                RequireNativePolicy(proposed);
                var report = new Report
                {
                    state = "Prepared", projectRoot = context.project, packageRoot = context.package,
                    materialPath = context.file, materialGuid = MaterialGuid, materialName = source.name,
                    beforeSha256 = BeforeSha, metaSha256 = MetaSha, sourceShaderGuid = LegacyShaderGuid,
                    targetShaderGuid = StockShaderGuid, targetShaderDependencyHash = AssetDatabase.GetAssetDependencyHash(StockShaderPath).ToString(),
                    expectedNativeState = Snapshot(proposed), backupDirectory = context.backup, reportPath = context.report
                };
                // No asset writes precede these exact-byte backups and durable journal.
                Directory.CreateDirectory(context.backup);
                File.Copy(context.file, Path.Combine(context.backup,"original.mat"));
                File.Copy(context.meta, Path.Combine(context.backup,"original.mat.meta"));
                RequireBackup(context);
                WriteReports(context, report);
                bool writeStarted = false;
                try
                {
                    // Recheck disk and memory after preparing the proposal and backup.
                    source = RequireOriginal(context);
                    writeStarted = true;
                    EditorUtility.CopySerialized(proposed, source);
                    source.name = report.materialName;
                    EditorUtility.SetDirty(source);
                    AssetDatabase.SaveAssetIfDirty(source);
                    report.state = "Applied";WriteReports(context, report);
                    AssetDatabase.ImportAsset(MaterialAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    source = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
                    RequireIdentity(source);RequireClean();RequireNativePolicy(source);
                    Require(Hash(context.meta) == MetaSha, "Material meta changed; original GUID/importer settings must remain byte-identical.");
                    Require(Snapshot(source) == report.expectedNativeState, "Saved native properties, keywords, passes or render state differ from the concrete proposal.");
                    RequireNoLegacyDependency();
                    report.afterSha256 = Hash(context.file);report.state = "Verified";WriteReports(context, report);
                    Debug.Log("HO_URP_DEFAULT_MATERIAL_APPLY_VERIFIED guid="+MaterialGuid+" shader="+StockShaderGuid+" metallic=1 smoothness=1 IBL=off");
                }
                catch(Exception original)
                {
                    report.error = original.ToString();
                    if (!writeStarted)
                    {
                        // A concurrent source change before our first mutation is
                        // newer user work, not permission to restore the old backup.
                        report.state = "RejectedBeforeWrite";WriteReports(context,report);throw;
                    }
                    try
                    {
                        RestoreExactBytes(context);
                        report.state = "RolledBack";WriteReports(context, report);
                    }
                    catch(Exception rollback)
                    {
                        report.state = "RollbackFailed";report.error += "\nROLLBACK: "+rollback;
                        try { WriteReports(context,report); } catch { /* Preserve the external original files even if reporting also fails. */ }
                        throw new AggregateException("URP default conversion and exact-byte rollback failed; retain the external backup.", original, rollback);
                    }
                    throw;
                }
            }
            finally { if(proposed != null) Object.DestroyImmediate(proposed); }
        }

        // Intentionally read-only, including external reports. Safe to run in a new
        // Editor process after the old lil material package has been removed.
        public static void RunVerify()
        {
            var context = ReadContext();
            string json = File.ReadAllText(context.report);
            Require(json == File.ReadAllText(context.journal), "External report and backup journal differ.");
            var report = JsonUtility.FromJson<Report>(json);
            Require(report != null && report.version == 1 && report.owner == "HoUnityTools.HoUrpDefaultMaterialMigration" && report.state == "Verified", "A Verified report is required.");
            Require(Same(report.projectRoot,context.project) && Same(report.packageRoot,context.package) && Same(report.materialPath,context.file)
                && Same(report.backupDirectory,context.backup) && Same(report.reportPath,context.report), "Report belongs to a different project/package/path.");
            Require(report.materialGuid == MaterialGuid && report.beforeSha256 == BeforeSha && report.metaSha256 == MetaSha
                && report.sourceShaderGuid == LegacyShaderGuid && report.targetShaderGuid == StockShaderGuid, "Report does not describe the fixed reviewed conversion.");
            RequireBackup(context);RequireStockShader(context);RequireClean();
            Require(Hash(context.file) == report.afterSha256 && Hash(context.meta) == MetaSha, "Post-conversion material/importer bytes changed.");
            Require(AssetDatabase.GetAssetDependencyHash(StockShaderPath).ToString() == report.targetShaderDependencyHash, "Stock shader dependencies changed since Apply.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            RequireIdentity(material);RequireNativePolicy(material);
            Require(material.name == report.materialName && Snapshot(material) == report.expectedNativeState, "Loaded native material differs from the verified target.");
            RequireNoLegacyDependency();
            Debug.Log("HO_URP_DEFAULT_MATERIAL_VERIFY_OK guid="+MaterialGuid+" materialSha256="+report.afterSha256);
        }

        private static Material RequireOriginal(Context context)
        {
            Require(Hash(context.file) == BeforeSha && Hash(context.meta) == MetaSha, "The pre-existing stage4a material/meta changed; do not overwrite newer user work.");
            RequireClean();var source = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);RequireIdentity(source);
            Require(source.shader != null && source.shader.name == "lilToon" && GuidOf(source.shader) == LegacyShaderGuid, "Source must still use the actual reviewed old lil shader.");
            Require(source.name == "Lit" && source.rawRenderQueue == -1 && source.renderQueue == 2000, "Source name/queue differs from the reviewed default.");
            Require(source.GetColor("_Color") == ApprovedColor, "Live source base tint differs from the reviewed material.");
            foreach(string name in new[]{"_Metallic","_Smoothness","_UseReflection","_SpecularStrength","_ApplySpecular"}) Require(Float(source,name) == 1, "Live source input changed: "+name);
            foreach(string name in new[]{"_PBRInputMode","_ApplyReflection","_AlphaMaskMode","_UseEmission","_UseBumpMap","_UseSSS","_UseDither","_Invisible","_HoTesionEnabled","_UseMatCap","_UseBacklight","_UseRim","_UseParallax"})
                Require(Float(source,name) == 0, "Live source feature changed: "+name);
            Require(Float(source,"_Cull") == 2 && Float(source,"_ZWrite") == 1 && Float(source,"_Cutoff") == .5f, "Source raster policy changed.");
            Require(source.GetVector("_MainTexHSVG") == new Vector4(0,1,1,1), "Source tone correction changed.");
            foreach(string name in new[]{"_MainTex","_MetallicGlossMap","_SmoothnessTex","_ReflectanceTex","_PBRMap","_BumpMap","_AOMap","_AlphaMask","_EmissionMap","_SSSThicknessMap"})
                Require(!source.HasProperty(name) || source.GetTexture(name) == null, "Unexpected live source texture: "+name);
            Require(source.GetTextureScale("_MainTex") == Vector2.one && source.GetTextureOffset("_MainTex") == Vector2.zero, "Source main texture transform changed.");
            Require(!source.enableInstancing && !source.doubleSidedGI && (int)source.globalIlluminationFlags == 4, "Source material flags changed.");
            return source;
        }
        private static Material BuildNative(Material source, Shader shader)
        {
            var target = new Material(shader) { name = source.name, hideFlags = source.hideFlags,
                enableInstancing = source.enableInstancing, doubleSidedGI = source.doubleSidedGI, globalIlluminationFlags = source.globalIlluminationFlags };
            try
            {
                target.SetColor("_BaseColor", source.GetColor("_Color"));target.SetColor("_Color", source.GetColor("_Color"));
                foreach(string map in new[]{"_BaseMap","_MainTex","_MetallicGlossMap","_SpecGlossMap","_BumpMap","_ParallaxMap","_OcclusionMap","_EmissionMap","_DetailMask","_DetailAlbedoMap","_DetailNormalMap"}) target.SetTexture(map,null);
                foreach(string map in new[]{"_BaseMap","_MainTex"}) { target.SetTextureScale(map,Vector2.one);target.SetTextureOffset(map,Vector2.zero); }
                target.SetFloat("_WorkflowMode",1);target.SetFloat("_Metallic",1);target.SetFloat("_Smoothness",1);target.SetFloat("_SmoothnessTextureChannel",0);
                target.SetFloat("_Surface",0);target.SetFloat("_AlphaClip",0);target.SetFloat("_Blend",0);target.SetFloat("_Cull",2);target.SetFloat("_Cutoff",.5f);
                target.SetFloat("_SpecularHighlights",1);target.SetFloat("_EnvironmentReflections",0);target.SetFloat("_ReceiveShadows",1);
                target.SetFloat("_BumpScale",1);target.SetFloat("_OcclusionStrength",1);target.SetFloat("_QueueOffset",0);target.SetColor("_EmissionColor",new Color(0,0,0,1));
                // Use URP's real editor validator without adding an Editor assembly dependency.
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader",false)).FirstOrDefault(t=>t!=null);
                Require(type != null && typeof(ShaderGUI).IsAssignableFrom(type), "The stock URP Lit ShaderGUI is unavailable.");
                ((ShaderGUI)Activator.CreateInstance(type,true)).ValidateMaterial(target);
                target.renderQueue = source.renderQueue; // preserve effective 2000, allowing native Opaque tag normalization
                target.globalIlluminationFlags = source.globalIlluminationFlags;
                // Keep the same canonical disabled-pass entry as the original URP template.
                target.SetShaderPassEnabled("MOTIONVECTORS",false);
                return target;
            }
            catch { Object.DestroyImmediate(target);throw; }
        }
        private static void RequireNativePolicy(Material material)
        {
            Require(material != null && material.shader != null && material.shader.name == "Universal Render Pipeline/Lit" && GuidOf(material.shader) == StockShaderGuid, "Target is not the fixed stock URP Lit shader.");
            Require(material.GetColor("_BaseColor") == ApprovedColor && material.GetFloat("_Metallic") == 1 && material.GetFloat("_Smoothness") == 1, "Approved visible PBR inputs changed.");
            Require(material.GetFloat("_WorkflowMode") == 1 && material.GetFloat("_SpecularHighlights") == 1 && material.GetFloat("_EnvironmentReflections") == 0, "Direct/IBL workflow changed.");
            Require(material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), "The native IBL-off keyword is missing.");
            foreach(string keyword in new[]{"_SPECULAR_SETUP","_SPECULARHIGHLIGHTS_OFF","_METALLICSPECGLOSSMAP","_NORMALMAP","_PARALLAXMAP","_OCCLUSIONMAP","_EMISSION","_ALPHATEST_ON","_SURFACE_TYPE_TRANSPARENT","_RECEIVE_SHADOWS_OFF"})
                Require(!material.IsKeywordEnabled(keyword), "Unexpected native feature keyword: "+keyword);
            Require(material.GetFloat("_Surface") == 0 && material.GetFloat("_AlphaClip") == 0 && material.GetFloat("_Cull") == 2 && material.GetFloat("_ZWrite") == 1
                && material.GetFloat("_SrcBlend") == 1 && material.GetFloat("_DstBlend") == 0 && material.renderQueue == 2000, "Native opaque raster state changed.");
            Require(material.GetTexture("_BaseMap") == null && material.GetTextureScale("_BaseMap") == Vector2.one && material.GetTextureOffset("_BaseMap") == Vector2.zero, "Unexpected target base texture/transform.");
        }
        private static string Snapshot(Material material)
        {
            var inputs = Enumerable.Range(0,material.shader.GetPropertyCount()).Select(i=>
            {
                string name=material.shader.GetPropertyName(i);var type=material.shader.GetPropertyType(i);var value=new Input{name=name,type=type.ToString()};
                if(type==UnityEngine.Rendering.ShaderPropertyType.Color)value.colorOrVector=material.GetColor(name);
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Vector)value.colorOrVector=material.GetVector(name);
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    var texture=material.GetTexture(name);value.textureGuid="";
                    if(texture!=null)Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture,out value.textureGuid,out value.textureLocalId),"Target texture lacks persistent identity.");
                    value.scale=material.GetTextureScale(name);value.offset=material.GetTextureOffset(name);
                }
                else value.scalar=type==UnityEngine.Rendering.ShaderPropertyType.Int?material.GetInteger(name):material.GetFloat(name);
                return value;
            }).ToArray();
            return JsonUtility.ToJson(new NativeState { shaderGuid=GuidOf(material.shader),name=material.name,rawQueue=material.rawRenderQueue,queue=material.renderQueue,
                giFlags=(int)material.globalIlluminationFlags,instancing=material.enableInstancing,doubleSidedGI=material.doubleSidedGI,renderType=material.GetTag("RenderType",false,""),
                keywords=material.shaderKeywords.OrderBy(k=>k,StringComparer.Ordinal).ToArray(),inputs=inputs,
                passStates=Enumerable.Range(0,material.passCount).Select(i=>material.GetPassName(i)+"="+material.GetShaderPassEnabled(material.GetPassName(i))).Concat(new[]{"MOTIONVECTORS="+material.GetShaderPassEnabled("MOTIONVECTORS")}).ToArray() });
        }
        private static Context ReadContext()
        {
            Require(!Application.isPlaying,"Exit Play Mode before a package-default migration.");
            string project=Path.GetFullPath(Required("HO_URP_DEFAULT_MIGRATION_PROJECT"));
            Require(Same(project,Path.GetFullPath(Path.Combine(Application.dataPath,".."))),"The selected migration project is not the running Editor project.");
            var package=PackageInfo.FindForAssetPath(MaterialAssetPath);Require(package!=null && package.name=="com.unity.render-pipelines.universal","The URP material package is unresolved.");
            var result=new Context {project=project,package=Path.GetFullPath(package.resolvedPath),report=Path.GetFullPath(Required("HO_URP_DEFAULT_MIGRATION_REPORT")),backup=Path.GetFullPath(Required("HO_URP_DEFAULT_MIGRATION_BACKUP"))};
            result.file=Path.Combine(result.package,"Runtime/Materials/Lit.mat");result.meta=result.file+".meta";result.journal=Path.Combine(result.backup,"transaction.json");
            Require(!Inside(result.report,project)&&!Inside(result.report,result.package)&&!Inside(result.backup,project)&&!Inside(result.backup,result.package),"Report and backup must be outside the project and URP source package.");
            return result;
        }
        private static Shader RequireStockShader(Context context)
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(StockShaderPath);
            Require(shader!=null&&shader.name=="Universal Render Pipeline/Lit"&&GuidOf(shader)==StockShaderGuid,"The approved stock Lit shader is missing.");
            Require(Hash(Path.Combine(context.package,"Shaders/Lit.shader"))==StockShaderSha,"Stock Lit shader source changed; regenerate the concrete policy review.");return shader;
        }
        private static void RequireClean()
        {
            var importer=AssetImporter.GetAtPath(MaterialAssetPath);Require(importer!=null&&!EditorUtility.IsDirty(importer),"Unsaved URP default importer settings must be handled first.");
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(MaterialAssetPath))Require(asset==null||!EditorUtility.IsDirty(asset),"Unsaved URP default/subasset edits must be handled first.");
        }
        private static void RequireIdentity(Material material)
        {Require(material!=null&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string guid,out long id)&&guid==MaterialGuid&&id==2100000,"Original material GUID/fileID changed.");}
        private static void RequireNoLegacyDependency()
        {Require(!AssetDatabase.GetDependencies(MaterialAssetPath,true).Any(p=>p.StartsWith("Packages/jp.lilxyzw.liltoon/",StringComparison.Ordinal)),"The converted stock default still depends on the legacy material package.");}
        private static void RequireBackup(Context context)
        {Require(Hash(Path.Combine(context.backup,"original.mat"))==BeforeSha&&Hash(Path.Combine(context.backup,"original.mat.meta"))==MetaSha,"Exact original backups are missing or changed.");}
        private static void RestoreExactBytes(Context context)
        {
            RequireBackup(context);File.Copy(Path.Combine(context.backup,"original.mat"),context.file,true);File.Copy(Path.Combine(context.backup,"original.mat.meta"),context.meta,true);
            AssetDatabase.ImportAsset(MaterialAssetPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            Require(Hash(context.file)==BeforeSha&&Hash(context.meta)==MetaSha,"Exact restoration failed after reimport.");RequireIdentity(AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath));
        }
        private static void WriteReports(Context context,Report report)
        {string json=JsonUtility.ToJson(report,true);WriteAtomic(context.journal,json);WriteAtomic(context.report,json);}
        private static void WriteAtomic(string path,string json)
        {Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        private static float Float(Material material,string name)=>material.HasProperty(name)?material.GetFloat(name):0;
        private static string GuidOf(Object asset)=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long _)?guid:"";
        private static string Required(string name)=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))?Environment.GetEnvironmentVariable(name):throw new InvalidOperationException("Set "+name+" explicitly.");
        private static bool Same(string a,string b)=>!string.IsNullOrEmpty(a)&&!string.IsNullOrEmpty(b)&&string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);
        private static bool Inside(string path,string root)=>Same(path,root)||Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("URP default migration: "+message);}
    }
}
