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
        /// <summary>
        /// 输出上限的默认值：**90 而不是 100**。果冻的全部意义就在超调，而钳在 100 上就看不见它；
        /// 留 10 的余量，过冲才露得出来（`HoBlinkConstraint` 的文档里也是这条建议）。
        /// </summary>
        public const float DefaultOutputMax = 90.0f;

        [SerializeField] private HoShapeKeyTarget target = new HoShapeKeyTarget();
        [SerializeField] private bool reversed;

        public HoShapeKeyTarget Target => target;

        /// <summary>true = 吃负半周（回弹侧）。</summary>
        public bool Reversed => reversed;

        public HoSpringTarget() { }

        public HoSpringTarget(string keyName, float gain, bool reversed = false, float outputMax = DefaultOutputMax)
        {
            target = HoShapeKeyTarget.CreateRuntime(keyName, gain, HoShapeKeyRampPreset.Direct, outputMax);
            this.reversed = reversed;
        }
    }

    /// <summary>
    /// <b>弹簧驱动</b>：读形态键 → 过阻尼弹簧 → 写形态键。Live2D 的"物理演算"在我们这边的对应物。
    ///
    /// **一个组件 = 一根弹簧。** 需要几个自由度就挂几个组件 —— 果冻眼就是两个（横向 / 纵向，
    /// 频率不同才有 Lissajous）。这不是为了省代码，是为了让**结构扁平**：组件里只有两层列表
    /// （读哪些键、写哪些键），没有"规则套自由度套目标"这种三层嵌套。挂组件是 Unity 用户
    /// 最熟的动作，而三级折叠的 Inspector 是谁都不想碰的。
    ///
    /// **它为什么不是控制器里的东西**（定案 19）：混合树不能输出带物理的参数，而且混合树是状态机里的
    /// 东西 —— 所以物理参数的生产跟状态机没关系；它又是纯值消费，消费场景也不需要待在状态机里。
    /// 两头都不需要状态机，于是这一整件事就是一个独立组件。
    ///
    /// **输入走"读已经落下来的键"**：它跟面捕完全解耦 —— 面捕、基础动画、别人写的键，
    /// 谁写进去的都行；读的那一帧值就是它看到的信号。读的和写的可以是同一批键而不打架：
    /// 写之前先 `Snapshot()`，读的时候走外部基准，切断自反馈。
    ///
    /// **写入靠 <see cref="HoShapeKeyWriter"/>**：键解析、ramp、增益/偏移、范围钳制、叠加/覆盖、
    /// 饱和合并、恢复原值、以及**占用表检查**（`HoFaceOutputOwnership`）全是现成的 ——
    /// 面捕正占着的键它不会去抢。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Constraints/Ho Spring Constraint")]
    public sealed class HoSpringConstraint : MonoBehaviour, IHoShapeKeyMeshProvider
    {
        [SerializeField] private List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();

        [Tooltip("读哪些形态键做输入（取其中最大的那个）。双眼键模型填一个，左右键模型填两个。")]
        [SerializeField] private List<string> keyNames = new List<string>();

        [Tooltip("跟进多快。果冻眼横向 6、纵向 8.5 —— 两个自由度频率不同，轨迹才不是一根直线来回。")]
        [SerializeField, Min(0.01f)] private float frequency = 6.0f;

        [Tooltip("回弹多少。0.25 有明显果冻感；1 是临界阻尼，完全不超调。")]
        [SerializeField, Range(0.0f, 4.0f)] private float damping = 0.25f;

        [Tooltip("输入增益。1 表示输入满值就驱动到满。")]
        [SerializeField] private float gain = 1.0f;

        [SerializeField] private List<HoSpringTarget> targets = new List<HoSpringTarget>();
        [SerializeField] private bool writingEnabled = true;
        [SerializeField] private float writeThreshold = 0.01f;
        [SerializeField] private HoShapeKeyMergeMode mergeMode = HoShapeKeyMergeMode.Saturate;

        private readonly HoShapeKeyWriter writer = new HoShapeKeyWriter();
        private readonly List<int> readBindings = new List<int>();
        private readonly List<int> targetIds = new List<int>();
        private HoFaceJellyState spring;
        private bool built;

        public List<SkinnedMeshRenderer> Meshes => meshes;

        public List<string> KeyNames => keyNames;

        public List<HoSpringTarget> Targets => targets;

        public float Frequency => frequency;

        public float Damping => damping;

        public float Gain => gain;

        /// <summary>弹簧当前值。0 = 静止，正 = 挤压（会过冲到 1 以上），负 = 回弹。</summary>
        public float SpringValue { get; private set; }

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

        public string DescribeKeyBindings(string keyName, int maxNames = 3) => writer.DescribeKeyBindings(keyName, maxNames);

        /// <summary>
        /// 当前输入值（0~1）。取所有输入键里**最大的那个** —— 双眼键模型只有一个键，
        /// 左右键模型两个键，两种都不用改配置。
        /// </summary>
        public float ReadInput()
        {
            if (!built)
            {
                return 0.0f;
            }

            float best = 0.0f;
            for (int i = 0; i < readBindings.Count; i++)
            {
                // 读外部基准（writePending = false）：切断自反馈 —— 这个组件写出去的键，
                // 下一帧不会被自己当成输入读回来。
                float value = writer.ReadBinding(readBindings[i], false);
                if (value > best)
                {
                    best = value;
                }
            }

            return Mathf.Clamp01(best / 100.0f);
        }

        /// <summary>把弹簧按当前输入复位（原地收起来，不从 0 冲一下）。</summary>
        public void ResetSpring()
        {
            HoFaceJelly.Reset(ref spring, 0.0f);
            SpringValue = 0.0f;
        }

        /// <summary>重建绑定（网格列表、输入键或目标变了以后调用）。</summary>
        public void Rebuild()
        {
            built = false;
            writer.RestoreWritten();
            writer.Reset();
            Build();
        }

        /// <summary>
        /// 把**本组件**配成"果冻眼"的某一路：跟随 / 回弹 / 增益 / 输入键；目标为空时补两行（挤压 / 回弹）。
        ///
        /// **只改自己 —— 不碰场景里别的组件，也不去加组件。** 预设的语义就是"把这台的参数填成某个样子"；
        /// 要两个自由度就自己再挂一根（一个组件 = 一根弹簧），那是用户的动作，不是预设的动作。
        /// </summary>
        public void ApplyJellyPreset(bool vertical)
        {
            frequency = vertical ? HoSpringPresets.JellyFrequencyY : HoSpringPresets.JellyFrequencyX;
            damping = HoSpringPresets.JellyDamping;
            gain = 1.0f;

            // 预设的职责之一是"给驱动键"：能匹配到眨眼键就填上，匹配不到就别把用户已经选的清掉。
            List<string> matched = HoSpringPresets.JellyInputKeys(meshes);
            if (matched.Count > 0)
            {
                keyNames = matched;
            }

            if (targets.Count == 0)
            {
                targets.Add(new HoSpringTarget(string.Empty, 1.0f, false));
                targets.Add(new HoSpringTarget(string.Empty, 1.0f, true));
            }

            Rebuild();
        }

        /// <summary>预设/脚本用的整体配置。</summary>
        public void Configure(List<SkinnedMeshRenderer> meshList, List<string> readKeys,
            float frequencyHz, float dampingRatio, float inputGain)
        {
            meshes = meshList ?? new List<SkinnedMeshRenderer>();
            keyNames = readKeys ?? new List<string>();
            frequency = Mathf.Max(0.01f, frequencyHz);
            damping = Mathf.Clamp(dampingRatio, 0.0f, 4.0f);
            gain = inputGain;
            Rebuild();
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

            if (!built || !writingEnabled)
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

            float input = ReadInput();
            HoFaceJelly.Step(ref spring, input * gain, deltaTime, frequency, damping);
            SpringValue = spring.value;

            writer.Snapshot();
            for (int t = 0; t < targets.Count; t++)
            {
                HoSpringTarget target = targets[t];
                if (target == null || target.Target == null || t >= targetIds.Count || targetIds[t] < 0)
                {
                    continue;
                }

                // 弹簧会过冲到 1 以上、回弹到 0 以下。正反向各取一半，
                // 于是"挤压"和"回弹"是两个不同的键，而不是同一路被钳成 0。
                writer.Apply(targetIds[t], Driver(spring.value, target.Reversed), deltaTime);
            }

            writer.Write();
        }

        /// <summary>
        /// 弹簧值 → 驱动值：正向目标吃正半周（挤压，含过冲），反向目标吃负半周（回弹）。
        /// **纯函数**，所以"回弹真的会被送到别的键上"这条可以直接断言，不用跑动画去肉眼看。
        /// 过冲（> 1）**不在这里夹掉** —— 它就是果冻那一下，交给目标的 增益 / 输出上限 去处理。
        /// </summary>
        public static float Driver(float springValue, bool reversed) =>
            reversed ? Mathf.Max(0.0f, -springValue) : Mathf.Max(0.0f, springValue);

        private void Build()
        {
            readBindings.Clear();
            targetIds.Clear();

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

            for (int i = 0; i < keyNames.Count; i++)
            {
                writer.RegisterReadKey(keyNames[i], readBindings);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                HoSpringTarget target = targets[i];
                targetIds.Add(target != null && target.Target != null ? writer.RegisterTarget(target.Target) : -1);
            }

            writer.EndBuild();
            built = true;
        }

        /// <summary>诊断用：第几个目标的当前输出（面板横条读它）。</summary>
        public float GetTargetOutput(int targetIndex)
        {
            return targetIndex < 0 || targetIndex >= targetIds.Count || targetIds[targetIndex] < 0
                ? 0.0f
                : writer.GetTargetOutput(targetIds[targetIndex]);
        }
    }

    /// <summary>
    /// 预设：搭结构 + 给驱动键（沿用眨眼约束那条"预设只做这两件事"的约定）。
    ///
    /// **第一个预设是果冻眼**，而且它是唯一一个不需要用户先想清楚"读什么"的预设：
    /// 眼睛的输入键是固定的那一族，两个自由度的频率也是调好的。
    /// 一个组件 = 一根弹簧，所以果冻眼 = **两个组件**。
    /// </summary>
    public static class HoSpringPresets
    {
        /// <summary>横向频率。两个轴频率不同，参数才走得出 Lissajous 而不是一根直线来回。</summary>
        public const float JellyFrequencyX = 6.0f;

        public const float JellyFrequencyY = 8.5f;

        /// <summary>阻尼比 0.25：会过冲、会回弹，也就是"果冻"。</summary>
        public const float JellyDamping = 0.25f;

        /// <summary>
        /// 果冻眼的输入键：先按 <see cref="HoBlinkKeyTable"/> 的"眼睑闭合"语义找
        /// （VRM `Blink`、MMD `まばたき`、VRChat `vrc.blink`……），**再补 ARKit 的
        /// `eyeBlinkLeft` / `eyeBlinkRight`** —— 面捕落下来的值落在模型自己的键上，
        /// 但整套 ARKit 命名的模型也很常见，而那张表是按 VRM/MMD 规范命名的、不含 ARKit。
        /// </summary>
        public static List<string> JellyInputKeys(IEnumerable<SkinnedMeshRenderer> meshes)
        {
            var keys = new List<string>();
            foreach (HoBlinkKeyEntry entry in HoBlinkKeyTable.BySemantic(HoBlinkKeySemantic.EyelidClosed))
            {
                AddIfPresent(keys, meshes, entry.Name);
            }

            AddIfPresent(keys, meshes, "eyeBlinkLeft");
            AddIfPresent(keys, meshes, "eyeBlinkRight");
            return keys;
        }

        private static void AddIfPresent(List<string> keys, IEnumerable<SkinnedMeshRenderer> meshes, string keyName)
        {
            if (keys.Contains(keyName) || !Exists(meshes, keyName))
            {
                return;
            }

            keys.Add(keyName);
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
