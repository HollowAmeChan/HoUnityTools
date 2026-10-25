using System.Collections.Generic;

namespace Hollow.HoUnityTools.RigConstraints.Import
{
    /// <summary>
    /// Blender 导出的中立 Rig 约束 IR。这里不保留旧版 Unity 映射 JSON。
    /// 最终落成标准 Constraint 还是 HoAuxRig，由导入器在预览阶段决定。
    /// </summary>
    [System.Serializable]
    public class ConstraintConfig
    {
        public string schema;
        public int schemaVersion;
        public string exportTime;
        public string armatureName;
        public List<string> mchEnabledBones;
        public List<MchBindingInfo> mchBindings;
        public List<AuxBoneInfo> auxBones;
        public List<KnownConstraintInfo> knownConstraints;
        public List<UnknownConstraintInfo> unknownConstraints;
        public List<AimBindingInfo> aimBindings;
        public ConstraintBindingContext bindingContext;
    }

    [System.Serializable]
    public class MchBindingInfo
    {
        public string sourceBone;
        public string mchBone;
        public BlenderConstraintInfo constraint;
    }

    [System.Serializable]
    public class AuxBoneInfo
    {
        public string boneName;
        public string auxType;
        public List<string> sourceBones;
        public List<string> constraintNames;
        public List<string> involvedBones;
        public List<BlenderConstraintInfo> constraints;
    }

    [System.Serializable]
    public class KnownConstraintInfo
    {
        public string ownerBone;
        public string relationType;
        public string auxBone;
        public string auxType;
        public BlenderConstraintInfo constraint;
    }

    [System.Serializable]
    public class UnknownConstraintInfo
    {
        public string ownerBone;
        public string reason;
        public BlenderConstraintInfo constraint;
    }

    /// <summary>
    /// Blender 原始约束。owner 与关系类型存放在外层，双坐标系保持在 parameters 中。
    /// </summary>
    [System.Serializable]
    public class BlenderConstraintInfo
    {
        public int stackIndex;
        public string name;
        public string constraintType;
        public string targetObjectName;
        public string targetBoneName;
        public BlenderConstraintParameters parameters;
        public BlenderConstraintReferences references;
    }

    /// <summary>
    /// 字段沿用 Blender RNA 名称；Copy Rotation 与 Stretch To 各自保留一份，
    /// owner_space 和 target_space 不会在导入前合并或退化。
    /// </summary>
    [System.Serializable]
    public class BlenderConstraintParameters
    {
        public float influence;
        public bool mute;
        public string owner_space;
        public string target_space;
        public string mix_mode;
        public string euler_order;
        public bool use_x;
        public bool use_y;
        public bool use_z;
        public bool invert_x;
        public bool invert_y;
        public bool invert_z;
        public bool use_offset;
        public float head_tail;
        public float rest_length;
        public string volume;
        public string keep_axis;
        public float bulge;
        public bool use_bulge_min;
        public bool use_bulge_max;
        public float bulge_min;
        public float bulge_max;
        public float bulge_smooth;
        public bool use_location_x;
        public bool use_location_y;
        public bool use_location_z;
        public bool use_rotation_x;
        public bool use_rotation_y;
        public bool use_rotation_z;
        public bool use_scale_x;
        public bool use_scale_y;
        public bool use_scale_z;
        public bool use_bbone_shape;
        public bool use_deform_preserve_volume;
        public bool use_bone_envelopes;
        public bool use_current_location;
        public string track_axis;
        public string lock_axis;
    }

    [System.Serializable] public class BlenderObjectReference { public string name; public string rnaType; }
    [System.Serializable] public class BlenderArmatureTargetProperties
    {
        public BlenderObjectReference target;
        public string subtarget;
        public float weight;
    }
    [System.Serializable] public class BlenderArmatureTarget
    {
        public string rnaType;
        public BlenderArmatureTargetProperties properties;
    }
    [System.Serializable] public class BlenderConstraintReferences
    {
        public List<BlenderArmatureTarget> targets;
    }
    [System.Serializable] public class AimBindingInfo
    {
        public string ownerBone, semanticType, targetBone, upBone, aimAxis, upAxis, upMode;
        public int semanticVersion;
        public bool muted;
        public int[] constraintStackIndices;
    }
    [System.Serializable] public class ConstraintBoneBinding
    {
        public string boneName, parentBone, inheritScale;
        public float[] restMatrix, poseBasis;
        public bool connected, positionOnly, hasPoseAnimation;
    }
    [System.Serializable] public class ConstraintBindingContext
    {
        public string space, matrixLayout, fbxBoneAxes;
        public float[] objectMatrix;
        public List<ConstraintBoneBinding> bones;
    }
}
