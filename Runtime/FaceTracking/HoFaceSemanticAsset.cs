using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数资产**：一份"这台角色有哪些语义参数"的**定义**（不是运行期的值）。
    ///
    /// 【它在体系里的位置】（2026-09-25 与用户定稿的设计）
    /// ```
    /// 动态参数资产（本文件）      = 这些名字代表什么（语义定义）
    /// HoFaceSemanticHub（组件）  = 某个角色实例上、这些语义**当前**是多少（运行期槽）
    /// 控制器（资产）             = 我要控制什么（它的曲线写进 Hub）
    /// 动画生成器                 = 把上面三者编译成 Animator 能吃的动画资产
    /// ```
    /// 所以**定义放资产、值放组件**：ScriptableObject 天然适合描述接口，不适合当"某个角色这一帧
    /// `MouthX = 0.73`"的容器 —— 后者是每个角色实例各自一份的运行期状态。
    ///
    /// 【为什么消费者"填这个资产"就够】
    /// 消费方（作者的脚本 / 材质控制 / 蓝图）只声明**语义**（`MouthOpen`），不关心它从哪来
    /// （ARKit？iPhone？混合树？另一个控制器？手动？）。运行时它拿这个资产去找到角色上的
    /// <see cref="HoFaceSemanticHub"/>，按下标取当前值。**换面捕方案时消费方一行都不用动。**
    ///
    /// 【⚠️ 为什么是"资产"而不是"字符串表写在组件里"】
    /// 控制器在 **bundle** 里跑（Warudo 侧），而 bundle 里放不了我们自己写的组件类型，
    /// 所以给控制器用的 Hub 是一份**代理**；代理上只有"值"，没有名字。
    /// 名字 ↔ 下标 的映射必须放在一个**双方都能引用**的东西上 —— 资产就是那个东西。
    /// 这也是生成器能在生成期就知道"`P/MouthX` 该写第几个槽"的依据。
    /// </summary>
    [CreateAssetMenu(fileName = "HoFaceSemantics", menuName = "Ho Unity Tools/面捕/动态参数资产", order = 10)]
    public sealed class HoFaceSemanticAsset : ScriptableObject
    {
        /// <summary>一个语义参数的定义。**只有定义，没有值。**</summary>
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("语义名（角色层面的公开名字，例如 MouthOpen / MouthX / FaceState）。")]
            public string key = "";

            [Tooltip("这个语义是什么、谁在用。给作者看，也进生成器的报告。")]
            public string note = "";

            [Tooltip("这一格中性时应该是多少。生成器的基准姿势、以及运行期没数据时的回退都用它。")]
            public float neutral;

            [Tooltip("上下限。生成期用来检查生成的曲线有没有超范围；运行期可选地夹取。")]
            public float min = 0f;
            public float max = 1f;
        }

        [Tooltip("这份资产属于哪张脸 / 哪个角色（只给人看）。")]
        public string displayName = "";

        [Tooltip("整份定义的说明：谁定义、给谁读、有没有对应控制器。")]
        [TextArea(2, 6)]
        public string notes = "";

        /// <summary>
        /// 语义参数表。**顺序即下标**：控制器写 `P/xxx` 时写的就是这里的下标，
        /// 所以**不要重排、不要在中间插入**（插到末尾是安全的）。
        ///
        /// ⚠️ 用 **public 字段**而不是 `[SerializeField] private`：这份类型要**逐字节同步进 mod 程序集**
        /// （`sync-modcore.ps1`），而"私有字段 + `[SerializeField]` 在 mod 程序集里能不能序列化"
        /// **没有实测过** —— public 字段是 Unity 一定序列化的形式，不赌这一条。
        /// </summary>
        public List<Entry> entries = new List<Entry>();

        /// <summary>有几个语义参数（= Hub 上运行期槽的数量）。</summary>
        public int Count { get { return entries != null ? entries.Count : 0; } }

        /// <summary>第 <paramref name="index"/> 个的名字；越界返回空串。</summary>
        public string KeyAt(int index)
        {
            if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return "";
            return entries[index].key;
        }

        /// <summary>名字 → 下标。找不到返回 **−1**。生成器与编辑器用；运行期也可以用来做一次解析。</summary>
        public int IndexOf(string key)
        {
            if (entries == null || string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && string.Equals(entries[i].key, key, System.StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>这一格的中性值；越界返回 0。</summary>
        public float NeutralAt(int index)
        {
            if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return 0f;
            return entries[index].neutral;
        }

        /// <summary>名字的快照（**顺序即下标**）。运行期要用的时候拿它，别每次去问资产。</summary>
        public string[] BuildKeySnapshot()
        {
            int count = Count;
            var keys = new string[count];
            for (int i = 0; i < count; i++) keys[i] = KeyAt(i);
            return keys;
        }

        /// <summary>
        /// 把中性值填进一份运行期数组（长度按本资产）。生成器的基准姿势、Hub 的初始状态都用它。
        /// </summary>
        public void FillNeutral(float[] values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
                values[i] = i < Count ? NeutralAt(i) : 0f;
        }

        /// <summary>给人看的一行摘要（面板 / 日志用）。</summary>
        public string Summary()
        {
            int named = 0;
            for (int i = 0; i < Count; i++) if (!string.IsNullOrEmpty(KeyAt(i))) named++;
            return (string.IsNullOrEmpty(displayName) ? name : displayName) + "（" + named + " 个语义参数）";
        }

        private void OnValidate()
        {
            // 重名会静默毁掉"名字 → 下标"，而这类错误在运行期表现为"某个参数永远不动"。
            //
            // ⚠️ 这里**故意不放在 `#if UNITY_EDITOR` 里**：这个文件要**逐字节**同步进 mod 程序集
            // （`sync-modcore.ps1` 只在文件头加 6 行标记、换命名空间），条件编译块会让两边不一致。
            // `OnValidate` 在播放器里不会被调用，留着无害。
            if (entries == null) return;
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (string.IsNullOrEmpty(entry.key)) continue;
                if (!seen.Add(entry.key))
                    Debug.LogError("[Ho 面捕] 动态参数资产「" + name + "」有重名 key：" + entry.key
                        + "（第 " + i + " 项）。重名会让「名字 → 下标」指向错的那个槽。", this);
                if (entry.max < entry.min)
                    Debug.LogWarning("[Ho 面捕] 动态参数资产「" + name + "」的 " + entry.key + " 上下限反了（min > max）。", this);
            }
        }
    }
}
