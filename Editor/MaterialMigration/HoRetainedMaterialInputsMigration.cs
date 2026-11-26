using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // One explicitly chained transaction AFTER the eight material batches. No
    // earlier journal/report is read or rewritten. Never called on import/startup.
    public static class HoRetainedMaterialInputsMigration
    {
        private const string Source = "Packages/jp.lilxyzw.liltoon/Texture/matcap_metal_realistic.png";
        private const string SourceGuid = "a06ed8ab718f1884fa52fe010de1c3c7";
        private const string Destination = "Assets/HoMaterialMigration/RetainedLegacyInputs/matcap_metal_realistic.png";
        private const string Property = "_MatCapMap";
        private const string ClothGuid = "4acceec14bc941e1b5d1e7b533de44a4";
        private const string Evidence = "D:/Temp/ho-specialized-upm-stage4c-20261126/legacy-package-texture-check.json";
        private static readonly string[] Guids = {
            "ae78861c13944584b92934be649cc6b6", "fbe3f11eab7238a46b6542462ad36e91", "12d527b74817a0b4185a6b42a4ecd76e",
            "1277a1ff8d41564439ebf3502a26c071", "f49ce3d380618c9469e678d7f659ae55", "64226a3432c42914fabbceb3b37b959c", "c3a7bffdf998f774181b27ea8e496969" };
        [Serializable] public sealed class Journal
        {
            public int version=1;
            public string owner="HoUnityTools.HoRetainedMaterialInputsMigration";
            public string state,projectRoot,reportPath,backupDirectory,evidenceSha256,error;
            public Proof source;
            public Row[] materials;
            public string destinationGuid;public long destinationLocalId;
            public string[] createdPaths=Array.Empty<string>(),ownedDirectories=Array.Empty<string>();
            public FileStamp[] ownedFiles=Array.Empty<FileStamp>();
        }
        [Serializable] public sealed class Row
        {public string guid,path,beforeSha256,metaSha256,afterSha256,unchangedState;public long localId;public bool touched;}
        [Serializable] public sealed class Proof
        {
            public string assetPath,guid,physicalPath,fileSha256,metaSha256,importerState,samplingState,pixelSha256,graphicsApi,colorSpace;
            public long localId;
        }
        [Serializable] public sealed class FileStamp {public string path,sha256;}
        [Serializable] private sealed class Fields {public Field[] values;}
        [Serializable] private sealed class Field {public string path,type,value;}
        [Serializable] private sealed class Sampling
        {public int width,height,mips,format,filter,wrapU,wrapV,wrapW,aniso;public float bias;public bool srgb,readable,streaming;}
        private sealed class Context {internal string project,report,backup,journal;}

        public static void RunApply()
        {
            var c=ContextFromEnvironment();
            Require(!File.Exists(c.report)&&!Directory.Exists(c.report)&&!File.Exists(c.backup)&&!Directory.Exists(c.backup),"Use a new external report and backup directory.");
            Require(!Inside(c.report,c.backup),"Report must be outside the backup directory.");
            string evidence=File.ReadAllText(Evidence);ValidateEvidence(evidence);RequireNewDestination();
            var source=AssetDatabase.LoadAssetAtPath<Texture2D>(Source);Require(source!=null,"Install the original legacy texture package before Apply.");
            var proof=TextureProof(Source,SourceGuid);Require(proof.localId==2800000,"Unexpected source texture fileID.");
            var rows=Guids.Select(g=>
            {
                string path=AssetDatabase.GUIDToAssetPath(g);RequireAsset(path);Require(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase),"The fixed material GUID is not a standalone .mat: "+g);
                Clean(path);var m=MaterialAt(path,g);RequireReference(m,SourceGuid,proof.localId,Source);
                return new Row {guid=g,path=path,localId=LocalId(m),beforeSha256=Hash(Absolute(path)),metaSha256=Hash(Absolute(path+".meta")),unchangedState=MaterialState(m)};
            }).ToArray();
            Require(rows.Select(r=>r.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==7,"Material paths must be distinct.");
            var j=new Journal {state="Prepared",projectRoot=c.project,reportPath=c.report,backupDirectory=c.backup,evidenceSha256=Hash(Evidence),source=proof,materials=rows};
            Directory.CreateDirectory(c.backup);
            File.Copy(Evidence,Path.Combine(c.backup,"evidence.json"));
            File.Copy(proof.physicalPath,Path.Combine(c.backup,"source.png"));File.Copy(proof.physicalPath+".meta",Path.Combine(c.backup,"source.png.meta"));
            foreach(var r in rows){File.Copy(Absolute(r.path),Before(c,r));File.Copy(Absolute(r.path+".meta"),Before(c,r)+".meta");}
            Backups(c,j);Write(c,j);
            try
            {
                RequireCurrentSource(j.source);foreach(var r in rows)BeforeWrite(r);
                EnsureFolder(Path.GetDirectoryName(Destination).Replace('\\','/'),c,j);
                RequireNewDestination();Track(Destination,c,j); // durable intent before CopyAsset, including a partial failed copy
                Require(AssetDatabase.CopyAsset(Source,Destination),"AssetDatabase.CopyAsset failed.");
                AssetDatabase.ImportAsset(Destination,ImportAssetOptions.ForceSynchronousImport);
                j.destinationGuid=AssetDatabase.AssetPathToGUID(Destination,AssetPathToGUIDOptions.OnlyExistingAssets);
                var copy=AssetDatabase.LoadAssetAtPath<Texture2D>(Destination);Require(copy!=null&&j.destinationGuid!=SourceGuid&&!string.IsNullOrEmpty(j.destinationGuid),"Copy must have a new persistent GUID.");
                j.destinationLocalId=LocalId(copy);Write(c,j);EquivalentCopy(j);
                foreach(var r in rows)
                {
                    BeforeWrite(r);r.touched=true;Write(c,j);
                    var m=MaterialAt(r.path,r.guid);m.SetTexture(Property,copy);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
                    AssetDatabase.ImportAsset(r.path,ImportAssetOptions.ForceSynchronousImport);
                    m=MaterialAt(r.path,r.guid);Clean(r.path);RequireReference(m,j.destinationGuid,j.destinationLocalId,Destination);
                    Require(MaterialState(m)==r.unchangedState&&Hash(Absolute(r.path+".meta"))==r.metaSha256,"A material field other than the texture reference changed: "+r.path);
                    r.afterSha256=Hash(Absolute(r.path));j.state="Applied";Write(c,j);
                }
                RequireCurrentSource(j.source);EquivalentCopy(j);
                j.ownedFiles=OwnedFiles(j).Select(p=>new FileStamp {path=p,sha256=Hash(Absolute(p))}).ToArray();j.ownedDirectories=OwnedDirectories(j);
                j.state="Verified";Write(c,j);Debug.Log("HO_RETAINED_INPUTS_APPLY_VERIFIED materials=7 copiedGuid="+j.destinationGuid);
            }
            catch(Exception error)
            {
                j.error=error.ToString();
                try {Rollback(c,j,false);j.state="RolledBack";Write(c,j);}
                catch(Exception rollback){j.state="RollbackFailed";j.error+="\nROLLBACK: "+rollback;try{Write(c,j);}catch{}throw new AggregateException("Retained-input migration failed; preserve its independent backup.",error,rollback);}
                throw;
            }
        }
        public static void RunVerify()
        {var c=ContextFromEnvironment();var j=Read(c);Verify(c,j,false);Debug.Log("HO_RETAINED_INPUTS_VERIFY_OK materials=7 copiedGuid="+j.destinationGuid);}
        public static void RunRestore()
        {
            var c=ContextFromEnvironment();var j=Read(c);Verify(c,j,true);
            // Verify completes ALL byte/dirty/reference/ownership/source checks before the first write.
            Rollback(c,j,true);j.state="Restored";Write(c,j);Debug.Log("HO_RETAINED_INPUTS_RESTORED materials=7");
        }
        private static void Verify(Context c,Journal j,bool restoring)
        {
            Require(j.state=="Verified","Only a Verified independent transaction can be verified/restored.");Backups(c,j);
            Require(j.source.graphicsApi==SystemInfo.graphicsDeviceType.ToString()&&j.source.colorSpace==QualitySettings.activeColorSpace.ToString(),"Use the Apply graphics API/color space for the exact mip sampling check.");
            if(restoring)RequireCurrentSource(j.source);
            else if(PackageInfo.FindForAssetPath(Source)!=null)RequireCurrentSource(j.source); // legacy package may already be intentionally uninstalled
            EquivalentCopy(j);RequireOwnedUnchanged(j);
            foreach(var r in j.materials)
            {
                Clean(r.path);Require(Hash(Absolute(r.path))==r.afterSha256&&Hash(Absolute(r.path+".meta"))==r.metaSha256,"Later material edits require a merge: "+r.path);
                var m=MaterialAt(r.path,r.guid);Require(LocalId(m)==r.localId&&MaterialState(m)==r.unchangedState,"Material state/identity changed: "+r.path);
                RequireReference(m,j.destinationGuid,j.destinationLocalId,Destination);
            }
        }
        private static void Rollback(Context c,Journal j,bool guarded)
        {
            Backups(c,j);
            foreach(var r in j.materials.Where(r=>r.touched))
            {File.Copy(Before(c,r),Absolute(r.path),true);File.Copy(Before(c,r)+".meta",Absolute(r.path+".meta"),true);}
            foreach(var r in j.materials.Where(r=>r.touched))AssetDatabase.ImportAsset(r.path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            foreach(var r in j.materials.Where(r=>r.touched))Require(Hash(Absolute(r.path))==r.beforeSha256&&Hash(Absolute(r.path+".meta"))==r.metaSha256,"Exact material rollback failed: "+r.path);
            foreach(string path in j.createdPaths.Reverse())
            {
                RequireOwnedPath(path);
                if(!File.Exists(Absolute(path))&&!Directory.Exists(Absolute(path))&&!File.Exists(Absolute(path+".meta")))continue;
                // An untracked file in a newly created directory is never deleted.
                if(Directory.Exists(Absolute(path)))Require(!Directory.EnumerateFileSystemEntries(Absolute(path)).Any(),"Created directory contains unrelated files; retain it for review: "+path);
                Require(AssetDatabase.DeleteAsset(path),"Cannot delete migration-owned asset/directory: "+path);
                Require(!File.Exists(Absolute(path))&&!Directory.Exists(Absolute(path))&&!File.Exists(Absolute(path+".meta")),"Owned asset remained after deletion: "+path);
            }
        }
        private static void BeforeWrite(Row r)
        {
            Clean(r.path);Require(Hash(Absolute(r.path))==r.beforeSha256&&Hash(Absolute(r.path+".meta"))==r.metaSha256,"Material changed before this chained migration: "+r.path);
            var m=MaterialAt(r.path,r.guid);Require(LocalId(m)==r.localId&&MaterialState(m)==r.unchangedState,"Loaded material inputs changed: "+r.path);RequireReference(m,SourceGuid,2800000,Source);
        }
        private static void EquivalentCopy(Journal j)
        {
            Clean(Destination);var proof=TextureProof(Destination,j.destinationGuid);
            Require(proof.localId==j.destinationLocalId&&proof.guid!=SourceGuid,"Copied texture identity changed.");
            Require(proof.fileSha256==j.source.fileSha256&&proof.importerState==j.source.importerState&&proof.samplingState==j.source.samplingState&&proof.pixelSha256==j.source.pixelSha256,
                "Copied PNG bytes, importer settings, runtime sampling or decoded mip pixels differ from the original.");
        }
        private static void RequireCurrentSource(Proof proof)
        {
            var current=TextureProof(Source,SourceGuid);
            Require(current.fileSha256==proof.fileSha256&&current.metaSha256==proof.metaSha256&&current.localId==proof.localId&&current.importerState==proof.importerState
                &&current.samplingState==proof.samplingState&&current.pixelSha256==proof.pixelSha256,"Reinstall the original legacy input package/settings before restoration; its source proof differs.");
        }
        private static Proof TextureProof(string path,string guid)
        {
            Clean(path);var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            Require(t!=null&&importer!=null&&GuidOf(t)==guid,"Missing/unexpected PNG texture identity: "+path);
            Require(importer.GetExternalObjectMap().Count==0,"This fixed PNG must not have external importer remaps.");
            Require(!t.streamingMipmaps||t.loadedMipmapLevel==0,"Wait until all source/copied texture mips are resident; no source streaming settings are changed.");
            string file=Physical(path);
            return new Proof {assetPath=path,guid=guid,localId=LocalId(t),physicalPath=file,fileSha256=Hash(file),metaSha256=Hash(file+".meta"),
                importerState=SerializedState(importer,null,path),samplingState=JsonUtility.ToJson(new Sampling {width=t.width,height=t.height,mips=t.mipmapCount,format=(int)t.format,filter=(int)t.filterMode,
                    wrapU=(int)t.wrapModeU,wrapV=(int)t.wrapModeV,wrapW=(int)t.wrapModeW,aniso=t.anisoLevel,bias=t.mipMapBias,srgb=t.isDataSRGB,readable=t.isReadable,streaming=t.streamingMipmaps}),
                pixelSha256=MipPixels(t),graphicsApi=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString()};
        }
        private static string MaterialState(Material m)
        {
            using var so=new SerializedObject(m);var env=so.FindProperty("m_SavedProperties.m_TexEnvs");Require(env!=null&&env.isArray,"Unsupported native material serialization.");
            var paths=new List<string>();for(int i=0;i<env.arraySize;i++){var entry=env.GetArrayElementAtIndex(i);if(entry.FindPropertyRelative("first").stringValue==Property)paths.Add(entry.FindPropertyRelative("second.m_Texture").propertyPath);}
            Require(paths.Count==1,"Exactly one serialized MatCap binding is required.");return SerializedState(m,paths[0],null);
        }
        // Includes saved/unexposed fields, custom tags, pass state and texture ST.
        // Object references use persistent IDs, never process-local instance IDs.
        private static string SerializedState(Object obj,string ignoredReference,string texturePath)
        {
            using var so=new SerializedObject(obj);var p=so.GetIterator();var fields=new List<Field>();
            bool enterChildren=true;
            while(p.Next(enterChildren))
            {
                string value;var type=p.propertyType;
                // ObjectReference children expose process-local m_FileID handles.
                // The parent persistent GUID/localID is the complete stable value.
                enterChildren=type!=SerializedPropertyType.ObjectReference;
                if(type==SerializedPropertyType.Generic)continue;
                switch(type)
                {
                    case SerializedPropertyType.ObjectReference:
                        value=p.propertyPath==ignoredReference?"<retained-matcap-reference>":p.objectReferenceValue==null?"<null>":GuidOf(p.objectReferenceValue)+":"+LocalId(p.objectReferenceValue);break;
                    case SerializedPropertyType.String:
                        value=p.stringValue;if(texturePath!=null&&(value==texturePath||value==Physical(texturePath)))value="<retained-png-path>";break;
                    case SerializedPropertyType.Boolean:value=p.boolValue?"1":"0";break;
                    case SerializedPropertyType.Float:value=p.doubleValue.ToString("R",CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.Integer:value=p.longValue.ToString(CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.Enum:case SerializedPropertyType.ArraySize:case SerializedPropertyType.Character:case SerializedPropertyType.LayerMask:value=p.intValue.ToString(CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.FixedBufferSize:value=p.fixedBufferSize.ToString(CultureInfo.InvariantCulture);break;
                    case SerializedPropertyType.Color:value=V(p.colorValue);break;
                    case SerializedPropertyType.Vector2:value=V(p.vector2Value);break;
                    case SerializedPropertyType.Vector3:value=V(p.vector3Value);break;
                    case SerializedPropertyType.Vector4:value=V(p.vector4Value);break;
                    case SerializedPropertyType.Quaternion:var q=p.quaternionValue;value=V(new Vector4(q.x,q.y,q.z,q.w));break;
                    case SerializedPropertyType.Rect:var r=p.rectValue;value=V(new Vector4(r.x,r.y,r.width,r.height));break;
                    case SerializedPropertyType.Hash128:value=p.hash128Value.ToString();break;
                    default:throw new InvalidOperationException("Unsupported serialized field in this fixed migration: "+p.propertyPath+" "+type);
                }
                fields.Add(new Field {path=p.propertyPath,type=type.ToString(),value=value});
            }
            return JsonUtility.ToJson(new Fields {values=fields.ToArray()});
        }
        private static string MipPixels(Texture2D texture)
        {
            Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"GPU mip proof requires a graphics device; do not use -nographics.");
            const string code=@"Shader ""Hidden/HoRetainedInputs/MipProof"" {Properties{_MainTex(""Source"",2D)=""white""{}}SubShader{Pass{Cull Off ZWrite Off ZTest Always HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D _MainTex;SamplerState sampler_point_clamp;float _Mip;
float4 frag(v2f_img i):SV_Target{return _MainTex.SampleLevel(sampler_point_clamp,i.uv,_Mip);}
ENDHLSL}}}";
            bool async=ShaderUtil.allowAsyncCompilation,write=GL.sRGBWrite;var active=RenderTexture.active;Shader shader=null;Material material=null;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;shader=ShaderUtil.CreateShaderAsset(code,false);shader.hideFlags=HideFlags.HideAndDontSave;
                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};ShaderUtil.CompilePass(material,0,true);Require(!ShaderUtil.ShaderHasError(shader),"Mip proof shader failed.");
                using var hash=SHA256.Create();GL.sRGBWrite=false;
                for(int mip=0;mip<texture.mipmapCount;mip++)
                {
                    var target=RenderTexture.GetTemporary(Math.Max(1,texture.width>>mip),Math.Max(1,texture.height>>mip),0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    try{material.SetFloat("_Mip",mip);Graphics.Blit(texture,target,material);var read=AsyncGPUReadback.Request(target);read.WaitForCompletion();Require(!read.hasError,"Mip readback failed.");byte[] bytes=read.GetData<byte>().ToArray();hash.TransformBlock(bytes,0,bytes.Length,null,0);}
                    finally{RenderTexture.ReleaseTemporary(target);}
                }
                hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return Hex(hash.Hash);
            }
            finally{ShaderUtil.allowAsyncCompilation=async;GL.sRGBWrite=write;RenderTexture.active=active;if(material!=null)Object.DestroyImmediate(material);if(shader!=null)Object.DestroyImmediate(shader);}
        }
        private static void ValidateEvidence(string json)
        {
            const string row=@"\[\s*""(?<g>[a-f0-9]{32})""\s*,\s*""Cloth""\s*,\s*""_MatCapMap""\s*,\s*""Packages/jp\.lilxyzw\.liltoon/Texture/matcap_metal_realistic\.png""\s*\]";
            var matches=Regex.Matches(json,row);Require(Regex.IsMatch(Regex.Replace(json,row,"x"),@"^\s*\[\s*x(?:\s*,\s*x)*\s*\]\s*$"),"The evidence must contain only the fixed PNG/property/Cloth rows.");
            Require(matches.Count==7&&matches.Cast<Match>().Select(m=>m.Groups["g"].Value).OrderBy(g=>g).SequenceEqual(Guids.OrderBy(g=>g)),"Evidence differs from the exact seven approved GUIDs.");
        }
        private static Material MaterialAt(string path,string guid)
        {var m=AssetDatabase.LoadAssetAtPath<Material>(path);Require(m!=null&&GuidOf(m)==guid&&m.shader!=null&&m.shader.name=="HoShader/Cloth URP"&&GuidOf(m.shader)==ClothGuid,"Expected a current native Cloth material: "+path);return m;}
        private static void RequireReference(Material m,string guid,long id,string path)
        {var t=m.GetTexture(Property);Require(t!=null&&GuidOf(t)==guid&&LocalId(t)==id&&AssetDatabase.GetAssetPath(t)==path,"Unexpected MatCap reference on "+AssetDatabase.GetAssetPath(m));}
        private static void Clean(string path)
        {var i=AssetImporter.GetAtPath(path);Require(i!=null&&!EditorUtility.IsDirty(i),"Unsaved importer settings: "+path);foreach(var o in AssetDatabase.LoadAllAssetsAtPath(path))Require(o==null||!EditorUtility.IsDirty(o),"Unsaved native asset/subasset: "+path);}
        private static void RequireNewDestination()
        {
            Require(!File.Exists(Absolute(Destination))&&!Directory.Exists(Absolute(Destination))&&!File.Exists(Absolute(Destination+".meta"))&&string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(Destination,AssetPathToGUIDOptions.OnlyExistingAssets)),"Destination must be new; never overwrite a retained/user PNG.");
            for(string p=Path.GetDirectoryName(Destination).Replace('\\','/');p!="Assets";p=Path.GetDirectoryName(p).Replace('\\','/'))Require(!File.Exists(Absolute(p))&&(Directory.Exists(Absolute(p))||!File.Exists(Absolute(p+".meta"))),"Invalid output ancestor: "+p);
        }
        private static void EnsureFolder(string path,Context c,Journal j)
        {
            if(AssetDatabase.IsValidFolder(path))return;RequireOwnedPath(path);Require(!File.Exists(Absolute(path))&&!Directory.Exists(Absolute(path))&&!File.Exists(Absolute(path+".meta")),"Refusing to adopt an existing unimported output folder.");
            string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent,c,j);string guid=AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
            Require(!string.IsNullOrEmpty(guid),"Could not create output directory.");string actual=AssetDatabase.GUIDToAssetPath(guid);Track(actual,c,j);Require(actual==path,"Unity renamed an output directory.");
        }
        private static void Track(string path,Context c,Journal j){RequireOwnedPath(path);j.createdPaths=j.createdPaths.Concat(new[]{path}).Distinct(StringComparer.Ordinal).ToArray();Write(c,j);}
        private static string[] OwnedFiles(Journal j)
        {var all=new HashSet<string>(StringComparer.Ordinal);foreach(string p in j.createdPaths){if(Directory.Exists(Absolute(p)))foreach(string f in Directory.GetFiles(Absolute(p),"*",SearchOption.AllDirectories))all.Add(Relative(f));else if(File.Exists(Absolute(p)))all.Add(p);if(File.Exists(Absolute(p+".meta")))all.Add(p+".meta");}return all.OrderBy(p=>p,StringComparer.Ordinal).ToArray();}
        private static string[] OwnedDirectories(Journal j)
        {var all=new HashSet<string>(StringComparer.Ordinal);foreach(string p in j.createdPaths.Where(p=>Directory.Exists(Absolute(p)))){all.Add(p);foreach(string d in Directory.GetDirectories(Absolute(p),"*",SearchOption.AllDirectories))all.Add(Relative(d));}return all.OrderBy(p=>p,StringComparer.Ordinal).ToArray();}
        private static void RequireOwnedUnchanged(Journal j)
        {
            Require(OwnedFiles(j).SequenceEqual(j.ownedFiles.Select(f=>f.path))&&OwnedDirectories(j).SequenceEqual(j.ownedDirectories),"New files/directories appeared in owned output.");
            foreach(var f in j.ownedFiles)Require(Hash(Absolute(f.path))==f.sha256,"Owned file changed: "+f.path);
            foreach(string p in j.createdPaths)if(File.Exists(Absolute(p))||Directory.Exists(Absolute(p)))Clean(p);
        }
        private static void Backups(Context c,Journal j)
        {
            Require(Hash(Path.Combine(c.backup,"source.png"))==j.source.fileSha256&&Hash(Path.Combine(c.backup,"source.png.meta"))==j.source.metaSha256,"Original PNG backup changed.");
            string evidence=File.ReadAllText(Path.Combine(c.backup,"evidence.json"));ValidateEvidence(evidence);Require(Hash(Path.Combine(c.backup,"evidence.json"))==j.evidenceSha256,"Frozen evidence changed.");
            foreach(var r in j.materials)Require(Hash(Before(c,r))==r.beforeSha256&&Hash(Before(c,r)+".meta")==r.metaSha256,"Material backup changed: "+r.guid);
        }
        private static Context ContextFromEnvironment()
        {
            Require(!Application.isPlaying,"Exit Play Mode.");string project=Path.GetFullPath(Required("HO_RETAINED_INPUTS_PROJECT"));Require(Same(project,ProjectRoot),"Run in the explicitly selected project.");
            var c=new Context{project=project,report=Path.GetFullPath(Required("HO_RETAINED_INPUTS_REPORT")),backup=Path.GetFullPath(Required("HO_RETAINED_INPUTS_BACKUP"))};c.journal=Path.Combine(c.backup,"transaction.json");
            Require(!Inside(c.report,project)&&!Inside(c.backup,project)&&!Inside(c.report,c.backup),"Use separate external report/backup paths.");return c;
        }
        private static Journal Read(Context c)
        {
            string json=File.ReadAllText(c.report);Require(json==File.ReadAllText(c.journal),"Independent report and backup journal differ.");var j=JsonUtility.FromJson<Journal>(json);
            Require(j!=null&&j.version==1&&j.owner=="HoUnityTools.HoRetainedMaterialInputsMigration"&&Same(j.projectRoot,c.project)&&Same(j.reportPath,c.report)&&Same(j.backupDirectory,c.backup),"Wrong retained-input transaction/project.");
            Require(j.source!=null&&j.source.assetPath==Source&&j.source.guid==SourceGuid&&j.materials.Length==7&&j.materials.Select(r=>r.guid).OrderBy(g=>g).SequenceEqual(Guids.OrderBy(g=>g)),"The fixed migration identities changed.");
            foreach(var r in j.materials)RequireAsset(r.path);foreach(string p in j.createdPaths)RequireOwnedPath(p);return j;
        }
        private static void Write(Context c,Journal j){string json=JsonUtility.ToJson(j,true);Atomic(c.journal,json);Atomic(c.report,json);}
        private static void Atomic(string path,string json){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,json,new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
        private static string Physical(string path){if(path.StartsWith("Assets/",StringComparison.Ordinal))return Absolute(path);var package=PackageInfo.FindForAssetPath(path);Require(package!=null,"Reinstall the source texture package before restoration.");return Path.Combine(package.resolvedPath,path.Substring(("Packages/"+package.name+"/").Length));}
        private static void RequireAsset(string path){Require(!string.IsNullOrEmpty(path)&&path.StartsWith("Assets/",StringComparison.Ordinal)&&!path.Contains('\\')&&!path.Split('/').Any(p=>p==".."||p=="."||p.Length==0)&&Inside(Absolute(path),Path.Combine(ProjectRoot,"Assets")),"Material/output path escapes Assets.");}
        private static void RequireOwnedPath(string path){RequireAsset(path);Require(path==Destination||path=="Assets/HoMaterialMigration"||path=="Assets/HoMaterialMigration/RetainedLegacyInputs","Unexpected owned output path: "+path);}
        private static string Before(Context c,Row r)=>Path.Combine(c.backup,r.guid+".mat");
        private static string GuidOf(Object o){Require(o!=null&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string g,out long _),"Persistent identity required.");AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long _);return guid;}
        private static long LocalId(Object o){Require(o!=null&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string _,out long _),"Persistent identity required.");AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string _,out long id);return id;}
        private static string V(Vector4 v)=>string.Join(",",new[]{v.x,v.y,v.z,v.w}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
        private static string Required(string key)=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))?Environment.GetEnvironmentVariable(key):throw new InvalidOperationException("Set "+key+" explicitly.");
        private static string ProjectRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(ProjectRoot,path));
        private static string Relative(string path)=>Path.GetRelativePath(ProjectRoot,path).Replace('\\','/');
        private static bool Same(string a,string b)=>!string.IsNullOrEmpty(a)&&!string.IsNullOrEmpty(b)&&string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);
        private static bool Inside(string path,string root)=>Same(path,root)||Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static string Hash(string path){using var sha=SHA256.Create();using var s=File.OpenRead(path);return Hex(sha.ComputeHash(s));}
        private static string HashBytes(byte[] data){using var sha=SHA256.Create();return Hex(sha.ComputeHash(data));}
        private static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Retained material inputs: "+message);}
    }
}
