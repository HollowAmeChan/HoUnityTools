#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Hollow.HoUnityTools.BoneRendering;
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
        private const string ConstraintKind = "constraints";
        private const string CollectionKind = "collections";

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

        [SerializeField] private GameObject fbxAsset;
        [SerializeField] private string fbxAssetPath;
        [SerializeField] private TextAsset humanoidJson;
        [SerializeField] private TextAsset constraintJson;
        [SerializeField] private TextAsset collectionJson;
        [SerializeField] private GameObject targetRig;
        [SerializeField] private bool applyHumanoid = true;
        [SerializeField] private bool applyConstraints;
        [SerializeField] private bool applyCollections;
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private bool mappingPreviewExpanded;
        [SerializeField] private ToolPage currentPage;

        private GUIStyle panelTitleStyle;
        private GUIStyle panelStatusStyle;
        private GUIStyle primaryButtonStyle;

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
            if (fbxAsset == null && IsFbx(fbxAssetPath))
                fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
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
                GUILayout.Space(2f);
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
                if (GUILayout.Button("安全清除导入约束", GUILayout.Height(28f)))
                    SafeClearImportedConstraints();
                if (GUILayout.Button("清除全部标准约束", GUILayout.Height(28f)))
                    ClearAllStandardConstraints();
                GUILayout.Space(2f);
            }

            GUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPanelHeader("骨架姿态", "Prefab", new Color(0.91f, 0.65f, 0.25f));
                GUILayout.Space(6f);
                if (GUILayout.Button("还原骨架到 Prefab 姿态", GUILayout.Height(28f)))
                    ResetRigToPrefabPose();
                GUILayout.Space(2f);
            }
        }

        private void ImportConstraintsFromUtilityPage()
        {
            if (!ValidateConstraintUtilityInputs(true))
                return;
            if (!EditorUtility.DisplayDialog(
                    "导入约束",
                    "将把约束 JSON 显式应用到目标骨架。用户手工创建的同类型约束不会被覆盖。",
                    "导入",
                    "取消"))
                return;

            try
            {
                int importedCount = ApplyConstraints(constraintJson, targetRig);
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

            if (requireJson && constraintJson == null)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "请先指定约束 JSON。", "确定");
                return false;
            }

            return true;
        }

        private void SafeClearImportedConstraints()
        {
            if (!ValidateConstraintUtilityInputs(false))
                return;

            HoImportedConstraintMarker[] markers =
                targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true);
            if (markers.Length == 0)
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "没有找到工具导入的约束。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "安全清除导入约束",
                    $"将清除 {markers.Length} 个骨骼上的工具约束，用户手工约束不受影响。",
                    "清除",
                    "取消"))
                return;

            int clearedCount = 0;
            foreach (HoImportedConstraintMarker marker in markers)
            {
                foreach (Component constraint in new List<Component>(marker.GetLiveConstraints()))
                {
                    Undo.DestroyObjectImmediate(constraint);
                    clearedCount++;
                }
                Undo.DestroyObjectImmediate(marker);
            }

            EditorUtility.DisplayDialog(
                "HoFBX导入处理",
                $"已安全清除 {clearedCount} 个导入约束。",
                "确定");
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
            foreach (HoImportedConstraintMarker marker in
                     targetRig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
                Undo.DestroyObjectImmediate(marker);

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
            if (panelTitleStyle == null)
            {
                panelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            if (panelStatusStyle == null)
            {
                panelStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            if (primaryButtonStyle == null)
            {
                primaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 36f,
                    fontStyle = FontStyle.Bold,
                    fontSize = 13
                };
            }
        }

        private void ScanAdjacentMetadata()
        {
            if (!IsFbx(fbxAssetPath))
                return;

            humanoidJson = null;
            collectionJson = null;
            constraintJson = null;

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
                        else if (entry.kind == ConstraintKind && constraintJson == null)
                            constraintJson = asset;
                    }
                }
            }

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
                {
                    constraintJson = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (constraintJson != null)
                        break;
                }
            }
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
                        constraintCount = ApplyConstraints(constraintJson, targetRig);
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
                   (applyConstraints ? "- 导入约束 JSON\n" : string.Empty) +
                   (applyCollections ? "- 更新 Bone Renderer 集合\n" : string.Empty) +
                   "\n用户已有的手动配置不会被 AssetPostprocessor 自动覆盖。";
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

        private static int ApplyConstraints(TextAsset configAsset, GameObject rig)
        {
            ConstraintConfig config = JsonUtility.FromJson<ConstraintConfig>(configAsset.text);
            if (config == null || config.bones == null)
                return 0;

            var boneMap = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
                boneMap[bone.name] = bone;

            int count = 0;
            foreach (BoneConstraint boneConfig in config.bones)
            {
                if (boneConfig == null || !boneMap.TryGetValue(boneConfig.boneName, out Transform bone))
                    continue;

                if (boneConfig.constraints == null)
                    continue;

                foreach (ConstraintInfo info in boneConfig.constraints)
                {
                    if (info == null || !boneMap.TryGetValue(info.targetPath, out Transform target))
                        continue;

                    if (info.type == "Rotation")
                        count += ConfigureRotation(bone, target, info, config) ? 1 : 0;
                    else if (info.type == "Location")
                        count += ConfigurePosition(bone, target, info, config) ? 1 : 0;
                    else if (info.type == "Scale")
                        count += ConfigureScale(bone, target, info, config) ? 1 : 0;
                    else if (info.type == "Child")
                        count += ConfigureParent(bone, target, info, config) ? 1 : 0;
                }
            }

            return count;
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
            marker.SetMetadata(config.armatureName, config.exportTime, config.version);
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

        private static bool ConfigureRotation(Transform bone, Transform target, ConstraintInfo info, ConstraintConfig config)
        {
            RotationConstraint constraint = GetManaged<RotationConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = info.weight;
            constraint.rotationAxis = ResolveAxis(info.axes);
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static bool ConfigurePosition(Transform bone, Transform target, ConstraintInfo info, ConstraintConfig config)
        {
            PositionConstraint constraint = GetManaged<PositionConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = info.weight;
            constraint.translationAxis = ResolveAxis(info.axes);
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static bool ConfigureScale(Transform bone, Transform target, ConstraintInfo info, ConstraintConfig config)
        {
            ScaleConstraint constraint = GetManaged<ScaleConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = info.weight;
            constraint.scalingAxis = ResolveAxis(info.axes);
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static bool ConfigureParent(Transform bone, Transform target, ConstraintInfo info, ConstraintConfig config)
        {
            ParentConstraint constraint = GetManaged<ParentConstraint>(bone, config);
            if (constraint == null) return false;
            constraint.AddSource(new ConstraintSource { sourceTransform = target, weight = 1f });
            constraint.weight = info.weight;
            ActivateAndLockConstraint(constraint);
            return true;
        }

        private static Axis ResolveAxis(AxesInfo axes)
        {
            Axis result = Axis.None;
            if (axes != null && axes.x) result |= Axis.X;
            if (axes != null && axes.y) result |= Axis.Y;
            if (axes != null && axes.z) result |= Axis.Z;
            return result;
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
