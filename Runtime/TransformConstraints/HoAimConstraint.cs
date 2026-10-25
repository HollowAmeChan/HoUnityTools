using System.Collections.Generic;
using UnityEngine;
namespace Hollow.HoUnityTools.TransformConstraints
{
    [DefaultExecutionOrder(30000)]
    [AddComponentMenu("HoUnityTools/Transform Constraints/Ho Aim Constraint")]
    public sealed class HoAimConstraint : HoTransformConstraint
    {
        [SerializeField] private HoSignedAxis aimAxis=HoSignedAxis.PositiveZ;
        [SerializeField] private HoSignedAxis upAxis=HoSignedAxis.PositiveY;
        [SerializeField] private HoAimUpMode upMode=HoAimUpMode.SceneUp;
        [SerializeField] private Transform upObject;
        [SerializeField] private Vector3 worldUp=Vector3.up;
        [SerializeField] private Vector3 rotationOffset;
        public override HoTransformConstraintKind Kind=>HoTransformConstraintKind.Aim;
        public HoSignedAxis AimAxis {get=>aimAxis;set=>aimAxis=value;}
        public HoSignedAxis UpAxis {get=>upAxis;set=>upAxis=value;}
        public HoAimUpMode UpMode {get=>upMode;set=>upMode=value;}
        public Transform UpObject {get=>upObject;set=>upObject=value;}
        public Vector3 WorldUp {get=>worldUp;set=>worldUp=value;}
        public Vector3 RotationOffset {get=>rotationOffset;set=>rotationOffset=value;}
        internal bool ValidateAim(out string error)
        {
            error=null;
            if(Space!=HoTransformConstraintSpace.World){error="Aim 使用世界空间几何；首版不支持跨父级的 Local Aim";return false;}
            if((int)aimAxis/2==(int)upAxis/2){error="Aim 轴和 Up 轴必须不同轴";return false;}
            if(upMode==HoAimUpMode.ObjectPosition||upMode==HoAimUpMode.ObjectRotation)
            {
                if(upObject==null){error="请指定 Up Object";return false;}
                if(upObject==TargetTransform||upObject.IsChildOf(TargetTransform)){error="Up Object 不能是 Target 或其子级";return false;}
            }
            if(!Finite(worldUp)||!Finite(rotationOffset)){error="Aim 参数包含非法数值";return false;}
            return true;
        }
    }
}
