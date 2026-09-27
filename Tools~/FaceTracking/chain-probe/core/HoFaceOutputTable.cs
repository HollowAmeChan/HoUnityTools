// ============================================================================
// PORTED FILE - do not edit here.
// Master: HoUnityTools/Runtime/FaceTracking/<same file name>
// Re-sync: see Core/PORTED.md (script: Tests~/SyncFaceModCore.ps1)
// Only the namespace differs; the code is otherwise byte-identical.
// ============================================================================

using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **输出表缓存**（2026-09-27 用户定）：一张 `参数名 → 这一帧已经写进去的值` 的表，
    /// **所有输出行都读它、写它**，整份配置走完之后才统一发布（影子 Animator / 参数 Hub）。
    ///
    /// 它就是 <c>out("…")</c> 的语义所在，也是"**双重形态**"能无限扩展的原因：
    /// <list type="bullet">
    /// <item><b>读</b>：拿到的永远是"**上面最近写过这个名字的那一行**"的值。
    ///   还没人写过 ⇒ <see cref="TryRead"/> 为 false —— 那一行按"引用了不存在的行"处理
    ///   （面板爆红 + 恒输出 <see cref="HoFaceOutput.defaultValue"/>）。</item>
    /// <item><b>写</b>：行按顺序写进来，**后写覆盖先写** ⇒ 同一个名字可以有很多行，
    ///   发布出去的是**最后写的那一行**。</item>
    /// <item>⇒ "关其他"那种门只要**每条形态再压一条同名行**：新行读
    ///   `out("同一个名字")`（= 上一条同名行的结果）再写同一个名字，
    ///   **加形态不用动任何已有行**，"无限拓展"就是这件事。</item>
    /// </list>
    ///
    /// ⚠️ **修饰符照旧安全**：表的键是**名字**，而修饰符状态是**每一行自己的**
    /// （`平滑` / `维持` / `延迟` 各一套数组，按行号索引）。行读到的是那一行
    /// **过完曲线与修饰符**之后的值，跟自己会不会被后面的同名行覆盖无关 ⇒ 没有反馈环；
    /// 而且读只能朝上（表里只有已经写过的行），依赖图**按构造就是 DAG**，
    /// 不需要环检测，也不需要拓扑排序。
    ///
    /// ⚠️ **它不负责校验**：行序对不对由 <see cref="HoFaceOutputOrder.Validate"/> 在编译期判
    /// （在这里只表现为"这个名字还没人写过"）。
    /// </summary>
    public sealed class HoFaceOutputTable
    {
        private readonly Dictionary<string, float> values = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// 这一帧的表。每帧开头清一次 —— **不是**"缺值就保持上一帧"：
        /// 输出行每帧都会重写自己那一格（缺的是"这一帧还没轮到"，不是"没数据"）。
        /// </summary>
        public void Clear() => values.Clear();

        /// <summary>写 / 覆盖：行按顺序调用它。名字为空的行不进表（没有名字就没有可引用的键）。</summary>
        public void Write(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            values[name] = value;
        }

        /// <summary>这个名字现在有没有值（= 上面有没有行写过它），有就顺带取出来。</summary>
        public bool TryRead(string name, out float value)
        {
            if (string.IsNullOrEmpty(name)) { value = 0f; return false; }
            return values.TryGetValue(name, out value);
        }

        /// <summary>读不到时给 0（表达式求值器不抛异常 —— 跟未知通道同一条规矩）。</summary>
        public float Read(string name) => TryRead(name, out float value) ? value : 0f;

        /// <summary>表里有几个名字（诊断用）。</summary>
        public int Count => values.Count;

        /// <summary>表里有没有这个名字（诊断用）。</summary>
        public bool Contains(string name) => !string.IsNullOrEmpty(name) && values.ContainsKey(name);

        /// <summary>
        /// 发布用：表里现在所有的 `(名字, 值)`。同名的只出现一次 —— 就是**最后写的那一份**。
        /// 发布一律走这里，于是"发出去的值"与"别的行读到的值"永远是同一个（不会有半成品）。
        /// </summary>
        public IEnumerable<KeyValuePair<string, float>> Entries => values;
    }
}
