using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 预设：只负责把键名与起点参数写进序列化数据，用户自建的高光/眼仁键始终留空由用户填。
    /// 凝视与果冻预设是"追加"规则，不会清掉已有规则。
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
        }

        /// <summary>凝视驱动：双眼四向（单极 4 条）或左右眼四向（双极 2 条，可切内外族）。</summary>
        public static void ApplyGazeRules(HoBlinkConstraint constraint, SerializedObject serializedObject, bool splitEyes, bool inOutFamily = false)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");

            if (!splitEyes)
            {
                AddRule(rules, "凝视 上", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddRule(rules, "凝视 下", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddRule(rules, "凝视 左", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddRule(rules, "凝视 右", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                serializedObject.ApplyModifiedProperties();
                Rebuild(constraint);
                return;
            }

            string positiveX;
            string negativeX;
            if (inOutFamily)
            {
                // 内/外族（ARKit、PICO）：左眼的 "内" 是往右看、"外" 是往左看
                positiveX = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left);
                negativeX = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left);
            }
            else
            {
                positiveX = FirstNotEmpty(
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both, false),
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Left));
                negativeX = FirstNotEmpty(
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both, false),
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left));
            }

            string positiveY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left));
            string negativeY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left));

            AddRule(rules, "凝视 X（右 − 左）", HoBlinkDriverKind.ShapeKey, positiveX, negativeX, HoBlinkDriverRange.Bipolar, false, 4.0f, 0.35f, 0.03f);
            AddRule(rules, "凝视 Y（上 − 下）", HoBlinkDriverKind.ShapeKey, positiveY, negativeY, HoBlinkDriverRange.Bipolar, false, 4.0f, 0.35f, 0.03f);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
        }

        /// <summary>
        /// 高光跟眼：只加 **两条** 双极规则（X：往右 − 往左，Y：上 − 下），驱动键取模型上真实存在的凝视键，
        /// 目标键**留空**由用户填高光键。找不到凝视键时驱动键留空、不硬填不存在的名字。
        /// </summary>
        public static void ApplyGazeJelly(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            AddJellyGazeRules(
                constraint,
                serializedObject,
                "高光跟眼",
                HoShapeKeyRampPreset.Direct,
                1.0f,
                4.0f,
                0.35f,
                0.03f);
        }

        /// <summary>眼仁形变：同上两条规则，慢一点、缓入、幅度小一点。</summary>
        public static void ApplyPupilJelly(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            AddJellyGazeRules(
                constraint,
                serializedObject,
                "眼仁形变",
                HoShapeKeyRampPreset.EaseIn,
                0.8f,
                3.0f,
                0.35f,
                0.05f);
        }

        /// <summary>眨眼压高光：AutoBlink 驱动（不需要键）+ 一个空目标（覆盖 + 放大）。</summary>
        public static void ApplyBlinkJelly(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");
            AddRule(rules, "眨眼压高光", HoBlinkDriverKind.AutoBlink, string.Empty, string.Empty, HoBlinkDriverRange.Unipolar, false, 6.0f, 0.3f, 0.02f);
            SerializedProperty rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            SerializedProperty targets = rule.FindPropertyRelative("targets");
            SerializedProperty target = AddTarget(targets, string.Empty, HoBlinkSide.Both);
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)HoShapeKeyRampPreset.Amplify;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)HoShapeKeyBlendMode.Override;
            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
            Debug.Log(
                "[HoBlinkConstraint] 眨眼压高光：加了 1 条规则，驱动 = 自动眨眼（不需要键）；"
                + "目标键留空 —— 在规则里点 ▾ 选高光键。",
                constraint);
        }

        /// <summary>
        /// 果冻用的两条双极规则。驱动键优先"相对头"的看左/看右，其次"相对眼球"的 In/Out；
        /// **找不到就留空**（不硬填内置表里的名字），由用户在规则里手填。
        /// </summary>
        private static void AddJellyGazeRules(
            HoBlinkConstraint constraint,
            SerializedObject serializedObject,
            string presetName,
            HoShapeKeyRampPreset rampPreset,
            float intensity,
            float frequency,
            float dampingRatio,
            float smoothing)
        {
            serializedObject.ApplyModifiedProperties();
            SerializedProperty rules = serializedObject.FindProperty("rules");

            string positiveX = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left, false));
            string negativeX = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left, false));
            string positiveY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left, false));
            string negativeY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left, false));

            AddRule(rules, "果冻 X（往右 − 往左）", HoBlinkDriverKind.ShapeKey, positiveX, negativeX, HoBlinkDriverRange.Bipolar, false, frequency, dampingRatio, smoothing);
            AddEmptyTarget(rules, rampPreset, intensity);

            AddRule(rules, "果冻 Y（上 − 下）", HoBlinkDriverKind.ShapeKey, positiveY, negativeY, HoBlinkDriverRange.Bipolar, false, frequency, dampingRatio, smoothing);
            AddEmptyTarget(rules, rampPreset, intensity);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);

            Debug.Log(
                "[HoBlinkConstraint] " + presetName + "：加了 2 条规则（果冻 X / 果冻 Y）。\n"
                + "· 驱动键：往右 = " + Describe(positiveX) + "，往左 = " + Describe(negativeX)
                + "，上 = " + Describe(positiveY) + "，下 = " + Describe(negativeY) + "\n"
                + "· 目标键留空 —— 在规则里点 ▾ 选你的高光/眼仁键。",
                constraint);

            static string Describe(string keyName)
            {
                return string.IsNullOrEmpty(keyName) ? "（模型上没有这类键，留空待填）" : keyName;
            }
        }

        /// <summary>给最后一条规则加一个空目标（键名留空，等用户填）。</summary>
        private static void AddEmptyTarget(SerializedProperty rules, HoShapeKeyRampPreset rampPreset, float intensity)
        {
            SerializedProperty targets = rules.GetArrayElementAtIndex(rules.arraySize - 1).FindPropertyRelative("targets");
            SerializedProperty target = AddTarget(targets, string.Empty, HoBlinkSide.Both);
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)rampPreset;
            target.FindPropertyRelative("rampIntensity").floatValue = intensity;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)HoShapeKeyBlendMode.Additive;
        }

        private static SerializedProperty AddRule(
            SerializedProperty rules,
            string label,
            HoBlinkDriverKind driverKind,
            string positiveKey,
            string negativeKey,
            HoBlinkDriverRange driverRange,
            bool invert,
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
            rule.FindPropertyRelative("invert").boolValue = invert;
            rule.FindPropertyRelative("readWrittenThisFrame").boolValue = false;
            rule.FindPropertyRelative("missingPolicy").enumValueIndex = (int)HoBlinkMissingPolicy.Skip;
            rule.FindPropertyRelative("jellyEnabled").boolValue = true;
            rule.FindPropertyRelative("frequency").floatValue = frequency;
            rule.FindPropertyRelative("dampingRatio").floatValue = dampingRatio;
            rule.FindPropertyRelative("inputSmoothing").floatValue = inputSmoothing;
            rule.FindPropertyRelative("maxStep").floatValue = 0.016f;
            rule.FindPropertyRelative("resetOnEnable").boolValue = true;
            rule.FindPropertyRelative("targets").ClearArray();
            return rule;
        }

        private static SerializedProperty AddTarget(SerializedProperty list, string keyName, HoBlinkSide side)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            SerializedProperty target = list.GetArrayElementAtIndex(list.arraySize - 1);
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
        private static string FindKeyOrPreferred(HoBlinkConstraint constraint, HoBlinkKeySemantic semantic, HoBlinkSide side = HoBlinkSide.Both, bool allowFallbackName = true)
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

            if (!allowFallbackName)
            {
                return null;
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
    }
}
