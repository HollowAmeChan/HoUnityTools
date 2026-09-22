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
    /// 目标那一行只放日常要动的四个：键名 / 正反向 / 增益 / 强度；`HoShapeKeyTarget` 剩下的
    /// （ramp、输出范围、混合方式）收在每行末尾的「更多」里 —— 一层、且是次要的，
    /// 而不是把 16 个字段全摊开。
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
        private readonly List<bool> more = new List<bool>();
        private readonly List<string> options = new List<string>();
        private string optionStamp = string.Empty;

        private void OnEnable()
        {
            meshes = serializedObject.FindProperty("meshes");
            keyNames = serializedObject.FindProperty("keyNames");
            frequency = serializedObject.FindProperty("frequency");
            damping = serializedObject.FindProperty("damping");
            gain = serializedObject.FindProperty("gain");
            targets = serializedObject.FindProperty("targets");
            writingEnabled = serializedObject.FindProperty("writingEnabled");
        }

        public override void OnInspectorGUI()
        {
            var component = (HoSpringConstraint)target;
            serializedObject.Update();

            bool live = Application.isPlaying;
            HoConstraintEditorControls.Title("弹簧驱动",
                component.Frequency.ToString("0.##") + " Hz",
                (writingEnabled.boolValue ? "写入 开" : "写入 关", writingEnabled.boolValue),
                (live ? "▲ " + component.SpringValue.ToString("0.00") : "未播放", live && component.SpringValue > 0.01f));

            DrawMeshes();
            DrawInputs(component);
            DrawSpring(component);
            DrawTargets();

            HoConstraintEditorControls.Separator(6.0f, 4.0f);
            DrawActions(component);

            serializedObject.ApplyModifiedProperties();
        }

        // ── 网格 ──────────────────────────────────────────────────────────────
        private void DrawMeshes()
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
                        if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18.0f)))
                        {
                            meshes.DeleteArrayElementAtIndex(i--);
                        }
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("＋ 网格", "把这个角色上的网格加进来。", false, 62.0f))
                    {
                        meshes.InsertArrayElementAtIndex(meshes.arraySize);
                        meshes.GetArrayElementAtIndex(meshes.arraySize - 1).objectReferenceValue = null;
                    }
                }
            }
        }

        // ── 输入键 ────────────────────────────────────────────────────────────
        private void DrawInputs(HoSpringConstraint component)
        {
            string[] available = AvailableKeys(component);

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("输入键", HoConstraintEditorTheme.LabelWidth,
                    "读这些键做输入，取其中最大的那个 —— 双眼键模型填一个键，左右键模型填两个。");
                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption(keyNames.arraySize + " 个");
            }

            using (HoConstraintEditorControls.Indent())
            {
                for (int i = 0; i < keyNames.arraySize; i++)
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        SerializedProperty element = keyNames.GetArrayElementAtIndex(i);
                        int index = IndexOf(available, element.stringValue);
                        int picked = EditorGUI.Popup(HoConstraintEditorControls.NextFlexible(120.0f), index, available,
                            HoConstraintEditorTheme.Field);
                        if (picked >= 0 && picked < available.Length)
                        {
                            element.stringValue = available[picked];
                        }

                        HoConstraintEditorControls.Flex();
                        if (available.Length == 0)
                        {
                            HoConstraintEditorControls.Pill("先加网格", false);
                        }

                        if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18.0f)))
                        {
                            keyNames.DeleteArrayElementAtIndex(i--);
                        }
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("＋ 输入键", null, false, 72.0f))
                    {
                        keyNames.InsertArrayElementAtIndex(keyNames.arraySize);
                        keyNames.GetArrayElementAtIndex(keyNames.arraySize - 1).stringValue =
                            available.Length > 0 ? available[0] : string.Empty;
                    }
                }
            }
        }

        // ── 弹簧 ──────────────────────────────────────────────────────────────
        private void DrawSpring(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("弹簧", HoConstraintEditorTheme.LabelWidth, null);
                HoConstraintEditorControls.Caption("跟随");
                frequency.floatValue = HoConstraintEditorControls.NumberField(frequency.floatValue, null,
                    "跟进多快（Hz）。果冻眼横向 6、纵向 8.5 —— 两个自由度频率不同，轨迹才不是一根直线。", 46.0f);
                HoConstraintEditorControls.Gap();
                HoConstraintEditorControls.Caption("回弹");
                damping.floatValue = HoConstraintEditorControls.NumberField(damping.floatValue, null,
                    "阻尼比。0.25 有明显果冻感；1 是临界阻尼，完全不超调。", 40.0f);
                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption("增益");
                gain.floatValue = HoConstraintEditorControls.NumberField(gain.floatValue, null, "输入增益。", 40.0f);
            }

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("当前值", HoConstraintEditorTheme.LabelWidth,
                    "0 = 静止，正 = 挤压（会过冲到 1 以上），负 = 回弹。");
                HoConstraintEditorControls.Meter(HoConstraintEditorControls.NextFlexible(80.0f),
                    Application.isPlaying ? component.SpringValue : 0.0f, -1.0f, 1.5f,
                    HoConstraintEditorTheme.AccentDriver);
            }

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Flex();
                writingEnabled.boolValue = HoConstraintEditorControls.Toggle("写入", writingEnabled.boolValue,
                    "关掉就只跑弹簧不写键（调试用）。", 50.0f);
            }
        }

        // ── 目标 ──────────────────────────────────────────────────────────────
        private void DrawTargets()
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
                for (int i = 0; i < targets.arraySize; i++)
                {
                    SerializedProperty entry = targets.GetArrayElementAtIndex(i);
                    SerializedProperty inner = entry.FindPropertyRelative("target");
                    SerializedProperty reversed = entry.FindPropertyRelative("reversed");
                    DrawTargetRow(i, entry, inner, reversed);
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("＋ 目标", "加一路输出。", false, 62.0f))
                    {
                        targets.InsertArrayElementAtIndex(targets.arraySize);
                    }
                }
            }
        }

        private void DrawTargetRow(int index, SerializedProperty entry, SerializedProperty inner, SerializedProperty reversed)
        {
            while (more.Count <= index)
            {
                more.Add(false);
            }

            SerializedProperty keyName = inner.FindPropertyRelative("keyName");
            SerializedProperty weight = inner.FindPropertyRelative("weight");
            SerializedProperty targetGain = inner.FindPropertyRelative("gain");

            using (HoConstraintEditorControls.Row(true))
            {
                keyName.stringValue = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(90.0f),
                    keyName.stringValue, HoConstraintEditorTheme.Field);
                HoConstraintEditorControls.Gap(4.0f);
                reversed.boolValue = HoConstraintEditorControls.Toggle("反", reversed.boolValue,
                    "吃弹簧的负半周（回弹侧）。", 44.0f);
                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption("增益");
                targetGain.floatValue = HoConstraintEditorControls.NumberField(targetGain.floatValue, null, null, 38.0f);
                HoConstraintEditorControls.Caption("强度");
                weight.floatValue = HoConstraintEditorControls.NumberField(weight.floatValue, null,
                    "0~1，这一路出多少力。", 34.0f);
                more[index] = HoConstraintEditorControls.InlineFoldout(more[index], "更多",
                    "混合方式、输出范围、ramp —— 多数情况不用动。");
                if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18.0f)))
                {
                    targets.DeleteArrayElementAtIndex(index);
                    return;
                }
            }

            if (!more[index])
            {
                return;
            }

            using (HoConstraintEditorControls.Indent(24.0f))
            {
                SerializedProperty blendMode = inner.FindPropertyRelative("blendMode");
                SerializedProperty outputMax = inner.FindPropertyRelative("outputMax");
                SerializedProperty clamp = inner.FindPropertyRelative("clampToRange");
                SerializedProperty rampPreset = inner.FindPropertyRelative("rampPreset");
                SerializedProperty rampIntensity = inner.FindPropertyRelative("rampIntensity");

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("混合", 34.0f, null);
                    blendMode.enumValueIndex = EditorGUI.Popup(HoConstraintEditorControls.Next(72.0f),
                        blendMode.enumValueIndex, blendMode.enumDisplayNames, HoConstraintEditorTheme.Field);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Caption("ramp");
                    rampPreset.enumValueIndex = EditorGUI.Popup(HoConstraintEditorControls.Next(78.0f),
                        rampPreset.enumValueIndex, rampPreset.enumDisplayNames, HoConstraintEditorTheme.Field);
                    HoConstraintEditorControls.Gap(4.0f);
                    rampIntensity.floatValue = HoConstraintEditorControls.NumberField(rampIntensity.floatValue, null, null, 34.0f);
                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("上限", 34.0f,
                        "输出上限。果冻超调看不见时把它压到 85~90，过冲就露出来了。");
                    outputMax.floatValue = HoConstraintEditorControls.NumberField(outputMax.floatValue, null, null, 42.0f);
                    HoConstraintEditorControls.Gap(4.0f);
                    clamp.boolValue = HoConstraintEditorControls.Toggle("钳制", clamp.boolValue, null, 50.0f);
                    HoConstraintEditorControls.Flex();
                    if (Application.isPlaying)
                    {
                        HoConstraintEditorControls.Caption("输出 " +
                            ((HoSpringConstraint)target).GetTargetOutput(index).ToString("0.0"));
                    }
                }
            }
        }

        // ── 动作 ──────────────────────────────────────────────────────────────
        private void DrawActions(HoSpringConstraint component)
        {
            using (HoConstraintEditorControls.Row())
            {
                if (HoConstraintEditorControls.Button("＋ 果冻眼（两个组件）",
                        "在这台 GameObject 上加两个组件：横向 6Hz、纵向 8.5Hz、阻尼 0.25，输入键自动匹配眨眼键。目标键留空由你接。",
                        true, 140.0f))
                {
                    AddJellyEye(component);
                }

                HoConstraintEditorControls.Gap();
                if (HoConstraintEditorControls.Button("＋ 再来一根（复制网格和输入）",
                        "同一套网格和输入键，再加一个组件，自己去调频率。", false, 172.0f))
                {
                    DuplicateSpring(component);
                }
            }

            if (component.MissingKeys != null && component.MissingKeys.Count > 0)
            {
                HoConstraintEditorControls.Separator(3.0f, 2.0f);
                string text = "这些键在网格上找不到，会被跳过：";
                for (int i = 0; i < component.MissingKeys.Count && i < 4; i++)
                {
                    text += (i == 0 ? string.Empty : "、") + component.MissingKeys[i];
                }

                HoConstraintEditorControls.Caption(text);
            }
        }

        private static void AddJellyEye(HoSpringConstraint component)
        {
            GameObject host = component.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(host, "Add Jelly Eye Springs");
            var horizontal = Undo.AddComponent<HoSpringConstraint>(host);
            var vertical = Undo.AddComponent<HoSpringConstraint>(host);
            horizontal.Meshes.AddRange(component.Meshes);
            vertical.Meshes.AddRange(component.Meshes);
            HoSpringPresets.ConfigureJelly(horizontal, false);
            HoSpringPresets.ConfigureJelly(vertical, true);
            EditorUtility.SetDirty(horizontal);
            EditorUtility.SetDirty(vertical);
        }

        private static void DuplicateSpring(HoSpringConstraint component)
        {
            GameObject host = component.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(host, "Duplicate Spring");
            var copy = Undo.AddComponent<HoSpringConstraint>(host);
            copy.Meshes.AddRange(component.Meshes);
            copy.KeyNames.AddRange(component.KeyNames);
            EditorUtility.SetDirty(copy);
        }

        // ── 键名候选 ──────────────────────────────────────────────────────────
        /// <summary>网格上真实存在的键（去重、排序）。网格没变就不重算。</summary>
        private string[] AvailableKeys(HoSpringConstraint component)
        {
            string stamp = string.Empty;
            for (int i = 0; i < component.Meshes.Count; i++)
            {
                Mesh mesh = component.Meshes[i] != null ? component.Meshes[i].sharedMesh : null;
                stamp += (mesh != null ? mesh.GetInstanceID().ToString() : "0") + ":" +
                    (mesh != null ? mesh.blendShapeCount.ToString() : "0") + ";";
            }

            if (stamp == optionStamp)
            {
                return options.ToArray();
            }

            optionStamp = stamp;
            options.Clear();
            var seen = new HashSet<string>();
            for (int i = 0; i < component.Meshes.Count; i++)
            {
                Mesh mesh = component.Meshes[i] != null ? component.Meshes[i].sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                for (int k = 0; k < mesh.blendShapeCount; k++)
                {
                    string name = mesh.GetBlendShapeName(k);
                    if (seen.Add(name))
                    {
                        options.Add(name);
                    }
                }
            }

            options.Sort(System.StringComparer.Ordinal);
            return options.ToArray();
        }

        private static int IndexOf(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
