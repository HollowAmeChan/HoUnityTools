using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 弹簧驱动的面板。
    ///
    /// **版面只有两层列表**：读哪些键、写哪些键。没有"规则套自由度套目标"那种三级折叠 ——
    /// 一个组件就是一弹簧，要几个自由度就挂几个组件（果冻眼 = 两个）。
    ///
    /// **加元素走的是真对象**（`component.Targets.Add(new HoSpringTarget(...))`）而不是
    /// `SerializedProperty.InsertArrayElementAtIndex`：后者插入的嵌套对象**不跑字段初始化**，
    /// 于是新目标会是"增益 0 / 权重 0 / 输出上限 0"——三个 0 摆在那儿，用户只会觉得"没效果"，
    /// 而不会知道是自己没填。（这个坑是默认 Inspector 的截图暴露出来的。）
    /// </summary>
    [CustomEditor(typeof(HoSpringConstraint))]
    internal sealed class HoSpringConstraintEditor : UnityEditor.Editor
    {
        private SerializedProperty meshes;
        private SerializedProperty keyNames;
        private SerializedProperty frequency;
        private SerializedProperty damping;
        private SerializedProperty gain;
        private SerializedProperty targets;
        private SerializedProperty writingEnabled;
        private SerializedProperty mergeMode;
        private readonly List<bool> more = new List<bool>();
        private readonly HashSet<string> usedKeys = new HashSet<string>();

        private void OnEnable()
        {
            meshes = serializedObject.FindProperty("meshes");
            keyNames = serializedObject.FindProperty("keyNames");
            frequency = serializedObject.FindProperty("frequency");
            damping = serializedObject.FindProperty("damping");
            gain = serializedObject.FindProperty("gain");
            targets = serializedObject.FindProperty("targets");
            writingEnabled = serializedObject.FindProperty("writingEnabled");
            mergeMode = serializedObject.FindProperty("mergeMode");
        }

        public override void OnInspectorGUI()
        {
            var component = (HoSpringConstraint)target;
            serializedObject.Update();

            bool live = Application.isPlaying;
            float value = live ? component.SpringValue : 0.0f;
            HoConstraintEditorControls.Title("弹簧驱动",
                component.Frequency.ToString("0.##") + " Hz · 回弹 " + component.Damping.ToString("0.##"),
                (writingEnabled.boolValue ? "写入 开" : "写入 关", writingEnabled.boolValue),
                (live ? value.ToString("+0.00;-0.00;0.00") : "未播放", live && Mathf.Abs(value) > 0.005f));

            DrawPresets(component);
            DrawMeshes(component);
            DrawInputs(component);
            DrawSpring(component);
            DrawTargets(component);
            DrawProblems(component);

            serializedObject.ApplyModifiedProperties();
        }

        // ── 预设 ──────────────────────────────────────────────────────────────
        private void DrawPresets(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("预设", HoConstraintEditorTheme.LabelWidth,
                        "一个组件 = 一根弹簧，所以预设会往这台 GameObject 上加组件。");
                    if (HoConstraintEditorControls.Button("果冻眼 ×2", "加两根：横向 6Hz、纵向 8.5Hz、回弹 0.25。输入键自动匹配眨眼键，目标键留给你接。", true, 84.0f))
                    {
                        HoSpringPresetActions.AddJellyEye(component);
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("复制这一根", "同一套网格和输入键，再加一根，自己去调频率。", false, 84.0f))
                    {
                        HoSpringPresetActions.Duplicate(component);
                    }

                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption("果冻眼 = 两个组件");
                }
            }
        }

        // ── 网格 ──────────────────────────────────────────────────────────────
        private void DrawMeshes(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("网格", HoConstraintEditorTheme.LabelWidth, "读和写都在这几张网格上。");
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(meshes.arraySize + " 个");
                }

                using (HoConstraintEditorControls.Indent())
                {
                    for (int i = 0; i < meshes.arraySize; i++)
                    {
                        using (HoConstraintEditorControls.Row())
                        {
                            SerializedProperty element = meshes.GetArrayElementAtIndex(i);
                            element.objectReferenceValue = EditorGUI.ObjectField(
                                HoConstraintEditorControls.NextFlexible(120.0f), element.objectReferenceValue,
                                typeof(SkinnedMeshRenderer), true);
                            if (HoConstraintEditorControls.IconButton("✕", "移除", 18.0f))
                            {
                                RemoveAt(component.Meshes, i);
                                return;
                            }
                        }
                    }

                    using (HoConstraintEditorControls.Row())
                    {
                        if (HoConstraintEditorControls.Button("＋ 网格", null, meshes.arraySize == 0, 62.0f))
                        {
                            Add(component, () => component.Meshes.Add(null), "加网格");
                        }

                        if (meshes.arraySize == 0)
                        {
                            HoConstraintEditorControls.Gap();
                            HoConstraintEditorControls.Caption("先指定网格，键名才有东西可选。");
                        }
                    }
                }
            }
        }

        // ── 输入键 ────────────────────────────────────────────────────────────
        private void DrawInputs(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("输入键", HoConstraintEditorTheme.LabelWidth,
                        "读这些键做输入，取其中最大的那个 —— 双眼键模型填一个键，左右键模型填两个。");
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(keyNames.arraySize + " 个");
                }

                using (HoConstraintEditorControls.Indent())
                {
                    if (keyNames.arraySize == 0)
                    {
                        HoConstraintEditorControls.Caption("还没有输入键。果冻眼用「果冻眼 ×2」预设会自动填好。");
                    }

                    for (int i = 0; i < keyNames.arraySize; i++)
                    {
                        using (HoConstraintEditorControls.Row())
                        {
                            DrawKeyField(component, keyNames.GetArrayElementAtIndex(i));
                            HoConstraintEditorControls.Flex();
                            if (HoConstraintEditorControls.IconButton("✕", "移除", 18.0f))
                            {
                                RemoveAt(component.KeyNames, i);
                                return;
                            }
                        }
                    }

                    using (HoConstraintEditorControls.Row())
                    {
                        if (HoConstraintEditorControls.Button("＋ 输入键", null, false, 72.0f))
                        {
                            Add(component, () => component.KeyNames.Add(string.Empty), "加输入键");
                        }
                    }
                }
            }
        }

        // ── 弹簧 ──────────────────────────────────────────────────────────────
        private void DrawSpring(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("跟随", HoConstraintEditorTheme.LabelWidth,
                        "跟进多快（Hz）。果冻眼横向 6、纵向 8.5 —— 两个自由度频率不同，轨迹才不是一根直线。");
                    frequency.floatValue = HoConstraintEditorControls.NumberField(frequency.floatValue, "Hz", null, 46.0f);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("回弹", HoConstraintEditorTheme.LabelWidthSm,
                        "阻尼比。0.25 有明显果冻感；1 是临界阻尼，完全不超调。");
                    damping.floatValue = HoConstraintEditorControls.NumberField(damping.floatValue, null, null, 40.0f);
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Label("增益", HoConstraintEditorTheme.LabelWidthSm, "输入增益。");
                    gain.floatValue = HoConstraintEditorControls.NumberField(gain.floatValue, null, null, 40.0f);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("输入", HoConstraintEditorTheme.LabelWidth,
                        "这一帧读到的输入（取所有输入键里最大的那个）。");
                    HoConstraintEditorControls.Meter(HoConstraintEditorControls.NextFlexible(80.0f),
                        Application.isPlaying ? component.ReadInput() : 0.0f, 0.0f, 1.0f,
                        HoConstraintEditorTheme.AccentBlink);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("弹簧", HoConstraintEditorTheme.LabelWidth,
                        "0 = 静止，正 = 挤压（会过冲到 1 以上），负 = 回弹。绿色那格就是超调区。");
                    HoConstraintEditorControls.Meter(HoConstraintEditorControls.NextFlexible(80.0f),
                        Application.isPlaying ? component.SpringValue : 0.0f, -1.0f, 1.5f,
                        HoConstraintEditorTheme.AccentDriver, 1.0f);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("合并", HoConstraintEditorTheme.LabelWidth,
                        "多路写同一个键时怎么合。果冻超调看得见要靠「软饱和」。");
                    mergeMode.enumValueIndex = EditorGUI.Popup(HoConstraintEditorControls.Next(96.0f),
                        mergeMode.enumValueIndex, mergeMode.enumDisplayNames, HoConstraintEditorTheme.Field);
                    HoConstraintEditorControls.Flex();
                    writingEnabled.boolValue = HoConstraintEditorControls.Toggle("写入", writingEnabled.boolValue,
                        "关掉就只跑弹簧不写键（调试用）。", 50.0f);
                }
            }
        }

        // ── 目标 ──────────────────────────────────────────────────────────────
        private void DrawTargets(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("目标", HoConstraintEditorTheme.LabelWidth,
                        "弹簧驱动哪些键。正向吃挤压、反向吃回弹 —— 果冻的回弹就靠反向目标。");
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(targets.arraySize + " 个");
                }

                using (HoConstraintEditorControls.Indent())
                {
                    if (targets.arraySize == 0)
                    {
                        HoConstraintEditorControls.Caption("还没有目标。果冻眼预设会给每根弹簧放两行（挤压 / 回弹）等你填键。");
                    }

                    for (int i = 0; i < targets.arraySize; i++)
                    {
                        if (DrawTargetRow(component, i))
                        {
                            return;
                        }
                    }

                    using (HoConstraintEditorControls.Row())
                    {
                        if (HoConstraintEditorControls.Button("＋ 目标", "加一路输出。", false, 62.0f))
                        {
                            Add(component, () => component.Targets.Add(new HoSpringTarget(string.Empty, 1.0f)), "加目标");
                        }
                    }
                }
            }
        }

        /// <summary>返回 true 表示这一帧不要再往下画了（刚删掉一行）。</summary>
        private bool DrawTargetRow(HoSpringConstraint component, int index)
        {
            while (more.Count <= index)
            {
                more.Add(false);
            }

            SerializedProperty entry = targets.GetArrayElementAtIndex(index);
            SerializedProperty inner = entry != null ? entry.FindPropertyRelative("target") : null;
            SerializedProperty reversed = entry != null ? entry.FindPropertyRelative("reversed") : null;
            SerializedProperty keyName = inner != null ? inner.FindPropertyRelative("keyName") : null;
            if (keyName == null)
            {
                // 万一是空壳（没有 target 子对象），说明这一行没法用；提示而不是静默画一半。
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption("这一行是空的（旧数据）—— 删掉重加一次。");
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.IconButton("✕", "移除", 18.0f))
                    {
                        RemoveAt(component.Targets, index);
                        return true;
                    }
                }

                return false;
            }

            using (HoConstraintEditorControls.Row(true))
            {
                DrawKeyField(component, keyName);
                HoConstraintEditorControls.Gap(4.0f);
                if (reversed != null)
                {
                    reversed.boolValue = HoConstraintEditorControls.Toggle("反", reversed.boolValue,
                        "吃弹簧的负半周（回弹侧）。果冻「弹回来」的那一下就是这一路。", 44.0f);
                }

                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption("增益");
                Set(inner, "gain", p => p.floatValue = HoConstraintEditorControls.NumberField(p.floatValue, null, null, 38.0f));
                HoConstraintEditorControls.Caption("强度");
                Set(inner, "weight", p => p.floatValue = HoConstraintEditorControls.NumberField(p.floatValue, null,
                    "0~1，这一路出多少力。", 34.0f));
                more[index] = HoConstraintEditorControls.InlineFoldout(more[index], "更多",
                    "成形、输出范围、混合方式 —— 多数情况不用动。");
                if (HoConstraintEditorControls.IconButton("✕", "移除", 18.0f))
                {
                    RemoveAt(component.Targets, index);
                    return true;
                }
            }

            if (!more[index])
            {
                return false;
            }

            using (HoConstraintEditorControls.Indent(24.0f))
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("成形", 34.0f, "ramp：这一路输出怎么随驱动成形。多数用「直通」，果冻感交给弹簧。");
                    Set(inner, "rampPreset", p => p.enumValueIndex = EditorGUI.Popup(
                        HoConstraintEditorControls.Next(80.0f), p.enumValueIndex, p.enumDisplayNames,
                        HoConstraintEditorTheme.Field));
                    HoConstraintEditorControls.Gap(4.0f);
                    Set(inner, "rampIntensity", p => p.floatValue = HoConstraintEditorControls.NumberField(
                        p.floatValue, null, "强度：按预设的语义缩放。", 34.0f));
                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("上限", 34.0f,
                        "输出上限。果冻超调看不见时把它压到 85~90，过冲就露出来了（默认已是 90）。");
                    Set(inner, "outputMax", p => p.floatValue = HoConstraintEditorControls.NumberField(p.floatValue, null, null, 42.0f));
                    HoConstraintEditorControls.Gap(4.0f);
                    Set(inner, "clampToRange", p => p.boolValue = HoConstraintEditorControls.Toggle("钳制", p.boolValue, null, 50.0f));
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("混合", 34.0f, "叠加 = 基础值 + 本路；覆盖 = 直接写本路。");
                    Set(inner, "blendMode", p => p.enumValueIndex = EditorGUI.Popup(
                        HoConstraintEditorControls.Next(72.0f), p.enumValueIndex, p.enumDisplayNames,
                        HoConstraintEditorTheme.Field));
                    HoConstraintEditorControls.Flex();
                    if (Application.isPlaying)
                    {
                        HoConstraintEditorControls.Caption("输出 " + component.GetTargetOutput(index).ToString("0.0"));
                    }
                }
            }

            return false;
        }

        // ── 问题 ──────────────────────────────────────────────────────────────
        private void DrawProblems(HoSpringConstraint component)
        {
            IReadOnlyList<string> missing = component.MissingKeys;
            if (missing == null || missing.Count == 0)
            {
                return;
            }

            using (HoConstraintEditorControls.Card(true))
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("缺失", HoConstraintEditorTheme.LabelWidth,
                        "这些键在网格上找不到，会被跳过。多半是拼错了，或者网格列表里少了一张网格。");
                    HoConstraintEditorControls.Caption(missing.Count + " 个");
                }

                using (HoConstraintEditorControls.Indent())
                {
                    for (int i = 0; i < missing.Count && i < 6; i++)
                    {
                        HoConstraintEditorControls.Caption(missing[i]);
                    }
                }
            }
        }

        // ── 小工具 ────────────────────────────────────────────────────────────
        private void DrawKeyField(HoSpringConstraint component, SerializedProperty property)
        {
            int bindings = component.CountKeyBindings(property.stringValue);
            string status = string.IsNullOrEmpty(property.stringValue)
                ? "还没填键名"
                : bindings > 0 ? "已写在：" + component.DescribeKeyBindings(property.stringValue) : "网格上没有这个键";
            string edited = HoConstraintEditorControls.KeyField(property.stringValue, bindings, status,
                "从当前网格上实际存在的键里选", out Rect dropdownRect, out bool clicked);
            if (edited != property.stringValue)
            {
                property.stringValue = edited;
            }

            if (clicked)
            {
                usedKeys.Clear();
                HoKeyNameDropdown.Show(dropdownRect, component, property, usedKeys);
            }
        }

        /// <summary>
        /// 加元素。走真对象而不是 `InsertArrayElementAtIndex`：后者插入的嵌套对象不跑字段初始化，
        /// 会得到"增益 0 / 权重 0 / 上限 0"。同时清掉"复制上一个元素"带来的键名残留。
        /// </summary>
        private void Add(HoSpringConstraint component, System.Action apply, string undoName)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(component, undoName);
            apply();
            serializedObject.Update();
            EditorUtility.SetDirty(component);
        }

        private void RemoveAt<T>(List<T> list, int index)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "移除");
            list.RemoveAt(index);
            serializedObject.Update();
            EditorUtility.SetDirty(target);
        }

        private static void Set(SerializedProperty parent, string name, System.Action<SerializedProperty> apply)
        {
            SerializedProperty property = parent != null ? parent.FindPropertyRelative(name) : null;
            if (property != null)
            {
                apply(property);
            }
        }
    }
}
