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
        /// 优先级分工：**眼睛 → 头颈 → 脊椎**（一层吃不下才交给下一层）。
        ///
        /// 真实角色的注视是"先转眼珠、再转头脖、最后才带脊椎"，所以这里不用"按固定比例分"：
        ///   1) 眼睛先吃：能吃多少 = 该方向上的眼球范围 × 眼球强度
        ///      （眼球强度就是"眼睛愿意出多少力"：1 = 在自己范围内尽量吃，0.3 = 只吃三成，剩下的交给头颈）；
        ///   2) 头颈再吃：剩下的，最多到头部限位；
        ///   3) 脊椎最后：按"头颈的负载"参与 —— 头颈越接近自己的限位、身体跟得越多，上限是身体强度。
        /// 眼球因为强度 &lt; 1 而少吃的那部分会自然落到头颈上，所以目光仍落在目标上（不会"差一点点"）。
        ///
        /// 返回值是**头部**要用的角度；眼睛的（最终）角度与脊椎权重写在 <paramref name="state"/> 里。
        /// 注意总强度不在这里乘：头部由 Unity 的 SetLookAtWeight 施加，眼睛由调用方施加，各一次。
        /// </summary>
        public static HoLookAtAngles Split(
            HoLookAtAngles total,
            in HoHeadSettings head,
            in HoEyeSettings eye,
            float eyeStrength,
            float bodyStrength,
            ref HoLookAtState state)
        {
            float eyeScale = Mathf.Clamp01(eyeStrength);
            float bodyScale = Mathf.Clamp01(bodyStrength);

            // 1) 眼睛先吃
            float eyeYaw = TakeUpTo(total.yaw, eye.angleLimitInner, eye.angleLimitOuter) * eyeScale;
            float eyePitch = TakeUpTo(total.pitch, eye.angleLimitUp, eye.angleLimitDown) * eyeScale;

            // 2) 头颈再吃（吃剩下的，最多到限位）
            float headYaw = Mathf.Clamp(total.yaw - eyeYaw, -head.yawLimit, head.yawLimit);
            float headPitch = Mathf.Clamp(total.pitch - eyePitch, -head.pitchLimit, head.pitchLimit);

            // 3) 脊椎最后：按头颈的负载参与
            float loadYaw = head.yawLimit > 0.01f ? Mathf.Abs(total.yaw - eyeYaw) / head.yawLimit : 1.0f;
            float loadPitch = head.pitchLimit > 0.01f ? Mathf.Abs(total.pitch - eyePitch) / head.pitchLimit : 1.0f;
            float load = Mathf.Clamp01(Mathf.Max(loadYaw, loadPitch));

            state.eyeYaw = eyeYaw;
            state.eyePitch = eyePitch;
            state.spineWeight = bodyScale * load;

            // 诊断用：眼睛没吃下、交给头颈的部分
            state.eyeOverflowYaw = total.yaw - eyeYaw;
            state.eyeOverflowPitch = total.pitch - eyePitch;

            HoLookAtAngles headAngles;
            headAngles.yaw = headYaw;
            headAngles.pitch = headPitch;
            return headAngles;
        }

        /// <summary>在限位内"能吃多少吃多少"（按方向取限位，正方向用正限位）。</summary>
        private static float TakeUpTo(float value, float positiveLimit, float negativeLimit)
        {
            if (value >= 0.0f)
            {
                return Mathf.Min(value, Mathf.Max(0.0f, positiveLimit));
            }

            return Mathf.Max(value, -Mathf.Max(0.0f, negativeLimit));
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

        /// <summary>目标方向与参考系前方的夹角（度）。</summary>
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
