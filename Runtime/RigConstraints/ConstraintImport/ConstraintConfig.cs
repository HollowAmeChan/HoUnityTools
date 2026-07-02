using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints.Import
{
    /// <summary>
    /// 约束配置数据结构 - 用于从外部文件导入骨架约束定义
    /// 注意：此模块专门用于导入标准 Unity Constraint 到骨架系统
    /// </summary>
    [System.Serializable]
    public class ConstraintConfig
    {
        /// <summary>
        /// 导出格式版本（HoTools Blender 导出器写入，如 "1.0"）
        /// </summary>
        public string version;

        /// <summary>
        /// 导出时间戳（ISO8601）
        /// </summary>
        public string exportTime;

        /// <summary>
        /// 源骨架名称
        /// </summary>
        public string armatureName;

        public List<BoneConstraint> bones;
    }

    [System.Serializable]
    public class BoneConstraint
    {
        public string boneName;
        public List<ConstraintInfo> constraints;
    }

    [System.Serializable]
    public class ConstraintInfo
    {
        /// <summary>
        /// 约束类型：Rotation, Location, Scale, Child
        /// </summary>
        public string type;

        /// <summary>
        /// HoTools 语义标记：fan / twist / generic。
        /// 由 Blender 导出器识别辅助骨约束语义后写入，Unity 端可据此选择处理策略。
        /// </summary>
        public string semantic;

        /// <summary>
        /// 语义子类型。由 Blender 导出端按 semantic 写入，例如 fan 的 FAN / FAN_SINGLE / FAN_SIDE。
        /// </summary>
        public string subType;

        /// <summary>
        /// Twist 链的源骨名（仅 semantic="twist" 时有效）
        /// </summary>
        public string sourceBone;

        /// <summary>
        /// 目标骨骼路径或名称
        /// </summary>
        public string targetPath;

        /// <summary>
        /// 约束权重 (0-1)
        /// </summary>
        [Range(0, 1)]
        public float weight;

        /// <summary>
        /// 空间参数（source/target 空间，如 world→world）。
        /// </summary>
        public SpaceInfo space;

        /// <summary>
        /// 轴向开关（Rotation/Location/Scale）。对 RotationConstraint 来说对应 Unity 的冻结/约束轴。
        /// fan/twist 的轴向预设由 Blender 导出端写入。
        /// </summary>
        public AxesInfo axes;
    }

    /// <summary>
    /// 约束空间参数。对应 Blender 的 owner_space / target_space。
    /// </summary>
    [System.Serializable]
    public class SpaceInfo
    {
        public string source;
        public string target;
    }

    /// <summary>
    /// 约束轴向开关。对应 Blender COPY_ROTATION/COPY_LOCATION 的 use_x/use_y/use_z。
    /// </summary>
    [System.Serializable]
    public class AxesInfo
    {
        public bool x;
        public bool y;
        public bool z;
    }
}
