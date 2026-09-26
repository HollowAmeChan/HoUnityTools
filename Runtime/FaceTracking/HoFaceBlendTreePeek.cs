using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 混合树观察台：把**正在生效的面捕会话**（影子台）的参数每帧抄到一个可见的 Animator 上，
    /// 于是你在 Animator 窗口里选中它，就能看见那棵树的红点与叶子明暗 —— 编辑器自带的混合树视图
    /// 看不到这些，因为真正的影子台是隐藏对象。
    ///
    /// **用法**：新建一个空物体 → 只挂这个组件 → **填一个 controller**（要观察的那份）→ Play 并把面捕驱动起来
    /// → 选中它看 Animator 窗口。它需要一个 Animator，但**不需要你手动加**：运行时自己补一个
    /// （补的那个用 `HideInInspector`，所以 Inspector 上不会多出一行）。
    ///
    /// **参数值从哪来**：只有一个来源 —— 正在生效的面捕会话（`HoFaceShadowLink.Active`，会话启动时登记进来）。
    /// 没有会话在跑时它就静止在那儿，什么都不抄。
    ///
    /// **无损**：这个 Animator 不驱动任何渲染器、也不读任何输出，它求值出来的姿势没有去处；
    /// 参数只写它自己身上，角色 Animator 与影子台都不受影响。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Blend Tree Peek")]
    public sealed class HoFaceBlendTreePeek : MonoBehaviour
    {
        [Tooltip("要观察的那份 controller（面板里填一个就行）。\n"
            + "留空时退一步用**来源正在跑的那个** —— 想对比「资产里的树」与「运行中的树」时，填上资产里的那份。")]
        public RuntimeAnimatorController controller;

        // ── 面板用的只读读数：把"它到底在跟谁、跑的是哪一份"摆到 Inspector 上，而不是只写进日志 ──
        /// <summary>现在跟随的影子 Animator（没有会话时为 null）。</summary>
        public Animator BoundSource => boundSource;

        /// <summary>实际跑在这台 Animator 上的 controller（你填的那份，或退一步用来源那份）。</summary>
        public RuntimeAnimatorController BoundController =>
            animator != null ? animator.runtimeAnimatorController : null;

        /// <summary>正在抄的 float 参数个数（= 来源与这份 controller 的**参数交集**）。</summary>
        public int CopiedParameterCount => wanted.Count;

        /// <summary>这一刻有没有正在生效的面捕会话（`HoFaceShadowLink.Active`）。</summary>
        public static bool HasLiveSession => HoFaceShadowLink.Active != null;

        private readonly HashSet<string> wanted = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastWritten = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private Animator animator;
        private Animator boundSource;
        private bool intersectNextFrame;
        private bool warnedNoController;
        private GUIStyle boxStyle;

        private void Awake()
        {
            // Animator 不要求用户挂：这个空物体只是个挂载点，缺了就自己补。
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = gameObject.AddComponent<Animator>();
                // **自己补的那一个不在 Inspector 上占一行**（2026-09-27 用户要求）：
                // 用户自己挂的 Animator 保持原样（不是我们加的，不动它）。
                animator.hideFlags = HideFlags.HideInInspector;
            }
            // 没有渲染器，不设 AlwaysAnimate 可能被剔除掉 —— 那窗口里就什么都不会动。
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            if (controller != null) animator.runtimeAnimatorController = controller;
        }

        private void Update()
        {
            var live = HoFaceShadowLink.Active;          // 唯一来源：正在生效的面捕会话（影子台）
            if (live == null || live == animator) return;

            if (boundSource != live)
            {
                var applied = controller != null ? controller : live.runtimeAnimatorController;
                if (applied == null)
                {
                    // 静默失效最难查：这条只说一次，说清"为什么窗口里什么都没有"。
                    if (!warnedNoController)
                    {
                        warnedNoController = true;
                        Debug.LogWarning("[Ho 混合树观察台] 没有 controller 可跑：本组件上没填，来源（"
                            + live.name + "）也没有正在跑的 controller ⇒ Animator 窗口里看不到树。", this);
                    }
                    return;
                }
                warnedNoController = false;
                animator.runtimeAnimatorController = applied;

                // 以来源的参数表为准建"要抄的名单"；绑定后下一帧取一次交集，免得两边版本不同时刷警告。
                wanted.Clear();
                lastWritten.Clear();
                foreach (var parameter in live.parameters)
                    if (parameter.type == AnimatorControllerParameterType.Float)
                        wanted.Add(parameter.name);

                boundSource = live;
                intersectNextFrame = true;
                Debug.Log("[Ho 混合树观察台] 已跟随 " + live.name + "（controller：" + applied.name
                    + "，参数 " + wanted.Count + " 个）", this);
            }

            if (intersectNextFrame && animator.parameters.Length > 0)
            {
                var names = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var parameter in animator.parameters) names.Add(parameter.name);
                wanted.IntersectWith(names);
                intersectNextFrame = false;
            }

            foreach (var parameter in live.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float) continue;
                if (!wanted.Contains(parameter.name)) continue;
                float value = live.GetFloat(parameter.name);
                // 值没变就不写：静止时不该每帧都在弄脏 Animator。
                if (lastWritten.TryGetValue(parameter.name, out float previous)
                    && Mathf.Abs(previous - value) < 0.0001f)
                    continue;
                lastWritten[parameter.name] = value;
                animator.SetFloat(parameter.name, value);
            }

            // 层权重也要抄：树是按层求值的，层权重不对，看到的就不是真身。
            for (int i = 0; i < live.layerCount && i < animator.layerCount; i++)
                animator.SetLayerWeight(i, live.GetLayerWeight(i));
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            var live = HoFaceShadowLink.Active;
            if (live == null) return;
            if (boxStyle == null) boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 11 };
            GUILayout.BeginArea(new Rect(8f, 8f, 310f, 50f), boxStyle);
            GUILayout.Label("Ho 混合树观察台　源：" + live.name);
            GUILayout.Label("选中本物体 → Animator 窗口（无损：无渲染器、不读输出）");
            GUILayout.EndArea();
        }
    }
}
