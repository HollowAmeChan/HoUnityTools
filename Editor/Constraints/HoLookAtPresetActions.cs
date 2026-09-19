using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 注视约束预设：一键装配（humanoid 骨骼 + 网格上的凝视键 + 族别判断）与几个常用组合。
    /// 只写序列化字段，不碰运行时状态。
    /// </summary>
    internal static class HoLookAtPresetActions
    {
        private const int ChannelCount = 6;

        public static void AutoRig(HoLookAtConstraint constraint, SerializedObject serializedObject)
        {
            serializedObject.ApplyModifiedProperties();

            if (constraint.GetComponent<Animator>() == null)
            {
                Animator found = constraint.GetComponentInParent<Animator>();
                if (found != null)
                {
                    serializedObject.FindProperty("animator").objectReferenceValue = found;
                }
            }

            if (serializedObject.FindProperty("reference").objectReferenceValue == null)
            {
                // 参考系要的是"角色的面向"，所以优先用 Animator 所在物体，而不是组件自己所在的骨骼/空物体
                SerializedProperty animatorProperty = serializedObject.FindProperty("animator");
                Animator resolvedAnimator = animatorProperty.objectReferenceValue as Animator;
                Transform frame = resolvedAnimator != null ? resolvedAnimator.transform : constraint.transform;
                serializedObject.FindProperty("reference").objectReferenceValue = frame;
            }

            SerializedProperty cameraProperty = serializedObject.FindProperty("mouseCamera");
            if (cameraProperty.objectReferenceValue == null)
            {
                Camera camera = HoMousePointer.ResolveCamera(null);
                if (camera != null)
                {
                    cameraProperty.objectReferenceValue = camera;
                }
            }

            SerializedProperty renderers = serializedObject.FindProperty("renderers");
            if (renderers.arraySize == 0)
            {
                constraint.CollectChildRenderers();
                serializedObject.Update();
            }

            // 监视键的族别：优先"相对头"的左右族，没有就退回"相对眼球"的内外族
            bool hasHeadRelative = FindBothKey(constraint, HoBlinkKeySemantic.GazeLeft) != null
                                   || FindBothKey(constraint, HoBlinkKeySemantic.GazeRight) != null;
            ApplyFamily(constraint, serializedObject, !hasHeadRelative);
        }

        /// <summary>按族别填横向通道；纵向通道两族共用。</summary>
        public static void ApplyFamily(HoLookAtConstraint constraint, SerializedObject serializedObject, bool eyeRelative)
        {
            serializedObject.ApplyModifiedProperties();
            HoLookAtConstraint host = constraint;
            if (host.MeshCount == 0)
            {
                host.Rebuild();
            }

            SerializedProperty entries = serializedObject.FindProperty("eyeEntries");
            entries.ClearArray();

            if (eyeRelative)
            {
                AddEntry(entries, HoLookAtEyeChannel.Inner,
                    FindKey(host, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
                    FindKey(host, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.Outer,
                    FindKey(host, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
                    FindKey(host, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right));
            }
            else
            {
                string left = FirstNotEmpty(FindBothKey(host, HoBlinkKeySemantic.GazeLeft), FindKey(host, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left));
                string right = FirstNotEmpty(FindBothKey(host, HoBlinkKeySemantic.GazeRight), FindKey(host, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.LookLeft, left, left);
                AddEntry(entries, HoLookAtEyeChannel.LookRight, right, right);
            }

            string up = FirstNotEmpty(FindBothKey(host, HoBlinkKeySemantic.GazeUp), FindKey(host, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left));
            string down = FirstNotEmpty(FindBothKey(host, HoBlinkKeySemantic.GazeDown), FindKey(host, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left));
            AddEntry(entries, HoLookAtEyeChannel.Up, up, up);
            AddEntry(entries, HoLookAtEyeChannel.Down, down, down);

            serializedObject.ApplyModifiedProperties();
            host.Rebuild();
        }

        public static void EyesOnly(SerializedObject serializedObject)
        {
            serializedObject.FindProperty("headEnabled").boolValue = false;
            serializedObject.FindProperty("spineEnabled").boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }

        public static void HeadAndEyes(SerializedObject serializedObject)
        {
            serializedObject.FindProperty("headEnabled").boolValue = true;
            serializedObject.FindProperty("headWeight").floatValue = 1.0f;
            serializedObject.FindProperty("spineEnabled").boolValue = true;
            serializedObject.FindProperty("bodyWeight").floatValue = 0.3f;
            serializedObject.ApplyModifiedProperties();
        }

        public static void Clear(SerializedObject serializedObject)
        {
            serializedObject.FindProperty("eyeEntries").ClearArray();
            serializedObject.ApplyModifiedProperties();
        }

        private static void AddEntry(SerializedProperty entries, HoLookAtEyeChannel channel, string leftKey, string rightKey)
        {
            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            entry.FindPropertyRelative("enabled").boolValue = true;
            entry.FindPropertyRelative("channel").enumValueIndex = (int)channel;

            SerializedProperty left = entry.FindPropertyRelative("leftEye");
            SerializedProperty right = entry.FindPropertyRelative("rightEye");
            ResetMapping(left, leftKey);
            ResetMapping(right, rightKey);
        }

        private static void ResetMapping(SerializedProperty mapping, string keyName)
        {
            mapping.FindPropertyRelative("meshScope").enumValueIndex = (int)HoShapeKeyMeshScope.All;
            mapping.FindPropertyRelative("meshIndex").intValue = 0;
            mapping.FindPropertyRelative("keyName").stringValue = keyName ?? string.Empty;
            mapping.FindPropertyRelative("blendMode").enumValueIndex = (int)HoShapeKeyBlendMode.Additive;
            mapping.FindPropertyRelative("weight").floatValue = 1.0f;
            mapping.FindPropertyRelative("gain").floatValue = 1.0f;
            mapping.FindPropertyRelative("offset").floatValue = 0.0f;
            mapping.FindPropertyRelative("outputMin").floatValue = 0.0f;
            mapping.FindPropertyRelative("outputMax").floatValue = 100.0f;
            mapping.FindPropertyRelative("clampToRange").boolValue = true;
            mapping.FindPropertyRelative("rampPreset").enumValueIndex = (int)HoShapeKeyRampPreset.Direct;
            mapping.FindPropertyRelative("rampIntensity").floatValue = 1.0f;
        }

        /// <summary>优先"双眼共用"的键（VRM/Meta 的 LookLeft 之类）。</summary>
        private static string FindBothKey(IHoShapeKeyMeshProvider host, HoBlinkKeySemantic semantic)
        {
            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic == semantic && entry.Side == HoBlinkSide.Both && host.KeyExists(entry.Name))
                {
                    return entry.Name;
                }
            }

            return null;
        }

        private static string FindKey(IHoShapeKeyMeshProvider host, HoBlinkKeySemantic semantic, HoBlinkSide side)
        {
            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic == semantic && entry.Side == side && host.KeyExists(entry.Name))
                {
                    return entry.Name;
                }
            }

            return null;
        }

        private static string FirstNotEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }
    }
}
