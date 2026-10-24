using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Armatures;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.Armatures
{
    /// <summary>
    /// SDK-free, editor-only skeleton assembly. Call ProcessHierarchy on a disposable export copy.
    /// Matching is parent-relative and exact after prefix/suffix removal, never a global name search.
    /// </summary>
    public static class CombineArmatureProcessor
    {
        public sealed class Report
        {
            public int matchedBones;
            public int uniqueBranches;
            public int affectedRenderers;
            public int remappedReferences;
            public int generatedConstraints;
            public readonly List<string> errors = new List<string>();
            public bool IsValid => errors.Count == 0;

            public override string ToString()
            {
                return IsValid
                    ? $"匹配 {matchedBones} 根骨骼，保留 {uniqueBranches} 个专用分支，涉及 {affectedRenderers} 个蒙皮网格。"
                    : string.Join("\n", errors);
            }
        }

        private sealed class Branch
        {
            public Transform source;
            public Transform parent;
        }

        private sealed class Plan
        {
            public CombineArmature config;
            public Transform scope;
            public readonly Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
            public readonly List<Transform> matched = new List<Transform>();
            public readonly List<Branch> branches = new List<Branch>();
            public readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        }

        /// <summary>Read-only validation. No object, mesh or animation asset is modified.</summary>
        public static Report Validate(CombineArmature config)
        {
            var report = new Report();
            CreatePlan(config, report);
            return report;
        }

        public static Transform GetCharacterRoot(CombineArmature config)
        {
            if (config == null || config.targetArmature == null) return null;
            if (config.characterRoot != null) return config.characterRoot;
            Transform common = config.transform;
            while (common != null && !Within(config.targetArmature, common)) common = common.parent;
            if (common == null) return null;
            for (Transform t = common; t != null; t = t.parent)
                if (t.GetComponent<Animator>() != null) return t;
            return common;
        }

        /// <summary>
        /// Processes a complete character/export copy, including inactive outfits. All configurations
        /// are validated before the first mutation. Disabled components are stripped without assembly.
        /// automaticOnly leaves manually managed configurations alone.
        /// </summary>
        public static Report ProcessHierarchy(GameObject root, bool automaticOnly = false)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (EditorUtility.IsPersistent(root))
                throw new InvalidOperationException("请处理实例或 Prefab 内容副本，不能直接处理资产。");

            var report = new Report();
            var configs = root.GetComponentsInChildren<CombineArmature>(true);
            var plans = new List<Plan>();
            var processed = new List<CombineArmature>();
            foreach (var config in configs)
            {
                if (automaticOnly && !config.processAutomatically) continue;
                processed.Add(config);
                if (!config.enabled) continue;
                Plan plan = CreatePlan(config, report);
                if (plan == null) continue;
                if (!Within(plan.scope, root.transform))
                    report.errors.Add($"{config.name}：处理根必须包含完整角色和目标骨架。");
                plans.Add(plan);
            }

            // First version deliberately rejects nested/cross-dependent assembly, rather than
            // depending on component discovery order or merging a second outfit into the first one.
            for (int a = 0; a < plans.Count; a++)
            for (int b = a + 1; b < plans.Count; b++)
            {
                Transform x = plans[a].config.transform;
                Transform y = plans[b].config.transform;
                if (Within(x, y) || Within(y, x) ||
                    Within(plans[a].config.targetArmature, y) || Within(plans[b].config.targetArmature, x))
                    report.errors.Add("CombineArmature 不支持嵌套或互相依赖的衣服骨架，请让各衣服直接指向本体。");
            }
            if (!report.IsValid) throw new InvalidOperationException("CombineArmature 检查失败：\n" + report);

            foreach (Plan plan in plans)
            {
                // Scene prefab instances must be unpacked on the disposable copy before changing
                // their Transform hierarchy. Source prefab assets are never unpacked or overwritten.
                if (plan.config.mode == CombineArmatureMode.Merge) UnpackInstance(plan.config.gameObject);
                if (plan.config.mode == CombineArmatureMode.Merge) ApplyMerge(plan, report);
                else ApplyConstraints(plan, report);
            }
            foreach (var config in processed)
                if (config != null) Object.DestroyImmediate(config);
            return report;
        }

        /// <summary>Creates a processed, unpacked scene copy; destroys only that copy on failure.</summary>
        public static GameObject BakeCopy(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            GameObject copy = Object.Instantiate(source, source.transform.parent);
            copy.name = source.name + "_Combined";
            try
            {
                ProcessHierarchy(copy);
                return copy;
            }
            catch
            {
                Object.DestroyImmediate(copy);
                throw;
            }
        }

        private static Plan CreatePlan(CombineArmature config, Report report)
        {
            if (config == null)
            {
                report.errors.Add("缺少 CombineArmature 组件。");
                return null;
            }
            Transform source = config.transform;
            Transform target = config.targetArmature;
            Transform scope = GetCharacterRoot(config);
            if (target == null || scope == null || !Within(source, scope) || !Within(target, scope))
            {
                report.errors.Add($"{config.name}：请指定目标骨架，并让角色根包含衣服和本体。");
                return null;
            }
            if (Within(source, target) || Within(target, source))
            {
                report.errors.Add($"{config.name}：衣服骨架和目标骨架必须是互不包含的独立子树。");
                return null;
            }
            if (source.gameObject.scene != target.gameObject.scene)
            {
                report.errors.Add($"{config.name}：衣服与目标必须属于同一个场景或 Prefab 内容副本。");
                return null;
            }
            if (config.mode != CombineArmatureMode.Merge && config.mode != CombineArmatureMode.Constraints)
                report.errors.Add($"{config.name}：未知的处理模式。");

            var plan = new Plan { config = config, scope = scope };
            Match(source, target, plan, report);
            report.matchedBones += plan.matched.Count;
            report.uniqueBranches += plan.branches.Count;
            if (plan.matched.Count <= 1)
                report.errors.Add($"{config.name}：除了骨架根没有匹配到骨骼。请检查目标根、名称前后缀和层级。");

            // The entire source subtree is an unstable skeleton implementation detail. Reject
            // animation tracks into it, including unmatched branches, rather than corrupt assets.
            ValidateAnimations(plan, report);
            foreach (Transform bone in plan.matched)
            {
                foreach (Component c in bone.GetComponents<Component>())
                    if (!(c is Transform) && c != config)
                        report.errors.Add($"{Path(bone, scope)}：共享骨骼上有额外组件或丢失脚本；请移到骨架外或专用分支。");
                if (!MatricesMatch(bone.localToWorldMatrix, plan.map[bone].localToWorldMatrix))
                    report.errors.Add($"{Path(bone, scope)}：与目标绑定姿势不一致（位置、旋转或缩放）。请先对齐模型。");
            }
            foreach (Transform t in source.GetComponentsInChildren<Transform>(true))
            {
                if (t != source && t.GetComponent<CombineArmature>() != null)
                    report.errors.Add($"{config.name}：衣服骨架内部不能再挂 CombineArmature。");
                if (t.GetComponent<Renderer>() != null || t.GetComponent<Animator>() != null)
                    report.errors.Add($"{Path(t, scope)}：网格和 Animator 必须放在衣服骨架子树之外。");
            }

            foreach (var renderer in scope.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool affected = renderer.rootBone != null && Within(renderer.rootBone, source);
                foreach (Transform bone in renderer.bones)
                    if (bone != null && Within(bone, source)) affected = true;
                if (!affected) continue;
                plan.renderers.Add(renderer);
                if (renderer.sharedMesh == null || renderer.bones.Length != renderer.sharedMesh.bindposes.Length)
                    report.errors.Add($"{renderer.name}：蒙皮骨骼数量与 Mesh.bindposes 不一致或缺少网格。");
                foreach (Transform bone in renderer.bones)
                    if (bone == null) report.errors.Add($"{renderer.name}：蒙皮包含空骨骼引用。");
            }
            report.affectedRenderers += plan.renderers.Count;
            ValidateExternalReferences(plan, report);
            return plan;
        }

        private static void Match(Transform source, Transform target, Plan plan, Report report)
        {
            plan.map.Add(source, target);
            plan.matched.Add(source);
            var targetChildren = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform child in target)
            {
                if (targetChildren.ContainsKey(child.name))
                    report.errors.Add($"{Path(target, plan.scope)}：目标存在同名子节点 {child.name}。");
                else targetChildren.Add(child.name, child);
            }
            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform child in source)
            {
                string name = MatchName(child.name, plan.config.bonePrefix, plan.config.boneSuffix);
                if (name.Length != 0 && !sourceNames.Add(name))
                    report.errors.Add($"{Path(source, plan.scope)}：衣服骨骼去除前后缀后重名 {name}。");
                if (name.Length != 0 && targetChildren.TryGetValue(name, out Transform match))
                    Match(child, match, plan, report);
                else plan.branches.Add(new Branch { source = child, parent = target });
            }
        }

        private static string MatchName(string name, string prefix, string suffix)
        {
            prefix = prefix ?? string.Empty;
            suffix = suffix ?? string.Empty;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal) ||
                name.Length <= prefix.Length + suffix.Length) return string.Empty;
            return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        }

        private static void ValidateAnimations(Plan plan, Report report)
        {
            var animators = new HashSet<Animator>(plan.scope.GetComponentsInChildren<Animator>(true));
            animators.UnionWith(plan.scope.GetComponentsInParent<Animator>(true));
            foreach (var animator in animators)
            {
                if (animator.runtimeAnimatorController == null) continue;
                string sourcePath = Path(plan.config.transform, animator.transform);
                var visited = new HashSet<AnimationClip>();
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip == null || !visited.Add(clip)) continue;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        Check(binding.path, clip);
                        if (plan.config.mode == CombineArmatureMode.Constraints && binding.type == typeof(Transform) &&
                            (binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal) ||
                             binding.propertyName.StartsWith("localScale.", StringComparison.Ordinal)))
                        {
                            string targetPath = Path(plan.config.targetArmature, animator.transform);
                            if (IsPathInside(binding.path, targetPath))
                                report.errors.Add($"{clip.name}：本体骨架有缩放轨道 {binding.path}；约束模式只复制位置/旋转，请使用直接合并。");
                        }
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip)) Check(binding.path, clip);
                }
                void Check(string path, AnimationClip clip)
                {
                    if (IsPathInside(path, sourcePath))
                        report.errors.Add($"{clip.name}：动画绑定了衣服骨架子树 {path}；请改为控制骨架外的网格/换装节点。");
                }
            }
            var legacyAnimations = new HashSet<Animation>(plan.scope.GetComponentsInChildren<Animation>(true));
            legacyAnimations.UnionWith(plan.scope.GetComponentsInParent<Animation>(true));
            foreach (var legacy in legacyAnimations)
            {
                string sourcePath = Path(plan.config.transform, legacy.transform);
                if (sourcePath == null) continue;
                foreach (AnimationState state in legacy)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(state.clip))
                        if (IsPathInside(binding.path, sourcePath))
                            report.errors.Add($"{state.clip.name}：Legacy Animation 绑定了衣服骨架子树。");
                }
            }
        }

        // References outside the declared character cannot be rewritten safely. Reject those in
        // loaded scenes before mutation, instead of leaving them pointing at destroyed bones.
        private static void ValidateExternalReferences(Plan plan, Report report)
        {
            if (plan.config.mode != CombineArmatureMode.Merge) return;
            foreach (Component component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null || component is Transform || component is CombineArmature ||
                    EditorUtility.IsPersistent(component) || !component.gameObject.scene.IsValid() ||
                    Within(component.transform, plan.scope)) continue;
                VisitReferences(component, (property, referenced) =>
                {
                    Transform bone = referenced is Transform t ? t : (referenced as GameObject)?.transform;
                    if (bone != null && Within(bone, plan.config.transform))
                        report.errors.Add($"{component.name}：角色根外的组件引用了衣服骨架（{property.propertyPath}），请扩大角色根范围。");
                    return null;
                });
            }
        }

        private static void ApplyMerge(Plan plan, Report report)
        {
            foreach (var renderer in plan.renderers)
            {
                Transform[] bones = renderer.bones;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null && plan.map.TryGetValue(bones[i], out Transform replacement)) bones[i] = replacement;
                renderer.bones = bones;
                if (renderer.rootBone != null && plan.map.TryGetValue(renderer.rootBone, out Transform root))
                    renderer.rootBone = root;
                EditorUtility.SetDirty(renderer);
            }
            foreach (Component component in plan.scope.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component is CombineArmature) continue;
                report.remappedReferences += VisitReferences(component, (property, referenced) =>
                {
                    Transform bone = referenced is Transform t ? t : (referenced as GameObject)?.transform;
                    // Keep the source root as a stable empty object. Generic references to that
                    // object keep their identity; only skin/rootBone references use the target root.
                    if (bone == null || bone == plan.config.transform || !plan.map.TryGetValue(bone, out Transform target))
                        return null;
                    return referenced is Transform ? (Object)target : target.gameObject;
                });
            }
            foreach (Branch branch in plan.branches)
            {
                // Namespace only the moved branch root, leaving its internal hierarchy intact.
                // Plans for all outfits were captured before moving any branches, so a later
                // outfit cannot accidentally merge into a branch contributed by an earlier one.
                string baseName = plan.config.name + "__" + branch.source.name;
                string name = baseName;
                int suffix = 2;
                while (HasChildNamed(branch.parent, name)) name = baseName + "_" + suffix++;
                branch.source.name = name;
                // Old and new parent world matrices were validated as equal. Keeping local TRS
                // avoids decomposing a possibly non-uniformly scaled world matrix into TRS again.
                branch.source.SetParent(branch.parent, false);
            }
            // Descendants first. Moved unique branches have already left the deleted subtree.
            for (int i = plan.matched.Count - 1; i > 0; i--)
                Object.DestroyImmediate(plan.matched[i].gameObject);
        }

        private static void ApplyConstraints(Plan plan, Report report)
        {
            var generated = new List<Behaviour>();
            foreach (Transform bone in plan.matched)
            {
                Transform target = plan.map[bone];
                var source = new ConstraintSource { sourceTransform = target, weight = 1f };
                var parent = bone.gameObject.AddComponent<ParentConstraint>();
                parent.enabled = false;
                parent.constraintActive = false;
                parent.locked = true;
                parent.translationAxis = Axis.X | Axis.Y | Axis.Z;
                parent.rotationAxis = Axis.X | Axis.Y | Axis.Z;
                parent.translationAtRest = bone.localPosition;
                parent.rotationAtRest = bone.localEulerAngles;
                parent.AddSource(source);
                parent.SetTranslationOffset(0, Vector3.zero);
                parent.SetRotationOffset(0, Vector3.zero);
                parent.weight = 1f;
                parent.locked = true;
                parent.constraintActive = true;

                generated.Add(parent);
                report.generatedConstraints++;
            }
            foreach (Behaviour constraint in generated) constraint.enabled = true;
        }

        private static int VisitReferences(Component component, Func<SerializedProperty, Object, Object> remap)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            int count = 0;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.propertyPath == "m_GameObject" || property.propertyPath == "m_Script") continue;
                Object reference = property.objectReferenceValue;
                if (reference == null) continue;
                Object replacement = remap(property, reference);
                if (replacement == null || replacement == reference) continue;
                property.objectReferenceValue = replacement;
                count++;
            }
            if (count != 0) serialized.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }

        private static void UnpackInstance(GameObject source)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(source);
            if (root != null)
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        private static bool HasChildNamed(Transform parent, string name)
        {
            foreach (Transform child in parent) if (child.name == name) return true;
            return false;
        }

        private static bool MatricesMatch(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (float.IsNaN(a[i]) || float.IsNaN(b[i]) || float.IsInfinity(a[i]) || float.IsInfinity(b[i]) ||
                    Mathf.Abs(a[i] - b[i]) > 0.0001f * Mathf.Max(1f, Mathf.Abs(a[i]), Mathf.Abs(b[i]))) return false;
            return true;
        }

        private static bool Within(Transform child, Transform root)
        {
            return child != null && root != null && (child == root || child.IsChildOf(root));
        }

        private static string Path(Transform child, Transform root)
        {
            return Within(child, root) ? AnimationUtility.CalculateTransformPath(child, root) : null;
        }

        private static bool IsPathInside(string path, string rootPath)
        {
            return rootPath != null && (rootPath.Length == 0 || path == rootPath ||
                path.StartsWith(rootPath + "/", StringComparison.Ordinal));
        }
    }
}
