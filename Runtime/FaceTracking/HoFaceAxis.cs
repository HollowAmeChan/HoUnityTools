using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 把"一对相反方向的 0~1 通道"合成"一根 -1~1 的单轴"。
    ///
    /// **为什么需要它**：ARKit 的 52 个通道里有一批是**成对的反向语义** ——
    /// `mouthSmileLeft` / `mouthFrownLeft`（欣 ↔ 悲）、`jawLeft` / `jawRight`（左 ↔ 右）、
    /// `eyeBlinkLeft` / `eyeWideLeft`（闭 ↔ 睁大）。让它们各占一个参数、各摆一棵树，
    /// 用户要摆两棵树，而且"两边都不明显"的那段会被两棵树各写一半。
    /// 合成一根轴之后就是**一棵三姿势的 1D 树**：`-1 / 0 / +1`。
    ///
    /// **这是参数中间层的活，不是混合树的活** —— 混合树只能"读参数、出姿势"，算不出新参数
    /// （这正是 19.2 那条论证的前提）。所以"值"和"树"必须成对交付：这里出值，
    /// 树那一半在控制器作品里（作者在混合树编辑器里摆），少一半都不成立。
    /// </summary>
    public static class HoFaceAxis
    {
        /// <summary>正向 − 反向，夹到 [-1, 1]。两边都是 0 时得到 0（中性）。</summary>
        public static float Merge(float positive, float negative) =>
            Mathf.Clamp(Finite(positive) - Finite(negative), -1f, 1f);

        /// <summary>把一根 -1~1 的轴拆回两个 0~1 分量（各自还是 0~1，不是带符号的）。</summary>
        public static void Split(float axis, out float positive, out float negative)
        {
            float value = Mathf.Clamp(Finite(axis), -1f, 1f);
            positive = value > 0f ? value : 0f;
            negative = value < 0f ? -value : 0f;
        }

        /// <summary>眼睑开合：`+1` = 闭（blink）/ `-1` = 睁大（wide）/ `0` = 中性。</summary>
        public static float LidOpenClose(float blink, float wide) => Merge(blink, wide);

        // ↓ 下面三个**目前没有消费者**（只有用例在测 Merge 本身）。它们是"成对反向通道 → 一根 −1~1 轴"
        //   的标准写法，但**哪些通道真的该合并、合并后树上摆几个姿势**这件事还没定案 —— 要先拿成熟实现
        //   与 Jerry 模板的做法取证，再决定给它们接线还是删掉。**别当成已经在用的功能。**

        /// <summary>唇角：`+1` = 欣（smile）/ `-1` = 悲（frown）。</summary>
        public static float SmileSad(float smile, float frown) => Merge(smile, frown);

        /// <summary>横向：`+1` = 往右 / `-1` = 往左。</summary>
        public static float Sideways(float right, float left) => Merge(right, left);

        /// <summary>
        /// 眼球两轴，各自由一对反向通道合成。
        /// **"内/外"是眼睛自己的定义**（左右眼的世界方向相反），这里不翻 —— 翻的是键怎么摆，
        /// 不把左右眼的差别藏进参数里。
        /// </summary>
        public static Vector2 Gaze(float inner, float outer, float up, float down) =>
            new Vector2(Merge(inner, outer), Merge(up, down));

        private static float Finite(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
}
