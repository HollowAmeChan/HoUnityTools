using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>曲线求值的小工具：**把 x 夹到曲线的关键点范围内**再求值。</summary>
    /// <remarks>
    /// 为什么要夹：Unity 的 <see cref="AnimationCurve.Evaluate"/> 在关键点范围外是**线性外推**，
    /// 于是"输入刚好超了一点"会把输出推到作者完全没摆过的值上。作者拖曲线时的心智模型是
    /// "这一段之外就按端点算"（VBridger 的输入曲线也是这样用的），所以统一夹住。
    /// </remarks>
    public static class HoFaceCurve
    {
        public static float Transfer(AnimationCurve curve, float x)
        {
            if (curve == null || curve.length == 0) return x;
            var keys = curve.keys;
            float low = keys[0].time, high = keys[keys.Length - 1].time;
            if (high < low) { float swap = low; low = high; high = swap; }
            float clamped = x < low ? low : x > high ? high : x;
            float value = curve.Evaluate(clamped);
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        /// <summary>恒等曲线（0..1 → 0..1）。</summary>
        public static AnimationCurve Identity() => AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>恒等曲线（−1..1 → −1..1，双向轴用）。</summary>
        public static AnimationCurve SignedIdentity() => AnimationCurve.Linear(-1f, -1f, 1f, 1f);
    }

    /// <summary>一步（翻页动画用）：过 <see cref="trigger"/> 跳到 <see cref="target"/>，掉回 <see cref="threshold"/> 之下再退回去。</summary>
    [Serializable]
    public sealed class HoFaceStep
    {
        [Tooltip("参数到这个值时触发。")]
        public float trigger = 0.3f;
        [Tooltip("触发后输出变成它。")]
        public float target = 0.4f;
        [Tooltip("触发后的最短保持时间（秒）。")]
        public float hold;
        [Tooltip("要从触发点往下掉这么多才退回去（迟滞，防止在阈值上抖）。")]
        public float threshold = 0.1f;
    }

    /// <summary>修饰符的种类。顺序由它在列表里的位置决定（**按列出顺序生效**）。</summary>
    public enum HoFaceModifierKind
    {
        [InspectorName("平滑")] Smooth,
        [InspectorName("延迟（未实现）")] Delay,
        [InspectorName("维持")] Steps
    }

    /// <summary>
    /// 一个**修饰符**：作用在这一行的曲线上，按顺序串起来。这是从 VBridger 抄的形状 ——
    /// 它的输出面板就是"每行一个有序修饰符列表"（Delay / Smoothing / Steps），
    /// 而不是我们原来那种"按分组给一个时间常数"。手感是**每一路自己的**。
    /// </summary>
    [Serializable]
    public sealed class HoFaceModifier
    {
        public HoFaceModifierKind kind = HoFaceModifierKind.Smooth;
        [Tooltip("平滑 / 延迟的时长（秒）。")]
        public float seconds;
        [Tooltip("维持：按 trigger 从小到大排列。")]
        public List<HoFaceStep> steps = new List<HoFaceStep>();

        /// <summary>这一步用不用得上（参数填了才算）。</summary>
        public bool Active
        {
            get
            {
                switch (kind)
                {
                    case HoFaceModifierKind.Smooth: return seconds > 0.0001f;
                    case HoFaceModifierKind.Delay: return seconds > 0.0001f;
                    default: return steps != null && steps.Count > 0;
                }
            }
        }
    }

    /// <summary>
    /// **中间层的一行输出**：<c>参数名 = 曲线(表达式(源键…))</c>，再经过一串有序修饰符。
    ///
    /// 形状直接照 VBridger 的输出行：一个参数名、一条方程式、一条曲线、一串修饰符。
    /// 我们原来那套"加权项 + offset + signed"被**表达式**取代了 —— 加权和只是它的一个特例，
    /// 而 `min`/`max`/`clamp`/`if`/除法这些"混合树做不到、必须留在外面"的东西，正好是表达式的主场。
    /// </summary>
    [Serializable]
    public sealed class HoFaceOutput
    {
        [Tooltip("写进控制器的参数名。控制器里没有这个名字时这一行会被跳过（不猜也不补）。")]
        public string parameter = "ARKit/jawOpen";
        [Tooltip("表达式：变量就是源形态键名，例如 `jawOpen`、`eyeBlinkLeft - eyeWideLeft`、`clamp(1-blink,0,1)`。\n"
            + "语法照 VBridger（含 if('条件', 真, 假) 与 clamp/lerp/min/max 等函数）。")]
        public string expression = "jawOpen";
        [Tooltip("响应曲线：横轴是表达式的值，纵轴是写出去的值。范围之外按端点算（不外推）。\n"
            + "死区 = 开头压平；增益 = 斜率；软饱和 = 尾部压平。")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("按列出顺序生效的修饰符（平滑 / 维持 / 延迟）。")]
        public List<HoFaceModifier> modifiers = new List<HoFaceModifier>();
        [Tooltip("备注：面板显示用，不参与求值（比如标注这一行属于哪个协议/哪台设备）。")]
        public string notes;

        /// <summary>表达式 → 曲线（修饰符不在这里：它们是逐帧状态，由会话持有）。</summary>
        public float Transform(float value) => HoFaceCurve.Transfer(curve, value);
    }

    /// <summary>
    /// **一套中间层**：输入行 + 输出行 + 说明。这是个纯数据对象；它的持久化形式是我们自己的
    /// JSON 配置文件（见 <see cref="HoFaceProfile"/>），窗口负责编辑它。
    ///
    /// 两类行**同一套形状**（`名字 = 曲线(表达式(变量…)) + 有序修饰符`），只是写出的名字含义不同：
    /// · **输入行** <see cref="inputs"/>：左值是**规范名**（如 `jawOpen`、`headRotX`），
    ///   右值的变量是**手机发来的线名**（iFacialMocap 的 `eyeBlink_L` / VTS 的 `EyeBlinkLeft`）。
    ///   改名、量纲、姿态分量、左右合并全在这一层 —— 接收端只交原样，不做任何隐式处理。
    /// · **输出行** <see cref="outputs"/>：左值是**控制器参数名**，右值的变量是规范名（或线名）。
    ///
    /// 求值顺序：输入行 → 逐通道整形（模式/输入曲线/中性）→ 输出行。见 docs/FACE_TRACKING_MIDDLE_LAYER.md。
    /// </summary>
    [Serializable]
    public sealed class HoFaceMiddleware
    {
        /// <summary>
        /// 配置的显示名。**默认是空串，不是任何一份具体表的名字** —— 这里以前写死 `"ho-2d"`
        /// （那份内置默认表的名字，已删）；留一个默认名等于让"没填名字"看起来像"填了某份表"。
        /// 名字该来自文件本身（`HoFaceProfileWindow` 新建时用文件名，读盘时用 JSON 里的 `displayName`）。
        /// </summary>
        public string displayName = "";
        public string notes;
        /// <summary>线名 → 规范名（+ 量纲）。引用到的线名这帧没来时，这一行**保持上一帧**（照 VBridger 的语义）。</summary>
        public List<HoFaceOutput> inputs = new List<HoFaceOutput>();
        public List<HoFaceOutput> outputs = new List<HoFaceOutput>();

        /// <summary>这套配置会读到的所有变量名（输入行与输出行都算；去重、按首次出现顺序）。</summary>
        public void UsedShapes(List<string> into)
        {
            if (into == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Collect(inputs, seen, into);
            Collect(outputs, seen, into);
        }

        private static void Collect(List<HoFaceOutput> rows, HashSet<string> seen, List<string> into)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.expression)) continue;
                if (!HoFaceExpression.TryParse(row.expression, out var parsed, out _)) continue;
                var names = new List<string>();
                parsed.CollectVariables(names);
                foreach (string name in names) if (seen.Add(name)) into.Add(name);
            }
        }

        /// <summary>这套输出会写出去的所有参数名（去重）。</summary>
        public void UsedParameters(List<string> into)
        {
            if (into == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var output in outputs)
                if (output != null && !string.IsNullOrEmpty(output.parameter) && seen.Add(output.parameter))
                    into.Add(output.parameter);
        }
    }

}
