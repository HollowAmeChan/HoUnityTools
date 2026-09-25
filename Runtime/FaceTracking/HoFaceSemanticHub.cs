using System;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数 Hub**：某个角色实例上、这些语义参数**当前**是多少的那片**纯存值区域**。
    ///
    /// 【它只干两件事】
    /// 存值、记名字。**它没有槽表、没有资产、没有配置** —— 槽是**写的人**在运行期开的：
    /// 谁写谁开（<see cref="ClaimSlot"/>），名字也是写的人填的（`names[i]`）。
    /// "这些名字是什么意思"那份给人看的表在 <see cref="HoFaceSemanticConnector"/> 上。
    ///
    /// 【为什么是"运行期开槽"而不是"事先预留固定数量"】
    /// 以前预留 128 个槽，是因为**控制器曲线**要写 `values.<i>`：曲线绑定是静态字符串，
    /// 越界写**不报错、值就是不动**，所以长度必须在打包前就猜对，而且作者得先知道下标。
    /// 现在改成 **`HoFaceSemanticWriterBehaviour`（状态机行为）用代码写**（见那份文件的说明）：
    /// 它是普通 C#，可以自己开槽、自己记名字 ⇒ **没有魔法数字、也不用任何人预先知道下标**。
    /// 于是"128 到底合不合适"这个问题**不存在了**：槽数就是控制器实际声明过的名字数。
    ///
    /// 【为什么值还是 `float[]` 而不是一堆 `public float MouthX`】
    /// ① **运行期读不能反射**（UMod 禁 `System.Reflection`）：一堆字段名读不出来，下标可以；
    /// ② 语义表是**每个角色自己定义的**、不是编译期固定的集合 ⇒ 字段名没法在编译期写出来；
    /// ③ 加一个语义 = 控制器多声明一个名字，**两边代码一行不动**。
    ///
    /// 【谁写、谁读】
    ///   · **写**：控制器里的 <see cref="HoFaceSemanticWriterBehaviour"/>（bundle 里跑，按名字开槽写值）、
    ///     Unity 侧调试会话（`SetFloat(i, v)`）、Warudo 侧的「HoFace写动态参数」节点（按下标写）。
    ///   · **读**：Warudo 侧按**下标 + 名字**采出来（见 `HoFaceController`）；
    ///     消费方（作者的脚本 / 材质控制 / 蓝图）通过 <see cref="HoFaceSemanticConnector"/> 按名字读。
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
        /// 空串 = 这个槽没有名字（老式纯按下标写的路径会留这种槽）。
        ///
        /// 它不是"配置"：它是**写的人当场声明的**（"我这个值叫 MouthX"），
        /// 所以两边（bundle 里的代理 Hub 与角色上的 Hub）各自声明各自的，谁也不需要事先约定顺序。
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
        /// 这是"写的人声明自己有什么语义"的唯一入口 —— 所以**不需要任何人预先知道下标**。
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

        /// <summary>
        /// 保证至少有 <paramref name="count"/> 个槽（**只增不减**，已有的值与名字都留着）。
        /// 给"按下标写"的那条路用（Warudo 侧写动态参数节点按角色的槽表选址）。
        /// </summary>
        public void Reserve(int count)
        {
            if (count <= SlotCount) return;
            Array.Resize(ref values, count);
            Array.Resize(ref names, count);
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
