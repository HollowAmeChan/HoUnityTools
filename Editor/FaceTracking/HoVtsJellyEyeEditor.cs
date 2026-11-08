using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoVtsJellyEye))]
    public sealed class HoVtsJellyEyeEditor : UnityEditor.Editor
    {
        private bool inputsOpen = true, movementOpen = true, scaleOpen = true, squashOpen = true;
        private string cachedIrText, irError;
        private HoFaceIrDocument document;
        private readonly List<string> inputChoices = new List<string>();
        private readonly List<string> outputChoices = new List<string>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("irController"), new GUIContent("表情 IR", "只读该组件本帧已写出的最终形态键权重。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetMeshes"), new GUIContent("驱动网格"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRun"), new GUIContent("自动运行"));
            RefreshChoices();
            EditorGUILayout.HelpBox("一个组件对应瞳孔或一层高光。输入、输出分别配置；所有方向可留空。静止后附加形变归零，保留接管前的中性权重。", MessageType.Info);

            inputsOpen = EditorGUILayout.Foldout(inputsOpen, "IR 输入形态键", true);
            if (inputsOpen)
            {
                EditorGUI.indentLevel++;
                var input = serializedObject.FindProperty("inputs");
                DrawBlinkGroups(input.FindPropertyRelative("blinkGroups"));
                DrawLegacyBlinkKeys(input.FindPropertyRelative("blinkKeys"));
                KeyField(input.FindPropertyRelative("lookLeft"), "视线向左", inputChoices);
                KeyField(input.FindPropertyRelative("lookRight"), "视线向右", inputChoices);
                KeyField(input.FindPropertyRelative("lookUp"), "视线向上", inputChoices);
                KeyField(input.FindPropertyRelative("lookDown"), "视线向下", inputChoices);
                EditorGUILayout.LabelField("缩放、压扁拉长由激励键驱动；位移可同时使用视线和激励。", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }

            DrawMovement();
            DrawScale();
            DrawSquash();
            serializedObject.ApplyModifiedProperties();

            var component = (HoVtsJellyEye)target;
            if (!string.IsNullOrEmpty(irError)) EditorGUILayout.HelpBox(irError, MessageType.Warning);
            DrawConfigurationWarnings(component);
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("状态", component.Status);
                EditorGUILayout.LabelField("输出绑定数", component.BindingCount.ToString());
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector2Field("位移附加权重", component.MovementOffset);
                    EditorGUILayout.FloatField("当前组合激励", component.BlinkInput);
                    EditorGUILayout.FloatField("缩放附加权重", component.ScaleOffset);
                    EditorGUILayout.FloatField("拉长 / 压扁附加权重", component.SquashOffset);
                }
                if (component.MissingInputKeys.Count > 0)
                    EditorGUILayout.HelpBox("输入键不存在或未绑定：" + string.Join("、", component.MissingInputKeys), MessageType.Warning);
                if (GUILayout.Button("复位弹簧")) component.ResetSprings();
                Repaint();
            }
        }

        private void DrawBlinkGroups(SerializedProperty groups)
        {
            EditorGUILayout.LabelField(new GUIContent("眨眼 / 激励组", "例如左眼一组、右眼一组。同眼不同情绪的形态键先按闭合权重相加，限制在 0～1，再在左右组之间取最大值。权重按 IR 控制点语义设置。"));
            for (int i = 0; i < groups.arraySize; i++)
            {
                var group = groups.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(60));
                group.isExpanded = EditorGUILayout.Foldout(group.isExpanded, "组 " + (i + 1), true);
                EditorGUILayout.EndVertical();
                int groupIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUILayout.PropertyField(group.FindPropertyRelative("name"), GUIContent.none);
                EditorGUI.indentLevel = groupIndent;
                if (GUILayout.Button("−", GUILayout.Width(22)))
                {
                    groups.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                if (!group.isExpanded) continue;
                EditorGUI.indentLevel++;
                var keys = group.FindPropertyRelative("keys");
                for (int keyIndex = 0; keyIndex < keys.arraySize; keyIndex++)
                {
                    var item = keys.GetArrayElementAtIndex(keyIndex);
                    EditorGUILayout.BeginHorizontal();
                    KeyField(item.FindPropertyRelative("key"), "", inputChoices);
                    int keyIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    GUILayout.Label("×", GUILayout.Width(12));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("weight"),
                        new GUIContent("", "闭合贡献权重；0 跳过。不同情绪的同阶段键使用相同权重。"),
                        GUILayout.Width(55));
                    EditorGUI.indentLevel = keyIndent;
                    if (GUILayout.Button("−", GUILayout.Width(22)))
                    {
                        keys.DeleteArrayElementAtIndex(keyIndex);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("添加 IR 键"))
                {
                    keys.InsertArrayElementAtIndex(keys.arraySize);
                    var added = keys.GetArrayElementAtIndex(keys.arraySize - 1);
                    added.FindPropertyRelative("key").stringValue = "";
                    added.FindPropertyRelative("weight").floatValue = 1;
                }
                EditorGUI.indentLevel--;
            }
            if (GUILayout.Button("添加激励组"))
            {
                int index = groups.arraySize;
                groups.InsertArrayElementAtIndex(index);
                var group = groups.GetArrayElementAtIndex(index);
                group.FindPropertyRelative("name").stringValue = index == 0 ? "左眼" : index == 1 ? "右眼" : "输入组 " + (index + 1);
                group.FindPropertyRelative("keys").ClearArray();
                group.isExpanded = true;
            }
        }

        private void DrawLegacyBlinkKeys(SerializedProperty blink)
        {
            if (blink.arraySize == 0) return;
            blink.isExpanded = EditorGUILayout.Foldout(blink.isExpanded,
                new GUIContent("旧版独立激励（兼容）", "旧预制体保持多个键取最大值。配置了有名称的分组键后只使用分组，旧列表保留以便撤回。"), true);
            if (!blink.isExpanded) return;
            for (int i = 0; i < blink.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                KeyField(blink.GetArrayElementAtIndex(i), "旧激励 " + (i + 1), inputChoices);
                if (GUILayout.Button("−", GUILayout.Width(22)))
                {
                    blink.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawMovement()
        {
            movementOpen = EditorGUILayout.Foldout(movementOpen, "上下左右移动", true);
            if (!movementOpen) return;
            EditorGUI.indentLevel++;
            var group = serializedObject.FindProperty("movement");
            KeyField(group.FindPropertyRelative("left"), "向左", outputChoices);
            KeyField(group.FindPropertyRelative("right"), "向右", outputChoices);
            KeyField(group.FindPropertyRelative("up"), "向上", outputChoices);
            KeyField(group.FindPropertyRelative("down"), "向下", outputChoices);
            var response = group.FindPropertyRelative("response");
            DrawResponse(response);
            if (response.isExpanded)
            {
                EditorGUILayout.Slider(group.FindPropertyRelative("blinkHorizontal"), -1, 1, new GUIContent("激励水平影响", "正值：激励上升时先向左，回弹向右；0 为不影响。"));
                EditorGUILayout.Slider(group.FindPropertyRelative("blinkVertical"), -1, 1, new GUIContent("激励垂直影响", "正值：激励上升时先向下，回弹向上；0 为不影响。"));
            }
            EditorGUI.indentLevel--;
        }

        private void DrawScale()
        {
            scaleOpen = EditorGUILayout.Foldout(scaleOpen, "放大缩小", true);
            if (!scaleOpen) return;
            EditorGUI.indentLevel++;
            var group = serializedObject.FindProperty("scale");
            KeyField(group.FindPropertyRelative("enlarge"), "放大", outputChoices);
            KeyField(group.FindPropertyRelative("shrink"), "缩小", outputChoices);
            DrawResponse(group.FindPropertyRelative("response"));
            EditorGUI.indentLevel--;
        }

        private void DrawSquash()
        {
            squashOpen = EditorGUILayout.Foldout(squashOpen, "压扁拉长", true);
            if (!squashOpen) return;
            EditorGUI.indentLevel++;
            var group = serializedObject.FindProperty("squash");
            KeyField(group.FindPropertyRelative("squash"), "压扁", outputChoices);
            KeyField(group.FindPropertyRelative("stretch"), "拉长", outputChoices);
            DrawResponse(group.FindPropertyRelative("response"));
            EditorGUI.indentLevel--;
        }

        private static void DrawResponse(SerializedProperty response)
        {
            EditorGUILayout.Slider(response.FindPropertyRelative("strength"), 0, 2, new GUIContent("强度", "只缩放附加形变，不改变弹簧状态；0 为关闭该类效果。"));
            response.isExpanded = EditorGUILayout.Foldout(response.isExpanded, "弹簧参数", true);
            if (!response.isExpanded) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.Slider(response.FindPropertyRelative("frequency"), 0.1f, 30, new GUIContent("响应频率（Hz）", "越高，来回弹动越快。"));
            EditorGUILayout.Slider(response.FindPropertyRelative("damping"), 0.05f, 2, new GUIContent("阻尼", "低值多次回弹；1 为临界阻尼。最小值大于 0，保证最终归中。"));
            EditorGUILayout.Slider(response.FindPropertyRelative("inputSmoothing"), 0, 0.2f, new GUIContent("输入平滑（秒）", "只抑制输入噪声。过大会削弱瞬间的果冻感。"));
            EditorGUILayout.Slider(response.FindPropertyRelative("maxAmplitude"), 0, 100, new GUIContent("最大附加权重", "限制本类形变的正负峰值；每个输出键同时使用中性值到 100 的剩余空间。"));
            EditorGUI.indentLevel--;
        }

        private void KeyField(SerializedProperty property, string label, List<string> choices)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            if (GUILayout.Button("选择", GUILayout.Width(42)))
            {
                string path = property.propertyPath, current = property.stringValue;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("（留空）"), string.IsNullOrEmpty(current), () => SetKey(path, ""));
                if (choices.Count > 0) menu.AddSeparator("");
                foreach (string choice in choices)
                {
                    string key = choice;
                    menu.AddItem(new GUIContent(key), current == key, () => SetKey(path, key));
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SetKey(string path, string key)
        {
            serializedObject.Update();
            serializedObject.FindProperty(path).stringValue = key;
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshChoices()
        {
            var source = serializedObject.FindProperty("irController").objectReferenceValue as HoVtsIrController;
            string text = source != null && source.IrAsset != null ? source.IrAsset.text : "";
            if (!string.Equals(text, cachedIrText, StringComparison.Ordinal))
            {
                cachedIrText = text; document = null; irError = null;
                if (!string.IsNullOrEmpty(text) && !HoFaceIrDocument.TryParse(text, out document, out irError))
                    irError = "IR 文件读取失败：" + irError;
            }
            inputChoices.Clear();
            if (document != null) foreach (var item in document.targets) inputChoices.Add(item.id);
            inputChoices.Sort(StringComparer.Ordinal);
            outputChoices.Clear();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var meshes = serializedObject.FindProperty("targetMeshes");
            for (int i = 0; i < meshes.arraySize; i++)
            {
                var renderer = meshes.GetArrayElementAtIndex(i).objectReferenceValue as SkinnedMeshRenderer;
                if (renderer == null || renderer.sharedMesh == null) continue;
                for (int j = 0; j < renderer.sharedMesh.blendShapeCount; j++) names.Add(renderer.sharedMesh.GetBlendShapeName(j));
            }
            outputChoices.AddRange(names); outputChoices.Sort(StringComparer.Ordinal);
        }

        private void DrawConfigurationWarnings(HoVtsJellyEye component)
        {
            var warnings = new List<string>();
            if (component.IrController == null) warnings.Add("请指定表情 IR 组件。");
            if (component.TargetMeshes.Count == 0) warnings.Add("请指定驱动网格列表。");
            var used = new HashSet<string>(StringComparer.Ordinal);
            var outputNames = new[] { component.Movement.left, component.Movement.right, component.Movement.up, component.Movement.down,
                component.Scale.enlarge, component.Scale.shrink, component.Squash.squash, component.Squash.stretch };
            foreach (string key in outputNames)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (!used.Add(key)) warnings.Add("多个方向重复使用输出键：" + key);
                if (!outputChoices.Contains(key)) warnings.Add("驱动网格中找不到输出键：" + key);
                if (document == null || !inputChoices.Contains(key) || component.IrController == null) continue;
                foreach (var renderer in component.TargetMeshes)
                    if (renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(key) >= 0
                        && component.IrController.TargetMeshes.Contains(renderer))
                    { warnings.Add("输出键与 IR 写入重叠：" + key); break; }
            }
            var inputNames = new List<string>
                { component.Inputs.lookLeft, component.Inputs.lookRight, component.Inputs.lookUp, component.Inputs.lookDown };
            if (component.Inputs.UsesBlinkGroups)
            {
                foreach (var group in component.Inputs.blinkGroups)
                {
                    if (group?.keys == null) continue;
                    var groupNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in group.keys)
                    {
                        if (item == null || string.IsNullOrEmpty(item.key)) continue;
                        if (float.IsNaN(item.weight) || float.IsInfinity(item.weight) || item.weight < 0)
                            warnings.Add("激励权重必须是非负有限数：" + item.key);
                        if (item.weight <= 0) continue;
                        if (!groupNames.Add(item.key)) warnings.Add("同一激励组内形态键重复：" + item.key);
                        inputNames.Add(item.key);
                    }
                }
            }
            else if (component.Inputs.blinkKeys != null) inputNames.AddRange(component.Inputs.blinkKeys);
            foreach (string key in inputNames)
                if (!string.IsNullOrEmpty(key) && document != null && !inputChoices.Contains(key))
                    warnings.Add("IR 未声明输入键：" + key);
            if (warnings.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);
        }
    }
}
