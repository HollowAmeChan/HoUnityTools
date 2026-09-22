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
        /// <summary>分组平滑之后、真正写进控制器参数的值。面板上「Controller 实值」这一列读的就是它。</summary>
        public readonly float[] Smoothed = new float[52];
        public readonly float[] ControllerValues = new float[52];
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
        private RuntimeAnimatorController sourceController;
        private string mappingStamp;
        private bool disposed;
        private bool configured;
        private bool written;
        /// <summary>会话第一帧的标记：那一帧把所有值一次到位，避免开场从 0 扫过来。</summary>
        private bool primed;
        private HoFaceJellyState jellyX, jellyY;
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

        /// <summary>果冻横向分量当前值（那个"带物理的参数"）。面板观测点，也是用例的观测点。</summary>
        public float JellyValueX => jellyX.value;

        /// <summary>果冻竖向分量当前值。</summary>
        public float JellyValueY => jellyY.value;

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
                        // 响应整形只作用在实时输入上：手动滑杆是调试用的，不该被死区吃掉。
                        float live = Finite01(HoFaceInputHub.Raw[index] * channel.gain);
                        float target = fresh ? Rig.ApplySensitivity(channel.shape, live) : neutral;
                        Effective[index] = fresh ? target : Mathf.MoveTowards(Effective[index], neutral, deltaTime / fade);
                        break;
                }
                nextSelected[index] = mayWrite && channel.mode != HoFaceInputMode.Release && (Rig.outputRegions & HoFaceTrackingChannels.Region(channel.shape)) != 0;
                // 分组平滑：Effective 是目标，Smoothed 才是真正写进控制器的那一个。
                // 只有**会话第一帧**做一次性初始化（免得开始时从 0 扫一遍）；之后一律走平滑。
                // 不需要"重新接管就快照"—— Smoothed 每帧都在跟进 Effective，本来就不会离得太远。
                float tau = Rig.SmoothSeconds(channel.shape);
                Smoothed[index] = !primed || tau <= 0.0001f
                    ? Effective[index]
                    : Mathf.Lerp(Smoothed[index], Effective[index], 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / tau));
            }
            primed = true;
            StepJelly(deltaTime);
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != nextSelected[i]) changed = true;
                selected[i] = nextSelected[i];
            }
            string stamp = MappingStamp();
            if (changed || sourceController != Rig.faceController || stamp != mappingStamp)
            {
                Rebuild();
                mappingStamp = stamp;
                configured = true;
            }
            foreach (var channel in Rig.channels)
            {
                int index = HoFaceTrackingChannels.IndexOf(channel.shape);
                if (index < 0 || !parameters.Contains(channel.parameter)) continue;
                shadow.SetFloat(channel.parameter, Smoothed[index]);
                ControllerValues[index] = shadow.GetFloat(channel.parameter);
            }
            // 双眼同步：必须在"写参数"之前、平滑之后 —— 它作用在最终要被写出去的那组值上。
            HoFaceEyeSync.Apply(Smoothed, Rig.eyeSync, Rig.eyeSyncMix, Rig.eyeSyncSingleKey);

            // 区域门控：把"这块驱动算不算数"写成一个**参数**（而不是靠重新生成控制器来切）。
            // 于是它也能被别的东西驱动 —— 用户自己的层、以后的菜单、AFK 之类。
            WriteGate(HoFaceAnimationAssets.EyeGateName, (Rig.outputRegions & HoFaceTrackingChannels.EyeRegion) != 0);
            WriteGate(HoFaceAnimationAssets.LipGateName, (Rig.outputRegions & HoFaceTrackingChannels.LipRegion) != 0);
            foreach (var preview in previews)
                if (parameters.Contains(preview.Key)) shadow.SetFloat(preview.Key, preview.Value);
        }

        private void WriteGate(string parameter, bool open)
        {
            if (!string.IsNullOrEmpty(parameter) && parameters.Contains(parameter))
                shadow.SetFloat(parameter, open ? 1f : 0f);
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

        /// <summary>
        /// 果冻眼：把眼睑信号过一遍一维弹簧，产出**两个方向**的带物理参数写进控制器。
        ///
        /// 为什么是两个方向而不是一个强度：两个弹簧取不同频率时，两个分量的合成路径是一条
        /// Lissajous 曲线 —— 读起来像有机运动，而不是一根直线来回。取相同频率就退化成直线。
        ///
        /// 这里只产参数。**消费它们是混合树**（2D FreeformCartesian，子节点是用户自己选的键，
        /// 由混合树小工具建），参数负责"每次都不一样"，树的形状负责"每次都好看"。
        ///
        /// **参数是有符号的。** 眼睑信号是一段脉冲（0→1→0，约 150ms），脉冲输入下弹簧不是"趋近 1
        /// 然后停住"，而是冲过 1 再**回弹到 0 以下**再收敛 —— 回弹才是果冻。所以这里不能夹到 0..1，
        /// 否则整个负半周被削掉，剩下的只是一次单向挤压。夹到 [-1, 1] 只是防发散。
        /// </summary>
        private void StepJelly(float deltaTime)
        {
            if (!Rig.jellyEnabled)
            {
                // 关掉就复位，免得再打开时从上次的残留冲一下。
                HoFaceJelly.Reset(ref jellyX, 0f);
                HoFaceJelly.Reset(ref jellyY, 0f);
                return;
            }

            // 输入取两眼睑里更闭的那只：眨眼是一起闭的，取 max 比取平均更跟手。
            int left = HoFaceTrackingChannels.IndexOf("eyeBlinkLeft");
            int right = HoFaceTrackingChannels.IndexOf("eyeBlinkRight");
            float target = Mathf.Max(
                left >= 0 ? Smoothed[left] : 0f,
                right >= 0 ? Smoothed[right] : 0f);

            HoFaceJelly.Step(ref jellyX, target, deltaTime, Rig.jellyFrequencyX, Rig.jellyDamping);
            HoFaceJelly.Step(ref jellyY, target, deltaTime, Rig.jellyFrequencyY, Rig.jellyDamping);

            // 弹簧会过冲到 1 以上、回弹到 0 以下。夹到 [-1, 1] 只是为了给混合树一个稳定的参数空间，
            // 不影响形状（过冲体现在轨迹上，不是数值溢出）。见 StepJelly 的注释。
            WriteJelly(Rig.jellyParameterX, jellyX.value);
            WriteJelly(Rig.jellyParameterY, jellyY.value);
        }

        private void WriteJelly(string parameter, float value)
        {
            if (!string.IsNullOrEmpty(parameter) && parameters.Contains(parameter))
                shadow.SetFloat(parameter, Mathf.Clamp(value, -1f, 1f));
        }

        private string MappingStamp()
        {
            var text = new System.Text.StringBuilder();
            foreach (var channel in Rig.channels) if (channel != null) text.Append(channel.shape).Append(':').Append(channel.parameter).Append(';');
            foreach (var map in Rig.pathRemaps) if (map != null) text.Append(map.sourcePath).Append(':').Append(map.target != null ? map.target.GetInstanceID() : 0).Append(';');
            return text.ToString();
        }

        private void Rebuild()
        {
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
                sourceController = Rig.faceController;
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
            if (shadowRoot != null) UnityEngine.Object.DestroyImmediate(shadowRoot);
            shadowRoot = null;
            shadow = null;
            Compiled?.Dispose();
            Compiled = null;
        }
    }
}
