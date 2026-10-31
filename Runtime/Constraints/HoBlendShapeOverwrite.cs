using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>Object-scoped shape overwrites; disabling the object releases its acquired keys.</summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Blend Shape Overwrite")]
    public sealed class HoBlendShapeOverwrite : MonoBehaviour
    {
        public enum RestoreMode { FixedValue, PreviousValue }

        [Serializable]
        public sealed class Rule
        {
            public string shapeName;
            [Range(0f, 100f)] public float value = 100f;
        }

        [SerializeField] private List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private List<Rule> rules = new List<Rule>();
        [SerializeField] private RestoreMode restoreMode = RestoreMode.FixedValue;
        [SerializeField, Range(0f, 100f)] private float restoreValue;

        private sealed class Applied
        {
            public Rule Source;
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public int Index;
            public float RestoreWeight;
        }

        private struct MeshConfiguration
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
        }

        private struct RuleConfiguration
        {
            public Rule Source;
            public string Name;
        }

        // Never let a second component restore a snapshot captured from somebody else's overwrite.
        private static readonly Dictionary<(SkinnedMeshRenderer, Mesh, int), HoBlendShapeOverwrite> Owners =
            new Dictionary<(SkinnedMeshRenderer, Mesh, int), HoBlendShapeOverwrite>();
        private readonly List<Applied> applied = new List<Applied>();
        private readonly List<MeshConfiguration> meshConfigurations = new List<MeshConfiguration>();
        private readonly List<RuleConfiguration> ruleConfigurations = new List<RuleConfiguration>();
        private bool acquired;
        private bool blockedByOwner;
        private static int ownershipVersion;
        private int acquiredOwnershipVersion;
        private bool rebuild = true;
        private RestoreMode acquiredMode;
        private float acquiredRestoreValue;
        [NonSerialized] private bool editorPreview;

        public bool EditorPreviewActive => editorPreview;
        public IReadOnlyList<SkinnedMeshRenderer> Meshes => meshes;
        public IReadOnlyList<Rule> Rules => rules;

        /// <summary>Explicit opt-in; never serialized into the prefab or carried into a build.</summary>
        public void SetEditorPreview(bool value)
        {
            editorPreview = value;
            EvaluateOverwrite();
        }

        private bool ShouldEvaluate => isActiveAndEnabled && gameObject.scene.IsValid() &&
            (Application.IsPlaying(gameObject) || (!Application.isPlaying && editorPreview));

        private void OnEnable() => EvaluateOverwrite();
        private void LateUpdate() => EvaluateOverwrite();
        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        // OnValidate may run off the main thread. Defer all Unity object access to evaluation.
        private void OnValidate() => rebuild = true;

        /// <summary>Also usable immediately after an external/offline animation sampler.</summary>
        public void EvaluateOverwrite()
        {
            if (!ShouldEvaluate) { Release(); return; }
            if (rebuild || !acquired || BindingsChanged()) { Release(); Acquire(); }
            foreach (Applied item in applied)
            {
                if (!IsValid(item) || FaceTracking.HoFaceOutputOwnership.IsReserved(item.Renderer, item.Index)) continue;
                float weight = Mathf.Clamp(item.Source.value, 0f, 100f);
                if (!Mathf.Approximately(item.Renderer.GetBlendShapeWeight(item.Index), weight))
                    item.Renderer.SetBlendShapeWeight(item.Index, weight);
            }
        }

        private bool BindingsChanged()
        {
            if (blockedByOwner && acquiredOwnershipVersion != ownershipVersion) return true;
            if (acquiredMode != restoreMode || acquiredRestoreValue != restoreValue ||
                meshConfigurations.Count != (meshes?.Count ?? 0) || ruleConfigurations.Count != (rules?.Count ?? 0)) return true;
            for (int i = 0; i < meshConfigurations.Count; i++)
            {
                SkinnedMeshRenderer renderer = meshes[i];
                if (renderer != meshConfigurations[i].Renderer ||
                    (renderer != null ? renderer.sharedMesh : null) != meshConfigurations[i].Mesh) return true;
            }
            for (int i = 0; i < ruleConfigurations.Count; i++)
                if (!ReferenceEquals(rules[i], ruleConfigurations[i].Source) ||
                    rules[i]?.shapeName != ruleConfigurations[i].Name) return true;
            return false;
        }

        private static bool IsValid(Applied item) => item.Renderer != null && item.Mesh != null &&
            item.Renderer.sharedMesh == item.Mesh && item.Index >= 0 && item.Index < item.Mesh.blendShapeCount;

        private void Acquire()
        {
            rebuild = false;
            acquired = true;
            blockedByOwner = false;
            acquiredMode = restoreMode;
            acquiredRestoreValue = restoreValue;
            if (meshes != null)
                foreach (SkinnedMeshRenderer renderer in meshes)
                    meshConfigurations.Add(new MeshConfiguration { Renderer = renderer, Mesh = renderer != null ? renderer.sharedMesh : null });
            if (rules == null) return;
            foreach (Rule rule in rules)
            {
                ruleConfigurations.Add(new RuleConfiguration { Source = rule, Name = rule?.shapeName });
                if (rule == null || string.IsNullOrWhiteSpace(rule.shapeName)) continue;
                bool found = false;
                foreach (MeshConfiguration config in meshConfigurations)
                {
                    if (config.Renderer == null || config.Mesh == null) continue;
                    int index = config.Mesh.GetBlendShapeIndex(rule.shapeName);
                    if (index < 0 && !HoShapeKeyResolver.TryResolve(config.Mesh, rule.shapeName, out index)) continue;
                    found = true;
                    var key = (config.Renderer, config.Mesh, index);
                    if (Owners.TryGetValue(key, out HoBlendShapeOverwrite owner) && owner != null)
                    {
                        if (owner != this)
                        {
                            blockedByOwner = true;
                            Debug.LogWarning($"HoBlendShapeOverwrite: {rule.shapeName} 已被 {owner.name} 接管，跳过重复覆写。请先关闭原覆写，再启用新覆写。", this);
                        }
                        continue;
                    }
                    if (FaceTracking.HoFaceOutputOwnership.IsReserved(config.Renderer, index))
                    {
                        Debug.LogWarning($"HoBlendShapeOverwrite: {rule.shapeName} 已被面捕接管，跳过。", this);
                        continue;
                    }
                    Owners[key] = this;
                    applied.Add(new Applied
                    {
                        Source = rule, Renderer = config.Renderer, Mesh = config.Mesh, Index = index,
                        RestoreWeight = restoreMode == RestoreMode.PreviousValue
                            ? config.Renderer.GetBlendShapeWeight(index) : Mathf.Clamp(restoreValue, 0f, 100f)
                    });
                }
                if (!found)
                    Debug.LogWarning($"HoBlendShapeOverwrite: Mesh 列表中找不到形态键 {rule.shapeName}。", this);
            }
            acquiredOwnershipVersion = ownershipVersion;
        }

        private void Release()
        {
            foreach (Applied item in applied)
            {
                var key = (item.Renderer, item.Mesh, item.Index);
                if (!Owners.TryGetValue(key, out HoBlendShapeOverwrite owner) || owner != this) continue;
                Owners.Remove(key);
                ownershipVersion++;
                // Never use a stale numeric index against a replacement mesh.
                if (IsValid(item) && !FaceTracking.HoFaceOutputOwnership.IsReserved(item.Renderer, item.Index))
                    item.Renderer.SetBlendShapeWeight(item.Index, item.RestoreWeight);
            }
            applied.Clear();
            meshConfigurations.Clear();
            ruleConfigurations.Clear();
            acquired = false;
        }
    }
}
