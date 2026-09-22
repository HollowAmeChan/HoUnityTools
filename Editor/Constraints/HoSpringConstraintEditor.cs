using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 弹簧驱动的面板。版面**照抄同目录的约束面板**（`HoFollowConstraintEditor` 那一套）：
    /// 顶部一行粗体组件名 + 预设按钮，下面一组可折叠的 `DrawSectionHeader` 分区
    /// （各自一个强调色 + 一句摘要），分区里用 `PropertyField` / `Slider`，
    /// 依赖项用 `DisabledScope` 压灰，最后是只读的调试读数。
    ///
    /// **加元素走真对象**（`component.Targets.Add(new HoSpringTarget(...))`）而不是
    /// `SerializedProperty.InsertArrayElementAtIndex`：后者插入的嵌套对象**不跑字段初始化**，
    /// 新目标会是"增益 0 / 权重 0 / 输出上限 0"——三个 0 摆在那儿，用户只会觉得"没效果"。
    /// </summary>
    [CustomEditor(typeof(HoSpringConstraint))]
    internal sealed class HoSpringConstraintEditor : UnityEditor.Editor
    {
        private static readonly Color MeshColor = new Color(0.36f, 0.71f, 0.96f);
        private static readonly Color InputColor = new Color(0.42f, 0.84f, 0.56f);
        private static readonly Color SpringColor = new Color(0.78f, 0.55f, 1.0f);
        private static readonly Color TargetColor = new Color(1.0f, 0.70f, 0.28f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        private static readonly GUIContent FrequencyLabel = new GUIContent("跟随", "跟进多快（Hz）。果冻眼横向 6、纵向 8.5 —— 两个自由度频率不同，轨迹才不是一根直线。");
        private static readonly GUIContent DampingLabel = new GUIContent("回弹", "阻尼比。0.25 有明显果冻感；1 是临界阻尼，完全不超调。");
        private static readonly GUIContent GainLabel = new GUIContent("增益", "输入增益。1 表示输入满值就驱动到满。");
        private static readonly GUIContent MergeLabel = new GUIContent("合并", "多路写同一个键时怎么合。果冻超调看得见要靠「软饱和」。");
        private static readonly GUIContent WriteLabel = new GUIContent("写入", "关掉就只跑弹簧不写键（调试用）。");
        private static readonly GUIContent ReversedLabel = new GUIContent("反", "吃弹簧的负半周（回弹侧）。果冻「弹回来」的那一下就是这一路。");
        private static readonly GUIContent TargetGainLabel = new GUIContent("增益");
        private static readonly GUIContent TargetWeightLabel = new GUIContent("强度", "0~1，这一路出多少力。");
        private static readonly GUIContent RampLabel = new GUIContent("成形", "ramp：这一路输出怎么随驱动成形。多数用「直通」，果冻感交给弹簧。");
        private static readonly GUIContent RampIntensityLabel = new GUIContent("强度", "按 ramp 预设的语义缩放。");
        private static readonly GUIContent OutputMaxLabel = new GUIContent("上限", "输出上限。果冻超调看不见时把它压到 85~90，过冲就露出来了（默认已是 90）。");
        private static readonly GUIContent ClampLabel = new GUIContent("钳制");
        private static readonly GUIContent BlendLabel = new GUIContent("混合", "叠加 = 基础值 + 本路；覆盖 = 直接写本路。");

        private SerializedProperty meshes;
        private SerializedProperty keyNames;
        private SerializedProperty frequency;
        private SerializedProperty damping;
        private SerializedProperty gain;
        private SerializedProperty targets;
        private SerializedProperty writingEnabled;
        private SerializedProperty mergeMode;

        private bool meshExpanded = true;
        private bool inputExpanded = true;
        private bool springExpanded = true;
        private bool targetExpanded = true;
        private bool debugExpanded;
        private readonly List<bool> targetDetails = new List<bool>();
        private readonly HashSet<string> usedKeys = new HashSet<string>();

        private void OnEnable()
        {
            meshes = Find("meshes");
            keyNames = Find("keyNames");
            frequency = Find("frequency");
            damping = Find("damping");
            gain = Find("gain");
            targets = Find("targets");
            writingEnabled = Find("writingEnabled");
            mergeMode = Find("mergeMode");
        }

        public override void OnInspectorGUI()
        {
            var component = (HoSpringConstraint)target;
            serializedObject.Update();

            DrawPresetToolbar(component);
            EditorGUILayout.Space(4.0f);

            DrawMeshSection(component);
            DrawInputSection(component);
            DrawSpringSection(component);
            DrawTargetSection(component);
            DrawDebugSection(component);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty Find(string propertyName) => serializedObject.FindProperty(propertyName);

        // ── 预设工具栏（照抄 HoFollowConstraintEditor.DrawPresetToolbar）────────────
        private void DrawPresetToolbar(HoSpringConstraint component)
        {
            EditorGUILayout.LabelField("Ho 弹簧驱动", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("果冻眼 ×2",
                    "加两根：横向 6Hz、纵向 8.5Hz、回弹 0.25。输入键自动匹配眨眼键，目标键留给你接。")))
            {
                HoSpringPresetActions.AddJellyEye(component);
            }

            if (GUILayout.Button(new GUIContent("复制这一根",
                    "同一套网格和输入键，再加一根，自己去调频率。")))
            {
                HoSpringPresetActions.Duplicate(component);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("一个组件就是一弹簧，所以预设会往这台 GameObject 上加组件。", EditorStyles.wordWrappedMiniLabel);
        }

        // ── 网格 ──────────────────────────────────────────────────────────────
        private void DrawMeshSection(HoSpringConstraint component)
        {
            string summary = meshes.arraySize + " 个";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref meshExpanded, "网格", summary, MeshColor))
            {
                return;
            }

            for (int i = 0; i < meshes.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                SerializedProperty element = meshes.GetArrayElementAtIndex(i);
                element.objectReferenceValue = EditorGUILayout.ObjectField(element.objectReferenceValue,
                    typeof(SkinnedMeshRenderer), true);
                if (GUILayout.Button(new GUIContent("✕", "移除"), GUILayout.Width(20.0f)))
                {
                    RemoveAt(component.Meshes, i);
                    return;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(new GUIContent("＋ 网格")))
            {
                Add(component, () => component.Meshes.Add(null), "加网格");
            }

            if (meshes.arraySize == 0)
            {
                EditorGUILayout.HelpBox("还没指定网格 —— 读和写都在网格上，先把它加进来。", MessageType.Info);
            }
        }

        // ── 输入 ──────────────────────────────────────────────────────────────
        private void DrawInputSection(HoSpringConstraint component)
        {
            string summary = keyNames.arraySize == 0 ? "未指定" : KeySummary();
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref inputExpanded, "输入", summary, InputColor))
            {
                return;
            }

            for (int i = 0; i < keyNames.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                DrawKeyField(component, keyNames.GetArrayElementAtIndex(i));
                if (GUILayout.Button(new GUIContent("✕", "移除"), GUILayout.Width(20.0f)))
                {
                    RemoveAt(component.KeyNames, i);
                    return;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(new GUIContent("＋ 输入键")))
            {
                Add(component, () => component.KeyNames.Add(string.Empty), "加输入键");
            }

            EditorGUILayout.LabelField("读这些键做输入，取其中最大的那个 —— 双眼键模型填一个键，左右键模型填两个。",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ── 弹簧 ──────────────────────────────────────────────────────────────
        private void DrawSpringSection(HoSpringConstraint component)
        {
            string summary = HoConstraintEditorSectionGui.FloatSummary(frequency, " Hz")
                + " · 回弹 " + HoConstraintEditorSectionGui.FloatSummary(damping);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref springExpanded, "弹簧", summary, SpringColor))
            {
                return;
            }

            EditorGUILayout.Slider(frequency, 0.1f, HoFaceJelly.MaxFrequency, FrequencyLabel);
            EditorGUILayout.Slider(damping, 0.0f, 2.0f, DampingLabel);
            EditorGUILayout.Slider(gain, 0.0f, 2.0f, GainLabel);

            mergeMode.enumValueIndex = EditorGUILayout.IntPopup(MergeLabel, mergeMode.enumValueIndex,
                MergeModeLabels, TwoEnumValues);
            writingEnabled.boolValue = EditorGUILayout.Toggle(WriteLabel, writingEnabled.boolValue);
        }

        // ── 目标 ──────────────────────────────────────────────────────────────
        private void DrawTargetSection(HoSpringConstraint component)
        {
            string summary = targets.arraySize == 0 ? "未指定" : targets.arraySize + " 个";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref targetExpanded, "目标", summary, TargetColor))
            {
                return;
            }

            for (int i = 0; i < targets.arraySize; i++)
            {
                if (DrawTargetRow(component, i))
                {
                    return;
                }
            }

            if (GUILayout.Button(new GUIContent("＋ 目标")))
            {
                Add(component, () => component.Targets.Add(new HoSpringTarget(string.Empty, 1.0f)), "加目标");
            }

            if (targets.arraySize == 0)
            {
                EditorGUILayout.HelpBox("还没有目标 —— 弹簧算出来的值没有地方去。「果冻眼 ×2」预设会给每根弹簧放好两行（挤压 / 回弹）。", MessageType.Info);
            }
        }

        /// <summary>返回 true 表示这一帧不要再往下画了（刚删掉一行）。</summary>
        private bool DrawTargetRow(HoSpringConstraint component, int index)
        {
            while (targetDetails.Count <= index)
            {
                targetDetails.Add(false);
            }

            SerializedProperty entry = targets.GetArrayElementAtIndex(index);
            SerializedProperty inner = entry != null ? entry.FindPropertyRelative("target") : null;
            SerializedProperty reversed = entry != null ? entry.FindPropertyRelative("reversed") : null;
            SerializedProperty keyName = inner != null ? inner.FindPropertyRelative("keyName") : null;
            if (keyName == null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("这一行是空的（旧数据）—— 删掉重加一次。", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("✕", "移除"), GUILayout.Width(20.0f)))
                {
                    RemoveAt(component.Targets, index);
                    return true;
                }

                EditorGUILayout.EndHorizontal();
                return false;
            }

            EditorGUILayout.BeginHorizontal();
            DrawKeyField(component, keyName);
            if (reversed != null)
            {
                reversed.boolValue = EditorGUILayout.ToggleLeft(ReversedLabel, reversed.boolValue, GUILayout.Width(38.0f));
            }

            targetDetails[index] = HoConstraintEditorControls.InlineFoldout(targetDetails[index], "更多",
                "成形、输出范围、混合方式 —— 多数情况不用动。");
            if (GUILayout.Button(new GUIContent("✕", "移除"), GUILayout.Width(20.0f)))
            {
                RemoveAt(component.Targets, index);
                return true;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            Set(inner, "gain", p => p.floatValue = EditorGUILayout.FloatField(TargetGainLabel, p.floatValue));
            Set(inner, "weight", p => p.floatValue = EditorGUILayout.Slider(TargetWeightLabel, p.floatValue, 0.0f, 1.0f));

            if (targetDetails[index])
            {
                Set(inner, "rampPreset", p => p.enumValueIndex = EditorGUILayout.IntPopup(RampLabel,
                    p.enumValueIndex, RampLabels, RampValues));
                Set(inner, "rampIntensity", p => p.floatValue = EditorGUILayout.Slider(RampIntensityLabel, p.floatValue, 0.0f, 2.0f));
                Set(inner, "outputMax", p => p.floatValue = EditorGUILayout.FloatField(OutputMaxLabel, p.floatValue));
                Set(inner, "clampToRange", p => p.boolValue = EditorGUILayout.Toggle(ClampLabel, p.boolValue));
                Set(inner, "blendMode", p => p.enumValueIndex = EditorGUILayout.IntPopup(BlendLabel,
                    p.enumValueIndex, BlendModeLabels, TwoEnumValues));
            }

            EditorGUI.indentLevel--;
            return false;
        }

        // ── 调试 ──────────────────────────────────────────────────────────────
        private void DrawDebugSection(HoSpringConstraint component)
        {
            bool live = Application.isPlaying;
            string summary = live ? component.SpringValue.ToString("+0.00;-0.00;0.00") : "未播放";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(!live))
            {
                EditorGUILayout.Slider("输入", component.ReadInput(), 0.0f, 1.0f);
                EditorGUILayout.Slider("弹簧", component.SpringValue, -1.0f, 1.5f);
                for (int i = 0; i < targets.arraySize; i++)
                {
                    EditorGUILayout.FloatField("目标 " + i + " 输出", component.GetTargetOutput(i));
                }
            }

            EditorGUILayout.LabelField("弹簧超过 1 的那一段就是超调 —— 果冻之所以是果冻全靠它；"
                + "看不见就把目标的「上限」压到 85~90。", EditorStyles.wordWrappedMiniLabel);

            IReadOnlyList<string> missing = component.MissingKeys;
            if (missing != null && missing.Count > 0)
            {
                EditorGUILayout.HelpBox("这些键在网格上找不到，会被跳过：\n" + string.Join("\n", ToArray(missing, 6)),
                    MessageType.Warning);
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
        /// 加元素走真对象：`InsertArrayElementAtIndex` 插入的嵌套对象不跑字段初始化，
        /// 会得到"增益 0 / 权重 0 / 上限 0"三个 0。顺带避开"复制上一个元素"的键名残留。
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

        private string KeySummary()
        {
            string text = string.Empty;
            for (int i = 0; i < keyNames.arraySize; i++)
            {
                string key = keyNames.GetArrayElementAtIndex(i).stringValue;
                text += (i == 0 ? string.Empty : " / ") + (string.IsNullOrEmpty(key) ? "?" : key);
            }

            return text;
        }

        private static string[] ToArray(IReadOnlyList<string> values, int max)
        {
            int count = Mathf.Min(max, values.Count);
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = values[i];
            }

            return result;
        }

        private static readonly GUIContent[] MergeModeLabels =
        {
            new GUIContent("夹断"),
            new GUIContent("软饱和"),
            new GUIContent("按比例分配")
        };

        private static readonly GUIContent[] RampLabels =
        {
            new GUIContent("直通"),
            new GUIContent("柔跟"),
            new GUIContent("放大"),
            new GUIContent("缓入"),
            new GUIContent("慢放"),
            new GUIContent("阶梯"),
            new GUIContent("自定义")
        };

        private static readonly GUIContent[] BlendModeLabels =
        {
            new GUIContent("叠加"),
            new GUIContent("覆盖")
        };

        private static readonly int[] TwoEnumValues = { 0, 1 };
        private static readonly int[] RampValues = { 0, 1, 2, 3, 4, 5, 6 };
    }
}
