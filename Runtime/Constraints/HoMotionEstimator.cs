using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 运动估计器：对最近若干帧的锚点位置做二次最小二乘拟合，一次得到速度与加速度。
    /// <para>
    /// 为什么不用逐帧二阶差分：<c>a = Δ²p / dt²</c> 会把位置抖动放大 1/dt² 倍。
    /// 60fps 下 1 毫米的位置抖动就是约 3.6 m/s² 的加速度噪声，直接表现成液面抽搐；
    /// 而编辑器拖拽、动画采样、父级物理都会带来这种抖动。
    /// </para>
    /// <para>
    /// 二次拟合对「匀速」是精确的（二次项系数为 0），对「恒加速」也是精确的，
    /// 同时对白噪声的增益远低于二阶差分。代价是窗口内加速度变化时会有约
    /// (窗口-1)/2 帧的等效滞后，比串联一阶低通小得多。
    /// 使用真实时间戳，因此编辑器里帧间隔不均匀也成立。
    /// </para>
    /// </summary>
    public struct HoMotionEstimator
    {
        public const int MinWindow = 3;
        public const int MaxWindow = 12;
        public const int DefaultWindow = 6;

        private Vector3[] positions;
        private double[] times;
        private int capacity;
        private int count;
        private int head;
        private Vector3 lastVelocity;
        private Vector3 lastAcceleration;
        private bool hasEstimate;

        /// <summary>当前窗口长度（帧）。</summary>
        public int Window => capacity;

        /// <summary>是否已经产生过估计值。</summary>
        public bool HasEstimate => hasEstimate;

        /// <summary>最近一次估计的速度（米/秒）。未初始化时为 0。</summary>
        public Vector3 Velocity => lastVelocity;

        /// <summary>最近一次估计的加速度（米/秒²）。未初始化时为 0。</summary>
        public Vector3 Acceleration => lastAcceleration;

        /// <summary>采样点数。</summary>
        public int SampleCount => count;

        public void EnsureCapacity(int window)
        {
            int clamped = Mathf.Clamp(window, MinWindow, MaxWindow);
            if (positions != null && capacity == clamped)
            {
                return;
            }

            capacity = clamped;
            positions = new Vector3[capacity];
            times = new double[capacity];
            count = 0;
            head = 0;
            hasEstimate = false;
            lastVelocity = Vector3.zero;
            lastAcceleration = Vector3.zero;
        }

        public void Reset()
        {
            count = 0;
            head = 0;
            hasEstimate = false;
            lastVelocity = Vector3.zero;
            lastAcceleration = Vector3.zero;
        }

        /// <summary>压入一个位置采样。</summary>
        public void Push(Vector3 position, double time)
        {
            if (positions == null || positions.Length == 0)
            {
                EnsureCapacity(DefaultWindow);
            }

            positions[head] = position;
            times[head] = time;
            head = (head + 1) % capacity;
            if (count < capacity)
            {
                count++;
            }
        }

        /// <summary>重新估计速度与加速度，结果通过 <see cref="Velocity"/> 与 <see cref="Acceleration"/> 读取。</summary>
        public void Estimate()
        {
            if (count >= 3)
            {
                if (TryFitQuadratic(out Vector3 velocity, out Vector3 acceleration))
                {
                    lastVelocity = velocity;
                    lastAcceleration = acceleration;
                    hasEstimate = true;
                    return;
                }
            }

            FallbackDifference();
        }

        /// <summary>样本不足或时间戳退化时的退路：两点差分。</summary>
        private void FallbackDifference()
        {
            if (count >= 2)
            {
                int newest = (head - 1 + capacity) % capacity;
                int previous = (head - 2 + capacity) % capacity;
                double delta = times[newest] - times[previous];
                if (delta > 1.0e-7)
                {
                    Vector3 velocity = (positions[newest] - positions[previous]) / (float)delta;
                    Vector3 acceleration = hasEstimate ? (velocity - lastVelocity) / (float)delta : Vector3.zero;
                    lastVelocity = velocity;
                    lastAcceleration = acceleration;
                    hasEstimate = true;
                    return;
                }
            }

            lastVelocity = Vector3.zero;
            lastAcceleration = Vector3.zero;
            hasEstimate = false;
        }

        private bool TryFitQuadratic(out Vector3 velocity, out Vector3 acceleration)
        {
            velocity = Vector3.zero;
            acceleration = Vector3.zero;

            int newestIndex = (head - 1 + capacity) % capacity;
            double newestTime = times[newestIndex];
            double oldestTime = newestTime;
            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity * 2) % capacity;
                oldestTime = System.Math.Min(oldestTime, times[index]);
            }

            double span = newestTime - oldestTime;
            double interval = span / (count - 1);
            if (interval <= 1.0e-7)
            {
                return false;
            }

            // 时间归一化到「以最新样本为 0、平均间隔为单位」，改善正规方程的条件数。
            double s0 = 0.0;
            double s1 = 0.0;
            double s2 = 0.0;
            double s3 = 0.0;
            double s4 = 0.0;
            double bx0 = 0.0;
            double bx1 = 0.0;
            double bx2 = 0.0;
            double by0 = 0.0;
            double by1 = 0.0;
            double by2 = 0.0;
            double bz0 = 0.0;
            double bz1 = 0.0;
            double bz2 = 0.0;

            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity * 2) % capacity;
                double t = (times[index] - newestTime) / interval;
                double t2 = t * t;
                Vector3 p = positions[index];

                s0 += 1.0;
                s1 += t;
                s2 += t2;
                s3 += t2 * t;
                s4 += t2 * t2;

                bx0 += p.x;
                bx1 += p.x * t;
                bx2 += p.x * t2;
                by0 += p.y;
                by1 += p.y * t;
                by2 += p.y * t2;
                bz0 += p.z;
                bz1 += p.z * t;
                bz2 += p.z * t2;
            }

            // 对称 3x3 正规方程 [[s0,s1,s2],[s1,s2,s3],[s2,s3,s4]] 的伴随矩阵与行列式。
            double c00 = (s2 * s4) - (s3 * s3);
            double c01 = (s2 * s3) - (s1 * s4);
            double c02 = (s1 * s3) - (s2 * s2);
            double c11 = (s0 * s4) - (s2 * s2);
            double c12 = (s1 * s2) - (s0 * s3);
            double c22 = (s0 * s2) - (s1 * s1);

            double determinant = (s0 * c00) + (s1 * c01) + (s2 * c02);
            if (System.Math.Abs(determinant) < 1.0e-12)
            {
                return false;
            }

            double inverseInterval = 1.0 / interval;
            velocity = new Vector3(
                (float)(((c01 * bx0) + (c11 * bx1) + (c12 * bx2)) / determinant * inverseInterval),
                (float)(((c01 * by0) + (c11 * by1) + (c12 * by2)) / determinant * inverseInterval),
                (float)(((c01 * bz0) + (c11 * bz1) + (c12 * bz2)) / determinant * inverseInterval));

            double accelerationScale = 2.0 * inverseInterval * inverseInterval;
            acceleration = new Vector3(
                (float)(((c02 * bx0) + (c12 * bx1) + (c22 * bx2)) / determinant * accelerationScale),
                (float)(((c02 * by0) + (c12 * by1) + (c22 * by2)) / determinant * accelerationScale),
                (float)(((c02 * bz0) + (c12 * bz1) + (c22 * bz2)) / determinant * accelerationScale));

            return true;
        }
    }
}
