using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 一个输出映射：目标（复用眨眼/注视那套 `HoShapeKeyTarget`）+ 它吃弹簧的哪半周。
    /// **回弹是果冻的一半**：正向目标吃挤压，反向目标吃回弹，否则负半周会被钳在 0，看不见。
    /// </summary>
    [Serializable]
    public sealed class HoSpringTarget
    {
        [SerializeField] private HoShapeKeyTarget target = new HoShapeKeyTarget();
        [SerializeField] private bool reversed;

        public HoShapeKeyTarget Target => target;

        /// <summary>true = 吃负半周（回弹侧）。</summary>
        public bool Reversed => reversed;

        public HoSpringTarget() { }

        public HoSpringTarget(string keyName, float gain, bool reversed = false)
        {
            target = HoShapeKeyTarget.CreateRuntime(keyName, gain);
            this.reversed = reversed;
        }
    }

    /// <summary>一个自由度：自己的弹簧（频率 / 阻尼）+ 自己的输出映射。</summary>
    [Serializable]
    public sealed class HoSpringAxis
    {
        [SerializeField] private string label = "横向";
        [SerializeField, Min(0.01f)] private float frequency = 6.0f;
        [SerializeField, Range(0.0f, 4.0f)] private float damping = 0.25f;
        [SerializeField] private float gain = 1.0f;
        [SerializeField] private List<HoSpringTarget> targets = new List<HoSpringTarget>();

        public string Label => label;
        public float Frequency => frequency;
        public float Damping => damping;
        public float Gain => gain;
        public List<HoSpringTarget> Targets => targets;

        public HoSpringAxis() { }

        public HoSpringAxis(string label, float frequency, float damping, float gain = 1.0f)
        {
            this.label = label;
            this.frequency = frequency;
            this.damping = damping;
            this.gain = gain;
        }
    }

    /// <summary>一路输入：读哪些键（**取最大**，这样双眼键 / 左右键两种模型都直接能用）+ 驱动哪几个自由度。</summary>
    [Serializable]
    public sealed class HoSpringSource
    {
        [SerializeField] private string label = "眨眼";
        [SerializeField] private List<string> keyNames = new List<string>();
        [SerializeField] private List<HoSpringAxis> axes = new List<HoSpringAxis>();

        public string Label => label;
        public List<string> KeyNames => keyNames;
        public List<HoSpringAxis> Axes => axes;

        public HoSpringSource() { }

        public HoSpringSource(string label)
        {
            this.label = label;
        }
    }

    /// <summary>
    /// <b>弹簧驱动</b>：读形态键 → 过阻尼弹簧 → 写形态键。Live2D 的"物理演算"在我们这边的对应物。
    ///
    /// **它为什么不是控制器里的东西**（定案 19）：混合树不能输出带物理的参数，而且混合树是状态机里的
    /// 东西 —— 所以物理参数的生产跟状态机没关系；它又是纯值消费，消费场景也不需要待在状态机里。
    /// 两头都不需要状态机，于是这一整件事就是一个独立组件。
    ///
    /// **输入走"读已经落下来的键"**（而不是去问面捕会话）：
    /// 于是它跟面捕完全解耦 —— 面捕、基础动画、别人写的键，谁写进去的都行；谁先写谁后写只由
    /// 执行顺序决定。它读的那一帧值就是它看到的信号。
    ///
    /// **写入靠 <see cref="HoShapeKeyWriter"/>**：键解析、ramp、增益/偏移、范围钳制、叠加/覆盖、
    /// 饱和合并、恢复原值、以及**占用表检查**（`HoFaceOutputOwnership`）全是现成的 ——
    /// 面捕正占着的键它不会去抢，这也是"读进去的键"和"写出去的键"可以重合而不打架的原因。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Constraints/Ho Spring Constraint")]
    public sealed class HoSpringConstraint : MonoBehaviour, IHoShapeKeyMeshProvider
    {
        [SerializeField] private List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private List<HoSpringSource> sources = new List<HoSpringSource>();
        [SerializeField] private bool writingEnabled = true;
        [SerializeField] private float writeThreshold = 0.01f;
        [SerializeField] private HoShapeKeyMergeMode mergeMode = HoShapeKeyMergeMode.Saturate;

        private readonly HoShapeKeyWriter writer = new HoShapeKeyWriter();
        private readonly List<HoFaceJellyState> springs = new List<HoFaceJellyState>();
        private readonly List<int> axisTargetStart = new List<int>();
        private readonly List<int> sourceBindingStart = new List<int>();
        private readonly List<int> sourceBindingCount = new List<int>();
        private readonly List<int> scratchBindings = new List<int>();
        private readonly List<int> targetIds = new List<int>();
        private bool built;

        public List<SkinnedMeshRenderer> Meshes => meshes;

        public List<HoSpringSource> Sources => sources;

        public bool WritingEnabled
        {
            get => writingEnabled;
            set => writingEnabled = value;
        }

        public IReadOnlyList<string> MissingKeys => writer.MissingKeys;

        public int BindingCount => writer.BindingCount;

        public int TargetCount => writer.TargetCount;

        public int MeshCount => writer.MeshCount;

        public SkinnedMeshRenderer GetMesh(int index) => writer.GetMesh(index);

        public bool KeyExists(string keyName) => writer.KeyExists(keyName);

        public int CountKeyBindings(string keyName) => writer.CountKeyBindings(keyName);

        /// <summary>把弹簧按当前输入复位（原地收起来，不从 0 冲一下）。</summary>
        public void ResetSprings()
        {
            for (int i = 0; i < springs.Count; i++)
            {
                HoFaceJellyState state = springs[i];
                HoFaceJelly.Reset(ref state, 0.0f);
                springs[i] = state;
            }
        }

        /// <summary>重建绑定（网格列表或目标变了以后调用）。</summary>
        public void Rebuild()
        {
            built = false;
            writer.RestoreWritten();
            writer.Reset();
            springs.Clear();
            axisTargetStart.Clear();
            sourceBindingStart.Clear();
            sourceBindingCount.Clear();
            Build();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Build();
            }
        }

        private void OnDisable()
        {
            if (!built)
            {
                return;
            }

            writer.RestoreWritten();
            writer.Reset();
            built = false;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Step(Time.deltaTime);
        }

        /// <summary>
        /// 推进一步。**抽成公开方法是为了可测**：纯逻辑留在里面，Unity 的胶水只剩
        /// "什么时候调它"（`LateUpdate` + 播放模式判断）。这样才能在编辑期直接喂帧验证
        /// "读一个键 → 弹簧 → 写另一个键"整条链路，而不是必须进播放模式肉眼看。
        /// </summary>
        public void Step(float deltaTime)
        {
            if (!built)
            {
                Build();
            }

            if (!built || !writingEnabled || sources.Count == 0)
            {
                return;
            }

            // 网格被换掉（换装、换模型）就重建，否则会往旧网格上写。
            if (writer.MeshesChanged())
            {
                Rebuild();
                if (!built)
                {
                    return;
                }
            }

            writer.Snapshot();

            int axisIndex = 0;
            for (int s = 0; s < sources.Count; s++)
            {
                HoSpringSource source = sources[s];
                float input = ReadSource(s);
                for (int a = 0; a < source.Axes.Count; a++, axisIndex++)
                {
                    HoSpringAxis axis = source.Axes[a];
                    HoFaceJellyState state = springs[axisIndex];
                    HoFaceJelly.Step(ref state, input * axis.Gain, deltaTime, axis.Frequency, axis.Damping);
                    springs[axisIndex] = state;

                    int start = axisTargetStart[axisIndex];
                    for (int t = 0; t < axis.Targets.Count; t++)
                    {
                        HoSpringTarget target = axis.Targets[t];
                        if (target == null || target.Target == null)
                        {
                            continue;
                        }

                        // 弹簧会过冲到 1 以上、回弹到 0 以下。正反向各取一半，
                        // 于是"挤压"和"回弹"是两个不同的键，而不是同一路被钳成 0。
                        writer.Apply(start + t, Driver(state.value, target.Reversed), deltaTime);
                    }
                }
            }

            writer.Write();
        }

        /// <summary>这一路的输入：它的键里**最闭的那个**（双眼键模型只有一个键，左右键模型取较大者）。</summary>
        private float ReadSource(int sourceIndex)
        {
            float best = 0.0f;
            int start = sourceBindingStart[sourceIndex];
            int count = sourceBindingCount[sourceIndex];
            for (int i = 0; i < count; i++)
            {
                // 读外部基准（writePending = false）：切断自反馈 —— 这个组件写出去的键，
                // 下一帧不会被自己当成输入读回来。
                float value = writer.ReadBinding(start + i, false);
                if (value > best)
                {
                    best = value;
                }
            }

            return Mathf.Clamp01(best / 100.0f);
        }

        private void Build()
        {
            springs.Clear();
            axisTargetStart.Clear();
            sourceBindingStart.Clear();
            sourceBindingCount.Clear();
            targetIds.Clear();
            scratchBindings.Clear();

            var renderers = new List<Renderer>();
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null)
                {
                    renderers.Add(meshes[i]);
                }
            }

            if (renderers.Count == 0)
            {
                built = false;
                return;
            }

            writer.WriteThreshold = writeThreshold;
            writer.WriteEnabled = writingEnabled;
            writer.MergeMode = mergeMode;
            writer.BeginBuild(renderers);

            for (int s = 0; s < sources.Count; s++)
            {
                HoSpringSource source = sources[s];
                int bindingStart = scratchBindings.Count;
                for (int k = 0; k < source.KeyNames.Count; k++)
                {
                    writer.RegisterReadKey(source.KeyNames[k], scratchBindings);
                }

                sourceBindingStart.Add(bindingStart);
                sourceBindingCount.Add(scratchBindings.Count - bindingStart);

                for (int a = 0; a < source.Axes.Count; a++)
                {
                    HoSpringAxis axis = source.Axes[a];
                    int targetStart = targetIds.Count;
                    for (int t = 0; t < axis.Targets.Count; t++)
                    {
                        HoSpringTarget target = axis.Targets[t];
                        targetIds.Add(target != null && target.Target != null ? writer.RegisterTarget(target.Target) : -1);
                    }

                    axisTargetStart.Add(targetStart);
                    springs.Add(default);
                }
            }

            writer.EndBuild();
            built = true;
        }

        /// <summary>
        /// 弹簧值 → 驱动值：正向目标吃正半周（挤压，含过冲），反向目标吃负半周（回弹）。
        /// **纯函数**，所以"回弹真的会被送到别的键上"这条可以直接断言，不用跑动画去肉眼看。
        /// 过冲（> 1）**不在这里夹掉** —— 它就是果冻那一下，交给目标的 增益 / 输出上限 去处理。
        /// </summary>
        public static float Driver(float springValue, bool reversed) =>
            reversed ? Mathf.Max(0.0f, -springValue) : Mathf.Max(0.0f, springValue);

        /// <summary>把果冻预设加进来（结构 + 驱动键；目标键留空由用户接）。</summary>
        public HoSpringSource AddJellyEyePreset(
            float frequencyX = HoSpringPresets.JellyFrequencyX,
            float frequencyY = HoSpringPresets.JellyFrequencyY,
            float damping = HoSpringPresets.JellyDamping)
        {
            HoSpringSource source = HoSpringPresets.JellyEye(meshes, frequencyX, frequencyY, damping);
            sources.Add(source);
            return source;
        }

        /// <summary>诊断用：某个自由度的目标当前输出（面板横条读它）。</summary>
        public float GetTargetOutput(int axisIndex, int targetIndex)
        {
            if (axisIndex < 0 || axisIndex >= axisTargetStart.Count)
            {
                return 0.0f;
            }

            int flat = axisTargetStart[axisIndex] + targetIndex;
            return flat < 0 || flat >= targetIds.Count || targetIds[flat] < 0
                ? 0.0f
                : writer.GetTargetOutput(targetIds[flat]);
        }
    }

    /// <summary>
    /// 预设：搭结构 + 给驱动键（沿用眨眼约束那条"预设只做这两件事"的约定）。
    ///
    /// **第一个预设是果冻眼**，而且它是唯一一个不需要用户先想清楚"读什么"的预设：
    /// 眼睛的输入键是固定的那一族，两个自由度的频率也是调好的。
    /// </summary>
    public static class HoSpringPresets
    {
        /// <summary>横向频率。两个轴频率不同，参数才走得出 Lissajous 而不是一根直线来回。</summary>
        public const float JellyFrequencyX = 6.0f;

        public const float JellyFrequencyY = 8.5f;

        /// <summary>阻尼比 0.25：会过冲、会回弹，也就是"果冻"。</summary>
        public const float JellyDamping = 0.25f;

        /// <summary>
        /// 果冻眼：一路输入（眨眼）驱动两个自由度。
        ///
        /// 输入键先按 <see cref="HoBlinkKeyTable"/> 的"眼睑闭合"语义找（VRM `Blink`、MMD `まばたき`、
        /// VRChat `vrc.blink`……），**再补 ARKit 的 `eyeBlinkLeft` / `eyeBlinkRight`** ——
        /// 面捕落下来的值就是落在模型自己的键上的，但整套 ARKit 命名的模型也很常见，
        /// 而那张表是按 VRM/MMD 规范命名的，不含 ARKit。
        /// </summary>
        public static HoSpringSource JellyEye(IEnumerable<SkinnedMeshRenderer> meshes,
            float frequencyX = JellyFrequencyX, float frequencyY = JellyFrequencyY, float damping = JellyDamping)
        {
            var source = new HoSpringSource("眨眼");
            foreach (HoBlinkKeyEntry entry in HoBlinkKeyTable.BySemantic(HoBlinkKeySemantic.EyelidClosed))
            {
                AddIfPresent(source, meshes, entry.Name);
            }

            AddIfPresent(source, meshes, "eyeBlinkLeft");
            AddIfPresent(source, meshes, "eyeBlinkRight");

            // 两个自由度：频率不同 → 参数在两个方向上不同步 → 轨迹是 Lissajous。
            source.Axes.Add(new HoSpringAxis("横向", frequencyX, damping));
            source.Axes.Add(new HoSpringAxis("纵向", frequencyY, damping));
            return source;
        }

        private static void AddIfPresent(HoSpringSource source, IEnumerable<SkinnedMeshRenderer> meshes, string keyName)
        {
            if (Exists(meshes, keyName) && !source.KeyNames.Contains(keyName))
            {
                source.KeyNames.Add(keyName);
            }
        }

        private static bool Exists(IEnumerable<SkinnedMeshRenderer> meshes, string keyName)
        {
            if (meshes == null)
            {
                return false;
            }

            foreach (SkinnedMeshRenderer mesh in meshes)
            {
                if (mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.GetBlendShapeIndex(keyName) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
