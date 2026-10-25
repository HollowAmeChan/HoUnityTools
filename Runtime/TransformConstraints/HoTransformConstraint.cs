using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.TransformConstraints
{
    /// <summary>Configuration lives on this host; only TargetTransform is driven.</summary>
    [ExecuteAlways]
    public abstract class HoTransformConstraint : MonoBehaviour
    {
        [SerializeField] private Transform targetTransform;
        [SerializeField] private List<HoTransformConstraintSource> sources=new List<HoTransformConstraintSource>();
        [SerializeField] private bool constraintActive;
        [SerializeField,Range(0,1)] private float weight=1;
        [SerializeField] private int order;
        [SerializeField] private HoTransformConstraintPhase phase=HoTransformConstraintPhase.LateUpdate;
        [SerializeField] private HoTransformConstraintSpace space=HoTransformConstraintSpace.World;
        [SerializeField] private HoTransformConstraintInput input=HoTransformConstraintInput.RestPose;
        [SerializeField] private bool previewInEditMode;
        [SerializeField] private HoTransformAxes positionAxes=HoTransformAxes.All;
        [SerializeField] private HoTransformAxes rotationAxes=HoTransformAxes.All;
        [SerializeField] private HoTransformAxes scaleAxes=HoTransformAxes.All;
        [SerializeField] private bool drawGizmos=true;
        [SerializeField,HideInInspector] private bool hasRestPose;
        [SerializeField,HideInInspector] private Transform boundTarget;
        [SerializeField,HideInInspector] private HoTransformLocalPose restPose;

        public abstract HoTransformConstraintKind Kind { get; }
        public Transform TargetTransform {get=>targetTransform;set=>targetTransform=value;}
        public IList<HoTransformConstraintSource> Sources=>sources;
        public bool ConstraintActive {get=>constraintActive;set=>constraintActive=value;}
        public float Weight {get=>weight;set=>weight=Mathf.Clamp01(value);}
        public int Order {get=>order;set=>order=value;}
        public HoTransformConstraintPhase Phase {get=>phase;set=>phase=value;}
        public HoTransformConstraintSpace Space {get=>space;set=>space=value;}
        public HoTransformConstraintInput Input {get=>input;set=>input=value;}
        public bool PreviewInEditMode {get=>previewInEditMode;set=>previewInEditMode=value;}
        public HoTransformAxes PositionAxes {get=>positionAxes;set=>positionAxes=value;}
        public HoTransformAxes RotationAxes {get=>rotationAxes;set=>rotationAxes=value;}
        public HoTransformAxes ScaleAxes {get=>scaleAxes;set=>scaleAxes=value;}
        public bool HasRestPose=>hasRestPose&&boundTarget==targetTransform;
        public HoTransformLocalPose RestPose=>restPose;
        public string LastError {get;internal set;}
        public HoTransformChannels WrittenChannels
        {
            get
            {
                if(Kind==HoTransformConstraintKind.Position)return positionAxes==0?0:HoTransformChannels.Position;
                if(Kind==HoTransformConstraintKind.Rotation||Kind==HoTransformConstraintKind.Aim)return rotationAxes==0?0:HoTransformChannels.Rotation;
                if(Kind==HoTransformConstraintKind.Scale)return scaleAxes==0?0:HoTransformChannels.Scale;
                return (positionAxes==0?0:HoTransformChannels.Position)|(rotationAxes==0?0:HoTransformChannels.Rotation);
            }
        }

        protected virtual void OnEnable(){HoTransformConstraintScheduler.Register(this);}
        protected virtual void OnDisable(){HoTransformConstraintScheduler.Unregister(this);}
        protected virtual void OnDestroy(){HoTransformConstraintScheduler.Unregister(this);}
        protected virtual void OnValidate(){weight=Mathf.Clamp01(weight);}
        private void Update(){if(Application.IsPlaying(gameObject)&&phase==HoTransformConstraintPhase.Update)HoTransformConstraintScheduler.Tick(phase);}
        private void LateUpdate(){if(Application.IsPlaying(gameObject)&&phase==HoTransformConstraintPhase.LateUpdate)HoTransformConstraintScheduler.Tick(phase);}
        private void FixedUpdate(){if(Application.IsPlaying(gameObject)&&phase==HoTransformConstraintPhase.FixedUpdate)HoTransformConstraintScheduler.Tick(phase);}

        public bool ValidateConfiguration(out string error,bool requireBinding=true)
        {
            error=null;
            if(targetTransform==null){error="请指定被驱动 Target；组件宿主不会自动作为 Target";return false;}
            if(requireBinding&&!HasRestPose){error="尚未捕获基础姿态，或 Target 已更换；请重新启用／捕获";return false;}
            if(!Finite(weight)||weight<0||weight>1){error="总体权重必须在 0–1 之间";return false;}
            double total=0;
            foreach(var source in sources)
            {
                if(source==null||!Finite(source.weight)||source.weight<0){error="来源权重无效";return false;}
                if(source.weight==0)continue;
                if(source.transform==null){error="正权重来源缺少 Transform";return false;}
                if(source.transform==targetTransform||source.transform.IsChildOf(targetTransform)){error="来源不能是 Target 或其子级（循环依赖）";return false;}
                if(requireBinding&&source.offsetSource!=null&&source.offsetSource!=source.transform){error="来源已替换，请重新捕获偏移";return false;}
                if(!Finite(source.positionOffset)||!Finite(source.rotationOffset)||!Finite(source.scaleMultiplier)){error="来源偏移包含非法数值";return false;}
                total+=source.weight;
            }
            if(total<=0||double.IsInfinity(total)){error="至少需要一个正权重来源";return false;}
            if(this is HoAimConstraint aim&&!aim.ValidateAim(out error))return false;
            return true;
        }

        public bool Activate(bool keepCurrentOffset,out string error)
        {
            if(!ValidateConfiguration(out error,false))
                return false;
            var pose=HoTransformLocalPose.Read(targetTransform);
            Quaternion aimOffset=Quaternion.identity;
            if(this is HoAimConstraint aim&&keepCurrentOffset)
            {
                if(!HoTransformConstraintMath.TryAimRotation(aim,out Quaternion desired,out error))return false;
                aimOffset=Quaternion.Inverse(desired)*targetTransform.rotation;
            }
            // Pre-compute everything before changing serialized binding state.
            var positions=new Vector3[sources.Count];var rotations=new Vector3[sources.Count];var scales=new Vector3[sources.Count];
            for(int i=0;i<sources.Count;i++)
            {
                var s=sources[i];if(s==null)continue;scales[i]=Vector3.one;
                if(!keepCurrentOffset||s.weight==0)continue;
                if(Kind==HoTransformConstraintKind.Aim)positions[i]=s.positionOffset;
                Matrix4x4 matrix=space==HoTransformConstraintSpace.World?s.transform.localToWorldMatrix:Matrix4x4.TRS(s.transform.localPosition,s.transform.localRotation,s.transform.localScale);
                if((WrittenChannels&HoTransformChannels.Position)!=0)
                {
                    if(Mathf.Abs(matrix.determinant)<1e-12f){error="来源矩阵不可逆，无法捕获位置偏移";return false;}
                    positions[i]=matrix.inverse.MultiplyPoint3x4(space==HoTransformConstraintSpace.World?targetTransform.position:pose.position);
                }
                Quaternion q=space==HoTransformConstraintSpace.World?s.transform.rotation:s.transform.localRotation;
                rotations[i]=(Quaternion.Inverse(q)*(space==HoTransformConstraintSpace.World?targetTransform.rotation:pose.rotation)).eulerAngles;
                if(Kind==HoTransformConstraintKind.Scale)
                {
                    Vector3 targetScale=space==HoTransformConstraintSpace.World?targetTransform.lossyScale:pose.scale;
                    Vector3 sourceScale=space==HoTransformConstraintSpace.World?s.transform.lossyScale:s.transform.localScale;
                    if(!HoTransformConstraintMath.TryDivide(targetScale,sourceScale,out scales[i])){error="来源缩放为零，无法捕获缩放比例";return false;}
                }
            }
            restPose=pose;boundTarget=targetTransform;hasRestPose=true;
            for(int i=0;i<sources.Count;i++)
            {
                var s=sources[i];if(s==null)continue;
                s.positionOffset=positions[i];s.rotationOffset=rotations[i];s.scaleMultiplier=scales[i];s.offsetSource=s.transform;
            }
            if(this is HoAimConstraint aimConstraint)aimConstraint.RotationOffset=aimOffset.eulerAngles;
            constraintActive=true;LastError=null;return true;
        }

        public void RestoreRestPose(){if(HasRestPose)restPose.Apply(targetTransform,WrittenChannels);}
        public bool EvaluateNow()
        {
            if(!isActiveAndEnabled||!constraintActive){LastError="请先启用组件并捕获／启用约束";return false;}
            HoTransformConstraintScheduler.Evaluate(new[]{this});return LastError==null;
        }
        protected virtual void OnDrawGizmosSelected()
        {
            if(!drawGizmos||targetTransform==null)return;
            Gizmos.color=new Color(.25f,.8f,1,.8f);Gizmos.DrawWireSphere(targetTransform.position,.012f);
            foreach(var s in sources)if(s!=null&&s.transform!=null&&s.weight>0)Gizmos.DrawLine(s.transform.position,targetTransform.position);
        }
        internal static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        internal static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
    }
}
