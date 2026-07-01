#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>
    /// 骨架约束导入工具 - 从配置文件批量导入标准 Unity Constraint 到骨架
    ///
    /// 功能：为骨架批量添加标准 Unity 约束（RotationConstraint, PositionConstraint 等）
    /// 用途：快速配置骨架的约束关系，适用于角色绑定、IK 设置等场景
    /// </summary>
    public class ConstraintImporterWindow : EditorWindow
    {
        // 序列化字段用于保存UI状态
        [SerializeField] private TextAsset configFile;
        [SerializeField] private GameObject targetRig;

        [MenuItem("HoUnityTools/骨架约束/导入标准约束")]
        public static void ShowWindow()
        {
            GetWindow<ConstraintImporterWindow>("骨架约束导入").minSize = new Vector2(350, 200);
        }

        void OnGUI()
        {
            DrawInfoSection();
            DrawConfigSection();
            DrawActionButtons();
        }

        private void DrawInfoSection()
        {
            EditorGUILayout.HelpBox(
                "此工具用于批量导入 Unity 标准约束到骨架系统。\n" +
                "支持的约束类型：Rotation, Location, Scale, Child\n" +
                "识别 HoTools 语义约束（fan / twist）：twist 自动锁 Y 轴。\n" +
                "导入的约束会被标记，可用「安全清除」只删除本工具生成的约束，保留手工约束。\n" +
                "「还原骨架到 Prefab 姿态」把骨骼 Transform 还原到 Prefab 原始值（需为 Prefab 实例）。",
                MessageType.Info
            );
            EditorGUILayout.Space(5);
        }

        private void DrawConfigSection()
        {
            // 直接拖入项目内的 .json（TextAsset），与骨骼分组文件的填写方式一致
            configFile = (TextAsset)EditorGUILayout.ObjectField(
                "配置文件 (.json)",
                configFile,
                typeof(TextAsset),
                false
            );

            targetRig = (GameObject)EditorGUILayout.ObjectField(
                "目标骨架",
                targetRig,
                typeof(GameObject),
                true
            );
        }

        private void DrawActionButtons()
        {
            if (GUILayout.Button("导入约束", GUILayout.Height(30)))
            {
                if (configFile == null)
                {
                    EditorUtility.DisplayDialog("错误", "请选择配置文件", "确定");
                    return;
                }

                if (targetRig == null)
                {
                    EditorUtility.DisplayDialog("错误", "请选择目标骨架", "确定");
                    return;
                }

                ApplyConstraints();
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("锁定全部约束", GUILayout.Height(30)))
            {
                LockAllConstraints();
            }

            // 安全清除：只删除本导入器生成并标记的约束，保留用户手工约束
            if (GUILayout.Button("安全清除（仅删除导入的约束）", GUILayout.Height(30)))
            {
                SafeClearImportedConstraints();
            }

            if (GUILayout.Button("清除全部约束", GUILayout.Height(30)))
            {
                ClearAllConstraints();
            }

            EditorGUILayout.Space(5);

            // 还原骨架：把所有骨骼 Transform 还原到 Prefab 原始姿态（等价于 Inspector 里 Revert）
            if (GUILayout.Button("还原骨架到 Prefab 姿态", GUILayout.Height(30)))
            {
                ResetRigToDefault();
            }
        }

        // 当前导入批次的元数据，供标记组件记录来源，"安全清除"时可追溯
        private string _importArmature;
        private string _importTime;
        private string _importVersion;

        private void ApplyConstraints()
        {
            try
            {
                var config = JsonUtility.FromJson<ConstraintConfig>(configFile.text);
                var successCount = 0;
                var totalCount = 0;

                // 记录本次导入来源信息，写入每个骨骼的标记组件
                _importArmature = config.armatureName;
                _importTime = config.exportTime;
                _importVersion = config.version;

                Undo.RecordObject(targetRig, "Apply Constraints");

                // 遍历骨架中的所有骨骼
                var allBones = targetRig.GetComponentsInChildren<Transform>();
                var boneMap = new System.Collections.Generic.Dictionary<string, Transform>();
                foreach (var bone in allBones)
                {
                    boneMap[bone.name] = bone;
                }

                foreach (var boneConfig in config.bones)
                {
                    // 查找骨骼
                    if (!boneMap.TryGetValue(boneConfig.boneName, out var boneTransform))
                    {
                        Debug.LogWarning($"骨骼不存在: {boneConfig.boneName}");
                        continue;
                    }

                    foreach (var constraint in boneConfig.constraints)
                    {
                        bool result = false;
                        switch (constraint.type)
                        {
                            case "Rotation":
                                result = AddRotationConstraint(boneTransform, constraint);
                                break;
                            case "Location":
                                result = AddPositionConstraint(boneTransform, constraint);
                                break;
                            case "Scale":
                                result = AddScaleConstraint(boneTransform, constraint);
                                break;
                            case "Child":
                                result = AddParentConstraint(boneTransform, constraint);
                                break;
                            default:
                                Debug.LogWarning($"未知约束类型: {constraint.type}");
                                break;
                        }

                        if (result) successCount++;
                        totalCount++;
                    }
                }

                EditorUtility.DisplayDialog("完成",
                    $"成功添加 {successCount}/{totalCount} 个约束",
                    "确定");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("错误",
                    $"操作失败: {e.Message}",
                    "确定");
            }
        }

        private Transform FindBoneInRig(string boneName)
        {
            // 遍历骨架中的所有骨骼
            var allBones = targetRig.GetComponentsInChildren<Transform>();
            foreach (var bone in allBones)
            {
                if (bone.name == boneName)
                {
                    return bone;
                }
            }
            return null;
        }

        /// <summary>
        /// 根据约束的 axes 字段解析出 Unity 的 Axis 位掩码。
        /// twist 约束会把 y 设为 false，从而只保留 X|Z（锁 Y 轴，只传 twist 分量）。
        /// </summary>
        private static Axis ResolveAxis(AxesInfo axes)
        {
            Axis result = Axis.None;
            if (axes.x) result |= Axis.X;
            if (axes.y) result |= Axis.Y;
            if (axes.z) result |= Axis.Z;
            return result;
        }

        /// <summary>
        /// 获取或创建"由导入器管理"的约束组件，是安全删除的核心。
        /// - 骨骼上已有本导入器管理的同类型约束：复用它并清空旧源，供调用方重建（幂等重导入）。
        /// - 骨骼上存在用户手工添加的同类型约束（未被标记管理）：跳过并返回 null，绝不污染用户数据。
        /// - 否则：新建约束，并确保骨骼挂有标记组件、把新约束登记进去。
        /// </summary>
        private T GetOrCreateManaged<T>(Transform bone) where T : Component, IConstraint
        {
            var marker = bone.GetComponent<HoImportedConstraintMarker>();
            var existing = bone.GetComponents<T>();

            if (existing.Length > 0)
            {
                // 优先复用本导入器管理的实例
                if (marker != null)
                {
                    foreach (var c in existing)
                    {
                        if (marker.Manages(c))
                        {
                            // 幂等：清空旧源，交由调用方重建，避免重复导入累积源
                            var con = (IConstraint)c;
                            for (int i = con.sourceCount - 1; i >= 0; i--)
                            {
                                con.RemoveSource(i);
                            }
                            return c;
                        }
                    }
                }
                // 存在但非本导入器管理 → 用户手工约束，跳过不动
                Debug.LogWarning($"骨骼 {bone.name} 已存在用户添加的 {typeof(T).Name}，跳过以避免覆盖");
                return null;
            }

            // 全新创建并登记到标记组件
            var created = Undo.AddComponent<T>(bone.gameObject);
            if (marker == null)
            {
                marker = Undo.AddComponent<HoImportedConstraintMarker>(bone.gameObject);
            }
            marker.SetMetadata(_importArmature, _importTime, _importVersion);
            marker.Register(created);
            EditorUtility.SetDirty(marker);

            return created;
        }

        private bool AddRotationConstraint(Transform bone, ConstraintInfo constraint)
        {
            try
            {
                // 查找目标骨骼
                var target = FindBoneInRig(constraint.targetPath);
                if (target == null)
                {
                    Debug.LogWarning($"目标不存在: {constraint.targetPath}");
                    return false;
                }

                // 获取或创建"由导入器管理"的约束组件；用户手工约束会被跳过
                var rotationConstraint = GetOrCreateManaged<RotationConstraint>(bone);
                if (rotationConstraint == null) return false;

                // 配置约束源
                var source = new ConstraintSource
                {
                    sourceTransform = target,
                    weight = 1f
                };

                // 添加并激活约束
                rotationConstraint.AddSource(source);
                rotationConstraint.constraintActive = true;
                rotationConstraint.weight = constraint.weight;
                // 轴向由导出的 axes 决定：twist 约束锁 Y 轴（只传 X|Z），fan/其他默认全轴。
                rotationConstraint.rotationAxis = ResolveAxis(constraint.axes);

                EditorUtility.SetDirty(rotationConstraint);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool AddPositionConstraint(Transform bone, ConstraintInfo constraint)
        {
            try
            {
                // 查找目标骨骼
                var target = FindBoneInRig(constraint.targetPath);
                if (target == null)
                {
                    Debug.LogWarning($"目标不存在: {constraint.targetPath}");
                    return false;
                }

                // 获取或创建"由导入器管理"的约束组件；用户手工约束会被跳过
                var positionConstraint = GetOrCreateManaged<PositionConstraint>(bone);
                if (positionConstraint == null) return false;

                // 配置约束源
                var source = new ConstraintSource
                {
                    sourceTransform = target,
                    weight = 1f
                };

                // 添加并激活约束
                positionConstraint.AddSource(source);
                positionConstraint.constraintActive = true;
                positionConstraint.weight = constraint.weight; // 约束整体权重
                positionConstraint.translationAxis = ResolveAxis(constraint.axes);

                EditorUtility.SetDirty(positionConstraint);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool AddScaleConstraint(Transform bone, ConstraintInfo constraint)
        {
            try
            {
                // 查找目标骨骼
                var target = FindBoneInRig(constraint.targetPath);
                if (target == null)
                {
                    Debug.LogWarning($"目标不存在: {constraint.targetPath}");
                    return false;
                }

                // 获取或创建"由导入器管理"的约束组件；用户手工约束会被跳过
                var scaleConstraint = GetOrCreateManaged<ScaleConstraint>(bone);
                if (scaleConstraint == null) return false;

                // 配置约束源
                var source = new ConstraintSource
                {
                    sourceTransform = target,
                    weight = 1f
                };

                // 添加并激活约束
                scaleConstraint.AddSource(source);
                scaleConstraint.constraintActive = true;
                scaleConstraint.weight = constraint.weight; // 约束整体权重
                scaleConstraint.scalingAxis = ResolveAxis(constraint.axes);

                EditorUtility.SetDirty(scaleConstraint);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool AddParentConstraint(Transform bone, ConstraintInfo constraint)
        {
            try
            {
                // 查找目标骨骼
                var target = FindBoneInRig(constraint.targetPath);
                if (target == null)
                {
                    Debug.LogWarning($"目标不存在: {constraint.targetPath}");
                    return false;
                }

                // 获取或创建"由导入器管理"的约束组件；用户手工约束会被跳过
                var parentConstraint = GetOrCreateManaged<ParentConstraint>(bone);
                if (parentConstraint == null) return false;

                // 配置约束源
                var source = new ConstraintSource
                {
                    sourceTransform = target,
                    weight = 1f
                };

                // 添加并激活约束
                parentConstraint.AddSource(source);
                parentConstraint.constraintActive = true;
                parentConstraint.weight = constraint.weight; // 约束整体权重

                EditorUtility.SetDirty(parentConstraint);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安全清除：只删除由本导入器生成并标记的约束，保留用户手工添加的约束。
        /// 依据每个骨骼上的 HoImportedConstraintMarker：删除它登记的约束组件，
        /// 随后连同标记组件一并移除。用户未经导入器创建的约束不受影响。
        /// </summary>
        private void SafeClearImportedConstraints()
        {
            if (targetRig == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择目标骨架", "确定");
                return;
            }

            var markers = targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true);
            if (markers.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到由导入器生成的约束（无标记）", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认",
                $"将删除 {markers.Length} 个骨骼上由导入器生成的约束，用户手工添加的约束不受影响。是否继续？",
                "确定", "取消")) return;

            var clearedCount = 0;

            foreach (var marker in markers)
            {
                // 先销毁受管约束，再移除标记本身
                foreach (var constraint in new List<Component>(marker.GetLiveConstraints()))
                {
                    Undo.DestroyObjectImmediate(constraint);
                    clearedCount++;
                }
                Undo.DestroyObjectImmediate(marker);
            }

            EditorUtility.DisplayDialog("完成",
                $"已安全清除 {clearedCount} 个导入的约束（保留用户约束）",
                "确定");
        }

        private void ClearAllConstraints()
        {
            if (!EditorUtility.DisplayDialog("警告",
                "确定要清除骨架上所有的标准约束吗？",
                "确定", "取消")) return;

            Undo.RecordObject(targetRig, "Clear Constraints");

            var constraintTypes = new System.Type[]
            {
                typeof(RotationConstraint),
                typeof(PositionConstraint),
                typeof(ScaleConstraint),
                typeof(ParentConstraint)
            };

            var clearedCount = 0;

            // 遍历所有骨骼
            foreach (var bone in targetRig.GetComponentsInChildren<Transform>())
            {
                // 遍历所有约束类型
                foreach (var constraintType in constraintTypes)
                {
                    var constraints = bone.GetComponents(constraintType);
                    foreach (var constraint in constraints)
                    {
                        Undo.DestroyObjectImmediate(constraint);
                        clearedCount++;
                    }
                }
            }

            // 约束已全部删除，导入标记组件失去意义，一并移除避免留下空标记
            foreach (var marker in targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
            {
                Undo.DestroyObjectImmediate(marker);
            }

            EditorUtility.DisplayDialog("完成",
                $"已清除 {clearedCount} 个标准约束",
                "确定");
        }

        /// <summary>
        /// 复位骨架到 Prefab 原始姿态：对每根骨的 Transform 还原 Prefab 覆盖（override），
        /// 等价于 Inspector 里右键 Transform → Revert，位置/旋转/缩放全部恢复为 Prefab 存的值。
        ///
        /// 依赖 Prefab 实例关系，仅编辑器可用。目标骨架必须是 Prefab 实例；
        /// 非 Prefab 实例的骨骼（无覆盖可还原）会被跳过。
        /// </summary>
        private void ResetRigToDefault()
        {
            if (targetRig == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择目标骨架", "确定");
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(targetRig))
            {
                EditorUtility.DisplayDialog("错误",
                    "目标骨架不是 Prefab 实例，无法还原到 Prefab 原始姿态。\n" +
                    "此功能依赖 Prefab 覆盖关系（等价于 Inspector 里 Transform 的 Revert）。",
                    "确定");
                return;
            }

            var bones = targetRig.GetComponentsInChildren<Transform>(true);
            if (bones.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "骨架下没有可复位的骨骼", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认",
                $"将把 {bones.Length} 根骨骼的 Transform 还原到 Prefab 原始姿态（位置/旋转/缩放全部恢复），当前改动会被丢弃。是否继续？",
                "确定", "取消")) return;

            var count = 0;
            foreach (var bone in bones)
            {
                if (bone == null) continue;
                // 只处理属于 Prefab 实例的骨骼；无覆盖时 RevertObjectOverride 是安全的空操作
                if (!PrefabUtility.IsPartOfPrefabInstance(bone)) continue;

                // 还原该 Transform 上的全部覆盖（InteractionMode.UserAction 记录 Undo）
                PrefabUtility.RevertObjectOverride(bone, InteractionMode.UserAction);
                count++;
            }

            EditorUtility.DisplayDialog("完成",
                $"已还原 {count} 根骨骼到 Prefab 原始姿态",
                "确定");
        }

        private void LockAllConstraints()
        {
            if (targetRig == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择目标骨架", "确定");
                return;
            }

            var constraintTypes = new System.Type[]
            {
                typeof(RotationConstraint),
                typeof(PositionConstraint),
                typeof(ScaleConstraint),
                typeof(ParentConstraint)
            };

            var constraintsList = new System.Collections.Generic.List<Component>();

            // 收集所有约束
            foreach (var bone in targetRig.GetComponentsInChildren<Transform>())
            {
                foreach (var constraintType in constraintTypes)
                {
                    constraintsList.AddRange(bone.GetComponents(constraintType));
                }
            }

            if (constraintsList.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到任何标准约束", "确定");
                return;
            }

            Undo.RecordObjects(constraintsList.ToArray(), "锁定约束");

            var lockedCount = 0;

            // 锁定所有约束
            foreach (var constraint in constraintsList)
            {
                switch (constraint)
                {
                    case RotationConstraint rotationConstraint:
                        if (!rotationConstraint.locked)
                        {
                            rotationConstraint.locked = true;
                            lockedCount++;
                        }
                        break;
                    case PositionConstraint positionConstraint:
                        if (!positionConstraint.locked)
                        {
                            positionConstraint.locked = true;
                            lockedCount++;
                        }
                        break;
                    case ScaleConstraint scaleConstraint:
                        if (!scaleConstraint.locked)
                        {
                            scaleConstraint.locked = true;
                            lockedCount++;
                        }
                        break;
                    case ParentConstraint parentConstraint:
                        if (!parentConstraint.locked)
                        {
                            parentConstraint.locked = true;
                            lockedCount++;
                        }
                        break;
                }

                EditorUtility.SetDirty(constraint);
            }

            EditorUtility.DisplayDialog("完成",
                $"已锁定 {lockedCount} 个标准约束",
                "确定");
        }
    }
}
#endif
