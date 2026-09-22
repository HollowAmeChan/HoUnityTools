using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 双眼同步：把左右眼合成一个值再写回去。
    ///
    /// **为什么需要它**：有些模型的左右眨眼键**各自都能闭双眼**（美术为了不让两只眼睛
    /// 闭合程度不一致，干脆把两个键都做成"双眼眨"）。面捕给的是左右两个通道，值又总有细微差异，
    /// 左右一起触发时同一个形变被写两遍 → **过眨眼**。所以非 wink 的情况下要强制两侧同值。
    ///
    /// **语义沿用参考实现**（`EyeSync` / `EyeSyncMix`，默认 0 / 0.5）：
    /// `sync` 是 0~1 的**交叉淡入**（0 = 左右独立、允许 wink；1 = 完全同步），
    /// `mix` 决定同步到哪个值（0 = 全用左眼、0.5 = 平均、1 = 全用右眼）。
    /// **作用范围也照它**：眼睑 + 眼球**横向**；眼球纵向不进同步。
    ///
    /// 混合树自己算不出这个（它只能"读参数、出姿势"），所以它属于**参数中间层** ——
    /// 跟"两个 0~1 合成一根 -1~1 的轴"是同一类活。
    /// </summary>
    public static class HoFaceEyeSync
    {
        /// <summary>
        /// 要同步的成对通道。眼睑是**同名左右成对**；
        /// 眼球横向是**跨对**才等于"同一个世界方向"——
        /// 左眼 In 与右眼 Out 都是"往右看"，左眼 Out 与右眼 In 都是"往左看"。
        /// </summary>
        public static readonly (string Left, string Right)[] Pairs =
        {
            ("eyeBlinkLeft", "eyeBlinkRight"),
            ("eyeSquintLeft", "eyeSquintRight"),
            ("eyeWideLeft", "eyeWideRight"),
            ("eyeLookInLeft", "eyeLookOutRight"),
            ("eyeLookOutLeft", "eyeLookInRight")
        };

        /// <summary>
        /// 就地把每一对的两个值朝同一个值靠。`sync` 为 0（默认）时**一个字节都不动**。
        /// 纯函数：直接在数组上跑，所以"过眨眼被治住了"这条可以直接断言，不用真机看。
        /// </summary>
        public static void Apply(float[] values, float sync, float mix)
        {
            if (values == null)
            {
                return;
            }

            float amount = Mathf.Clamp01(Finite(sync));
            if (amount <= 0.0001f)
            {
                return;
            }

            float blend = Mathf.Clamp01(Finite(mix));
            for (int i = 0; i < Pairs.Length; i++)
            {
                int a = HoFaceTrackingChannels.IndexOf(Pairs[i].Left);
                int b = HoFaceTrackingChannels.IndexOf(Pairs[i].Right);
                if (a < 0 || b < 0 || a >= values.Length || b >= values.Length)
                {
                    continue;
                }

                float shared = Mathf.Lerp(values[a], values[b], blend);
                values[a] = Mathf.Lerp(values[a], shared, amount);
                values[b] = Mathf.Lerp(values[b], shared, amount);
            }
        }

        private static float Finite(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0.0f : value;
    }
}
