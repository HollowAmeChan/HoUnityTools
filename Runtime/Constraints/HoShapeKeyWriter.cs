using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 形态键写入器：绑定表 + 外部基准快照 + 目标求值/合并 + 阈值写。
    /// 眨眼约束与注视约束共用 —— 宿主只负责"算出驱动值"，这里负责"怎么写进网格"。
    ///
    /// 两条踩过的坑固化在这里：
    /// 1) 外部基准检测：网格上的值若等于我们上次写入的值，就认为没有外部写者动过，沿用上次的基准；
    ///    否则叠加模式会把自己写进去的值再加一遍（眨眼闭住不睁开就是这么来的）。
    /// 2) 写回值夹到 0..100，让 LastWritten 与实际读回值一致，避免污染上面的判定。
    /// </summary>
    public sealed class HoShapeKeyWriter
    {
        private struct Binding
        {
            public int MeshIndex;
            public int KeyIndex;
            public float BaseValue;
            public float ExternalBase;
            public float LastWritten;
            public float Sum;
            public float OverrideValue;
            public bool HasOverride;
            public bool EverWritten;

            /// <summary>本帧"基准 + 原始求和"（合并之前），给饱和诊断用。</summary>
            public float LastRequest;

            /// <summary>本帧最终写出去的值（即使因为阈值没写，也是算出来的值）。</summary>
            public float LastFinal;

            /// <summary>本帧我们这一路被合并策略削过。</summary>
            public bool LastClipped;
        }

        private struct CompiledTarget
        {
            public int BindingStart;
            public int BindingCount;
            public float Envelope;
            public float Output;
        }

        /// <summary>
        /// 上一轮"我们写过"的键（跨重建保留）。
        /// 重建（改规则、换网格、键名变动）会换掉整张绑定表，如果就这么让它重新读网格，
        /// 它会把我们上一次的输出当成"外部基准"，于是关掉规则也回不去 —— 键就停在最后一次的值上。
        /// </summary>
        private struct RememberedKey
        {
            public SkinnedMeshRenderer Mesh;
            public int KeyIndex;
            public float ExternalBase;
            public float BaseValue;
            public float LastWritten;
        }

        private readonly List<Binding> bindingList = new List<Binding>();
        private readonly List<int> flatBindingIds = new List<int>();
        private readonly List<CompiledTarget> compiledTargets = new List<CompiledTarget>();
        private readonly List<HoShapeKeyTarget> sources = new List<HoShapeKeyTarget>();
        private readonly List<string> missingKeys = new List<string>();
        private readonly Dictionary<long, int> bindingMap = new Dictionary<long, int>();
        private readonly List<RememberedKey> remembered = new List<RememberedKey>();

        private SkinnedMeshRenderer[] meshes;
        private Mesh[] meshRefs;
        private Dictionary<string, int>[] meshLookups;
        private Binding[] bindings;
        private CompiledTarget[] compiled;
        private int[] bindingIds;
        private int lastRendererCount = -1;

        /// <summary>变化小于该值就不写，减少 mesh dirty。</summary>
        public float WriteThreshold { get; set; } = 0.01f;

        public bool WriteEnabled { get; set; } = true;

        /// <summary>求和超过 100 时怎么处理（见 <see cref="HoShapeKeyMergeMode"/>）。默认夹断。</summary>
        public HoShapeKeyMergeMode MergeMode { get; set; } = HoShapeKeyMergeMode.Saturate;

        public int MeshCount => meshes != null ? meshes.Length : 0;

        public int BindingCount => bindings != null ? bindings.Length : 0;

        public int TargetCount => compiled != null ? compiled.Length : 0;

        public IReadOnlyList<string> MissingKeys => missingKeys;

        public bool IsBuilt => compiled != null;

        public SkinnedMeshRenderer GetMesh(int index)
        {
            if (meshes == null || index < 0 || index >= meshes.Length)
            {
                return null;
            }

            return meshes[index];
        }

        /// <summary>键名是否在任意一个网格上存在（面板用它标黄缺失项）。</summary>
        public bool KeyExists(string keyName)
        {
            if (meshLookups == null || string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            for (int i = 0; i < meshLookups.Length; i++)
            {
                if (HoShapeKeyResolver.TryResolve(meshLookups[i], keyName, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>这个键名在这批网格上解析到几个绑定（0 = 一个都没找到）。面板用。</summary>
        public int CountKeyBindings(string keyName)
        {
            if (meshLookups == null || string.IsNullOrEmpty(keyName))
            {
                return 0;
            }

            int found = 0;
            for (int m = 0; m < meshLookups.Length; m++)
            {
                if (HoShapeKeyResolver.TryResolve(meshLookups[m], keyName, out _))
                {
                    found++;
                }
            }

            return found;
        }

        /// <summary>命中的网格名（最多 maxNames 个），给面板显示"这个键落在谁身上"。</summary>
        public string DescribeKeyBindings(string keyName, int maxNames = 3)
        {
            if (meshes == null || string.IsNullOrEmpty(keyName))
            {
                return string.Empty;
            }

            string result = string.Empty;
            int shown = 0;
            int total = 0;
            for (int m = 0; m < meshes.Length; m++)
            {
                if (meshLookups == null || !HoShapeKeyResolver.TryResolve(meshLookups[m], keyName, out _))
                {
                    continue;
                }

                total++;
                if (shown < maxNames)
                {
                    result = shown == 0 ? meshes[m].name : result + "、" + meshes[m].name;
                    shown++;
                }
            }

            if (total > shown)
            {
                result += " 等 " + total + " 个";
            }

            return result;
        }
        /// <summary>网格列表或 mesh 变了（换装 / mesh 重建）——宿主每帧问一次，用来触发重建。</summary>
        public bool MeshesChanged()
        {
            if (meshes == null || meshRefs == null || meshes.Length != meshRefs.Length)
            {
                return true;
            }

            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null || meshRefs[i] != meshes[i].sharedMesh)
                {
                    return true;
                }
            }

            return false;
        }

        // ── 构建期 ────────────────────────────────────────────────────────

        /// <summary>开始重建：缓存网格与键名索引，清空上一轮的绑定与目标。</summary>
        public void BeginBuild(List<Renderer> renderers)
        {
            // 先把"上一轮我们写过的键"记下来：EndBuild 时还在写的继承基准，不再写的硬写清场
            remembered.Clear();
            if (bindings != null && meshes != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!bindings[i].EverWritten || meshes[bindings[i].MeshIndex] == null)
                    {
                        continue;
                    }

                    remembered.Add(new RememberedKey
                    {
                        Mesh = meshes[bindings[i].MeshIndex],
                        KeyIndex = bindings[i].KeyIndex,
                        ExternalBase = bindings[i].ExternalBase,
                        BaseValue = bindings[i].BaseValue,
                        LastWritten = bindings[i].LastWritten
                    });
                }
            }

            bindingList.Clear();
            flatBindingIds.Clear();
            compiledTargets.Clear();
            sources.Clear();
            missingKeys.Clear();
            bindingMap.Clear();

            List<SkinnedMeshRenderer> found = new List<SkinnedMeshRenderer>();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                    if (skinned == null)
                    {
                        skinned = renderer.GetComponent<SkinnedMeshRenderer>();
                    }

                    if (skinned == null || skinned.sharedMesh == null || found.Contains(skinned))
                    {
                        continue;
                    }

                    found.Add(skinned);
                }
            }

            meshes = found.ToArray();
            meshRefs = new Mesh[meshes.Length];
            meshLookups = new Dictionary<string, int>[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshRefs[i] = meshes[i].sharedMesh;
                meshLookups[i] = HoShapeKeyResolver.BuildLookup(meshRefs[i]);
            }

            lastRendererCount = renderers != null ? renderers.Count : 0;
        }

        /// <summary>注册一个输出目标，返回 targetId（按注册顺序递增）。</summary>
        public int RegisterTarget(HoShapeKeyTarget target)
        {
            int start = flatBindingIds.Count;
            int resolved = 0;
            if (target != null && !string.IsNullOrEmpty(target.KeyName))
            {
                int from = 0;
                int to = meshes.Length;
                if (target.MeshScope == HoShapeKeyMeshScope.Index)
                {
                    from = Mathf.Clamp(target.MeshIndex, 0, Mathf.Max(0, meshes.Length - 1));
                    to = Mathf.Min(from + 1, meshes.Length);
                }

                for (int m = from; m < to; m++)
                {
                    if (!HoShapeKeyResolver.TryResolve(meshLookups[m], target.KeyName, out int keyIndex))
                    {
                        continue;
                    }

                    flatBindingIds.Add(GetOrAddBinding(m, keyIndex));
                }

                resolved = flatBindingIds.Count - start;
                if (resolved == 0)
                {
                    AddMissing(target.KeyName);
                }
            }

            sources.Add(target);
            compiledTargets.Add(new CompiledTarget
            {
                BindingStart = start,
                BindingCount = resolved
            });
            return compiledTargets.Count - 1;
        }

        /// <summary>
        /// 注册一个"读"键（驱动来源）。同一个键名可能出现在多个网格上，全部追加进 <paramref name="results"/>；
        /// 一个都没解析到时记进缺失清单。
        /// </summary>
        public void RegisterReadKey(string keyName, List<int> results)
        {
            if (results == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(keyName) || meshes.Length == 0)
            {
                if (!string.IsNullOrEmpty(keyName))
                {
                    AddMissing(keyName);
                }

                return;
            }

            int found = 0;
            for (int m = 0; m < meshes.Length; m++)
            {
                if (!HoShapeKeyResolver.TryResolve(meshLookups[m], keyName, out int keyIndex))
                {
                    continue;
                }

                results.Add(GetOrAddBinding(m, keyIndex));
                found++;
            }

            if (found == 0)
            {
                AddMissing(keyName);
            }
        }

        public void EndBuild()
        {
            bindings = bindingList.ToArray();
            bindingIds = flatBindingIds.ToArray();
            compiled = compiledTargets.ToArray();
            AdoptRemembered();
        }

        /// <summary>
        /// 重建收尾：
        /// 1) 新一轮还在写的键，继承上一轮的"外部基准 / 上次写入值" —— 不许把我们自己的输出当基准；
        /// 2) 新一轮不再有人写、但我们上一轮写过的键，**硬写回基准**（不看阈值）—— 关掉规则/删掉目标时清场，
        ///    否则那根键会永远停在我们最后一次写的值上。
        /// </summary>
        private void AdoptRemembered()
        {
            if (remembered.Count == 0)
            {
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                SkinnedMeshRenderer mesh = meshes[bindings[i].MeshIndex];
                for (int r = 0; r < remembered.Count; r++)
                {
                    if (remembered[r].Mesh != mesh || remembered[r].KeyIndex != bindings[i].KeyIndex)
                    {
                        continue;
                    }

                    bindings[i].ExternalBase = remembered[r].ExternalBase;
                    bindings[i].BaseValue = remembered[r].BaseValue;
                    bindings[i].LastWritten = remembered[r].LastWritten;
                    bindings[i].EverWritten = true;
                    remembered[r] = default;   // 已被认领
                    break;
                }
            }

            for (int r = 0; r < remembered.Count; r++)
            {
                SkinnedMeshRenderer mesh = remembered[r].Mesh;
                if (mesh == null)
                {
                    continue;
                }

                mesh.SetBlendShapeWeight(remembered[r].KeyIndex, Mathf.Clamp(remembered[r].ExternalBase, 0.0f, 100.0f));
            }

            remembered.Clear();
        }

        /// <summary>
        /// 把我们写过的键全部**硬写回基准值**（不看阈值、不管当前值），用于约束被关掉/禁用时清场。
        /// </summary>
        public void RestoreWritten()
        {
            if (bindings == null || meshes == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                if (!bindings[i].EverWritten)
                {
                    continue;
                }

                SkinnedMeshRenderer mesh = meshes[bindings[i].MeshIndex];
                if (mesh == null)
                {
                    continue;
                }

                float value = Mathf.Clamp(bindings[i].ExternalBase, 0.0f, 100.0f);
                mesh.SetBlendShapeWeight(bindings[i].KeyIndex, value);
                bindings[i].LastWritten = value;
                bindings[i].EverWritten = false;
                bindings[i].Sum = 0.0f;
                bindings[i].HasOverride = false;
            }
        }

        /// <summary>清掉"我们写过"的记账与包络状态（重建、启用、重置时调用）。</summary>
        public void Reset()
        {
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    bindings[i].EverWritten = false;
                    bindings[i].Sum = 0.0f;
                    bindings[i].HasOverride = false;
                    bindings[i].OverrideValue = 0.0f;
                }
            }

            if (compiled != null)
            {
                for (int i = 0; i < compiled.Length; i++)
                {
                    compiled[i].Envelope = 0.0f;
                    compiled[i].Output = 0.0f;
                }
            }
        }

        // ── 每帧 ──────────────────────────────────────────────────────────

        /// <summary>读一遍所有涉及的键，确定本帧的外部基准并清空累加。</summary>
        public void Snapshot()
        {
            if (bindings == null || meshes == null)
            {
                return;
            }

            float tolerance = Mathf.Max(WriteThreshold, 0.0001f);
            for (int i = 0; i < bindings.Length; i++)
            {
                float current = meshes[bindings[i].MeshIndex].GetBlendShapeWeight(bindings[i].KeyIndex);

                if (!bindings[i].EverWritten || Mathf.Abs(current - bindings[i].LastWritten) > tolerance)
                {
                    bindings[i].ExternalBase = current;
                }

                bindings[i].BaseValue = bindings[i].ExternalBase;
                bindings[i].Sum = 0.0f;
                bindings[i].HasOverride = false;
                bindings[i].OverrideValue = 0.0f;
            }
        }

        /// <summary>把一个驱动值（归一化）按目标的映射写进累加器。</summary>
        public void Apply(int targetId, float driverValue, float deltaTime)
        {
            if (compiled == null || targetId < 0 || targetId >= compiled.Length)
            {
                return;
            }

            HoShapeKeyTarget target = sources[targetId];
            if (target == null)
            {
                return;
            }

            float x = driverValue * target.Gain + target.Offset;
            float shaped = HoShapeKeyRampPresets.Shape(target.RampPreset, target.RampIntensity, target.RampCurve, x);
            HoShapeKeyRampPresets.Times(
                target.RampPreset,
                target.RampIntensity,
                target.RampAttack,
                target.RampRelease,
                out float attack,
                out float release);

            compiled[targetId].Envelope = HoShapeKeyRampPresets.Envelope(
                compiled[targetId].Envelope,
                shaped,
                deltaTime,
                attack,
                release);

            float output = compiled[targetId].Envelope * 100.0f;
            if (target.ClampToRange)
            {
                float min = Mathf.Min(target.OutputMin, target.OutputMax);
                float max = Mathf.Max(target.OutputMin, target.OutputMax);
                output = Mathf.Clamp(output, min, max);
            }

            compiled[targetId].Output = output;
            if (target.Weight <= 0.0f || compiled[targetId].BindingCount == 0)
            {
                return;
            }

            for (int i = 0; i < compiled[targetId].BindingCount; i++)
            {
                int id = bindingIds[compiled[targetId].BindingStart + i];
                if (target.BlendMode == HoShapeKeyBlendMode.Override)
                {
                    bindings[id].HasOverride = true;
                    bindings[id].OverrideValue = Mathf.Lerp(bindings[id].BaseValue, output, target.Weight);
                }
                else
                {
                    bindings[id].Sum += output * target.Weight;
                }
            }
        }

        /// <summary>合并并写回（阈值写、同键只写一次）。</summary>
        public void Write()
        {
            if (!WriteEnabled || bindings == null || meshes == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                float baseValue = bindings[i].BaseValue;
                float sum = bindings[i].Sum;
                float merged = MergeContribution(sum, baseValue);

                float final = bindings[i].HasOverride
                    ? bindings[i].OverrideValue
                    : baseValue + merged;

                float requested = baseValue + sum;
                bindings[i].LastRequest = requested;
                bindings[i].LastClipped = !bindings[i].HasOverride
                                          && (merged < sum - 0.001f || requested > 100.0f || requested < 0.0f);

                final = Mathf.Clamp(final, 0.0f, 100.0f);
                bindings[i].LastFinal = final;

                if (bindings[i].EverWritten && Mathf.Abs(final - bindings[i].LastWritten) <= WriteThreshold)
                {
                    continue;
                }

                SkinnedMeshRenderer mesh = meshes[bindings[i].MeshIndex];
                if (mesh == null)
                {
                    continue;
                }

                mesh.SetBlendShapeWeight(bindings[i].KeyIndex, final);
                bindings[i].LastWritten = final;
                bindings[i].EverWritten = true;
            }
        }

        /// <summary>软压缩的拐点：这个值以上开始压，渐近到 100 但到不了。</summary>
        private const float SoftClipKnee = 80.0f;

        /// <summary>
        /// 把"我们这一路求和出来的贡献"合并成一个可以直接加在基准上的值。
        ///
        /// 注意这里**只动我们自己的贡献，不动基准** —— 动画/面捕写在键上的值不会被我们改写，
        /// 我们没出力（sum = 0）时输出恒等于基准。
        /// </summary>
        private float MergeContribution(float sum, float baseValue)
        {
            if (Mathf.Abs(sum) <= 0.0001f)
            {
                return 0.0f;
            }

            switch (MergeMode)
            {
                case HoShapeKeyMergeMode.SoftClip:
                    return SoftClipValue(sum);

                case HoShapeKeyMergeMode.Normalize:
                {
                    float total = baseValue + sum;
                    if (sum > 0.0f && total > 100.0f)
                    {
                        // 剩余空间按比例分给各路，比例关系不变
                        float room = Mathf.Max(0.0f, 100.0f - baseValue);
                        return sum * (room / total);
                    }

                    return sum;
                }

                default:
                    return sum;
            }
        }

        private static float SoftClipValue(float sum)
        {
            float magnitude = Mathf.Abs(sum);
            if (magnitude <= SoftClipKnee)
            {
                return sum;
            }

            float span = 100.0f - SoftClipKnee;
            float compressed = SoftClipKnee + span * (1.0f - Mathf.Exp(-(magnitude - SoftClipKnee) / span));
            return Mathf.Sign(sum) * compressed;
        }

        /// <summary>
        /// 收集"已经写满（或我们这一路被削过）"的键，给面板显示用。
        /// 只有编辑器会调用，所以键名/网格名的字符串开销无所谓。
        /// </summary>
        public void CollectSaturated(List<HoShapeKeySaturation> results, float threshold = 99.5f)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            if (bindings == null || meshes == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                if (!bindings[i].EverWritten)
                {
                    continue;
                }

                if (bindings[i].LastFinal < threshold && !bindings[i].LastClipped)
                {
                    continue;
                }

                SkinnedMeshRenderer mesh = meshes[bindings[i].MeshIndex];
                Mesh shared = mesh != null ? mesh.sharedMesh : null;
                results.Add(new HoShapeKeySaturation
                {
                    MeshName = shared != null ? shared.name : "（网格丢失）",
                    KeyName = shared != null ? shared.GetBlendShapeName(bindings[i].KeyIndex) : "?",
                    Base = bindings[i].ExternalBase,
                    Request = bindings[i].LastRequest,
                    Final = bindings[i].LastFinal,
                    Clipped = bindings[i].LastClipped
                });
            }
        }

        /// <summary>
        /// 读一个绑定。writePending = true 时读"本帧到目前为止这一路会写出的值"，
        /// 供宿主实现"读本帧已写值"的链式联动；false 时读外部基准（切断自反馈）。
        /// </summary>
        public float ReadBinding(int bindingId, bool writePending)
        {
            if (bindings == null || bindingId < 0 || bindingId >= bindings.Length)
            {
                return 0.0f;
            }

            Binding binding = bindings[bindingId];
            if (!writePending)
            {
                return binding.BaseValue;
            }

            return binding.HasOverride ? binding.OverrideValue : binding.BaseValue + binding.Sum;
        }

        public float GetTargetOutput(int targetId)
        {
            if (compiled == null || targetId < 0 || targetId >= compiled.Length)
            {
                return 0.0f;
            }

            return compiled[targetId].Output;
        }

        private int GetOrAddBinding(int meshIndex, int keyIndex)
        {
            long key = ((long)meshIndex << 32) | (uint)keyIndex;
            if (bindingMap.TryGetValue(key, out int id))
            {
                return id;
            }

            id = bindingList.Count;
            bindingList.Add(new Binding { MeshIndex = meshIndex, KeyIndex = keyIndex });
            bindingMap.Add(key, id);
            return id;
        }

        private void AddMissing(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || missingKeys.Contains(keyName))
            {
                return;
            }

            missingKeys.Add(keyName);
        }
    }
}