#if UNITY_EDITOR
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
                "支持的约束类型：Rotation, Location, Scale, Child",
                MessageType.Info
            );
            EditorGUILayout.Space(5);
        }

        private string _configFilePath;
        private void DrawConfigSection()
        {
            // 选择配置文件
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("配置文件 (.json)", GUILayout.Width(100));

                if (GUILayout.Button("选择文件", GUILayout.Width(80)))
                {
                    var path = EditorUtility.OpenFilePanel("选择配置文件", Application.dataPath, "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _configFilePath = path; // 保存文件路径
                        var jsonContent = System.IO.File.ReadAllText(path);
                        configFile = new TextAsset(jsonContent);
                    }
                }

                // 显示文件路径
                EditorGUILayout.LabelField(_configFilePath ?? "未选择文件", EditorStyles.textField);
            }

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

            if (GUILayout.Button("清除全部约束", GUILayout.Height(30)))
            {
                ClearAllConstraints();
            }
        }

        private void ApplyConstraints()
        {
            try
            {
                var config = JsonUtility.FromJson<ConstraintConfig>(configFile.text);
                var successCount = 0;
                var totalCount = 0;

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

                // 添加/获取约束组件
                var rotationConstraint = bone.GetComponent<RotationConstraint>();
                if (rotationConstraint == null)
                {
                    rotationConstraint = Undo.AddComponent<RotationConstraint>(bone.gameObject);
                }

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
                rotationConstraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;

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

                // 添加/获取约束组件
                var positionConstraint = bone.GetComponent<PositionConstraint>();
                if (positionConstraint == null)
                {
                    positionConstraint = Undo.AddComponent<PositionConstraint>(bone.gameObject);
                }

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
                positionConstraint.translationAxis = Axis.X | Axis.Y | Axis.Z;

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

                // 添加/获取约束组件
                var scaleConstraint = bone.GetComponent<ScaleConstraint>();
                if (scaleConstraint == null)
                {
                    scaleConstraint = Undo.AddComponent<ScaleConstraint>(bone.gameObject);
                }

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
                scaleConstraint.scalingAxis = Axis.X | Axis.Y | Axis.Z;

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

                // 添加/获取约束组件
                var parentConstraint = bone.GetComponent<ParentConstraint>();
                if (parentConstraint == null)
                {
                    parentConstraint = Undo.AddComponent<ParentConstraint>(bone.gameObject);
                }

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

            EditorUtility.DisplayDialog("完成",
                $"已清除 {clearedCount} 个标准约束",
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
