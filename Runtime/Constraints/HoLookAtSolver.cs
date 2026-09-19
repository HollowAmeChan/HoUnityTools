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
        /// </summary>
        public static HoLookAtAngles Split(
            HoLookAtAngles total,
            in HoHeadSettings head,
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
                return default;
            }

            // 超过死区的部分按 headShare 分给头部
            float share = Mathf.Clamp01(head.headShare);
            float headMagnitude = (magnitude - head.deadZone) * share;
            float scale = headMagnitude / magnitude;

            float headYaw = yaw * scale;
            float headPitch = pitch * scale;

            // 限位（俯仰对称，对角线方向按椭圆比例整体缩放，避免对角超限）
            headYaw = Mathf.Clamp(headYaw, -head.yawLimit, head.yawLimit);
            headPitch = Mathf.Clamp(headPitch, -head.pitchLimit, head.pitchLimit);
            if (head.yawLimit > 0.0f && head.pitchLimit > 0.0f)
            {
                ClampToEllipse(ref headYaw, ref headPitch, head.yawLimit, head.pitchLimit);
            }

            state.eyeYaw = yaw - headYaw;
            state.eyePitch = pitch - headPitch;

            HoLookAtAngles headAngles;
            headAngles.yaw = headYaw;
            headAngles.pitch = headPitch;
            return headAngles;
        }

        /// <summary>椭圆夹取：把 (x, y) 缩到 (x/maxX)² + (y/maxY)² ≤ 1 之内。</summary>
        public static void ClampToEllipse(ref float x, ref float y, float maxX, float maxY)
        {
            if (maxX <= 0.0f || maxY <= 0.0f)
            {
                x = 0.0f;
                y = 0.0f;
                return;
            }

            float nx = x / maxX;
            float ny = y / maxY;
            float length = Mathf.Sqrt(nx * nx + ny * ny);
            if (length > 1.0f)
            {
                float scale = 1.0f / length;
                x *= scale;
                y *= scale;
            }
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
