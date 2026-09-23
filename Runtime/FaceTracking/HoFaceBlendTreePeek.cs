using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 混合树**观察台**（调试道具）：一个**可见、空、无渲染器**的 GameObject，挂同一个 controller，
    /// 每帧把两个参数按正弦喂进去。它要回答的是四个"凭经验猜、但没实测"的问题 ——
    /// 这四个问题的答案决定"让编辑器看见运行中的混合树"能不能做成一个通用组件：
    ///
    /// 1. Play 模式下选中一个正在跑的 Animator，Animator 窗口**是否画出** 2D 混合位置的红点与叶子明暗？
    /// 2. controller 是**运行期创建、没有磁盘路径**的对象时（勾上「运行期复制」），窗口还画不画？
    /// 3. controller 里的曲线指向本物体上**不存在的路径**（本物体没有子物体）时，Console 会不会报错/警告？
    /// 4. 窗口里显示的层权重，是**控制器的默认值**还是**该 Animator 的 live 值**？（改「第 2 层权重」看它变不变）
    ///
    /// **它是无损的**：本物体不驱动任何渲染器、也不读任何输出，它求值出来的姿势没有去处；
    /// 参数是写它自己身上的，与角色的 Animator、与面捕的影子台都没有关系。
    ///
    /// 用法：Play → 在 Hierarchy 里选中本物体 → 打开 Animator 窗口 → 看那棵树动不动、Console 干不干净。
    /// **看不到树不等于方案不成立**：可能是窗口不认运行期的 controller（第 2 条），所以磁盘资产与运行期复制
    /// 两个变体都要试一遍再下结论。
    /// </summary>
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Blend Tree Peek")]
    public sealed class HoFaceBlendTreePeek : MonoBehaviour
    {
        [Tooltip("要观察的 controller：用生成出来的那个资产（Ho/00 Drive 那个 .controller）。")]
        public RuntimeAnimatorController controller;

        [Tooltip("运行期复制一份再挂上 —— 复制出来的对象**没有磁盘路径**，用来验证第 2 条问题。"
            + "两个变体都要试：不勾（磁盘资产）与勾上（内存对象）。")]
        public bool useRuntimeCopy;

        [Tooltip("横轴参数名。默认是左眼眼睑的两根轴 —— 它们正好驱动那棵 2D 树，红点会在方阵里走。")]
        public string parameterX = HoFaceNaming.LidAxis(0, true);
        [Tooltip("纵轴参数名。")]
        public string parameterY = HoFaceNaming.LidAxis(0, false);

        [Tooltip("两根轴的正弦频率（Hz）。取**不同值**，红点才会走成李萨如曲线 —— 比一条直线更容易看出它在动。")]
        public float speedX = 0.35f;
        public float speedY = 0.55f;

        [Tooltip("运行时把第 2 层权重强制设成这个值（< 0 = 不碰）。验证第 4 条："
            + "窗口里那一行的权重跟着动 = 显示的是 live 值；不动 = 显示的是控制器默认值。")]
        public float secondLayerWeight = -1f;

        [Tooltip("再建一个空 Animator，把参数与层权重抄过去 —— 验证同一个 controller 能不能同时挂两个 Animator。")]
        public bool mirror;

        private Animator animator;
        private Animator mirrorAnimator;
        private GameObject mirrorRoot;
        private float currentX;
        private float currentY;
        private GUIStyle boxStyle;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;

            RuntimeAnimatorController applied = controller;
            if (useRuntimeCopy && controller != null)
            {
                var copy = new AnimatorOverrideController(controller) { name = controller.name + "（运行期复制）" };
                applied = copy;
            }

            animator.runtimeAnimatorController = applied;

            // 参数名不存在时红点不会动，容易被误读成"窗口画不出来" —— 先校验，缺了就退回一个存在的 Float。
            WarnIfMissing(parameterX, parameterY, ref parameterX);
            WarnIfMissing(parameterY, parameterX, ref parameterY);

            if (mirror && applied != null)
            {
                mirrorRoot = new GameObject(name + "（镜像）");
                mirrorRoot.transform.SetParent(transform.parent, false);
                mirrorAnimator = mirrorRoot.AddComponent<Animator>();
                mirrorAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                mirrorAnimator.runtimeAnimatorController = applied;
            }

            Debug.Log("[Ho 混合树观察台] controller = " + (applied != null ? applied.name : "（空）")
                + (useRuntimeCopy ? "，运行期复制（无磁盘路径）" : "，磁盘资产")
                + "；层数 " + animator.layerCount
                + "；参数 " + animator.parameters.Length
                + "；横轴 " + parameterX + "，纵轴 " + parameterY
                + (mirror && applied != null ? "；另建了一个镜像 Animator" : string.Empty)
                + "。选中本物体并打开 Animator 窗口，看那棵树有没有红点/明暗在动。", this);
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            // X 走 −1..1、Y 走 0..1：正好是眼睑两根轴的真实值域（X 双边、Y 单端）。
            currentX = Mathf.Sin(Time.time * speedX * Mathf.PI * 2f);
            currentY = 0.5f + 0.5f * Mathf.Sin(Time.time * speedY * Mathf.PI * 2f);
            animator.SetFloat(parameterX, currentX);
            animator.SetFloat(parameterY, currentY);

            if (secondLayerWeight >= 0f && animator.layerCount > 1)
                animator.SetLayerWeight(1, secondLayerWeight);

            if (mirrorAnimator == null) return;
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Float)
                    mirrorAnimator.SetFloat(parameter.name, animator.GetFloat(parameter.name));
            for (int i = 0; i < animator.layerCount && i < mirrorAnimator.layerCount; i++)
                mirrorAnimator.SetLayerWeight(i, animator.GetLayerWeight(i));
        }

        private void OnDestroy()
        {
            if (mirrorRoot != null) Destroy(mirrorRoot);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (boxStyle == null) boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 11 };
            GUILayout.BeginArea(new Rect(8f, 8f, 330f, 96f), boxStyle);
            GUILayout.Label("Ho 混合树观察台（无损：无渲染器、不读输出）");
            GUILayout.Label("横轴 " + parameterX + " = " + currentX.ToString("F3"));
            GUILayout.Label("纵轴 " + parameterY + " = " + currentY.ToString("F3"));
            GUILayout.Label(useRuntimeCopy ? "controller：运行期复制（无磁盘路径）" : "controller：磁盘资产");
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
