using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 摆锤求解器输入。角度单位为度，长度单位为米，加速度单位为 m/s²，角速度单位为弧度/秒，时间单位为秒。
    /// 除 <see cref="localAcceleration"/>、<see cref="localUp"/> 和 <see cref="localAngularVelocity"/> 外，
    /// 其余字段来自约束组件的序列化参数。
    /// </summary>
    public struct HoPendulumSolverInput
    {
        /// <summary>锚点本地坐标系中的线加速度，驱动液面倾斜的主项。</summary>
        public Vector3 localAcceleration;

        /// <summary>世界 up 在锚点本地坐标系中的单位向量，用于让液面在世界空间中保持水平。</summary>
        public Vector3 localUp;

        /// <summary>锚点本地坐标系中的角速度（弧度/秒），仅用于离心项。</summary>
        public Vector3 localAngularVelocity;

        /// <summary>摆长（米）。只影响摆锤可视化位置与离心项半径，不决定晃动频率。</summary>
        public float length;

        /// <summary>液面响应固有频率（Hz）。</summary>
        public float frequency;

        /// <summary>阻尼比。1 为临界阻尼，越小晃动持续越久。</summary>
        public float dampingRatio;

        /// <summary>倾斜角上限（度），按合成倾角夹取。</summary>
        public float maxAngle;

        /// <summary>加速度灵敏度倍率。1 为物理值。</summary>
        public float sensitivity;

        /// <summary>参考重力加速度（m/s²）。</summary>
        public float gravity;

        /// <summary>朝向跟随强度 0~1。0 表示液面始终垂直于锚点自身轴，1 表示始终水平于世界。</summary>
        public float gravityInfluence;

        /// <summary>离心项强度 0~1。</summary>
        public float centrifugalInfluence;

        /// <summary>单个子步的最长时间，用于保证积分精度与帧率无关。</summary>
        public float maxStep;

        /// <summary>饱和柔和度 0~1。0 为硬夹取，1 为完全 tanh 软饱和。</summary>
        public float saturationSoftness;

        /// <summary>平衡角一阶滤波时间常数（秒）。0 表示不滤波。</summary>
        public float equilibriumSmoothing;

        /// <summary>是否启用竖直方向的径向弹簧自由度。</summary>
        public bool radialEnabled;

        /// <summary>1g 静止时的伸长量（米），同时决定径向弹簧的固有频率 √(g/ΔL)。0 表示刚性。</summary>
        public float restElongation;

        /// <summary>径向弹簧阻尼比。</summary>
        public float radialDampingRatio;

        public static HoPendulumSolverInput CreateDefault()
        {
            HoPendulumSolverInput input = new HoPendulumSolverInput();
            input.localAcceleration = Vector3.zero;
            input.localUp = Vector3.up;
            input.localAngularVelocity = Vector3.zero;
            input.length = 0.25f;
            input.frequency = 3.0f;
            input.dampingRatio = 0.18f;
            input.maxAngle = 20.0f;
            input.sensitivity = 1.0f;
            input.gravity = 9.81f;
            input.gravityInfluence = 1.0f;
            input.centrifugalInfluence = 1.0f;
            input.maxStep = 1.0f / 240.0f;
            input.saturationSoftness = 0.65f;
            input.equilibriumSmoothing = 0.035f;
            input.radialEnabled = true;
            input.restElongation = 0.02f;
            input.radialDampingRatio = 0.25f;
            return input;
        }

        /// <summary>把参数收敛到求解器可安全使用的范围。</summary>
        public void Sanitize()
        {
            localAcceleration = SanitizeVector(localAcceleration);
            localAngularVelocity = SanitizeVector(localAngularVelocity);
            localUp = SanitizeVector(localUp);
            if (localUp.sqrMagnitude < 0.000001f)
            {
                localUp = Vector3.up;
            }
            else
            {
                localUp.Normalize();
            }

            length = Mathf.Clamp(SanitizeFloat(length), 0.0f, 1000.0f);
            frequency = Mathf.Clamp(SanitizeFloat(frequency), HoPendulumSolver.MinFrequency, HoPendulumSolver.MaxFrequency);
            dampingRatio = Mathf.Clamp(SanitizeFloat(dampingRatio), 0.0f, 10.0f);
            maxAngle = Mathf.Clamp(SanitizeFloat(maxAngle), HoPendulumSolver.MinMaxAngle, HoPendulumSolver.MaxMaxAngle);
            sensitivity = Mathf.Clamp(SanitizeFloat(sensitivity), 0.0f, 100.0f);
            gravity = Mathf.Clamp(SanitizeFloat(gravity), 0.001f, 1000.0f);
            gravityInfluence = Mathf.Clamp01(SanitizeFloat(gravityInfluence));
            centrifugalInfluence = Mathf.Clamp01(SanitizeFloat(centrifugalInfluence));
            maxStep = Mathf.Clamp(SanitizeFloat(maxStep), 1.0f / 2000.0f, 0.5f);
            saturationSoftness = Mathf.Clamp01(SanitizeFloat(saturationSoftness));
            equilibriumSmoothing = Mathf.Clamp(SanitizeFloat(equilibriumSmoothing), 0.0f, 1.0f);
            restElongation = Mathf.Clamp(SanitizeFloat(restElongation), 0.0f, 100.0f);
            radialDampingRatio = Mathf.Clamp(SanitizeFloat(radialDampingRatio), 0.0f, 10.0f);
        }

        internal static float SanitizeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0.0f : value;
        }

        internal static Vector3 SanitizeVector(Vector3 value)
        {
            return new Vector3(SanitizeFloat(value.x), SanitizeFloat(value.y), SanitizeFloat(value.z));
        }
    }

    /// <summary>摆锤求解器输出。角度单位为度，角速度单位为度/秒，法线为锚点本地坐标系中的单位向量。</summary>
    public struct HoPendulumSolverOutput
    {
        /// <summary>当前倾斜角（度），正值表示液面法线朝本地 +X 倾斜。</summary>
        public float angleX;

        /// <summary>当前倾斜角（度），正值表示液面法线朝本地 +Z 倾斜。</summary>
        public float angleZ;

        /// <summary>平衡倾斜角（度），已含滤波与软饱和，是振荡器实际追赶的目标。</summary>
        public float equilibriumAngleX;

        /// <summary>平衡倾斜角（度）。</summary>
        public float equilibriumAngleZ;

        /// <summary>角速度（度/秒）。</summary>
        public float angularVelocityX;

        /// <summary>角速度（度/秒）。</summary>
        public float angularVelocityZ;

        /// <summary>相对平衡位置的振荡幅度，按最大倾斜角归一化到 0~1。</summary>
        public float amplitude;

        /// <summary>累加相位（弧度，0~2π）。改频率不会导致相位跳变。</summary>
        public float phase;

        /// <summary>当前液面法线（锚点本地坐标系）。</summary>
        public Vector3 localNormal;

        /// <summary>目标液面法线（锚点本地坐标系），由滤波后的平衡角还原。</summary>
        public Vector3 localEquilibriumNormal;

        /// <summary>合倾斜角（度），按幅值计算。</summary>
        public float tiltMagnitude;

        /// <summary>目标合倾斜角（度）。</summary>
        public float equilibriumTiltMagnitude;

        /// <summary>有效重力相对参考重力的倍率。静止为 1，自由落体趋近 0，向上加速大于 1。</summary>
        public float effectiveGravity;

        /// <summary>径向弹簧当前偏移（米）。0 表示正好在静止摆长上。</summary>
        public float radialOffset;

        /// <summary>径向弹簧平衡偏移（米）。</summary>
        public float radialEquilibrium;

        /// <summary>径向速度（米/秒）。</summary>
        public float radialVelocity;

        /// <summary>本帧是否触及最大倾斜角。</summary>
        public bool saturated;

        /// <summary>本帧是否因为状态出现非有限值而被就地复位（NaN 自愈，见 Step）。</summary>
        public bool recovered;

        /// <summary>倾斜方向（锚点本地坐标系，YZ 平面内的单位向量，指向液面抬升的一侧）。</summary>
        public Vector2 tiltDirection;
    }

    /// <summary>
    /// 摆锤求解器：纯数学实现，不依赖 MonoBehaviour，可脱离 Unity 场景单独测试。
    /// <para>
    /// 倾斜部分：平衡法线由有效重力决定
    /// <c>n = normalize(a_local * sensitivity + g * vertical + centrifugal)</c>，
    /// 其中 <c>vertical</c> 在锚点自身轴与世界 up 之间按朝向跟随强度球面插值。
    /// 每轴按 <c>θ'' = -ω²(θ - θ_eq) - 2ζω θ'</c> 做半隐式欧拉积分，并自动分子步。
    /// 平衡角按合成幅值做可调软饱和，先滤波再积分，因此恒加速度下的稳态严格满足 <c>tan(θ) = a / g</c>。
    /// </para>
    /// <para>
    /// 径向部分：摆动只改变方向，竖直方向的伸缩由一维弹簧描述，
    /// <c>x'' = -ω_r²(x - x_eq) - 2ζ_r ω_r x'</c>，<c>x_eq = ΔL * (g_eff / g - 1)</c>，
    /// <c>ω_r = √(g / ΔL)</c>。静止时偏移为 0，自由落体时收缩，过载时伸长。
    /// </para>
    /// </summary>
    public struct HoPendulumSolver
    {
        public const float MinFrequency = 0.01f;
        public const float MaxFrequency = 20.0f;
        public const float MinMaxAngle = 0.01f;
        public const float MaxMaxAngle = 89.0f;
        public const int MaxSubSteps = 64;

        /// <summary>子步的稳定系数：半隐式欧拉要求 ω·h &lt; 2，这里取 0.5，留 4 倍余量。</summary>
        private const float SubStepStabilityFactor = 0.5f;

        /// <summary>子步长下限，避免极高频参数把子步数顶爆。</summary>
        private const float MinEffectiveStep = 0.0001f;

        /// <summary>径向偏移的安全边界 = 静止伸长的这么多倍（再取一个绝对下限）。</summary>
        private const float MaxRadialOffsetFactor = 12.0f;

        /// <summary>径向偏移安全边界的绝对下限（米）。静止伸长很小时不至于把量程卡得过死。</summary>
        private const float MaxRadialOffsetMinimum = 0.1f;

        private const float TwoPi = 6.28318530718f;
        private const float Epsilon = 0.00000001f;

        /// <summary>当前倾斜角（弧度）。</summary>
        public float angleX;

        /// <summary>当前倾斜角（弧度）。</summary>
        public float angleZ;

        /// <summary>角速度（弧度/秒）。</summary>
        public float angularVelocityX;

        /// <summary>角速度（弧度/秒）。</summary>
        public float angularVelocityZ;

        /// <summary>累加相位（弧度）。</summary>
        public float phase;

        /// <summary>是否已初始化。未初始化时第一次求值会直接落到平衡位置，避免启用瞬间抖动。</summary>
        public bool initialized;

        /// <summary>滤波后的平衡角（弧度），振荡器实际追赶的目标。</summary>
        public float smoothedEquilibriumX;

        /// <summary>滤波后的平衡角（弧度）。</summary>
        public float smoothedEquilibriumZ;

        /// <summary>是否已有滤波后的平衡角。</summary>
        public bool hasSmoothedEquilibrium;

        /// <summary>径向弹簧偏移（米）。</summary>
        public float radialOffset;

        /// <summary>径向速度（米/秒）。</summary>
        public float radialVelocity;

        public void Reset()
        {
            angleX = 0.0f;
            angleZ = 0.0f;
            angularVelocityX = 0.0f;
            angularVelocityZ = 0.0f;
            phase = 0.0f;
            initialized = false;
            smoothedEquilibriumX = 0.0f;
            smoothedEquilibriumZ = 0.0f;
            hasSmoothedEquilibrium = false;
            radialOffset = 0.0f;
            radialVelocity = 0.0f;
        }

        /// <summary>把倾斜状态直接放到给定角度（弧度）并清零角速度。</summary>
        public void SnapTo(float radiansX, float radiansZ)
        {
            angleX = radiansX;
            angleZ = radiansZ;
            angularVelocityX = 0.0f;
            angularVelocityZ = 0.0f;
            initialized = true;
            smoothedEquilibriumX = radiansX;
            smoothedEquilibriumZ = radiansZ;
            hasSmoothedEquilibrium = false;
        }

        /// <summary>由摆长换算单摆固有频率（Hz）。仅用于「摆长决定频率」的物理单摆模式。</summary>
        public static float FrequencyFromLength(float length, float gravity)
        {
            if (length <= 0.0001f)
            {
                return 0.0f;
            }

            return Mathf.Sqrt(Mathf.Max(0.0001f, gravity) / length) / TwoPi;
        }

        /// <summary>径向弹簧固有频率（Hz）。静止伸长越小，弹簧越硬。</summary>
        public static float RadialFrequencyFromElongation(float restElongation, float gravity)
        {
            if (restElongation <= 0.000001f)
            {
                return 0.0f;
            }

            return Mathf.Sqrt(Mathf.Max(0.0001f, gravity) / restElongation) / TwoPi;
        }

        /// <summary>
        /// 把幅值按软饱和压缩。使用光滑最小值 <c>m / (1 + (m/L)^p)^(1/p)</c>：
        /// 小角度下几乎就是线性（相对误差二阶），渐近线严格是 limit，永远不会超过 limit。
        /// 柔和度为 0 时退化为硬夹取，为 1 时 p = 2（最软）。
        /// </summary>
        public static float SoftLimitMagnitude(float magnitude, float limit, float softness)
        {
            if (limit <= 0.0f || magnitude <= 0.0f)
            {
                return 0.0f;
            }

            float clampedSoftness = Mathf.Clamp01(softness);
            float hard = Mathf.Min(magnitude, limit);
            if (clampedSoftness <= 0.0f)
            {
                return hard;
            }

            float exponent = Mathf.Lerp(6.0f, 2.0f, clampedSoftness);
            float ratio = magnitude / limit;
            float soft = magnitude / Mathf.Pow(1.0f + Mathf.Pow(ratio, exponent), 1.0f / exponent);
            return Mathf.Lerp(hard, soft, clampedSoftness);
        }

        /// <summary>
        /// 单位向量之间的球面插值。用纯数学实现，避免依赖 UnityEngine 的内部调用，
        /// 并且对完全反向的两个向量给出确定的结果（绕任意垂直轴旋转 t·π），不会像 Lerp+归一化那样在中间塌成零向量。
        /// </summary>
        public static Vector3 InterpolateDirection(Vector3 from, Vector3 to, float t)
        {
            float blend = Mathf.Clamp01(t);
            float dot = Mathf.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
            float angle = Mathf.Acos(dot);

            if (angle < 0.0001f)
            {
                return to;
            }

            if (angle > Mathf.PI - 0.0001f)
            {
                Vector3 reference = Mathf.Abs(from.x) < 0.9f ? Vector3.right : Vector3.forward;
                Vector3 axis = Vector3.Cross(from, reference);
                if (axis.sqrMagnitude < Epsilon)
                {
                    return to;
                }

                axis.Normalize();
                return RotateAroundAxis(from, axis, Mathf.PI * blend);
            }

            float sin = Mathf.Sin(angle);
            return (from * Mathf.Sin((1.0f - blend) * angle) + to * Mathf.Sin(blend * angle)) / sin;
        }

        private static Vector3 RotateAroundAxis(Vector3 value, Vector3 axis, float radians)
        {
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return value * cos + Vector3.Cross(axis, value) * sin + axis * (Vector3.Dot(axis, value) * (1.0f - cos));
        }

        /// <summary>由两个轴的倾斜角（弧度）还原液面法线。</summary>
        public static Vector3 NormalFromAngles(float radiansX, float radiansZ)
        {
            float height = Mathf.Cos(radiansX) * Mathf.Cos(radiansZ);
            return new Vector3(Mathf.Sin(radiansX), height, Mathf.Sin(radiansZ));
        }

        /// <summary>平衡角只做数值保护：接近与本地 Y 轴垂直时 atan2 会发散。</summary>
        public const float MaxEquilibriumAngle = 89.5f;

        /// <summary>由区域坐标记录的方向还原倾斜角（弧度）。</summary>
        public static void AnglesFromNormal(Vector3 normal, out float radiansX, out float radiansZ)
        {
            float height = Mathf.Max(Mathf.Abs(normal.y), 0.0001f);
            radiansX = Mathf.Atan2(normal.x, height);
            radiansZ = Mathf.Atan2(normal.z, height);
        }

        /// <summary>计算有效重力方向（液面平衡法线）与有效重力倍率。</summary>
        public static Vector3 ComputeEquilibriumNormal(in HoPendulumSolverInput input, out float effectiveGravity)
        {
            Vector3 vertical = InterpolateDirection(Vector3.up, input.localUp, Mathf.Clamp01(input.gravityInfluence));
            if (vertical.sqrMagnitude < Epsilon)
            {
                vertical = Vector3.up;
            }

            vertical.Normalize();

            Vector3 centrifugal = Vector3.zero;
            if (input.length > 0.0f && input.centrifugalInfluence > 0.0f)
            {
                Vector3 radius = Vector3.down * input.length;
                centrifugal = Vector3.Cross(input.localAngularVelocity, Vector3.Cross(input.localAngularVelocity, radius));
                centrifugal *= input.centrifugalInfluence;
            }

            Vector3 drive = input.localAcceleration * input.sensitivity + centrifugal;
            effectiveGravity = (drive + vertical * input.gravity).magnitude / input.gravity;

            Vector3 normal = drive + vertical * input.gravity;
            if (normal.sqrMagnitude < Epsilon)
            {
                normal = vertical;
            }

            normal.Normalize();
            return normal;
        }

        /// <summary>
        /// 计算平衡倾斜角（弧度）与平衡法线。
        /// <para>
        /// 平衡角**不按最大倾斜角压缩**：液面必须完整跟随重力方向，瓶子放平或倒过来时也要正确，
        /// 否则「屈服重力」就无从谈起。最大倾斜角限制的是相对平衡面的摆动幅度，见 <see cref="Step"/>。
        /// </para>
        /// </summary>
        public static void ComputeEquilibrium(
            in HoPendulumSolverInput input,
            out float equilibriumX,
            out float equilibriumZ,
            out Vector3 equilibriumNormal)
        {
            equilibriumNormal = ComputeEquilibriumNormal(input, out float unusedGravity);
            AnglesFromNormal(equilibriumNormal, out equilibriumX, out equilibriumZ);

            float guard = MaxEquilibriumAngle * Mathf.Deg2Rad;
            equilibriumX = Mathf.Clamp(equilibriumX, -guard, guard);
            equilibriumZ = Mathf.Clamp(equilibriumZ, -guard, guard);
        }

        /// <summary>把法线朝给定倾斜向量方向掰过去（倾斜向量会先投影到法线的切平面）。</summary>
        public static Vector3 ApplyTilt(Vector3 normal, Vector3 tiltVector)
        {
            Vector3 perpendicular = tiltVector - (normal * Vector3.Dot(tiltVector, normal));
            float angle = perpendicular.magnitude;
            if (angle < 0.000001f)
            {
                return normal;
            }

            Vector3 axis = Vector3.Cross(normal, perpendicular / angle);
            if (axis.sqrMagnitude < Epsilon)
            {
                return normal;
            }

            axis.Normalize();
            Vector3 tilted = RotateAroundAxis(normal, axis, angle);
            return tilted.sqrMagnitude < Epsilon ? normal : tilted.normalized;
        }

        /// <summary>推进一帧仿真。</summary>
        public void Step(in HoPendulumSolverInput input, float deltaTime, out HoPendulumSolverOutput output)
        {
            float delta = Mathf.Max(0.0f, deltaTime);
            float maxAngleRadians = input.maxAngle * Mathf.Deg2Rad;

            Vector3 rawNormal = ComputeEquilibriumNormal(input, out float effectiveGravity);
            AnglesFromNormal(rawNormal, out float rawEquilibriumX, out float rawEquilibriumZ);
            float guard = MaxEquilibriumAngle * Mathf.Deg2Rad;
            rawEquilibriumX = Mathf.Clamp(rawEquilibriumX, -guard, guard);
            rawEquilibriumZ = Mathf.Clamp(rawEquilibriumZ, -guard, guard);

            // 平衡角单级低通：滤波对象是有界的平衡角，直流增益为 1，
            // 所以恒加速度下的稳态倾角不受滤波影响，只是响应延迟了一个时间常数。
            if (!hasSmoothedEquilibrium || input.equilibriumSmoothing <= 0.0f || delta <= 0.0f)
            {
                smoothedEquilibriumX = rawEquilibriumX;
                smoothedEquilibriumZ = rawEquilibriumZ;
                hasSmoothedEquilibrium = true;
            }
            else
            {
                float blend = 1.0f - Mathf.Exp(-delta / input.equilibriumSmoothing);
                smoothedEquilibriumX += (rawEquilibriumX - smoothedEquilibriumX) * blend;
                smoothedEquilibriumZ += (rawEquilibriumZ - smoothedEquilibriumZ) * blend;
            }

            float equilibriumX = smoothedEquilibriumX;
            float equilibriumZ = smoothedEquilibriumZ;

            if (!initialized)
            {
                SnapTo(equilibriumX, equilibriumZ);
            }

            float omega = input.frequency * TwoPi;
            float stiffness = omega * omega;
            float damping = 2.0f * input.dampingRatio * omega;

            bool radialActive = input.radialEnabled && input.restElongation > 0.000001f;
            float radialOmega = radialActive ? Mathf.Sqrt(input.gravity / input.restElongation) : 0.0f;
            float radialStiffness = radialOmega * radialOmega;
            float radialDamping = 2.0f * input.radialDampingRatio * radialOmega;
            float radialEquilibrium = radialActive ? input.restElongation * (effectiveGravity - 1.0f) : 0.0f;
            float radialLimit = radialActive
                ? Mathf.Max(input.restElongation * MaxRadialOffsetFactor, MaxRadialOffsetMinimum)
                : 0.0f;

            // 子步上限要同时满足两件事：
            //   ① 不超过用户给的 maxStep；
            //   ② 不超过这个刚度的数值稳定边界（半隐式欧拉要求 ω·h < 2）。
            // 只钳「子步数」是不够的：切出窗口再回来、编辑器卡顿、域重载之后，
            // 一帧的 delta 可以是几秒，step = delta / 64 会远大于 maxStep，
            // 径向弹簧（ω_r = √(g/ΔL) ≈ 25.6 rad/s）按 (ω_r·h)² 几何增长，
            // 64 步就溢出成 Infinity，再靠 Infinity − Infinity 变成 NaN。
            // 角度那一路有 ClampDeviation 兜着，径向那一路没有 —— 所以先炸的正是 _LiquidOffset。
            float stiffest = Mathf.Max(omega, radialOmega);
            float stableStep = stiffest > 0.0f ? SubStepStabilityFactor / stiffest : input.maxStep;
            float effectiveMaxStep = Mathf.Max(MinEffectiveStep, Mathf.Min(input.maxStep, stableStep));

            // 长帧只模拟这么多：宁可让这一帧的动画慢一点，也不让积分器超步长发散。
            float simulatedDelta = Mathf.Min(delta, effectiveMaxStep * MaxSubSteps);

            int stepCount = Mathf.Clamp(Mathf.CeilToInt(simulatedDelta / effectiveMaxStep), 1, MaxSubSteps);
            float step = simulatedDelta / stepCount;
            bool saturated = false;

            if (step > 0.0f)
            {
                for (int i = 0; i < stepCount; i++)
                {
                    angularVelocityX += (-stiffness * (angleX - equilibriumX) - damping * angularVelocityX) * step;
                    angularVelocityZ += (-stiffness * (angleZ - equilibriumZ) - damping * angularVelocityZ) * step;
                    angleX += angularVelocityX * step;
                    angleZ += angularVelocityZ * step;

                    if (radialActive)
                    {
                        radialVelocity += (-radialStiffness * (radialOffset - radialEquilibrium) - radialDamping * radialVelocity) * step;
                        radialOffset += radialVelocity * step;

                        // 径向没有 ClampDeviation 那样的物理夹取，必须自己兜住量程
                        ClampRadialOffset(radialLimit);
                    }

                    saturated |= ClampDeviation(equilibriumX, equilibriumZ, maxAngleRadians, input.saturationSoftness);
                }
            }

            // 兜底自愈：状态里一旦留下非有限值，之后每帧都会重算出 NaN，用户只能重进 Play
            // （OnEnable -> ResetState）才恢复。这里就地回到平衡位姿，让这一帧的输出就已经是好的。
            bool recovered = false;
            if (!HasFiniteState())
            {
                angleX = equilibriumX;
                angleZ = equilibriumZ;
                angularVelocityX = 0.0f;
                angularVelocityZ = 0.0f;
                radialOffset = radialEquilibrium;
                radialVelocity = 0.0f;
                recovered = true;
            }

            if (!radialActive)
            {
                radialOffset = 0.0f;
                radialVelocity = 0.0f;
            }

            phase = Mathf.Repeat(phase + omega * delta, TwoPi);

            float deviationX = angleX - equilibriumX;
            float deviationZ = angleZ - equilibriumZ;
            float deviation = Mathf.Sqrt(deviationX * deviationX + deviationZ * deviationZ);
            float tiltMagnitude = Mathf.Sqrt(angleX * angleX + angleZ * angleZ);
            float equilibriumMagnitude = Mathf.Sqrt(equilibriumX * equilibriumX + equilibriumZ * equilibriumZ);

            output = new HoPendulumSolverOutput();
            output.angleX = angleX * Mathf.Rad2Deg;
            output.angleZ = angleZ * Mathf.Rad2Deg;
            output.equilibriumAngleX = equilibriumX * Mathf.Rad2Deg;
            output.equilibriumAngleZ = equilibriumZ * Mathf.Rad2Deg;
            output.angularVelocityX = angularVelocityX * Mathf.Rad2Deg;
            output.angularVelocityZ = angularVelocityZ * Mathf.Rad2Deg;
            output.amplitude = maxAngleRadians > 0.0f ? Mathf.Clamp01(deviation / maxAngleRadians) : 0.0f;
            output.phase = phase;

            // 当前法线 = 平衡法线（未折叠，任意朝向都指向真实的重力反方向）再叠加摆动偏移。
            // 用向量而不是两轴角度还原，瓶子放平/倒过来时方向才不会丢。
            Vector3 deviationVector = new Vector3(
                Mathf.Sin(angleX - equilibriumX),
                0.0f,
                Mathf.Sin(angleZ - equilibriumZ));
            output.localNormal = ApplyTilt(rawNormal, deviationVector);
            output.localEquilibriumNormal = rawNormal;
            output.tiltMagnitude = tiltMagnitude * Mathf.Rad2Deg;
            output.equilibriumTiltMagnitude = equilibriumMagnitude * Mathf.Rad2Deg;
            output.effectiveGravity = effectiveGravity;
            output.radialOffset = radialOffset;
            output.radialEquilibrium = radialEquilibrium;
            output.radialVelocity = radialVelocity;
            output.saturated = saturated;
            output.recovered = recovered;

            Vector2 tilt = new Vector2(Mathf.Sin(angleX), Mathf.Sin(angleZ));
            output.tiltDirection = tilt.sqrMagnitude > Epsilon ? tilt.normalized : Vector2.zero;
        }

        /// <summary>
        /// 径向偏移的物理量程夹取。角度那一路有 <see cref="ClampDeviation"/>（按最大倾斜角软饱和），
        /// 径向没有对应的物理上限，于是一旦数值发散就会一路涨到 Infinity —— 这里补上边界，
        /// 并把继续朝外的速度清零（否则会贴在边界上继续加速，形成"钉住还在抖"）。
        /// </summary>
        private void ClampRadialOffset(float limit)
        {
            if (radialOffset > limit)
            {
                radialOffset = limit;
                if (radialVelocity > 0.0f)
                {
                    radialVelocity = 0.0f;
                }
            }
            else if (radialOffset < -limit)
            {
                radialOffset = -limit;
                if (radialVelocity < 0.0f)
                {
                    radialVelocity = 0.0f;
                }
            }
        }

        /// <summary>状态是否全部有限。NaN / Infinity 一旦进入状态就会自我复制，必须在源头拦。</summary>
        private bool HasFiniteState()
        {
            return IsFinite(angleX) && IsFinite(angleZ) &&
                   IsFinite(angularVelocityX) && IsFinite(angularVelocityZ) &&
                   IsFinite(radialOffset) && IsFinite(radialVelocity) &&
                   IsFinite(phase);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// 按摆动幅度夹取：只限制相对平衡面的偏离量，不限制平衡角本身。
        /// 这样瓶子放平或倒过来时，平衡角该到 90°/180° 就到，液面才能真正屈服于重力。
        /// </summary>
        private bool ClampDeviation(float equilibriumX, float equilibriumZ, float limit, float softness)
        {
            float deviationX = angleX - equilibriumX;
            float deviationZ = angleZ - equilibriumZ;
            float magnitude = Mathf.Sqrt(deviationX * deviationX + deviationZ * deviationZ);
            if (magnitude <= Epsilon)
            {
                return false;
            }

            float limited = SoftLimitMagnitude(magnitude, limit, softness);
            if (limited >= magnitude)
            {
                return false;
            }

            float scale = limited / magnitude;
            angleX = equilibriumX + deviationX * scale;
            angleZ = equilibriumZ + deviationZ * scale;

            float directionX = deviationX / magnitude;
            float directionZ = deviationZ / magnitude;
            float outward = angularVelocityX * directionX + angularVelocityZ * directionZ;
            if (outward > 0.0f)
            {
                angularVelocityX -= outward * directionX;
                angularVelocityZ -= outward * directionZ;
            }

            return true;
        }
    }
}
