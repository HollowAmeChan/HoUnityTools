#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    internal static partial class HoDirectConstraintImporter
    {
        internal enum Backend { Standard, HoAux, Vrc }
        internal enum Capability { Exact, NativeApproximation, Unsupported, Invalid, Skipped }
        internal sealed class Entry
        {
            internal ConstraintConfig config;
            internal KnownConstraintInfo raw;
            internal AimBindingInfo aim;
            internal string Owner { get { return aim != null ? aim.ownerBone : raw.ownerBone; } }
            internal string Type { get { return aim != null ? "AIM" : raw.constraint.constraintType; } }
            internal int Index { get { return aim != null ? aim.constraintStackIndices[0] : raw.constraint.stackIndex; } }
            internal string Target { get { return aim != null ? aim.targetBone : raw.constraint.targetBoneName; } }
            internal bool Muted { get { return aim != null ? aim.muted : raw.constraint.parameters != null && (raw.constraint.parameters.mute || raw.constraint.parameters.influence == 0); } }
        }
        internal sealed class Prepared
        {
            internal Backend backend;
            internal ConstraintConfig config;
            internal GameObject rig;
            internal Transform root;
            internal Dictionary<string,Transform> map;
            internal Bindings bind;
            internal readonly List<Entry> entries = new List<Entry>();
        }

        internal static List<Entry> Build(ConstraintConfig config)
        {
            var result = new List<Entry>();
            if(config.schemaVersion < 3) return result;
            foreach(var known in config.knownConstraints)
                if(known.relationType == "DIRECT_CONSTRAINT")result.Add(new Entry {config=config,raw=known});
            foreach(var aim in config.aimBindings)result.Add(new Entry {config=config,aim=aim});
            return result.OrderBy(e=>e.Owner,StringComparer.Ordinal).ThenBy(e=>e.Index).ToList();
        }

        internal static string TargetLabel(Entry e)
        {
            if(e.aim!=null)return e.aim.targetBone+"（瞄准） / "+e.aim.upBone+"（上方向）";
            if(e.Type!="ARMATURE")return e.Target;
            var targets=e.raw.constraint.references==null?null:e.raw.constraint.references.targets;
            if(targets==null||targets.Count==0)return "无来源";
            double total=targets.Where(t=>t!=null&&t.properties!=null&&Finite(t.properties.weight)).Sum(t=>(double)t.properties.weight);
            return string.Join(" + ",targets.Select(t=>{
                var p=t==null?null:t.properties;
                if(p==null)return "无效来源";
                string weight=p.weight.ToString("G4",CultureInfo.InvariantCulture);
                string normalized=total>0&&Finite(p.weight)?"（"+(p.weight/total).ToString("P1",CultureInfo.InvariantCulture)+"）":"";
                return p.subtarget+" × "+weight+normalized;
            }));
        }

        internal static IEnumerable<string> ReferencedBones(Entry e){return References(e);}

        internal static bool ValidateV3(ConstraintConfig config,out string error)
        {
            error = null;
            if(config.schemaVersion==2)return true;
            if(config.aimBindings==null || config.bindingContext==null || config.bindingContext.bones==null ||
               config.bindingContext.space!="BLENDER_ARMATURE" || config.bindingContext.matrixLayout!="ROW_MAJOR" || !ValidMatrix(config.bindingContext.objectMatrix))
            { error="IR v3 缺少 aimBindings 或有效 bindingContext";return false; }
            var boneNames=new HashSet<string>(StringComparer.Ordinal);
            foreach(var b in config.bindingContext.bones)
                if(b==null||string.IsNullOrEmpty(b.boneName)||!boneNames.Add(b.boneName)||!ValidMatrix(b.restMatrix)||!ValidMatrix(b.poseBasis))
                {error="IR v3 骨骼绑定矩阵缺失、非有限或身份重复";return false;}
            if(config.bindingContext.bones.Any(b=>!string.IsNullOrEmpty(b.parentBone)&&!boneNames.Contains(b.parentBone)))
            {error="IR v3 绑定上下文缺少父骨身份";return false;}
            var aimKeys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var aim in config.aimBindings)
            {
                if(aim==null||aim.semanticType!="AIM"||aim.semanticVersion!=1||aim.upMode!="OBJECT_POSITION"||
                   string.IsNullOrEmpty(aim.ownerBone)||string.IsNullOrEmpty(aim.targetBone)||string.IsNullOrEmpty(aim.upBone)||
                   aim.ownerBone==aim.targetBone||aim.ownerBone==aim.upBone||aim.targetBone==aim.upBone||
                   aim.constraintStackIndices==null||aim.constraintStackIndices.Length!=2||aim.constraintStackIndices[0]<0||
                   aim.constraintStackIndices[1]!=aim.constraintStackIndices[0]+1||!TrackAxis(aim.aimAxis,out Vector3 a)||
                   !TrackAxis(aim.upAxis,out Vector3 u)||Mathf.Abs(Vector3.Dot(a,u))>0.001f)
                {error="IR v3 Aim 旋转语义无效";return false;}
                var aux=config.auxBones.FirstOrDefault(x=>x.boneName==aim.ownerBone);
                if(aux==null||aux.auxType!="AIM"||aux.sourceBones.Count!=2||aux.sourceBones[0]!=aim.targetBone||aux.sourceBones[1]!=aim.upBone)
                {error="Aim 没有匹配的显式 Ho 骨属性";return false;}
                for(int i=0;i<2;i++)
                {
                    string key=Key(aim.ownerBone,aim.constraintStackIndices[i]);
                    var record=config.knownConstraints.FirstOrDefault(k=>Key(k.ownerBone,k.constraint.stackIndex)==key);
                    var c=record==null?null:record.constraint;
                    var p=c==null?null:c.parameters;
                    if(!aimKeys.Add(key)||record==null||record.relationType!="AIM_BINDING"||record.auxType!="AIM"||record.auxBone!=aim.ownerBone||
                       c.constraintType!=(i==0?"DAMPED_TRACK":"LOCKED_TRACK")||c.targetObjectName!=config.armatureName||
                       c.targetBoneName!=(i==0?aim.targetBone:aim.upBone)||!aux.constraintNames.Contains(c.name)||p==null||
                       p.owner_space!="WORLD"||p.target_space!="WORLD"||p.influence!=1||p.mute!=aim.muted||p.head_tail!=0||p.use_bbone_shape||
                       p.track_axis!=(i==0?aim.aimAxis:aim.upAxis)||(i==1&&p.lock_axis!="LOCK_"+aim.aimAxis.Last()))
                    {error="Aim 语义与登记的相邻 Damped/Locked 原始约束不一致";return false;}
                }
            }
            foreach(var known in config.knownConstraints)
            {
                if(known.relationType=="AIM_BINDING"&&!aimKeys.Contains(Key(known.ownerBone,known.constraint.stackIndex)))
                {error="孤立 AIM_BINDING 记录";return false;}
                if(known.relationType=="DIRECT_CONSTRAINT"&&known.constraint.constraintType!="COPY_LOCATION"&&known.constraint.constraintType!="COPY_ROTATION"&&known.constraint.constraintType!="ARMATURE")
                {error="DIRECT_CONSTRAINT 包含未注册类型";return false;}
            }
            return true;
        }

        internal static string Describe(Entry e,Backend backend)
        {
            string issue=Issue(e,backend);
            if(issue!=null)return "已识别 / "+(Status(e,backend)==Capability.Invalid?"数据无效：":"不支持：")+issue;
            if(e.Muted)return "已识别 / 不执行（静音或零影响度）";
            string type=e.Type=="COPY_LOCATION"?"Position":e.Type=="COPY_ROTATION"?"Rotation":e.Type=="ARMATURE"?"Parent":"Aim";
            string notice=DependencyNotice(e);
            string suffix=notice==null?string.Empty:" / "+notice;
            if(backend==Backend.HoAux)return "HoAuxRig / "+e.Type+(e.Type=="ARMATURE"?"（仿射；非 TRS 写回有诊断）":"")+suffix;
            return (backend==Backend.Vrc?"VRC":"")+type+"Constraint"+(e.Type=="ARMATURE"?" / 刚性近似，非完整 Armature":
                Status(e,backend)==Capability.NativeApproximation?" / 原生插值（未保证 Blender Slerp）":" / 栈阶段")+suffix;
        }

        private static string DependencyNotice(Entry e)
        {
            foreach(string source in References(e))
            {
                var seen=new HashSet<string>();string name=source;
                while(!string.IsNullOrEmpty(name)&&seen.Add(name))
                {
                    if(e.config.unknownConstraints.Any(c=>c.ownerBone==name))return "来源 "+name+" 有未实现约束，读取 Unity 当前姿态";
                    var b=e.config.bindingContext.bones.FirstOrDefault(x=>x.boneName==name);name=b==null?null:b.parentBone;
                }
            }
            return null;
        }

        internal static Capability Status(Entry e,Backend backend)
        {
            if(e.aim==null)
            {
                var p=e.raw.constraint.parameters;
                if(p==null||!Finite(p.influence)||p.influence<0||p.influence>1)return Capability.Invalid;
                if(e.Type=="ARMATURE")
                {
                    var ts=e.raw.constraint.references==null?null:e.raw.constraint.references.targets;
                    if(ts==null||ts.Count==0||ts.Any(t=>t==null||t.properties==null||t.properties.target==null||string.IsNullOrEmpty(t.properties.subtarget)||!Finite(t.properties.weight)||t.properties.weight<0)||ts.Sum(t=>(double)t.properties.weight)<=0)return Capability.Invalid;
                }
            }
            if(Issue(e,backend)!=null)return Capability.Unsupported;
            if(e.Muted)return Capability.Skipped;
            if(backend!=Backend.HoAux&&(e.Type=="ARMATURE"||e.Type=="COPY_ROTATION"&&e.raw.constraint.parameters.influence!=1&&e.raw.constraint.parameters.influence!=.5f))return Capability.NativeApproximation;
            return Capability.Exact;
        }

        internal static string Issue(Entry e,Backend backend)
        {
            string barrier=StackBarrier(e);
            if(barrier!=null)return barrier;
            if(e.config.knownConstraints.Any(x=>x.ownerBone==e.Owner&&x.relationType!="DIRECT_CONSTRAINT"&&x.relationType!="AIM_BINDING"))return "同一 Owner 混合旧 Aux/MCH 与通用栈";
            if(backend==Backend.HoAux)
                foreach(var legacy in e.config.knownConstraints.Where(k=>k.relationType=="MCH_BINDING"||k.relationType=="AUX_CONSTRAINT"))
                {
                    var legacyConstraint=legacy.constraint;
                    if(legacyConstraint.parameters==null||legacyConstraint.parameters.mute||legacyConstraint.parameters.influence==0||legacyConstraint.targetObjectName!=e.config.armatureName)continue;
                    string name=legacyConstraint.targetBoneName;var seen=new HashSet<string>();
                    while(!string.IsNullOrEmpty(name)&&seen.Add(name))
                    {
                        if(name==e.Owner)return "旧 Ho 层依赖此通用 Owner，首版不跨旧层反馈重排";
                        var parent=e.config.bindingContext.bones.FirstOrDefault(b=>b.boneName==name);name=parent==null?null:parent.parentBone;
                    }
                }
            var binding=e.config.bindingContext.bones.FirstOrDefault(b=>b.boneName==e.Owner);
            if(binding==null||binding.connected&&(e.Type=="COPY_LOCATION"||e.Type=="ARMATURE")||binding.inheritScale!="FULL"||!Identity(binding.poseBasis)||binding.hasPoseAnimation)return "Owner 需静置局部输入、FULL 继承；位置/Armature 操作不支持连接骨";
            if(e.aim!=null)return null; // The structural validator has checked the complete pair.
            var c=e.raw.constraint;var p=c.parameters;
            if(p==null||!Finite(p.influence)||p.influence<0||p.influence>1)return "影响度或参数无效";
            if(p.owner_space!="WORLD"||p.target_space!="WORLD")return "v3 首版要求 WORLD → WORLD";
            if(e.Type=="ARMATURE")
            {
                if(p.use_deform_preserve_volume||p.use_bone_envelopes||p.use_current_location)return "Armature 的 Preserve Volume/Envelope/Current Location 暂不支持";
                if(p.influence!=1&&p.influence!=0)return "Armature 首版只支持完整或零影响度";
                var targets=c.references==null?null:c.references.targets;
                if(targets==null||targets.Count==0)return "Armature 缺少多源列表";
                double sum=0;
                foreach(var t in targets)
                {
                    var s=t==null?null:t.properties;
                    if(s==null||s.target==null||s.target.name!=e.config.armatureName||string.IsNullOrEmpty(s.subtarget)||!Finite(s.weight)||s.weight<0)return "Armature 源身份或权重无效／跨骨架尚未支持";
                    sum+=s.weight;
                }
                if(sum<=0||double.IsInfinity(sum))return "Armature 权重总和必须为有限正数";
                return null;
            }
            if(c.targetObjectName!=e.config.armatureName||string.IsNullOrEmpty(c.targetBoneName))return "缺少当前骨架的目标骨";
            if(!p.use_x||!p.use_y||!p.use_z||p.invert_x||p.invert_y||p.invert_z||p.head_tail!=0||p.use_bbone_shape||p.use_offset)return "仅支持全轴、无反相／偏移／尾部／B-Bone";
            if(e.Type=="COPY_ROTATION"&&p.mix_mode!="REPLACE")return "Copy Rotation 首版只支持 REPLACE";
            return null;
        }

        internal static string StackBarrier(Entry e)
        {
            var unknown=e.config.unknownConstraints.Where(x=>x.ownerBone==e.Owner).ToList();
            if(unknown.Count==0)return null;
            bool positionOnly=e.config.knownConstraints.Where(x=>x.ownerBone==e.Owner).All(x=>
                x.relationType=="DIRECT_CONSTRAINT"&&x.constraint.constraintType=="COPY_LOCATION");
            bool rotationOnlyUnknown=unknown.All(x=>x.constraint.constraintType=="DAMPED_TRACK"||x.constraint.constraintType=="LOCKED_TRACK");
            return positionOnly&&rotationOnlyUnknown?null:"同一 Owner 有影响当前通道的 unknown 栈屏障";
        }

        internal static Prepared Prepare(ConstraintConfig config,GameObject rig,Transform root,
            Dictionary<string,Transform> map,Backend backend)
        {
            var result=new Prepared {config=config,rig=rig,root=root,map=map,backend=backend};
            var entries=Build(config);
            if(entries.Count==0)return result;
            result.bind=new Bindings(config,rig.transform,map,root);
            var rejected=new HashSet<string>(StringComparer.Ordinal);
            foreach(var group in entries.GroupBy(e=>e.Owner))
            {
                string issue=null;
                if(!map.TryGetValue(group.Key,out Transform owner)||owner==rig.transform)issue="Owner 缺失／不能驱动 rig-space 本身";
                issue=issue??StackBarrier(group.First());
                if(config.knownConstraints.Any(x=>x.ownerBone==group.Key&&x.relationType!="DIRECT_CONSTRAINT"&&x.relationType!="AIM_BINDING"))issue="同一 Owner 混合了旧 Aux/MCH 与通用栈，暂不跨语义重排";
                var binding=config.bindingContext.bones.FirstOrDefault(b=>b.boneName==group.Key);
                if(binding==null||!Identity(binding.poseBasis)||binding.connected&&group.Any(e=>e.Type=="COPY_LOCATION"||e.Type=="ARMATURE")||binding.inheritScale!="FULL")issue="Owner 需要 FULL 继承和静置输入，位置操作需非连接骨";
                if(binding!=null&&!string.IsNullOrEmpty(binding.parentBone)&&
                   (!map.TryGetValue(binding.parentBone,out Transform parent)||owner==null||owner.parent!=parent))issue="Owner 父级与导出绑定不一致";
                if(owner!=null&&backend==Backend.Standard&&owner.GetComponents<Component>().Any(c=>(c is IConstraint)&&!Managed(owner,c)))issue="Owner 有手工标准约束，不覆盖";
                if(owner!=null)issue=issue??SceneIssue(owner,rig);
                foreach(var e in group)
                {
                    issue=issue??Issue(e,backend);
                    string notice=DependencyNotice(e);
                    if(notice!=null)Debug.LogWarning("HoFBX direct "+e.Owner+"："+notice);
                    foreach(string name in References(e))
                        if(!map.TryGetValue(name,out Transform target)||target==owner||!result.bind.rest.ContainsKey(name))issue="目标骨缺失、引用自身或没有绑定矩阵："+name;
                }
                if(issue!=null){rejected.Add(group.Key);Debug.LogWarning("HoFBX direct "+group.Key+"："+issue);}
            }
            var remaining=entries.Where(e=>!rejected.Contains(e.Owner)&&!e.Muted).ToList();
            // Reject cycles before clearing prior imports or allocating helpers.
            var ordered=Topological(remaining,map);
            result.entries.AddRange(ordered);
            if(backend==Backend.Vrc&&result.entries.Count>0)
                foreach(string type in new[]{"Position","Rotation","Scale","Parent","Aim"})
                    HoVrcConstraintBridge.ValidateDirectApi(type);
            return result;
        }

        internal static string SceneIssue(Transform owner,GameObject rig)
        {
            if(owner==null)return "目标骨架缺少 Owner";
            var animators=rig.GetComponentsInChildren<Animator>(true).Concat(rig.GetComponentsInParent<Animator>(true)).Distinct();
            foreach(var animator in animators)
            {
                if(owner!=animator.transform&&!owner.IsChildOf(animator.transform))continue;
                if(animator.isHuman)
                {
                    try {for(int i=0;i<(int)HumanBodyBones.LastBone;i++)if(animator.GetBoneTransform((HumanBodyBones)i)==owner)return "Owner 由 Humanoid 动画驱动；通用栈首版不覆盖其动画输入";}
                    catch(InvalidOperationException){return "无法验证 Humanoid Owner 输入";}
                }
                if(animator.runtimeAnimatorController==null)continue;
                string path=HoAuxRig.GetRelativePath(animator.transform,owner);
                if(animator.runtimeAnimatorController.animationClips.Any(clip=>AnimationUtility.GetCurveBindings(clip).Any(b=>b.type==typeof(Transform)&&b.path==path)))return "Owner 有独立 Transform 动画；通用栈首版需要静置局部输入";
            }
            return null;
        }

        internal static List<Entry> Topological(List<Entry> entries,Dictionary<string,Transform> map)
        {
            var groups=entries.GroupBy(e=>e.Owner).ToDictionary(g=>g.Key,g=>g.OrderBy(e=>e.Index).ToList());
            var done=new HashSet<string>();var visiting=new HashSet<string>();var result=new List<Entry>();
            Action<string> visit=null;
            visit=name=>{
                if(done.Contains(name))return;
                if(!visiting.Add(name))throw new InvalidOperationException("通用约束存在循环依赖："+name+"；未清除现有导入");
                var dependencies=new HashSet<string>();
                Transform owner=map[name];
                for(Transform p=owner.parent;p!=null;p=p.parent)if(groups.ContainsKey(p.name)){dependencies.Add(p.name);break;}
                foreach(var e in groups[name])foreach(string source in References(e))
                    for(Transform p=map[source];p!=null;p=p.parent)if(groups.ContainsKey(p.name)){dependencies.Add(p.name);break;}
                foreach(string d in dependencies.OrderBy(x=>x,StringComparer.Ordinal))visit(d);
                visiting.Remove(name);done.Add(name);result.AddRange(groups[name]);
            };
            foreach(string name in groups.Keys.OrderBy(x=>x,StringComparer.Ordinal))visit(name);
            return result;
        }

        private static IEnumerable<string> References(Entry e)
        {
            if(e.aim!=null){yield return e.aim.targetBone;yield return e.aim.upBone;}
            else if(e.Type=="ARMATURE")
            {
                if(e.raw.constraint.references!=null&&e.raw.constraint.references.targets!=null)
                    foreach(var t in e.raw.constraint.references.targets)if(t!=null&&t.properties!=null&&!string.IsNullOrEmpty(t.properties.subtarget))yield return t.properties.subtarget;
            }
            else if(!string.IsNullOrEmpty(e.Target))yield return e.Target;
        }
        private static bool Managed(Transform owner,Component c)
        { var marker=owner.GetComponent<HoImportedConstraintMarker>();return marker!=null&&marker.Manages(c); }
        private static string Key(string owner,int index){return owner+"\u001f"+index;}
        private static bool Finite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
        private static bool ValidMatrix(float[] values)
        {
            if(values==null||values.Length!=16||!values.All(Finite)||Mathf.Abs(values[12])+Mathf.Abs(values[13])+Mathf.Abs(values[14])>0.00001f||Mathf.Abs(values[15]-1)>0.00001f)return false;
            float determinant=Matrix(values).determinant;return Finite(determinant)&&Mathf.Abs(determinant)>1e-20f;
        }
        private static bool Identity(float[] values)
        { if(!ValidMatrix(values))return false;for(int i=0;i<16;i++)if(Mathf.Abs(values[i]-(i%5==0?1:0))>0.00001f)return false;return true; }
        private static Matrix4x4 Matrix(float[] values)
        {var m=new Matrix4x4();for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=values[r*4+c];return m;}
        internal static bool TrackAxis(string name,out Vector3 axis)
        {
            axis=Vector3.zero;
            if(name=="TRACK_X")axis=Vector3.right;else if(name=="TRACK_Y")axis=Vector3.up;else if(name=="TRACK_Z")axis=Vector3.forward;
            else if(name=="TRACK_NEGATIVE_X")axis=Vector3.left;else if(name=="TRACK_NEGATIVE_Y")axis=Vector3.down;else if(name=="TRACK_NEGATIVE_Z")axis=Vector3.back;
            return axis!=Vector3.zero;
        }

        internal sealed class Bindings
        {
            internal readonly Dictionary<string,Matrix4x4> rest=new Dictionary<string,Matrix4x4>();
            internal readonly Dictionary<string,Matrix4x4> basis=new Dictionary<string,Matrix4x4>();
            internal readonly Dictionary<string,Matrix4x4> local=new Dictionary<string,Matrix4x4>();
            internal Bindings(ConstraintConfig config,Transform rig,Dictionary<string,Transform> map,Transform host)
            {
                var cached=new Dictionary<Transform,Matrix4x4>();
                var old=new List<HoAuxRig>(rig.GetComponentsInChildren<HoAuxRig>(true));
                if(host!=null)old.AddRange(host.GetComponents<HoAuxRig>());
                foreach(var b in config.bindingContext.bones)
                {
                    if(!map.TryGetValue(b.boneName,out Transform t))continue;
                    Matrix4x4 value=BindWorld(t,rig,cached,old);
                    rest[b.boneName]=rig.worldToLocalMatrix*value;
                    local[b.boneName]=t.parent==null?value:BindWorld(t.parent,rig,cached,old).inverse*value;
                }
                bool needsRotationBasis=config.aimBindings.Count>0||config.knownConstraints.Any(k=>k.relationType=="DIRECT_CONSTRAINT"&&k.constraint.constraintType=="COPY_ROTATION");
                if(!needsRotationBasis)
                {
                    foreach(string name in rest.Keys)basis[name]=Matrix4x4.identity;
                    return;
                }
                Matrix4x4 conversion=Fit(config.bindingContext,rest);
                foreach(var b in config.bindingContext.bones)
                    if(rest.ContainsKey(b.boneName))basis[b.boneName]=Matrix(b.restMatrix).inverse*conversion.inverse*rest[b.boneName];
            }
            private static Matrix4x4 BindWorld(Transform t,Transform rig,Dictionary<Transform,Matrix4x4> cache,List<HoAuxRig> old)
            {
                if(t==null)return Matrix4x4.identity;if(t==rig)return rig.localToWorldMatrix;
                if(cache.TryGetValue(t,out Matrix4x4 result))return result;
                var source=PrefabUtility.GetCorrespondingObjectFromSource(t);
                var marker=t.GetComponent<HoImportedConstraintMarker>();
                var prior=old.SelectMany(x=>x.DirectOperations).FirstOrDefault(x=>x.owner==t);
                Matrix4x4 local;
                if(marker!=null&&marker.hasOwnerBindPose)local=Matrix4x4.TRS(marker.bindLocalPosition,marker.bindLocalRotation,marker.bindLocalScale);
                else if(prior!=null)local=Matrix4x4.TRS(prior.bindLocalPosition,prior.bindLocalRotation,prior.bindLocalScale);
                else {var b=source!=null?source:t;local=Matrix4x4.TRS(b.localPosition,b.localRotation,b.localScale);}
                result=BindWorld(t.parent,rig,cache,old)*local;cache[t]=result;return result;
            }
            private static Matrix4x4 Fit(ConstraintBindingContext context,Dictionary<string,Matrix4x4> unity)
            {
                var bones=context.bones.Where(b=>unity.ContainsKey(b.boneName)).ToList();
                if(bones.Count==0)throw new InvalidOperationException("IR v3 找不到配套绑定骨骼");
                Vector3 origin=Matrix(bones[0].restMatrix).GetColumn(3);
                var second=bones.OrderByDescending(b=>Vector3.SqrMagnitude((Vector3)Matrix(b.restMatrix).GetColumn(3)-origin)).First();
                Vector3 x=(Vector3)Matrix(second.restMatrix).GetColumn(3)-origin;
                var third=bones.OrderByDescending(b=>Vector3.Cross(x,(Vector3)Matrix(b.restMatrix).GetColumn(3)-origin).sqrMagnitude).First();
                Vector3 y=(Vector3)Matrix(third.restMatrix).GetColumn(3)-origin;
                if(bones.Count<3||Vector3.Cross(x,y).sqrMagnitude<1e-14f)
                {
                    if(context.fbxBoneAxes!="Y_X")throw new InvalidOperationException("绑定位置不足以验证旋转基底；需要配套 HoFBX 的 Y/X 骨轴声明");
                    Matrix4x4 b=Matrix(bones[0].restMatrix),u=unity[bones[0].boneName];
                    float unit=x.magnitude>1e-8f?Vector3.Distance(unity[second.boneName].GetColumn(3),u.GetColumn(3))/x.magnitude:1;
                    Matrix4x4 c=Matrix4x4.Rotate(u.rotation)*Matrix4x4.Scale(new Vector3(-unit,unit,unit))*Matrix4x4.Rotate(Quaternion.Inverse(b.rotation));
                    Vector3 offset=(Vector3)u.GetColumn(3)-c.MultiplyVector(origin);c.SetColumn(3,new Vector4(offset.x,offset.y,offset.z,1));
                    if(bones.Any(item=>Vector3.Distance(c.MultiplyPoint3x4(Matrix(item.restMatrix).GetColumn(3)),unity[item.boneName].GetColumn(3))>0.0001f))throw new InvalidOperationException("HoFBX 骨轴声明与绑定位置不匹配");
                    foreach(var item in bones)
                    {
                        Matrix4x4 k=Matrix(item.restMatrix).inverse*c.inverse*unity[item.boneName];
                        if(Vector3.Dot(k.MultiplyVector(Vector3.right).normalized,Vector3.left)<.9999f||Vector3.Dot(k.MultiplyVector(Vector3.up).normalized,Vector3.up)<.9999f||Vector3.Dot(k.MultiplyVector(Vector3.forward).normalized,Vector3.forward)<.9999f)
                            throw new InvalidOperationException("HoFBX Y/X 骨轴与配套静置方向不匹配");
                    }
                    return c;
                }
                Vector3 uo=unity[bones[0].boneName].GetColumn(3),ux=(Vector3)unity[second.boneName].GetColumn(3)-uo,uy=(Vector3)unity[third.boneName].GetColumn(3)-uo;
                float scale=ux.magnitude/x.magnitude;
                var bframe=Frame(x,y);var uframe=Frame(ux,uy);float best=float.PositiveInfinity;Matrix4x4 answer=Matrix4x4.identity;
                foreach(float sign in new[]{1f,-1f})
                {
                    Matrix4x4 c=uframe*Matrix4x4.Scale(new Vector3(scale,scale,scale*sign))*bframe.transpose;
                    Vector3 offset=uo-c.MultiplyVector(origin);c.SetColumn(3,new Vector4(offset.x,offset.y,offset.z,1));
                    float error=bones.Max(b=>Vector3.Distance(c.MultiplyPoint3x4(Matrix(b.restMatrix).GetColumn(3)),unity[b.boneName].GetColumn(3)));
                    if(error<best){best=error;answer=c;}
                }
                if(best>0.0001f)throw new InvalidOperationException("FBX 与 IR 绑定位置不匹配（"+best+"）；未清除现有导入");
                return answer;
            }
            private static Matrix4x4 Frame(Vector3 x,Vector3 y)
            {x.Normalize();y=Vector3.ProjectOnPlane(y,x).normalized;var m=Matrix4x4.identity;m.SetColumn(0,x);m.SetColumn(1,y);m.SetColumn(2,Vector3.Cross(x,y));return m;}
        }
    }
}
#endif
