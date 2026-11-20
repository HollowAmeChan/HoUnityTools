#if UNITY_EDITOR
using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoBsCorrective))]
    internal sealed class HoBsCorrectiveEditor : UnityEditor.Editor
    {
        private bool showRules = true;

        public override void OnInspectorGUI()
        {
            HoBsCorrective component = (HoBsCorrective)target;
            DrawRuntimePanel(component);
            GUILayout.Space(7f);
            DrawRulesPanel(component);

            if (component.EditorPreviewActive || HoBsCorrectivePreview.IsViewPreviewing(component) || Application.isPlaying)
            {
                Repaint();
                if (component.EditorPreviewActive || HoBsCorrectivePreview.IsViewPreviewing(component)) SceneView.RepaintAll();
            }
        }

        private static void DrawRuntimePanel(HoBsCorrective component)
        {
            string status;
            Color accent;
            if (!component.enabled)
            {
                status = "已停用";
                accent = new Color(0.88f, 0.42f, 0.28f);
            }
            else if (Application.isPlaying && HoBsCorrectivePreview.IsViewPreviewing(component))
            {
                status = "运行中 · 视图预览";
                accent = HoBsCorrectiveEditorTheme.AccentDriver;
            }
            else if (Application.isPlaying)
            {
                status = "运行中";
                accent = new Color(0.20f, 0.68f, 0.57f);
            }
            else if (component.EditorPreviewActive && HoBsCorrectivePreview.IsViewPreviewing(component))
            {
                status = "权重 + 视图";
                accent = HoBsCorrectiveEditorTheme.AccentOutput;
            }
            else if (component.EditorPreviewActive)
            {
                status = "权重预览";
                accent = HoBsCorrectiveEditorTheme.AccentOutput;
            }
            else if (HoBsCorrectivePreview.IsViewPreviewing(component))
            {
                status = "视图预览";
                accent = HoBsCorrectiveEditorTheme.AccentDriver;
            }
            else
            {
                status = "待机";
                accent = new Color(0.91f, 0.65f, 0.25f);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("Ho BS 修型", status, accent);
                GUILayout.Space(5f);

                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(
                    new GUIContent("运行时启用", "禁用时释放本组件写入的全部形态键权重。"),
                    component.enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(component, "切换 Ho BS 修型");
                    component.enabled = enabled;
                    EditorUtility.SetDirty(component);
                }

                int valid = 0;
                int enabledRules = 0;
                foreach (HoBsCorrective.Rule rule in component.Rules)
                {
                    if (rule == null || !rule.enabled) continue;
                    enabledRules++;
                    if (HoBsCorrective.ValidateRule(rule, out _)) valid++;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("规则", HoBsCorrectiveEditorTheme.Label, GUILayout.Width(30f));
                    GUILayout.Label(component.Rules.Count.ToString(), HoBsCorrectiveEditorTheme.Value, GUILayout.Width(28f));
                    GUILayout.Label("启用", HoBsCorrectiveEditorTheme.Label, GUILayout.Width(32f));
                    GUILayout.Label(enabledRules.ToString(), HoBsCorrectiveEditorTheme.Value, GUILayout.Width(28f));
                    GUILayout.Label("有效", HoBsCorrectiveEditorTheme.Label, GUILayout.Width(32f));
                    GUILayout.Label(valid.ToString(), HoBsCorrectiveEditorTheme.Value, GUILayout.Width(28f));
                    GUILayout.FlexibleSpace();
                    // Weight evaluation is always part of the runtime component,
                    // just like HoAux. The user-facing switch is for SceneView.
                    using (new EditorGUI.DisabledScope(true))
                    {
                        string weightLabel = Application.isPlaying ? "权重运行中" : "仅运行时";
                        GUILayout.Button(new GUIContent(weightLabel, "BS 修型只在 Play Mode 中求值。"), GUILayout.Width(78f));
                    }
                    // HoAux-style preview is a runtime control: animation must be
                    // running before the user can inspect the live 3D state.
                    using (new EditorGUI.DisabledScope(!Application.isPlaying || EditorUtility.IsPersistent(component)))
                    {
                        bool viewPreview = HoBsCorrectivePreview.IsViewPreviewing(component);
                        EditorGUI.BeginChangeCheck();
                        viewPreview = GUILayout.Toggle(
                            viewPreview,
                            new GUIContent("视图预览", "3D 视图预览：显示全部修型骨、姿态点、影响范围和当前权重，不写入 BlendShape。"),
                            GUI.skin.button,
                            GUILayout.Width(78f));
                        if (EditorGUI.EndChangeCheck())
                            HoBsCorrectivePreview.SetViewPreview(component, viewPreview);
                    }
                }

                if (component.EditorPreviewActive && HoBsCorrectivePreview.IsViewPreviewing(component))
                {
                    EditorGUILayout.HelpBox(
                        "权重预览会写入当前场景中的 BlendShape；视图预览同时显示全部修型骨与姿态点。保存、撤销、重载或进入播放模式时自动恢复。",
                        MessageType.None);
                }
                else if (component.EditorPreviewActive)
                {
                    EditorGUILayout.HelpBox(
                        "权重预览会写入当前场景中的 BlendShape；保存、撤销、重载或进入播放模式时自动恢复。",
                        MessageType.None);
                }
                else if (HoBsCorrectivePreview.IsViewPreviewing(component))
                {
                    EditorGUILayout.HelpBox(
                        "视图预览只绘制全部修型骨、姿态点、影响范围和当前独立权重，不写入 BlendShape。",
                        MessageType.None);
                }
                else if (!Application.isPlaying)
                {
                    EditorGUILayout.LabelField(
                        "编辑态不会写入修型键。开启预览后可直接拖动骨骼并查看范围、姿态和输出权重。",
                        HoBsCorrectiveEditorTheme.LabelDim);
                }
            }
        }

        private void DrawRulesPanel(HoBsCorrective component)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("修型规则", $"{component.Rules.Count} 条 · 只读", HoBsCorrectiveEditorTheme.AccentRules);
                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("单击一行可切换 SceneView 高亮", HoBsCorrectiveEditorTheme.LabelDim);
                    GUILayout.FlexibleSpace();
                    showRules = EditorGUILayout.Foldout(showRules, "显示", true, EditorStyles.foldoutHeader);
                }

                if (showRules)
                {
                    for (int i = 0; i < component.Rules.Count; i++)
                        DrawRuleRow(component, component.Rules[i], i);
                }
            }
        }

        private static void DrawRuleRow(
            HoBsCorrective component,
            HoBsCorrective.Rule rule,
            int index)
        {
            bool selected = HoBsCorrectivePreview.SelectedRule(component) == index;
            using (new EditorGUILayout.HorizontalScope())
            {
                string state = rule != null && rule.enabled ? "●" : "○";
                GUIStyle stateStyle = rule != null && rule.enabled
                    ? HoBsCorrectiveEditorTheme.Value : HoBsCorrectiveEditorTheme.LabelDim;
                GUILayout.Label(state, stateStyle, GUILayout.Width(14f));
                string title = rule == null || string.IsNullOrEmpty(rule.bsName) ? "未命名姿态" : rule.bsName;
                GUIStyle titleStyle = selected ? HoBsCorrectiveEditorTheme.Bold : HoBsCorrectiveEditorTheme.Label;
                if (GUILayout.Button(title, titleStyle, GUILayout.Width(108f)))
                {
                    HoBsCorrectivePreview.SelectRule(component, index);
                    GUI.changed = true;
                }
                GUILayout.Label(rule == null || rule.bone == null ? "未绑定骨骼" : rule.bone.name,
                    HoBsCorrectiveEditorTheme.LabelDim, GUILayout.Width(90f));
                string target = rule == null || rule.target == null ? "未绑定网格" : rule.target.name;
                string shape = rule == null || string.IsNullOrEmpty(rule.shapeName) ? "未指定键" : rule.shapeName;
                GUILayout.Label(target + " / " + shape, HoBsCorrectiveEditorTheme.LabelDim, GUILayout.MinWidth(100f));
                GUILayout.Label(rule == null ? "—" : $"{rule.radius * Mathf.Rad2Deg:F0}° · {rule.weight:P0}",
                    HoBsCorrectiveEditorTheme.LabelDim, GUILayout.Width(72f));
                if (rule != null)
                    DrawOutputMeter(
                        rule,
                        component.EditorPreviewActive || Application.isPlaying,
                        HoBsCorrectivePreview.IsViewPreviewing(component));
            }
        }

        private static void DrawOutputMeter(HoBsCorrective.Rule rule, bool live, bool view)
        {
            Rect rect = GUILayoutUtility.GetRect(96f, 18f, GUILayout.Width(112f));
            EditorGUI.DrawRect(rect, HoBsCorrectiveEditorTheme.WellColor);
            float output = live
                ? Mathf.Clamp01(rule.lastOutput)
                : view ? Mathf.Clamp01(HoBsCorrectivePreview.PreviewWeight(rule)) : 0f;
            Color color = Color.Lerp(HoBsCorrectiveEditorTheme.AccentDriver, HoBsCorrectiveEditorTheme.AccentOutput, output);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width * output, rect.height - 2f), color);
            string text = live || view ? $"{output:P0}" : "预览";
            string tooltip = live
                ? $"当前 {rule.lastDistance * Mathf.Rad2Deg:F1}° / 半径 {rule.radius * Mathf.Rad2Deg:F1}° / 输出 {output:P0}"
                : view
                    ? $"视图预览输出 {output:P0} / 半径 {rule.radius * Mathf.Rad2Deg:F1}°"
                    : "开启预览后显示当前角度和输出权重";
            GUI.Label(rect, new GUIContent(text, tooltip), HoBsCorrectiveEditorTheme.Caption);
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
                padding = new RectOffset(12, 0, 0, 0)
            };
            GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 8, 0, 0)
            };
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 100f, rect.height), title, titleStyle);
            GUI.Label(new Rect(rect.xMax - 100f, rect.y, 94f, rect.height), status, statusStyle);
        }
    }

    [InitializeOnLoad]
    internal static class HoBsCorrectivePreview
    {
        private static readonly HashSet<HoBsCorrective> Previewing = new HashSet<HoBsCorrective>();
        private static readonly HashSet<HoBsCorrective> ViewPreviewing = new HashSet<HoBsCorrective>();
        private static readonly Dictionary<HoBsCorrective, int> Selected = new Dictionary<HoBsCorrective, int>();
        private static bool playModeDefaultsApplied;
        private static readonly Color PoseColor = new Color(0.50f, 0.72f, 1.00f, 0.95f);
        private static readonly Color CurrentColor = new Color(0.30f, 0.95f, 0.65f, 0.95f);
        private static readonly Color RangeColor = new Color(0.60f, 0.72f, 0.92f, 0.42f);

        static HoBsCorrectivePreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += (_, __) => StopAll();
            PrefabStage.prefabSaving += _ => StopAll();
            Undo.undoRedoPerformed += StopAll;
            EditorApplication.update += Tick;
            SceneView.duringSceneGui += DrawScene;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playModeDefaultsApplied = false;
                SceneView.RepaintAll();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode ||
                     state == PlayModeStateChange.EnteredEditMode)
            {
                playModeDefaultsApplied = false;
                StopAll();
            }
        }

        private static void EnableDefaultViewPreview()
        {
            foreach (HoBsCorrective component in Object.FindObjectsOfType<HoBsCorrective>(true))
            {
                if (component != null && component.isActiveAndEnabled && component.gameObject.scene.IsValid())
                    ViewPreviewing.Add(component);
            }
        }

        internal static int SelectedRule(HoBsCorrective component)
        {
            if (component == null) return -1;
            if (!Selected.TryGetValue(component, out int index)) return 0;
            return Mathf.Clamp(index, 0, Mathf.Max(0, component.Rules.Count - 1));
        }

        internal static bool IsViewPreviewing(HoBsCorrective component)
        {
            return component != null && ViewPreviewing.Contains(component);
        }

        internal static void SelectRule(HoBsCorrective component, int index)
        {
            if (component == null) return;
            Selected[component] = Mathf.Max(0, index);
            SceneView.RepaintAll();
        }

        internal static void Set(HoBsCorrective component, bool value)
        {
            if (component == null) return;
            if (value) Previewing.Add(component); else Previewing.Remove(component);
            component.SetEditorPreview(value);
            SceneView.RepaintAll();
        }

        internal static void SetViewPreview(HoBsCorrective component, bool value)
        {
            if (component == null) return;
            if (value) ViewPreviewing.Add(component); else ViewPreviewing.Remove(component);
            SceneView.RepaintAll();
        }

        private static void Tick()
        {
            Previewing.RemoveWhere(component => component == null);
            ViewPreviewing.RemoveWhere(component => component == null || !component.isActiveAndEnabled);
            if (EditorApplication.isPlaying && !playModeDefaultsApplied)
            {
                EnableDefaultViewPreview();
                playModeDefaultsApplied = true;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                foreach (HoBsCorrective component in Previewing)
                    component.EvaluateNow();
            }
            if (Previewing.Count > 0 || ViewPreviewing.Count > 0) SceneView.RepaintAll();
        }

        private static void DrawScene(SceneView sceneView)
        {
            foreach (HoBsCorrective component in ViewPreviewing)
            {
                if (component == null || !component.isActiveAndEnabled || !IsVisible(component)) continue;
                DrawComponent(component);
            }
        }

        private static bool IsVisible(HoBsCorrective component)
        {
            if (Selection.Contains(component.gameObject)) return true;
            foreach (HoBsCorrective.Rule rule in component.Rules)
                if (rule != null && rule.bone != null && Selection.Contains(rule.bone.gameObject)) return true;
            return false;
        }

        private static void DrawComponent(HoBsCorrective component)
        {
            int selected = SelectedRule(component);
            for (int i = 0; i < component.Rules.Count; i++)
            {
                HoBsCorrective.Rule rule = component.Rules[i];
                if (rule == null || rule.bone == null) continue;
                bool highlight = i == selected || Selection.Contains(rule.bone.gameObject);
                DrawRule(rule, highlight);
            }
        }

        private static void DrawRule(HoBsCorrective.Rule rule, bool highlight)
        {
            Color previousHandlesColor = Handles.color;
            Transform bone = rule.bone;
            Vector3 origin = bone.position;
            float handle = HandleUtility.GetHandleSize(origin);
            float length = handle * (highlight ? 0.72f : 0.48f);
            float alpha = !rule.enabled ? 0.10f : highlight ? 1f : 0.32f;

            Quaternion referenceWorld = bone.parent != null ? bone.parent.rotation * rule.referenceRotation : rule.referenceRotation;
            Quaternion desiredLocal = rule.referenceRotation * rule.pose;
            Quaternion desiredWorld = bone.parent != null ? bone.parent.rotation * desiredLocal : desiredLocal;
            Vector3 desired = desiredWorld * Vector3.up;
            Vector3 current = bone.up;
            Vector3 reference = referenceWorld * Vector3.up;

            float output = rule.enabled ? PreviewWeight(rule) : 0f;
            Color poseColor = Color.Lerp(PoseColor, CurrentColor, output); poseColor.a *= alpha;
            Color currentColor = CurrentColor; currentColor.a *= alpha;
            Color rangeColor = RangeColor; rangeColor.a *= alpha;
            Handles.color = rangeColor;
            Handles.DrawWireDisc(origin, reference, handle * 0.16f);
            DrawCone(origin, desired, rule.radius, length * 0.74f, rangeColor);

            Handles.color = poseColor;
            Handles.DrawDottedLine(origin, origin + desired * length, 3f);
            Handles.SphereHandleCap(0, origin + desired * length, Quaternion.identity, handle * (highlight ? 0.085f : 0.055f), EventType.Repaint);
            Handles.color = currentColor;
            Handles.DrawLine(origin, origin + current * length);
            Handles.SphereHandleCap(0, origin + current * length, Quaternion.identity, handle * (highlight ? 0.075f : 0.045f), EventType.Repaint);

            if (highlight)
            {
                string label = string.IsNullOrEmpty(rule.bsName) ? "修型姿态" : rule.bsName;
                Handles.Label(origin + desired * (length * 1.08f),
                    $"{label}  {output:P0}\n半径 {rule.radius * Mathf.Rad2Deg:F0}°",
                    LabelStyle(poseColor));
            }
            Handles.color = previousHandlesColor;
        }

        internal static float PreviewWeight(HoBsCorrective.Rule rule)
        {
            if (rule == null || rule.bone == null) return 0f;
            Quaternion relative = HoBsCorrective.ReadLocalRotation(rule.bone.localRotation, rule.referenceRotation);
            return HoBsCorrective.EvaluatePose(relative, rule.pose, rule.radius, rule.weight);
        }

        private static void DrawCone(Vector3 origin, Vector3 direction, float angle, float length, Color color)
        {
            direction.Normalize();
            Vector3 tangent = Vector3.Cross(direction, Vector3.up);
            if (tangent.sqrMagnitude < 1e-5f) tangent = Vector3.Cross(direction, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
            const int segments = 24;
            Vector3 previous = origin + ConeDirection(direction, tangent, bitangent, angle, 0f) * length;
            Handles.color = color;
            for (int i = 1; i <= segments; i++)
            {
                float phase = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = origin + ConeDirection(direction, tangent, bitangent, angle, phase) * length;
                Handles.DrawLine(previous, next);
                previous = next;
            }
        }

        private static Vector3 ConeDirection(Vector3 direction, Vector3 tangent, Vector3 bitangent, float angle, float phase)
        {
            float radians = Mathf.Clamp(angle, 0f, Mathf.PI);
            return (direction * Mathf.Cos(radians) +
                (tangent * Mathf.Cos(phase) + bitangent * Mathf.Sin(phase)) * Mathf.Sin(radians)).normalized;
        }

        private static GUIStyle LabelStyle(Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.whiteLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = color }
            };
            return style;
        }

        internal static void StopAll()
        {
            foreach (HoBsCorrective component in Previewing)
                if (component != null) component.SetEditorPreview(false);
            Previewing.Clear();
            ViewPreviewing.Clear();
            Selected.Clear();
            SceneView.RepaintAll();
        }
    }

    internal static class HoBsCorrectiveEditorTheme
    {
        public static readonly Color AccentRules = new Color(0.77f, 0.55f, 1.0f);
        public static readonly Color AccentDriver = new Color(0.36f, 0.71f, 0.96f);
        public static readonly Color AccentOutput = new Color(0.42f, 0.84f, 0.56f);
        public static readonly Color WellColor = EditorGUIUtility.isProSkin
            ? new Color(0.18f, 0.19f, 0.21f) : new Color(0.92f, 0.93f, 0.94f);

        private static GUIStyle label;
        private static GUIStyle labelDim;
        private static GUIStyle value;
        private static GUIStyle caption;
        private static GUIStyle bold;

        public static GUIStyle Label => label ??= Make(EditorStyles.label, 11, new Color(0.72f, 0.75f, 0.80f));
        public static GUIStyle LabelDim => labelDim ??= Make(EditorStyles.miniLabel, 10, new Color(0.55f, 0.58f, 0.63f));
        public static GUIStyle Value => value ??= Make(EditorStyles.label, 11, AccentOutput);
        public static GUIStyle Caption => caption ??= Make(EditorStyles.miniLabel, 10, new Color(0.70f, 0.73f, 0.78f));
        public static GUIStyle Bold => bold ??= Make(EditorStyles.boldLabel, 12, Color.white);

        private static GUIStyle Make(GUIStyle source, int size, Color color)
        {
            GUIStyle style = new GUIStyle(source) { fontSize = size };
            style.normal.textColor = color;
            style.hover.textColor = color;
            return style;
        }

    }
    internal sealed class HoBsCorrectiveSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            HoBsCorrectivePreview.StopAll();
            return paths;
        }
    }
}
#endif
