using System;

namespace Hollow.HoUnityTools.Editor.PhysBoneTuner
{
    /// <summary>
    /// 「内置预设库」文件的 schema（包内只读）。
    ///
    /// 一个文件装全部内置预设，而不是一个预设一个文件：
    ///   · 只读资源不需要用户可以逐个挑，逐个文件只是多几个 .meta；
    ///   · 用户自己的预设走"导入 json"对话框，本来就不需要在这里被发现。
    /// 字段名刻意跟 VRCPhysBone 的字段名对齐：json 是给人手改、给别人的，
    /// 一致性比 C# 命名规范重要。
    /// </summary>
    [Serializable]
    internal sealed class HoPhysBonePresetLibraryFile
    {
        public string schema = "hotools.physbone-presets";
        public int schemaVersion = 1;
        public HoPhysBonePreset[] presets = Array.Empty<HoPhysBonePreset>();
    }

    /// <summary>
    /// 单个预设。只描述本面板会写入的那几个参数 ——
    /// 其余 PhysBone 字段（碰撞、抓取、限制、动画…）一律不碰，
    /// 这样预设只做"把这几条调成某种手感"，不会顺手改掉用户的其他设置。
    /// </summary>
    [Serializable]
    internal sealed class HoPhysBonePreset
    {
        /// <summary>稳定标识，用于记住上次选择。</summary>
        public string id = string.Empty;

        /// <summary>下拉里显示的名字。</summary>
        public string name = string.Empty;

        /// <summary>一句话说明，显示在名字下面。</summary>
        public string description = string.Empty;

        // ---- 参数（对应 VRCPhysBone 的同名字段）----
        //
        // 标量 × 曲线 = 最终作用值（SDK 就是这么算的）：
        //     CalcPull(t) = clamp01(SafeEvaluate(pullCurve, t) * pull)
        //     SafeEvaluate(curve, t) => curve != null && curve.length > 0 ? curve.Evaluate(t) : 1f
        // t = Clamp01(骨骼在链里的下标 / 链长)，所以：
        //   · 标量 = 整体强度
        //   · 曲线 = 沿链的分布（"根硬稍软"就是一条从左到右递减的曲线）
        //   · 曲线为空 = 恒定 1，也就是"均匀"，而不是 0
        //
        // 所以预设是「标量 + 可选曲线」两件套，缺一个都表达不了真实手感。

        public float pull = 0.2f;
        public float spring = 0.2f;
        public float stiffness = 0.2f;
        public float gravity;
        public float radius = 0.02f;

        // ---- 曲线（可选）----
        //
        // 三种意图，必须区分开，因为"不动"和"清空"是相反的操作：
        //   · json 里没写这对字段     → 保持原样（不要碰用户已有的曲线）
        //   · "useXxxCurve": false    → 清空曲线（回到均匀 1.0）
        //   · "useXxxCurve": true     → 写入 xxxCurve
        //
        // 坑：JsonUtility 分不清"字段缺失"和"显式 false"，两者都读成 false。
        // 所以解析时先扫一遍 json 文本，看这个键到底出现过没有 ——
        // 出现过的才允许写（true 写曲线、false 清空），没出现的一律不动。
        // 结果记在下面的 HasXxxCurveField 上（非序列化，不参与 json）。

        public bool usePullCurve;
        public UnityEngine.AnimationCurve pullCurve;

        public bool useSpringCurve;
        public UnityEngine.AnimationCurve springCurve;

        public bool useStiffnessCurve;
        public UnityEngine.AnimationCurve stiffnessCurve;

        public bool useGravityCurve;
        public UnityEngine.AnimationCurve gravityCurve;

        public bool useRadiusCurve;
        public UnityEngine.AnimationCurve radiusCurve;

        /// <summary>json 里出现过 usePullCurve / pullCurve 之一 —— 才允许动曲线。</summary>
        [NonSerialized] public bool HasPullCurveField;
        [NonSerialized] public bool HasSpringCurveField;
        [NonSerialized] public bool HasStiffnessCurveField;
        [NonSerialized] public bool HasGravityCurveField;
        [NonSerialized] public bool HasRadiusCurveField;

        /// <summary>
        /// 该曲线在 json 里出现过吗。出现且 useCurve=false → 要清空；
        /// 出现且 useCurve=true 且有键 → 要写入；没出现 → 不动。
        /// </summary>
        internal bool WantsCurveClear(bool hasField, bool useCurve, UnityEngine.AnimationCurve curve)
        {
            return hasField && (!useCurve || curve == null || curve.length == 0);
        }

        internal bool WantsCurveWrite(bool hasField, bool useCurve, UnityEngine.AnimationCurve curve)
        {
            return hasField && useCurve && curve != null && curve.length > 0;
        }

        // ---- 自适应开关 ----
        //
        // 开启后该参数不再用上面的绝对值，而是按选中的骨骼现场推导：
        //   · gravityScaleWithVerticality：重力随"这条链有多朝下"插值，
        //     水平链几乎不吃重力。范围就是 gravity 与 gravityVertical 之间。
        //   · radiusFromBoneSize：半径 = 相邻骨长 × radiusBoneRatio。
        //
        // 这样一条朝后翘的尾巴不会被重力拽着往下沉 —— 这是预设里最值钱的部分。

        public bool gravityScaleWithVerticality;

        /// <summary>链越垂直，重力越接近这个值。</summary>
        public float gravityVertical = 0.3f;

        public bool radiusFromBoneSize;

