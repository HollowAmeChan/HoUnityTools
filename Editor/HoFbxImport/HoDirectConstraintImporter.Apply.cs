#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    internal static partial class HoDirectConstraintImporter
    {
        internal static int Apply(Prepared p,HoAuxRig ho)
        {
            if(p.entries.Count==0)return 0;
            if(p.backend==Backend.HoAux)return ApplyHo(p,ho);
            foreach(var group in p.entries.GroupBy(e=>e.Owner))
            {
                Transform owner=p.map[group.Key];
                Matrix4x4 local=p.bind.local[group.Key];
                Transform previous=Helper(p,owner.parent,"Input_"+group.Key);
                SetLocal(previous,local);
                foreach(Entry e in group.OrderBy(x=>x.Index))
                {
                    Transform next=Helper(p,previous,e.Type+"_"+group.Key+"_"+e.Index);
                    if(e.Type=="COPY_LOCATION")
                    {
                        Position(p,next,new[]{Source(p.map[e.Target],1),Source(previous,0)},e.raw.constraint.parameters.influence);
                    }
                    else if(e.Type=="COPY_ROTATION")
                    {
                        Transform target=Helper(p,p.map[e.Target],"RotationBasis_"+group.Key+"_"+e.Index);
                        target.localPosition=Vector3.zero;target.localScale=Vector3.one;
                        target.localRotation=Correction(p,e.Target,e.Owner);
                        Rotation(p,next,new[]{Source(target,1),Source(previous,0)},e.raw.constraint.parameters.influence);
                    }
                    else if(e.Type=="ARMATURE")
                    {
                        var targets=e.raw.constraint.references.targets;
                        double total=targets.Sum(t=>(double)t.properties.weight);
                        var sources=targets.Select(t=>Source(p.map[t.properties.subtarget],(float)(t.properties.weight/total))).ToList();
                        var offsets=new List<Vector3>();var rotations=new List<Vector3>();
                        Matrix4x4 bindOwner=p.rig.transform.localToWorldMatrix*p.bind.rest[e.Owner];
                        foreach(var t in targets)
                        {
                            Matrix4x4 bindTarget=p.rig.transform.localToWorldMatrix*p.bind.rest[t.properties.subtarget];
                            offsets.Add(bindTarget.inverse.MultiplyPoint3x4(bindOwner.GetColumn(3)));
                            rotations.Add((Quaternion.Inverse(bindTarget.rotation)*bindOwner.rotation).eulerAngles);
                        }
                        sources.Add(Source(previous,0));offsets.Add(Vector3.zero);rotations.Add(Vector3.zero);
                        Parent(p,next,sources,offsets,rotations);
                        Debug.LogWarning("HoFBX "+e.Owner+"：Armature → 多源 Parent 刚性近似；不保留完整仿射混合／动态约束前父级");
                    }
                    else if(e.Type=="AIM")
                    {
                        Vector3 a,u;TrackAxis(e.aim.aimAxis,out a);TrackAxis(e.aim.upAxis,out u);
                        a=p.bind.basis[e.Owner].inverse.MultiplyVector(a).normalized;
                        u=p.bind.basis[e.Owner].inverse.MultiplyVector(u).normalized;
                        Aim(p,next,p.map[e.aim.targetBone],p.map[e.aim.upBone],a,u,previous);
                    }
                    previous=next;
                }
                Position(p,owner,new[]{Source(previous,1)});
                Rotation(p,owner,new[]{Source(previous,1)});
                Scale(p,owner,previous);
                {
                    var marker=Marker(p,owner.gameObject,ConstraintHostRole.Bone);
                    marker.hasOwnerBindPose=true;marker.bindLocalPosition=local.GetColumn(3);
                    marker.bindLocalRotation=local.rotation;marker.bindLocalScale=ScaleOf(local);
                    EditorUtility.SetDirty(marker);
                }
            }
            return p.entries.Count;
        }

        private static int ApplyHo(Prepared p,HoAuxRig ho)
        {
            if(ho==null)ho=Undo.AddComponent<HoAuxRig>(p.root.gameObject);
            Undo.RecordObject(ho,"导入 IR v3 通用约束");
            ho.RigRoot=p.root;ho.SourceArmature=p.config.armatureName;ho.ExportTime=p.config.exportTime;ho.ExporterVersion=p.config.schemaVersion.ToString();
            ho.SetDirectSpace(p.rig.transform);
            foreach(Entry e in p.entries)
            {
                Matrix4x4 local=p.bind.local[e.Owner];
                var op=new HoAuxRig.DirectOperation {
                    type=e.Type,owner=p.map[e.Owner],stackIndex=e.Index,
                    bindLocalPosition=local.GetColumn(3),bindLocalRotation=local.rotation,bindLocalScale=ScaleOf(local),
                    weight=e.aim!=null?1:e.raw.constraint.parameters.influence,
                };
                if(e.Type=="ARMATURE")
                {
                    op.positionOnly=p.config.bindingContext.bones.First(b=>b.boneName==e.Owner).positionOnly;
                    double total=e.raw.constraint.references.targets.Sum(t=>(double)t.properties.weight);
                    foreach(var target in e.raw.constraint.references.targets)
                        op.sources.Add(new HoAuxRig.DirectSource {bone=p.map[target.properties.subtarget],weight=(float)(target.properties.weight/total),inverseBind=p.bind.rest[target.properties.subtarget].inverse});
                }
                else
                {
                    op.target=p.map[e.Target];
                    if(e.Type=="COPY_ROTATION")op.rotationCorrection=Correction(p,e.Target,e.Owner);
                    if(e.aim!=null)
                    {
                        op.up=p.map[e.aim.upBone];Vector3 a,u;TrackAxis(e.aim.aimAxis,out a);TrackAxis(e.aim.upAxis,out u);
                        op.aimAxis=p.bind.basis[e.Owner].inverse.MultiplyVector(a).normalized;
                        op.upAxis=p.bind.basis[e.Owner].inverse.MultiplyVector(u).normalized;
                    }
                }
                ho.AddDirectOperation(op);
            }
            EditorUtility.SetDirty(ho);
            if(PrefabUtility.IsPartOfPrefabInstance(ho))PrefabUtility.RecordPrefabInstancePropertyModifications(ho);
            return p.entries.Count;
        }

        private static Quaternion Correction(Prepared p,string target,string owner)
        {return (p.bind.basis[target].inverse*p.bind.basis[owner]).rotation;}
        private static Vector3 ScaleOf(Matrix4x4 m)
        {return new Vector3(m.GetColumn(0).magnitude,m.GetColumn(1).magnitude,m.GetColumn(2).magnitude);}
        private static void SetLocal(Transform t,Matrix4x4 m)
        {t.localPosition=m.GetColumn(3);t.localRotation=m.rotation;t.localScale=ScaleOf(m);}
        private static ConstraintSource Source(Transform t,float w)
        {return new ConstraintSource {sourceTransform=t,weight=w};}
        private const Axis All=Axis.X|Axis.Y|Axis.Z;

        private static HoImportedConstraintMarker Marker(Prepared p,GameObject go,ConstraintHostRole role)
        {
            var marker=go.GetComponent<HoImportedConstraintMarker>();
            if(marker==null)marker=Undo.AddComponent<HoImportedConstraintMarker>(go);
            marker.SetMetadata(p.config.armatureName,p.config.exportTime,p.config.schemaVersion.ToString());
            marker.SetHost(role,HoAuxRig.GetRelativePath(p.root,go.transform));
            marker.directBackend=p.backend.ToString();EditorUtility.SetDirty(marker);return marker;
        }
        private static Transform Helper(Prepared p,Transform parent,string label)
        {
            var go=new GameObject("[HoDirect] "+label);Undo.RegisterCreatedObjectUndo(go,"创建 IR v3 阶段");
            go.transform.SetParent(parent,false);Marker(p,go,ConstraintHostRole.GeneratedHelper);return go.transform;
        }
        private static void Register(Prepared p,Transform owner,Component c)
        {
            var existing=owner.GetComponent<HoImportedConstraintMarker>();
            var marker=existing!=null?existing:Marker(p,owner.gameObject,ConstraintHostRole.Bone);
            marker.Register(c);EditorUtility.SetDirty(marker);EditorUtility.SetDirty(c);
            if(PrefabUtility.IsPartOfPrefabInstance(c))PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
        private static void Position(Prepared p,Transform owner,IList<ConstraintSource> sources,float influence=1)
        {
            if(p.backend==Backend.Vrc){Vrc(p,"Position",owner,sources,null,null,null,influence);return;}
            var c=Undo.AddComponent<PositionConstraint>(owner.gameObject);c.constraintActive=false;c.locked=false;
            c.SetSources(sources.ToList());c.translationAxis=All;c.translationAtRest=owner.localPosition;c.translationOffset=Vector3.zero;
            c.weight=influence;c.locked=true;c.constraintActive=true;Register(p,owner,c);
        }
        private static void Rotation(Prepared p,Transform owner,IList<ConstraintSource> sources,float influence=1)
        {
            if(p.backend==Backend.Vrc){Vrc(p,"Rotation",owner,sources,null,null,null,influence);return;}
            var c=Undo.AddComponent<RotationConstraint>(owner.gameObject);c.constraintActive=false;c.locked=false;
            c.SetSources(sources.ToList());c.rotationAxis=All;c.rotationAtRest=owner.localEulerAngles;c.rotationOffset=Vector3.zero;
            c.weight=influence;c.locked=true;c.constraintActive=true;Register(p,owner,c);
        }
        private static void Scale(Prepared p,Transform owner,Transform source)
        {
            var sources=new[]{Source(source,1)};
            if(p.backend==Backend.Vrc){Vrc(p,"Scale",owner,sources,null,null,null);return;}
            var c=Undo.AddComponent<ScaleConstraint>(owner.gameObject);c.constraintActive=false;c.locked=false;
            c.SetSources(sources.ToList());c.scalingAxis=All;c.scaleAtRest=owner.localScale;c.scaleOffset=Vector3.zero;
            c.weight=1;c.locked=true;c.constraintActive=true;Register(p,owner,c);
        }
        private static void Parent(Prepared p,Transform owner,IList<ConstraintSource> sources,List<Vector3> offsets,List<Vector3> rotations)
        {
            if(p.backend==Backend.Vrc){Vrc(p,"Parent",owner,sources,offsets,rotations,null);return;}
            var c=Undo.AddComponent<ParentConstraint>(owner.gameObject);c.constraintActive=false;c.locked=false;
            c.SetSources(sources.ToList());c.translationAxis=All;c.rotationAxis=All;
            for(int i=0;i<sources.Count;i++){c.SetTranslationOffset(i,offsets[i]);c.SetRotationOffset(i,rotations[i]);}
            c.weight=1;c.locked=true;c.constraintActive=true;Register(p,owner,c);
        }
        private static void Aim(Prepared p,Transform owner,Transform target,Transform up,Vector3 axis,Vector3 upAxis,Transform previous)
        {
            var sources=new[]{Source(target,1),Source(up,0),Source(previous,0)};
            if(p.backend==Backend.Vrc)
            {
                Vrc(p,"Aim",owner,sources,null,null,new Dictionary<string,object>{{"AimAxis",axis},{"UpAxis",upAxis},{"WorldUpTransform",up},{"WorldUp","ObjectUp"}});return;
            }
            var c=Undo.AddComponent<AimConstraint>(owner.gameObject);c.constraintActive=false;c.locked=false;
            c.SetSources(sources.ToList());c.aimVector=axis;c.upVector=upAxis;c.worldUpType=AimConstraint.WorldUpType.ObjectUp;c.worldUpObject=up;
            c.rotationAxis=All;c.rotationAtRest=owner.localEulerAngles;c.rotationOffset=Vector3.zero;c.weight=1;c.locked=true;c.constraintActive=true;Register(p,owner,c);
        }
        private static void Vrc(Prepared p,string kind,Transform owner,IList<ConstraintSource> sources,List<Vector3> offsets,List<Vector3> rotations,Dictionary<string,object> settings,float influence=1)
        {
            var host=new GameObject("[VRC Direct] "+kind+" "+owner.name);Undo.RegisterCreatedObjectUndo(host,"创建 VRC 约束挂点");host.transform.SetParent(p.root,false);
            var c=HoVrcConstraintBridge.AddDirectConstraint(host,kind,owner,sources,offsets,rotations,settings,influence);
            var marker=Marker(p,host,ConstraintHostRole.VrcConstraintObject);marker.Register(c);EditorUtility.SetDirty(marker);
        }

        internal static int ClearHelpers(GameObject rig,Transform explicitRoot,string backend=null,string armature=null)
        {
            var markers=new HashSet<HoImportedConstraintMarker>();
            if(rig!=null)foreach(var m in rig.GetComponentsInChildren<HoImportedConstraintMarker>(true))markers.Add(m);
            if(explicitRoot!=null)foreach(var m in explicitRoot.GetComponentsInChildren<HoImportedConstraintMarker>(true))markers.Add(m);
            int count=0;
            foreach(var marker in markers.OrderByDescending(m=>Depth(m.transform)))
            {
                if(marker==null||marker.hostRole!=ConstraintHostRole.GeneratedHelper||(backend!=null&&marker.directBackend!=backend)||(!string.IsNullOrEmpty(armature)&&marker.sourceArmature!=armature))continue;
                foreach(var c in marker.GetLiveConstraints().ToList())Undo.DestroyObjectImmediate(c);
                marker.ClearManaged();
                bool empty=marker.transform.childCount==0&&marker.GetComponents<Component>().All(c=>c is Transform||c==marker);
                if(empty){Undo.DestroyObjectImmediate(marker.gameObject);count++;}
                else EditorUtility.SetDirty(marker);
            }
            return count;
        }
        private static int Depth(Transform t){int n=0;for(;t!=null;t=t.parent)n++;return n;}
    }
}
#endif
