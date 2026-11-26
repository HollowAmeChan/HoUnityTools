using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LWGUI;
using LWGUI.Runtime.LwguiGradient;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.ShadowResponse.LwguiIntegration
{
    // Explicit, guarded recovery of original source snapshots. Never called during import or repaint.
    // Materials store a linear texture and surface inputs; all thresholds remain offline curve data.
    public static class HoShadowRampRecovery
    {
        [Serializable] public sealed class SourceProperty
        {
            public string name, type, textureGuid, texturePath, textureSha256, textureMetaSha256, dependencyHash;
            public float scalar, x, y, z, w, scaleX, scaleY, offsetX, offsetY;
            public long textureLocalId;
        }
        [Serializable] public sealed class Row
        {
            public string sourceGuid, sourcePath, sourceSha256, sourceMetaSha256, sourceShaderGuid, sourceShaderPath, sourceShaderFilePath, sourceShaderSha256;
            public string sourceBackupPath, sourceBackupSha256, targetGuid, targetPath, targetSha256, targetMetaSha256, targetFamily, shapeSignature, rampAssetPath;
            public bool activeShadow, isFork, rampIsPackagePreset;
            public SourceProperty[] sourceProperties;
        }
        [Serializable] public sealed class Ramp
        {
            public string assetPath, shapeSignature;
            public bool isPackagePreset, isNeutral;
            public float border1, blur1, border2, blur2, border3, blur3, borderRange;
            public string[] targetGuids;
        }
        [Serializable] public sealed class Plan
        {
            public int schemaVersion;
            public string kind, projectRoot, inventoryPath, inventorySha256, provenancePath, provenanceSha256, inputFingerprint;
            public Row[] rows;
            public Ramp[] ramps, neutralPresets;
            public MathSource[] sourceMathFiles;
        }
        [Serializable] public sealed class MathSource { public string path, sha256; }
        [Serializable] public sealed class Stamp { public string path, sha; }
        [Serializable] public sealed class Applied
        {
            public Row row;
            public string unaffectedState, shaderGuid, shaderHash, afterSha, afterMetaSha;
            public long localId, rampLocalId;
        }
        [Serializable] public sealed class Journal
        {
            public int version = 1;
            public string projectRoot, planSha, state, reusedFromBackup, reusedFromJournalSha;
            public Applied[] rows;
            public Stamp[] resources;
            public string[] createdAssets, createdFolders;
        }
        [Serializable] public sealed class Preview
        {
            public string projectRoot, planSha;
            public int sourceCount, targetCount, activeTargetCount, actorRampCount, neutralPresetCount;
            public string[] boundaries;
        }
        [Serializable] public sealed class Recipe
        {
            public string format = "HoShader.ShadowResponseWeights/1";
            public string channels = "R=first lit weight; G=second lit weight; B=third lit weight; A=first border weight";
            public string sampling = "linear RGBAHalf, endpoints, clamp, bilinear";
            public Ramp ramp;
            public SourceRecipe[] materials;
        }
        [Serializable] public sealed class SourceRecipe
        {
            public string sourceGuid, sourcePath, sourceSha256, targetGuid, targetPath;
            public bool activeShadow;
            public SourceProperty[] originalShadowInputs;
        }
        [Serializable] private sealed class Field { public string path, type, value; }
        [Serializable] private sealed class Fields { public Field[] fields; }
        private static readonly string[] ImportedTextures = { "StrengthMask", "ReceiveMask", "ColorTex1", "ColorTex2", "ColorTex3", "AOMap", "AOMask" };

        public static void RunPreview()
        {
            var plan = ReadPlan();
            ValidatePlan(plan, true);
            var preview = new Preview
            {
                projectRoot = Root, planSha = Hash(Required("HO_SHADOW_RECOVERY_PLAN")),
                sourceCount = plan.rows.Select(r => r.sourceGuid).Distinct().Count(), targetCount = plan.rows.Length,
                activeTargetCount = plan.rows.Count(r => r.activeShadow), actorRampCount = plan.ramps.Count(r => !r.isPackagePreset), neutralPresetCount = plan.ramps.Count(r => r.isPackagePreset),
                boundaries = new[] {
                    "Original source parameters and spatial texture references are restored. Native URP family lighting, GI and specular remain in use; full old-forward-frame equality is not claimed.",
                    "RGBA stores four independent visibility functions. Runtime has no old band border/blur parameters. Curve editing and clone retain this encoding.",
                    "Zero-width and very narrow transitions have a finite bilinear LUT boundary footprint. Sharp curves use 16384 samples and keep exact authoring keys; GPU error around discontinuities is reported separately.",
                    "Identical functions share one editable asset within their original character texture owner. Disabled source shadows use a neutral package preset and an explicit disabled response.",
                    "Original material and meta bytes are backed up externally. Apply/Verify/Restore guard identities, source SHA, generated resources and unrelated effective fields." }
            };
            WriteNew(External("HO_SHADOW_RECOVERY_PREVIEW", true), preview);
            Debug.Log("HO_SHADOW_RECOVERY_PREVIEW_OK targets=" + preview.targetCount + " active=" + preview.activeTargetCount + " actorRamps=" + preview.actorRampCount);
        }

        public static void RunApply()
        {
            var plan = ReadPlan();
            string reusePath = Environment.GetEnvironmentVariable("HO_SHADOW_RECOVERY_REUSE_BACKUP");
            Journal reused = null;
            if (!string.IsNullOrEmpty(reusePath))
            {
                reusePath = External("HO_SHADOW_RECOVERY_REUSE_BACKUP", false);
                reused = ReadJournal(reusePath);
                Need(reused.state == "Restored" && reused.planSha == Hash(Required("HO_SHADOW_RECOVERY_PLAN")), "Reuse only an exactly restored transaction with the same immutable plan.");
                CheckResourceStamps(reused);
                Need(reused.rows.Select(r=>r.row.targetGuid).OrderBy(g=>g,StringComparer.Ordinal).SequenceEqual(plan.rows.Select(r=>r.targetGuid).OrderBy(g=>g,StringComparer.Ordinal)), "Reused target set changed.");
                foreach(var ramp in plan.ramps)Need(reused.createdAssets.Contains(ramp.assetPath),"Reused transaction does not own planned Ramp: "+ramp.assetPath);
            }
            ValidatePlan(plan, reused == null);
            string backup = External("HO_SHADOW_RECOVERY_BACKUP", true);
            Directory.CreateDirectory(backup);
            var journal = new Journal { projectRoot = Root, planSha = Hash(Required("HO_SHADOW_RECOVERY_PLAN")), state = "Prepared",
                reusedFromBackup = reusePath, reusedFromJournalSha = reused == null ? null : Hash(Path.Combine(reusePath,"journal.json")),
                rows = plan.rows.Select(row => Capture(row)).ToArray(), resources = Array.Empty<Stamp>(), createdAssets = Array.Empty<string>(), createdFolders = Array.Empty<string>() };
            foreach (var item in journal.rows)
            {
                File.Copy(Absolute(item.row.targetPath), Path.Combine(backup, item.row.targetGuid + ".mat"));
                File.Copy(Absolute(item.row.targetPath + ".meta"), Path.Combine(backup, item.row.targetGuid + ".meta"));
            }
            File.Copy(Required("HO_SHADOW_RECOVERY_PLAN"), Path.Combine(backup, "plan.json"));
            // The full immutable recipe stays outside Assets; no private actor data enters UPM packages.
            if (!string.IsNullOrEmpty(plan.provenancePath)) File.Copy(plan.provenancePath, Path.Combine(backup, "source-provenance.json"));
            Save(backup, journal);
            var created = reused == null ? new List<string>() : new List<string>(reused.createdAssets);
            var folders = reused == null ? new List<string>() : new List<string>(reused.createdFolders);
            journal.createdAssets = created.ToArray(); journal.createdFolders = folders.ToArray();
            journal.resources = Resources(created,folders); Save(backup,journal);
            try
            {
                foreach (Ramp ramp in reused == null ? plan.ramps : Array.Empty<Ramp>())
                {
                    EnsureFolder(Parent(ramp.assetPath), folders);
                    Texture2D texture;
                    if (ramp.isPackagePreset)
                    {
                        // Pure native textures keep optional LWGUI absent installations independent of editor authoring types.
                        texture = new Texture2D(16, 1, TextureFormat.RGBAHalf, false, true)
                        { name = "HoShadowResponse.Weights", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0 };
                        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); texture.Apply(false, false);
                        AssetDatabase.CreateAsset(texture, ramp.assetPath);
                        // CreateAsset initially assigns the file basename to a main object.
                        // The stable encoding marker is serialized after creation, just as on editable subassets.
                        texture.name = "HoShadowResponse.Weights";
                        EditorUtility.SetDirty(texture);
                    }
                    else
                    {
                        var asset = ShadowRampStorage.CreateWeighted(ramp.assetPath, Gradient(ramp), Width(ramp), ShadowRampSampling.Endpoints);
                        texture = asset.LookupTexture;
                    }
                    created.Add(ramp.assetPath);
                    AssetDatabase.SaveAssetIfDirty(texture);
                    if (!ramp.isPackagePreset)
                    {
                        string recipePath = ramp.assetPath + ".recipe.json";
                        WriteNew(Absolute(recipePath), new Recipe { ramp = ramp, materials = plan.rows.Where(r => r.rampAssetPath == ramp.assetPath).Select(r => new SourceRecipe {
                            sourceGuid=r.sourceGuid,sourcePath=r.sourcePath,sourceSha256=r.sourceSha256,targetGuid=r.targetGuid,targetPath=r.targetPath,activeShadow=r.activeShadow,
                            originalShadowInputs=r.sourceProperties.Where(p=>p.name.StartsWith("_Shadow",StringComparison.Ordinal)||p.name.StartsWith("_AO",StringComparison.Ordinal)||p.name=="_BackfaceForceShadow").ToArray() }).ToArray() });
                        AssetDatabase.ImportAsset(recipePath, ImportAssetOptions.ForceSynchronousImport);
                        created.Add(recipePath);
                    }
                    journal.createdAssets = created.ToArray(); journal.createdFolders = folders.ToArray();
                    journal.resources = Resources(created, folders); Save(backup, journal);
                }
                foreach (var item in journal.rows)
                {
                    Before(item.row);
                    var material = Target(item.row);
                    var texture = TextureAt(item.row.rampAssetPath);
                    Bind(material, item.row, texture);
                    Sync(material, item.row.targetFamily);
                    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                    AssetDatabase.ImportAsset(item.row.targetPath, ImportAssetOptions.ForceSynchronousImport);
                    Check(item);
                    item.afterSha = Hash(Absolute(item.row.targetPath)); item.afterMetaSha = Hash(Absolute(item.row.targetPath + ".meta"));
                    item.rampLocalId = LocalId(texture);
                    journal.state = "Applying"; Save(backup, journal);
                }
                journal.resources = Resources(created, folders); journal.state = "Verified"; Save(backup, journal);
                Debug.Log("HO_SHADOW_RECOVERY_APPLY_OK targets=" + journal.rows.Length + " rampAssets=" + plan.ramps.Length + " createdResources=" + created.Count + " backup=" + backup);
            }
            catch
            {
                // Preserve any generated curves for inspection. Roll back only precisely backed-up target bytes.
                foreach (var item in journal.rows)
                {
                    File.Copy(Path.Combine(backup, item.row.targetGuid + ".mat"), Absolute(item.row.targetPath), true);
                    File.Copy(Path.Combine(backup, item.row.targetGuid + ".meta"), Absolute(item.row.targetPath + ".meta"), true);
                    AssetDatabase.ImportAsset(item.row.targetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                journal.createdAssets = created.ToArray(); journal.createdFolders = folders.ToArray(); journal.resources = Resources(created, folders);
                journal.state = "RolledBack"; Save(backup, journal); throw;
            }
        }

        public static void RunVerify()
        {
            string backup = External("HO_SHADOW_RECOVERY_BACKUP", false); var journal = ReadJournal(backup);
            Need(journal.state == "Verified", "Only a completed transaction can be verified.");
            CheckResourceStamps(journal);
            foreach (var item in journal.rows)
            {
                Need(Hash(Absolute(item.row.targetPath)) == item.afterSha && Hash(Absolute(item.row.targetPath + ".meta")) == item.afterMetaSha, "Target bytes changed: " + item.row.targetPath);
                Need(Hash(Path.Combine(backup, item.row.targetGuid + ".mat")) == item.row.targetSha256, "Backup SHA changed.");
                Need(Hash(Path.Combine(backup, item.row.targetGuid + ".meta")) == item.row.targetMetaSha256, "Backup meta SHA changed; restore must preserve the material GUID.");
                Check(item);
                Need(LocalId(TextureAt(item.row.rampAssetPath)) == item.rampLocalId, "LUT local ID changed.");
            }
            Debug.Log("HO_SHADOW_RECOVERY_VERIFY_OK targets=" + journal.rows.Length + " resources=" + journal.resources.Length);
        }

        public static void RunRestore()
        {
            string backup = External("HO_SHADOW_RECOVERY_BACKUP", false); var journal = ReadJournal(backup);
            Need(journal.state == "Verified", "Restore requires a completed transaction.");
            RunVerify();
            foreach (var item in journal.rows)
            {
                File.Copy(Path.Combine(backup, item.row.targetGuid + ".mat"), Absolute(item.row.targetPath), true);
                File.Copy(Path.Combine(backup, item.row.targetGuid + ".meta"), Absolute(item.row.targetPath + ".meta"), true);
                AssetDatabase.ImportAsset(item.row.targetPath, ImportAssetOptions.ForceSynchronousImport);
                Need(Hash(Absolute(item.row.targetPath)) == item.row.targetSha256 && Hash(Absolute(item.row.targetPath + ".meta")) == item.row.targetMetaSha256, "Exact restore failed.");
            }
            // Curves intentionally remain as unused authored resources; restoring materials never deletes reused assets.
            journal.state = "Restored"; Save(backup, journal);
            Debug.Log("HO_SHADOW_RECOVERY_RESTORE_OK targets=" + journal.rows.Length);
        }

        private static void Bind(Material material, Row row, Texture2D ramp)
        {
            material.SetTexture(RampProperty(row.targetFamily), ramp);
            material.SetFloat("_ShadowResponseEnabled", row.activeShadow ? 1 : 0);
            material.SetVector("_ShadowResponseNormal", new Vector4(F(row,"_ShadowNormalStrength"),F(row,"_Shadow2ndNormalStrength"),F(row,"_Shadow3rdNormalStrength"),0));
            material.SetVector("_ShadowResponseReceive", new Vector4(F(row,"_ShadowReceive"),F(row,"_Shadow2ndReceive"),F(row,"_Shadow3rdReceive"),0));
            material.SetVector("_ShadowResponseParams", new Vector4(F(row,"_ShadowStrength"),F(row,"_ShadowMainStrength"),F(row,"_ShadowEnvStrength"),F(row,"_BackfaceForceShadow")));
            material.SetVector("_ShadowResponseMaskParams", new Vector4(F(row,"_ShadowMaskType"),F(row,"_ShadowFlatBorder"),F(row,"_ShadowFlatBlur"),F(row,"_AOStrength")));
            material.SetVector("_ShadowResponseAOParams", new Vector4(F(row,"_AOSource"),F(row,"_AORealtimeWeight"),F(row,"_AOLevel"),F(row,"_AOContrast")));
            foreach (var mapping in new[] { ("Color1","_ShadowColor"), ("Color2","_Shadow2ndColor"), ("Color3","_Shadow3rdColor"), ("BorderColor","_ShadowBorderColor") })
            {
                var property = P(row, mapping.Item2); material.SetColor("_ShadowResponse" + mapping.Item1, new Color(property.x,property.y,property.z,property.w));
            }
            string[] names = { "_ShadowStrengthMask", "_ShadowReceiveMask", "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex", "_AOMap", "_AOMask" };
            for (int i=0;i<names.Length;++i) material.SetTexture("_ShadowResponse"+ImportedTextures[i], SourceTexture(P(row,names[i])));
        }
        private static void Check(Applied item)
        {
            var material=Target(item.row);
            Need(LocalId(material)==item.localId && Guid(material.shader)==item.shaderGuid, "Material or shader identity changed.");
            Need(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(material.shader)).ToString()==item.shaderHash,"Shader dependencies changed; regenerate preview.");
            string currentState = State(material);
            Need(currentState==item.unaffectedState,"Unrelated material fields changed: "+item.row.targetPath+" "+Difference(item.unaffectedState,currentState));
            Need(material.GetTexture(RampProperty(item.row.targetFamily))==TextureAt(item.row.rampAssetPath),"Wrong restored Ramp binding.");
            Need(material.GetFloat("_ShadowResponseMode")==1 && material.GetFloat("_ShadowResponseEnabled")== (item.row.activeShadow?1:0),"Wrong response encoding or enabled state.");
            // Compare all imported values against a detached reference without modifying the live material.
            var reference=new Material(material);
            try
            {
                Bind(reference,item.row,TextureAt(item.row.rampAssetPath));
                for(int i=0;i<material.shader.GetPropertyCount();++i)
                {
                    string name=material.shader.GetPropertyName(i); if(!name.StartsWith("_ShadowResponse",StringComparison.Ordinal))continue;
                    var type=material.shader.GetPropertyType(i);
                    bool same=type switch {
                        UnityEngine.Rendering.ShaderPropertyType.Color=>material.GetColor(name)==reference.GetColor(name),
                        UnityEngine.Rendering.ShaderPropertyType.Vector=>material.GetVector(name)==reference.GetVector(name),
                        UnityEngine.Rendering.ShaderPropertyType.Texture=>material.GetTexture(name)==reference.GetTexture(name),
                        _=>material.GetFloat(name)==reference.GetFloat(name)};
                    Need(same,"Imported input mismatch: "+name+" "+item.row.targetPath);
                }
            }
            finally { Object.DestroyImmediate(reference); }
        }
        private static void ValidatePlan(Plan plan,bool fresh)
        {
            Need(plan.rows?.Length>0 && plan.ramps!=null && plan.neutralPresets!=null,"Incomplete recovery plan.");
            Need(plan.rows.Select(r=>r.targetGuid).Distinct().Count()==plan.rows.Length,"Duplicate target.");
            Need(Hash(plan.inventoryPath)==plan.inventorySha256,"Source inventory changed.");
            Need(plan.sourceMathFiles?.Length >= 2,"Pin source shadow math includes in the recovery plan.");
            foreach(var source in plan.sourceMathFiles)Need(Hash(source.path)==source.sha256,"Original source math changed: "+source.path);
            if(!string.IsNullOrEmpty(plan.provenancePath))Need(Hash(plan.provenancePath)==plan.provenanceSha256,"Source provenance changed.");
            var ramps=plan.ramps.ToDictionary(r=>r.assetPath,StringComparer.Ordinal);
            Need(ramps.Count==plan.ramps.Length,"Duplicate output Ramp path.");
            foreach(var ramp in ramps.Values)
            {
                AssetPath(ramp.assetPath); Need(!ramp.isPackagePreset||ramp.isNeutral,"Only generic neutral package presets are generated by this transaction.");
                var consumers=plan.rows.Where(r=>r.rampAssetPath==ramp.assetPath).Select(r=>r.targetGuid).OrderBy(g=>g,StringComparer.Ordinal).ToArray();
                Need(consumers.Length>0,"No target uses planned Ramp: "+ramp.assetPath);
                Need(consumers.SequenceEqual((ramp.targetGuids??Array.Empty<string>()).OrderBy(g=>g,StringComparer.Ordinal)),"Ramp consumer mapping changed: "+ramp.assetPath);
                if(fresh)Need(!File.Exists(Absolute(ramp.assetPath))&&!File.Exists(Absolute(ramp.assetPath+".meta"))&&!File.Exists(Absolute(ramp.assetPath+".recipe.json")),"Preserve existing Ramp: "+ramp.assetPath);
            }
            foreach(var row in plan.rows)
            {
                Need(ramps.ContainsKey(row.rampAssetPath),"Missing curve recipe."); Before(row);
                Need(row.rampIsPackagePreset==ramps[row.rampAssetPath].isPackagePreset,"Output ownership changed.");
                Need(row.activeShadow ? !ramps[row.rampAssetPath].isNeutral&&row.shapeSignature==ramps[row.rampAssetPath].shapeSignature : ramps[row.rampAssetPath].isNeutral,"Output shape mapping changed.");
                Need(F(row,"_ShadowColorType")==0,"A spatial 3D color LUT requires its own evaluator; do not flatten it.");
                Need(string.IsNullOrEmpty(P(row,"_ShadowBlurMask").textureGuid),"A spatial blur mask cannot be represented by one shared visibility curve.");
                Need(F(row,"_ShadowStrengthMaskLOD")==0&&F(row,"_ShadowBlurMaskLOD")==0,"Explicit mask mip levels require an additional sampling contract.");
                Need(Hash(row.sourceBackupPath)==row.sourceBackupSha256 && row.sourceSha256==row.sourceBackupSha256,"Original source backup changed.");
                Need(Hash(row.sourceShaderFilePath)==row.sourceShaderSha256,"Original source shader changed.");
                foreach(var property in row.sourceProperties.Where(p=>!string.IsNullOrEmpty(p.textureGuid)))
                {
                    var texture=SourceTexture(property); Need(texture!=null,"Missing source texture: "+property.name);
                    if(!string.IsNullOrEmpty(property.textureSha256))Need(Hash(Absolute(property.texturePath))==property.textureSha256,"Source texture bytes changed.");
                    if(!string.IsNullOrEmpty(property.textureMetaSha256))Need(Hash(Absolute(property.texturePath+".meta"))==property.textureMetaSha256,"Source texture meta changed.");
                }
                var material=Target(row);
                foreach(string name in new[]{"_ShadowResponseMode","_ShadowResponseEnabled","_ShadowResponseAOParams",RampProperty(row.targetFamily)})Need(material.HasProperty(name),"Shader does not implement response contract: "+name);
            }
        }
        private static Applied Capture(Row row)
        {
            var material=Target(row);
            return new Applied {row=row,unaffectedState=State(material),localId=LocalId(material),shaderGuid=Guid(material.shader),shaderHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(material.shader)).ToString()};
        }
        private static void Before(Row row)
        {
            AssetPath(row.targetPath); Clean(row.targetPath);
            Need(Hash(Absolute(row.targetPath))==row.targetSha256 && Hash(Absolute(row.targetPath+".meta"))==row.targetMetaSha256,"Material changed since recovery plan: "+row.targetPath);
            Target(row);
        }
        private static Material Target(Row row)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(row.targetPath);
            Need(material && material.shader && Guid(material)==row.targetGuid,"Target identity changed: "+row.targetPath);
            string expected=row.targetFamily switch {"Cloth"=>"HoShader/Cloth URP","Hair"=>"HoShader/Hair URP","Skin"=>"HoShader/Skin URP","Eye"=>"HoShader/Eye URP","Environment"=>"HoShader/Environment URP",_=>throw new InvalidOperationException("Unknown family "+row.targetFamily)};
            Need(material.shader.name==expected,"Target family changed: "+row.targetPath+" expected="+expected+" actual="+material.shader.name);return material;
        }
        private static void Sync(Material material,string family)
        {
            string name=family switch {"Cloth"=>"HoShader.ClothURP.Editor.ClothMaterialUtility","Hair"=>"HoShader.HairURP.Editor.HairMaterialUtility","Skin"=>"HoShader.SkinStylizedURP.Editor.SkinMaterialUtility","Eye"=>"HoShader.SkinStylizedURP.Editor.EyeMaterialUtility","Environment"=>"HoShader.EnvironmentURP.Editor.EnvironmentMaterialUtility",_=>throw new InvalidOperationException("Unknown family")};
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
            Need(type!=null,"Missing stable material Utility: "+name);
            type.GetMethod(family=="Hair"?"SyncSurfaceMode":"SyncMaterial",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{material});
        }
        private static string RampProperty(string family)=>family=="Skin"?"_RampMap":"_ShadowRamp";
        private static SourceProperty P(Row row,string name)=>row.sourceProperties.SingleOrDefault(p=>p.name==name)??throw new InvalidOperationException("Missing effective source property "+name+" on "+row.sourcePath);
        private static float F(Row row,string name)=>P(row,name).scalar;
        private static Texture2D SourceTexture(SourceProperty property)
        {
            if(string.IsNullOrEmpty(property.textureGuid)||property.textureGuid.All(c=>c=='0'))return null;
            string path=AssetDatabase.GUIDToAssetPath(property.textureGuid);Need(path==property.texturePath,"Source texture GUID/path mismatch.");
            var texture=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().SingleOrDefault(t=>LocalId(t)==property.textureLocalId);
            Need(texture,"Source texture local ID changed: "+property.texturePath);return texture;
        }
        private static Texture2D TextureAt(string path)
        {
            var asset=AssetDatabase.LoadAssetAtPath<LwguiShadowRampAsset>(path);
            var texture=asset?asset.LookupTexture:AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Need(texture&&texture.name=="HoShadowResponse.Weights"&&!texture.isDataSRGB&&texture.format==TextureFormat.RGBAHalf,"Invalid weight LUT: "+path+" name="+(texture?texture.name:"<null>")+" format="+(texture?texture.format.ToString():"<null>"));return texture;
        }
        public static int Width(Ramp ramp)
        {
            float smallest=Mathf.Min(ramp.blur1,ramp.blur2,ramp.blur3);
            return smallest<=.001f?16384:smallest<.02f?4096:1024;
        }
        public static LwguiGradient Gradient(Ramp ramp)
        {
            var result=new LwguiGradient();
            result.rawCurves[0]=BandCurve(ramp.border1,ramp.blur1,0);
            result.rawCurves[1]=BandCurve(ramp.border2,ramp.blur2,0);
            result.rawCurves[2]=BandCurve(ramp.border3,ramp.blur3,0);
            result.rawCurves[3]=BandCurve(ramp.border1,ramp.blur1,ramp.borderRange);return result;
        }
        public static AnimationCurve BandCurve(float border,float blur,float range)
        {
            float lower=Mathf.Clamp01(border-blur*.5f-range),upper=Mathf.Clamp01(border+blur*.5f);
            var keys=new List<Keyframe>();
            if(upper<=lower)
            {
                if(lower>0)keys.Add(new Keyframe(0,0,0,float.PositiveInfinity));
                keys.Add(new Keyframe(lower,1,float.PositiveInfinity,0));
                if(lower<1)keys.Add(new Keyframe(1,1,0,0));
            }
            else
            {
                float slope=1/(upper-lower);
                if(lower>0)keys.Add(new Keyframe(0,0,0,0));
                keys.Add(new Keyframe(lower,0,0,slope));keys.Add(new Keyframe(upper,1,slope,0));
                if(upper<1)keys.Add(new Keyframe(1,1,0,0));
            }
            return new AnimationCurve(keys.ToArray()){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        // Canonical comparison preserves declared effective properties, raw retired properties and material variant ownership.
        private static string State(Material material)
        {
            using var so=new SerializedObject(material);var fields=new List<Field>();var it=so.GetIterator();bool enter=true;
            while(it.Next(enter)){enter=true;if(it.propertyPath=="m_SavedProperties"){enter=false;continue;}if(it.propertyType==SerializedPropertyType.Generic)continue;fields.Add(new Field{path=it.propertyPath,type=it.propertyType.ToString(),value=Value(it)});if(it.propertyType==SerializedPropertyType.ObjectReference)enter=false;}
            var declared=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<material.shader.GetPropertyCount();++i)
            {
                string name=material.shader.GetPropertyName(i);declared.Add(name);if(Changed(name))continue;
                var type=material.shader.GetPropertyType(i);string value=type switch {
                    UnityEngine.Rendering.ShaderPropertyType.Color=>JsonUtility.ToJson(material.GetColor(name)),
                    UnityEngine.Rendering.ShaderPropertyType.Vector=>JsonUtility.ToJson(material.GetVector(name)),
                    UnityEngine.Rendering.ShaderPropertyType.Texture=>TextureValue(material,name),
                    UnityEngine.Rendering.ShaderPropertyType.Int=>material.GetInteger(name).ToString(CultureInfo.InvariantCulture),
                    _=>material.GetFloat(name).ToString("R",CultureInfo.InvariantCulture)};
                fields.Add(new Field{path="declared/"+name,type=type.ToString(),value=value});
            }
            foreach(string group in new[]{"m_TexEnvs","m_Floats","m_Ints","m_Colors"})
            {
                var array=so.FindProperty("m_SavedProperties."+group);if(array==null)continue;
                for(int i=0;i<array.arraySize;++i)
                {
                    var entry=array.GetArrayElementAtIndex(i);string name=entry.FindPropertyRelative("first").stringValue;if(Changed(name)||declared.Contains(name))continue;
                    var value=entry.FindPropertyRelative("second");var p=value.Copy();var end=value.GetEndProperty();bool children=true;
                    do{if(p.propertyType!=SerializedPropertyType.Generic)fields.Add(new Field{path=group+"/"+name+"/"+p.propertyPath.Substring(value.propertyPath.Length),type=p.propertyType.ToString(),value=Value(p)});children=p.propertyType!=SerializedPropertyType.ObjectReference;}
                    while(p.Next(children)&&!SerializedProperty.EqualContents(p,end));
                }
            }
            return JsonUtility.ToJson(new Fields{fields=fields.OrderBy(f=>f.path,StringComparer.Ordinal).ToArray()});
        }
        private static string TextureValue(Material m,string name)
        {var texture=m.GetTexture(name);return(texture?Guid(texture)+":"+LocalId(texture):"null")+";scale="+JsonUtility.ToJson(m.GetTextureScale(name))+";offset="+JsonUtility.ToJson(m.GetTextureOffset(name));}
        private static bool Changed(string name)=>name.StartsWith("_ShadowResponse",StringComparison.Ordinal)||name=="_ShadowRamp"||name=="_RampMap"||name=="_UseRamp";
        private static string Difference(string before,string after)
        {
            var first=JsonUtility.FromJson<Fields>(before).fields.ToDictionary(f=>f.path,f=>f.type+":"+f.value);
            var second=JsonUtility.FromJson<Fields>(after).fields.ToDictionary(f=>f.path,f=>f.type+":"+f.value);
            string key=first.Keys.Concat(second.Keys).Distinct().OrderBy(p=>p,StringComparer.Ordinal).FirstOrDefault(p=>!first.TryGetValue(p,out string a)||!second.TryGetValue(p,out string b)||a!=b);
            return key==null?"":"field="+key+" before="+(first.TryGetValue(key,out string x)?x:"<missing>")+" after="+(second.TryGetValue(key,out string y)?y:"<missing>");
        }
        private static string Value(SerializedProperty p)=>p.propertyType switch {
            SerializedPropertyType.ObjectReference=>p.objectReferenceValue?Guid(p.objectReferenceValue)+":"+LocalId(p.objectReferenceValue):"null",
            SerializedPropertyType.String=>p.stringValue,SerializedPropertyType.Boolean=>p.boolValue?"1":"0",SerializedPropertyType.Integer=>p.longValue.ToString(CultureInfo.InvariantCulture),SerializedPropertyType.Float=>p.doubleValue.ToString("R",CultureInfo.InvariantCulture),
            SerializedPropertyType.Color=>JsonUtility.ToJson(p.colorValue),SerializedPropertyType.Vector2=>JsonUtility.ToJson(p.vector2Value),SerializedPropertyType.Vector3=>JsonUtility.ToJson(p.vector3Value),SerializedPropertyType.Vector4=>JsonUtility.ToJson(p.vector4Value),
            SerializedPropertyType.Enum or SerializedPropertyType.ArraySize or SerializedPropertyType.LayerMask=>p.intValue.ToString(CultureInfo.InvariantCulture),_=>throw new InvalidOperationException("Unsupported field "+p.propertyPath+" "+p.propertyType)};
        private static Plan ReadPlan()
        {
            Need(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode,"Stable Edit Mode required.");
            Need(Path.GetFullPath(Required("HO_SHADOW_RECOVERY_PROJECT"))==Root,"Explicit project pin required.");
            var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(External("HO_SHADOW_RECOVERY_PLAN",false)));
            Need(plan?.schemaVersion==1&&plan.kind=="HoShader.ShadowResponseRecovery"&&Path.GetFullPath(plan.projectRoot)==Root,"Wrong recovery plan project or schema.");return plan;
        }
        private static Journal ReadJournal(string backup)
        {Need(Path.GetFullPath(Required("HO_SHADOW_RECOVERY_PROJECT"))==Root,"Explicit project pin required.");var value=JsonUtility.FromJson<Journal>(File.ReadAllText(Path.Combine(backup,"journal.json")));Need(value?.version==1&&value.projectRoot==Root,"Wrong recovery journal.");return value;}
        private static Stamp[] Resources(IEnumerable<string> assets,IEnumerable<string> folders)=>assets.SelectMany(p=>new[]{p,p+".meta"}).Concat(folders.Select(p=>p+".meta")).Where(p=>File.Exists(Absolute(p))).Select(p=>new Stamp{path=p,sha=Hash(Absolute(p))}).ToArray();
        private static void CheckResourceStamps(Journal journal){foreach(var stamp in journal.resources)Need(Hash(Absolute(stamp.path))==stamp.sha,"Authored resources changed: "+stamp.path);}
        private static void EnsureFolder(string path,List<string> folders)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            AssetPath(path);string parent=Parent(path);if(parent!="Assets"&&!parent.StartsWith("Packages/com.hoshader.",StringComparison.Ordinal))EnsureFolder(parent,folders);
            else if(!AssetDatabase.IsValidFolder(parent))EnsureFolder(parent,folders);
            Need(!File.Exists(Absolute(path))&&!File.Exists(Absolute(path+".meta")),"Preserve existing folder metadata: "+path);
            Need(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))),"Cannot create folder: "+path);folders.Add(path);
        }
        private static void Clean(string path){var importer=AssetImporter.GetAtPath(path);Need(!importer||!EditorUtility.IsDirty(importer),"Unsaved importer: "+path);foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))Need(!EditorUtility.IsDirty(asset),"Unsaved asset: "+path);}
        private static void AssetPath(string path){Need((path.StartsWith("Assets/",StringComparison.Ordinal)||path.StartsWith("Packages/com.hoshader.",StringComparison.Ordinal))&&!path.Contains('\\')&&!path.Split('/').Any(p=>p==".."||p=="."||p==""),"Invalid asset path: "+path);}
        private static string Absolute(string path)
        {
            if(!path.StartsWith("Packages/",StringComparison.Ordinal))return Path.GetFullPath(Path.Combine(Root,path));
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);Need(package!=null,"Package path unavailable: "+path);
            return Path.GetFullPath(Path.Combine(package.resolvedPath,path.Substring(("Packages/"+package.name+"/").Length)));
        }
        private static string Parent(string path)=>Path.GetDirectoryName(path).Replace('\\','/');
        private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        private static string Required(string key)=>Environment.GetEnvironmentVariable(key)??throw new InvalidOperationException("Set "+key);
        private static string External(string key,bool fresh){string path=Path.GetFullPath(Required(key));Need(!path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Use an external plan or backup.");if(fresh)Need(!File.Exists(path)&&!Directory.Exists(path),"Preserve existing evidence: "+path);return path;}
        private static void WriteNew(string path,object value){using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write);using var writer=new StreamWriter(file,new UTF8Encoding(false));writer.Write(JsonUtility.ToJson(value,true));}
        private static void Save(string backup,Journal journal){string path=Path.Combine(backup,"journal.json"),temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(journal,true),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        private static string Guid(Object value){Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string guid,out long _),"Persistent object required.");return guid;}
        private static long LocalId(Object value){Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string _,out long id),"Persistent object required.");return id;}
        private static string Hash(string path){using var hash=SHA256.Create();using var file=File.OpenRead(path);return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
