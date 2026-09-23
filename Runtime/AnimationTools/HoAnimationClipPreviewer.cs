using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Hollow.HoUnityTools.Animations
{
    /// <summary>
    /// 单条 AnimationClip 的直通预览器 —— 把 clip 填进来就能播，不需要任何 AnimatorController。
    ///
    /// <para>**为什么需要它**：Unity 强制 Humanoid clip 只能经 Animator 求值（clip 存的是肌肉空间，
    /// 只有 Avatar 能把它重定向到骨骼），而 Animator 又要求先有一个控制器。本组件在运行时搭一张
    /// <see cref="PlayableGraph"/>（<see cref="AnimationClipPlayable"/> → <see cref="Animator"/> 输出），
    /// 于是"填 clip → 播"这一段不再需要往工程里落一个 .controller 资产。</para>
    ///
    /// <para>**人形重定向**：Humanoid 的重定向发生在 Animator 输出端，所以只要 Animator 上有合法人形
    /// Avatar 就能驱动骨架 —— 这和 Animator 自己播这条 clip 走的是同一条求值路径。</para>
    ///
    /// <para>**时间由本组件说了算**：图的更新模式是 <see cref="DirectorUpdateMode.Manual"/>，
    /// 播放头、暂停、倍速全落在这里，不依赖 Animator 的状态机推进。
    /// 因此**暂停时骨架一定停住**，不会出现"暂停了还在飘"。</para>
    ///
    /// <para>**不破坏场景**：预览期间只临时改 Animator 上的 Avatar / 控制器引用与剔除模式，
    /// 组件禁用或销毁时原样还原；不写资产、不改 clip。控制器引用在预览期间会被置空 ——
    /// 这是必要的（否则状态机会继续和预览抢时间），但也意味着**不能边跑游戏逻辑边预览**。</para>
    ///
    /// <para>**根运动**：默认开启，位移会真的作用到骨架上，能看清根位移量；想原地看循环动作就关掉。
    /// 注意这条路径不带控制器，`Animator.applyRootMotion` 的语义与状态机不同 —— 图是直接写骨骼的。</para>
    ///
    /// <para>**换 clip 不留残留**：图只写"这条 clip 里有的通道"，所以换 clip 时上一条写过、
    /// 这一条没写的通道会停在旧值上（形态键不回零、没被驱动的骨骼停在旧姿势）。
    /// 组件在进入预览时快照整副骨架的本地 TRS 与全部形态键，每次重建和退出预览时还原。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Ho Animation Clip Previewer")]
    public sealed class HoAnimationClipPreviewer : MonoBehaviour
    {
        [Tooltip("要播放的动画剪辑。Humanoid / Generic / Legacy 都可以；Humanoid 需要目标 Animator 上有合法人形 Avatar。")]
        [SerializeField]
        private AnimationClip clip = null;

        [Tooltip("驱动哪个 Animator。留空则从本物体向父级查找。Humanoid clip 必须由带人形 Avatar 的 Animator 求值。")]
        [SerializeField]
        private Animator targetAnimator = null;

        [Tooltip("播放模式下启用时是否立即开始播放。编辑器里不自动接管 Animator —— 拖进剪辑后直接按播放键即可。")]
        [SerializeField]
        private bool playOnEnable = true;

        [Tooltip("播放倍速。1 = 原速。0 会停在原地（等同暂停且保留位姿）。")]
        [Range(0f, 8f)]
        [SerializeField]
        private float playbackSpeed = 1f;

        [Tooltip("循环播放。关闭时播到片尾停住。")]
        [SerializeField]
        private bool loop = true;

        [Tooltip("是否让 clip 的根运动作用到骨骼上。\n" +
                 "开启（默认）：位移真的发生，能看清根位移量。想原地看循环动作就关掉。")]
        [SerializeField]
        private bool applyRootMotion = true;

        // ---- 运行状态 ----------------------------------------------------------

        private PlayableGraph graph;
        private AnimationClipPlayable clipPlayable;
        private bool graphValid;

        /// <summary>预览期间的播放头（秒）。这是权威时间，不依赖图内部状态。</summary>
        private double playhead;

        private bool playing;
        private bool previewing;

        /// <summary>本帧是否还有新时间没推给图。</summary>
        private bool evaluationPending;

        /// <summary>上一条错误信息。同时用来保证同一个错误只写一次日志。</summary>
        private string lastError;

        /// <summary>待执行的重建（见 <see cref="RequestRebuild"/>）。</summary>
        private bool pendingRebuild;
        private bool pendingRebuildResetPlayhead;

        /// <summary>
        /// 一个 Animator 同时只能被一个预览器接管。
        /// 两个组件挂在同一棵骨架上时，后建的那张图会把先建的那张的姿势覆盖掉，
        /// 表现是"两个面板互相打架"，很难查 —— 所以这里直接登记归属并拒绝第二个。
        /// </summary>
        private static readonly Dictionary<Animator, HoAnimationClipPreviewer> AnimatorOwners =
            new Dictionary<Animator, HoAnimationClipPreviewer>();

        /// <summary>本组件当前是否持有这个 Animator 的预览权。</summary>
        private bool OwnsAnimator(Animator animator)
        {
            if (animator == null)
                return false;

            HoAnimationClipPreviewer owner;
            return AnimatorOwners.TryGetValue(animator, out owner) && owner == this;
        }

        // 还原用：只记录我们真正改动过的字段
        private RuntimeAnimatorController originalController;
        private Avatar originalAvatar;
        private AnimatorCullingMode originalCullingMode;
        private bool originalApplyRootMotion;
        private bool controllerOverridden;
        private bool avatarOverridden;
        private bool cullingOverridden;
        private bool rootMotionOverridden;

        // 建图时用的入参快照，用来判断是否需要重建
        private AnimationClip activeClip;
        private Animator activeAnimator;

        // ---- 查询 --------------------------------------------------------------

        /// <summary>当前驱动的 Animator。未指定时向父级查找。</summary>
        public Animator TargetAnimator
        {
            get
            {
                if (targetAnimator == null)
                    targetAnimator = GetComponentInParent<Animator>();
                return targetAnimator;
            }
        }

        /// <summary>当前剪辑。</summary>
        public AnimationClip Clip
        {
            get { return clip; }
        }

        /// <summary>
        /// 销毁图并清空句柄。**必须问图自己**，不能信手写的 <c>graphValid</c> 标记 ——
        /// 那是个缓存，会过期：对已经死掉的图调 <c>Destroy()</c> 会抛
        /// <c>NullReferenceException: The PlayableGraph is null</c>。
        ///
        /// <para><c>graphValid</c> 只用来决定"要不要 Evaluate"，销毁一律以 <c>graph.IsValid()</c> 为准。
        /// 重复调用是安全的。</para>
        /// </summary>
        private void CleanupGraph()
        {
            graphValid = false;

            if (graph.IsValid())
            {
                try
                {
                    graph.Destroy();
                }
                catch (System.Exception e)
                {
                    // 图已被外部销毁（domain reload、场景切换、别的组件动过）：
                    // 状态照旧收干净，但不要把异常抛给调用方 —— 那会打断 Inspector 的重绘。
                    Debug.LogWarning("Ho Animation Clip Previewer：销毁播放图失败，已忽略：" + e.Message, this);
                }
            }

            graph = default(PlayableGraph);
            clipPlayable = default(AnimationClipPlayable);
        }

        /// <summary>是否已进入预览（图已建立并接管 Animator）。</summary>
        public bool IsPreviewing
        {
            get { return previewing; }
        }

        /// <summary>
        /// 组件现在是否在驱动时间。**禁用状态下恒为 false** ——
        /// 面板上那些控件只有为 true 时才该可点。
        ///
        /// <para>注意判据是 <c>previewing</c> 而不是 <c>enabled</c>：两者会短暂不同步
        /// —— <c>OnDisable</c> 之前镜头可能还开着，那种情况下走带应当只读，
        /// 免得用户点了没反应却不知道为什么。</para>
        /// </summary>
        public bool ShouldDrive
        {
            get { return enabled && previewing; }
        }

        /// <summary>
        /// 播放单元是否可用 —— "可以安全地推时间"的判据。
        /// <para>必须问 <c>clipPlayable.IsValid()</c>，不能用 <c>graph.IsValid()</c>：
        /// 图存在而 playable 句柄已失效时，后者仍返回 true，于是 <c>SetTime</c> 会抛
        /// <c>ArgumentNullException: The Playable is null</c>。</para>
        /// </summary>
        public bool IsPlayableReady
        {
            get { return previewing && clipPlayable.IsValid(); }
        }

        /// <summary>上一条错误信息（没有则为 null）。面板用它把失败原因摆到脸上，而不是只丢进 Console。</summary>
        public string LastError
        {
            get { return lastError; }
        }

        /// <summary>
        /// 当前是不是在"预制体资产"里（而不是场景实例）。
        /// <para>预制体资产上不能建运行时 playable，预览会失败；这种情况下不如提前说清楚。
        /// 运行时构建里恒为 false。</para>
        /// </summary>
        public static bool IsPrefabAssetContext(GameObject candidate)
        {
#if UNITY_EDITOR
            if (candidate == null)
                return false;

            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(candidate))
                return true;

            // 预制体编辑模式（Prefab Mode）里打开的也是资产，但它不在主舞台里。
            try
            {
                return UnityEditor.SceneManagement.StageUtility.GetStageHandle(candidate)
                    != UnityEditor.SceneManagement.StageUtility.GetMainStageHandle();
            }
            catch (System.Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>是否正在推进时间（暂停时为 false）。</summary>
        public bool IsPlaying
        {
            get { return playing; }
        }

        /// <summary>播放倍速。可运行时改。</summary>
        public float PlaybackSpeed
        {
            get { return playbackSpeed; }
            set { playbackSpeed = Mathf.Max(0f, value); }
        }

        /// <summary>是否循环。</summary>
        public bool Loop
        {
            get { return loop; }
            set { loop = value; }
        }

        /// <summary>根运动是否作用到物体上。</summary>
        public bool ApplyRootMotion
        {
            get { return applyRootMotion; }
            set { applyRootMotion = value; }
        }

        /// <summary>剪辑时长（秒）。没有 clip 时为 0。</summary>
        public float Duration
        {
            get { return clip != null ? Mathf.Max(0f, clip.length) : 0f; }
        }

        /// <summary>剪辑采样率。没有 clip 时为 0。</summary>
        public float FrameRate
        {
            get { return clip != null ? Mathf.Max(0f, clip.frameRate) : 0f; }
        }

        /// <summary>总帧数（按时长 × 采样率换算，至少 1）。</summary>
        public int FrameCount
        {
            get
            {
                float rate = FrameRate;
                if (rate <= 0f || clip == null)
                    return 1;
                return Mathf.Max(1, Mathf.RoundToInt(clip.length * rate) + 1);
            }
        }

        /// <summary>当前播放头（秒）。</summary>
        public float CurrentTime
        {
            get { return (float)playhead; }
        }

        /// <summary>当前归一化时间 0~1。</summary>
        public float NormalizedTime
        {
            get
            {
                float duration = Duration;
                return duration <= 0f ? 0f : Mathf.Clamp01((float)(playhead / duration));
            }
        }

        /// <summary>当前帧号（0 基）。</summary>
        public int CurrentFrame
        {
            get
            {
                float rate = FrameRate;
                if (rate <= 0f)
                    return 0;
                return Mathf.Clamp(Mathf.RoundToInt((float)playhead * rate), 0, FrameCount - 1);
            }
        }

        /// <summary>本组件能否真的播：有 clip 且找得到 Animator。</summary>
        public bool CanPreview
        {
            get { return clip != null && TargetAnimator != null; }
        }

        /// <summary>是 Humanoid clip 但目标 Animator 上没有合法人形 Avatar —— 这种情况下无法重定向。</summary>
        public bool MissingHumanoidAvatar
        {
            get
            {
                if (clip == null || !clip.humanMotion)
                    return false;
                Animator animator = TargetAnimator;
                return animator == null || animator.avatar == null || !animator.avatar.isHuman;
            }
        }

        /// <summary>是 Humanoid clip（肌肉空间，必须经 Avatar 重定向）。</summary>
        public bool IsHumanoidClip
        {
            get { return clip != null && clip.humanMotion; }
        }

        // ---- 生命周期 ----------------------------------------------------------

        /// <summary>
        /// 组件被添加（或 Inspector 上点 Reset）时自动找 Animator。
        ///
        /// <para><see cref="TargetAnimator"/> 本来就会在运行时向父级回退查找，所以**不加这段也能用**；
        /// 但序列化字段会一直空着，面板上显示 "None"，看起来像没配好。
        /// 这里把找到的那个**写进字段**，让状态一眼可见，也让用户能手改。
        /// 没找到就留空 —— 那是"父级还没有 Animator"的正常情况，不报错。</para>
        /// </summary>
        private void Reset()
        {
            if (targetAnimator == null)
                targetAnimator = GetComponentInParent<Animator>();
        }

        private void OnEnable()
        {
            // 编辑器里不主动接管 Animator：那会在没播放的情况下把场景里 Animator 的控制器引用
            // 换掉（Unity 会因此把场景标记为已修改）。编辑器里的预览由要预览的人显式发起
            // —— 面板上的「预览」按钮，或运行时脚本调 Play()/SetTime()。
            if (!Application.isPlaying)
                return;

            if (!CanPreview)
                return;

            BeginPreview();
            playing = playOnEnable;
            evaluationPending = true;
            EvaluateNow();
        }

        private void OnDisable()
        {
            // 禁用 = 彻底放手：拆图、还原 Animator 引用、还原骨架。
            //
            // 这里必须**无条件**走完还原流程（包括"曾经预览过但中途失败"的半途状态），
            // 否则被改过的 Avatar / 控制器引用会留在 Animator 上。
            EndPreview(force: true);

            // 被推迟的重建也一并作废 —— 组件都禁用了，没有理由再去动场景。
            pendingRebuild = false;
            pendingRebuildResetPlayhead = false;
        }

        private void Update()
        {
            // 运行时被推迟的重建，在这里落一次。
            if (pendingRebuild)
                FlushPendingRebuild();

            if (!Application.isPlaying)
                return;

            MaintainPreview();

            if (!IsPlayableReady || !playing)
                return;

            AdvanceBy(UnityEngine.Time.deltaTime);
        }

        // ---- 对外控制 ----------------------------------------------------------

        /// <summary>
        /// 换一条剪辑。预览中会就地重建图。
        /// <paramref name="resetPlayhead"/> 为 true 时回到片头（换片子默认从头看）。
        /// </summary>
        public void SetClip(AnimationClip newClip, bool resetPlayhead = true)
        {
            if (clip == newClip)
                return;

            clip = newClip;
            RebuildPreview(resetPlayhead);
        }

        /// <summary>指向另一个 Animator。预览中会就地重建图。</summary>
        public void SetTargetAnimator(Animator animator)
        {
            if (targetAnimator == animator)
                return;

            targetAnimator = animator;
            RebuildPreview(false);
        }

        /// <summary>
        /// 请求就地重建（换 clip / 换 Animator 后用）。
        ///
        /// <para>编辑器里如果是被 <c>OnValidate</c> 触发的，不能立刻重建 —— 重建要
        /// <c>SetActive</c> / 销毁图，而 OnValidate 期间 Unity 禁止 SendMessage：
        /// 场景里但凡有 <c>OnBecameVisible</c>/<c>OnBecameInvisible</c> 这类回调
        /// （蒙皮网格很常见），就会刷屏 "SendMessage cannot be called during Awake,
        /// CheckConsistency, or OnValidate"。所以推迟到下一次编辑器 update。</para>
        /// </summary>
        public void RequestRebuild(bool resetPlayhead = false)
        {
            pendingRebuild = true;
            pendingRebuildResetPlayhead |= resetPlayhead;

#if UNITY_EDITOR
            // 不在 OnValidate 里就地做；注意 Application.isPlaying 在 OnValidate 期间
            // 未必已经为 true，所以这个判断要放在运行时执行的那一次。
            UnityEditor.EditorApplication.delayCall += FlushPendingRebuild;
#endif
        }

        /// <summary>
        /// 退出预览并说明原因，用于"预览链路被外力破坏"这一类情况。
        /// 只报一次，不会每帧刷屏。
        /// </summary>
        private void AbortPreview(string reason)
        {
            ReportError(reason);
            EndPreview();
        }

        /// <summary>
        /// 每帧体检：图还在，但驱动链已经断了的情况要主动收摊，而不是静默失效。
        /// <para>看的是**整个祖先链**的启用状态 —— 有些 clip 会开关物体，
        /// 一旦把 Animator 自己所在的物体关掉，图就再也驱动不了任何东西，
        /// 而组件本身还在"启用"（因为被关的可能是它父级）。</para>
        /// </summary>
        private void MaintainPreview()
        {
            if (!previewing)
                return;

            Animator animator = activeAnimator;
            if (animator == null || !animator.isActiveAndEnabled)
            {
                AbortPreview("目标 Animator 已被禁用或销毁，预览已结束。"
                    + "（若这是某条 clip 的 m_IsActive 曲线关掉的，它同样属于残留的一种极端情况。）");
                return;
            }

            if (!OwnsAnimator(animator) && !AnimatorOwners.ContainsKey(animator))
            {
                // 归属被清掉了（通常意味着另一处 EndPreview 走了），重建归属即可，不必中断。
                AnimatorOwners[animator] = this;
            }
        }

        private void FlushPendingRebuild()
        {
            if (!pendingRebuild)
                return;

            bool reset = pendingRebuildResetPlayhead;
            pendingRebuild = false;
            pendingRebuildResetPlayhead = false;

            // 组件禁用期间一律不做 —— 禁用意味着"别动场景"，积压的重建直接作废。
            if (!enabled)
                return;

            // 运行时的 Update 会在自己那一帧处理，这里不重复。
            if (Application.isPlaying)
                return;

            RebuildPreview(reset);
        }

        /// <summary>编辑器（Inspector 的 update 回调）用：把推迟的重建立即落地。</summary>
        public void FlushPendingRebuilds()
        {
            if (!enabled)
                return;

            FlushPendingRebuild();
        }

        /// <summary>编辑器（Inspector 的 update 回调）用：每帧体检，及时收掉断掉的预览。</summary>
        public void MaintainPreviewState()
        {
            if (!enabled)
                return;

            MaintainPreview();
        }

        /// <summary>
        /// 就地重建图：先原样拆掉（还原 Animator 与骨架姿势），再按当前 clip / Animator 重新接管。
        /// 未处于预览状态时是空操作。播放状态会被保留。
        ///
        /// <para>组件禁用时是空操作 —— 禁用状态下不该再去接管 Animator。</para>
        /// </summary>
        public void RebuildPreview(bool resetPlayhead = false)
        {
            if (!enabled)
                return;

            if (!previewing && !CanPreview)
                return;

            bool wasPlaying = playing;
            EndPreview();
            if (!CanPreview)
                return;

            if (resetPlayhead)
                playhead = 0.0;

            BeginPreview();
            playing = wasPlaying;
            evaluationPending = true;
            EvaluateNow();
        }

        /// <summary>
        /// 从当前播放头开始播放。
        /// <para>**组件禁用时是空操作** —— 禁用状态下这些接口不得去接管 Animator，
        /// 否则"动一下滑条就把组件启用了"（其实是绕过 enabled 把预览又拉起来了）。</para>
        /// </summary>
        public void Play()
        {
            if (!enabled || !CanPreview)
                return;

            if (!IsPlayableReady)
            {
                BeginPreview();
                if (!IsPlayableReady)
                    return;
            }

            // 已经停在片尾时按"从头再播"处理，否则按播放键没有任何反应。
            if (!loop && playhead >= Duration)
                playhead = 0.0;

            playing = true;
            evaluationPending = true;
            EvaluateNow();
        }

        /// <summary>暂停，骨架停在当前帧。</summary>
        public void Pause()
        {
            playing = false;
        }

        /// <summary>播放 / 暂停切换。</summary>
        public void TogglePlay()
        {
            if (playing)
                Pause();
            else
                Play();
        }

        /// <summary>回到起点，保持当前播放 / 暂停状态。</summary>
        public void Restart()
        {
            SetTime(0f, playing);
        }

        /// <summary>
        /// 直接落到某个时间（秒）。<paramref name="keepPlaying"/> 为 true 时从该点继续播。
        /// 拖动滑条走的就是这条路 —— 暂停状态下也能逐帧看。
        ///
        /// <para>**组件禁用时是空操作**（见 <see cref="Play"/>）。</para>
        /// </summary>
        public void SetTime(float seconds, bool keepPlaying)
        {
            if (!enabled || !CanPreview)
                return;

            // 只有"播放单元真的可用"才动时间 —— 否则每次拖动滑条都会撞一次空 playable。
            if (!IsPlayableReady)
            {
                BeginPreview();
                if (!IsPlayableReady)
                    return;
            }

            playhead = Mathf.Clamp(seconds, 0f, Duration);
            playing = keepPlaying;
            evaluationPending = true;
            EvaluateNow();
        }

        /// <summary>直接落到某个归一化时间 0~1。</summary>
        public void SetNormalizedTime(float normalized, bool keepPlaying)
        {
            SetTime(Mathf.Clamp01(normalized) * Duration, keepPlaying);
        }

        /// <summary>直接落到某一帧（0 基）。</summary>
        public void SetFrame(int frame, bool keepPlaying)
        {
            float rate = FrameRate;
            if (rate <= 0f)
            {
                SetTime(0f, keepPlaying);
                return;
            }

            SetTime(Mathf.Clamp(frame, 0, FrameCount - 1) / rate, keepPlaying);
        }

        /// <summary>相对当前帧步进。<paramref name="delta"/> 可为负。会顺带暂停。</summary>
        public void StepFrames(int delta)
        {
            SetFrame(CurrentFrame + delta, false);
        }

        /// <summary>步进一帧。<paramref name="forward"/> 为 false 时后退。</summary>
        public void StepOneFrame(bool forward)
        {
            StepFrames(forward ? 1 : -1);
        }

        /// <summary>
        /// 按真实经过时间推进（不受 MonoBehaviour.Update 影响），随后立即求值。
        /// 编辑器里没有游戏循环，由 Inspector 的 update 回调按这个接口喂时间。
        /// </summary>
        public void AdvanceBy(float unscaledDeltaSeconds)
        {
            if (!enabled || !IsPlayableReady || !playing)
                return;

            playhead += unscaledDeltaSeconds * playbackSpeed;
            ClampPlayhead();
            evaluationPending = true;
            EvaluateNow();
        }

        /// <summary>
        /// 把当前播放头立即推给图求值。没有新时间时是空操作。
        ///
        /// <para>这里必须查 <c>clipPlayable.IsValid()</c> 而不能只信 <c>graphValid</c>：
        /// <c>PlayableGraph.IsValid()</c> 只看图自己的版本号，图存在而 playable 的句柄已经失效
        /// （例如重建时被销毁、或当前平台/版本下压根没建起来）时它仍返回 true，
        /// 于是 <c>SetTime</c> 会抛 <c>ArgumentNullException: The Playable is null</c>。</para>
        /// </summary>
        public void EvaluateNow()
        {
            if (!previewing || !evaluationPending)
                return;

            if (!clipPlayable.IsValid())
            {
                // 句柄没了：把状态收干净（含还原 Animator 引用），
                // 别在每帧的 update 回调里反复抛异常。
                ReportError("播放单元句柄失效（本次预览已结束，Animator 引用已还原）。");
                evaluationPending = false;
                EndPreview();
                return;
            }

            clipPlayable.SetTime(playhead);
            // 同理，求值也问图自己 —— graphValid 只是个缓存。
            if (graph.IsValid())
                graph.Evaluate(0f);
            evaluationPending = false;
        }

        /// <summary>记下失败原因：面板会展示它，同一个错误只往 Console 写一次。</summary>
        private void ReportError(string message)
        {
            if (lastError == message)
                return;

            lastError = message;
            Debug.LogWarning("Ho Animation Clip Previewer：" + message, this);
        }

        private void ClearError()
        {
            lastError = null;
        }

        // ---- 内部 --------------------------------------------------------------

        /// <summary>把播放头折回 [0, duration]；不循环时夹在片尾并停住。</summary>
        private void ClampPlayhead()
        {
            double duration = Duration;
            if (duration <= 0.0)
            {
                playhead = 0.0;
                return;
            }

            if (loop)
            {
                if (playhead >= duration || playhead < 0.0)
                    playhead -= duration * System.Math.Floor(playhead / duration);
                if (playhead >= duration)
                    playhead = 0.0;
            }
            else if (playhead >= duration)
            {
                playhead = duration;
                playing = false;
            }
            else if (playhead < 0.0)
            {
                playhead = 0.0;
                playing = false;
            }
        }

        /// <summary>建图并接管 Animator。重复调用是安全的。</summary>
        private void BeginPreview()
        {
            if (previewing)
                return;

            Animator animator = TargetAnimator;
            if (animator == null || clip == null)
            {
                // 走到这里说明是"重建途中条件不成立"（例如 clip 被清空）。
                // 必须把上一次接管的引用还回去，否则 Animator 会一直停在被置空的状态。
                EndPreview();
                return;
            }

            // 预制体资产上建不了运行时 playable —— 提前说清楚，别让它变成一个空句柄。
            if (IsPrefabAssetContext(gameObject))
            {
                ReportError("当前在预制体资产（Prefab 资产或 Prefab Mode）里，这里不能建运行时播放图。"
                    + "把组件放到场景里的实例上再预览。");
                return;
            }

            // 一个 Animator 只允许一个预览器接管。
            HoAnimationClipPreviewer other;
            if (AnimatorOwners.TryGetValue(animator, out other) && other != null && other != this)
            {
                ReportError("这个 Animator 已经被另一个 Ho Animation Clip Previewer 接管了："
                    + other.gameObject.name + "。同一个 Animator 同时只能有一个预览器。");
                return;
            }

            // 进图之前快照一次未预览的姿势 —— 换 clip 时靠它把上一条 clip 的残留清掉。
            // 只在第一次接管时快照：重建走的是 EndPreview → BeginPreview，
            // EndPreview 会先把快照还原并置空，所以这里拿到的一定是干净的基准。
            if (snapshot == null)
            {
                snapshot = new PoseSnapshot();
                snapshot.Capture(gameObject);
            }

            if (!animator.isActiveAndEnabled)
            {
                Debug.LogWarning(
                    "Ho Animation Clip Previewer：目标 Animator 未启用，PlayableGraph 无法驱动它：" + animator.name,
                    animator);
            }

            // 预览要独占时间轴：控制器必须先让位。留着旧控制器引用的话，
            // Animator 仍会按状态机推进，和我们的播放头抢骨架。原值记下来，退出时还原。
            originalController = animator.runtimeAnimatorController;
            if (originalController != null)
            {
                animator.runtimeAnimatorController = null;
                controllerOverridden = true;
            }

            // Humanoid 需要一个合法人形 Avatar；目标上没有时，借 clip 资产里的那个
            // （重定向规则与 Animator 一致 —— 不同源的 Avatar 之间靠骨骼映射表转换）。
            originalAvatar = animator.avatar;
            Avatar resolvedAvatar = ResolveAvatar(animator);
            if (resolvedAvatar != null && !ReferenceEquals(resolvedAvatar, originalAvatar))
            {
                animator.avatar = resolvedAvatar;
                avatarOverridden = true;
            }

            // 预览台不该因为看不见就停止求值。
            originalCullingMode = animator.cullingMode;
            if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                cullingOverridden = true;
            }

            originalApplyRootMotion = animator.applyRootMotion;
            if (animator.applyRootMotion != applyRootMotion)
            {
                animator.applyRootMotion = applyRootMotion;
                rootMotionOverridden = true;
            }

            graph = PlayableGraph.Create("HoAnimationClipPreviewer");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            if (!clipPlayable.IsValid())
            {
                // 建不起来就彻底退回去（还原 Animator 引用），不要留一个"自称在预览"的空壳，
                // 否则每次拖动滑条都会在 SetTime 上抛异常。
                ReportError("无法为该剪辑创建播放单元，已放弃预览：" + clip.name
                    + "（时长 " + Duration.ToString("F3") + "s）");
                EndPreview();
                return;
            }

            clipPlayable.SetDuration(Mathf.Max(0f, Duration));
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "HoClipPreview", animator);
            output.SetSourcePlayable(clipPlayable);

            // 先 Play() 再接源，是为了绕开"编辑器里第一帧不生效"的老问题；
            // 顺序反了在编辑模式下常常要等一次额外求值才动。
            graph.Play();

            graphValid = graph.IsValid();
            previewing = true;
            activeClip = clip;
            activeAnimator = animator;
            AnimatorOwners[animator] = this;
            ClearError();

            clipPlayable.SetTime(playhead);
            evaluationPending = false;
            if (graph.IsValid())
                graph.Evaluate(0f);
        }

        /// <summary>拆图并还原 Animator 的原始引用。</summary>
        private void EndPreview()
        {
            EndPreview(force: false);
        }

        /// <summary>
        /// 拆图并还原 Animator 的原始引用。
        ///
        /// <para><paramref name="force"/> = true 时**即使从没成功进入过预览**也走完还原流程。
        /// 这是给 <c>OnDisable</c> 用的：重建中途失败会留下"引用已经改了、previewing 却是 false"
        /// 的半途状态，那种状态下如果按常规提前 return，被改过的 Avatar / 控制器引用就永远留着了。</para>
        /// </summary>
        private void EndPreview(bool force)
        {
            // 纪律：**图的清理不看 previewing**。previewing 只是个标记，一旦它和实际状态不一致
            // （重建中途失败最典型），提前 return 就会把图漏在身后 —— 下一次 EndPreview 拿到
            // 的是一张已死的图，Destroy() 直接抛 NullReferenceException。
            // CleanupGraph 内部按 graph.IsValid() 判断，重复调用无副作用。
            CleanupGraph();

            bool needsRestore = previewing
                || avatarOverridden
                || controllerOverridden
                || cullingOverridden
                || rootMotionOverridden;

            previewing = false;
            playing = false;
            evaluationPending = false;
            activeClip = null;
            activeAnimator = null;

            // 没进入过预览（也没改过任何引用）就别动骨架 —— 否则会把别人的状态"还原"掉。
            if (!needsRestore && !force)
                return;

            // 把骨架与形态键还原成进入预览前的样子。
            // 重建路径也走这里 —— 这正是"换 clip 不清残留"的修复点。
            if (snapshot != null)
            {
                snapshot.Restore();
                snapshot = null;
            }

            Animator animator = targetAnimator;
            if (animator == null)
                return;

            // 交还这个 Animator 的预览权（只在自己持有的时候交还）。
            HoAnimationClipPreviewer owner;
            if (AnimatorOwners.TryGetValue(animator, out owner) && owner == this)
                AnimatorOwners.Remove(animator);

            if (avatarOverridden)
            {
                animator.avatar = originalAvatar;
                avatarOverridden = false;
            }

            if (controllerOverridden)
            {
                animator.runtimeAnimatorController = originalController;
                controllerOverridden = false;
            }

            if (cullingOverridden)
            {
                animator.cullingMode = originalCullingMode;
                cullingOverridden = false;
            }

            if (rootMotionOverridden)
            {
                animator.applyRootMotion = originalApplyRootMotion;
                rootMotionOverridden = false;
            }
        }

        /// <summary>
        /// 找到能驱动这条 clip 的 Avatar：目标自己的够用就用它；Humanoid clip 而目标没有
        /// 合法人形 Avatar 时，去 clip 所在的资产里借一个。
        /// </summary>
        private Avatar ResolveAvatar(Animator animator)
        {
            Avatar own = animator.avatar;
            if (!clip.humanMotion)
                return own;
            if (own != null && own.isHuman)
                return own;
            return FindSourceHumanoidAvatar();
        }

        private Avatar FindSourceHumanoidAvatar()
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
                return null;

            Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                Avatar candidate = assets[i] as Avatar;
                if (candidate != null && candidate.isHuman)
                    return candidate;
            }