        /// <summary>半径 = 平均骨长 × 这个比例。0.25 是"轻微有体积感"的经验值。</summary>
        public float radiusBoneRatio = 0.25f;

        /// <summary>骨长取不到时用的半径。</summary>
        public float radiusFallback = 0.02f;

        public float radiusMin = 0.005f;
        public float radiusMax = 0.2f;

        /// <summary>
        /// 复制一份，供"把预设当草稿"用。
        ///
        /// 必须复制而不是直接引用：草稿会被滑杆改，而内置预设是只读的；
        /// 共用引用就等于在改只读库，语义上说不通，也让"还原"失去参照
        /// （还原要拿库里那条原样值，结果它已经被改过了）。
        /// AnimationCurve 是引用类型，也一并复制键，免得两边共享同一条曲线。
        /// </summary>
        internal HoPhysBonePreset Clone()
        {
            var copy = (HoPhysBonePreset)MemberwiseClone();
            copy.pullCurve = CopyCurve(pullCurve);
            copy.springCurve = CopyCurve(springCurve);
            copy.stiffnessCurve = CopyCurve(stiffnessCurve);
            copy.gravityCurve = CopyCurve(gravityCurve);
            copy.radiusCurve = CopyCurve(radiusCurve);
            return copy;
        }

        private static UnityEngine.AnimationCurve CopyCurve(UnityEngine.AnimationCurve curve)
        {
            if (curve == null)
                return null;
            try
            {
                return new UnityEngine.AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode,
                };
            }
            catch (Exception)
            {
                // 曲线复制失败不该让载入预设整个失败：退化成不复制曲线。
                return null;
            }
        }

        /// <summary>
        /// 从任意 json 文本解析一个预设。两种写法都接受：
        ///   { "schema": "...", "preset": { ... } }   ← 包封
        ///   { "id": "...", "pull": 0.2, ... }        ← 裸预设
        /// 解析不出来返回 null。
        /// </summary>
        internal static HoPhysBonePreset Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            HoPhysBonePreset result;
            try
            {
                var file = new HoPhysBonePresetFile();
                UnityEngine.JsonUtility.FromJsonOverwrite(json, file);
                if (file.preset != null)
                {
                    result = file.preset;
                }
                else
                {
                    // 没有 preset 键：当成裸预设再解析一次。
                    result = new HoPhysBonePreset();
                    UnityEngine.JsonUtility.FromJsonOverwrite(json, result);
                }
            }
            catch (Exception)
            {
                return null;
            }

            if (result == null)
                return null;

            // 键是否出现过：JsonUtility 读不出这个信息，只能扫文本。
            result.HasPullCurveField = HasKey(json, "usePullCurve") || HasKey(json, "pullCurve");
            result.HasSpringCurveField = HasKey(json, "useSpringCurve") || HasKey(json, "springCurve");
            result.HasStiffnessCurveField = HasKey(json, "useStiffnessCurve") || HasKey(json, "stiffnessCurve");
            result.HasGravityCurveField = HasKey(json, "useGravityCurve") || HasKey(json, "gravityCurve");
            result.HasRadiusCurveField = HasKey(json, "useRadiusCurve") || HasKey(json, "radiusCurve");

            result.Clamp();
            return result.IsUsable ? result : null;
        }

        /// <summary>
        /// json 文本里有没有这个键。刻意只做"出现过吗"这一个判断 ——
        /// 目的是区分"字段缺失"和"显式 false"，两者在 JsonUtility 里同值。
        /// </summary>
        private static bool HasKey(string json, string key)
        {
            return json.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0;
        }

        /// <summary>写回 json（用于"保存为"）。</summary>
        internal string ToJson()
        {
            var file = new HoPhysBonePresetFile
            {
                schema = "hotools.physbone-preset",
                schemaVersion = 1,
                preset = this,
            };
            return UnityEngine.JsonUtility.ToJson(file, true);
        }

        /// <summary>把 json 里的异常数值夹回合法范围，免得坏预设把参数写成天文数字。</summary>
        internal void Clamp()
        {
            pull = UnityEngine.Mathf.Clamp01(pull);
            spring = UnityEngine.Mathf.Clamp01(spring);
            stiffness = UnityEngine.Mathf.Clamp01(stiffness);
            // gravity 允许为负：负重力 = 往上飘，做飘带/裙摆时要用。
            gravity = UnityEngine.Mathf.Clamp(gravity, -1.0f, 1.0f);
            gravityVertical = UnityEngine.Mathf.Clamp(gravityVertical, -1.0f, 1.0f);
            radius = UnityEngine.Mathf.Max(0.0f, radius);
            radiusBoneRatio = UnityEngine.Mathf.Max(0.0f, radiusBoneRatio);
            radiusFallback = UnityEngine.Mathf.Max(0.0f, radiusFallback);
            if (radiusMin < 0.0f) radiusMin = 0.0f;
            if (radiusMax < radiusMin) radiusMax = radiusMin;
        }

        internal bool IsUsable
        {
            get { return !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name); }
        }
    }

    /// <summary>
    /// 用户从磁盘导入的单个预设文件。
    ///
    /// 只留一个 <see cref="preset"/> 字段：json 里没有 "preset" 键时它保持 null，
    /// 由此判断"这是裸预设还是包封过的"，不需要第二个同类型字段来二选一。
    /// </summary>
    [Serializable]
    internal sealed class HoPhysBonePresetFile
    {
        public string schema = string.Empty;
        public int schemaVersion;
        public HoPhysBonePreset preset;
    }
}
