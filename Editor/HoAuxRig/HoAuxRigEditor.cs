#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>
    /// HoAuxRig 的只读状态面板。操作由 HoFBX 导入中控写入，用户只需要控制
    /// 组件是否在运行时启用；删除组件即移除整套 HoAux Rig 操作。
    /// </summary>
    [CustomEditor(typeof(HoAuxRig))]
    internal sealed class HoAuxRigEditor : UnityEditor.Editor
    {
        private bool showOperationDetails;
        private bool showDirectDetails;

        public override void OnInspectorGUI()
        {
            HoAuxRig rig = (HoAuxRig)target;

            DrawRuntimePanel(rig);
            GUILayout.Space(7f);
            DrawSourcePanel(rig);
            GUILayout.Space(7f);
            DrawLayersPanel(rig);
            GUILayout.Space(7f);
            DrawDirectPanel(rig);

            GUILayout.Space(7f);
            EditorGUILayout.HelpBox(
                "此组件由 HoFBX 导入中控维护，只在播放时执行。删除组件会移除整套 HoAux Rig 约束；添加或更新请重新走导入中控。",
                MessageType.Info);
        }

        private static void DrawRuntimePanel(HoAuxRig rig)
        {
            string status;
            Color accent;
            if (!Application.isPlaying)
            {
                status = "等待播放";
                accent = new Color(0.91f, 0.65f, 0.25f);
            }
            else if (!rig.enabled)
            {
                status = "已停用";
                accent = new Color(0.88f, 0.42f, 0.28f);
            }
            else
            {
                status = "运行中";
                accent = new Color(0.20f, 0.68f, 0.57f);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("HoAux Rig", status, accent);
                GUILayout.Space(5f);

                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("运行时启用", rig.enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(rig, "切换 HoAux Rig");
                    rig.enabled = enabled;
                    EditorUtility.SetDirty(rig);
                }

                EditorGUILayout.LabelField(
                    "执行方式",
                    rig.Mode == HoAuxRig.UpdateMode.Manual
                        ? "运行时手动调用 EvaluateNow"
                        : "运行时 LateUpdate（不在编辑态求值）");
                EditorGUILayout.LabelField("当前操作", $"{CountOperations(rig)} 条（分层 {rig.LayerOperationCount} + 通用 {rig.DirectOperationCount}）");
            }
        }

        private static void DrawSourcePanel(HoAuxRig rig)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("导入来源", "只读", new Color(0.24f, 0.54f, 0.88f));
                GUILayout.Space(5f);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Rig 根节点", rig.RigRoot, typeof(Transform), true);
                    EditorGUILayout.LabelField("Blender Armature", EmptyLabel(rig.SourceArmature));
                    EditorGUILayout.LabelField("导出时间", EmptyLabel(rig.ExportTime));
                    EditorGUILayout.LabelField("IR 版本", EmptyLabel(rig.ExporterVersion));
                }
            }
        }

        private void DrawLayersPanel(HoAuxRig rig)
        {
            List<HoAuxRig.Layer> layers = new List<HoAuxRig.Layer>();
            foreach (HoAuxRig.Layer layer in rig.Layers)
            {
                if (layer != null)
                    layers.Add(layer);
            }
            layers.Sort((left, right) => left.order.CompareTo(right.order));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("Rig 层", $"{layers.Count} 层", new Color(0.62f, 0.45f, 0.84f));
                GUILayout.Space(5f);

                if (layers.Count == 0)
                {
                    EditorGUILayout.LabelField("暂无分层操作", EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                foreach (HoAuxRig.Layer layer in layers)
                {
                    int operationCount = layer.operations == null ? 0 : layer.operations.Count;
                    string state = layer.enabled ? "启用" : "停用";
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(layer.name, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField(
                            $"{operationCount} 条 · {state}",
                            EditorStyles.miniLabel,
                            GUILayout.Width(105f));
                    }
                }

                GUILayout.Space(4f);
                showOperationDetails = EditorGUILayout.Foldout(
                    showOperationDetails,
                    "查看绑定摘要",
                    true,
                    EditorStyles.foldoutHeader);
                if (showOperationDetails)
                    DrawOperationDetails(layers);
            }
        }

        private static void DrawOperationDetails(List<HoAuxRig.Layer> layers)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (HoAuxRig.Layer layer in layers)
                {
                    if (layer.operations == null)
                        continue;
                    foreach (HoAuxRig.Operation operation in layer.operations)
                    {
                        if (operation == null)
                            continue;
                        string owner = operation.owner == null
                            ? operation.ownerPath
                            : operation.owner.name;
                        string target = operation.target == null
                            ? operation.targetPath
                            : operation.target.name;
                        EditorGUILayout.LabelField(
                            operation.type.ToString(),
                            $"{owner}  ->  {target}  ({operation.weight:0.##})");
                    }
                }
            }
        }

        private static int CountOperations(HoAuxRig rig)
        {
            return rig.OperationCount;
        }

        private static Dictionary<string, int> DirectTypeCounts(HoAuxRig rig)
        {
            var counts = new Dictionary<string, int>();
            foreach (HoAuxRig.DirectOperation operation in rig.DirectOperations)
            {
                if (operation == null) continue;
                string type = string.IsNullOrEmpty(operation.type) ? "（未记录类型）" : operation.type;
                counts.TryGetValue(type, out int count);
                counts[type] = count + 1;
            }
            return counts;
        }

        private static string DirectTypeLabel(string type)
        {
            switch (type)
            {
                case "COPY_LOCATION": return "Copy Location · 复制位置";
                case "COPY_ROTATION": return "Copy Rotation · 复制旋转";
                case "ARMATURE": return "Armature · 多源仿射混合";
                case "AIM": return "Aim · 朝向";
                default: return type;
            }
        }

        private void DrawDirectPanel(HoAuxRig rig)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("IR v3 通用约束", $"{rig.DirectOperationCount} 条", new Color(0.24f, 0.54f, 0.88f));
                GUILayout.Space(5f);
                foreach (var pair in DirectTypeCounts(rig))
                    EditorGUILayout.LabelField(DirectTypeLabel(pair.Key), $"{pair.Value} 条");
                if (rig.DirectOperationCount == 0)
                    EditorGUILayout.LabelField("暂无通用操作", EditorStyles.centeredGreyMiniLabel);
                else
                {
                    showDirectDetails = EditorGUILayout.Foldout(showDirectDetails, "查看通用约束绑定", true, EditorStyles.foldoutHeader);
                    if (showDirectDetails)
                        foreach (HoAuxRig.DirectOperation operation in rig.DirectOperations)
                            if (operation != null) DrawDirectOperation(operation);
                }
                if (!string.IsNullOrEmpty(rig.LastDirectError))
                    EditorGUILayout.HelpBox(rig.LastDirectError, MessageType.Warning);
            }
        }

        private static void DrawDirectOperation(HoAuxRig.DirectOperation operation)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(DirectTypeLabel(operation.type), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("原始栈序 / 影响度", $"{operation.stackIndex} / {operation.weight:0.###}");
                DrawReference("Owner · 被驱动", operation.owner, operation.ownerPath);
                if (operation.type == "ARMATURE")
                {
                    EditorGUILayout.LabelField("输出", operation.positionOnly ? "仅位置" : "仿射混合（非 TRS 写回有诊断）");
                    foreach (HoAuxRig.DirectSource source in operation.sources)
                    {
                        if (source == null) continue;
                        DrawReference($"来源 · {source.weight:P1}", source.bone, source.path);
                    }
                }
                else
                {
                    DrawReference("Target · 来源", operation.target, operation.targetPath);
                    if (operation.type == "AIM")
                    {
                        DrawReference("Up · 上方向", operation.up, operation.upPath);
                        EditorGUILayout.Vector3Field("Aim 轴", operation.aimAxis);
                        EditorGUILayout.Vector3Field("Up 轴", operation.upAxis);
                    }
                }
            }
        }

        private static void DrawReference(string label, Transform value, string path)
        {
            EditorGUILayout.ObjectField(label, value, typeof(Transform), true);
            if (value == null && !string.IsNullOrEmpty(path))
                EditorGUILayout.LabelField("保存路径", path, EditorStyles.miniLabel);
        }

        private static string EmptyLabel(string value)
        {
            return string.IsNullOrEmpty(value) ? "（未记录）" : value;
        }

        private static void DrawPanelHeader(string title, string status, Color accent)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 27f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.82f, 0.83f, 0.85f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 0, 0, 0),
            };
            GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 8, 0, 0),
            };
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 100f, rect.height), title, titleStyle);
            GUI.Label(new Rect(rect.xMax - 100f, rect.y, 94f, rect.height), status, statusStyle);
        }
    }
}
#endif
