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
        public readonly HoFaceDebugSettings Settings;
        public HoFaceCompiledController Compiled { get; private set; }
        public readonly float[] Effective = new float[52];
        /// <summary>通道层整形之后的值（模式 / 输入曲线 / 断流回中性）。**表达式读的就是它。**</summary>
        public readonly float[] Input = new float[52];
        /// <summary>
        /// 按参数名读某一行最后的输出值（面板的「参数输出」栏、用例用它；没有这一行时返回 <see cref="float.NaN"/>）。
        /// ⚠️ 这里**不再有** `ControllerValues`（"名字正好是 `ARKit/&lt;键&gt;` 的那 52 行"的快速索引）：
        /// 那个约定 2026-09-26 已经废掉（出口用**裸规范名**），而且它早已没有任何读者 —— 面板现在直接
        /// 按行名读 <see cref="OutputValue"/>，不依赖命名约定。
        /// </summary>
        public float OutputValue(string parameter) =>
            parameter != null && outputIndex.TryGetValue(parameter, out int row) ? outputValues[row] : float.NaN;

        /// <summary>
        /// 按**规范名**读输入行算出来的值（「排查」栏的姿态监视用）。没有对应输入行时返回 <see cref="float.NaN"/> ——
        /// 那说明这份配置根本没有把这个名字从线名映射过来。
        /// </summary>
        public float MiddlewareInput(string canonical) =>
            canonical != null && inputIndex.TryGetValue(canonical, out int row) ? inputValues[row] : float.NaN;
        private readonly Dictionary<string, int> outputIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>输入行（线名 → 规范名）。它们先把手机原值翻成规范名，通道与输出行都只认规范名。</summary>
        private HoFaceOutput[] inputRows = new HoFaceOutput[0];
        private HoFaceExpression[] inputExpressions = new HoFaceExpression[0];
        private float[] inputValues = new float[0];
        private bool[] inputFresh = new bool[0];
        private float[] inputSmooth = new float[0];
        private int[] inputStepIndex = new int[0];
        private double[] inputStepUntil = new double[0];

        /// <summary>延迟用的 FIFO，每行一条。元素是 <c>(到期时刻, 值)</c>。</summary>
        private Queue<(double At, float Value)>[] inputDelay = new Queue<(double At, float Value)>[0];
        /// <summary>规范名 → 输入行号（同名多行时**最后一行**生效，方便用户覆盖）。</summary>
        private readonly Dictionary<string, int> inputIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> touched = new List<string>();
        private HoFaceOutput[] outputs = new HoFaceOutput[0];
        private HoFaceExpression[] expressions = new HoFaceExpression[0];
        private float[] outputValues = new float[0];
        private float[] outputSmooth = new float[0];
        private int[] stepIndex = new int[0];
        private double[] stepUntil = new double[0];

        /// <summary>延迟用的 FIFO，每行一条。见 <see cref="inputDelay"/>。</summary>
        private Queue<(double At, float Value)>[] outputDelay = new Queue<(double At, float Value)>[0];
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

        /// <summary>
        /// 影子上的**语义 Hub**：控制器里的「语义写手」（`HoFaceSemanticWriterBehaviour`）写它。
        /// ⚠️ 写手是**跑在影子 Animator 上**的（那是唯一在跑控制器的地方），而消费方读的是**角色身上**那片
        /// Hub —— 所以影子根上必须有一片，且每帧由 <see cref="RelaySemantics"/> 搬到角色那边。
        /// </summary>
        private HoFaceSemanticHub shadowHub;

        /// <summary>角色身上的 Connector（转发时按它的槽表解释名字）。换角色 / 换表时重新找。</summary>
        private HoFaceSemanticConnector semanticConnector;
        private GameObject semanticOwner;
        /// <summary>转发时被跳过的名字（不在槽表里的），只留前几个用来点名。</summary>
        private readonly List<string> semanticSkipped = new List<string>();
        /// <summary>上一次报过的转发状态（结构变了才重算字符串 + 报一次，免得每帧刷屏）。</summary>
        private string semanticReported;

        /// <summary>
        /// 语义转发的**结构**摘要（写几个、跳过几个、跳过谁）。面板直接读它 —— 值本身去读 Hub。
        /// 只在结构变化时更新，所以不用担心每帧分配字符串。
        /// </summary>
        public string SemanticStatus { get; private set; }

        /// <summary>
        /// 影子 Hub 上有几个槽（给面板的"链路走到哪一步"用）。
        /// **−1 = 连影子 Hub 都不存在**（会话建影子时没加 Hub ⇒ 多半是包代码没重编译），
        /// **0 = 影子 Hub 在、但写手一格都没声明过**（= 状态机行为没被调用，或条目是空的）。
        /// 这两种在界面上本来长得一样，所以这里分开报。
        /// </summary>
        public int ShadowHubSlotCount { get { return shadowHub != null ? shadowHub.SlotCount : -1; } }

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

        public HoFaceAnimationSession(HoFaceDebugSettings settings)
        {
            // ⚠️ 别把参数也起名叫 Settings：那样这一行会变成 `Settings = Settings;`（自赋值），
            // 字段永远拿不到值、之后处处 NRE，而编译器一声不吭。
            Settings = settings;
            animator = settings.TargetAnimator();
            if (!EditorApplication.isPlaying || animator == null || !animator.isActiveAndEnabled)
                throw new InvalidOperationException("请进入播放模式，并确保调试对象上有一个已启用的 Animator。");
            // 不去碰 animator.runtimeAnimatorController，所以也不用限制 Animator 的更新模式：
            // 影子 Animator 用自己的默认更新，角色怎么更新是角色自己的事。
            try
            {
                // Validate even when no phone frames have arrived yet.
                using (var check = HoFaceAnimationAssets.Compile(Settings)) { }
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
            // 影子根上放一片**语义 Hub**：控制器里的「语义写手」用 `GetComponentInChildren` 找它，
            // 找不到就什么都写不进去（只报一句）。见 RelaySemantics() —— 它是"影子 → 角色"的中转站。
            shadowHub = shadowRoot.AddComponent<HoFaceSemanticHub>();
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
            if (animator == null || Settings.TargetAnimator() != animator || !animator.isActiveAndEnabled)
                throw new InvalidOperationException("目标 Animator 已变更或禁用，会话已停止。");
            foreach (var entry in meshRefs)
                if (entry.Key == null || entry.Key.sharedMesh != entry.Value) throw new InvalidOperationException("模型 Mesh 已变更，请重新检查绑定后启动。");
            bool changed = !configured;
            var nextSelected = new bool[52];
            double now = HoFaceClock.Now;

            // ── 输入行：线名 → 规范名（改名 + 量纲都在这里，接收端只交原样）──────────────
            // 规矩照 VBridger：**引用到的线名这一帧没来 ⇒ 这一行不写**（保持上一帧）。
            // 于是"两种协议的输入行同时存在"是安全的：哪个源在发，只有那一套行会动。
            EvaluateInputs(now, Mathf.Max(0f, deltaTime));

            foreach (var channel in Settings.channels)
            {
                if (channel == null) continue;
                int index = HoFaceTrackingChannels.IndexOf(channel.shape);
                if (index < 0) continue;
                float fade = Mathf.Max(0.01f, Settings.neutralFadeSeconds);
                bool hasValue = inputIndex.TryGetValue(channel.shape, out int inputRow);
                double age = hasValue ? HoFaceInputHub.LastFrameTime : 0;
                age = age > 0 ? now - age : double.MaxValue;
                bool fresh = hasValue && inputFresh[inputRow] && HoFaceInputHub.Connected && age <= Mathf.Max(0.1f, Settings.staleSeconds);
                // Never received live channels do not reserve model properties.
                bool mayWrite = channel.mode != HoFaceInputMode.Live || (hasValue && inputFresh[inputRow] && age <= Mathf.Max(0.1f, Settings.staleSeconds) + fade);
                if (channel.mode != lastModes[index] && channel.mode == HoFaceInputMode.Hold) held[index] = Effective[index];
                lastModes[index] = channel.mode;
                float neutral = Finite01(channel.neutral);
                float raw = hasValue ? inputValues[inputRow] : 0f;
                switch (channel.mode)
                {
                    case HoFaceInputMode.Manual: Effective[index] = Finite01(channel.manual); break;
                    case HoFaceInputMode.Hold: Effective[index] = held[index]; break;
                    case HoFaceInputMode.Neutral: Effective[index] = neutral; break;
                    case HoFaceInputMode.Release: break;
                    default:
                        // 输入曲线只作用在实时输入上：手动滑杆是调试用的，不该被它整形。
                        float live = HoFaceCurve.Transfer(channel.inputCurve, Finite01(raw));
                        Effective[index] = fresh ? live : Mathf.MoveTowards(Effective[index], neutral, deltaTime / fade);
                        break;
                }

                // 这里**没有区域门控**：哪些键算数由使用者自己的混合树/参数决定，不由我们注入开关。
                nextSelected[index] = mayWrite && channel.mode != HoFaceInputMode.Release;
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
            if (changed || runningController != Settings.FaceController() || stamp != mappingStamp)
            {
                Rebuild();
                mappingStamp = stamp;
                configured = true;
            }
            // 双眼同步（`HoFaceEyeSync`）已经删掉了：那是**混合树的事**，不该在参数生产这一层做。
            // 面板上那三个开关（eyeSync / eyeSyncMix / eyeSyncSingleKey）也一并退休 ——
            // 需要在树里合并就画在树里，这样"面板里看到什么 = Warudo 里是什么"才成立。
            // 这里曾经是：HoFaceEyeSync.Apply(Input, Settings.eyeSync, Settings.eyeSyncMix, Settings.eyeSyncSingleKey);

            // ── 参数生产：每一行 = 曲线(表达式(源键…))，再走它自己那串有序修饰符 ──────────
            // 这里**不再有**"某个键写某个参数"的硬编码：参数名与算法都在中间层资产里，
            // 控制器里没有那个参数名就跳过（不猜也不补）。轴也是普通一行：
            // `Ho/Drive/Lid/Left/BlinkWide = eyeBlinkLeft - eyeWideLeft`。
            double frameNow = HoFaceClock.Now;
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

            foreach (var preview in previews)
                if (parameters.Contains(preview.Key)) shadow.SetFloat(preview.Key, preview.Value);

            // ⚠️ **强制立刻求值一次**，然后调用方紧接着调 WriteOutputs() 就能读到这一帧的结果。
            //
            // 为什么必须这样：以前是组件的 Update/LateUpdate 一对 —— 设参数在 Update、抄回在
            // LateUpdate，中间那段时间让 Unity 自己把影子 Animator 算完。现在没有组件了，
            // 而"设参数"和"读结果"如果分在两个编辑器回调里，就是在**赌回调顺序**。
            // 自己 Update(0f) 之后，这一步变成同步的：设参数 → 求值 → 读值，一次调用里完成。
            // （影子是 AlwaysAnimate 的活动对象，Unity 之后还会再算一次 —— 参数没变，无害。）
            shadow.Update(0f);

            // 影子算完 → 控制器里的语义写手也写完了 → 把它搬到角色身上那片 Hub（Warudo 侧由节点做同一件事）。
            RelaySemantics();
        }

        /// <summary>
        /// 把影子 Hub 上的语义值**按名字**搬到角色身上那片 Hub（<see cref="HoFaceSemanticConnector.hub"/>）。
        ///
        /// 【为什么需要这一步】控制器里的「语义写手」跑在**影子**上（那是唯一在跑控制器的地方），
        /// 而消费方读的是**角色**上那片 Hub。Warudo 侧这同一件事由「HoFace写动态参数」节点做
        /// （`HoFaceHubWriteNode`）—— 两边都按**角色 Connector 的槽表**解释名字，所以"面板里看到什么
        /// = Warudo 里是什么"这条规矩在语义值上也成立。
        ///
        /// ⚠️ **名字对不上**（写手填的 `slot` 不在槽表里）是这套设计里最阴的失败：它完全不报错，
        /// 只表现为"某个语义永远不动"。所以这里会点名，而且只在结构变化时报一次。
        /// </summary>
        private void RelaySemantics()
        {
            if (shadowHub == null) return;

            GameObject character = Settings.Character();
            if (semanticConnector == null || semanticOwner != character)
            {
                semanticOwner = character;
                semanticConnector = character != null
                    ? character.GetComponentInChildren<HoFaceSemanticConnector>(true)
                    : null;
                semanticReported = null;
            }

            if (semanticConnector == null || semanticConnector.hub == null)
            {
                if (semanticReported != "no-connector")
                {
                    semanticReported = "no-connector";
                    SemanticStatus = "⚠ 角色上没有 Connector（或它没填 Hub）⇒ 语义值没有地方落";
                }
                return;
            }

            HoFaceSemanticHub target = semanticConnector.hub;
            target.Reserve(semanticConnector.Count);

            if (shadowHub.SlotCount == 0)
            {
                // 写手一格都没声明过。这一条**必须明说**：它和"值恒为 0"在面板上看起来一样，
                // 但原因完全不同（状态机行为没被调用 / 条目是空的 vs 参数本来就是 0）。
                if (semanticReported != "empty-shadow")
                {
                    semanticReported = "empty-shadow";
                    SemanticStatus = "⚠ 影子 Hub 还是空的 ⇒ 控制器里的「语义写手」没被调用，或它的条目是空的"
                        + "（写手挂在状态上、`OnStateUpdate` 每帧跑）";
                }
                return;
            }

            int written = 0;
            int skipped = 0;
            semanticSkipped.Clear();
            for (int i = 0; i < shadowHub.SlotCount; i++)
            {
                string name = shadowHub.NameAt(i);
                if (string.IsNullOrEmpty(name)) continue;      // 空位：写手还没声明过这一格
                int index = semanticConnector.IndexOf(name);
                if (index < 0 || index >= target.SlotCount)
                {
                    skipped++;
                    if (semanticSkipped.Count < 4) semanticSkipped.Add(name);
                    continue;
                }
                target.SetFloat(index, shadowHub.GetFloat(i));
                // 顺手把名字也镜像到角色那片 Hub 上：它在 Inspector 里就会自己说明"第 i 格是什么"，
                // 而不用去对照 Connector 的槽表。（名字来自槽表，顺序与它一致。）
                target.names[index] = name;
                written++;
            }

            string state = written + "|" + skipped + "|" + string.Join(",", semanticSkipped);
            if (state == semanticReported) return;
            semanticReported = state;

            SemanticStatus = "语义转发：写 " + written + " 个"
                + (skipped > 0
                    ? " · **跳过 " + skipped + " 个**（不在槽表里：" + string.Join("、", semanticSkipped) + "）"
                    : " · 全部对上");
            if (skipped > 0)
                Debug.LogWarning("[Ho 面捕] 语义转发：有 " + skipped + " 个名字不在角色的 Connector 槽表里（"
                    + string.Join("、", semanticSkipped) + "）⇒ 那几个语义永远不动。");
        }

        /// <summary>
        /// 求值输入行：<c>规范名 = 曲线(表达式(线名…))</c>。
        ///
        /// **缺键语义**（与 VBridger 一致）：表达式引用到的线名只要有一个这一帧没来，这一行就**不写** ——
        /// 值保持上一帧、并标记为"不新鲜"，让通道那边按断流规则回中性。
        /// 这条规矩让"两种协议的行同时存在"变成安全操作。
        /// </summary>
        private void EvaluateInputs(double now, float deltaTime)
        {
            for (int row = 0; row < inputRows.Length; row++)
            {
                var expression = inputExpressions[row];
                if (expression == null) { inputFresh[row] = false; continue; }

                touched.Clear();
                float value = expression.Evaluate(name =>
                {
                    touched.Add(name);
                    return HoFaceInputHub.Input(name);
                });

                bool present = touched.Count > 0;
                for (int i = 0; i < touched.Count; i++)
                    if (!HoFaceInputHub.Has(touched[i])) { present = false; break; }
                if (!present) { inputFresh[row] = false; continue; }

                value = inputRows[row].Transform(value);
                value = ApplyModifiers(row, inputRows[row], value, deltaTime, now,
                    inputSmooth, inputStepIndex, inputStepUntil, inputDelay);
                inputValues[row] = value;
                inputFresh[row] = true;
            }
        }

        /// <summary>
        /// 表达式取变量，三级（越靠前越"规范"）：
        /// ① **形态键**（52 个规范名）→ 走**通道值**（模式 / 输入曲线 / 断流回中性都算完的那一份；
        ///    通道的原始输入来自输入行，所以通道与输入行不会互相打架）；
        /// ② 其它**输入行的结果**（`headRotX`、`volume`…）；
        /// ③ 合并后的**原始线名**（`eyeBlink_L`、`Rotation_x`…）—— 想直接用原值也允许。
        /// 未知名字按 0（表达式求值器不抛异常）。
        /// </summary>
        private float Lookup(string name)
        {
            if (name == null) return 0f;
            int index = HoFaceTrackingChannels.CanonicalIndexOf(name);
            if (index >= 0) return Input[index];
            if (inputIndex.TryGetValue(name, out int row)) return inputValues[row];
            return HoFaceInputHub.Input(name);
        }

        /// <summary>
        /// 有序修饰符。按列出顺序生效（照 VBridger 的输出修饰符）：
        /// 平滑 / 延迟 / 维持，按修饰符链的顺序依次作用。
        /// 输入行与输出行共用这一套实现，只是状态数组各带一份（<paramref name="smooth"/> 等）。
        /// </summary>
        private float ApplyModifiers(int row, HoFaceOutput output, float value, float deltaTime, double now,
            float[] smooth, int[] stepRows, double[] stepUntil,
            Queue<(double At, float Value)>[] delay)
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
                        smooth[row] = !primed
                            ? value
                            : Mathf.Lerp(smooth[row], value, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / modifier.seconds));
                        value = smooth[row];
                        break;
                    case HoFaceModifierKind.Steps:
                        value = Step(row, modifier, value, now, stepRows, stepUntil);
                        break;
                    case HoFaceModifierKind.Delay:
                        value = Delay(row, modifier, value, now, delay);
                        break;
                    default:
                        break;
                }
            }

            return value;
        }

        private float ApplyModifiers(int row, HoFaceOutput output, float value, float deltaTime, double now) =>
            ApplyModifiers(row, output, value, deltaTime, now, outputSmooth, stepIndex, stepUntil, outputDelay);

        /// <summary>
        /// 延迟：一个**每行一条的 FIFO**，进去的值等 <c>seconds</c> 秒之后再出来。
        ///
        /// 单位是**秒**（与平滑一致）。VBridger 那份是"帧数"、由它用 `round(delay*0.06)` 从毫秒折出来，
        /// 与帧率绑定；我们统一走秒，换机器手感不变。
        ///
        /// 几个刻意的决定：
        /// * **不推入 NaN / 无穷** —— 放进去会让整条队列的值都变成 NaN，而且是永久性的。
        /// * **首帧先垫**：队列为空时直接返回 <paramref name="value"/>，不先垫一个 0，
        ///   否则会话一开始会从 0 爬上来（跟平滑那边 `primed` 的道理一样）。
        /// * **队列超时上限**：读数时间戳有抖动时，防止一条永远取不出来的队列无限长大。
        /// </summary>
        private float Delay(int row, HoFaceModifier modifier, float value, double now,
            Queue<(double At, float Value)>[] delay)
        {
            float seconds = Mathf.Max(0.0001f, modifier.seconds);
            Queue<(double At, float Value)> queue = delay[row];
            if (queue == null)
            {
                queue = new Queue<(double At, float Value)>();
                delay[row] = queue;
            }

            if (!float.IsNaN(value) && !float.IsInfinity(value))
            {
                queue.Enqueue((now + seconds, value));
            }

            float result = queue.Count > 0 ? queue.Peek().Value : value;
            while (queue.Count > 0 && (queue.Peek().At <= now || queue.Count > 512))
            {
                result = queue.Dequeue().Value;
            }

            return result;
        }

        /// <summary>
        /// 维持：参数过 <c>trigger</c> 就跳到 <c>target</c>，往下掉超过 <c>threshold</c> 才退回去，
        /// 触发后至少保持 <c>hold</c> 秒。没触发任何档时输出 0（等于隐含的"最小档"）。
        /// </summary>
        private float Step(int row, HoFaceModifier modifier, float value, double now, int[] stepRows, double[] stepUntil)
        {
            var steps = modifier.steps;
            if (steps == null || steps.Count == 0) return value;

            int next = -1;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null && value >= steps[i].trigger) next = i;

            int current = stepRows[row];
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
                stepRows[row] = next;
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
        /// 或者外部编辑器改了这个 .json）后实例可以不变。所以再带上"文件路径 + 写盘时间戳 + 解析出来的那个对象"
        /// （<see cref="HoFaceDebugSettings.Middleware"/> 会在路径或时间戳变化后重新解析）。
        /// </summary>
        private string MappingStamp()
        {
            var text = new System.Text.StringBuilder();
            text.Append("rows:").Append(Settings.Outputs().Count).Append('/').Append(Settings.Inputs().Count).Append(';');
            var middleware = Settings.Middleware;
            text.Append("profile:").Append(Settings.ProfileStamp)
                .Append(':').Append(middleware != null ? middleware.GetHashCode() : 0).Append(';');
            var controller = Settings.FaceController();
            text.Append("src:").Append(controller != null ? controller.GetInstanceID() : 0).Append(';');
            foreach (var mesh in Settings.Meshes())
                text.Append(mesh != null ? mesh.GetInstanceID() : 0).Append(';');
            return text.ToString();
        }

        /// <summary>把当前的输入行与输出行编译成"求值用"的数组（表达式解析一次，状态数组按行开）。</summary>
        private void BuildOutputs()
        {
            var middleware = Settings.Middleware;
            var inputList = Settings.Inputs();
            inputRows = new HoFaceOutput[inputList.Count];
            inputExpressions = new HoFaceExpression[inputList.Count];
            inputValues = new float[inputList.Count];
            inputFresh = new bool[inputList.Count];
            inputSmooth = new float[inputList.Count];
            inputStepIndex = new int[inputList.Count];
            inputStepUntil = new double[inputList.Count];
            inputDelay = new Queue<(double At, float Value)>[inputList.Count];
            inputIndex.Clear();
            for (int i = 0; i < inputStepIndex.Length; i++) inputStepIndex[i] = -1;
            for (int i = 0; i < inputList.Count; i++)
            {
                inputRows[i] = inputList[i];
                if (inputList[i] == null || string.IsNullOrEmpty(inputList[i].parameter)) continue;
                if (HoFaceExpression.TryParse(inputList[i].expression, out var parsed, out string inputError))
                    inputExpressions[i] = parsed;
                else
                    Debug.LogWarning("[Ho 面捕] 第 " + (i + 1) + " 条输入行的表达式用不了（" + inputList[i].parameter + "）：" + inputError);
                inputIndex[inputList[i].parameter] = i;   // 同名多行：最后一行生效（用户覆盖用）
            }

            var rows = Settings.Outputs();
            outputs = new HoFaceOutput[rows.Count];
            expressions = new HoFaceExpression[rows.Count];
            outputValues = new float[rows.Count];
            outputSmooth = new float[rows.Count];
            stepIndex = new int[rows.Count];
            stepUntil = new double[rows.Count];
            outputDelay = new Queue<(double At, float Value)>[rows.Count];
            outputIndex.Clear();
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
            }
        }

        private void Rebuild()
        {
            BuildOutputs();
            var next = HoFaceAnimationAssets.Compile(Settings, shape => { int i = HoFaceTrackingChannels.IndexOf(shape); return i >= 0 && selected[i]; });
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
                runningController = Settings.FaceController();
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
