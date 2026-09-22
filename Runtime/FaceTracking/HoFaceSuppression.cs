using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 通道之间的**抑制**：一个通道值大了，就把另一个压下去。
    ///
    /// **为什么必须有**：ARKit 的 52 个键不是互相独立的 —— `eyeBlink`（闭眼）与 `eyeSquint`（眯眼）
    /// 在模型上闭的是**同一块**，裸输入里两者会同时非 0，于是同一个形变被写了两遍 → 过眨眼。
    /// 这类"语义上重叠"的键还有：`jawOpen` 与唇形、`tongueOut` 与嘴部、`mouthClose` 与下颌。
    ///
    /// **参考实现怎么处理**：它把会叠加的语义放进**同一棵 2D 树**（眼睑 = 开合 × 眯眼，
    /// 由作者摆好 `Blink / Neutral / Wide / Squint / Open_Squint` 五个姿势）。树是**插值**，
    /// 所以两个轴走到哪就是哪个姿势，天然不会相加。这也解释了"为什么眼睑是 2D 而嘴是 1D"——
    /// 不是好看，是必须。
    ///
    /// **我们这边**：树木来是零散的直通（每个键一个子节点），所以先在**参数中间层**做等价的事 ——
    /// 这也正是 §18.5 说的"抑制放 C#、不放树"。公式是乘法衰减：
    ///
    ///     target' = target × (1 − driver × amount)
    ///
    /// 一眼可读的性质：driver 满值时目标归零；driver 为 0 时**一个字节都不动**；
    /// amount 是这条抑制的力度。纯函数，可以直接断言。
    /// </summary>
    public static class HoFaceSuppression
    {
        /// <summary>
        /// 默认抑制对。**眨眼压眯眼**：两者闭的是同一块，眨眼时眯眼那份要么已经被包含在
        /// 眨眼姿势里，要么就是跟踪器的重复上报。
        /// </summary>
        public static readonly (string Driver, string Target)[] Defaults =
        {
            ("eyeBlinkLeft", "eyeSquintLeft"),
            ("eyeBlinkRight", "eyeSquintRight")
        };

        /// <summary>就地抑制。`enabled` 关着、或 `amount` 为 0 时**一个字节都不动**。</summary>
        public static void Apply(float[] values, bool enabled, float amount, (string Driver, string Target)[] pairs = null)
        {
            if (values == null || !enabled)
            {
                return;
            }

            float strength = Mathf.Clamp01(Finite(amount));
            if (strength <= 0.0001f)
            {
                return;
            }

            (string Driver, string Target)[] rules = pairs ?? Defaults;
            for (int i = 0; i < rules.Length; i++)
            {
                int driver = HoFaceTrackingChannels.IndexOf(rules[i].Driver);
                int target = HoFaceTrackingChannels.IndexOf(rules[i].Target);
                if (driver < 0 || target < 0 || driver >= values.Length || target >= values.Length)
                {
                    continue;
                }

                values[target] *= 1.0f - Mathf.Clamp01(Finite(values[driver])) * strength;
            }
        }

        private static float Finite(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0.0f : value;
    }
}
