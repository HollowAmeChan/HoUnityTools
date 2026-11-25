using System.Linq;
using Hollow.HoUnityTools.TransformConstraints;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.TransformConstraints
{
    [CustomEditor(typeof(HoTransformConstraint), true)]
    public sealed class HoTransformConstraintEditor : UnityEditor.Editor
    {
        private ReorderableList sourceList;
        private bool advanced;
        private HoTransformConstraint Constraint => (HoTransformConstraint)target;

        private SerializedProperty Property(string name) => serializedObject.FindProperty(name);

        private void OnEnable()
        {
            sourceList = new ReorderableList(serializedObject, Property("sources"), true, true, true, true);
            sourceList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "来源物体 / 权重");
            sourceList.elementHeight = EditorGUIUtility.singleLineHeight + 5f;
            sourceList.drawElementCallback = (rect, index, active, focused) =>
            {
                var source = sourceList.serializedProperty.GetArrayElementAtIndex(index);
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width - 60f, rect.height),
                    source.FindPropertyRelative("transform"), GUIContent.none);
                EditorGUI.PropertyField(new Rect(rect.xMax - 55f, rect.y, 55f, rect.height),
                    source.FindPropertyRelative("weight"), new GUIContent("", "多个来源按权重混合。权重为 0 时跳过来源。"));
            };
            sourceList.onAddCallback = list =>
            {
                var sources = list.serializedProperty;
                int index = sources.arraySize++;
                var source = sources.GetArrayElementAtIndex(index);
                source.FindPropertyRelative("transform").objectReferenceValue = null;
                source.FindPropertyRelative("weight").floatValue = 1f;
                source.FindPropertyRelative("positionOffset").vector3Value = Vector3.zero;
                source.FindPropertyRelative("rotationOffset").vector3Value = Vector3.zero;
                source.FindPropertyRelative("scaleMultiplier").vector3Value = Vector3.one;
                source.FindPropertyRelative("offsetSource").objectReferenceValue = null;
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUILayout.HorizontalScope())
            {
                Field("targetTransform", "被驱动物体");
                if (Property("targetTransform").objectReferenceValue == null &&
                    GUILayout.Button("用当前物体", GUILayout.Width(88f)))
                {
                    Property("targetTransform").objectReferenceValue = Constraint.transform;
                }
            }

            sourceList.DoLayoutList();
            Field("constraintActive", "运行时启用", "约束仅在播放时写入。手动阶段由调用方触发。");
            Field("weight", "约束强度", "1 使用来源结果，0 停止写入。");
            serializedObject.ApplyModifiedProperties();

            if (Constraint.ValidateConfiguration(out string error, false))
            {
                if (!Constraint.ValidateConfiguration(out error))
                {
                    DrawBindingButtons();
                }
                else if (IsPlayingInScene && Constraint.ConstraintActive &&
                         !string.IsNullOrEmpty(Constraint.LastError))
                {
                    EditorGUILayout.HelpBox(Constraint.LastError, MessageType.Warning);
                }

                DrawWriterConflict();
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Info);
            }

            advanced = EditorGUILayout.Foldout(advanced, "高级设置", true);
            if (advanced)
            {
                DrawAdvanced();
            }
        }

        private void DrawWriterConflict()
        {
            var current = Constraint;
            if (!current.enabled || !current.gameObject.activeInHierarchy || !current.ConstraintActive || current.Weight <= 0f)
            {
                return;
            }

            var peers = IsPlayingInScene ? HoTransformConstraintScheduler.Registered : Resources.FindObjectsOfTypeAll<HoTransformConstraint>();
            var conflict = peers.FirstOrDefault(other =>
                other != current && other.enabled && other.gameObject.activeInHierarchy && other.ConstraintActive &&
                other.TargetTransform == current.TargetTransform &&
                other.Weight > 0f && (other.WrittenChannels & current.WrittenChannels) != 0 &&
                (other.Phase != current.Phase || other.Order == current.Order));
            if (conflict == null)
            {
                return;
            }

            string description = conflict.Phase == current.Phase
                ? "两者的执行顺序相同。请在高级设置中为它们指定不同顺序，或停用其中一个。"
                : "两者在不同阶段写入。需要按顺序组合时，请使用相同的更新阶段和不同的执行顺序。";
            EditorGUILayout.HelpBox($"与「{conflict.gameObject.name}」上的{KindName(conflict.Kind)}约束控制相同通道。{description}", MessageType.Warning);
        }

        private static string KindName(HoTransformConstraintKind kind)
        {
            switch (kind)
            {
                case HoTransformConstraintKind.Position: return "位置";
                case HoTransformConstraintKind.Rotation: return "旋转";
                case HoTransformConstraintKind.Scale: return "缩放";
                case HoTransformConstraintKind.Parent: return "父级";
                default: return "朝向";
            }
        }

        private void DrawAdvanced()
        {
            serializedObject.Update();
            Field("phase", "更新阶段");
            Field("order", "执行顺序", "同阶段中，控制相同通道的约束按数字从小到大执行。");
            if (Constraint.Kind != HoTransformConstraintKind.Aim)
            {
                Field("space", "坐标空间", "本地模式使用每个物体在各自父级下的数值。");
            }

            Field("input", "混合基准", "当帧动画需要上游每帧重新写入。约束强度低于 1 时，缺少上游输入会逐帧追向来源。");
            bool position = Constraint.Kind == HoTransformConstraintKind.Position || Constraint.Kind == HoTransformConstraintKind.Parent;
            bool rotation = Constraint.Kind == HoTransformConstraintKind.Rotation || Constraint.Kind == HoTransformConstraintKind.Parent;
            bool scale = Constraint.Kind == HoTransformConstraintKind.Scale;
            bool aim = Constraint.Kind == HoTransformConstraintKind.Aim;

            if (position) Field("positionAxes", "位置轴");
            if (rotation || aim) Field("rotationAxes", "旋转轴");
            if (scale) Field("scaleAxes", "缩放轴");
            if (aim)
            {
                Field("aimAxis", "瞄准轴");
                Field("upAxis", "本地向上轴");
                Field("upMode", "向上方向");
                var up = (HoAimUpMode)Property("upMode").intValue;
                if (up == HoAimUpMode.ObjectPosition || up == HoAimUpMode.ObjectRotation)
                {
                    Field("upObject", "向上参考物体");
                }

                if (up == HoAimUpMode.WorldVector || up == HoAimUpMode.ObjectRotation)
                {
                    Field("worldUp", "向上向量");
                }

                Field("rotationOffset", "朝向偏移");
            }

            var sources = Property("sources");
            for (int index = 0; index < sources.arraySize; index++)
            {
                var source = sources.GetArrayElementAtIndex(index);
                var reference = source.FindPropertyRelative("transform").objectReferenceValue;
                EditorGUILayout.LabelField(reference != null ? reference.name : $"来源 {index + 1}", EditorStyles.boldLabel);
                if (position || aim)
                    EditorGUILayout.PropertyField(source.FindPropertyRelative("positionOffset"), new GUIContent("位置偏移"));
                if (rotation)
                    EditorGUILayout.PropertyField(source.FindPropertyRelative("rotationOffset"), new GUIContent("旋转偏移"));
                if (scale)
                    EditorGUILayout.PropertyField(source.FindPropertyRelative("scaleMultiplier"), new GUIContent("缩放倍率"));
            }

            Field("drawGizmos", "选中时显示连线");
            serializedObject.ApplyModifiedProperties();

            if (Constraint.ValidateConfiguration(out _))
            {
                DrawBindingButtons();
                if (IsPlayingInScene)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("应用一次"))
                            Constraint.EvaluateNow();
                        if (GUILayout.Button("恢复绑定姿态"))
                            Constraint.RestoreRestPose();
                    }
                }
            }
        }

        private void Field(string name, string label, string tooltip = null)
        {
            EditorGUILayout.PropertyField(Property(name), new GUIContent(label, tooltip));
        }

        private bool IsPlayingInScene => Constraint.gameObject.scene.IsValid() && Application.IsPlaying(Constraint.gameObject);

        private void DrawBindingButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("保留偏移并启用", "记录当前姿态相对来源的偏移。")))
                    Activate(true);
                if (GUILayout.Button(new GUIContent("贴合来源并启用", "清零偏移，直接跟随来源姿态。")))
                    Activate(false);
            }
        }

        private void Activate(bool keepOffset)
        {
            Undo.RecordObject(Constraint, "绑定约束");
            if (!Constraint.Activate(keepOffset, out string error))
            {
                Debug.LogWarning(error, Constraint);
                return;
            }

            EditorUtility.SetDirty(Constraint);
            RecordPrefabOverride(Constraint);
            serializedObject.Update();
        }

        private static void RecordPrefabOverride(Object item)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(item))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
        }
    }
}
