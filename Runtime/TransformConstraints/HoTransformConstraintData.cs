using System;
using UnityEngine;

namespace Hollow.HoUnityTools.TransformConstraints
{
    public enum HoTransformConstraintKind { Position, Rotation, Scale, Parent, Aim }
    public enum HoTransformConstraintPhase
    {
        [InspectorName("更新（Update）")] Update,
        [InspectorName("动画后（LateUpdate）")] LateUpdate,
        [InspectorName("固定更新（FixedUpdate）")] FixedUpdate,
        [InspectorName("手动")] Manual
    }

    public enum HoTransformConstraintSpace
    {
        [InspectorName("世界")] World,
        [InspectorName("本地")] Local
    }

    public enum HoTransformConstraintInput
    {
        [InspectorName("绑定姿态")] RestPose,
        [InspectorName("当帧动画")] Animated
    }

    public enum HoAimUpMode
    {
        [InspectorName("场景向上")] SceneUp,
        [InspectorName("世界方向")] WorldVector,
        [InspectorName("参考物体位置")] ObjectPosition,
        [InspectorName("参考物体旋转")] ObjectRotation
    }
    public enum HoSignedAxis { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }
    [Flags] public enum HoTransformChannels { None=0, Position=1, Rotation=2, Scale=4 }
    [Flags] public enum HoTransformAxes { None=0, X=1, Y=2, Z=4, All=7 }

    [Serializable]
    public sealed class HoTransformConstraintSource
    {
        public Transform transform;
        [Min(0)] public float weight=1;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        public Vector3 scaleMultiplier=Vector3.one;
        [HideInInspector] public Transform offsetSource;
    }

    [Serializable]
    public struct HoTransformLocalPose
    {
        public Vector3 position,scale;
        public Quaternion rotation;
        public static HoTransformLocalPose Read(Transform t)
        { return new HoTransformLocalPose {position=t.localPosition,rotation=t.localRotation,scale=t.localScale}; }
        public void Apply(Transform t,HoTransformChannels channels)
        {
            if(t==null)return;
            if((channels&HoTransformChannels.Position)!=0)t.localPosition=position;
            if((channels&HoTransformChannels.Rotation)!=0)t.localRotation=rotation;
            if((channels&HoTransformChannels.Scale)!=0)t.localScale=scale;
        }
        public void CopyFrom(HoTransformLocalPose other,HoTransformChannels channels)
        {
            if((channels&HoTransformChannels.Position)!=0)position=other.position;
            if((channels&HoTransformChannels.Rotation)!=0)rotation=other.rotation;
            if((channels&HoTransformChannels.Scale)!=0)scale=other.scale;
        }
    }
}
