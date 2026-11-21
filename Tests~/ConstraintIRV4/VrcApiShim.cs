// Structural API test double only. Run.ps1 installs this in an isolated project.
// No VRChat numerical/runtime compatibility is inferred from these tests.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace VRC.Dynamics
{
    [Serializable] public struct VRCConstraintSource
    {
        public Transform SourceTransform;
        public float Weight;
        public Vector3 ParentPositionOffset,ParentRotationOffset;
    }
    [Serializable] public class VRCConstraintSourceKeyableList
    {
        public const int MaxFlatLength=16;
        public VRCConstraintSource source0,source1,source2,source3,source4,source5,source6,source7,
            source8,source9,source10,source11,source12,source13,source14,source15;
        public int totalLength;
        public List<VRCConstraintSource> overflowList=new List<VRCConstraintSource>();
    }
}
namespace VRC.SDK3.Dynamics.Constraint.Components
{
    public enum ShimWorldUp {SceneUp,ObjectUp,ObjectRotationUp,Vector,None}
    public class VRCConstraint : MonoBehaviour
    {
        public Transform TargetTransform,WorldUpTransform;
        public bool IsActive,Locked,SolveInLocalSpace;
        public float GlobalWeight=1;
        public bool AffectsPositionX=true,AffectsPositionY=true,AffectsPositionZ=true;
        public bool AffectsRotationX=true,AffectsRotationY=true,AffectsRotationZ=true;
        public bool AffectsScaleX=true,AffectsScaleY=true,AffectsScaleZ=true;
        public Vector3 AimAxis=Vector3.forward,UpAxis=Vector3.up;
        public ShimWorldUp WorldUp;
        public VRC.Dynamics.VRCConstraintSourceKeyableList Sources=new VRC.Dynamics.VRCConstraintSourceKeyableList();
        public int applyCalls,zeroCalls;
        public static bool ThrowOnZero;
        public void ApplyConfigurationChanges(){applyCalls++;}
        public void ZeroConstraint(){if(ThrowOnZero)throw new InvalidOperationException("Injected SDK configuration failure");zeroCalls++;IsActive=true;Locked=true;}
        public void ActivateConstraint(){throw new InvalidOperationException("Direct Copy must not bake offsets");}
    }
}
