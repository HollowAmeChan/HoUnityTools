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
        private const int ChannelCount = 4;

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

            // 参考系留空 = 自动用 Animator 所在物体的朝向（那才是角色的面向）。
            // 一键装配直接清掉：以前这里会填"组件自己"，很容易填成一根骨骼/空物体，轴向随机，
            // 结果总角度读数、限位、鼠标左右全部失准。
            serializedObject.FindProperty("reference").objectReferenceValue = null;

            SerializedProperty cameraProperty = serializedObject.FindProperty("mouseCamera");
            if (cameraProperty.objectReferenceValue == null)
            {
                Camera camera = HoMousePointer.FindSceneCamera(null);
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

            // 监视键的族别：模型上有"看左/看右"就用左右族，只有 In/Out 才用内外族
            bool hasHeadRelative = HasAny(constraint, HoBlinkKeySemantic.GazeLeft) || HasAny(constraint, HoBlinkKeySemantic.GazeRight);
            ApplyFamily(constraint, serializedObject, !hasHeadRelative);
        }

        /// <summary>网格上是否存在这个语义的任意键（不分左右/双眼）。</summary>
        private static bool HasAny(IHoShapeKeyMeshProvider host, HoBlinkKeySemantic semantic)
        {
            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic == semantic && host.KeyExists(entry.Name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按族别填四条通道。两族其实是同一件事的两种键名：
        ///   往右 = 左眼的内侧键（In）+ 右眼的外侧键（Out）
        ///   往左 = 左眼的外侧键（Out）+ 右眼的内侧键（In）
        /// 左右族（VRM/Meta）则是"双眼共用一个键"，两格填同一个名字（写入器会自动只写一次）。
        /// </summary>
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
                // 内外族（ARKit/PICO/OpenXR）：四向里每一向都拆到左右眼
                AddEntry(entries, HoLookAtEyeChannel.LookRight,
                    FindKey(host, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
                    FindKey(host, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.LookLeft,
                    FindKey(host, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
                    FindKey(host, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.Up,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.Down,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right));
            }
            else
            {
                // 左右族：优先双眼共用的键（两格同名 → 只写一次），没有就退回左右眼各一个
                AddEntry(entries, HoLookAtEyeChannel.LookLeft,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.LookRight,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.Up,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right));
                AddEntry(entries, HoLookAtEyeChannel.Down,
                    FindGazeSide(host, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
                    FindGazeSide(host, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right));
            }

            serializedObject.ApplyModifiedProperties();
            host.Rebuild();
        }

        /// <summary>先找"双眼共用"的键，再退回指定眼别的键。都没有就返回空串。</summary>
        private static string FindGazeSide(IHoShapeKeyMeshProvider host, HoBlinkKeySemantic semantic, HoBlinkSide side)
        {
            return FirstNotEmpty(FindBothKey(host, semantic), FindKey(host, semantic, side));
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

            SetKey(entry.FindPropertyRelative("leftEye"), leftKey);
            SetKey(entry.FindPropertyRelative("rightEye"), rightKey);
        }

        private static void SetKey(SerializedProperty key, string keyName)
        {
            key.FindPropertyRelative("enabled").boolValue = true;
            key.FindPropertyRelative("keyName").stringValue = keyName ?? string.Empty;
            key.FindPropertyRelative("gain").floatValue = 1.0f;
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
