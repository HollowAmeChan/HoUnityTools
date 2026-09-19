using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 注视解算的纯数学：目标方向 → 参考系里的 yaw/pitch → 死区/分工/限位 → 头部方向与眼睛残余。
    /// 全部以角度为单位，不碰 Transform（写骨骼与写形态键由组件负责）。
    /// </summary>
    public static class HoLookAtSolver
    {
        /// <summary>把一个世界方向分解成参考系里的 yaw/pitch（度）。正 yaw = 参考系右侧，正 pitch = 上方。</summary>
        public static HoLookAtAngles Decompose(Vector3 forward, Vector3 up, Vector3 direction)
        {
            HoLookAtAngles angles = default;
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, up);
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, up);
            if (flatForward.sqrMagnitude > 1e-8f && flatDirection.sqrMagnitude > 1e-8f)
            {
                angles.yaw = Vector3.SignedAngle(flatForward.normalized, flatDirection.normalized, up);
            }

            Vector3 normalizedDirection = direction.sqrMagnitude > 1e-8f ? direction.normalized : forward.normalized;
            angles.pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(normalizedDirection, up.normalized), -1.0f, 1.0f)) * Mathf.Rad2Deg;
            return angles;
        }

        /// <summary>
        /// 按"死区 → 头部承担 → 限位"分工，返回头部要用的角度，残余部分写进 <paramref name="state"/> 供眼睛使用。
        /// 与设计文档的不变量一致：先判死区、再分工、最后把头部那部分夹到限位内，剩下的全给眼睛。
        ///
        /// **眼睛范围的溢出会还给头**：残余超过眼球能转的范围时，把超出的部分加回头部（再受头部限位约束），
        /// 然后重算残余。否则就会出现"总角 18°、头只转 7°、眼睛限位 10°" → 目光永远差 1° 的情况
        /// （实测非常显眼）。头部也没余量时才真的到不了，那时调试区会显示还差多少。
        /// </summary>
        public static HoLookAtAngles Split(
            HoLookAtAngles total,
            in HoHeadSettings head,
            in HoEyeSettings eye,
            float weight,
            ref HoLookAtState state)
        {
            float clampedWeight = Mathf.Clamp01(weight);
            float yaw = total.yaw * clampedWeight;
            float pitch = total.pitch * clampedWeight;

            float magnitude = Mathf.Sqrt(yaw * yaw + pitch * pitch);
            if (magnitude <= head.deadZone || magnitude <= 1e-4f)
            {
                // 死区内：头部不动，眼睛全吃
                state.eyeYaw = yaw;
                state.eyePitch = pitch;
                state.eyeOverflowYaw = 0.0f;
                state.eyeOverflowPitch = 0.0f;
                return default;
            }

            // 超过死区的部分按 headShare 分给头部
            float share = Mathf.Clamp01(head.headShare);
            float headMagnitude = (magnitude - head.deadZone) * share;
            float scale = headMagnitude / magnitude;

            float headYaw = yaw * scale;
            float headPitch = pitch * scale;

            // 限位：按轴独立夹取。
            // 不用"椭圆整体缩放" —— 那会把斜向目标按比例拉回中心，眼睛就精确指不到目标了（实测能偏 5~7°）。
            headYaw = Mathf.Clamp(headYaw, -head.yawLimit, head.yawLimit);
            headPitch = Mathf.Clamp(headPitch, -head.pitchLimit, head.pitchLimit);

            // 眼球转不过来的部分交回头部（头部还有余量时，总方向仍然指得到目标）
            float overflowYaw = Overflow(yaw - headYaw, eye.angleLimitInner, eye.angleLimitOuter);
            float overflowPitch = Overflow(pitch - headPitch, eye.angleLimitUp, eye.angleLimitDown);
            if (overflowYaw != 0.0f || overflowPitch != 0.0f)
            {
                headYaw = Mathf.Clamp(headYaw + overflowYaw, -head.yawLimit, head.yawLimit);
                headPitch = Mathf.Clamp(headPitch + overflowPitch, -head.pitchLimit, head.pitchLimit);
            }

            state.eyeYaw = yaw - headYaw;
            state.eyePitch = pitch - headPitch;
            state.eyeOverflowYaw = overflowYaw;
            state.eyeOverflowPitch = overflowPitch;

            HoLookAtAngles headAngles;
            headAngles.yaw = headYaw;
            headAngles.pitch = headPitch;
            return headAngles;
        }

        /// <summary>超出眼睛范围的量（带符号）：正数表示往正方向超了，负数表示往负方向超了。</summary>
        private static float Overflow(float value, float positiveLimit, float negativeLimit)
        {
            if (value > positiveLimit)
            {
                return value - positiveLimit;
            }

            if (value < -negativeLimit)
            {
                return value + negativeLimit;
            }

            return 0.0f;
        }

        /// <summary>兼容旧签名：不把眼睛溢出还给头（等价于眼睛范围无限）。</summary>
        public static HoLookAtAngles Split(
            HoLookAtAngles total,
            in HoHeadSettings head,
            float weight,
            ref HoLookAtState state)
        {
            HoEyeSettings unlimited = new HoEyeSettings
            {
                angleLimitInner = 180.0f,
                angleLimitOuter = 180.0f,
                angleLimitUp = 180.0f,
                angleLimitDown = 180.0f
            };
            return Split(total, head, unlimited, weight, ref state);
        }

        /// <summary>
        /// 按轴独立夹取（yaw 一个上限、pitch 一个上限）。
        /// 注意**不要**改用"椭圆整体缩放"：那会让斜向目标被按比例拉回中心，
        /// 眼睛就精确指不到目标了（斜 30°/24° 配 35/25 的限位会偏 6° 以上）。
        /// </summary>
        public static void ClampAxes(ref float yaw, ref float pitch, float yawLimit, float pitchLimit)
        {
            yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);
            pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
        }

        /// <summary>由参考系的前/上 + yaw/pitch 生成一个方向。</summary>
        public static Vector3 DirectionFromAngles(Vector3 forward, Vector3 up, float yaw, float pitch)
        {
            Quaternion frame = Quaternion.LookRotation(forward, up);
            return frame * Quaternion.Euler(-pitch, yaw, 0.0f) * Vector3.forward;
        }

        /// <summary>把"头部承担角度"换算成一个可以交给 SetLookAtPosition 的世界点。</summary>
        public static Vector3 PointFromAngles(Vector3 origin, Vector3 forward, Vector3 up, HoLookAtAngles angles, float distance)
        {
            return origin + DirectionFromAngles(forward, up, angles.yaw, angles.pitch) * Mathf.Max(0.01f, distance);
        }

        /// <summary>目标方向在参考系中"前方"一侧的角度（用于 spineMinAngle 之类的门槛判断）。</summary>
        public static float AngleToDirection(Vector3 forward, Vector3 direction)
        {
            if (forward.sqrMagnitude < 1e-8f || direction.sqrMagnitude < 1e-8f)
            {
                return 0.0f;
            }

            return Vector3.Angle(forward.normalized, direction.normalized);
        }

        /// <summary>一阶平滑（时间常数 0 = 直通），带角速度上限（度/秒，0 = 不限）。</summary>
        public static float SmoothAngle(float current, float target, float deltaTime, float timeConstant, float maxSpeed)
        {
            float result = current;
            if (timeConstant <= 0.0f || deltaTime <= 0.0f)
            {
                result = target;
            }
            else
            {
                float t = 1.0f - Mathf.Exp(-deltaTime / timeConstant);
                result = Mathf.Lerp(current, target, t);
            }

            if (maxSpeed > 0.0f && deltaTime > 0.0f)
            {
                float maxDelta = maxSpeed * deltaTime;
                result = Mathf.MoveTowards(current, result, maxDelta);
            }

            return result;
        }
    }
}
