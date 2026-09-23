using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 混合树**观察台**：一个**可见、空、无渲染器**的 GameObject，挂一个 controller，
    /// 每帧往它的参数里写值 —— 于是你在 Animator 窗口里选中它，就能看到那棵树的红点与叶子明暗。
    ///
    /// **两种取值模式**（<see cref="source"/>）：
    /// <list type="bullet">
    /// <item><b>跟随正在生效的会话</b>：每帧把**影子台**（面捕会话里那个隐藏的 Animator）的
    /// **全部 Float 参数 + 每层权重**抄过来 —— 这才是"看真实输入"。影子台由
    /// <see cref="HoFaceShadowLink"/> 登记，不需要你拖任何引用。</item>
    /// <item><b>正弦测试信号</b>：自己造两个正弦值写进去。**与真实输入无关**，只用来验证
    /// "Animator 窗口画不画得出一棵正在跑的树"。看到"很规则地动"就是它的签名，不是真实输入。</item>
    /// </list>
    ///
    /// **它是无损的**：本物体不驱动任何渲染器、也不读任何输出，抄过来的姿势求值出来没有去处；
    /// 参数只写它自己身上，角色 Animator 与影子台都不受影响。
    ///
    /// 用法：Play（要真实输入就再把面捕驱动起来）→ 在 Hierarchy 里选中本物体 → 打开 Animator 窗口。
    /// 物体上的小面板显示当前模式、来源、已抄参数个数与两轴值，方便和窗口里的显示对照。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Blend Tree Peek")]
    public sealed class HoFaceBlendTreePeek : MonoBehaviour
    {
        /// <summary>参数从哪来。</summary>
        public enum PeekSource
        {
            [InspectorName("跟随正在生效的会话（真实输入）")] FollowSession,
            [InspectorName("正弦测试信号（与真实输入无关）")] Sine
        }

        [Tooltip("取值模式。跟随会话 = 看真实输入；正弦 = 只验证窗口能不能显示一棵正在跑的树。")]
        public PeekSource source = PeekSource.FollowSession;

        [Tooltip("跟随哪个 Animator。留空 = 用当前正在生效的面捕会话（影子台）。"
            + "也可以拖别的 Animator 进来 —— 那就与面捕无关了，任何管线都能观察。")]
        public Animator sourceAnimator;

        [Tooltip("要观察的 controller。**建议留空**：跟随模式下会自动用影子台正在跑的那个"
            + "（运行期编译、没有磁盘路径，顺便验证窗口认不认这种 controller）。"
            + "填了就显示你指定的资产 —— 那时最好就是影子台跑的那一份，否则参数表可能对不上。")]
        public RuntimeAnimatorController controller;

        [Tooltip("运行期复制一份再挂上 —— 复制出来的对象**没有磁盘路径**，用来验证窗口对内存 controller 还画不画。")]
        public bool useRuntimeCopy;

        [Tooltip("【正弦模式】横轴参数名。菜单建物体时会自动填成 controller 里那棵 2D 树的真实轴。")]
        public string parameterX = HoFaceNaming.LidAxis(0, true);
        [Tooltip("【正弦模式】纵轴参数名。")]
        public string parameterY = HoFaceNaming.LidAxis(0, false);

        [Tooltip("【正弦模式】两根轴的正弦频率（Hz）。取不同值，红点才会走成李萨如曲线。")]
        public float speedX = 0.35f;
        public float speedY = 0.55f;

        [Tooltip("额外把第 2 层权重强制设成这个值（< 0 = 不碰）。验证：窗口里那一行的权重跟着变 = "
            + "显示的是 live 值；不变 = 显示的是控制器默认值。")]
        public float secondLayerWeight = -1f;

        [Tooltip("每秒抄几次。0 = 每帧。编辑器窗口自己都不是 60 帧刷新，写那么勤对显示没有意义；"
            + "调低能直接减负（静止时尤其明显）。卡的话先把它调到 10 试试。")]
        public int updatesPerSecond = 20;

        private readonly HashSet<string> copyable = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastWritten = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private Animator animator;
        private Animator boundSource;
        private bool tightenNextFrame;
        private float currentX;
        private float currentY;
        private float nextUpdateTime;
        private int copiedParameters;
        private GUIStyle boxStyle;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;

            if (source == PeekSource.Sine)
            {
                animator.runtimeAnimatorController = CopyIfAsked(controller);
                // 参数名不存在时红点不会动，容易被误读成"窗口画不出来" —— 先校验、缺了就退回一个存在的 Float。
                WarnIfMissing(parameterX, parameterY, ref parameterX);
                WarnIfMissing(parameterY, parameterX, ref parameterY);
                if (parameterX == parameterY)
                    Debug.LogWarning("[Ho 混合树观察台] 横纵轴解析成了同一个参数（" + parameterX
                        + "）—— 红点只会沿一条直线来回，2D 图上看不出插值。", this);
            }
            else if (controller != null)
            {
                // 跟随模式下也可以先挂上资产；等会话起来再换成它正在跑的那个。
                animator.runtimeAnimatorController = CopyIfAsked(controller);
            }
        }

        private void Update()
        {
            if (animator == null) return;

            // 限频：编辑器窗口不是 60 帧刷新的，每帧写一遍只是白干活（静止时更明显）。
            if (updatesPerSecond > 0)
            {
                if (Time.unscaledTime < nextUpdateTime) return;
                nextUpdateTime = Time.unscaledTime + 1f / updatesPerSecond;
            }

            if (source == PeekSource.FollowSession)
            {
                FollowSession();
                return;
            }

            if (animator.runtimeAnimatorController == null) return;

            // X 走 −1..1、Y 走 0..1：正好是眼睑两根轴的真实值域（X 双边、Y 单端）。
            currentX = Mathf.Sin(Time.time * speedX * Mathf.PI * 2f);
            currentY = 0.5f + 0.5f * Mathf.Sin(Time.time * speedY * Mathf.PI * 2f);
            animator.SetFloat(parameterX, currentX);
            animator.SetFloat(parameterY, currentY);

            if (secondLayerWeight >= 0f && animator.layerCount > 1)
                animator.SetLayerWeight(1, secondLayerWeight);
        }

        /// <summary>把正在生效的会话（影子台）的全部 Float 参数与层权重抄过来 —— 这就是"看真实输入"。</summary>
        private void FollowSession()
        {
            var live = sourceAnimator != null ? sourceAnimator : HoFaceShadowLink.Active;
            if (live == null || live == animator) return;

            if (boundSource != live)
            {
                var applied = controller != null ? controller : live.runtimeAnimatorController;
                if (applied == null) return;
                applied = CopyIfAsked(applied);
                animator.runtimeAnimatorController = applied;

                // 以**来源**的参数表为准建"要抄的名单"。镜像挂的通常就是同一个 controller，名单自然一致。
                copyable.Clear();
                lastWritten.Clear();
                foreach (var parameter in live.parameters)
                    if (parameter.type == AnimatorControllerParameterType.Float)
                        copyable.Add(parameter.name);

                boundSource = live;
                tightenNextFrame = true;
                Debug.Log("[Ho 混合树观察台] 已跟随 " + live.name + "，controller = " + applied.name
                    + (controller != null ? "（你指定的资产）" : "（影子台正在跑的那个：没有磁盘路径）")
                    + "；要抄的 Float 参数 " + copyable.Count + " 个", this);
            }

            if (tightenNextFrame && animator.parameters.Length > 0)
            {
                // 绑定后的下一帧镜像的参数表已刷新：取交集兜一层，免得两边版本不同时每帧刷"参数不存在"。
                var names = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var parameter in animator.parameters) names.Add(parameter.name);
                copyable.IntersectWith(names);
                tightenNextFrame = false;
            }

            copiedParameters = 0;
            foreach (var parameter in live.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;
                if (!copyable.Contains(parameter.name)) continue;
                float value = live.GetFloat(parameter.name);
                if (parameter.name == parameterX) currentX = value;
                if (parameter.name == parameterY) currentY = value;

                // 值没变就不写：静止的脸不该每帧都在弄脏 Animator（窗口那边是有代价的）。
                if (lastWritten.TryGetValue(parameter.name, out float previous)
                    && Mathf.Abs(previous - value) < 0.0001f)
                    continue;
                lastWritten[parameter.name] = value;
                animator.SetFloat(parameter.name, value);
                copiedParameters++;
            }

            // 层权重也要抄：树是按层求值的，层权重不同，看到的就不是真身。
            for (int i = 0; i < live.layerCount && i < animator.layerCount; i++)
                animator.SetLayerWeight(i, live.GetLayerWeight(i));

            if (secondLayerWeight >= 0f && animator.layerCount > 1)
                animator.SetLayerWeight(1, secondLayerWeight);
        }

        private RuntimeAnimatorController CopyIfAsked(RuntimeAnimatorController applied)
        {
            if (!useRuntimeCopy || applied == null) return applied;
            return new AnimatorOverrideController(applied) { name = applied.name + "（运行期复制）" };
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (boxStyle == null) boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 11 };
            var live = sourceAnimator != null ? sourceAnimator : HoFaceShadowLink.Active;
            GUILayout.BeginArea(new Rect(8f, 8f, 370f, 110f), boxStyle);
            GUILayout.Label("Ho 混合树观察台（无损：无渲染器、不读输出）");
            if (source == PeekSource.FollowSession)
            {
                GUILayout.Label("模式：跟随会话　来源：" + (live != null ? live.name : "（没有正在生效的会话）"));
                GUILayout.Label("名单 " + copyable.Count + " 个参数　本次实际写入 " + copiedParameters
                    + "　节奏 " + (updatesPerSecond > 0 ? updatesPerSecond + " Hz" : "每帧")
                    + (useRuntimeCopy ? "　（运行期复制）" : string.Empty));
                GUILayout.Label("横轴 " + parameterX + " = " + currentX.ToString("F3")
                    + "　纵轴 " + parameterY + " = " + currentY.ToString("F3"));
            }
            else
            {
                GUILayout.Label("模式：正弦测试信号（与真实输入无关）");
                GUILayout.Label("横轴 " + parameterX + " = " + currentX.ToString("F3"));
                GUILayout.Label("纵轴 " + parameterY + " = " + currentY.ToString("F3"));
            }

            GUILayout.EndArea();
        }

        /// <summary>参数名不在 controller 里就换一个存在的 Float，免得"红点不动"被误读成"窗口画不出来"。</summary>
        private void WarnIfMissing(string wanted, string other, ref string slot)
        {
            if (animator == null) return;
            foreach (var parameter in animator.parameters)
                if (parameter.name == wanted) return;

            string fallback = null;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;
                if (parameter.name == other) continue;
                fallback = parameter.name;
                break;
            }

            Debug.LogWarning("[Ho 混合树观察台] 参数 " + wanted + " 不在这个 controller 里"
                + (fallback != null ? "，改用 " + fallback : "，而且它连一个 Float 参数都没有"), this);
            if (fallback != null) slot = fallback;
        }
    }
}
