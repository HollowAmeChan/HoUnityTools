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
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != nextSelected[i]) changed = true;
                selected[i] = nextSelected[i];
            }
            CheckGazeOverlap();
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

        /// <summary>
        /// 面捕凝视与 HoLookAt 眼球都开时**提示**，但不阻塞。
        ///
        /// 这两边各有一个开关，怎么分工是用户的事：LookAt 不是一定存在的，默认就把面捕凝视关掉
        /// 反而是错的（不装 LookAt 的人会奇怪为什么眼珠不转）。所以这里只把"两边都在写眼睛方向"
        /// 这件事说出来 —— 硬报错会让默认配置 + 装了 LookAt 的人直接起不来。
        /// </summary>
        private void CheckGazeOverlap()
        {
            Warning = "";
            bool gaze = false;
            for (int i = 0; i < selected.Length; i++)
                if (selected[i] && HoFaceTrackingChannels.Region(HoFaceTrackingChannels.Names[i]) == HoFaceRegion.Gaze) { gaze = true; break; }
            if (!gaze) return;
            foreach (var lookAt in animator.GetComponentsInChildren<HoLookAtConstraint>(true))
                if (lookAt.EyeOutputEnabled)
                {
                    Warning = "面捕的凝视键和 HoLookAt 的眼球同时在写眼睛方向，看起来会转两次。"
                        + "两个开关留一个就好：要么关掉这边「凝视形态键交给面捕」，要么把 LookAt 的眼睛外部权重设为 0。";
                    return;
                }
        }

        /// <summary>不阻塞的提示（目前只有凝视重叠一种）。没问题时是空串。</summary>
        public string Warning { get; private set; } = "";

        private static float Finite01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);

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
