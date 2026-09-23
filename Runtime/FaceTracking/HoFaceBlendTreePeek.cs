using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 混合树观察台：把**正在生效的面捕会话**（影子台）的参数每帧抄到一个可见的 Animator 上，
    /// 于是你在 Animator 窗口里选中它，就能看见那棵树的红点与叶子明暗 —— 编辑器自带的混合树视图
    /// 看不到这些，因为真正的影子台是隐藏对象。
    ///
    /// **用法**：新建一个空物体 → 只挂这个组件 → Play（并把面捕驱动起来）→ 选中它看 Animator 窗口。
    /// 它需要一个 Animator，但**不需要你手动加**：运行时自己补一个 —— 所以那个空物体只是个挂载点。
    ///
    /// **无损**：这个 Animator 不驱动任何渲染器、也不读任何输出，它求值出来的姿势没有去处；
    /// 参数只写它自己身上，角色 Animator 与影子台都不受影响。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Blend Tree Peek")]
    public sealed class HoFaceBlendTreePeek : MonoBehaviour
    {
        [Tooltip("跟随哪个 Animator。留空 = 跟随当前正在生效的面捕会话（影子台）。"
            + "也可以拖别的 Animator 进来观察 —— 那就与面捕无关了，任何管线都能用。")]
        public Animator sourceAnimator;

        [Tooltip("观察用的 controller。**留空最省事**：直接用来源正在跑的那一个。"
            + "只有想对比「资产里的树」与「运行中的树」时才填。")]
        public RuntimeAnimatorController controller;

        private readonly HashSet<string> wanted = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastWritten = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private Animator animator;
        private Animator boundSource;
        private bool intersectNextFrame;
        private GUIStyle boxStyle;

        private void Awake()
        {
            // Animator 不要求用户挂：这个空物体只是个挂载点，缺了就自己补。
            animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            // 没有渲染器，不设 AlwaysAnimate 可能被剔除掉 —— 那窗口里就什么都不会动。
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            if (controller != null) animator.runtimeAnimatorController = controller;
        }

        private void Update()
        {
            var live = sourceAnimator != null ? sourceAnimator : HoFaceShadowLink.Active;
            if (live == null || live == animator) return;

            if (boundSource != live)
            {
                var applied = controller != null ? controller : live.runtimeAnimatorController;
                if (applied == null) return;
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
            var live = sourceAnimator != null ? sourceAnimator : HoFaceShadowLink.Active;
            if (live == null) return;
            if (boxStyle == null) boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 11 };
            GUILayout.BeginArea(new Rect(8f, 8f, 310f, 50f), boxStyle);
            GUILayout.Label("Ho 混合树观察台　跟随：" + live.name);
            GUILayout.Label("选中本物体 → Animator 窗口（无损：无渲染器、不读输出）");
            GUILayout.EndArea();
        }
    }
}
