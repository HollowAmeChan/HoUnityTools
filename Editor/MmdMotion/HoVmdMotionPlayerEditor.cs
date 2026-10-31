#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdMotionPlayer))]
    internal sealed class HoVmdMotionPlayerEditor : UnityEditor.Editor
    {
        private const int RowsPerPage = 24;
        private bool showBoneBindings;
        private bool showMorphBindings;
        private bool showIkBindings;
        private HoVmdMorphAuditResult audit;
        private string report;
        private HoVmdAnimation countedAnimation;
        private int boneTrackCount;
        private int morphTrackCount;
        private int ikTrackCount;
        private bool mappingSummaryDirty = true;
        private int unmappedBoneCount;
        private int unmappedMorphCount;
        private int unmappedIkCount;
        private int bonePage;
        private int morphPage;
        private int ikPage;
        private string boneSearch = string.Empty;
        private string morphSearch = string.Empty;
        private string ikSearch = string.Empty;

        private HoVmdMotionPlayer Player => (HoVmdMotionPlayer)target;
        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("VMD 动作播放", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Field("animation", "VMD 动画");
            Field("modelRoot", "角色根", "包括主骨架、脸部网格和 IK 控制物体的角色根。");
            Field("skeletonRoot", "骨骼搜索根", "通常指定实际驱动角色的主骨架，避免扫描到衣装等附加骨架。留空时使用角色根。");
            bool scanInputsChanged = EditorGUI.EndChangeCheck();
            if (scanInputsChanged)
            {
                serializedObject.ApplyModifiedProperties();
                ScanAndMatch();
                serializedObject.Update();
            }
            Field("frameRate", "帧率");
            Field("playOnEnable", "运行时自动播放");
            Field("loop", "循环");
            Field("applyBones", "应用骨骼轨道");
            Field("applyMorphs", "应用表情轨道");
            Field("applyIkToggles", "应用 VMD 的 IK 开关");
            Field("currentFrame", "当前帧");
            using (new EditorGUI.DisabledScope(!Application.isPlaying)) Field("playing", "正在播放");

            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Player.Playing ? "暂停" : "播放"))
                        Player.Playing = !Player.Playing;
                    if (GUILayout.Button("从头播放"))
                    {
                        Player.CurrentFrame = 0.0f;
                        Player.Playing = true;
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("运行时在 Play 模式播放；配置不会生成外部映射资产。", EditorStyles.miniLabel);
            }

            if (Player.Animation != null && Player.Animation.boneFrames.Count == 0 &&
                Player.Animation.cameraFrames.Count > 0)
                EditorGUILayout.HelpBox("这是镜头 VMD，请在 Camera 上使用 VMD Camera Player。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(Player.Animation == null || Player.ModelRoot == null))
            {
                if (GUILayout.Button("扫描 VMD 轨道并匹配角色"))
                {
                    serializedObject.ApplyModifiedProperties();
                    ScanAndMatch();
                    serializedObject.Update();
                }
            }

            if (Player.Animation != null)
                DrawTrackSummary();

            EditorGUILayout.Space(4.0f);
            showBoneBindings = EditorGUILayout.Foldout(showBoneBindings, "骨骼轨道映射", true);
            bool mappingChanged = false;
            if (showBoneBindings)
            {
                EditorGUI.BeginChangeCheck();
                DrawBoneMappings();
                mappingChanged |= EditorGUI.EndChangeCheck();
            }
            showMorphBindings = EditorGUILayout.Foldout(showMorphBindings, "表情轨道映射", true);
            if (showMorphBindings)
            {
                EditorGUI.BeginChangeCheck();
                DrawMorphMappings();
                mappingChanged |= EditorGUI.EndChangeCheck();
            }
            showIkBindings = EditorGUILayout.Foldout(showIkBindings, "VMD IK 开关目标", true);
            if (showIkBindings)
            {
                EditorGUI.BeginChangeCheck();
                DrawIkMappings();
                mappingChanged |= EditorGUI.EndChangeCheck();
            }

            using (new EditorGUI.DisabledScope(Player.Animation == null || Player.ModelRoot == null))
            {
                if (GUILayout.Button("检查角色表情依赖"))
                    report = HoVmdInspectorGUI.WriteAudit(Player.Animation, Player.ModelRoot.gameObject,
                        Player.BindingProfile, out audit, Player.MorphBindings);
            }
            HoVmdInspectorGUI.DrawAudit(audit, report, localMappings: true);
            serializedObject.ApplyModifiedProperties();
            if (mappingChanged) mappingSummaryDirty = true;
        }

        private void DrawTrackSummary()
        {
            EnsureTrackCounts();
            if (mappingSummaryDirty)
            {
                unmappedBoneCount = Player.BoneBindings.Count(x => x == null || x.target == null);
                unmappedMorphCount = Player.MorphBindings.Count(x => x == null || x.renderer == null || x.blendShapeIndex < 0);
                unmappedIkCount = Player.IkToggleBindings.Count(x => x == null || x.targetObject == null);
                mappingSummaryDirty = false;
            }
            EditorGUILayout.LabelField(
                $"VMD 轨道：骨骼 {boneTrackCount}，表情 {morphTrackCount}，IK 开关 {ikTrackCount}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"待处理：骨骼 {unmappedBoneCount}，表情 {unmappedMorphCount}，IK 开关 {unmappedIkCount}",
                EditorStyles.miniLabel);
        }

        private void EnsureTrackCounts()
        {
            if (countedAnimation == Player.Animation) return;
            countedAnimation = Player.Animation;
            boneTrackCount = UniqueBoneTracks().Count;
            morphTrackCount = UniqueMorphTracks().Count;
            ikTrackCount = UniqueIkTracks().Count;
        }

        private void DrawBoneMappings()
        {
            SerializedProperty array = P("boneBindings");
            boneSearch = DrawSearch(boneSearch, "骨骼名筛选");
            List<int> indices = FilterIndices(Player.BoneBindings, x => x?.vmdName,
                x => x != null && x.target != null, boneSearch);
            DrawPageControls(ref bonePage, indices.Count);
            DrawBindingRows(array, indices, ref bonePage, "target");
        }

        private void DrawMorphMappings()
        {
            SerializedProperty array = P("morphBindings");
            morphSearch = DrawSearch(morphSearch, "表情名筛选");
            List<int> indices = FilterIndices(Player.MorphBindings, x => x?.vmdName,
                x => x != null && x.renderer != null && x.blendShapeIndex >= 0, morphSearch);
            DrawPageControls(ref morphPage, indices.Count);
            DrawMorphRows(array, indices, ref morphPage);
        }

        private void DrawIkMappings()
        {
            SerializedProperty array = P("ikToggleBindings");
            ikSearch = DrawSearch(ikSearch, "IK 名称筛选");
            List<int> indices = FilterIndices(Player.IkToggleBindings, x => x?.vmdName,
                x => x != null && x.targetObject != null, ikSearch);
            DrawPageControls(ref ikPage, indices.Count);
            DrawBindingRows(array, indices, ref ikPage, "targetObject");
        }

        private string DrawSearch(string value, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(100));
                return EditorGUILayout.TextField(value, EditorStyles.toolbarSearchField);
            }
        }

        private static List<int> FilterIndices<T>(IList<T> items, Func<T, string> sourceName,
            Func<T, bool> isMapped, string filter)
        {
            var indices = new List<int>();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    string source = sourceName(items[i]);
                    bool mapped = isMapped(items[i]);
                    if ((pass == 0) == mapped) continue;
                    if (!string.IsNullOrEmpty(filter) &&
                        (source == null || source.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    indices.Add(i);
                }
            }
            return indices;
        }

        private static void DrawPageControls(ref int page, int count)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(count / (float)RowsPerPage));
            page = Mathf.Clamp(page, 0, pageCount - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page <= 0))
                    if (GUILayout.Button("上一页", GUILayout.Width(64))) page--;
                EditorGUILayout.LabelField(count == 0 ? "0 项" : $"{page + 1} / {pageCount} 页 · {count} 项");
                using (new EditorGUI.DisabledScope(page >= pageCount - 1))
                    if (GUILayout.Button("下一页", GUILayout.Width(64))) page++;
            }
        }

        private static void DrawBindingRows(SerializedProperty array, List<int> indices, ref int page,
            string targetField)
        {
            int start = page * RowsPerPage;
            int end = Mathf.Min(start + RowsPerPage, indices.Count);
            for (int row = start; row < end; row++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(indices[row]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(item.FindPropertyRelative("vmdName").stringValue, GUILayout.Width(180));
                    SerializedProperty target = item.FindPropertyRelative(targetField);
                    EditorGUILayout.PropertyField(target, GUIContent.none);
                }
            }
        }

        private static void DrawMorphRows(SerializedProperty array, List<int> indices, ref int page)
        {
            int start = page * RowsPerPage;
            int end = Mathf.Min(start + RowsPerPage, indices.Count);
            for (int row = start; row < end; row++)
            {
                SerializedProperty item = array.GetArrayElementAtIndex(indices[row]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(item.FindPropertyRelative("vmdName").stringValue, GUILayout.Width(180));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("renderer"), GUIContent.none);
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("blendShapeIndex"), GUIContent.none, GUILayout.Width(55));
                }
            }
        }

        private void ScanAndMatch()
        {
            Undo.RecordObject(Player, "扫描并匹配 VMD 轨道");
            Transform root = Player.ModelRoot;
            Transform skeleton = Player.SkeletonRoot;
            if (root == null || skeleton == null || Player.Animation == null) return;

            var existingBones = Player.BoneBindings.Where(x => x != null && !string.IsNullOrEmpty(x.vmdName))
                .GroupBy(x => x.vmdName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var existingMorphs = Player.MorphBindings.Where(x => x != null && !string.IsNullOrEmpty(x.vmdName))
                .GroupBy(x => x.vmdName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
            var existingIk = Player.IkToggleBindings.Where(x => x != null && !string.IsNullOrEmpty(x.vmdName))
                .GroupBy(x => x.vmdName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

            Transform[] transforms = skeleton.GetComponentsInChildren<Transform>(true);
            var exactBones = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            var normalizedBones = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            foreach (Transform bone in transforms)
            {
                Add(exactBones, bone.name, bone);
                Add(normalizedBones, Normalize(bone.name), bone);
            }

            Animator animator = FindHumanoidAnimator(skeleton, root);
            HoIkConstraint[] constraints = root.GetComponentsInChildren<HoIkConstraint>(true);
            var controlsByName = new Dictionary<string, List<HoIkConstraint>>(StringComparer.Ordinal);
            foreach (HoIkConstraint constraint in constraints)
                if (constraint != null) Add(controlsByName, Normalize(constraint.IkName), constraint);
            var ikTrackNames = new HashSet<string>(UniqueIkTracks().Select(Normalize), StringComparer.Ordinal);

            Player.BoneBindings.Clear();
            foreach (string source in UniqueBoneTracks())
            {
                Transform mapped = existingBones.TryGetValue(source, out var existing) ? existing.target : null;
                bool isIkTrack = ikTrackNames.Contains(Normalize(source)) || controlsByName.ContainsKey(Normalize(source));
                if (mapped == null && isIkTrack) mapped = FindIkTarget(controlsByName, source);
                if (mapped == null) mapped = FindHumanoidBone(animator, source);
                if (mapped == null) mapped = FindIkTarget(controlsByName, source);
                if (mapped == null) mapped = FindNamedBone(exactBones, normalizedBones, source);
                Player.BoneBindings.Add(new HoVmdMotionPlayer.BoneBinding { vmdName = source, target = mapped });
            }

            var shapes = new Dictionary<string, List<HoVmdMotionPlayer.MorphBinding>>(StringComparer.Ordinal);
            var normalizedShapes = new Dictionary<string, List<HoVmdMotionPlayer.MorphBinding>>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    string shapeName = renderer.sharedMesh.GetBlendShapeName(i);
                    var candidate = new HoVmdMotionPlayer.MorphBinding
                    { vmdName = shapeName, renderer = renderer, blendShapeIndex = i };
                    Add(shapes, shapeName, candidate);
                    Add(normalizedShapes, Normalize(shapeName), candidate);
                }
            }

            Player.MorphBindings.Clear();
            foreach (string source in UniqueMorphTracks())
            {
                var mapped = existingMorphs.TryGetValue(source, out var existing)
                    ? existing.Where(x => x.renderer != null && x.blendShapeIndex >= 0).ToList()
                    : new List<HoVmdMotionPlayer.MorphBinding>();
                if (mapped.Count == 0 && shapes.TryGetValue(source, out var exactMatches))
                    mapped.AddRange(exactMatches);
                if (mapped.Count == 0)
                {
                    HoVmdMotionPlayer.MorphBinding normalized = Unique(normalizedShapes, Normalize(source));
                    if (normalized != null) mapped.Add(normalized);
                }
                if (mapped.Count == 0)
                    Player.MorphBindings.Add(new HoVmdMotionPlayer.MorphBinding { vmdName = source });
                else
                    foreach (var match in mapped)
                        Player.MorphBindings.Add(new HoVmdMotionPlayer.MorphBinding
                        { vmdName = source, renderer = match.renderer, blendShapeIndex = match.blendShapeIndex });
            }

            Player.IkToggleBindings.Clear();
            foreach (string source in UniqueIkTracks())
            {
                GameObject targetObject =
                    existingIk.TryGetValue(source, out var existing) && existing.targetObject != null
                        ? existing.targetObject
                        : FindIkObject(controlsByName, source);
                Player.IkToggleBindings.Add(new HoVmdMotionPlayer.IkToggleBinding
                { vmdName = source, targetObject = targetObject });
            }

            Player.RebuildBindings();
            EditorUtility.SetDirty(Player);
            if (PrefabUtility.IsPartOfPrefabInstance(Player))
                PrefabUtility.RecordPrefabInstancePropertyModifications(Player);
            mappingSummaryDirty = true;
        }

        private List<string> UniqueBoneTracks() => UniqueNames(Player.Animation?.boneFrames.Select(x => x.boneName));
        private List<string> UniqueMorphTracks() => UniqueNames(Player.Animation?.morphFrames.Select(x => x.morphName));
        private List<string> UniqueIkTracks() => UniqueNames(Player.Animation?.showIkFrames
            .Where(x => x.toggles != null).SelectMany(x => x.toggles).Select(x => x.boneName));

        private static List<string> UniqueNames(IEnumerable<string> names) => names == null
            ? new List<string>()
            : names.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToList();

        private static void Add<T>(Dictionary<string, List<T>> index, string key, T value)
        {
            if (!index.TryGetValue(key, out var list)) index.Add(key, list = new List<T>());
            list.Add(value);
        }

        private static T Unique<T>(Dictionary<string, List<T>> index, string key) where T : class
        {
            return index.TryGetValue(key, out var list) && list.Count == 1 ? list[0] : null;
        }

        private static Animator FindHumanoidAnimator(Transform skeleton, Transform root)
        {
            Animator animator = skeleton.GetComponentInParent<Animator>();
            if (animator != null && animator.isHuman &&
                (animator.transform == root || animator.transform.IsChildOf(root))) return animator;
            foreach (Animator candidate in skeleton.GetComponentsInChildren<Animator>(true))
                if (candidate != null && candidate.isHuman) return candidate;
            return null;
        }

        private static Transform FindHumanoidBone(Animator animator, string source)
        {
            if (animator == null) return null;
            HumanBodyBones bone = HumanBodyBone(source);
            return bone == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(bone);
        }

        private static Transform FindIkTarget(Dictionary<string, List<HoIkConstraint>> controls, string source)
        {
            HoIkConstraint constraint = Unique(controls, Normalize(source));
            return constraint != null ? constraint.Target : null;
        }

        private static GameObject FindIkObject(Dictionary<string, List<HoIkConstraint>> controls, string source)
        {
            HoIkConstraint constraint = Unique(controls, Normalize(source));
            return constraint != null ? constraint.gameObject : null;
        }

        private static Transform FindNamedBone(Dictionary<string, List<Transform>> exact,
            Dictionary<string, List<Transform>> normalized, string source)
        {
            Transform bone = Unique(exact, source);
            if (bone != null) return bone;
            string alias = BoneAlias(source);
            if (!string.IsNullOrEmpty(alias) && (bone = Unique(exact, alias)) != null) return bone;
            return Unique(normalized, Normalize(source));
        }

        private static HumanBodyBones HumanBodyBone(string name)
        {
            string side = name.StartsWith("左", StringComparison.Ordinal) ? "Left" :
                name.StartsWith("右", StringComparison.Ordinal) ? "Right" : string.Empty;
            if (name == "上半身") return HumanBodyBones.Spine;
            if (name == "上半身2") return HumanBodyBones.Chest;
            if (name == "下半身" || name == "腰") return HumanBodyBones.Hips;
            if (name == "首") return HumanBodyBones.Neck;
            if (name == "頭") return HumanBodyBones.Head;
            if (name == "左肩" || name == "右肩") return side == "Left" ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            if (name == "左腕" || name == "右腕") return side == "Left" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            if (name == "左ひじ" || name == "右ひじ") return side == "Left" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            if (name == "左手首" || name == "右手首") return side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            if (name == "左足" || name == "右足") return side == "Left" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            if (name == "左ひざ" || name == "右ひざ") return side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            if (name == "左足首" || name == "右足首") return side == "Left" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            if (name == "左つま先" || name == "右つま先") return side == "Left" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            return HumanBodyBones.LastBone;
        }

        private static string BoneAlias(string name)
        {
            switch (name)
            {
                case "下半身": return "hips";
                case "上半身": return "spine";
                case "上半身2": return "chest";
                case "首": return "neck";
                case "頭": return "head";
            }
            string suffix = name.StartsWith("左", StringComparison.Ordinal) ? ".L" :
                name.StartsWith("右", StringComparison.Ordinal) ? ".R" : null;
            if (suffix == null) return null;
            switch (name.Substring(1))
            {
                case "肩": return "shoulder" + suffix;
                case "腕": return "upper_arm" + suffix;
                case "ひじ": return "lower_arm" + suffix;
                case "手首": return "hand" + suffix;
                case "足": return "upper_leg" + suffix;
                case "ひざ": return "lower_leg" + suffix;
                case "足首": return "foot" + suffix;
                case "つま先": return "toes" + suffix;
                default: return null;
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var builder = new StringBuilder(value.Normalize(NormalizationForm.FormKC).Length);
            foreach (char c in value.Normalize(NormalizationForm.FormKC))
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        private void Field(string name, string label, string tooltip = null) =>
            EditorGUILayout.PropertyField(P(name), new GUIContent(label, tooltip));
    }
}
#endif
