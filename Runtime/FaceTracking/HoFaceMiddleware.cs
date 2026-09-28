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
        [Tooltip("**进入维持**（秒）：要在 trigger 之上**连续**待够这么久才真的跳过去。0 = 不看时间（老行为）。")]
        public float enterSeconds;
        [Tooltip("**退出维持**（秒）：要掉到释放点之下**连续**待够这么久才真的退回去（一般取 enterSeconds 的一半）。0 = 不看时间。")]
        public float exitSeconds;
    }

    /// <summary>修饰符的种类。顺序由它在列表里的位置决定（**按列出顺序生效**）。</summary>
    public enum HoFaceModifierKind
    {
        [InspectorName("平滑")] Smooth,
        [InspectorName("延迟")] Delay,
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

        /// <summary>
        /// **这一行的默认值**（照 VBridger 每行都带的 `defaultValue` 抄的形状，2026-09-26 补）。
        /// 一句话：**"没有任何东西驱动它"时，这一行的值是多少。** 默认 `0`（= 中性）。
        ///
        /// 【输入行】= 这个**规范名**在那条线名**一帧都没来过**时的值（会话/链路把这一行的初值摆成它）。
        /// 于是"没数据"不再等于"隐式 0"，而是**作者声明过的值** —— 这正是"全量默认值"要的东西：
        /// 规则层对每一个它认得的值都能给出答案，不用再回到"什么都不写、看控制器默认"。
        ///
        /// 【输出行】= `expression` **留空**时这一行写出去的值 —— 也就是**常量行**：
        /// `parameter = Ho/Drive/Gate/Lip` + `defaultValue = 1` + 空表达式 = 中间层自己产出的门控
        /// （"不需要输入、总有一个默认值"的那种东西）。表达式**写了但解析不了**时也退回它
        /// （比退回 0 诚实：那是作者声明的默认，不是魔法 0）。
        ///
        /// ⚠️ **它不是"钳制范围"**：范围由那一行的 `curve` 负责（超出按端点算）。
        /// 也不参与"缺键 = 保持上一帧"那条规矩 —— 那条说的是**已经有数据之后**漏帧的情况。
        /// </summary>
        public float defaultValue;

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

    /// <summary>
    /// **输出行之间的"只能引用上面"规则**（2026-09-27 用户定）。
    ///
    /// 【为什么需要它】风格化特殊形态（鼓嘴 / 倒V / 苦嘴）要**关掉整块"张嘴 × 笑"的混合树**，
    /// 于是中间层得有"由**别的输出行**算出来的门"。而中间层原来是**纯前馈**的（每行只读输入通道），
    /// 算不了这种东西。
    ///
    /// 【规则】刻意做得简单（用户定的形状）：
    /// <list type="number">
    /// <item>**按行序求值** —— 就是 profile 里 <c>outputs</c> 的顺序，不排序、不递归；
    ///   所有行读写的都是同一张**输出表缓存**（<see cref="HoFaceOutputTable"/>）；</item>
    /// <item>用 <c>out("参数名")</c> 读**上面最近写过这个名字的那一行**的值（过完曲线与修饰符的那一份）。
    ///   用**函数**而不是裸名字有两个理由：参数名里带 `/`（裸标识符写不出来），
    ///   以及**防止与输入通道重名**（输出行叫 `Brows` 时能明确说"我要输出那一份"）；</item>
    /// <item>**同名多行是合法且有意的**（2026-09-27 用户定）：行按顺序**覆盖**表里的那一格，
    ///   发布出去的是**最后写的那一行** ⇒ 于是"每条形态再压一条同名行、读自己、写自己"就能
    ///   **无限拓展**（加形态不用动任何已有行）。⚠️ 所以这里判的是"**上面有没有**这个名字"，
    ///   不是"这个名字唯一不唯一"；</item>
    /// <item>**上面没有这个名字（或引用了下面才出现的行）⇒ 这一行无效**：面板上爆红，
    ///   且**始终输出 `defaultValue`**（不参与求值）。</item>
    /// </list>
    ///
    /// 【为什么不用拓扑排序】只在"上面"找 ⇒ 依赖图**按构造就是 DAG**：不可能有环，
    /// 也不需要"谁先算"的推导 —— 行序就是顺序。
    ///
    /// ⚠️ **输入行不许用 `out(...)`**（那边求值在输出行之前，没有"上面"可言），见 <see cref="InputRowError"/>。
    /// </summary>
    public static class HoFaceOutputOrder
    {
        /// <summary>
        /// 逐行检查。返回数组与 <paramref name="rows"/> 等长：`null` = 没问题；
        /// 非 null = 给人看的说明（面板据此爆红，会话据此退回 `defaultValue`）。
        /// **表达式本身解析不了的不在这里报**（那是另一类错，别处已经报过）。
        /// </summary>
        public static string[] Validate(IList<HoFaceOutput> rows)
        {
            if (rows == null) return new string[0];
            var errors = new string[rows.Count];

            // 参数名 → 行号。⚠️ 同名多行是**合法且有意的**（输出表缓存：后写覆盖先写），
            // 所以这里必须**按行序边走边覆盖**：本行查到的永远是「上面最近写过这个名字的那一行」，
            // 与 HoFaceOutputTable 的语义**逐字一致**（那边是"这个名字现在有没有值"）。
            var above = new Dictionary<string, int>(StringComparer.Ordinal);
            // 只为报错措辞：这个名字在**整份**表里出现过吗（出现过却不在上面 ⇒ 它只出现在下面）。
            var anywhere = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                anywhere[row.parameter] = i;
            }

            var refs = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;

                if (!string.IsNullOrWhiteSpace(row.expression))
                {
                    HoFaceExpression parsed;
                    string why;
                    if (HoFaceExpression.TryParse(row.expression, out parsed, out why))
                    {
                        refs.Clear();
                        parsed.CollectOutputRefs(refs);
                        for (int k = 0; k < refs.Count; k++)
                        {
                            // `above` 里只有**已经走过的行** ⇒ 命中就一定在本行上面（不需要再比行号）
                            if (!above.ContainsKey(refs[k]))
                            {
                                errors[i] = anywhere.ContainsKey(refs[k])
                                    ? "out(\"" + refs[k] + "\") 上面还没有写过这个名字（只有下面的第 "
                                        + (anywhere[refs[k]] + 1) + " 行写）"
                                        + " —— 只能引用上面已经算完的行；本行始终输出默认值"
                                    : "out(\"" + refs[k] + "\") 引用了不存在的输出行";
                                break;
                            }
                        }
                    }
                }

                // 走完这一行才把它写进"上面"：于是 `out("自己的名字")` 读到的是**上一条同名行**
                // （双重形态的链就是这么长出来的），而**第一条**同名行读自己是"上面还没有" ⇒ 爆红。
                if (!string.IsNullOrEmpty(row.parameter)) above[row.parameter] = i;
            }
            return errors;
        }

        /// <summary>
        /// `out(...)` 只允许出现在输出行里。输入行用了就返回一条说明（否则 null）——
        /// 会话会把这种输入行当无效行（表达式丢掉 ⇒ 不新鲜 ⇒ 通道按断流规则回中性）。
        /// </summary>
        public static string InputRowError(HoFaceOutput row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.expression)) return null;
            HoFaceExpression parsed;
            string why;
            if (!HoFaceExpression.TryParse(row.expression, out parsed, out why)) return null;
            var refs = new List<string>();
            parsed.CollectOutputRefs(refs);
            if (refs.Count == 0) return null;
            return "输入行里不能用 out(\"" + refs[0] + "\")：输入行在输出行之前求值，没有\"上面\"可引用";
        }
    }

}