#endif
            return null;
        }

        // ---- 姿势残留 ----------------------------------------------------------

        /// <summary>
        /// 预览前的姿势快照。
        ///
        /// <para>**为什么必须有它**：PlayableGraph 只写"这条 clip 里有的通道"。换 clip 时，
        /// 上一条 clip 写过、而这一条没有的通道会**留在上一次的值上** —— 表现就是形态键不回零、
        /// 某根没被新 clip 驱动的骨骼停在旧姿势。想要干净，只能在换 clip 前把底层状态还原成
        /// 预览前的样子，让新 clip 从同一个基准开始。</para>
        ///
        /// <para>所以：进入预览时快照一次，每次重建（换 clip / 换 Animator）前还原一次，
        /// 退出预览时再还原一次。</para>
        /// </summary>
        private sealed class PoseSnapshot
        {
            public Transform[] transforms;
            public Vector3[] localPositions;
            public Quaternion[] localRotations;
            public Vector3[] localScales;

            public SkinnedMeshRenderer[] renderers;
            public float[][] blendShapeWeights;

            /// <summary>每个 Transform 所在物体的激活状态。索引与 <see cref="transforms"/> 对齐。</summary>
            public GameObject[] objects;
            public bool[] activeStates;

            public void Capture(GameObject root)
            {
                transforms = root.GetComponentsInChildren<Transform>(true);
                localPositions = new Vector3[transforms.Length];
                localRotations = new Quaternion[transforms.Length];
                localScales = new Vector3[transforms.Length];
                objects = new GameObject[transforms.Length];
                activeStates = new bool[transforms.Length];
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform t = transforms[i];
                    localPositions[i] = t.localPosition;
                    localRotations[i] = t.localRotation;
                    localScales[i] = t.localScale;

                    GameObject owner = t.gameObject;
                    objects[i] = owner;
                    activeStates[i] = ReferenceEquals(owner, root) || owner.activeSelf;
                }

                renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                blendShapeWeights = new float[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                {
                    SkinnedMeshRenderer renderer = renderers[i];
                    if (renderer == null || renderer.sharedMesh == null)
                        continue;

                    // 未预览的基准状态就是全 0（形态键默认值），逐键读一遍只是为了让
                    // "预览前本来就摆过形态键"的场景也能原样还回去。
                    int count = renderer.sharedMesh.blendShapeCount;
                    if (count <= 0)
                        continue;

                    var weights = new float[count];
                    for (int shape = 0; shape < count; shape++)
                        weights[shape] = renderer.GetBlendShapeWeight(shape);

                    blendShapeWeights[i] = weights;
                }
            }

            public void Restore()
            {
                if (transforms != null)
                {
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        Transform t = transforms[i];
                        if (t == null)
                            continue;
                        t.localPosition = localPositions[i];
                        t.localRotation = localRotations[i];
                        t.localScale = localScales[i];
                    }
                }

                // 激活状态：有些 clip 会开关物体（m_IsActive 曲线），它同样是"上一条 clip 的残留"。
                // 根物体跳过 —— 关掉它等于把本组件连同预览一起弄没。
                if (objects != null)
                {
                    for (int i = 0; i < objects.Length; i++)
                    {
                        GameObject owner = objects[i];
                        if (owner == null)
                            continue;
                        if (owner.activeSelf == activeStates[i])
                            continue;

                        // 已经销毁/不在场景里的物体不能 SetActive。
                        if (!owner.scene.IsValid())
                            continue;

                        owner.SetActive(activeStates[i]);
                    }
                }

                if (renderers == null)
                    return;

                for (int i = 0; i < renderers.Length; i++)
                {
                    float[] weights = blendShapeWeights[i];
                    SkinnedMeshRenderer renderer = renderers[i];
                    if (weights == null || renderer == null)
                        continue;
                    for (int shape = 0; shape < weights.Length; shape++)
                        renderer.SetBlendShapeWeight(shape, weights[shape]);
                }
            }
        }

        private PoseSnapshot snapshot;

        /// <summary>立刻把骨架恢复成进入预览前的姿势（形态键一并回位），并保持预览状态。</summary>
        public void ResetPose()
        {
            if (!previewing)
                return;

            if (snapshot != null)
                snapshot.Restore();

            evaluationPending = true;
            EvaluateNow();
        }

        private void OnValidate()
        {
            playbackSpeed = Mathf.Max(0f, playbackSpeed);

            // 面板上直接改了 clip / Animator 时（PropertyField 不经过 SetClip），
            // 这里兜底重建 —— 否则骨架会停在旧 clip 的最后一帧上。
            //
            // **必须推迟**：OnValidate 期间 Unity 禁止 SendMessage，而重建会 SetActive、
            // 销毁图，场景里带 OnBecameVisible/OnBecameInvisible 的物体会立刻报
            // "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate"。
            if (previewing && (activeClip != clip || activeAnimator != TargetAnimator || !CanPreview))
                RequestRebuild(false);
        }
    }
}
