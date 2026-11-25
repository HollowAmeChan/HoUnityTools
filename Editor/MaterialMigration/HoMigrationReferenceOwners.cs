using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // Resolves authoring ownership only. The caller owns approval, backup, final
    // target creation, scene/prefab writes and the single encompassing transaction.
    public static class HoMigrationReferenceOwners
    {
        [Serializable] public sealed class Report
        {
            public int schemaVersion=1;
            public string projectRoot, planFingerprint, coreHash;
            public bool retainMixedModelAnchors;
            public Intent[] intents=Array.Empty<Intent>();
            public SlotOwner[] slotOwners=Array.Empty<SlotOwner>();
            public ImporterRemap[] remaps=Array.Empty<ImporterRemap>();
            public ModelDefault[] modelDefaults=Array.Empty<ModelDefault>();
            public Wrapper[] wrappers=Array.Empty<Wrapper>();
            public Container[] containers=Array.Empty<Container>();
            public string[] blockers=Array.Empty<string>();
        }
        [Serializable] public sealed class Container
        {public string path,guid,assetSha256,metaSha256;}
        [Serializable] public sealed class Node
        {
            public string containerPath,rendererGlobalId,rendererPath,rendererType;
            public int componentIndex,slot;
            public string materialGuid;public long materialLocalId;
            public bool materialOverride, model;
        }
        [Serializable] public sealed class Intent
        {
            public int index;
            public HoMaterialMigrationSlotAction action;
            public Node[] chain=Array.Empty<Node>();
            public string ownerId, resolution;
        }
        [Serializable] public sealed class SlotOwner
        {
            public string id;
            public HoMaterialMigrationSlotAction action;
            public int[] intentIndices=Array.Empty<int>();
        }
        [Serializable] public sealed class Identifier
        {
            public string sourceType,sourceName,provenance;
            public bool hadExplicitRemap;
            public string expectedGuid;public long expectedLocalId;
            public int pmxMaterialIndex=-1;
            public string sourceMeshGuid;public long sourceMeshLocalId;
        }
        [Serializable] public sealed class ImporterRemap
        {
            public string id,modelPath,importerType,targetSourceGuid,targetFamily,targetAssetPath;
            public Identifier identifier;
            public int[] intentIndices=Array.Empty<int>();
        }
        [Serializable] public sealed class ModelDefault
        {
            public int intentIndex;
            public string modelPath,requestedFamily,effectiveFamily,targetSourceGuid,targetAssetPath,reason;
            public bool requestedSubdivisionSatisfied,wrapperRequired;
            public Identifier[] candidates=Array.Empty<Identifier>();
        }
        [Serializable] public sealed class Wrapper
        {public string modelPath,suggestedAssetPath;public int[] intentIndices=Array.Empty<int>();public string status="NotCreated";}
        [Serializable] public sealed class Validation
        {public int verifiedIntentCount,declaredModelDefaultExceptions;public string[] blockers=Array.Empty<string>();}

        public static Report Resolve(HoMaterialMigrationPlan plan,bool retainMixedModelAnchors=false)
        {
            if(plan==null||plan.rows==null||plan.slotActions==null)throw new ArgumentException("A complete material plan is required.");
            if(Path.GetFullPath(plan.projectRoot)!=ProjectRoot)throw new InvalidOperationException("Owner resolution must run in the plan project or an explicitly rebound private copy.");
            var result=new Report {projectRoot=ProjectRoot,planFingerprint=PlanFingerprint(plan),retainMixedModelAnchors=retainMixedModelAnchors};
            using var read=new ReadScope();
            var errors=new List<string>();var stamps=new Dictionary<string,Container>(StringComparer.Ordinal);
            var intents=new List<Intent>();
            for(int index=0;index<plan.slotActions.Length;index++)
            {
                var action=Clone(plan.slotActions[index]);action.reviewed=false;
                var intent=new Intent {index=index,action=action};intents.Add(intent);
                try
                {
                    read.Visit(action,renderer=>
                    {
                        RequireMaterial(renderer,action.slot,action.expectedMaterialGuid,action.expectedMaterialLocalId);
                        var nodes=new List<Node>();Renderer current=renderer;
                        var seen=new HashSet<string>(StringComparer.Ordinal);
                        while(current!=null)
                        {
                            string path=nodes.Count==0?action.containerPath:AssetDatabase.GetAssetPath(current);
                            if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Source renderer has no persistent authoring container.");
                            var node=Observe(current,path,action.slot);
                            if(!seen.Add(Key(node)))throw new InvalidOperationException("Prefab source chain is cyclic.");
                            nodes.Add(node);if(!stamps.ContainsKey(path))stamps.Add(path,Stamp(path));
                            current=PrefabUtility.GetCorrespondingObjectFromSource(current);
                        }
                        intent.chain=nodes.ToArray();
                        if(!string.IsNullOrEmpty(action.rendererGlobalId)&&action.rendererGlobalId!=nodes[0].rendererGlobalId)
                            throw new InvalidOperationException("Renderer GlobalObjectId differs from the observed native selector.");
                    });
                    var stamp=stamps[action.containerPath];
                    if(stamp.guid!=action.containerGuid||stamp.assetSha256!=action.containerSha256||stamp.metaSha256!=action.containerMetaSha256)
                        throw new InvalidOperationException("Expanded intent container bytes/GUID differ from the plan.");
                }
                catch(Exception error){intent.resolution="Blocked";errors.Add("Intent "+index+": "+error.Message);}
            }
            result.intents=intents.ToArray();
            var byNode=new Dictionary<string,Intent>(StringComparer.Ordinal);
            foreach(var intent in intents.Where(i=>i.chain.Length>0&&i.resolution!="Blocked"))
            {
                string key=Key(intent.chain[0]);
                if(byNode.TryGetValue(key,out var other))
                {
                    if(Target(other.action)!=Target(intent.action))errors.Add("Conflicting expanded intents for the same native slot: "+key);
                }
                else byNode.Add(key,intent);
            }
            var owners=new Dictionary<string,SlotOwner>(StringComparer.Ordinal);
            var ownerIndices=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            foreach(var intent in intents.Where(i=>i.chain.Length>0&&!i.chain[0].model&&i.resolution!="Blocked"))
            {
                Intent owner=FindOwner(intent,new HashSet<int>());
                string id="slot:"+Key(owner.chain[0]);
                if(owners.TryGetValue(id,out var existing)&&Target(existing.action)!=Target(intent.action))
                {errors.Add("Owner target conflict: "+id);intent.resolution="Blocked";continue;}
                if(!owners.ContainsKey(id))
                {
                    var action=Clone(owner.action);
                    action.kind=plan.rows.Single(r=>r.sourceGuid==action.targetSourceGuid).targetFamily==action.targetFamily?"retain-anchor":"assign-fork";
                    owners.Add(id,new SlotOwner {id=id,action=action});ownerIndices.Add(id,new List<int>());
                }
                ownerIndices[id].Add(intent.index);intent.ownerId=id;intent.resolution=owner.index==intent.index?"AuthoringSlot":"InheritedFromOwner";
            }
            foreach(var pair in owners)pair.Value.intentIndices=ownerIndices[pair.Key].OrderBy(i=>i).ToArray();
            result.slotOwners=owners.Values.OrderBy(o=>o.action.containerPath,StringComparer.Ordinal).ThenBy(o=>o.action.rendererPath,StringComparer.Ordinal).ThenBy(o=>o.action.slot).ToArray();
            var remaps=new List<ImporterRemap>();var defaults=new List<ModelDefault>();var wrappers=new List<Wrapper>();
            foreach(var group in intents.Where(i=>i.chain.Length>0&&i.chain[0].model&&i.resolution!="Blocked").GroupBy(i=>i.action.containerPath))
            {
                var importer=AssetImporter.GetAtPath(group.Key);
                if(importer==null){errors.Add("Missing model importer: "+group.Key);continue;}
                foreach(var materialGroup in ModelGroups(importer,group))
                {
                    Intent first=materialGroup.First();var all=materialGroup.ToArray();
                    Identifier[] candidates=IdentifiersForIntent(importer,first);
                    string[] targets=all.Select(i=>Target(i.action)).Distinct().ToArray();
                    bool complete=AllModelUsesCovered(group.Key,first.action.expectedMaterialGuid,first.action.expectedMaterialLocalId,all,candidates);
                    // Several explicit aliases may safely move together only if every
                    // affected use has the same requested target. Never choose one by GUID.
                    bool executable=targets.Length==1&&complete&&candidates.Length>0
                        &&candidates.All(c=>c.hadExplicitRemap||c.provenance.StartsWith("PMX report exact",StringComparison.Ordinal)||c.provenance.StartsWith("ModelImporter.sourceMaterials exact embedded",StringComparison.Ordinal));
                    var anchor=plan.rows.SingleOrDefault(r=>r.sourceGuid==first.action.expectedMaterialGuid);
                    bool noOp=anchor!=null&&targets.Length==1&&all.All(i=>i.action.targetSourceGuid==anchor.sourceGuid&&i.action.targetFamily==anchor.targetFamily);
                    if(executable&&!noOp)
                    {
                        foreach(var identifier in candidates)
                            remaps.Add(new ImporterRemap {id="remap:"+group.Key+":"+identifier.sourceType+":"+identifier.sourceName,modelPath=group.Key,
                                importerType=importer.GetType().FullName,identifier=identifier,targetSourceGuid=first.action.targetSourceGuid,targetFamily=first.action.targetFamily,
                                targetAssetPath=first.action.targetAssetPath,intentIndices=all.Select(i=>i.index).ToArray()});
                        foreach(var intent in all){intent.ownerId="remap:"+group.Key;intent.resolution="ImporterRemap";}
                        continue;
                    }
                    bool permitted=noOp||retainMixedModelAnchors&&anchor!=null;
                    string reason=noOp?"Existing external model target is the original-GUID anchor; no importer mutation is necessary."
                        :targets.Length>1?"One imported source identifier/alias set has conflicting per-renderer families. Retain the explicit original-GUID anchor; subdivision requires a reviewed wrapper."
                        :!complete?"Not every use of this model material is represented; do not change its importer mapping globally."
                        :"The final external material does not uniquely identify its original per-slot source key; no guessed remap is performed.";
                    if(!permitted)errors.Add("Model default needs an explicit anchor/wrapper decision: "+group.Key+" "+first.action.expectedMaterialGuid);
                    foreach(var intent in all)
                    {
                        bool satisfied=anchor!=null&&intent.action.targetSourceGuid==anchor.sourceGuid&&intent.action.targetFamily==anchor.targetFamily;
                        defaults.Add(new ModelDefault {intentIndex=intent.index,modelPath=group.Key,requestedFamily=intent.action.targetFamily,
                            effectiveFamily=anchor?.targetFamily,targetSourceGuid=anchor?.sourceGuid,targetAssetPath=anchor?.assetPath,
                            requestedSubdivisionSatisfied=satisfied,wrapperRequired=!satisfied,reason=reason,candidates=candidates});
                        intent.ownerId="model-anchor:"+group.Key;intent.resolution=permitted?"ExplicitModelDefaultAnchor":"Blocked";
                    }
                }
                int[] pending=defaults.Where(d=>d.modelPath==group.Key&&d.wrapperRequired).Select(d=>d.intentIndex).ToArray();
                if(pending.Length>0)wrappers.Add(new Wrapper {modelPath=group.Key,suggestedAssetPath=Path.ChangeExtension(group.Key,null)+"__HoSpecialized.prefab",intentIndices=pending});
            }
            result.remaps=remaps.OrderBy(r=>r.id,StringComparer.Ordinal).ToArray();result.modelDefaults=defaults.OrderBy(d=>d.intentIndex).ToArray();
            result.wrappers=wrappers.ToArray();result.containers=stamps.Values.OrderBy(s=>s.path,StringComparer.Ordinal).ToArray();
            result.blockers=errors.Distinct().OrderBy(e=>e,StringComparer.Ordinal).ToArray();result.coreHash=Digest(result);return result;

            Intent FindOwner(Intent value,HashSet<int> visiting)
            {
                if(!visiting.Add(value.index))throw new InvalidOperationException("Owner recursion is cyclic.");
                if(value.chain[0].materialOverride)return value;
                for(int i=1;i<value.chain.Length;i++)
                {
                    if(value.chain[i].model)break;
                    if(!byNode.TryGetValue(Key(value.chain[i]),out var parent))
                    {if(value.chain[i].materialOverride)return value;continue;}
                    // A conflicting planned parent requires this level's explicit
                    // override, even when its current value happened to be inherited.
                    if(Target(parent.action)!=Target(value.action))return value;
                    return FindOwner(parent,visiting);
                }
                return value;
            }
        }

        public static void ValidateBefore(HoMaterialMigrationPlan plan,Report report)
        {
            RequireReport(report);if(report.planFingerprint!=PlanFingerprint(plan))throw new InvalidOperationException("Owner plan fingerprint changed.");
            var current=Resolve(plan,report.retainMixedModelAnchors);
            if(current.coreHash!=report.coreHash)throw new InvalidOperationException("Authoring ownership, source keys, source overrides or container bytes changed; regenerate preview.");
            if(report.blockers.Length>0)throw new InvalidOperationException(string.Join("\n",report.blockers));
            // Refuse pending importer edits before the encompassing transaction
            // creates resources or writes materials. Reimporting on rollback
            // cannot recover settings that were never serialized to disk.
            foreach(var remap in report.remaps)ValidateRemapBefore(remap);
        }
        public static HoMaterialMigrationSlotAction[] ExecutableSlotActions(Report report)
        {RequireReport(report);return report.slotOwners.Select(o=>Clone(o.action)).ToArray();}

        // The caller must back up every model .meta in report.remaps before this
        // method. No nested transaction or importer-profile change is performed.
        public static void ApplyRemaps(Report report,Func<string,string,Material> finalTarget)
        {
            RequireReport(report);if(report.blockers.Length>0)throw new InvalidOperationException(string.Join("\n",report.blockers));
            foreach(string path in report.remaps.Select(r=>r.modelPath).Distinct())
            {
                var stamp=report.containers.Single(c=>c.path==path);
                if(FileHash(path)!=stamp.assetSha256||FileHash(path+".meta")!=stamp.metaSha256||AssetDatabase.AssetPathToGUID(path)!=stamp.guid)
                    throw new InvalidOperationException("Model/importer bytes changed before AddRemap: "+path);
            }
            foreach(var remap in report.remaps)ValidateRemapBefore(remap);
            foreach(var group in report.remaps.GroupBy(r=>r.modelPath))
            {
                var importer=AssetImporter.GetAtPath(group.Key);
                foreach(var remap in group)
                {
                    Material target=finalTarget(remap.targetSourceGuid,remap.targetFamily);RequirePersistent(target);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),remap.identifier.sourceName),target);
                }
                EditorUtility.SetDirty(importer);importer.SaveAndReimport();
            }
        }
        public static Validation ValidateAfter(Report report,Func<string,string,Material> finalTarget)
        {
            RequireReport(report);if(report.blockers.Length>0)throw new InvalidOperationException(string.Join("\n",report.blockers));
            using var read=new ReadScope();var errors=new List<string>();int verified=0,exceptions=0;
            foreach(var intent in report.intents)
            {
                try
                {
                    var modelDefault=report.modelDefaults.SingleOrDefault(d=>d.intentIndex==intent.index);
                    string source=modelDefault?.targetSourceGuid??intent.action.targetSourceGuid;
                    string family=modelDefault?.effectiveFamily??intent.action.targetFamily;
                    Material target=finalTarget(source,family);RequirePersistent(target);
                    Identity(target,out string guid,out long localId);
                    read.Visit(intent.action,r=>RequireMaterial(r,intent.action.slot,guid,localId));
                    verified++;if(modelDefault!=null&&!modelDefault.requestedSubdivisionSatisfied)exceptions++;
                }
                catch(Exception e){errors.Add("Intent "+intent.index+": "+e.Message);}
            }
            foreach(var remap in report.remaps)
            {
                try
                {
                    var importer=AssetImporter.GetAtPath(remap.modelPath);var key=new AssetImporter.SourceAssetIdentifier(typeof(Material),remap.identifier.sourceName);
                    if(importer==null||!importer.GetExternalObjectMap().TryGetValue(key,out Object value)||value!=finalTarget(remap.targetSourceGuid,remap.targetFamily))
                        throw new InvalidOperationException("Saved exact importer key differs: "+remap.modelPath+" "+key.name);
                }
                catch(Exception e){errors.Add(e.Message);}
            }
            return new Validation {verifiedIntentCount=verified,declaredModelDefaultExceptions=exceptions,blockers=errors.ToArray()};
        }
        public static void RestoreRemapMetadata(Report report,Func<string,byte[]> originalMeta)
        {
            RequireReport(report);var paths=report.remaps.Select(r=>r.modelPath).Distinct().ToArray();
            var originals=new Dictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(string path in paths)
            {
                var stamp=report.containers.Single(c=>c.path==path);byte[] bytes=originalMeta(path+".meta");
                if(Hash(bytes)!=stamp.metaSha256||FileHash(path)!=stamp.assetSha256||AssetDatabase.AssetPathToGUID(path)!=stamp.guid)
                    throw new InvalidOperationException("Original importer backup/model identity differs: "+path);
                originals.Add(path,bytes);
            }
            foreach(string path in paths)File.WriteAllBytes(Absolute(path+".meta"),originals[path]);
            foreach(string path in paths)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            foreach(string path in paths)if(FileHash(path+".meta")!=report.containers.Single(c=>c.path==path).metaSha256)throw new IOException("Exact importer metadata restoration failed: "+path);
            using var read=new ReadScope();
            foreach(var intent in report.intents.Where(i=>paths.Contains(i.action.containerPath)))read.Visit(intent.action,r=>RequireMaterial(r,intent.action.slot,intent.action.expectedMaterialGuid,intent.action.expectedMaterialLocalId));
        }

        public static Identifier[] Identifiers(AssetImporter importer,string guid,long localId)
        {
            var result=new List<Identifier>();var explicitMap=importer.GetExternalObjectMap();
            foreach(var pair in explicitMap.Where(p=>p.Key.type==typeof(Material)))
                if(IsIdentity(pair.Value,guid,localId))result.Add(new Identifier {sourceType=typeof(Material).FullName,sourceName=pair.Key.name,provenance="GetExternalObjectMap",hadExplicitRemap=true,expectedGuid=guid,expectedLocalId=localId});
            if(IsPmx(importer))
            {
                foreach(var entry in PmxEntries(importer).Where(m=>IsIdentity(m.material,guid,localId)))
                    if(entry.identifier==(string)importer.GetType().GetMethod("MaterialIdentifier",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(int)},null)?.Invoke(null,new object[]{entry.index}))
                    {
                        var meshes=AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Mesh>().Where(m=>m.name==entry.index.ToString("D3")+"_"+entry.sourceName).ToArray();
                        if(meshes.Length!=1)continue;
                        Identity(meshes[0],out string meshGuid,out long meshId);
                        var id=result.SingleOrDefault(i=>i.sourceName==entry.identifier);
                        if(id==null){id=new Identifier {sourceType=typeof(Material).FullName,sourceName=entry.identifier,expectedGuid=guid,expectedLocalId=localId};result.Add(id);}
                        id.provenance="PMX report exact index/identifier/material identity + mesh subasset GUID/localID";
                        id.pmxMaterialIndex=entry.index;id.sourceMeshGuid=meshGuid;id.sourceMeshLocalId=meshId;
                    }
            }
            if(importer is ModelImporter)
            {
                // This property is internal on some supported Editors. Its absence
                // is an explicit unresolved key, never a material-name guess.
                var property=typeof(ModelImporter).GetProperty("sourceMaterials",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                var identifiers=property?.GetValue(importer) as AssetImporter.SourceAssetIdentifier[];
                var embedded=AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Material>().Where(m=>IsIdentity(m,guid,localId)).ToArray();
                if(identifiers!=null)foreach(var id in identifiers.Where(i=>i.type==typeof(Material)&&!explicitMap.ContainsKey(i)))
                    if(embedded.Length==1&&id.name==embedded[0].name)result.Add(new Identifier {sourceType=typeof(Material).FullName,sourceName=id.name,provenance="ModelImporter.sourceMaterials exact embedded material identity/name",expectedGuid=guid,expectedLocalId=localId});
            }
            return result.OrderBy(r=>r.sourceName,StringComparer.Ordinal).ToArray();
        }

        private static IEnumerable<Intent[]> ModelGroups(AssetImporter importer,IEnumerable<Intent> intents)
        {
            return intents.GroupBy(i=>
            {
                var ids=IdentifiersForIntent(importer,i);
                return IsPmx(importer)&&ids.Length==1&&ids[0].pmxMaterialIndex>=0?"PMX:"+ids[0].sourceName:i.action.expectedMaterialGuid+":"+i.action.expectedMaterialLocalId;
            }).Select(g=>g.ToArray());
        }
        private static Identifier[] IdentifiersForIntent(AssetImporter importer,Intent intent)
        {
            var ids=Identifiers(importer,intent.action.expectedMaterialGuid,intent.action.expectedMaterialLocalId);
            if(!IsPmx(importer))return ids;
            if(!ids.Any(i=>i.pmxMaterialIndex>=0))return ids; // all explicit aliases remain evidence; no invented per-slot key
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
            var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>Hierarchy(r.transform)==intent.action.rendererPath&&r.GetType().FullName==intent.action.rendererType&&Array.IndexOf(r.GetComponents<Renderer>(),r)==intent.action.componentIndex).ToArray();
            if(renderers.Length!=1||intent.action.slot!=0)return Array.Empty<Identifier>();
            var renderer=renderers[0];Mesh mesh=renderer is SkinnedMeshRenderer skinned?skinned.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh==null)return Array.Empty<Identifier>();
            Identity(mesh,out string guid,out long localId);
            return ids.Where(i=>i.pmxMaterialIndex>=0&&i.sourceMeshGuid==guid&&i.sourceMeshLocalId==localId).ToArray();
        }
        private sealed class PmxEntry {public int index;public string identifier,sourceName;public Material material;}
        private static bool IsPmx(AssetImporter importer)=>importer.GetType().FullName=="Hollow.HoUnityTools.Editor.Pmx.HoPmxImporter";
        private static IEnumerable<PmxEntry> PmxEntries(AssetImporter importer)
        {
            var report=AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<ScriptableObject>().SingleOrDefault(o=>o.GetType().FullName=="Hollow.HoUnityTools.Editor.Pmx.HoPmxImportReport");
            if(report==null)yield break;
            if(!(report.GetType().GetField("materials",BindingFlags.Public|BindingFlags.Instance)?.GetValue(report) is IEnumerable entries))yield break;
            foreach(object value in entries)
            {
                var type=value.GetType();var index=type.GetField("index")?.GetValue(value);
                if(!(index is int number))continue;
                yield return new PmxEntry {index=number,identifier=type.GetField("identifier")?.GetValue(value) as string,
                    sourceName=type.GetField("sourceName")?.GetValue(value) as string,material=type.GetField("material")?.GetValue(value) as Material};
            }
        }

        private static void ValidateRemapBefore(ImporterRemap remap)
        {
            var importer=AssetImporter.GetAtPath(remap.modelPath);
            if(importer==null||importer.GetType().FullName!=remap.importerType||EditorUtility.IsDirty(importer))throw new InvalidOperationException("Importer changed/dirty: "+remap.modelPath);
            var key=new AssetImporter.SourceAssetIdentifier(typeof(Material),remap.identifier.sourceName);var map=importer.GetExternalObjectMap();
            bool has=map.TryGetValue(key,out var target);
            if(has!=remap.identifier.hadExplicitRemap||has&&!IsIdentity(target,remap.identifier.expectedGuid,remap.identifier.expectedLocalId))throw new InvalidOperationException("Exact source remap changed: "+remap.modelPath+" "+key.name);
            if(!Identifiers(importer,remap.identifier.expectedGuid,remap.identifier.expectedLocalId).Any(i=>i.sourceName==key.name&&i.sourceType==remap.identifier.sourceType))throw new InvalidOperationException("Source identifier no longer exists.");
        }
        private static bool AllModelUsesCovered(string path,string guid,long id,Intent[] intents,Identifier[] identifiers)
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(root==null)return false;
            var keys=new HashSet<string>(intents.Select(i=>Key(i.chain[0])),StringComparer.Ordinal);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(identifiers.Length==1&&identifiers[0].pmxMaterialIndex>=0)
                {
                    Mesh mesh=renderer is SkinnedMeshRenderer skinned?skinned.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if(!IsIdentity(mesh,identifiers[0].sourceMeshGuid,identifiers[0].sourceMeshLocalId))continue;
                }
                for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)if(IsIdentity(renderer.sharedMaterials[slot],guid,id)&&!keys.Contains(Key(Observe(renderer,path,slot))))return false;
            }
            return true;
        }
        private static Node Observe(Renderer renderer,string path,int slot)
        {
            if(slot<0||slot>=renderer.sharedMaterials.Length)throw new InvalidOperationException("Source-chain material slot no longer exists.");
            var source=PrefabUtility.GetCorrespondingObjectFromSource(renderer);bool overridden=false;
            if(source!=null)
            {
                using var serialized=new SerializedObject(renderer);var values=serialized.FindProperty("m_Materials");
                overridden=values==null||slot>=values.arraySize||values.GetArrayElementAtIndex(slot).prefabOverride||source.sharedMaterials.Length!=renderer.sharedMaterials.Length;
                if(slot<source.sharedMaterials.Length&&!Same(renderer.sharedMaterials[slot],source.sharedMaterials[slot]))overridden=true;
            }
            Identity(renderer.sharedMaterials[slot],out string guid,out long localId);
            return new Node {containerPath=path,rendererGlobalId=GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString(),rendererPath=Hierarchy(renderer.transform),rendererType=renderer.GetType().FullName,
                componentIndex=Array.IndexOf(renderer.GetComponents<Renderer>(),renderer),slot=slot,materialGuid=guid,materialLocalId=localId,materialOverride=overridden,model=IsModel(path)};
        }
        private sealed class ReadScope:IDisposable
        {
            private readonly Dictionary<string,Renderer[]> renderers=new(StringComparer.Ordinal);
            private readonly List<Scene> scenes=new();
            public void Visit(HoMaterialMigrationSlotAction action,Action<Renderer> visit)
            {
                RequireAssetPath(action.containerPath);
                if(!renderers.TryGetValue(action.containerPath,out var values))
                {
                    GameObject[] roots;
                    if(action.containerPath.EndsWith(".unity",StringComparison.OrdinalIgnoreCase))
                    {
                        if(SceneManager.GetSceneByPath(action.containerPath).isLoaded)throw new InvalidOperationException("Close target scene before ownership audit.");
                        var scene=EditorSceneManager.OpenPreviewScene(action.containerPath);scenes.Add(scene);roots=scene.GetRootGameObjects();
                    }
                    else {var root=AssetDatabase.LoadAssetAtPath<GameObject>(action.containerPath);if(root==null)throw new InvalidOperationException("Missing prefab/model root.");roots=new[]{root};}
                    values=roots.SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).ToArray();renderers.Add(action.containerPath,values);
                }
                var candidates=values.Where(r=>r.GetType().FullName==action.rendererType&&Hierarchy(r.transform)==action.rendererPath&&Array.IndexOf(r.GetComponents<Renderer>(),r)==action.componentIndex).ToArray();
                if(candidates.Length!=1)throw new InvalidOperationException("Native renderer is missing/ambiguous: "+action.rendererPath);visit(candidates[0]);
            }
            public void Dispose(){foreach(var scene in scenes)if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static Container Stamp(string path){RequireAssetPath(path);return new Container {path=path,guid=AssetDatabase.AssetPathToGUID(path),assetSha256=FileHash(path),metaSha256=FileHash(path+".meta")};}
        private static string Target(HoMaterialMigrationSlotAction action)=>action.targetSourceGuid+"/"+action.targetFamily+"/"+action.targetAssetPath;
        private static string Key(Node node)=>node.containerPath+"|"+node.rendererGlobalId+"|"+node.rendererType+"|"+node.componentIndex+"|"+node.slot;
        private static bool IsModel(string path)=>!path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)&&!path.EndsWith(".unity",StringComparison.OrdinalIgnoreCase);
        private static bool Same(Object a,Object b){Identity(a,out string ag,out long ai);Identity(b,out string bg,out long bi);return ag==bg&&ai==bi;}
        private static bool IsIdentity(Object value,string guid,long id)=>value!=null&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string g,out long i)&&g==guid&&i==id;
        private static void Identity(Object value,out string guid,out long id){if(value==null||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out guid,out id))throw new InvalidOperationException("A persistent GUID/localID is required.");}
        private static void RequirePersistent(Object value)=>Identity(value,out _,out _);
        private static void RequireMaterial(Renderer renderer,int slot,string guid,long id){if(slot<0||slot>=renderer.sharedMaterials.Length||!IsIdentity(renderer.sharedMaterials[slot],guid,id))throw new InvalidOperationException("Native material GUID/localID differs.");}
        private static string Hierarchy(Transform value){var names=new List<string>();for(;value!=null;value=value.parent)names.Add(value.name+"["+value.GetSiblingIndex()+"]");names.Reverse();return string.Join("/",names);}
        private static HoMaterialMigrationSlotAction Clone(HoMaterialMigrationSlotAction action)=>JsonUtility.FromJson<HoMaterialMigrationSlotAction>(JsonUtility.ToJson(action));
        private static string PlanFingerprint(HoMaterialMigrationPlan plan){var copy=JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));foreach(var r in copy.rows){r.reviewed=false;r.acceptFamilyModelChanges=false;r.acceptedDifferences=Array.Empty<string>();}foreach(var a in copy.slotActions)a.reviewed=false;return Hash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(copy)));}
        private static string Digest(Report report){string previous=report.coreHash;report.coreHash="";try{return Hash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report)));}finally{report.coreHash=previous;}}
        private static void RequireReport(Report report){if(report==null||report.schemaVersion!=1||report.projectRoot!=ProjectRoot||report.coreHash!=Digest(report))throw new InvalidOperationException("Owner evidence changed or belongs to another project.");}
        private static void RequireAssetPath(string path){if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains('\\')||path.Split('/').Contains(".."))throw new InvalidOperationException("Owner path must stay inside Assets.");}
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(ProjectRoot,path));
        private static string FileHash(string path)=>Hash(File.ReadAllBytes(Absolute(path)));
        private static string Hash(byte[] bytes){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static string ProjectRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
    }
}
