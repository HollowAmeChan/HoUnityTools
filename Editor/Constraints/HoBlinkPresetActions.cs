using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 预设：只负责把**结构**和**起点参数**写进序列化数据 —— 规则、角色的标签、每条目标的 ramp / 混合 / 增益。
    ///
    /// 两条底线：
    /// 1. **不猜用户自建的键名**。目标键一律留空，由用户在面板上从"网格上真实存在的键"里选（键名格旁边的 ▾）。
    ///    名字匹配那套东西已经删掉：它只在碰巧撞上某种命名习惯时有用，撞不上就是"看起来配好了其实接错键"。
    /// 2. **驱动键只在网格上真的存在时才填**，找不到就留空并在 Console 说明（不把内置表里的名字硬填进去）。
    /// </summary>
    internal static class HoBlinkPresetActions
    {
        public static void Rebuild(HoBlinkConstraint constraint)
        {
            constraint.Rebuild();
            EditorUtility.SetDirty(constraint);
        }

        public static void ClearAll(SerializedObject serializedObject)
        {
            serializedObject.FindProperty("blinkTargets").ClearArray();
            serializedObject.FindProperty("rules").ClearArray();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>眼睑键自动匹配：模型上有"双眼闭合键"就用一个双眼键，否则用左右两个键。</summary>
        public static void AutoMatchEyelidKeys(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            bool splitLeftRight = string.IsNullOrEmpty(FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both));
            ApplyBlinkOutput(constraint, serializedObject, splitLeftRight);
        }

        /// <summary>
        /// 追加「四向注视」：上 / 下 / 左 / 右 四条**键驱动**规则，每条一个目标。
        /// 驱动键按语义在网格上找（VRM 的 LookUp… 或 ARKit 的 eyeLookUp…），找不到就留空；
        /// 目标键留空 + 带方向角色，由用户在面板上选。
        /// </summary>
        public static void ApplyFourWayGaze(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");
            string up = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp);
            string down = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown);
            string left = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft);
            string right = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight);

            AddRule(rules, "注视 · 上", HoBlinkDriverKind.ShapeKey, up, string.Empty, HoBlinkDriverRange.Unipolar, 4.0f, 0.35f, 0.03f);
            AddTarget(rules, "往上");
            AddRule(rules, "注视 · 下", HoBlinkDriverKind.ShapeKey, down, string.Empty, HoBlinkDriverRange.Unipolar, 4.0f, 0.35f, 0.03f);
            AddTarget(rules, "往下");
            AddRule(rules, "注视 · 左", HoBlinkDriverKind.ShapeKey, left, string.Empty, HoBlinkDriverRange.Unipolar, 4.0f, 0.35f, 0.03f);
            AddTarget(rules, "往左");
            AddRule(rules, "注视 · 右", HoBlinkDriverKind.ShapeKey, right, string.Empty, HoBlinkDriverRange.Unipolar, 4.0f, 0.35f, 0.03f);
            AddTarget(rules, "往右");

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
            Debug.Log(
                "[HoBlinkConstraint] 四向注视：加了 4 条规则（上 / 下 / 左 / 右）。\n"
                + "· 驱动键：上 = " + Describe(up) + "，下 = " + Describe(down) + "，左 = " + Describe(left) + "，右 = " + Describe(right) + "\n"
                + "· 目标键留空 —— 在每条目标的第一格点 ▾，从网格上真实存在的键里选。",
                constraint);
        }

        /// <summary>
        /// 追加「眨眼追踪」：**一条**眨眼加速度驱动的规则，四个目标（压扁 / 拉宽 / 下移 / 眼仁压一下）。
        /// 不需要任何驱动键：加速度由组件自己从眨眼曲线算，并归一化到 0..1。
        /// </summary>
        public static void ApplyBlinkTrack(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");
            AddRule(rules, "眨眼追踪", HoBlinkDriverKind.BlinkAccel, string.Empty, string.Empty, HoBlinkDriverRange.Unipolar, 9.0f, 0.20f, 0.012f);

            SerializedProperty rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            rule.FindPropertyRelative("blinkEnvelope").floatValue = 0.025f;
            rule.FindPropertyRelative("driverGain").floatValue = 1.0f;

            AddTarget(rules, "高光 · 压扁", HoShapeKeyRampPreset.Amplify, 1.35f, HoShapeKeyBlendMode.Override, 1.0f, 1.0f);
            AddTarget(rules, "高光 · 拉宽", HoShapeKeyRampPreset.Amplify, 1.0f, HoShapeKeyBlendMode.Override, -0.5f, 1.0f);
            AddTarget(rules, "高光 · 下移", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, -0.45f, 0.9f);
            AddTarget(rules, "眼仁 · 压一下", HoShapeKeyRampPreset.EaseIn, 0.9f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
            Debug.Log(
                "[HoBlinkConstraint] 眨眼追踪：加了 1 条规则 + 4 个目标。\n"
                + "· 驱动 = 眨眼加速度（组件自己算，不需要键）：9 Hz / ζ0.20 / 强度 1.0 / 包络 25 ms\n"
                + "· 目标键留空 —— 高光压扁、拉宽、下移、眼仁压一下，各自在第一格点 ▾ 选键。",
                constraint);
        }

        /// <summary>眨眼输出：双眼键版（一个双眼键）或左右键版（左右两个键，值相同）。</summary>
        public static void ApplyBlinkOutput(HoBlinkConstraint constraint, SerializedObject serializedObject, bool splitLeftRight)
        {
            SerializedProperty list = serializedObject.FindProperty("blinkTargets");
            list.ClearArray();

            string bothKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both);
            string leftKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left);
            string rightKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right);

            if (!splitLeftRight && !string.IsNullOrEmpty(bothKey))
            {
                AddTarget(list, bothKey, HoBlinkSide.Both);
            }
            else
            {
                AddTarget(list, FirstNotEmpty(leftKey, bothKey), HoBlinkSide.Left);
                AddTarget(list, FirstNotEmpty(rightKey, bothKey), HoBlinkSide.Right);
            }

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);

            string summary = string.Empty;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty target = list.GetArrayElementAtIndex(i);
                string key = target.FindPropertyRelative("keyName").stringValue;
                string mesh = constraint.DescribeKeyBindings(key);
                summary += (summary.Length == 0 ? string.Empty : "\n") + "· "
                    + key + "（" + target.FindPropertyRelative("side").enumDisplayNames[target.FindPropertyRelative("side").enumValueIndex] + "）"
                    + (string.IsNullOrEmpty(mesh) ? " ← 这些网格上没有这个键！" : " → " + mesh);
            }

            Debug.Log(
                "[HoBlinkConstraint] 眨眼输出：" + (splitLeftRight ? "左右键版" : "双眼键版") + "\n" + summary,
                constraint);
        }

        // ── 序列化写入的小工具 ──────────────────────────────────────────

        /// <summary>给最后一条规则加一个"带角色"的目标：角色只是给人看的标签，键名留空。</summary>
        private static void AddTarget(SerializedProperty rules, string role)
        {
            AddTarget(rules, role, HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
        }

        private static void AddTarget(
            SerializedProperty rules,
            string role,
            HoShapeKeyRampPreset rampPreset,
            float intensity,
            HoShapeKeyBlendMode blendMode,
            float gain,
            float weight)
        {
            SerializedProperty targets = rules.GetArrayElementAtIndex(rules.arraySize - 1).FindPropertyRelative("targets");
            SerializedProperty target = AddTarget(targets, string.Empty, HoBlinkSide.Both);
            target.FindPropertyRelative("label").stringValue = role;
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)rampPreset;
            target.FindPropertyRelative("rampIntensity").floatValue = intensity;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)blendMode;
            target.FindPropertyRelative("gain").floatValue = gain;
            target.FindPropertyRelative("weight").floatValue = weight;
        }

        private static SerializedProperty AddRule(
            SerializedProperty rules,
            string label,
            HoBlinkDriverKind driverKind,
            string positiveKey,
            string negativeKey,
            HoBlinkDriverRange driverRange,
            float frequency,
            float dampingRatio,
            float inputSmoothing)
        {
            rules.InsertArrayElementAtIndex(rules.arraySize);
            SerializedProperty rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            rule.FindPropertyRelative("label").stringValue = label;
            rule.FindPropertyRelative("enabled").boolValue = true;
            rule.FindPropertyRelative("driverKind").enumValueIndex = (int)driverKind;
            rule.FindPropertyRelative("positiveKey").stringValue = positiveKey ?? string.Empty;
            rule.FindPropertyRelative("negativeKey").stringValue = negativeKey ?? string.Empty;
            rule.FindPropertyRelative("driverRange").enumValueIndex = (int)driverRange;
            rule.FindPropertyRelative("invert").boolValue = false;
            rule.FindPropertyRelative("readWrittenThisFrame").boolValue = false;
            rule.FindPropertyRelative("missingPolicy").enumValueIndex = (int)HoBlinkMissingPolicy.Skip;
            rule.FindPropertyRelative("jellyEnabled").boolValue = true;
            rule.FindPropertyRelative("frequency").floatValue = frequency;
            rule.FindPropertyRelative("dampingRatio").floatValue = dampingRatio;
            rule.FindPropertyRelative("inputSmoothing").floatValue = inputSmoothing;
            rule.FindPropertyRelative("maxStep").floatValue = 0.016f;
            rule.FindPropertyRelative("driverGain").floatValue = 1.0f;
            rule.FindPropertyRelative("blinkEnvelope").floatValue = 0.025f;
            rule.FindPropertyRelative("resetOnEnable").boolValue = true;
            rule.FindPropertyRelative("targets").ClearArray();
            return rule;
        }

        private static SerializedProperty AddTarget(SerializedProperty list, string keyName, HoBlinkSide side)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            SerializedProperty target = list.GetArrayElementAtIndex(list.arraySize - 1);
            target.FindPropertyRelative("label").stringValue = string.Empty;   // 插入会复制上一个元素，标签必须显式清掉
            target.FindPropertyRelative("meshScope").enumValueIndex = (int)HoShapeKeyMeshScope.All;
            target.FindPropertyRelative("meshIndex").intValue = 0;
            target.FindPropertyRelative("keyName").stringValue = keyName ?? string.Empty;
            target.FindPropertyRelative("side").enumValueIndex = (int)side;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)HoShapeKeyBlendMode.Additive;
            target.FindPropertyRelative("weight").floatValue = 1.0f;
            target.FindPropertyRelative("gain").floatValue = 1.0f;
            target.FindPropertyRelative("offset").floatValue = 0.0f;
            target.FindPropertyRelative("outputMin").floatValue = 0.0f;
            target.FindPropertyRelative("outputMax").floatValue = 100.0f;
            target.FindPropertyRelative("clampToRange").boolValue = true;
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)HoShapeKeyRampPreset.Direct;
            target.FindPropertyRelative("rampIntensity").floatValue = 1.0f;
            target.FindPropertyRelative("rampAttack").floatValue = 0.0f;
            target.FindPropertyRelative("rampRelease").floatValue = 0.0f;
            return target;
        }

        private static string FindKey(HoBlinkConstraint constraint, HoBlinkKeySemantic semantic, HoBlinkSide side)
        {
            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic != semantic || entry.Side != side)
                {
                    continue;
                }

                if (constraint.KeyExists(entry.Name))
                {
                    return entry.Name;
                }
            }

            return null;
        }

        /// <summary>找不到就退回该语义的首选名（会显示成缺失，提示用户改）。</summary>
        private static string FindKeyOrPreferred(HoBlinkConstraint constraint, HoBlinkKeySemantic semantic, HoBlinkSide side = HoBlinkSide.Both)
        {
            string found = FindKey(constraint, semantic, side);
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }

            if (side == HoBlinkSide.Both)
            {
                found = FindKey(constraint, semantic, HoBlinkSide.Left);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic == semantic && (side == HoBlinkSide.Both || entry.Side == side))
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

        private static string Describe(string keyName)
        {
            return string.IsNullOrEmpty(keyName) ? "（网格上没有这类键，留空待填）" : keyName;
        }
    }
}
