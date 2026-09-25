using System;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数 Hub**：某个角色实例上、这些参数**当前**是多少的那片**纯存值区域**。
    ///
    /// 【它只干两件事】
    /// 存值、记名字。**它没有槽表、没有资产、没有配置** —— 槽是**写的人**在运行期开的：
    /// 谁写谁开（<see cref="ClaimSlot"/>），名字也是写的人填的（`names[i]`）。
    /// "这些名字是什么意思"那份给人看的表在 <see cref="HoFaceSemanticConnector"/> 上。
    ///
    /// 【谁写：中间层，算完顺手写】（2026-09-26 清理）
    ///   · **Unity 侧**：调试会话（`HoFaceAnimationSession`）每帧把**中间层算出来的输出行**
    ///     （配置文件里那些 `参数名 = 曲线(表达式(源键…))`）按名字写进来；
    ///   · **Warudo 侧**：「HoFace写动态参数」节点（`HoFaceHubWriteNode`）把**参数处理 / 合并字典**
    ///     那份字典按名字写进来。
    /// 两边写的是**同一份东西**（中间层的输出 = 那些 `参数名`），所以"面板里看到什么、Warudo 里就是什么"。
    ///
    /// ⚠️ **曾经不是这样**：槽一度是由控制器里的一个**状态机行为**（`HoFaceSemanticWriterBehaviour`）
    /// 按表达式从 Animator 参数算出来、再写进**影子上**那片 Hub，然后由会话/节点中转到角色上的。
    /// 那条路 2026-09-26 **整个删掉**了 —— 理由：中间层本来就能算出那些值（它就是写参数的人），
    /// 让控制器再算一遍 = 两份真相 + 一个只在 bundle 里跑、编辑器里看不见的写者。
    /// 于是"预留 128 个槽"这个问题也彻底不存在了（它当年只是为了让动画曲线能写 `values.<i>`）。
    ///
    /// 【为什么值还是 `float[]` 而不是一堆 `public float MouthX`】
    /// ① **运行期读不能反射**（UMod 禁 `System.Reflection`）：一堆字段名读不出来，下标可以；
    /// ② 参数是**每份配置自己定义的**、不是编译期固定的集合 ⇒ 字段名没法在编译期写出来；
    /// ③ 加一个参数 = 中间层多一行输出，**两边代码一行不动**。
    ///
    /// 【谁读】
    /// 消费方（作者的脚本 / 材质控制 / 蓝图）通过 <see cref="HoFaceSemanticConnector"/> 按名字读。
    ///
    /// 【值不属于任何资产】
    /// 值就是运行期状态，进程结束就没了；这个组件不会把值写到盘上，也没有"退出播放时存回去"这回事。
    /// </summary>
    [DisallowMultipleComponent]
    // ⚠️ AddComponentMenu 的写法与本包其余组件一致：根 `HoUnityTools`（**没有空格**）、
    // 分类与组件名**都用英文**、组件名带 `Ho ` 前缀。中文标题在这个菜单里很扎眼
    // （它跟 Animation / Layout / Mesh 那些英文分类并列），而且根名多两个空格会**多出一整个
    // 顶层分类** `Ho Unity Tools`，与 `HoUnityTools` 并存 —— 真发生过。
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Semantic Hub")]
    public sealed class HoFaceSemanticHub : MonoBehaviour
    {
        /// <summary>
        /// 当前值：**下标就是槽号**，含义由名字决定（见 <see cref="names"/>）。
        /// `public` 是有意的：它既是本组件的对外读面，也是**写的人直接写**的那个数组。
        /// </summary>
        [Tooltip("当前值（下标 = 槽号）。槽由写的人运行期开出来，这里不用手填。")]
        public float[] values = new float[0];

        /// <summary>
        /// 名字：与 <see cref="values"/> **一一对应**，由**写的人**在开槽时填。
        /// 空串 = 这个槽没有名字（正常路径不会出现：<see cref="ClaimSlot"/> 不收空名字；
        /// 只有在 Inspector 里手工改过数组长度时才会留下没名字的格子）。
        ///
        /// 它不是"配置"：它是**写的人当场声明的**（"我这个值叫 `jawOpen`"），
        /// 所以每个角色各自声明各自的，谁也不需要事先约定顺序。
        /// </summary>
        [Tooltip("名字（与 values 一一对应），由写的人运行期填。空 = 这一格没有名字。")]
        public string[] names = new string[0];

        /// <summary>槽位数（= 数组长度）。</summary>
        public int SlotCount { get { return values != null ? values.Length : 0; } }

        // ── 读 ──────────────────────────────────────────────────────────────────

        /// <summary>按下标取值；越界返回 0。</summary>
        public float GetFloat(int index)
        {
            if (values == null || index < 0 || index >= values.Length) return 0f;
            return values[index];
        }

        /// <summary>下标 → 名字；越界或没名字返回空串。</summary>
        public string NameAt(int index)
        {
            if (names == null || index < 0 || index >= names.Length) return "";
            string name = names[index];
            return name != null ? name : "";
        }

        /// <summary>名字 → 下标（线性找；没有返回 −1）。运行期偶尔用一次，别每帧调。</summary>
        public int IndexOfName(string name)
        {
            if (names == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < names.Length; i++)
                if (names[i] != null && string.Equals(names[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        // ── 写 ──────────────────────────────────────────────────────────────────

        /// <summary>按下标写值；越界**什么都不做**（调用方要自己检查 <see cref="SlotCount"/> 并报出来）。</summary>
        public void SetFloat(int index, float value)
        {
            if (values == null || index < 0 || index >= values.Length) return;
            values[index] = value;
        }

        /// <summary>
        /// **开一个槽**（或复用已有的同名槽）：有这个名字就返回它的下标，没有就追加一格。
        /// 这是"写的人声明自己有什么参数"的唯一入口 —— 所以**不需要任何人预先知道下标**。
        /// 名字为空返回 −1（没有名字的槽没法被对上下，宁可不写）。
        /// </summary>
        public int ClaimSlot(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            int existing = IndexOfName(name);
            if (existing >= 0) return existing;

            int index = SlotCount;
            Array.Resize(ref values, index + 1);
            Array.Resize(ref names, index + 1);
            names[index] = name;
            return index;
        }

        /// <summary>把所有槽清零（丢脸 / 进场 / 手动重置）。</summary>
        public void Zero()
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) values[i] = 0f;
        }

        /// <summary>给人看的一行状态（调试面板 / 日志用）。</summary>
        public string Summary()
        {
            int named = 0;
            if (names != null) for (int i = 0; i < names.Length; i++) if (!string.IsNullOrEmpty(names[i])) named++;
            return name + " · " + SlotCount + " 个槽（" + named + " 个有名字）";
        }
    }
}
