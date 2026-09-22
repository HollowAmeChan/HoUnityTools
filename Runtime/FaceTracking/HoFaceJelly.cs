using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>果冻的一维状态：位移 + 速度。显式保存，方便单测直接喂阶跃。</summary>
    public struct HoFaceJellyState
    {
        public float value;
        public float velocity;
    }

    /// <summary>
    /// 一维阻尼谐振子（果冻 / 摆锤）。中间层的"参数生产"里产出带物理的那个参数。
    ///
    /// **为什么自己实现、而不是接一个外部物理：** 三个宿主里只有我们自己这条路能跑任意逻辑 ——
    /// Warudo 的摆锤写死、进不了蓝图（用户明确不要依赖它）；VRC 侧要曲线驱动参数，那边没这个
    /// 能力。而它本来就是一维的东西，下面几十行就够。
    ///
    /// **为什么做成纯函数：** 果冻的定义就是"过冲 + 回弹 + 稳定"，而这三样最好用一条阶跃响应
    /// 断言来验收 —— 纯函数可以直接喂，不用跑一遍动画去肉眼观察。
    /// </summary>
    public static class HoFaceJelly
    {
        /// <summary>频率上限。再高（或帧率再低）子步数会失控。</summary>
        public const float MaxFrequency = 30f;

        public static void Reset(ref HoFaceJellyState state, float value = 0f)
        {
            state.value = value;
            state.velocity = 0f;
        }

        /// <summary>
        /// 推进一步。半隐式欧拉 + 子步：频率高或帧率低时一步积分会直接发散，子步是必需的
        /// （一个周期至少切 8 刀）。
        /// </summary>
        public static void Step(ref HoFaceJellyState state, float target, float deltaTime,
            float frequency, float damping)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            if (float.IsNaN(target) || float.IsInfinity(target)) target = 0f;

            frequency = Mathf.Clamp(frequency, 0.01f, MaxFrequency);
            damping = Mathf.Clamp(damping, 0f, 4f);

            float omega = 2f * Mathf.PI * frequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * frequency * 8f), 1, 64);
            float h = deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                // 半隐式欧拉：先更速度再更位移，比显式欧拉稳得多。
                state.velocity += (-omega * omega * (state.value - target) - 2f * damping * omega * state.velocity) * h;
                state.value += state.velocity * h;
            }
        }
    }
}
