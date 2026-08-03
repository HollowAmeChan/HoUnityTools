using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints.Import
{
    /// <summary>
    /// 导入约束标记 — 记录本骨骼上由 HoTools 约束导入器生成的约束组件。
    ///
    /// 用途：Unity 标准约束（RotationConstraint 等）本身无法携带"由谁创建"的信息，
    /// 因此在每个被导入的骨骼上挂一个本组件，直接引用它所管理的约束组件。
    /// 这样"安全清除"就能只删除导入器生成的约束，而不误删用户手工添加的约束。
    ///
    /// 该组件由导入器自动挂载，不应手动添加（已从添加组件菜单隐藏）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class HoImportedConstraintMarker : MonoBehaviour
    {
        /// <summary>
        /// 源骨架名称（来自导出 JSON 的 armatureName）。
        /// </summary>
        public string sourceArmature;

        /// <summary>
        /// 导入时间戳（ISO8601，来自导出 JSON 的 exportTime）。
        /// </summary>
        public string exportTime;

        /// <summary>
        /// 中立 Rig 约束 IR 版本（来自导出 JSON 的 schemaVersion）。
        /// </summary>
        public string exporterVersion;

        /// <summary>
        /// 本骨骼上由导入器管理的约束组件引用列表。
        /// 约束被销毁后对应引用会变为 Unity 的伪 null，清理时需过滤。
        /// </summary>
        [SerializeField]
        private List<Component> managedConstraints = new List<Component>();

        /// <summary>
        /// 判断指定约束组件是否由本标记管理（即导入器生成）。
        /// </summary>
        public bool Manages(Component constraint)
        {
            if (constraint == null) return false;
            return managedConstraints.Contains(constraint);
        }

        /// <summary>
        /// 登记一个由导入器新建的约束组件。重复登记会被忽略。
        /// </summary>
        public void Register(Component constraint)
        {
            if (constraint == null) return;
            if (!managedConstraints.Contains(constraint))
            {
                managedConstraints.Add(constraint);
            }
        }

        /// <summary>
        /// 返回当前仍存活的受管约束组件（已过滤被销毁的伪 null 引用）。
        /// </summary>
        public IEnumerable<Component> GetLiveConstraints()
        {
            foreach (var c in managedConstraints)
            {
                if (c != null) yield return c;
            }
        }

        /// <summary>
        /// 元数据赋值。仅在字段为空时填充，避免重复导入覆盖首次导入信息。
        /// </summary>
        public void SetMetadata(string armature, string time, string version)
        {
            if (string.IsNullOrEmpty(sourceArmature)) sourceArmature = armature;
            if (string.IsNullOrEmpty(exportTime)) exportTime = time;
            if (string.IsNullOrEmpty(exporterVersion)) exporterVersion = version;
        }
    }
}
