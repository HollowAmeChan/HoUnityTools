using System.Collections.Generic;

namespace Hollow.HoUnityTools.RigConstraints.Import
{
    /// <summary>
    /// In-memory backend plan produced exclusively from validated IR v4.
    /// This is not a JSON protocol and is never serialized into imported assets.
    /// </summary>
    public sealed class HoConstraintExecutionPlan
    {
        public readonly ConstraintIR.HoConstraintIrImportContext source;
        public string exportTime { get { return source.Document.Root.StringAt("exportTime"); } }
        public string armatureName { get { return source.Document.Root.StringAt("armatureName"); } }
        public int SourceSchemaVersion { get { return source.Document.Root.Get("schemaVersion").Int32(); } }
        public readonly List<MchBindingInfo> mchBindings = new List<MchBindingInfo>();
        public readonly List<AuxBoneInfo> composites = new List<AuxBoneInfo>();
        public readonly List<HoConstraintPlannedOperation> operations = new List<HoConstraintPlannedOperation>();
        public readonly List<HoConstraintExecutionBarrier> barriers = new List<HoConstraintExecutionBarrier>();
        public readonly List<AimBindingInfo> aimBindings = new List<AimBindingInfo>();
        public ConstraintBindingContext finalBindings;
        public HoConstraintExecutionPlan(ConstraintIR.HoConstraintIrImportContext source)
        { this.source = source ?? throw new System.ArgumentNullException("source"); }
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
        public string semanticId, semanticType;
        public string boneName;
        public string auxType;
        public List<string> sourceBones;
        public List<BlenderConstraintInfo> constraints;
    }

    public class HoConstraintPlannedOperation
    {
        public string semanticId, semanticType, sourceId;
        public string ownerBone;
        public string operationRole;
        public string auxType;
        public BlenderConstraintInfo constraint;
    }

    public class HoConstraintExecutionBarrier
    {
        public string sourceId;
        public string ownerBone;
        public string code;
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
        public bool inheritRotation = true;
    }
    [System.Serializable] public class ConstraintBindingContext
    {
        public string space, matrixLayout, fbxBoneAxes;
        public float[] objectMatrix;
        public List<ConstraintBoneBinding> bones;
    }
}
