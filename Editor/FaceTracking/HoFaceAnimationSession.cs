using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 面捕驱动会话。核心决定：**不接管角色的 Animator**。
    ///
    /// 为什么不用 PlayableGraph 把「身体层 + 面部层」叠起来：
    /// 面捕需要的是"只覆盖它拥有的形态键，其它一律别碰"。Unity 的 AnimationLayerMixerPlayable
    /// 表达不了这件事 —— 覆盖层会把图层自己没动的属性也一并写成默认值（实测：基础动画写的
    /// eyeLookInLeft=33 被面部层压成 0），而 AvatarMask 根本不支持形态键，没法按属性设遮罩；
    /// 改成加算层又会变成"基础 17 + 面捕 60"，而且 Unity 有"两个 AnimatorControllerPlayable
    /// 第二个设加算会串动画"的已知问题。所以两层 Playable 合成"身体 + 面捕"这条路本身不成立。
    ///
    /// 这里走"影子求值 + 只写拥有的键"：
    ///   1. 一份隐藏的镜像层级 + 独立 Animator 跑面部 Controller，混合树照常被 Unity 真实求值；
    ///   2. 动画求值之后，把影子上算出来的形态键值抄到角色真实 Renderer，且只抄面捕拥有的键。
    /// 角色的 Animator / Controller / 其它任何动画都不受影响，LookAt 与 HoBlink 照常工作。
    /// </summary>
    public sealed class HoFaceAnimationSession : IDisposable
    {
        public readonly HoFaceTrackingDebugger Rig;
        public HoFaceCompiledController Compiled { get; private set; }
        public readonly float[] Effective = new float[52];
        /// <summary>输入侧整形之后的值（输入曲线 + 断流回中性 + 双眼同步）。**表达式读的就是它。**</summary>
        public readonly float[] Input = new float[52];
        /// <summary>每一行输出最后写出去的值（面板上「Controller 实值」这一列读它）。</summary>
        public readonly float[] ControllerValues = new float[52];
        /// <summary>按参数名读某一行最后的输出值（用例与面板用；没有这一行时返回 <see cref="float.NaN"/>）。</summary>
        public float OutputValue(string parameter) =>
            parameter != null && outputIndex.TryGetValue(parameter, out int row) ? outputValues[row] : float.NaN;
        private readonly Dictionary<string, int> outputIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>某一行输出对应哪个通道（参数名正好是 `ARKit/&lt;键&gt;` 时）—— 面板的「实值」列用它。</summary>
        private readonly int[] arkitRow = new int[52];
        private HoFaceOutput[] outputs = new HoFaceOutput[0];
        private HoFaceExpression[] expressions = new HoFaceExpression[0];
        private float[] outputValues = new float[0];
        private float[] outputSmooth = new float[0];
        private int[] stepIndex = new int[0];
        private double[] stepUntil = new double[0];
        private readonly HoFaceInputMode[] lastModes = new HoFaceInputMode[52];
        private readonly bool[] selected = new bool[52];
        private readonly float[] held = new float[52];
        private readonly Animator animator;
        private readonly Dictionary<(SkinnedMeshRenderer, int), HoFaceBinding> owned = new Dictionary<(SkinnedMeshRenderer, int), HoFaceBinding>();
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> meshRefs = new Dictionary<SkinnedMeshRenderer, Mesh>();
        private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> shadows = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
        private readonly HashSet<string> parameters = new HashSet<string>(StringComparer.Ordinal);
        private GameObject shadowRoot;
        private Animator shadow;
        private RuntimeAnimatorController runningController;
        private string mappingStamp;
        private bool disposed;
        private bool configured;
        private bool written;
        /// <summary>会话第一帧的标记：那一帧把所有值一次到位，避免开场从 0 扫过来。</summary>
        private bool primed;
        private readonly Dictionary<string, float> previews = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// 调试预览：外部（混合树小工具的滑条）直接指定某个参数，在所有生产逻辑之后覆盖。
        ///
        /// 走这条路而不是直接 `animator.SetFloat`，是因为**预览必须走完整条管线** ——
        /// 参数 → 影子上的混合树 → 被占用的键 → 真模型。否则预览看到的和实际跑起来看到的不是一回事。
        /// </summary>
        public void SetPreview(string parameter, float value)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            previews[parameter] = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        public void ClearPreviews() => previews.Clear();

        public HoFaceAnimationSession(HoFaceTrackingDebugger rig)
        {
            Rig = rig;
            animator = rig.targetAnimator;
            if (!EditorApplication.isPlaying || animator == null || !rig.isActiveAndEnabled || !animator.isActiveAndEnabled)
                throw new InvalidOperationException("请进入播放模式，并启用角色组件和 Animator。");
            // 不去碰 animator.runtimeAnimatorController，所以也不用限制 Animator 的更新模式：
            // 影子 Animator 用自己的默认更新，角色怎么更新是角色自己的事。
            try
            {
                // Validate even when no phone frames have arrived yet.
                using (var check = HoFaceAnimationAssets.Compile(rig)) { }
                BuildShadow();
                Tick(0);
            }
            catch { Dispose(); throw; }
        }

        // ── 影子求值台 ────────────────────────────────────────────────────────

        private void BuildShadow()
        {
            shadowRoot = new GameObject("Ho Face Shadow") { hideFlags = HideFlags.HideAndDontSave };
            shadow = shadowRoot.AddComponent<Animator>();
            shadow.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // 登记给调试器（混合树观察台）：影子台是隐藏对象，调试组件自己找不到它。
            // 这是调试接入的全部代价 —— 一行，且不改任何生产逻辑。
            HoFaceShadowLink.Register(shadow);
        }

        /// <summary>按编译结果的绑定路径搭出镜像层级，每格挂一个只被驱动、不上屏的 SkinnedMeshRenderer。</summary>
        private void BuildProxies()
        {
            foreach (var binding in Compiled.bindings)
            {
                if (binding.renderer == null || shadows.ContainsKey(binding.renderer)) continue;
                Transform node = shadowRoot.transform;
                if (!string.IsNullOrEmpty(binding.path))
                {
                    foreach (string part in binding.path.Split('/'))
                    {
                        Transform child = node.Find(part);
                        if (child == null)
                        {
                            child = new GameObject(part) { hideFlags = HideFlags.HideAndDontSave }.transform;
                            child.SetParent(node, false);
                        }
                        node = child;
                    }
                }
                var proxy = node.gameObject.AddComponent<SkinnedMeshRenderer>();
                proxy.sharedMesh = binding.renderer.sharedMesh;
                // 只用来被动画驱动取值，绝不参与渲染或包围盒计算。
                proxy.forceRenderingOff = true;
                proxy.updateWhenOffscreen = false;
                shadows[binding.renderer] = proxy;
            }
        }

        // ── 每帧驱动 ──────────────────────────────────────────────────────────

        public void Tick(float deltaTime)
        {
            if (disposed) return;
            if (animator == null || Rig.targetAnimator != animator || !animator.isActiveAndEnabled)
                throw new InvalidOperationException("目标 Animator 已变更或禁用，会话已停止。");
            foreach (var entry in meshRefs)
                if (entry.Key == null || entry.Key.sharedMesh != entry.Value) throw new InvalidOperationException("模型 Mesh 已变更，请重新检查绑定后启动。");
            bool changed = !configured;
            var nextSelected = new bool[52];
            double now = IFacialMocapReceiver.Now;
            foreach (var channel in Rig.channels)
            {
                if (channel == null) continue;
                int index = HoFaceTrackingChannels.IndexOf(channel.shape);
                if (index < 0) continue;
                float fade = Mathf.Max(0.01f, Rig.neutralFadeSeconds);
                double age = now - HoFaceInputHub.ReceivedAt[index];
                bool fresh = HoFaceInputHub.Connected && age <= Mathf.Max(0.1f, Rig.staleSeconds);
                // Never received live channels do not reserve model properties.
                bool mayWrite = channel.mode != HoFaceInputMode.Live ||
                    (HoFaceInputHub.ReceivedAt[index] > 0 && age <= Mathf.Max(0.1f, Rig.staleSeconds) + fade);
                if (channel.mode != lastModes[index] && channel.mode == HoFaceInputMode.Hold) held[index] = Effective[index];
                lastModes[index] = channel.mode;
                float neutral = Finite01(channel.neutral);
                switch (channel.mode)
                {
                    case HoFaceInputMode.Manual: Effective[index] = Finite01(channel.manual); break;
                    case HoFaceInputMode.Hold: Effective[index] = held[index]; break;
                    case HoFaceInputMode.Neutral: Effective[index] = neutral; break;
                    case HoFaceInputMode.Release: break;
                    default:
                        // 输入曲线只作用在实时输入上：手动滑杆是调试用的，不该被它整形。
                        float live = HoFaceCurve.Transfer(channel.inputCurve, Finite01(HoFaceInputHub.Raw[index]));
                        Effective[index] = fresh ? live : Mathf.MoveTowards(Effective[index], neutral, deltaTime / fade);
                        break;
                }
                nextSelected[index] = mayWrite && channel.mode != HoFaceInputMode.Release && (Rig.outputRegions & HoFaceTrackingChannels.Region(channel.shape)) != 0;
                // 输入侧到此为止（模式 / 输入曲线 / 断流回中性）。**平滑不在这里** ——
                // 它现在是每一行输出自己的修饰符（照 VBridger 的粒度）：要平滑哪一路就在那一行加。
                Input[index] = Effective[index];
            }
            primed = true;
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != nextSelected[i]) changed = true;
                selected[i] = nextSelected[i];
            }
            string stamp = MappingStamp();
            if (changed || runningController != Rig.faceController || stamp != mappingStamp)
            {
                Rebuild();
                mappingStamp = stamp;
                configured = true;
            }
            // 双眼同步：跨眼合并作用在**输入**上，必须在表达式之前 —— 表达式读的就是这一组值。
            HoFaceEyeSync.Apply(Input, Rig.eyeSync, Rig.eyeSyncMix, Rig.eyeSyncSingleKey);

            // ── 参数生产：每一行 = 曲线(表达式(源键…))，再走它自己那串有序修饰符 ──────────
            // 这里**不再有**"某个键写某个参数"的硬编码：参数名与算法都在中间层资产里，
            // 控制器里没有那个参数名就跳过（不猜也不补）。轴也是普通一行：
            // `Ho/Drive/Lid/Left/BlinkWide = eyeBlinkLeft - eyeWideLeft`。
            double frameNow = IFacialMocapReceiver.Now;
            for (int row = 0; row < outputs.Length; row++)
            {
                var output = outputs[row];
                if (output == null) continue;
                float value = expressions[row] != null ? expressions[row].Evaluate(Lookup) : 0f;
                value = output.Transform(value);
                value = ApplyModifiers(row, output, value, Mathf.Max(0f, deltaTime), frameNow);
                outputValues[row] = value;
                if (parameters.Contains(output.parameter)) shadow.SetFloat(output.parameter, value);
            }

            // 面板的「Controller 实值」列：参数名正好是 `ARKit/<键>` 的行，就把它显示在那一行上。
            for (int index = 0; index < arkitRow.Length; index++)
                ControllerValues[index] = arkitRow[index] >= 0 ? outputValues[arkitRow[index]] : 0f;

            // 区域门控：把"这块驱动算不算数"写成一个**参数**（而不是靠重新生成控制器来切）。
            // 于是它也能被别的东西驱动 —— 用户自己的层、以后的菜单、AFK 之类。
            WriteGate(HoFaceNaming.Gate(HoFaceGate.Eye), (Rig.outputRegions & HoFaceTrackingChannels.EyeRegion) != 0);
            WriteGate(HoFaceNaming.Gate(HoFaceGate.Lip), (Rig.outputRegions & HoFaceTrackingChannels.LipRegion) != 0);
            foreach (var preview in previews)
                if (parameters.Contains(preview.Key)) shadow.SetFloat(preview.Key, preview.Value);
        }

        /// <summary>表达式取变量：源键名 → 输入侧整形后的值。未知名字按 0（表达式求值器不抛异常）。</summary>
        private float Lookup(string shape)
        {
            int index = HoFaceTrackingChannels.IndexOf(shape);
            return index >= 0 ? Input[index] : 0f;
        }

        /// <summary>区域门控：参数在控制器里才写。</summary>
        private void WriteGate(string parameter, bool open)
        {
            if (!string.IsNullOrEmpty(parameter) && parameters.Contains(parameter))
                shadow.SetFloat(parameter, open ? 1f : 0f);
        }

        /// <summary>
        /// 有序修饰符。按列出顺序生效（照 VBridger 的输出修饰符）：
        /// 平滑 / 分档（延迟**还没实现**，面板会标出来）。
        /// </summary>
        private float ApplyModifiers(int row, HoFaceOutput output, float value, float deltaTime, double now)
        {
            if (output.modifiers == null || output.modifiers.Count == 0) return value;
            for (int i = 0; i < output.modifiers.Count; i++)
            {
                var modifier = output.modifiers[i];
                if (modifier == null || !modifier.Active) continue;
                switch (modifier.kind)
                {
                    case HoFaceModifierKind.Smooth:
                        // 只有**会话第一帧**做一次性初始化，免得开场从 0 扫过来。
                        outputSmooth[row] = !primed
                            ? value
                            : Mathf.Lerp(outputSmooth[row], value, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / modifier.seconds));
                        value = outputSmooth[row];
                        break;
                    case HoFaceModifierKind.Steps:
                        value = Step(row, modifier, value, now);
                        break;
                    default:
                        break;   // 延迟：数据留位，未实现（面板上标出来）
                }
            }

            return value;
        }

        /// <summary>
        /// 分档：参数过 <c>trigger</c> 就跳到 <c>target</c>，往下掉超过 <c>threshold</c> 才退回去，
        /// 触发后至少保持 <c>hold</c> 秒。没触发任何档时输出 0（等于隐含的"最小档"）。
        /// </summary>
        private float Step(int row, HoFaceModifier modifier, float value, double now)
        {
            var steps = modifier.steps;
            if (steps == null || steps.Count == 0) return value;

            int next = -1;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null && value >= steps[i].trigger) next = i;

            int current = stepIndex[row];
            if (current >= 0 && current < steps.Count && steps[current] != null)
            {
                if (now < stepUntil[row]) next = current;                       // 最短保持
                else
                {
                    float release = steps[current].trigger - Mathf.Abs(steps[current].threshold);
                    if (value >= release && next < current) next = current;      // 迟滞：没掉够就不退
                }
            }

            if (next != current)
            {
                stepIndex[row] = next;
                stepUntil[row] = next >= 0 && steps[next] != null ? now + Mathf.Max(0f, steps[next].hold) : 0.0;
            }

            return next >= 0 && steps[next] != null ? steps[next].target : 0f;
        }

        /// <summary>
        /// 动画求值之后调用（角色的 LateUpdate 阶段）：把影子的结果抄到真实模型上，只抄拥有的键。
        /// 放在动画之后是为了拿到本帧刚算完的值，不做任何缓存或延迟一帧的近似。
        /// </summary>
        public void WriteOutputs()
        {
            if (disposed || !configured) return;
            foreach (var pair in owned)
            {
                var binding = pair.Value;
                if (binding.renderer == null || !shadows.TryGetValue(binding.renderer, out var proxy) || proxy == null) continue;
                binding.renderer.SetBlendShapeWeight(binding.index, proxy.GetBlendShapeWeight(binding.index));
                written = true;
            }
        }

        private static float Finite01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);

        // 果冻不在这里。它曾经是"弹簧生产参数 → 控制器里的 2D 树消费参数"的两段式，
        // 现已整体搬到独立的 HoSpringConstraint（读键 → 弹簧 → 写键，不再借道 Animator 参数）。
        // 原因见 docs/archive/FACE_TRACKING_PIPELINE_SPLIT.md 19.2：混合树出不了物理参数，
        // 于是生产者与消费者本来就都不属于状态机 —— 两头都在外面，它就该是一个组件。

        /// <summary>
        /// 会话的"配置指纹"：换控制器、换驱动对象、**换/改配置文件**都算换了一份配置，要重建影子与输出行。
        ///
        /// 配置文件是同一个 TextAsset 时，光看 `GetInstanceID()` 不够 —— 内容被改写（窗口保存、
        /// 或者外部编辑器改了这个 .json）后实例可以不变。所以再带上"文本长度 + 解析出来的那个对象"
        /// （`Rig.Middleware` 在长度变化或 <see cref="HoFaceTrackingDebugger.ReloadProfile"/> 之后会重新解析）。
        /// </summary>
        private string MappingStamp()
        {
            var text = new System.Text.StringBuilder();
            text.Append("rows:").Append(Rig.Outputs().Count).Append(';');
            var middleware = Rig.Middleware;
            text.Append("profile:").Append(Rig.profile != null ? Rig.profile.GetInstanceID() : 0)
                .Append(':').Append(Rig.profile != null ? Rig.profile.text.Length : 0)
                .Append(':').Append(middleware != null ? middleware.GetHashCode() : 0).Append(';');
            text.Append("src:").Append(Rig.treeTemplate != null ? Rig.treeTemplate.GetInstanceID() : 0).Append(';');
            if (Rig.meshes != null)
                foreach (var mesh in Rig.meshes)
                    text.Append(mesh != null ? mesh.GetInstanceID() : 0).Append(';');
            return text.ToString();
        }

        /// <summary>把当前的输出行编译成"求值用"的数组（表达式解析一次，状态数组按行开）。</summary>
        private void BuildOutputs()
        {
            var rows = Rig.Outputs();
            outputs = new HoFaceOutput[rows.Count];
            expressions = new HoFaceExpression[rows.Count];
            outputValues = new float[rows.Count];
            outputSmooth = new float[rows.Count];
            stepIndex = new int[rows.Count];
            stepUntil = new double[rows.Count];
            outputIndex.Clear();
            for (int i = 0; i < 52; i++) arkitRow[i] = -1;
            for (int i = 0; i < stepIndex.Length; i++) stepIndex[i] = -1;   // −1 = 还没进任何档
            for (int i = 0; i < rows.Count; i++)
            {
                outputs[i] = rows[i];
                if (rows[i] == null) continue;
                if (HoFaceExpression.TryParse(rows[i].expression, out var parsed, out string error))
                    expressions[i] = parsed;
                else
                    Debug.LogWarning("[Ho 面捕] 第 " + (i + 1) + " 行的表达式用不了（" + rows[i].parameter + "）：" + error);
                if (!string.IsNullOrEmpty(rows[i].parameter) && !outputIndex.ContainsKey(rows[i].parameter))
                    outputIndex[rows[i].parameter] = i;
                string shape = rows[i].parameter != null && rows[i].parameter.StartsWith("ARKit/", StringComparison.Ordinal)
                    ? rows[i].parameter.Substring("ARKit/".Length)
                    : null;
                int channel = HoFaceTrackingChannels.IndexOf(shape);
                if (channel >= 0) arkitRow[channel] = i;
            }
        }

        private void Rebuild()
        {
            BuildOutputs();
            var next = HoFaceAnimationAssets.Compile(Rig, shape => { int i = HoFaceTrackingChannels.IndexOf(shape); return i >= 0 && selected[i]; });
            try
            {
                var nextKeys = new HashSet<(SkinnedMeshRenderer, int)>();
                foreach (var binding in next.bindings)
                {
                    var key = (binding.renderer, binding.index);
                    nextKeys.Add(key);
                    if (owned.TryGetValue(key, out var existing)) binding.initial = existing.initial;
                }
                // Release old properties before any new owner writes. Ho writers skip restoration while reserved.
                foreach (var pair in owned)
                    if (!nextKeys.Contains(pair.Key) && pair.Value.renderer != null)
                        pair.Value.renderer.SetBlendShapeWeight(pair.Value.index, pair.Value.initial);
                HoFaceOutputOwnership.Release(this);
                owned.Clear();
                meshRefs.Clear();
                // 先摘掉影子上的旧 Controller，再销毁它引用的临时资产，避免"资产已销毁但还挂着"。
                if (shadow != null) shadow.runtimeAnimatorController = null;
                DestroyProxies();
                Compiled?.Dispose();
                Compiled = next;
                foreach (var binding in next.bindings)
                {
                    HoFaceOutputOwnership.Reserve(binding.renderer, binding.index, this);
                    owned[(binding.renderer, binding.index)] = binding;
                    meshRefs[binding.renderer] = binding.renderer.sharedMesh;
                }
                BuildProxies();
                parameters.Clear();
                foreach (string name in next.floatParameters) parameters.Add(name);
                shadow.runtimeAnimatorController = next.controller;
                runningController = Rig.faceController;
                written = false;
            }
            catch { if (Compiled != next) next.Dispose(); throw; }
        }

        private void DestroyProxies()
        {
            foreach (var proxy in shadows.Values)
                if (proxy != null) UnityEngine.Object.DestroyImmediate(proxy);
            shadows.Clear();
            if (shadowRoot == null) return;
            for (int i = shadowRoot.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(shadowRoot.transform.GetChild(i).gameObject);
        }

        public string StateSummary => Compiled == null
            ? "未运行"
            : "影子网格 " + shadows.Count + "；输出绑定 " + owned.Count + (written ? "；已写入" : "；未写入");

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var binding in owned.Values)
                if (binding.renderer != null && meshRefs.TryGetValue(binding.renderer, out var mesh) && binding.renderer.sharedMesh == mesh)
                    binding.renderer.SetBlendShapeWeight(binding.index, binding.initial);
            HoFaceOutputOwnership.Release(this);
            owned.Clear();
            meshRefs.Clear();
            DestroyProxies();
            HoFaceShadowLink.Unregister(shadow);
            if (shadowRoot != null) UnityEngine.Object.DestroyImmediate(shadowRoot);
            shadowRoot = null;
            shadow = null;
            Compiled?.Dispose();
            Compiled = null;
        }
    }
}
