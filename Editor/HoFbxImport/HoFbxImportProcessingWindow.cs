#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.BoneRendering;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>
    /// Explicit FBX metadata application entry point. Importers never mutate
    /// user assets implicitly; this window applies the selected pieces only
    /// after confirmation.
    /// </summary>
    internal sealed class HoFbxImportProcessingWindow : EditorWindow
    {
        private enum ToolPage
        {
            FbxProcessing,
            ConstraintUtilities,
        }

        private const string ManifestSuffix = "_unity.json";
        private const string MetadataDirectoryName = "HoFBX";
        private const string HumanoidKind = "humanoid";
        private const string ConstraintKind = "rigConstraintIR";
        private const string CollectionKind = "collections";
        private const string ConstraintSchema = "hotools.rig-constraint-ir";
        private const int ConstraintSchemaVersion = 2;

        [Serializable]
        private sealed class MetadataManifest
        {
            public MetadataEntry[] files;
        }

        [Serializable]
        private sealed class MetadataEntry
        {
            public string kind;
            public string file;
        }

        [Serializable]
        private sealed class HumanoidMappingFile
        {
            public HumanoidArmature[] armatures;
        }

        [Serializable]
        private sealed class HumanoidArmature
        {
            public string armatureName;
            public HumanoidBone[] bones;
        }

        [Serializable]
        private sealed class HumanoidBone
        {
            public string boneName;
            public string humanName;
        }

        private enum ConstraintPlanKind
        {
            Parent,
            Twist,
            Fan,
            Unknown,
        }

        /// <summary>
        /// 约束导入的落盘形态。三种形态的"可管理性"差别很大，UI 上必须说清楚：
        /// 内置约束散在被驱动的每根骨上，另两种各自集中在一处。
        /// </summary>
        private enum ConstraintMode
        {
            /// <summary>Unity 内置约束，挂在被驱动的骨骼上（会散在各处）。</summary>
            Standard,

            /// <summary>单一 HoAuxRig 中控组件，挂在约束根物体上。</summary>
            HoAux,

            /// <summary>每条约束一个独立空物体（挂点），全部摊在约束根物体底下。</summary>
            Vrc,
        }

        private const string ConstraintRootFieldTooltip =
            "可选。VRC 与 HoAux 两种模式共用的根物体：\n" +
            "· HoAux：HoAuxRig 中控组件直接挂到这个物体上。\n" +
            "· VRC：每条约束在这个物体底下占一个独立空物体，约束挂在那个空物体上\n" +
            "  （空物体只是挂点，真正被驱动的是 TargetTransform 指向的那根骨骼）。\n" +
            "留空 = 直接用目标骨架自己（也就是预制件底下），不额外建容器。\n" +
            "建议填一个预制件底下的空物体：根物体在哪儿，约束就跟着在哪儿，便于整体搬运与排查。\n" +
            "Unity 内置模式不使用这个字段。";

        private const string ConstraintModeTooltip =
            "决定约束最终以什么形态落盘：\n" +
            "· Unity 内置约束 —— 约束组件挂在被驱动的每根骨骼上，会散在整个骨架里。\n" +
            "· HoAux Rig —— Parent / Twist / Fan 收进一个 HoAuxRig 中控组件，挂在约束根物体上。\n" +
            "· VRC 约束 —— 每条约束一个独立空物体、全部摊在约束根物体底下；空物体只是挂点，\n" +
            "  约束的 TargetTransform 指回被驱动的那根骨骼。需要工程里装了 VRChat SDK。\n" +
            "换形态会清掉上一次由本工具导入的其它形态，手工创建的约束不受影响。\n" +
            "\n" +
            "求值时机：HoAuxRig 只在播放模式求值；VRC 约束默认编辑模式也会求值，\n" +
            "可在 VRChat SDK 控制面板关掉「Execute VRChat Constraints in Edit Mode」。";

        private const string ClearScopeTooltip =
            "「安全清除导入约束」只删本工具导入的东西：内置约束 + 逐骨导入标记\n" +
            "+ 导入过的 HoAuxRig + VRC 空物体。\n" +
            "作用范围 = 目标骨架，外加约束根物体（及其所在预制件）那一侧；\n" +
            "若目标骨架上的标记都属于同一份导出，则只清那一份。\n" +
            "「清除全部标准约束」只扫目标骨架层级，只认 Unity 内置约束（含你手工加的）；\n" +
            "HoAux 与 VRC 不受影响。\n" +
            "这两个按钮是为「内置约束」准备的 —— 那种形态散在被驱动的每根骨上；\n" +
            "HoAux 是一整个组件、VRC 是一整批空物体，各自集中在一处，选中直接删更省事。";

        private const string SafeClearTooltip =
            "只删本工具导入的东西：内置约束 + 逐骨的导入标记 + 导入过的 HoAuxRig + VRC 空物体。\n" +
            "专门为「Unity 内置约束」准备 —— 那种形态每条约束都散在被驱动的骨骼上，\n" +
            "没有标记就分不清哪条是导入的、哪条是你手搓的。\n" +
            "HoAux 是一整个组件、VRC 是一整批空物体，都在同一处，直接选中删掉即可，不需要这个按钮。";

        private const string ClearStandardTooltip =
            "只认 Unity 内置约束（Rotation / Position / Scale / Parent），\n" +
            "包括你自己手工加的那些，会一并删除。\n" +
            "HoAux 组件与 VRC 空物体不受影响 —— 那两种各自集中在一处，直接选中删掉就行。";

        private const string ResetPoseTooltip =
            "把骨架还原成 Prefab 里的姿态。\n" +
            "删掉 VRC 约束之后骨骼不会自己还原（VRC 约束停用/删除只是停止求值），\n" +
            "必要时用这个按钮收尾。\n" +
            "HoAuxRig 不需要它：播放模式下禁用或删除组件本身就会把骨骼还原到绑定姿态。";

        private sealed class ConstraintPlanEntry
        {
            public ConstraintPlanKind kind;
            public string ownerBone;
            public string targetBone;
            public string sourceBone;
            public string reason;
            public string hoAuxUnsupportedReason;
            public float weight = 1.0f;
            public bool maintainOffset = true;
            public BlenderConstraintInfo copyRotation;
            public BlenderConstraintInfo stretchTo;
            public BlenderConstraintInfo rawConstraint;
        }

        [SerializeField] private GameObject fbxAsset;
        [SerializeField] private string fbxAssetPath;
        [SerializeField] private TextAsset humanoidJson;
        [SerializeField] private TextAsset constraintJson;
        [SerializeField] private List<TextAsset> constraintJsonCandidates = new List<TextAsset>();
        [SerializeField] private TextAsset collectionJson;
        [SerializeField] private GameObject targetRig;
        [SerializeField] private bool applyHumanoid = true;
        [SerializeField] private bool applyConstraints;
        [SerializeField] private bool applyCollections;

        [SerializeField] private ConstraintMode constraintMode = ConstraintMode.Standard;

        /// <summary>
        /// 旧字段（`使用 HoAux Rig` 复选）。只在迁移时读一次，之后恒为 false。
        /// 保留它是为了让老会话里选过 HoAux 的用户不被静默降级成 Standard。
        /// </summary>
        [SerializeField] private bool legacyUseHoAuxRig;

        [SerializeField] private bool constraintModeMigrated;

        /// <summary>约束根物体：HoAux 挂组件的地方，VRC 摊空物体的地方。留空 = 目标骨架自己。</summary>
        [SerializeField] private Transform constraintRoot;

        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private bool mappingPreviewExpanded;
        [SerializeField] private bool constraintPreviewExpanded = true;
        [SerializeField] private ToolPage currentPage;

        private GUIStyle panelTitleStyle;
        private GUIStyle panelStatusStyle;
        private GUIStyle primaryButtonStyle;
        private bool stylesBuiltForProSkin;

        // Unity's Inspector Activate button calls the internal
        // IConstraintInternal.ActivateAndPreserveOffset method. Cache the
        // explicit interface implementation so imported constraints behave
        // exactly like constraints activated from the Inspector.
        private static readonly Dictionary<Type, MethodInfo> ActivateConstraintMethods =
            new Dictionary<Type, MethodInfo>();
        private static readonly HashSet<Type> MissingActivateConstraintMethods =
            new HashSet<Type>();

        [MenuItem("HoUnityTools/HoFBX导入处理", false, 20)]
        internal static void ShowWindow()
        {
            var window = GetWindow<HoFbxImportProcessingWindow>("HoFBX导入处理");
            window.minSize = new Vector2(560f, 500f);
            window.Show();
        }

        internal static void ShowConstraintUtilities()
        {
            ShowWindow();
            GetWindow<HoFbxImportProcessingWindow>().currentPage = ToolPage.ConstraintUtilities;
        }

        [MenuItem("Assets/HoUnityTools/HoFBX导入处理", false, 2000)]
        private static void OpenFromSelection()
        {
            string path = Selection.activeObject == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!IsFbx(path))
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "请先选择一个 FBX 资产。", "确定");
                return;
            }

            ShowWindow();
            var window = GetWindow<HoFbxImportProcessingWindow>();
            window.currentPage = ToolPage.FbxProcessing;
            GameObject selectedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (selectedAsset != null)
                window.SetFbxAsset(selectedAsset, true);
            else
            {
                window.fbxAsset = null;
                window.fbxAssetPath = path;
                window.ScanAdjacentMetadata();
            }
        }

        [MenuItem("Assets/HoUnityTools/HoFBX导入处理", true)]
        private static bool ValidateOpenFromSelection()
        {
            return IsFbx(Selection.activeObject == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private static bool IsFbx(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private void OnEnable()
        {
            MigrateLegacyConstraintMode();
            if (fbxAsset == null && IsFbx(fbxAssetPath))
                fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
        }

        /// <summary>
        /// 一次性把旧的「使用 HoAux Rig」勾选迁移到三形态枚举。
        /// 不做迁移的话，老会话里选了 HoAux 的用户重开窗口会静默变回 Standard。
        /// </summary>
        private void MigrateLegacyConstraintMode()
        {
            if (constraintModeMigrated)
                return;
            constraintModeMigrated = true;

            if (legacyUseHoAuxRig && constraintMode == ConstraintMode.Standard)
                constraintMode = ConstraintMode.HoAux;

            legacyUseHoAuxRig = false;
        }

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 118f;
            EnsureStyles();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                currentPage = (ToolPage)GUILayout.Toolbar(
                    (int)currentPage,
                    new[] { "FBX 处理", "约束工具" },
                    EditorStyles.toolbarButton,
                    GUILayout.Width(280f));
                GUILayout.FlexibleSpace();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10f);
                using (new EditorGUILayout.VerticalScope())
                {
                    if (currentPage == ToolPage.FbxProcessing)
                        DrawFbxProcessingPage();
                    else
                        DrawConstraintUtilitiesPage();
                }
                GUILayout.Space(10f);
            }
            GUILayout.Space(10f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawFbxProcessingPage()
        {
            DrawAssetPanel();
            GUILayout.Space(8f);
            DrawMappingPanel();
            GUILayout.Space(8f);
            DrawTargetPanel();
            GUILayout.Space(8f);
            DrawOptionsPanel();
            GUILayout.Space(14f);

            if (GUILayout.Button("应用配置", primaryButtonStyle))
                ApplySelectedMapping();
        }

        private void DrawAssetPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("FBX 资产", IsFbx(fbxAssetPath) ? "已选择" : "未选择", new Color(0.24f, 0.54f, 0.88f));
                GUILayout.Space(5f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    GameObject selectedAsset = (GameObject)EditorGUILayout.ObjectField(
                        "FBX", fbxAsset, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck())
                        SetFbxAsset(selectedAsset, true);

                    GUIContent refreshContent = EditorGUIUtility.IconContent("Refresh");
                    refreshContent.tooltip = "重新扫描邻接 JSON";
                    if (GUILayout.Button(refreshContent, GUILayout.Width(30f), GUILayout.Height(19f)))
                        ScanAdjacentMetadata();
                }
                GUILayout.Space(2f);
            }
        }

        private void SetFbxAsset(GameObject asset, bool scanMetadata)
        {
            string path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (!IsFbx(path))
            {
                fbxAsset = null;
                fbxAssetPath = string.Empty;
                ClearScannedMetadata();
                return;
            }

            fbxAsset = asset;
            fbxAssetPath = path;
            if (scanMetadata)
                ScanAdjacentMetadata();
        }

        private void ClearScannedMetadata()
        {
            humanoidJson = null;
            constraintJson = null;
            if (constraintJsonCandidates == null)
                constraintJsonCandidates = new List<TextAsset>();
            else
                constraintJsonCandidates.Clear();
            collectionJson = null;
        }

        private void DrawMappingPanel()
        {
            int foundCount = (humanoidJson != null ? 1 : 0) +
                             (constraintJson != null ? 1 : 0) +
                             (collectionJson != null ? 1 : 0);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("导入数据", $"已找到 {foundCount}/3", new Color(0.20f, 0.68f, 0.57f));
                GUILayout.Space(5f);
                humanoidJson = (TextAsset)EditorGUILayout.ObjectField(
                    "Humanoid 映射", humanoidJson, typeof(TextAsset), false);
                constraintJson = (TextAsset)EditorGUILayout.ObjectField(
                    "约束 JSON", constraintJson, typeof(TextAsset), false);
                DrawConstraintCandidatePopup();
                collectionJson = (TextAsset)EditorGUILayout.ObjectField(
                    "骨骼集合", collectionJson, typeof(TextAsset), false);

                DrawMappingPreview();
                GUILayout.Space(2f);
            }
        }

        private void DrawMappingPreview()
        {
            GUILayout.Space(4f);
            HumanBone[] previewBones;
            int mappedCount;
            string error;
            bool previewValid = TryBuildHumanBonePreview(
                humanoidJson, out previewBones, out mappedCount, out error);
            string summary = humanoidJson == null
                ? "最终 Humanoid 映射预览"
                : $"最终 Humanoid 映射预览  {mappedCount}/{HumanTrait.BoneCount}";

            mappingPreviewExpanded = EditorGUILayout.Foldout(
                mappingPreviewExpanded, summary, true, EditorStyles.foldoutHeader);
            if (!mappingPreviewExpanded)
                return;

            if (humanoidJson == null)
            {
                EditorGUILayout.LabelField("未选择 Humanoid 映射 JSON。", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (!previewValid)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Humanoid 槽位", EditorStyles.miniBoldLabel, GUILayout.Width(190f));
                GUILayout.Label("最终骨骼", EditorStyles.miniBoldLabel);
            }

            for (int i = 0; i < previewBones.Length; i++)
                DrawMappingPreviewRow(previewBones[i], i);
        }

        private static void DrawMappingPreviewRow(HumanBone humanBone, int index)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 19f, GUILayout.ExpandWidth(true));
            if ((index & 1) != 0)
            {
                Color stripe = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f)
                    : new Color(0f, 0f, 0f, 0.035f);
                EditorGUI.DrawRect(row, stripe);
            }

            Rect humanRect = new Rect(row.x + 5f, row.y, 185f, row.height);
            Rect boneRect = new Rect(row.x + 195f, row.y, row.width - 200f, row.height);
            GUI.Label(humanRect, humanBone.humanName, EditorStyles.miniLabel);

            bool isMapped = !string.IsNullOrEmpty(humanBone.boneName);
            Color previousColor = GUI.color;
            GUI.color = isMapped
                ? (EditorGUIUtility.isProSkin ? new Color(0.55f, 0.92f, 0.67f) : new Color(0.08f, 0.46f, 0.20f))
                : (EditorGUIUtility.isProSkin ? new Color(0.62f, 0.64f, 0.68f) : new Color(0.42f, 0.44f, 0.47f));
            GUI.Label(boneRect, isMapped ? humanBone.boneName : "（置空）", EditorStyles.miniLabel);
            GUI.color = previousColor;
        }

        private void DrawConstraintImportPreview()
        {
            ConstraintConfig config = null;
            string error = string.Empty;
            List<ConstraintPlanEntry> plan = new List<ConstraintPlanEntry>();
            if (constraintJson != null)
            {
                try
                {
                    config = JsonUtility.FromJson<ConstraintConfig>(constraintJson.text);
                    if (config != null && TryValidateConstraintConfig(config, out error))
                        plan = BuildConstraintImportPlan(config);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
            }

            string mode = ConstraintModeLabel(constraintMode);
            constraintPreviewExpanded = EditorGUILayout.Foldout(
                constraintPreviewExpanded,
                $"约束导入预览  {plan.Count} 项 / {mode}",
                true,
                EditorStyles.foldoutHeader);
            if (!constraintPreviewExpanded)
                return;

            if (constraintJson == null)
            {
                EditorGUILayout.LabelField("未选择约束 JSON。", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (!string.IsNullOrEmpty(error) || config == null)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(error) ? "约束 JSON 结构无效。" : error,
                    MessageType.Error);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Owner -> Target", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                GUILayout.Label("最终导入结果", EditorStyles.miniBoldLabel);
            }

            Dictionary<string, Transform> previewTransforms = targetRig == null
                ? null
                : BuildTransformMap(targetRig.transform);
            for (int rowIndex = 0; rowIndex < plan.Count; rowIndex++)
            {
                ConstraintPlanEntry entry = plan[rowIndex];
                string description = DescribeConstraintImport(entry, constraintMode);
                if (entry.kind != ConstraintPlanKind.Unknown &&
                    !(constraintMode == ConstraintMode.HoAux &&
                      !string.IsNullOrEmpty(entry.hoAuxUnsupportedReason)) &&
                    previewTransforms != null)
                {
                    if (!TryResolveTransform(previewTransforms, entry.ownerBone, out Transform owner) ||
                        !TryResolveTransform(previewTransforms, entry.targetBone, out _))
                    {
                        description = "跳过：目标骨架缺少或无法唯一定位 Owner/Target";
                    }
                    else if (constraintMode == ConstraintMode.Standard &&
                             HasUnmanagedStandardConstraint(owner, entry.kind))
                    {
                        description = "跳过：Owner 已有用户创建的同类型标准约束";
                    }
                }
                DrawConstraintPreviewRow(
                    entry.ownerBone,
                    entry.targetBone,
                    description,
                    rowIndex);
            }
        }

        private void DrawConstraintCandidatePopup()
        {
            if (constraintJsonCandidates == null || constraintJsonCandidates.Count <= 1)
                return;

            string[] labels = new string[constraintJsonCandidates.Count];
            for (int index = 0; index < constraintJsonCandidates.Count; index++)
                labels[index] = ConstraintCandidateLabel(constraintJsonCandidates[index]);

            int current = constraintJsonCandidates.IndexOf(constraintJson);
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("约束骨架", Mathf.Max(0, current), labels);
            if (EditorGUI.EndChangeCheck())
                constraintJson = constraintJsonCandidates[selected];
        }

        private static string ConstraintCandidateLabel(TextAsset asset)
        {
            if (asset == null)
                return "（缺失）";
            try
            {
                ConstraintConfig config = JsonUtility.FromJson<ConstraintConfig>(asset.text);
                if (config != null && !string.IsNullOrEmpty(config.armatureName))
                    return config.armatureName + "  /  " + asset.name;
            }
            catch (Exception)
            {
                // The selected file's preview reports detailed validation errors.
            }
            return asset.name;
        }

        private static void DrawConstraintPreviewRow(
            string owner,
            string target,
            string result,
            int index)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 19f, GUILayout.ExpandWidth(true));
            if ((index & 1) != 0)
            {
                Color stripe = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f)
                    : new Color(0f, 0f, 0f, 0.035f);
                EditorGUI.DrawRect(row, stripe);
            }

            Rect relationRect = new Rect(row.x + 5f, row.y, 215f, row.height);
            Rect resultRect = new Rect(row.x + 225f, row.y, row.width - 230f, row.height);
            GUI.Label(relationRect, $"{owner} -> {target}", EditorStyles.miniLabel);
            GUI.Label(resultRect, new GUIContent(result, result), EditorStyles.miniLabel);
        }

        private static string DescribeConstraintImport(ConstraintPlanEntry entry, ConstraintMode mode)
        {
            if (entry.kind == ConstraintPlanKind.Unknown)
            {
                string type = entry.rawConstraint == null
                    ? "未知类型"
                    : entry.rawConstraint.constraintType;
                return $"未知约束 / 跳过  {type}  {entry.reason}";
            }

            if (mode == ConstraintMode.HoAux)
            {
                if (!string.IsNullOrEmpty(entry.hoAuxUnsupportedReason))
                    return "HoAuxRig / 跳过  " + entry.hoAuxUnsupportedReason;
                if (entry.kind == ConstraintPlanKind.Parent) return "HoAuxRig / Parent";
                if (entry.kind == ConstraintPlanKind.Twist)
                {
                    string stretch = entry.stretchTo == null
                        ? "缺少 Stretch To"
                        : "S:" + ConstraintSpacePair(entry.stretchTo);
                    return $"HoAuxRig / Twist  R:{ConstraintSpacePair(entry.copyRotation)}  {stretch}";
                }
                if (entry.kind == ConstraintPlanKind.Fan)
                    return $"HoAuxRig / Fan  R:{ConstraintSpacePair(entry.copyRotation)}";
            }
            else if (mode == ConstraintMode.Vrc)
            {
                // VRC 约束与内置约束一样没有中间语义，只有退化后的标准行为。
                // 每条约束一个独立空物体当挂点，TargetTransform 指回被驱动的骨骼。
                if (entry.kind == ConstraintPlanKind.Parent)
                    return "VRCParentConstraint / 独立空物体挂点";
                if (entry.kind == ConstraintPlanKind.Twist)
                    return $"VRCRotationConstraint（仅 Y）/ 独立空物体挂点  原始:{ConstraintSpacePair(entry.copyRotation)}";
                if (entry.kind == ConstraintPlanKind.Fan)
                    return $"VRCRotationConstraint（XYZ）/ 独立空物体挂点  原始:{ConstraintSpacePair(entry.copyRotation)}";
            }
            else
            {
                if (entry.kind == ConstraintPlanKind.Parent) return "ParentConstraint";
                if (entry.kind == ConstraintPlanKind.Twist)
                    return $"RotationConstraint（仅 Y）  原始:{ConstraintSpacePair(entry.copyRotation)}";
                if (entry.kind == ConstraintPlanKind.Fan)
                    return $"RotationConstraint（XYZ）  原始:{ConstraintSpacePair(entry.copyRotation)}";
            }

            return "不支持";
        }

        private static string ConstraintSpacePair(BlenderConstraintInfo constraint)
        {
            BlenderConstraintParameters parameters = constraint == null
                ? null
                : constraint.parameters;
            if (parameters == null)
                return "未记录";
            string owner = string.IsNullOrEmpty(parameters.owner_space)
                ? "WORLD"
                : parameters.owner_space;
            string target = string.IsNullOrEmpty(parameters.target_space)
                ? "WORLD"
                : parameters.target_space;
            return owner + "->" + target;
        }

        private static List<ConstraintPlanEntry> BuildConstraintImportPlan(ConstraintConfig config)
        {
            var result = new List<ConstraintPlanEntry>();
            BuildNeutralIrPlan(config, result);
            return result;
        }

        private static bool TryValidateConstraintConfig(
            ConstraintConfig config,
            out string error)
        {
            if (config == null)
            {
                error = "约束 JSON 为空。";
                return false;
            }
            if (!string.Equals(config.schema, ConstraintSchema, StringComparison.Ordinal))
            {
                error = $"不支持的约束 schema：{config.schema ?? "<空>"}。";
                return false;
            }
            if (config.schemaVersion != ConstraintSchemaVersion)
            {
                error = $"不支持的约束 IR 版本：{config.schemaVersion}，当前要求 {ConstraintSchemaVersion}。";
                return false;
            }
            if (string.IsNullOrEmpty(config.armatureName))
            {
                error = "约束 IR 缺少 armatureName。";
                return false;
            }
            if (config.mchEnabledBones == null || config.mchBindings == null || config.auxBones == null ||
                config.knownConstraints == null || config.unknownConstraints == null)
            {
                error = "约束 IR 缺少 v2 必需的关系或分类列表。";
                return false;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var knownByKey = new Dictionary<string, KnownConstraintInfo>(StringComparer.Ordinal);
            foreach (KnownConstraintInfo known in config.knownConstraints)
            {
                if (known == null || known.constraint == null || string.IsNullOrEmpty(known.ownerBone) ||
                    string.IsNullOrEmpty(known.relationType))
                {
                    error = "knownConstraints 中存在不完整记录。";
                    return false;
                }
                if (!string.Equals(known.relationType, "MCH_BINDING", StringComparison.Ordinal) &&
                    !string.Equals(known.relationType, "AUX_CONSTRAINT", StringComparison.Ordinal))
                {
                    error = $"knownConstraints 中存在未知 relationType：{known.relationType ?? "<空>"}。";
                    return false;
                }
                string key = ConstraintRecordKey(known.ownerBone, known.constraint);
                if (!keys.Add(key))
                {
                    error = $"约束分类重复：{known.ownerBone} / {known.constraint.stackIndex}。";
                    return false;
                }
                knownByKey.Add(key, known);
            }
            foreach (UnknownConstraintInfo unknown in config.unknownConstraints)
            {
                if (unknown == null || unknown.constraint == null || string.IsNullOrEmpty(unknown.ownerBone))
                {
                    error = "unknownConstraints 中存在不完整记录。";
                    return false;
                }
                if (!keys.Add(ConstraintRecordKey(unknown.ownerBone, unknown.constraint)))
                {
                    error = $"约束分类重复：{unknown.ownerBone} / {unknown.constraint.stackIndex}。";
                    return false;
                }
            }

            var auxByBone = new Dictionary<string, AuxBoneInfo>(StringComparer.Ordinal);
            foreach (AuxBoneInfo aux in config.auxBones)
            {
                if (aux == null || string.IsNullOrEmpty(aux.boneName) ||
                    string.IsNullOrEmpty(aux.auxType) || aux.sourceBones == null ||
                    aux.constraintNames == null || aux.involvedBones == null ||
                    aux.constraints == null)
                {
                    error = "auxBones 中存在不完整记录。";
                    return false;
                }
                if (auxByBone.ContainsKey(aux.boneName))
                {
                    error = $"auxBones 中存在重复骨骼：{aux.boneName}。";
                    return false;
                }
                auxByBone.Add(aux.boneName, aux);

                var registeredNames = new HashSet<string>(aux.constraintNames, StringComparer.Ordinal);
                if (registeredNames.Count != aux.constraintNames.Count)
                {
                    error = $"骨骼 {aux.boneName} 的 constraintNames 重复。";
                    return false;
                }
                if (string.Equals(aux.auxType, "MCH", StringComparison.OrdinalIgnoreCase) &&
                    aux.constraints.Count != 0)
                {
                    error = $"MCH 骨 {aux.boneName} 不能通过普通 Aux constraints 认领约束。";
                    return false;
                }

                var auxConstraintKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (BlenderConstraintInfo constraint in aux.constraints)
                {
                    if (constraint == null || string.IsNullOrEmpty(constraint.name))
                    {
                        error = $"Aux 骨 {aux.boneName} 中存在不完整约束。";
                        return false;
                    }
                    if (!registeredNames.Contains(constraint.name))
                    {
                        error = $"Aux 骨 {aux.boneName} 的约束未登记到 constraintNames：{constraint.name}。";
                        return false;
                    }
                    string key = ConstraintRecordKey(aux.boneName, constraint);
                    if (!auxConstraintKeys.Add(key))
                    {
                        error = $"Aux 骨 {aux.boneName} 的约束栈索引重复：{constraint.stackIndex}。";
                        return false;
                    }
                    if (!knownByKey.TryGetValue(key, out KnownConstraintInfo known) ||
                        !string.Equals(known.relationType, "AUX_CONSTRAINT", StringComparison.Ordinal) ||
                        !string.Equals(known.auxBone, aux.boneName, StringComparison.Ordinal) ||
                        !string.Equals(known.auxType, aux.auxType, StringComparison.Ordinal) ||
                        !SameConstraintIdentity(known.constraint, constraint))
                    {
                        error = $"Aux 骨 {aux.boneName} 的约束未与 knownConstraints 汇总一致：{constraint.name}。";
                        return false;
                    }
                }
            }

            var mchConstraintKeys = new HashSet<string>(StringComparer.Ordinal);
            var enabledMchBones = new HashSet<string>(
                config.mchEnabledBones ?? new List<string>(),
                StringComparer.Ordinal);
            foreach (MchBindingInfo binding in config.mchBindings)
            {
                if (binding == null || binding.constraint == null ||
                    string.IsNullOrEmpty(binding.mchBone) || string.IsNullOrEmpty(binding.sourceBone))
                {
                    error = "mchBindings 中存在不完整记录。";
                    return false;
                }
                if (!enabledMchBones.Contains(binding.sourceBone) ||
                    !string.Equals(binding.constraint.constraintType, "CHILD_OF", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(binding.constraint.targetObjectName, config.armatureName, StringComparison.Ordinal) ||
                    !string.Equals(binding.constraint.targetBoneName, binding.sourceBone, StringComparison.Ordinal))
                {
                    error = $"MCH 绑定不满足严格 source/CHILD_OF 签名：{binding.mchBone}。";
                    return false;
                }
                if (!auxByBone.TryGetValue(binding.mchBone, out AuxBoneInfo mchAux) ||
                    !string.Equals(mchAux.auxType, "MCH", StringComparison.OrdinalIgnoreCase) ||
                    mchAux.sourceBones == null || !mchAux.sourceBones.Contains(binding.sourceBone) ||
                    mchAux.constraintNames == null || !mchAux.constraintNames.Contains(binding.constraint.name))
                {
                    error = $"MCH 绑定没有对应的显式 MCH Aux 元数据：{binding.mchBone}。";
                    return false;
                }
                string key = ConstraintRecordKey(binding.mchBone, binding.constraint);
                if (!mchConstraintKeys.Add(key))
                {
                    error = $"mchBindings 中存在重复约束：{binding.mchBone} / {binding.constraint.stackIndex}。";
                    return false;
                }
                if (!knownByKey.TryGetValue(key, out KnownConstraintInfo known) ||
                    !string.Equals(known.relationType, "MCH_BINDING", StringComparison.Ordinal) ||
                    !string.Equals(known.auxBone, binding.mchBone, StringComparison.Ordinal) ||
                    !string.Equals(known.auxType, "MCH", StringComparison.Ordinal) ||
                    !SameConstraintIdentity(known.constraint, binding.constraint))
                {
                    error = $"MCH 绑定未与 knownConstraints 汇总一致：{binding.mchBone}。";
                    return false;
                }
            }

            foreach (KnownConstraintInfo known in config.knownConstraints)
            {
                string key = ConstraintRecordKey(known.ownerBone, known.constraint);
                bool inMch = mchConstraintKeys.Contains(key);
                bool inAux = false;
                if (known.relationType == "AUX_CONSTRAINT" &&
                    auxByBone.TryGetValue(known.ownerBone, out AuxBoneInfo aux))
                {
                    foreach (BlenderConstraintInfo constraint in aux.constraints)
                    {
                        if (ConstraintRecordKey(aux.boneName, constraint) == key)
                        {
                            inAux = true;
                            break;
                        }
                    }
                }
                if (inMch == inAux)
                {
                    error = $"knownConstraints 无法唯一对应关系：{known.ownerBone} / {known.constraint.stackIndex}。";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static bool SameConstraintIdentity(
            BlenderConstraintInfo first,
            BlenderConstraintInfo second)
        {
            return first != null && second != null &&
                first.stackIndex == second.stackIndex &&
                string.Equals(first.name, second.name, StringComparison.Ordinal) &&
                string.Equals(first.constraintType, second.constraintType, StringComparison.Ordinal) &&
                string.Equals(first.targetObjectName, second.targetObjectName, StringComparison.Ordinal) &&
                string.Equals(first.targetBoneName, second.targetBoneName, StringComparison.Ordinal);
        }

        private static void BuildNeutralIrPlan(
            ConstraintConfig config,
            List<ConstraintPlanEntry> result)
        {
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (MchBindingInfo binding in config.mchBindings)
            {
                if (binding == null || binding.constraint == null ||
                    ConstraintMuted(binding.constraint) ||
                    string.IsNullOrEmpty(binding.mchBone) || string.IsNullOrEmpty(binding.sourceBone))
                    continue;
                result.Add(new ConstraintPlanEntry
                {
                    kind = ConstraintPlanKind.Parent,
                    ownerBone = binding.mchBone,
                    targetBone = binding.sourceBone,
                    weight = ConstraintInfluence(binding.constraint, 1.0f),
                    maintainOffset = true,
                    copyRotation = binding.constraint,
                });
                consumed.Add(ConstraintRecordKey(binding.mchBone, binding.constraint));
            }

            foreach (AuxBoneInfo aux in config.auxBones)
            {
                if (aux == null || string.IsNullOrEmpty(aux.boneName))
                    continue;
                string auxType = string.IsNullOrEmpty(aux.auxType)
                    ? string.Empty
                    : aux.auxType.Trim().ToUpperInvariant();
                BlenderConstraintInfo copyRotation = FindBlenderConstraint(aux, "COPY_ROTATION");
                BlenderConstraintInfo stretchTo = FindBlenderConstraint(aux, "STRETCH_TO");

                if (auxType == "TWIST" || auxType.EndsWith("_TWIST", StringComparison.Ordinal))
                {
                    if (copyRotation == null || stretchTo == null ||
                        !HaveSameCurrentArmatureTarget(config, copyRotation, stretchTo))
                        continue;
                    var entry = new ConstraintPlanEntry
                    {
                        kind = ConstraintPlanKind.Twist,
                        ownerBone = aux.boneName,
                        targetBone = copyRotation.targetBoneName,
                        sourceBone = FirstSourceBone(aux),
                        weight = ConstraintInfluence(copyRotation, 1.0f),
                        copyRotation = copyRotation,
                        stretchTo = stretchTo,
                    };
                    if (ConstraintMuted(copyRotation) || ConstraintMuted(stretchTo))
                        continue;
                    entry.hoAuxUnsupportedReason = ValidateHoAuxTwist(entry);
                    result.Add(entry);
                    consumed.Add(ConstraintRecordKey(aux.boneName, copyRotation));
                    consumed.Add(ConstraintRecordKey(aux.boneName, stretchTo));
                    continue;
                }

                if (auxType == "FAN" || auxType == "FAN_SINGLE" || auxType == "FAN_SIDE")
                {
                    if (copyRotation == null || !TargetsCurrentArmature(config, copyRotation))
                        continue;
                    var entry = new ConstraintPlanEntry
                    {
                        kind = ConstraintPlanKind.Fan,
                        ownerBone = aux.boneName,
                        targetBone = copyRotation.targetBoneName,
                        sourceBone = FirstSourceBone(aux),
                        weight = ConstraintInfluence(copyRotation, 1.0f),
                        copyRotation = copyRotation,
                    };
                    if (ConstraintMuted(copyRotation))
                        continue;
                    entry.hoAuxUnsupportedReason = ValidateHoAuxFan(entry);
                    result.Add(entry);
                    consumed.Add(ConstraintRecordKey(aux.boneName, copyRotation));
                }
            }

            foreach (KnownConstraintInfo known in config.knownConstraints)
            {
                if (consumed.Contains(ConstraintRecordKey(known.ownerBone, known.constraint)))
                    continue;
                AddUnknownPlan(
                    result,
                    known.ownerBone,
                    known.constraint,
                    ConstraintMuted(known.constraint)
                        ? "Blender 约束已静音"
                        : $"已知关系 {known.relationType}/{known.auxType} 未被当前 Parent/Twist/Fan 解析器完整消费");
            }

            foreach (UnknownConstraintInfo unknown in config.unknownConstraints)
            {
                AddUnknownPlan(result, unknown.ownerBone, unknown.constraint, unknown.reason);
            }
        }

        private static void AddUnknownPlan(
            List<ConstraintPlanEntry> result,
            string ownerBone,
            BlenderConstraintInfo constraint,
            string reason)
        {
            result.Add(new ConstraintPlanEntry
            {
                kind = ConstraintPlanKind.Unknown,
                ownerBone = ownerBone,
                targetBone = ConstraintTargetLabel(constraint),
                reason = reason,
                rawConstraint = constraint,
                weight = ConstraintInfluence(constraint, 1.0f),
            });
        }

        private static string ConstraintRecordKey(string ownerBone, BlenderConstraintInfo constraint)
        {
            return (ownerBone ?? string.Empty) + "\u001f" +
                (constraint == null ? -1 : constraint.stackIndex).ToString();
        }

        private static string ConstraintTargetLabel(BlenderConstraintInfo constraint)
        {
            if (constraint == null)
                return "（无目标）";
            string objectName = constraint.targetObjectName ?? string.Empty;
            string boneName = constraint.targetBoneName ?? string.Empty;
            if (string.IsNullOrEmpty(objectName))
                return string.IsNullOrEmpty(boneName) ? "（无目标）" : boneName;
            return string.IsNullOrEmpty(boneName) ? objectName : objectName + ":" + boneName;
        }

        private static bool TargetsCurrentArmature(
            ConstraintConfig config,
            BlenderConstraintInfo constraint)
        {
            return constraint != null && !string.IsNullOrEmpty(constraint.targetBoneName) &&
                string.Equals(
                    constraint.targetObjectName,
                    config.armatureName,
                    StringComparison.Ordinal);
        }

        private static bool HaveSameCurrentArmatureTarget(
            ConstraintConfig config,
            BlenderConstraintInfo first,
            BlenderConstraintInfo second)
        {
            return TargetsCurrentArmature(config, first) && TargetsCurrentArmature(config, second) &&
                string.Equals(first.targetBoneName, second.targetBoneName, StringComparison.Ordinal);
        }

        private static string FirstSourceBone(AuxBoneInfo aux)
        {
            return aux.sourceBones != null && aux.sourceBones.Count > 0
                ? aux.sourceBones[0]
                : string.Empty;
        }

        private static BlenderConstraintInfo FindBlenderConstraint(AuxBoneInfo aux, string type)
        {
            if (aux.constraints == null)
                return null;
            foreach (BlenderConstraintInfo constraint in aux.constraints)
            {
                if (constraint != null &&
                    string.Equals(constraint.constraintType, type, StringComparison.OrdinalIgnoreCase))
                    return constraint;
            }
            return null;
        }

        private static float ConstraintInfluence(
            BlenderConstraintInfo constraint,
            float fallback)
        {
            return Mathf.Clamp01(constraint == null || constraint.parameters == null
                ? fallback
                : constraint.parameters.influence);
        }

        private static bool ConstraintMuted(BlenderConstraintInfo constraint)
        {
            return constraint != null && constraint.parameters != null && constraint.parameters.mute;
        }

        private static string ValidateHoAuxTwist(ConstraintPlanEntry entry)
        {
            BlenderConstraintParameters copy = entry.copyRotation == null
                ? null
                : entry.copyRotation.parameters;
            BlenderConstraintParameters stretch = entry.stretchTo == null
                ? null
                : entry.stretchTo.parameters;
            if (!SupportsCopyRotationSpaces(copy, true))
                return "Copy Rotation 空间/轴/mix mode 超出当前 HoAux 能力";
            if (stretch == null || !IsWorldSpace(stretch.owner_space) ||
                !IsWorldSpace(stretch.target_space))
                return "Stretch To 当前只支持 WORLD -> WORLD";
            if (Mathf.Abs(stretch.head_tail) > 0.0001f)
                return "Stretch To head_tail 尚未实现";
            if (!string.IsNullOrEmpty(stretch.keep_axis) &&
                !string.Equals(stretch.keep_axis, "SWING_Y", StringComparison.OrdinalIgnoreCase))
                return "Stretch To 当前只支持 SWING_Y";
            if (!string.IsNullOrEmpty(stretch.volume) &&
                !string.Equals(stretch.volume, "NO_VOLUME", StringComparison.OrdinalIgnoreCase))
                return "Stretch To volume 保持尚未精确实现";
            return string.Empty;
        }

        private static string ValidateHoAuxFan(ConstraintPlanEntry entry)
        {
            BlenderConstraintParameters parameters = entry.copyRotation == null
                ? null
                : entry.copyRotation.parameters;
            return SupportsCopyRotationSpaces(parameters, false)
                ? string.Empty
                : "Fan 只支持 WORLD -> WORLD、XYZ、REPLACE";
        }

        private static bool SupportsCopyRotationSpaces(
            BlenderConstraintParameters parameters,
            bool allowLocalOwnerOrient)
        {
            if (parameters == null || !parameters.use_x || !parameters.use_y ||
                !parameters.use_z || parameters.invert_x || parameters.invert_y ||
                parameters.invert_z)
                return false;
            if (!string.IsNullOrEmpty(parameters.mix_mode) &&
                !string.Equals(parameters.mix_mode, "REPLACE", StringComparison.OrdinalIgnoreCase))
                return false;
            if (IsWorldSpace(parameters.owner_space) && IsWorldSpace(parameters.target_space))
                return true;
            return allowLocalOwnerOrient &&
                string.Equals(parameters.owner_space, "LOCAL", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(parameters.target_space, "LOCAL", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parameters.target_space, "LOCAL_OWNER_ORIENT", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsWorldSpace(string value)
        {
            return string.IsNullOrEmpty(value) ||
                string.Equals(value, "WORLD", StringComparison.OrdinalIgnoreCase);
        }

        private void DrawTargetPanel()
        {
            bool targetRequired = applyConstraints || applyCollections;
            string status = targetRig != null ? "已指定" : (targetRequired ? "需要指定" : "可选");
            Color color = targetRequired && targetRig == null
                ? new Color(0.88f, 0.42f, 0.28f)
                : new Color(0.91f, 0.65f, 0.25f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("目标骨架", status, color);
                GUILayout.Space(5f);
                targetRig = (GameObject)EditorGUILayout.ObjectField(
                    "场景 Rig", targetRig, typeof(GameObject), true);
                // FBX 页：只有勾了「导入约束」才需要这个字段。
                DrawConstraintRootField(applyConstraints);
                GUILayout.Space(2f);
            }
        }

        /// <summary>
        /// 「约束根物体」字段。
        ///
        /// <paramref name="available"/> 由调用页面给：FBX 页要看「导入约束」勾没勾，
        /// 约束工具页的「导入约束」按钮根本不看那个勾选，所以必须让它始终可填。
        /// 用它自己的状态去猜"该不该灰"，就会在两个页面上得出相反的结论。
        /// </summary>
        private void DrawConstraintRootField(bool available)
        {
            bool standard = constraintMode == ConstraintMode.Standard;
            using (new EditorGUI.DisabledScope(!available))
            {
                EditorGUILayout.BeginHorizontal();
                var label = new GUIContent(
                    "约束根物体",
                    standard ? "当前形态是 Unity 内置约束，用不到这个字段。" : ConstraintRootFieldTooltip);
                using (new EditorGUI.DisabledScope(standard))
                {
                    constraintRoot = (Transform)EditorGUILayout.ObjectField(
                        label,
                        constraintRoot,
                        typeof(Transform),
                        true);
                    using (new EditorGUI.DisabledScope(constraintRoot == null))
                    {
                        if (GUILayout.Button(
                                new GUIContent("用骨架", "把根物体清空，回到「目标骨架自己」的默认行为。"),
                                GUILayout.Width(56f)))
                        {
                            constraintRoot = null;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!available || standard)
                return;

            string effective = constraintMode == ConstraintMode.HoAux
                ? "留空 = HoAuxRig 组件挂到目标骨架根上"
                : "留空 = VRC 约束空物体直接摊在目标骨架底下";
            EditorGUILayout.LabelField(
                new GUIContent(
                    "  生效根物体：" +
                    (constraintRoot != null
                        ? GetTransformPath(constraintRoot)
                        : (targetRig != null ? targetRig.name + "（目标骨架）" : "目标骨架（未指定）")),
                    effective),
                EditorStyles.miniLabel);
        }

        private static string GetTransformPath(Transform value)
        {
            if (value == null)
                return "<空>";
            var names = new List<string>();
            Transform current = value;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string ConstraintModeLabel(ConstraintMode mode)
        {
            switch (mode)
            {
                case ConstraintMode.HoAux: return "HoAux Rig";
                case ConstraintMode.Vrc: return "VRC 约束";
                default: return "Unity 内置约束";
            }
        }

        private void DrawConstraintModeField()
        {
            EditorGUILayout.LabelField(new GUIContent("约束形态", ConstraintModeTooltip));
            using (new EditorGUI.IndentLevelScope())
            {
                ConstraintMode[] modes =
                {
                    ConstraintMode.Standard,
                    ConstraintMode.HoAux,
                    ConstraintMode.Vrc,
                };
                var labels = new GUIContent[]
                {
                    new GUIContent("Unity 内置", "约束组件挂在被驱动的骨骼上，会散在整个骨架里。"),
                    new GUIContent("HoAux Rig", "Parent / Twist / Fan 收进一个 HoAuxRig 组件，挂在约束根物体上。"),
                    new GUIContent("VRC 约束", "每条约束一个独立空物体当挂点，全部摊在约束根物体底下。需要 VRChat SDK。"),
                };

                int current = Array.IndexOf(modes, constraintMode);
                int selected = EditorGUILayout.Popup(Mathf.Max(0, current), labels);
                if (selected >= 0 && selected < modes.Length)
                    constraintMode = modes[selected];
            }

            if (constraintMode == ConstraintMode.Vrc && !HoVrcConstraintBridge.IsSdkAvailable)
            {
                EditorGUILayout.HelpBox(
                    "没有在本工程里找到 VRChat 约束类型（VRC.SDK3.Dynamics.Constraint）。\n" +
                    "装好 VRChat SDK 后本窗口会自动重新识别；当前请改用另外两种形态。",
                    MessageType.Error);
            }
        }

        private void DrawOptionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("处理项目", GetEnabledOptionCount() + " 项已启用", new Color(0.62f, 0.45f, 0.84f));
                GUILayout.Space(5f);
                applyHumanoid = EditorGUILayout.ToggleLeft("应用 Humanoid 映射", applyHumanoid);
                applyConstraints = EditorGUILayout.ToggleLeft("导入约束", applyConstraints);
                if (applyConstraints)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawConstraintModeField();
                        DrawConstraintImportPreview();
                    }
                }
                applyCollections = EditorGUILayout.ToggleLeft("应用 Bone Renderer 骨骼集合", applyCollections);
                GUILayout.Space(2f);
            }
        }

        private void DrawConstraintUtilitiesPage()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string status = targetRig == null ? "未指定" : targetRig.name;
                DrawPanelHeader("约束工作区", status, new Color(0.24f, 0.54f, 0.88f));
                GUILayout.Space(5f);
                targetRig = (GameObject)EditorGUILayout.ObjectField(
                    "目标骨架", targetRig, typeof(GameObject), true);
                constraintJson = (TextAsset)EditorGUILayout.ObjectField(
                    "约束 JSON", constraintJson, typeof(TextAsset), false);
                DrawConstraintCandidatePopup();
                DrawConstraintModeField();
                // 约束工具页：本页的「导入约束」按钮不看 applyConstraints，所以永远可填。
                DrawConstraintRootField(true);
                DrawConstraintImportPreview();
                GUILayout.Space(2f);
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("约束导入", "可撤销", new Color(0.20f, 0.68f, 0.57f));
                GUILayout.Space(6f);
                if (GUILayout.Button("导入约束", primaryButtonStyle))
                    ImportConstraintsFromUtilityPage();

                GUILayout.Space(2f);
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("清理", "谨慎操作", new Color(0.88f, 0.42f, 0.28f));
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "下面两个按钮只服务于「Unity 内置约束」；\n" +
                        "HoAux 与 VRC 两种形态各自集中在一处，选中直接删更省事。",
                        ClearScopeTooltip),
                    EditorStyles.miniLabel);
                if (GUILayout.Button(
                        new GUIContent("安全清除导入约束", SafeClearTooltip),
                        GUILayout.Height(28f)))
                    SafeClearImportedConstraints();
                if (GUILayout.Button(
                        new GUIContent("清除全部标准约束", ClearStandardTooltip),
                        GUILayout.Height(28f)))
                    ClearAllStandardConstraints();
                GUILayout.Space(2f);
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("骨架姿态", "Prefab", new Color(0.91f, 0.65f, 0.25f));
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    new GUIContent("删掉 VRC 约束之后骨骼不会自己还原，必要时用下面这个按钮收尾。", ResetPoseTooltip),
                    EditorStyles.miniLabel);
                if (GUILayout.Button(
                        new GUIContent("还原骨架到 Prefab 姿态", "把骨骼的位置 / 旋转 / 缩放还原成 Prefab 里的值。"),
                        GUILayout.Height(28f)))
                    ResetRigToPrefabPose();
                GUILayout.Space(2f);
            }
        }

        private void ImportConstraintsFromUtilityPage()
        {
            if (!ValidateConstraintUtilityInputs(true))
                return;
            if (constraintMode == ConstraintMode.Vrc && !HoVrcConstraintBridge.IsSdkAvailable)
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "本工程没有 VRChat 约束类型（VRC.SDK3.Dynamics.Constraint）。\n" +
                    "请先装好 VRChat SDK，或改用 Unity 内置约束 / HoAux Rig。",
                    "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "导入约束",
                    "将按上方预览把中立约束 IR 应用到目标骨架。\n当前形态：" +
                    ConstraintModeLabel(constraintMode) +
                    "\n落盘位置：" +
                    (constraintMode == ConstraintMode.Standard
                        ? "被驱动的骨骼上（散在整个骨架里）"
                        : ConstraintRootLabel() + "（集中管理）") +
                    "\n用户手工创建的同类型约束不会被覆盖。",
                    "导入",
                    "取消"))
                return;

            try
            {
                int importedCount = ApplyConstraints(
                    constraintJson,
                    targetRig,
                    constraintMode,
                    constraintRoot);
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    $"约束导入完成。导入：{importedCount}",
                    "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("HoFBX导入处理", "约束导入失败：" + exception.Message, "确定");
            }
        }

        private bool ValidateConstraintUtilityInputs(bool requireJson)
        {
            if (targetRig == null)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "请先指定目标骨架。", "确定");
                return false;
            }

            // 目标必须是场景对象。允许指向 FBX / 预制件资产的话，
            // VRC 形态会在资产内部新建 GameObject 层级、HoAux 形态会往资产挂组件，
            // 容易出现"场景里看着改了、其实改在资产上"的状态不一致。
            if (!IsSceneObject(targetRig))
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    $"目标骨架「{targetRig.name}」不是场景对象（看着像 FBX / 预制件资产）。\n" +
                    "请把预制件拖进场景后再指定。",
                    "确定");
                return false;
            }

            if (constraintRoot != null && !IsSceneObject(constraintRoot.gameObject))
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    $"约束根物体「{constraintRoot.name}」不是场景对象（看着像资产）。\n" +
                    "请指定场景里的物体。",
                    "确定");
                return false;
            }

            if (requireJson && constraintJson == null)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "请先指定约束 JSON。", "确定");
                return false;
            }

            return true;
        }

        private static bool IsSceneObject(GameObject value)
        {
            return value != null && value.scene.IsValid();
        }

        private void SafeClearImportedConstraints()
        {
            if (!ValidateConstraintUtilityInputs(false))
                return;

            // 目标骨架自己的标记单独取一份：来源骨架名只能从这一份推断，
            // 否则"目标骨架从没导入过、同预制件里另一个骨架导入过"时，
            // 会把另一个骨架的导入当成这次的清理目标。
            var rigMarkers = new List<HoImportedConstraintMarker>();
            foreach (HoImportedConstraintMarker marker in
                     targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
            {
                if (marker != null)
                    rigMarkers.Add(marker);
            }

            List<HoImportedConstraintMarker> markers =
                CollectImportedMarkers(targetRig, constraintRoot);
            HoAuxRig hoAux = FindImportedHoAuxRig(targetRig, constraintRoot, null);

            string expectedArmature = FindExpectedArmature(rigMarkers);
            if (!string.IsNullOrEmpty(expectedArmature))
            {
                markers = FilterMarkersByArmature(markers, expectedArmature);
                if (hoAux != null &&
                    !string.IsNullOrEmpty(hoAux.SourceArmature) &&
                    !string.Equals(hoAux.SourceArmature, expectedArmature, StringComparison.Ordinal))
                {
                    hoAux = null;
                }
            }
            else if (constraintRoot != null && rigMarkers.Count == 0)
            {
                // 目标骨架自己没有任何导入痕迹，却要按"根物体那一侧"的范围清 ——
                // 那多半会牵连别的骨架，至少让用户先看到这个事实。
                Debug.LogWarning(
                    "HoFBX: 目标骨架「" + targetRig.name +
                    "」上没有任何导入标记，但约束根物体在别处；" +
                    "本次清理范围按约束根物体那一侧计算。");
            }

            if (markers.Count == 0 && hoAux == null)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "没有找到工具导入的约束。", "确定");
                return;
            }

            // 挂点物体只在该收的时候收：用户把它当普通空物体用过（塞了子物体或别的组件）
            // 就不能整块删，否则一次点击连带删掉用户的东西。
            var emptyHosts = new List<GameObject>();
            var keptHosts = new List<GameObject>();
            var seenHosts = new HashSet<int>();
            foreach (HoImportedConstraintMarker marker in markers)
            {
                if (marker.hostRole != ConstraintHostRole.VrcConstraintObject)
                    continue;
                GameObject host = marker.gameObject;
                if (host == null || !seenHosts.Add(host.GetInstanceID()))
                    continue;
                if (IsPureVrcHost(host))
                    emptyHosts.Add(host);
                else
                    keptHosts.Add(host);
            }

            int constraintCount = 0;
            foreach (HoImportedConstraintMarker marker in markers)
                foreach (Component constraint in marker.GetLiveConstraints())
                    constraintCount++;
            int hoAuxOperationCount = hoAux == null ? 0 : CountHoAuxOperations(hoAux);

            if (!EditorUtility.DisplayDialog(
                    "安全清除导入约束",
                    $"将删除：\n" +
                    $"· 工具导入的约束 {constraintCount} 条\n" +
                    (emptyHosts.Count == 0 ? string.Empty : $"· VRC 挂点空物体 {emptyHosts.Count} 个（连物体一起）\n") +
                    (keptHosts.Count == 0 ? string.Empty : $"· VRC 挂点 {keptHosts.Count} 个上还有别的东西，只删约束、保留物体\n") +
                    (hoAux == null ? string.Empty : $"· 1 个导入的 HoAuxRig（含 {hoAuxOperationCount} 条操作）\n") +
                    (string.IsNullOrEmpty(expectedArmature)
                        ? "\n清理范围：目标骨架与约束根物体那一侧的全部工具导入。"
                        : $"\n清理范围：限定为「{expectedArmature}」这一份导出。") +
                    "\n用户手工创建的约束不受影响。",
                    "清除",
                    "取消"))
            {
                return;
            }

            int clearedCount = 0;
            foreach (HoImportedConstraintMarker marker in markers)
            {
                foreach (Component constraint in new List<Component>(marker.GetLiveConstraints()))
                {
                    Undo.DestroyObjectImmediate(constraint);
                    clearedCount++;
                }
                if (marker.hostRole == ConstraintHostRole.VrcConstraintObject)
                    continue;
                Undo.DestroyObjectImmediate(marker);
            }

            // VRC 模式下"每条约束一个独立空物体当挂点"：约束组件已经被上面的循环删掉了，
            // 纯挂点的空物体连同它的导入标记一起收掉，别在根物体底下留一堆空壳。
            foreach (GameObject host in emptyHosts)
            {
                if (host == null)
                    continue;
                foreach (Component leftover in HoVrcConstraintBridge.GetConstraintComponents(host))
                {
                    Undo.DestroyObjectImmediate(leftover);
                    clearedCount++;
                }
                var marker = host.GetComponent<HoImportedConstraintMarker>();
                if (marker != null)
                    Undo.DestroyObjectImmediate(marker);
                Undo.DestroyObjectImmediate(host);
            }

            if (hoAux != null)
            {
                clearedCount += hoAuxOperationCount;
                Undo.DestroyObjectImmediate(hoAux);
            }

            EditorUtility.DisplayDialog(
                "HoFBX导入处理",
                $"已安全清除 {clearedCount} 个导入约束" +
                (emptyHosts.Count == 0 ? "。" : $"，并回收 {emptyHosts.Count} 个 VRC 挂点空物体。") +
                (keptHosts.Count == 0
                    ? string.Empty
                    : $"\n{keptHosts.Count} 个挂点上还有别的内容，只删了约束、物体保留。") +
                "\n用户手工创建的约束不受影响。",
                "确定");
        }

        /// <summary>
        /// 这个 VRC 挂点是不是"纯挂点"：没有子物体，也没有除 Transform /
        /// 导入标记 / VRChat 约束以外的组件。只有纯挂点才能连物体一起删。
        /// </summary>
        private static bool IsPureVrcHost(GameObject host)
        {
            if (host == null || host.transform.childCount > 0)
                return false;

            List<Component> vrcConstraints = HoVrcConstraintBridge.GetConstraintComponents(host);
            Component[] components = host.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null || component is Transform)
                    continue;
                if (component is HoImportedConstraintMarker)
                    continue;
                if (vrcConstraints.Contains(component))
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 目标骨架 + 约束根物体（可能挂在骨架外面）两处的导入标记。
        /// 外部根物体上的 VRC 空物体不在骨架层级里，只查骨架会漏掉它们。
        /// </summary>
        private static List<HoImportedConstraintMarker> CollectImportedMarkers(
            GameObject rig,
            Transform explicitRoot)
        {
            var result = new List<HoImportedConstraintMarker>();
            var seen = new HashSet<int>();

            if (rig != null)
            {
                foreach (HoImportedConstraintMarker marker in
                         rig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
                {
                    if (marker != null && seen.Add(marker.GetInstanceID()))
                        result.Add(marker);
                }
            }

            if (explicitRoot != null)
            {
                GameObject boundary = PrefabUtility.GetOutermostPrefabInstanceRoot(explicitRoot);
                foreach (HoImportedConstraintMarker marker in
                         explicitRoot.GetComponentsInChildren<HoImportedConstraintMarker>(true))
                {
                    if (marker != null && seen.Add(marker.GetInstanceID()))
                        result.Add(marker);
                }

                if (boundary != null)
                {
                    foreach (HoImportedConstraintMarker marker in
                             boundary.GetComponentsInChildren<HoImportedConstraintMarker>(true))
                    {
                        if (marker != null && seen.Add(marker.GetInstanceID()))
                            result.Add(marker);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 目标骨架自己那批标记里登记的来源骨架名。用于把清理范围限定在同一份导出上。
        /// 返回第一个非空值；目标骨架自身一个标记都没有时返回 null（表示不限定）。
        /// </summary>
        private static string FindExpectedArmature(List<HoImportedConstraintMarker> markers)
        {
            if (markers == null)
                return null;
            for (int index = 0; index < markers.Count; index++)
            {
                HoImportedConstraintMarker marker = markers[index];
                if (marker == null || marker.hostRole == ConstraintHostRole.VrcConstraintObject)
                    continue;
                if (!string.IsNullOrEmpty(marker.sourceArmature))
                    return marker.sourceArmature;
            }
            return null;
        }

        private static List<HoImportedConstraintMarker> FilterMarkersByArmature(
            List<HoImportedConstraintMarker> markers,
            string armature)
        {
            var result = new List<HoImportedConstraintMarker>();
            for (int index = 0; index < markers.Count; index++)
            {
                HoImportedConstraintMarker marker = markers[index];
                if (marker == null)
                    continue;
                // 来源为空的旧数据一律保留，不然会被永远清不掉。
                if (string.IsNullOrEmpty(marker.sourceArmature) ||
                    string.Equals(marker.sourceArmature, armature, StringComparison.Ordinal))
                {
                    result.Add(marker);
                }
            }
            return result;
        }

        private static int CountHoAuxOperations(HoAuxRig rig)
        {
            int count = 0;
            foreach (HoAuxRig.Layer layer in rig.Layers)
            {
                if (layer != null && layer.operations != null)
                    count += layer.operations.Count;
            }
            return count;
        }

        private void ClearAllStandardConstraints()
        {
            if (!ValidateConstraintUtilityInputs(false))
                return;

            List<Component> constraints = GetAllStandardConstraints(targetRig);
            if (constraints.Count == 0)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "目标骨架上没有标准约束。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清除全部标准约束",
                    $"将删除 {constraints.Count} 个标准约束，包括用户手工创建的约束。",
                    "全部清除",
                    "取消"))
                return;

            foreach (Component constraint in constraints)
                Undo.DestroyObjectImmediate(constraint);

            // 只收掉"挂在骨骼上"的导入标记：VRC 空物体上的标记属于 VRC 形态，
            // 这个按钮不碰它（否则会把 VRC 空物体变成没有标签的孤儿）。
            foreach (HoImportedConstraintMarker marker in
                     targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
            {
                if (marker.hostRole == ConstraintHostRole.VrcConstraintObject)
                    continue;
                Undo.DestroyObjectImmediate(marker);
            }

            EditorUtility.DisplayDialog(
                "HoFBX导入处理",
                $"已清除 {constraints.Count} 个标准约束。",
                "确定");
        }

        private static List<Component> GetAllStandardConstraints(GameObject rig)
        {
            var result = new List<Component>();
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                result.AddRange(bone.GetComponents<RotationConstraint>());
                result.AddRange(bone.GetComponents<PositionConstraint>());
                result.AddRange(bone.GetComponents<ScaleConstraint>());
                result.AddRange(bone.GetComponents<ParentConstraint>());
            }
            return result;
        }

        private void ResetRigToPrefabPose()
        {
            if (!ValidateConstraintUtilityInputs(false))
                return;
            if (!PrefabUtility.IsPartOfPrefabInstance(targetRig))
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "目标骨架不是 Prefab 实例，无法还原 Prefab 姿态。",
                    "确定");
                return;
            }

            List<Transform> bones = CollectRigBones(targetRig);
            var editableBones = new List<Transform>(bones.Count);
            var sourceBones = new List<Transform>(bones.Count);
            foreach (Transform bone in bones)
            {
                if (bone == null)
                    continue;
                Transform source = PrefabUtility.GetCorrespondingObjectFromSource(bone) as Transform;
                if (source == null)
                    continue;
                editableBones.Add(bone);
                sourceBones.Add(source);
            }

            if (editableBones.Count == 0)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "没有可还原的 Prefab 骨骼。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "还原骨架到 Prefab 姿态",
                    $"将还原 {editableBones.Count} 根骨骼的位置、旋转和缩放，当前姿态改动会被覆盖。",
                    "还原",
                    "取消"))
                return;

            Undo.RecordObjects(editableBones.ToArray(), "还原骨架到 Prefab 姿态");
            for (int i = 0; i < editableBones.Count; i++)
            {
                editableBones[i].localPosition = sourceBones[i].localPosition;
                editableBones[i].localRotation = sourceBones[i].localRotation;
                editableBones[i].localScale = sourceBones[i].localScale;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(targetRig.scene);
            EditorUtility.DisplayDialog(
                "HoFBX导入处理",
                $"已还原 {editableBones.Count} 根骨骼。",
                "确定");
        }

        private static List<Transform> CollectRigBones(GameObject rig)
        {
            var roots = new List<Transform>();
            foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform root = renderer.rootBone != null
                    ? renderer.rootBone
                    : FindCommonAncestor(renderer.bones);
                if (root != null && !roots.Contains(root))
                    roots.Add(root);
            }

            roots.RemoveAll(root =>
                roots.Exists(other => other != root && root.IsChildOf(other)));

            var bones = new List<Transform>();
            if (roots.Count == 0)
            {
                CollectBonesUnderRoot(rig.transform, bones);
                return bones;
            }

            foreach (Transform root in roots)
                CollectBonesUnderRoot(root, bones);
            return bones;
        }

        private static Transform FindCommonAncestor(Transform[] bones)
        {
            if (bones == null || bones.Length == 0)
                return null;

            Transform ancestor = bones[0];
            for (int i = 1; i < bones.Length && ancestor != null; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                    continue;
                while (ancestor != null && !bone.IsChildOf(ancestor))
                    ancestor = ancestor.parent;
            }
            return ancestor;
        }

        private static void CollectBonesUnderRoot(Transform node, List<Transform> output)
        {
            if (node == null || node.GetComponent<Renderer>() != null || node.GetComponent<MeshFilter>() != null)
                return;

            output.Add(node);
            for (int i = 0; i < node.childCount; i++)
                CollectBonesUnderRoot(node.GetChild(i), output);
        }


        private int GetEnabledOptionCount()
        {
            return (applyHumanoid ? 1 : 0) +
                   (applyConstraints ? 1 : 0) +
                   (applyCollections ? 1 : 0);
        }

        private void DrawPanelHeader(string title, string status, Color accent)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.82f, 0.83f, 0.85f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            Rect titleRect = new Rect(rect.x + 12f, rect.y, rect.width - 120f, rect.height);
            Rect statusRect = new Rect(rect.xMax - 104f, rect.y, 94f, rect.height);
            GUI.Label(titleRect, title, panelTitleStyle);
            GUI.Label(statusRect, status, panelStatusStyle);
        }

        private void EnsureStyles()
        {
            // 缓存的 GUIStyle 副本会把创建时的皮肤整个固定下来。编辑器启动、域重载后恢复窗口，
            // 或者切换明暗主题时，EditorStyles/GUI.skin 已经重建，副本却还停在旧皮肤上：
            // 面板标题、状态和主按钮就是这样变成浅色皮肤的黑字的（深色主题下几乎看不见），
            // 按钮背景也会整个丢掉。副本不会自愈，只能在发现皮肤变了时整体重建。
            bool proSkin = EditorGUIUtility.isProSkin;
            if (stylesBuiltForProSkin != proSkin ||
                !HoEditorStyles.MatchesSource(panelTitleStyle, EditorStyles.boldLabel) ||
                !HoEditorStyles.MatchesSource(panelStatusStyle, EditorStyles.miniLabel) ||
                !HoEditorStyles.MatchesSource(primaryButtonStyle, GUI.skin.button))
            {
                stylesBuiltForProSkin = proSkin;

                panelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0)
                };

                panelStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 0, 0, 0)
                };

                primaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 36f,
                    fontStyle = FontStyle.Bold,
                    fontSize = 13
                };
            }

            // 双保险：来源样式实例没换、只是重建过颜色时，也每帧把文字颜色对齐一次。
            HoEditorStyles.SyncTextColor(panelTitleStyle, EditorStyles.boldLabel);
            HoEditorStyles.SyncTextColor(panelStatusStyle, EditorStyles.miniLabel);
        }

        private void ScanAdjacentMetadata()
        {
            if (!IsFbx(fbxAssetPath))
                return;

            humanoidJson = null;
            collectionJson = null;
            constraintJson = null;
            if (constraintJsonCandidates == null)
                constraintJsonCandidates = new List<TextAsset>();
            else
                constraintJsonCandidates.Clear();

            string directory = Path.GetDirectoryName(fbxAssetPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(fbxAssetPath);
            string metadataDirectory = NormalizeAssetPath(
                Path.Combine(directory, MetadataDirectoryName));
            string manifestPath = NormalizeAssetPath(
                Path.Combine(metadataDirectory, baseName + ManifestSuffix));
            TextAsset manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);

            if (manifestAsset != null)
            {
                MetadataManifest manifest = null;
                try
                {
                    manifest = JsonUtility.FromJson<MetadataManifest>(manifestAsset.text);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"HoUnityTools manifest parse failed: {manifestPath}\n{exception.Message}");
                }

                if (manifest != null && manifest.files != null)
                {
                    foreach (MetadataEntry entry in manifest.files)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.file))
                            continue;

                        TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                            NormalizeAssetPath(Path.Combine(metadataDirectory, entry.file)));
                        if (asset == null)
                            continue;

                        if (entry.kind == HumanoidKind && humanoidJson == null)
                            humanoidJson = asset;
                        else if (entry.kind == CollectionKind && collectionJson == null)
                            collectionJson = asset;
                        else if (entry.kind == ConstraintKind)
                            AddConstraintCandidate(asset);
                    }
                }
            }

            if (constraintJsonCandidates.Count > 0)
                constraintJson = constraintJsonCandidates[0];

            // Manual export mode can omit the manifest; scan only the new HoFBX folder.
            if (humanoidJson == null)
                humanoidJson = LoadMetadataTextAsset(metadataDirectory, baseName + "_humanoid.json");
            if (collectionJson == null)
            {
                foreach (string path in FindMetadataJson(metadataDirectory, baseName, "_collection.json"))
                {
                    collectionJson = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (collectionJson != null)
                        break;
                }
            }
            if (constraintJson == null)
            {
                foreach (string path in FindMetadataJson(metadataDirectory, baseName, "_constraint.json"))
                    AddConstraintCandidate(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                if (constraintJsonCandidates.Count > 0)
                    constraintJson = constraintJsonCandidates[0];
            }
        }

        private void AddConstraintCandidate(TextAsset asset)
        {
            if (constraintJsonCandidates == null)
                constraintJsonCandidates = new List<TextAsset>();
            if (asset != null && !constraintJsonCandidates.Contains(asset))
                constraintJsonCandidates.Add(asset);
        }

        private static TextAsset LoadMetadataTextAsset(string metadataDirectory, string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<TextAsset>(
                NormalizeAssetPath(Path.Combine(metadataDirectory, fileName)));
        }

        private static IEnumerable<string> FindMetadataJson(string metadataDirectory, string baseName, string suffix)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absoluteDirectory = Path.Combine(
                projectRoot,
                metadataDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteDirectory))
                yield break;

            string prefix = baseName + "_";
            foreach (string absolutePath in Directory.GetFiles(absoluteDirectory, "*.json"))
            {
                string fileName = Path.GetFileName(absolutePath);
                if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = absolutePath.Substring(projectRoot.Length + 1);
                yield return NormalizeAssetPath(relative);
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/');
        }

        private void ApplySelectedMapping()
        {
            if (!IsFbx(fbxAssetPath))
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "FBX 资产路径无效。", "确定");
                return;
            }

            if (!applyHumanoid && !applyConstraints && !applyCollections)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "至少选择一项要应用的配置。", "确定");
                return;
            }

            if ((applyConstraints || applyCollections) && targetRig == null)
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "导入约束或骨骼集合时必须指定目标骨架。",
                    "确定");
                return;
            }

            // 与约束工具页共用同一套"目标必须是场景对象"校验：
            // 资产上落 VRC 挂点 / HoAuxRig 会在资产内部建物体，状态容易不一致。
            if (applyConstraints && !ValidateConstraintUtilityInputs(false))
                return;

            if (applyHumanoid && humanoidJson == null)
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "已选择 Humanoid 映射，但没有 Humanoid JSON。",
                    "确定");
                return;
            }

            if (applyHumanoid)
            {
                HumanBone[] previewBones;
                int mappedCount;
                string mappingError;
                if (!TryBuildHumanBones(humanoidJson, out previewBones, out mappedCount, out mappingError))
                {
                    EditorUtility.DisplayDialog("HoFBX导入处理", mappingError, "确定");
                    return;
                }

                if (mappedCount == 0)
                {
                    EditorUtility.DisplayDialog(
                        "HoFBX导入处理",
                        "最终 Humanoid 映射为空，已阻止覆盖当前 Avatar 配置。",
                        "确定");
                    return;
                }
            }

            if (applyConstraints && constraintJson == null)
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "已选择导入约束，但没有约束 JSON。",
                    "确定");
                return;
            }

            if (applyCollections && collectionJson == null)
            {
                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    "已选择应用骨骼集合，但没有骨骼集合 JSON。",
                    "确定");
                return;
            }

            string targetId = targetRig == null
                ? string.Empty
                : GlobalObjectId.GetGlobalObjectIdSlow(targetRig).ToString();
            int humanoidCount = 0;
            int constraintCount = 0;
            bool collectionApplied = false;

            if (!EditorUtility.DisplayDialog(
                "确认应用 HoFBX 配置",
                BuildConfirmationText(),
                "应用",
                "取消"))
            {
                return;
            }

            try
            {
                if (applyHumanoid)
                {
                    humanoidCount = ApplyHumanoidMapping();
                    targetRig = ResolveTargetRig(targetId, fbxAssetPath);
                }

                if (applyConstraints)
                {
                    if (targetRig != null)
                        constraintCount = ApplyConstraints(
                            constraintJson,
                            targetRig,
                            constraintMode,
                            constraintRoot);
                }

                if (applyCollections && collectionJson != null && targetRig != null)
                {
                    collectionApplied = ApplyCollections(collectionJson, targetRig);
                }

                EditorUtility.DisplayDialog(
                    "HoFBX导入处理",
                    $"应用完成。Humanoid：{humanoidCount}，约束：{constraintCount}，Bone Renderer：{(collectionApplied ? "已更新" : "未应用")}",
                    "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("HoFBX导入处理", "应用失败：" + exception.Message, "确定");
            }
        }

        private string BuildConfirmationText()
        {
            string humanoidOperation = string.Empty;
            if (applyHumanoid && TryBuildHumanBones(
                    humanoidJson, out HumanBone[] _, out int mappedCount, out string _))
            {
                humanoidOperation =
                    $"- 严格写入 Humanoid 映射：{mappedCount} 项映射，{HumanTrait.BoneCount - mappedCount} 项置空\n";
            }

            return "将执行以下显式操作：\n" +
                   humanoidOperation +
                   (applyConstraints ? ConstraintConfirmationLine() : string.Empty) +
                   (applyCollections ? "- 更新 Bone Renderer 集合\n" : string.Empty) +
                   "\n用户已有的手动配置不会被 AssetPostprocessor 自动覆盖。";
        }

        private string ConstraintConfirmationLine()
        {
            switch (constraintMode)
            {
                case ConstraintMode.HoAux:
                    return "- 以 HoAux Rig 解析 Parent / Twist / Fan（组件挂在" +
                        ConstraintRootLabel() + "）\n";
                case ConstraintMode.Vrc:
                    return "- 以 VRChat 约束导入（每条约束一个独立空物体当挂点，摊在" +
                        ConstraintRootLabel() + "底下，TargetTransform 指回被驱动的骨骼）\n";
                default:
                    return "- 以标准 Unity Constraint 导入（Twist 仅 Y 轴，挂在被驱动的骨骼上）\n";
            }
        }

        private string ConstraintRootLabel()
        {
            if (constraintRoot != null)
                return "「" + GetTransformPath(constraintRoot) + "」";
            return targetRig == null ? "目标骨架" : "目标骨架「" + targetRig.name + "」";
        }

        private int ApplyHumanoidMapping()
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("无法获取 FBX ModelImporter。");

            HumanBone[] humanBones;
            int mappedCount;
            string error;
            if (!TryBuildHumanBones(humanoidJson, out humanBones, out mappedCount, out error))
                throw new InvalidOperationException(error);
            if (mappedCount == 0)
                throw new InvalidOperationException("最终 Humanoid 映射为空。");

            HumanDescription description = importer.humanDescription;
            description.human = humanBones;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.humanDescription = description;
            importer.SaveAndReimport();
            return mappedCount;
        }

        private static bool TryBuildHumanBones(
            TextAsset mappingAsset,
            out HumanBone[] humanBones,
            out int mappedCount,
            out string error)
        {
            humanBones = Array.Empty<HumanBone>();
            mappedCount = 0;
            error = string.Empty;
            if (mappingAsset == null)
            {
                error = "没有 Humanoid 映射 JSON。";
                return false;
            }

            HumanoidMappingFile mapping;
            try
            {
                mapping = JsonUtility.FromJson<HumanoidMappingFile>(mappingAsset.text);
            }
            catch (Exception exception)
            {
                error = "Humanoid 映射 JSON 解析失败：" + exception.Message;
                return false;
            }

            humanBones = BuildHumanBones(mapping);
            foreach (HumanBone humanBone in humanBones)
            {
                if (!string.IsNullOrEmpty(humanBone.boneName))
                    mappedCount++;
            }

            return true;
        }

        private static HumanBone[] BuildHumanBones(HumanoidMappingFile mapping)
        {
            var mappedBones = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedHumanNames = new HashSet<string>(StringComparer.Ordinal);
            var usedBoneNames = new HashSet<string>(StringComparer.Ordinal);
            if (mapping != null && mapping.armatures != null)
            {
                foreach (HumanoidArmature armature in mapping.armatures)
                {
                    if (armature == null || armature.bones == null)
                        continue;

                    foreach (HumanoidBone item in armature.bones)
                    {
                        if (item == null || string.IsNullOrEmpty(item.boneName) || string.IsNullOrEmpty(item.humanName))
                            continue;
                        if (!IsUnityHumanoidName(item.humanName) ||
                            !usedHumanNames.Add(item.humanName) ||
                            !usedBoneNames.Add(item.boneName))
                            continue;

                        mappedBones[item.humanName] = item.boneName;
                    }
                }
            }

            var result = new List<HumanBone>(mappedBones.Count);
            foreach (string humanName in HumanTrait.BoneName)
            {
                if (!mappedBones.TryGetValue(humanName, out string boneName))
                    continue;

                var humanBone = new HumanBone
                {
                    humanName = humanName,
                    boneName = boneName,
                };
                humanBone.limit.useDefaultValues = true;
                result.Add(humanBone);
            }

            return result.ToArray();
        }

        private static bool TryBuildHumanBonePreview(
            TextAsset mappingAsset,
            out HumanBone[] previewBones,
            out int mappedCount,
            out string error)
        {
            previewBones = Array.Empty<HumanBone>();
            mappedCount = 0;
            error = string.Empty;
            if (!TryBuildHumanBones(
                    mappingAsset,
                    out HumanBone[] mappedBones,
                    out mappedCount,
                    out error))
                return false;

            var mappedByHumanName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (HumanBone humanBone in mappedBones)
                mappedByHumanName[humanBone.humanName] = humanBone.boneName;

            previewBones = new HumanBone[HumanTrait.BoneCount];
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                string humanName = HumanTrait.BoneName[i];
                mappedByHumanName.TryGetValue(humanName, out string boneName);
                var humanBone = new HumanBone
                {
                    humanName = humanName,
                    boneName = boneName ?? string.Empty,
                };
                humanBone.limit.useDefaultValues = true;
                previewBones[i] = humanBone;
            }

            return true;
        }
        private static bool IsUnityHumanoidName(string name)
        {
            foreach (string knownName in HumanTrait.BoneName)
            {
                if (string.Equals(knownName, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static GameObject ResolveTargetRig(string targetId, string assetPath)
        {
            if (!string.IsNullOrEmpty(targetId) && GlobalObjectId.TryParse(targetId, out GlobalObjectId id))
            {
                GameObject resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (resolved != null)
                    return resolved;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private static int ApplyConstraints(
            TextAsset configAsset,
            GameObject rig,
            ConstraintMode mode,
            Transform explicitRoot)
        {
            ConstraintConfig config = JsonUtility.FromJson<ConstraintConfig>(configAsset.text);
            if (!TryValidateConstraintConfig(config, out string validationError))
                throw new InvalidDataException(validationError);

            if (mode == ConstraintMode.Vrc && !HoVrcConstraintBridge.IsSdkAvailable)
            {
                throw new InvalidOperationException(
                    "本工程没有 VRChat 约束类型（VRC.SDK3.Dynamics.Constraint）。\n" +
                    "请先装好 VRChat SDK，或改用 Unity 内置约束 / HoAux Rig。");
            }

            List<ConstraintPlanEntry> plan = BuildConstraintImportPlan(config);
            Dictionary<string, Transform> transformMap = BuildTransformMap(rig.transform);

            // 先把"能不能落盘"判完，再动任何东西。
            // 否则一旦后面抛错/全跳过，用户上一次的导入已经被清掉了，只留下半截状态。
            ValidateBeforeClearing(config, rig, mode, plan, transformMap, explicitRoot);

            // 三种形态互斥：清掉上一次由本工具导入的三种形态，再落新的。
            // HoAuxRig 按"约束根物体/骨架"这个范围回收，不按骨架名 ——
            // 在"约束骨架"下拉里换了候选（armatureName 变了）时旧组件也必须回收，
            // 否则两种形态会同时驱动同一批骨骼。
            ClearImportedStandardConstraints(rig);
            int removedHoAux = RemoveImportedHoAuxRigs(rig, explicitRoot);
            int removedVrcObjects = RemoveImportedVrcConstraintObjects(rig, explicitRoot);

            int count = 0;
            switch (mode)
            {
                case ConstraintMode.HoAux:
                    count = ApplyHoAuxConstraints(config, rig, plan, transformMap, explicitRoot);
                    break;
                case ConstraintMode.Vrc:
                    count = ApplyVrcConstraints(config, rig, plan, transformMap, explicitRoot);
                    break;
                default:
                    count = ApplyStandardConstraints(plan, transformMap, config);
                    break;
            }

            if (removedHoAux > 0)
                Debug.Log($"HoFBX: 已回收上一次导入的 {removedHoAux} 个 HoAuxRig。");
            if (removedVrcObjects > 0)
                Debug.Log($"HoFBX: 已回收上一次导入的 {removedVrcObjects} 个 VRC 约束空物体。");
            return count;
        }

        /// <summary>
        /// 清场之前能做的判定全部做掉：VRC 类型是否可用、HoAux 根物体上有没有
        /// 用户手工配置的组件、计划里的 owner/target 能不能解析。
        /// </summary>
        private static void ValidateBeforeClearing(
            ConstraintConfig config,
            GameObject rig,
            ConstraintMode mode,
            List<ConstraintPlanEntry> plan,
            Dictionary<string, Transform> transformMap,
            Transform explicitRoot)
        {
            foreach (ConstraintPlanEntry entry in plan)
            {
                if (entry.kind == ConstraintPlanKind.Unknown)
                    continue;
                if (mode == ConstraintMode.HoAux && !string.IsNullOrEmpty(entry.hoAuxUnsupportedReason))
                    continue;
                TryResolvePlanTransforms(transformMap, entry, out Transform _, out Transform _);
            }

            if (mode != ConstraintMode.HoAux)
                return;

            Transform root = ResolveConstraintRoot(rig, explicitRoot);
            if (root == null)
                return;

            HoAuxRig existing = ResolveSingleHoAuxRig(root.gameObject);
            if (existing == null)
                return;
            if (string.IsNullOrEmpty(existing.SourceArmature) && CountHoAuxOperations(existing) > 0)
            {
                throw new InvalidOperationException(
                    $"约束根物体「{root.name}」上已有手动配置的 HoAuxRig。" +
                    "请先清空或移除该组件，导入器不会覆盖手动操作。");
            }
        }

        /// <summary>Unity 内置约束：挂在被驱动的骨骼上。</summary>
        private static int ApplyStandardConstraints(
            List<ConstraintPlanEntry> plan,
            Dictionary<string, Transform> transformMap,
            ConstraintConfig config)
        {
            int count = 0;
            foreach (ConstraintPlanEntry entry in plan)
            {
                if (entry.kind == ConstraintPlanKind.Unknown)
                    continue;
                if (!TryResolvePlanTransforms(transformMap, entry, out Transform bone, out Transform target))
                    continue;

                count += ConfigureStandardOperation(bone, target, entry, config) ? 1 : 0;
            }
            return count;
        }

        /// <summary>
        /// HoAux：整套语义收进一个 HoAuxRig 组件，组件挂在约束根物体上
        /// （根物体留空时就是目标骨架自己）。
        /// </summary>
        private static int ApplyHoAuxConstraints(
            ConstraintConfig config,
            GameObject rig,
            List<ConstraintPlanEntry> plan,
            Dictionary<string, Transform> transformMap,
            Transform explicitRoot)
        {
            if (!ContainsHoAuxOperations(plan))
                return 0;

            Transform root = ResolveConstraintRoot(rig, explicitRoot);
            if (root == null)
                return 0;

            // 约束根物体可能不在骨架层级里，所以先按"根物体"找一遍，
            // 再退回骨架层级 —— 老数据把组件挂在骨架根上、RigRoot 为空。
            HoAuxRig hoAux = ResolveSingleHoAuxRig(root.gameObject);
            if (hoAux == null && rig != null && root != rig.transform)
                hoAux = ResolveSingleHoAuxRig(rig);
            if (hoAux == null)
            {
                hoAux = Undo.AddComponent<HoAuxRig>(root.gameObject);
            }
            else
            {
                if (string.IsNullOrEmpty(hoAux.SourceArmature) && CountHoAuxOperations(hoAux) > 0)
                {
                    throw new InvalidOperationException(
                        $"约束根物体「{root.name}」上已有手动配置的 HoAuxRig。" +
                        "请先清空或移除该组件，导入器不会覆盖手动操作。");
                }
                if (!string.IsNullOrEmpty(hoAux.SourceArmature) &&
                    !string.Equals(hoAux.SourceArmature, config.armatureName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"约束根物体上的 HoAuxRig 属于另一骨架：{hoAux.SourceArmature}。");
                }
                Undo.RecordObject(hoAux, "重新导入 HoAux Rig");
            }

            string schemaVersion = config.schemaVersion.ToString();
            hoAux.RigRoot = root;
            hoAux.RemoveOperationsFromSource(config.armatureName, config.exportTime, schemaVersion);
            hoAux.SourceArmature = config.armatureName;
            hoAux.ExportTime = config.exportTime;
            hoAux.ExporterVersion = schemaVersion;

            int count = 0;
            foreach (ConstraintPlanEntry entry in plan)
            {
                if (entry.kind != ConstraintPlanKind.Parent &&
                    entry.kind != ConstraintPlanKind.Twist &&
                    entry.kind != ConstraintPlanKind.Fan)
                    continue;
                if (!string.IsNullOrEmpty(entry.hoAuxUnsupportedReason))
                    continue;
                if (!TryResolvePlanTransforms(transformMap, entry, out Transform bone, out Transform target))
                    continue;

                if (ConfigureHoAuxOperation(hoAux, bone, target, entry))
                    count++;
            }

            hoAux.CaptureBindPose();
            EditorUtility.SetDirty(hoAux);
            if (PrefabUtility.IsPartOfPrefabInstance(hoAux))
                PrefabUtility.RecordPrefabInstancePropertyModifications(hoAux);
            return count;
        }

        /// <summary>
        /// VRC 约束：每条约束独占一个独立空物体，全部摊在约束根物体底下。
        ///
        /// 空物体只是"挂点"：约束的 TargetTransform 指回被驱动的那根骨骼，
        /// 所以真正被驱动的是骨骼，而不是这个空物体。这样约束组件集中在一处
        /// （挂在根物体下面便于整体搬运排查），骨骼该被驱动的照样被驱动。
        ///
        /// 一条约束一个物体是必需的：同一类型的两条约束放在同一个 GameObject 上，
        /// 引擎无法可靠处理对它们的动画（SDK 自动转换时会为此专门发警告）。
        /// </summary>
        private static int ApplyVrcConstraints(
            ConstraintConfig config,
            GameObject rig,
            List<ConstraintPlanEntry> plan,
            Dictionary<string, Transform> transformMap,
            Transform explicitRoot)
        {
            Transform root = ResolveConstraintRoot(rig, explicitRoot);
            if (root == null)
                return 0;

            // 只认"上一次 VRC 挂点"留下的同名子物体。带标记但角色不是 VrcConstraintObject
            // （例如某根骨上的标记被挪到了这里）不能复用：复用会把它的角色改写成挂点，
            // 之后清理时就会连它整棵子树一起删掉。
            var existingHosts = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (Transform child in root)
            {
                if (child == null || existingHosts.ContainsKey(child.name))
                    continue;
                HoImportedConstraintMarker marker = child.GetComponent<HoImportedConstraintMarker>();
                if (marker != null && marker.hostRole == ConstraintHostRole.VrcConstraintObject)
                    existingHosts[child.name] = child.gameObject;
            }

            int count = 0;
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Count; index++)
            {
                ConstraintPlanEntry entry = plan[index];
                if (entry.kind == ConstraintPlanKind.Unknown)
                    continue;
                if (!TryResolvePlanTransforms(transformMap, entry, out Transform bone, out Transform target))
                    continue;

                bool parent = entry.kind == ConstraintPlanKind.Parent;
                Axis axes = entry.kind == ConstraintPlanKind.Twist
                    ? Axis.Y
                    : Axis.X | Axis.Y | Axis.Z;
                Type constraintType = HoVrcConstraintBridge.ResolveConstraintType(parent);
                if (constraintType == null)
                {
                    Debug.LogWarning(
                        $"HoFBX: 本工程缺少 {entry.kind} 对应的 VRChat 约束类型，已跳过 " +
                        $"{entry.ownerBone} -> {entry.targetBone}。");
                    continue;
                }

                string hostName = UniqueVrcHostName(
                    root,
                    VrcHostName(entry, parent ? "Parent" : axes == Axis.Y ? "RotationY" : "RotationXYZ"),
                    usedNames);
                GameObject host = ResolveVrcHost(root, hostName, existingHosts, out bool createdHost);

                // 复用来的挂点上若已经有约束（上次导入没清干净、或用户手工加的），
                // 再加一条就变成"同物体同类型两条"—— SDK 明确不支持这种被动画的情况。
                if (!createdHost && HoVrcConstraintBridge.GetConstraintComponents(host).Count > 0)
                {
                    Debug.LogWarning(
                        $"HoFBX: 挂点「{host.name}」上已有 VRChat 约束，本次会在同一物体上追加一条。" +
                        "同一物体上同类型的多条约束，引擎无法可靠处理对它们的动画；" +
                        "建议先清掉该挂点上的旧约束。");
                }

                Component constraint = HoVrcConstraintBridge.AddConstraint(
                    host,
                    constraintType,
                    bone,
                    new List<Transform> { target },
                    entry.weight,
                    out string error);
                if (constraint == null)
                {
                    Debug.LogWarning(
                        $"HoFBX: 在「{root.name}/{hostName}」上创建 VRChat 约束失败：{error}；" +
                        $"已跳过 {entry.ownerBone} -> {entry.targetBone}。");
                    // 只回收本次新建的挂点；复用来的物体在导入前就存在，不能连它一起删。
                    if (createdHost)
                        Undo.DestroyObjectImmediate(host);
                    continue;
                }

                // 轴掩码必须在最后设：改完属性要再同步一次，否则可能同步不回约束。
                if (!parent && !ConfigureVrcRotationAxes(constraint, axes))
                {
                    Debug.LogWarning(
                        $"HoFBX: 无法写入 {constraintType.Name} 的 AffectsRotationX/Y/Z，已跳过 " +
                        $"{entry.ownerBone} -> {entry.targetBone}。");
                    Undo.DestroyObjectImmediate(constraint);
                    if (createdHost)
                        Undo.DestroyObjectImmediate(host);
                    continue;
                }

                HoImportedConstraintMarker marker = host.GetComponent<HoImportedConstraintMarker>();
                if (marker == null)
                    marker = Undo.AddComponent<HoImportedConstraintMarker>(host);
                else
                    marker.ClearManaged();
                marker.SetMetadata(
                    config.armatureName,
                    config.exportTime,
                    config.schemaVersion.ToString());
                marker.Register(constraint);
                marker.SetHost(
                    ConstraintHostRole.VrcConstraintObject,
                    HoAuxRig.GetRelativePath(root, host.transform));

                EditorUtility.SetDirty(marker);
                EditorUtility.SetDirty(constraint);
                HoVrcConstraintBridge.RecordPrefabOverrides(constraint);
                count++;
            }
            return count;
        }

        private static string UniqueVrcHostName(
            Transform root,
            string baseName,
            HashSet<string> used)
        {
            if (used.Add(baseName) && IsNameFree(root, baseName))
                return baseName;
            for (int suffix = 2; ; suffix++)
            {
                string candidate = baseName + " (" + suffix + ")";
                if (used.Add(candidate) && IsNameFree(root, candidate))
                    return candidate;
            }
        }

        /// <summary>根物体下没有同名子物体时才算名字可用，避免和手工物体撞名。</summary>
        private static bool IsNameFree(Transform root, string name)
        {
            if (root == null)
                return true;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (child != null && string.Equals(child.name, name, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static string VrcHostName(ConstraintPlanEntry entry, string kind)
        {
            string owner = string.IsNullOrEmpty(entry.ownerBone) ? "Owner" : entry.ownerBone;
            return "[VRC] " + kind + " " + owner;
        }

        private static GameObject ResolveVrcHost(
            Transform root,
            string hostName,
            Dictionary<string, GameObject> existingHosts,
            out bool created)
        {
            if (existingHosts.TryGetValue(hostName, out GameObject existing) && existing != null)
            {
                created = false;
                return existing;
            }

            var host = new GameObject(hostName);
            Undo.RegisterCreatedObjectUndo(host, "创建 VRChat 约束空物体");
            host.transform.SetParent(root, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;
            existingHosts[hostName] = host;
            created = true;
            return host;
        }

        /// <summary>
        /// 把轴掩码写进 VRC 约束的 Affects* 字段。这些字段在 6 个具体类型上同名，
        /// 所以按名字反射即可，不需要引用 SDK 程序集。
        /// 三个字段全部写入成功才返回 true —— 半写会用错轴。
        /// </summary>
        private static bool ConfigureVrcRotationAxes(Component constraint, Axis axes)
        {
            if (!SetMember(constraint, "AffectsRotationX", (axes & Axis.X) != 0) ||
                !SetMember(constraint, "AffectsRotationY", (axes & Axis.Y) != 0) ||
                !SetMember(constraint, "AffectsRotationZ", (axes & Axis.Z) != 0))
            {
                return false;
            }

            HoVrcConstraintBridge.ApplyConfigurationChanges(constraint);
            HoVrcConstraintBridge.RecordPrefabOverrides(constraint);
            return true;
        }

        private static bool SetMember(Component target, string name, object value)
        {
            Type type = target.GetType();
            var field = type.GetField(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }

            var property = type.GetProperty(
                name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }

            return false;
        }

        private static bool TryResolvePlanTransforms(
            Dictionary<string, Transform> transformMap,
            ConstraintPlanEntry entry,
            out Transform bone,
            out Transform target)
        {
            if (TryResolveTransform(transformMap, entry.ownerBone, out bone) &&
                TryResolveTransform(transformMap, entry.targetBone, out target))
            {
                return true;
            }

            Debug.LogWarning(
                $"HoFBX: 无法解析约束骨骼 {entry.ownerBone} -> {entry.targetBone}，已跳过。");
            bone = null;
            target = null;
            return false;
        }

        /// <summary>约束根物体：显式指定优先，留空回落到目标骨架自己。</summary>
        private static Transform ResolveConstraintRoot(GameObject rig, Transform explicitRoot)
        {
            if (explicitRoot != null)
                return explicitRoot;
            return rig == null ? null : rig.transform;
        }

        private static bool ContainsHoAuxOperations(List<ConstraintPlanEntry> plan)
        {
            foreach (ConstraintPlanEntry entry in plan)
            {
                if (entry.kind == ConstraintPlanKind.Parent ||
                    entry.kind == ConstraintPlanKind.Twist || entry.kind == ConstraintPlanKind.Fan)
                {
                    if (!string.IsNullOrEmpty(entry.hoAuxUnsupportedReason))
                        continue;
                    return true;
                }
            }
            return false;
        }

        private static Dictionary<string, Transform> BuildTransformMap(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                string path = HoAuxRig.GetRelativePath(root, transform);
                result[path] = transform;
                if (duplicateNames.Contains(transform.name))
                    continue;
                if (result.TryGetValue(transform.name, out Transform existing) && existing != transform)
                {
                    result.Remove(transform.name);
                    duplicateNames.Add(transform.name);
                }
                else
                {
                    result[transform.name] = transform;
                }
            }
            return result;
        }

        private static bool TryResolveTransform(
            Dictionary<string, Transform> transformMap,
            string pathOrName,
            out Transform transform)
        {
            transform = null;
            return !string.IsNullOrEmpty(pathOrName) && transformMap.TryGetValue(pathOrName, out transform);
        }

        private static bool HasUnmanagedStandardConstraint(
            Transform owner,
            ConstraintPlanKind kind)
        {
            if (owner == null)
                return false;
            HoImportedConstraintMarker marker = owner.GetComponent<HoImportedConstraintMarker>();
            if (kind == ConstraintPlanKind.Parent)
            {
                foreach (ParentConstraint constraint in owner.GetComponents<ParentConstraint>())
                {
                    if (marker == null || !marker.Manages(constraint))
                        return true;
                }
                return false;
            }
            if (kind == ConstraintPlanKind.Twist || kind == ConstraintPlanKind.Fan)
            {
                foreach (RotationConstraint constraint in owner.GetComponents<RotationConstraint>())
                {
                    if (marker == null || !marker.Manages(constraint))
                        return true;
                }
            }
            return false;
        }

        private static bool ConfigureHoAuxOperation(
            HoAuxRig rig,
            Transform owner,
            Transform target,
            ConstraintPlanEntry entry)
        {
            HoAuxRig.OperationType type;
            if (entry.kind == ConstraintPlanKind.Parent) type = HoAuxRig.OperationType.Parent;
            else if (entry.kind == ConstraintPlanKind.Twist) type = HoAuxRig.OperationType.Twist;
            else if (entry.kind == ConstraintPlanKind.Fan) type = HoAuxRig.OperationType.Fan;
            else return false;

            HoAuxRig.Operation operation = rig.AddOperation(type, owner, target, entry.weight);
            operation.sourceBone = entry.sourceBone ?? string.Empty;
            operation.maintainOffset = entry.maintainOffset;
            BlenderConstraintParameters copyParameters = entry.copyRotation == null
                ? null
                : entry.copyRotation.parameters;
            operation.sourceSpace = ResolveHoAuxSpace(
                copyParameters == null ? null : copyParameters.owner_space);
            operation.targetSpace = ResolveHoAuxSpace(
                copyParameters == null ? null : copyParameters.target_space);

            // HoAux Twist/Fan 使用完整四元数；标准模式的 Twist 才退化为 Y 轴。
            operation.useX = true;
            operation.useY = true;
            operation.useZ = true;

            if (entry.kind == ConstraintPlanKind.Twist)
            {
                BlenderConstraintInfo stretch = entry.stretchTo;
                BlenderConstraintParameters stretchParameters = stretch == null
                    ? null
                    : stretch.parameters;
                operation.stretchEnabled = stretch != null;
                operation.stretchWeight = Mathf.Clamp01(
                    ConstraintInfluence(stretch, 1.0f));
                operation.stretchHeadTail = stretchParameters == null ? 0.0f : stretchParameters.head_tail;
                operation.restLength = stretchParameters == null ? 0.0f : stretchParameters.rest_length;
                operation.stretchSourceSpace = ResolveHoAuxSpace(
                    stretchParameters == null ? null : stretchParameters.owner_space);
                operation.stretchTargetSpace = ResolveHoAuxSpace(
                    stretchParameters == null ? null : stretchParameters.target_space);
                operation.volume = stretchParameters == null || string.IsNullOrEmpty(stretchParameters.volume)
                    ? "NO_VOLUME"
                    : stretchParameters.volume;
                operation.keepAxis = stretchParameters == null || string.IsNullOrEmpty(stretchParameters.keep_axis)
                    ? "SWING_Y"
                    : stretchParameters.keep_axis;
            }
            else
            {
                operation.stretchEnabled = false;
            }
            return true;
        }

        private static HoAuxRig.Space ResolveHoAuxSpace(string value)
        {
            if (string.Equals(value, "LOCAL_OWNER_ORIENT", StringComparison.OrdinalIgnoreCase))
                return HoAuxRig.Space.LocalOwnerOrient;
            if (string.Equals(value, "LOCAL_WITH_PARENT", StringComparison.OrdinalIgnoreCase))
                return HoAuxRig.Space.LocalWithParent;
            if (string.Equals(value, "LOCAL", StringComparison.OrdinalIgnoreCase))
                return HoAuxRig.Space.Local;
            if (string.Equals(value, "POSE", StringComparison.OrdinalIgnoreCase))
                return HoAuxRig.Space.Pose;
            if (string.Equals(value, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                return HoAuxRig.Space.Custom;
            return HoAuxRig.Space.World;
        }

        private static bool ConfigureStandardOperation(
            Transform bone,
            Transform target,
            ConstraintPlanEntry entry,
            ConstraintConfig config)
        {
            // 标准模式保留 VRC 兼容退化：Twist 只取 Y 轴，Fan 只接受 XYZ 的世界对世界语义。
            if (entry.kind == ConstraintPlanKind.Parent)
                return ConfigureParent(bone, target, entry.weight, config);
            if (entry.kind == ConstraintPlanKind.Twist)
                return ConfigureRotation(bone, target, entry.weight, config, Axis.Y);
            if (entry.kind == ConstraintPlanKind.Fan)
            {
                return ConfigureRotation(
                    bone,
                    target,
                    entry.weight,
                    config,
                    Axis.X | Axis.Y | Axis.Z);
            }
            return false;
        }

        private static int ClearImportedStandardConstraints(GameObject rig)
        {
            int count = 0;
            foreach (HoImportedConstraintMarker marker in
                     rig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
            {
                if (marker.hostRole == ConstraintHostRole.VrcConstraintObject)
                    continue;

                foreach (Component constraint in new List<Component>(marker.GetLiveConstraints()))
                {
                    Undo.DestroyObjectImmediate(constraint);
                    count++;
                }
                Undo.DestroyObjectImmediate(marker);
            }

            return count;
        }

        /// <summary>
        /// 回收上一次 VRC 模式留下的空物体（连标记一起）。
        /// 返回回收的物体数；用户手工放在那里的物体不会被碰。
        /// </summary>
        private static int RemoveImportedVrcConstraintObjects(GameObject rig, Transform explicitRoot)
        {
            var hosts = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (HoImportedConstraintMarker marker in CollectImportedMarkers(rig, explicitRoot))
            {
                if (marker.hostRole != ConstraintHostRole.VrcConstraintObject)
                    continue;
                if (rig != null && marker.gameObject == rig)
                    continue;
                if (explicitRoot != null && marker.gameObject == explicitRoot.gameObject)
                    continue;
                if (seen.Add(marker.gameObject.GetInstanceID()))
                    hosts.Add(marker.gameObject);
            }

            foreach (GameObject host in hosts)
            {
                if (host == null)
                    continue;
                foreach (Component constraint in HoVrcConstraintBridge.GetConstraintComponents(host))
                    Undo.DestroyObjectImmediate(constraint);
                Undo.DestroyObjectImmediate(host);
            }
            return hosts.Count;
        }

        /// <summary>
        /// 找出"以 host 为宿主或根"的唯一 HoAuxRig。
        ///
        /// 两种情况都算命中：组件就挂在 host 上；或者组件挂在别处但绑定到了 host。
        /// 后者是老数据的形态——组件在骨架根上，RigRoot 为空即"根就是我自己"。
        /// </summary>
        private static HoAuxRig ResolveSingleHoAuxRig(GameObject host)
        {
            if (host == null)
                return null;

            var candidates = new List<HoAuxRig>();
            foreach (HoAuxRig candidate in host.GetComponentsInChildren<HoAuxRig>(true))
            {
                if (candidate == null || candidates.Contains(candidate))
                    continue;
                if (candidate.transform == host.transform ||
                    candidate.ExplicitRigRoot == host.transform)
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0)
                return null;
            if (candidates.Count > 1)
            {
                throw new InvalidOperationException(
                    "约束根物体「" + host.name + "」的层级里存在多个 HoAuxRig；" +
                    "请只保留一个组件。\n导入中控会让这一个组件统一控制骨架内的全部 Rig 约束。");
            }
            return candidates[0];
        }

        /// <summary>
        /// 回收"约束根物体/骨架"这个范围内由导入器写进去的全部 HoAuxRig。
        ///
        /// 刻意不按 armatureName 过滤：在"约束骨架"下拉里换了候选、或 Blender 里改了骨架名
        /// 再重导时，旧组件必须照样回收，否则它会和新落下的形态同时驱动同一批骨骼。
        /// 手工配置（SourceArmature 为空）的组件一律不动。
        /// </summary>
        private static int RemoveImportedHoAuxRigs(GameObject rig, Transform explicitRoot)
        {
            List<HoAuxRig> components = CollectImportedHoAuxRigs(rig, explicitRoot);
            for (int index = 0; index < components.Count; index++)
            {
                if (components[index] != null)
                    Undo.DestroyObjectImmediate(components[index]);
            }
            return components.Count;
        }

        /// <summary>
        /// 范围内全部"由导入器写入"的 HoAuxRig（SourceArmature 非空）。
        /// 范围 = 骨架子树 + 约束根物体子树 + 从根物体上溯到预制件根的那条链。
        /// </summary>
        private static List<HoAuxRig> CollectImportedHoAuxRigs(GameObject rig, Transform explicitRoot)
        {
            var candidates = new List<HoAuxRig>();
            var seen = new HashSet<int>();

            if (rig != null)
            {
                foreach (HoAuxRig hoAux in rig.GetComponentsInChildren<HoAuxRig>(true))
                {
                    if (hoAux != null && seen.Add(hoAux.GetInstanceID()))
                        candidates.Add(hoAux);
                }
            }

            if (explicitRoot != null)
            {
                foreach (HoAuxRig hoAux in explicitRoot.GetComponentsInChildren<HoAuxRig>(true))
                {
                    if (hoAux != null && seen.Add(hoAux.GetInstanceID()))
                        candidates.Add(hoAux);
                }

                GameObject boundary = PrefabUtility.GetOutermostPrefabInstanceRoot(explicitRoot);
                Transform cursor = explicitRoot;
                while (cursor != null)
                {
                    HoAuxRig hoAux = cursor.GetComponent<HoAuxRig>();
                    if (hoAux != null && seen.Add(hoAux.GetInstanceID()))
                        candidates.Add(hoAux);
                    if (boundary != null && cursor.gameObject == boundary)
                        break;
                    cursor = cursor.parent;
                }
            }

            var imported = new List<HoAuxRig>();
            for (int index = 0; index < candidates.Count; index++)
            {
                if (candidates[index] != null && !string.IsNullOrEmpty(candidates[index].SourceArmature))
                    imported.Add(candidates[index]);
            }
            return imported;
        }

        private static HoAuxRig FindImportedHoAuxRig(
            GameObject rig,
            Transform explicitRoot,
            string armatureName)
        {
            List<HoAuxRig> candidates = CollectImportedHoAuxRigs(rig, explicitRoot);
            for (int index = 0; index < candidates.Count; index++)
            {
                HoAuxRig candidate = candidates[index];
                if (!string.IsNullOrEmpty(armatureName) &&
                    !string.Equals(candidate.SourceArmature, armatureName, StringComparison.Ordinal))
                    continue;
                return candidate;
            }
            return null;
        }

        private static T GetManaged<T>(Transform bone, ConstraintConfig config) where T : Component, IConstraint
        {
            HoImportedConstraintMarker marker = bone.GetComponent<HoImportedConstraintMarker>();
            T[] existing = bone.GetComponents<T>();
            foreach (T item in existing)
            {
                if (marker != null && marker.Manages(item))
                {
                    PrepareConstraintForReconfigure(item);
                    IConstraint constraint = item;
                    for (int i = constraint.sourceCount - 1; i >= 0; i--)
                        constraint.RemoveSource(i);
                    return item;
                }
            }

            if (existing.Length > 0)
                return null;

            T created = Undo.AddComponent<T>(bone.gameObject);
            if (marker == null)
                marker = Undo.AddComponent<HoImportedConstraintMarker>(bone.gameObject);
            marker.SetMetadata(
                config.armatureName,
                config.exportTime,
                config.schemaVersion.ToString());
            marker.Register(created);
            EditorUtility.SetDirty(marker);
            return created;
        }

        private static void PrepareConstraintForReconfigure(Component constraint)
        {
            if (!(constraint is IConstraint standardConstraint))
                return;

            // Removing sources from an active/locked constraint can leave its
            // native offset state stale. Reconfiguration must start from the
            // same inactive/unlocked state as a newly-created component.
            standardConstraint.constraintActive = false;
            standardConstraint.locked = false;
        }

        private static void ActivateAndLockConstraint(IConstraint constraint)
        {
            Component component = constraint as Component;
            // The public assignments are the fallback for Unity versions that
            // do not expose the internal interface implementation to
            // reflection. They also make the serialized state unambiguous
            // after the native helper has preserved the current offset.
            if (component != null)
                TryInvokeActivateAndPreserveOffset(component);
            constraint.constraintActive = true;
            constraint.locked = true;

            if (component == null)
                return;

            EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(component.transform);
            if (PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (component.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        private static bool TryInvokeActivateAndPreserveOffset(Component constraint)
        {
            Type constraintType = constraint.GetType();
            MethodInfo method;
            if (!ActivateConstraintMethods.TryGetValue(constraintType, out method))
            {
                method = FindConstraintInternalMethod(constraintType);
                ActivateConstraintMethods.Add(constraintType, method);
            }

            if (method == null)
            {
                if (MissingActivateConstraintMethods.Add(constraintType))
                {
                    Debug.LogWarning(
                        "HoFBX: Unity constraint activation helper was not found for " +
                        constraintType.FullName + ". Falling back to public state properties.");
                }
                return false;
            }

            try
            {
                method.Invoke(constraint, null);
                return true;
            }
            catch (Exception exception)
            {
                if (MissingActivateConstraintMethods.Add(constraintType))
                {
                    Debug.LogWarning(
                        "HoFBX: Unity constraint activation helper failed for " +
                        constraintType.FullName + ". Falling back to public state properties. " +
                        exception.Message);
                }
                return false;
            }
        }

        private static MethodInfo FindConstraintInternalMethod(Type constraintType)
        {
            foreach (Type interfaceType in constraintType.GetInterfaces())
            {
                if (!string.Equals(
                        interfaceType.FullName,
                        "UnityEngine.Animations.IConstraintInternal",
                        StringComparison.Ordinal))
                    continue;

                InterfaceMapping mapping = constraintType.GetInterfaceMap(interfaceType);
                for (int i = 0; i < mapping.InterfaceMethods.Length; i++)
                {
                    if (string.Equals(
                            mapping.InterfaceMethods[i].Name,
                            "ActivateAndPreserveOffset",
                            StringComparison.Ordinal))
                        return mapping.TargetMethods[i];
                }
            }

            return null;
        }

        private static bool ConfigureRotation(
            Transform bone,
            Transform target,
            float weight,
            ConstraintConfig config,
            Axis axes)
        {
            RotationConstraint constraint = GetManaged<RotationConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = weight;
            constraint.rotationAxis = axes;
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static bool ConfigureParent(
            Transform bone,
            Transform target,
            float weight,
            ConstraintConfig config)
        {
            ParentConstraint constraint = GetManaged<ParentConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = weight;
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static bool ApplyCollections(TextAsset json, GameObject rig)
        {
            HoBoneRenderer renderer = rig.GetComponent<HoBoneRenderer>();
            if (renderer == null)
                renderer = Undo.AddComponent<HoBoneRenderer>(rig);

            renderer.SkeletonRoot = rig.transform;
            renderer.GroupJson = json;
            renderer.RefreshGroupJson();
            EditorUtility.SetDirty(renderer);
            return true;
        }
    }
}
#endif
