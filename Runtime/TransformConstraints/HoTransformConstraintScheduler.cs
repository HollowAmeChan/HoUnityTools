using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hollow.HoUnityTools.TransformConstraints
{
    /// <summary>Explicit transform dependencies, independent of component hosts.</summary>
    public static class HoTransformConstraintScheduler
    {
        private static readonly HashSet<HoTransformConstraint> registry=new HashSet<HoTransformConstraint>();
        private static int updateFrame=-1,lateFrame=-1;
        private static double fixedTime=double.NaN;
        public static IEnumerable<HoTransformConstraint> Registered=>registry.Where(c=>c!=null).ToArray();
        internal static void Register(HoTransformConstraint c){registry.Add(c);}
        internal static void Unregister(HoTransformConstraint c){registry.Remove(c);}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClock(){updateFrame=lateFrame=-1;fixedTime=double.NaN;registry.RemoveWhere(c=>c==null);}

        internal static void Tick(HoTransformConstraintPhase phase)
        {
            if(phase==HoTransformConstraintPhase.Update){if(updateFrame==Time.frameCount)return;updateFrame=Time.frameCount;}
            if(phase==HoTransformConstraintPhase.LateUpdate){if(lateFrame==Time.frameCount)return;lateFrame=Time.frameCount;}
            if(phase==HoTransformConstraintPhase.FixedUpdate){if(fixedTime==Time.fixedTimeAsDouble)return;fixedTime=Time.fixedTimeAsDouble;}
            Evaluate(registry.Where(c=>c!=null&&Application.IsPlaying(c.gameObject)&&c.Phase==phase).ToArray());
        }

        public static void Evaluate(IEnumerable<HoTransformConstraint> requested)
        {
            var nodes=requested.Where(c=>c!=null&&c.isActiveAndEnabled&&c.ConstraintActive).Distinct().ToList();
            var blocked=new HashSet<HoTransformConstraint>();
            foreach(var c in nodes)
            {
                c.LastError=null;
                if(!c.ValidateConfiguration(out string error)){c.LastError=error;blocked.Add(c);}
            }
            var potentialWriters=nodes.Where(c=>c.TargetTransform!=null&&c.Weight>0&&c.WrittenChannels!=0).ToList();
            nodes=nodes.Where(c=>!blocked.Contains(c)&&c.Weight>0&&c.WrittenChannels!=0).ToList();
            var edges=nodes.ToDictionary(c=>c,c=>new HashSet<HoTransformConstraint>());
            foreach(var group in nodes.GroupBy(c=>c.TargetTransform))
            {
                var writers=group.OrderBy(c=>c.Order).ThenBy(c=>Priority(c.Kind)).ThenBy(c=>c.GetInstanceID()).ToList();
                for(int i=0;i<writers.Count;i++)for(int j=i+1;j<writers.Count;j++)
                    if(writers[i].Order==writers[j].Order&&(writers[i].WrittenChannels&writers[j].WrittenChannels)!=0)
                    {writers[i].LastError=writers[j].LastError="同一 Target／通道有相同 Order 的多个写者，请设置不同 Order";blocked.Add(writers[i]);blocked.Add(writers[j]);}
                for(int i=1;i<writers.Count;i++)edges[writers[i]].Add(writers[i-1]);
            }
            foreach(var c in nodes)
            {
                var inputs=c.Sources.Where(s=>s!=null&&s.weight>0).Select(s=>s.transform).ToList();
                if(c.TargetTransform.parent!=null)inputs.Add(c.TargetTransform.parent);
                if(c is HoAimConstraint aim&&(aim.UpMode==HoAimUpMode.ObjectPosition||aim.UpMode==HoAimUpMode.ObjectRotation))inputs.Add(aim.UpObject);
                foreach(var source in inputs)
                    foreach(var writer in potentialWriters)
                        if(writer!=c&&writer.TargetTransform!=c.TargetTransform&&source!=null&&
                           (source==writer.TargetTransform||source.IsChildOf(writer.TargetTransform)))edges[c].Add(writer);
            }
            var order=new List<HoTransformConstraint>();var state=new Dictionary<HoTransformConstraint,int>();
            Func<HoTransformConstraint,bool> visit=null;
            visit=c=>{
                if(blocked.Contains(c))return false;
                if(state.TryGetValue(c,out int value))
                {if(value==2)return true;c.LastError="约束依赖存在循环";blocked.Add(c);return false;}
                state[c]=1;
                foreach(var dependency in edges[c])if(!visit(dependency))
                {c.LastError=c.LastError??"上游约束无效或存在循环";blocked.Add(c);state[c]=2;return false;}
                state[c]=2;order.Add(c);return true;
            };
            foreach(var c in nodes.OrderBy(c=>c.Order).ThenBy(c=>Priority(c.Kind)))visit(c);
            // Snapshot local input before any graph writes; animated input is then
            // composed with this phase's updated parent when the node evaluates.
            var input=nodes.Select(c=>c.TargetTransform).Distinct().ToDictionary(t=>t,HoTransformLocalPose.Read);
            var written=new Dictionary<Transform,HoTransformChannels>();
            foreach(var c in order)
            {
                if(blocked.Contains(c))continue;
                if(edges[c].Any(d=>d.LastError!=null)){c.LastError="上游约束求值失败";continue;}
                written.TryGetValue(c.TargetTransform,out HoTransformChannels channels);
                if(!HoTransformConstraintMath.Evaluate(c,input[c.TargetTransform],channels,out string error)){c.LastError=error;continue;}
                written[c.TargetTransform]=channels|c.WrittenChannels;
            }
        }
        private static int Priority(HoTransformConstraintKind kind)
        {switch(kind){case HoTransformConstraintKind.Position:return 0;case HoTransformConstraintKind.Parent:return 1;case HoTransformConstraintKind.Scale:return 2;case HoTransformConstraintKind.Rotation:return 3;default:return 4;}}
    }
}
