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
        /// 目标骨骼路径或名称
        /// </summary>
        public string targetPath;

        /// <summary>
        /// 约束权重 (0-1)
        /// </summary>
        [Range(0, 1)]
        public float weight;
    }
}
