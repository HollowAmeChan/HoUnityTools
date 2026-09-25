using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数连接器**：把"这些槽位是什么意思"接到一片纯存值的
    /// <see cref="HoFaceSemanticHub"/> 上。**这是作者要挂的那一个组件**，也是消费方的入口。
    ///
    /// 【体系里的四个角色】
    /// ```
    /// HoFaceSemanticHub（组件）        = 值：某个角色实例上这些槽**当前**是多少（不认识名字）
    /// HoFaceSemanticConnector（本组件） = 名字：槽表（名字 / 备注）+ 指向那个 Hub
    /// 控制器（资产）                    = 我要控制什么（它的曲线按**下标**写进 Hub）
    /// 消费方                            = 填本组件，按**名字**读（connector.GetFloat("MouthOpen")）
    /// ```
    /// ⚠️ 表里**没有中性值这一项**：中性 = **0**，是静态规则（见 <see cref="ApplyNeutral"/>）。
    /// 这张表只是"下标是什么意思"的**连接规则**，不携带值、也不携带每槽的默认值。
    ///
    /// 【为什么要有它（而不是让 Hub 自己带名字）】
    /// ① **bundle 里的那份 Hub 不能带名字**：控制器在 bundle 里跑，bundle 里放不了我们自己写的
    ///    组件类型；给控制器用的是一份**代理 Hub**（只有 `float[]`）。名字如果长在 Hub 上，
    ///    代理那份就得有一张"假表"，两边迟早对不上。
    /// ② **表和值分开之后，值可以随便清零/重排，表可以慢慢改**：
    ///    改名字不动值，清值不动名字。
    /// ③ **消费方只需要这一个引用**：填 Connector（它自己指向 Hub），就不用同时维护两个引用。
    /// ④ **每个角色一份表是有意的**（不是一个缺陷）：表长在预制件上，N 个角色 = N 份拷贝。
    ///    我们**不做**跨角色共享词表 —— 那正是原来那个资产要做的事，也正是不值得为它多一个文件的原因。
    ///
    /// 【⚠️ 为什么槽位是"预留"的，而不是本组件按表把 Hub 调长】
    /// Hub 一开始就预留 <see cref="HoFaceSemanticHub.DefaultSlotCount"/> 个槽（见那边注释）。
    /// 本组件**不会**改 Hub 的数组长度 —— 那会让两个组件耦合（改表就静默改写正在动的值），
    /// 而且代理那份 Hub 没有表、长度就没人定。**表只管解释下标，不管长度。**
    /// 所以：**顺序即下标**，表里第 i 项解释槽 i；表比 Hub 的槽多时只有前
    /// <see cref="HoFaceSemanticHub.SlotCount"/> 项有意义（`OnValidate` 会点出来）。
    ///
    /// 【怎么用】
    ///   · 角色 mod：在角色的一个空物体（例如 `Character/SemanticHub`）上挂 **Hub**（存值），
    ///     再把本组件挂在**同一个物体**（或任何方便编排的地方）并把 `hub` 拖上。
    ///     添加组件那一刻如果同一个物体上已经有 Hub，`Reset` 会顺手填好引用（只填一次，永不覆盖）。
    ///   · 消费方：填本组件，`GetFloat("MouthOpen")`；每帧都要读的话在 `Start` 里
    ///     `IndexOf` 解析一次、之后用 `GetFloat(int)`。
    ///
    /// ⚠️ **类型要逐字节同步进 mod 程序集**（`.research/sync-modcore.ps1`，只有命名空间不同），
    /// 所以里面**不要写 `#if UNITY_EDITOR`**，也不要用"私有字段 + `[SerializeField]`"
    /// （能不能在 mod 程序集里序列化没实测过，public 字段是 Unity 一定序列化的形式）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Semantic Connector")]
    public sealed class HoFaceSemanticConnector : MonoBehaviour
    {
        /// <summary>
        /// 一个语义槽的**定义**（只有定义，没有值；值在 <see cref="hub"/> 上，下标就是表里的位置）。
        /// </summary>
        [Serializable]
        public sealed class Slot
        {
            [Tooltip("语义名（角色层面的公开名字，例如 MouthOpen / MouthX / FaceState）。")]
            public string key = "";

            [Tooltip("这个语义是什么、谁在用。给作者看，也进生成器的报告。")]
            public string note = "";
        }

        [Tooltip("存值的那个 Hub。挂在同一个物体上时，添加本组件那一刻会自动填上。")]
        public HoFaceSemanticHub hub;

        /// <summary>
        /// 槽表。**顺序即下标**：控制器写 `values.<i>` 时写的就是这里的第 i 项，
        /// 所以**不要重排、不要在中间插入**（插到末尾是安全的）。
        ///
        /// ⚠️ public 字段而不是 `[SerializeField] private`：本类型要逐字节同步进 mod 程序集，
        /// public 字段是 Unity 一定序列化的形式，不赌"私有字段在那边能不能序列化"。
        /// </summary>
        public List<Slot> slots = new List<Slot>();

        /// <summary>表里声明了几项（= 有名字的槽有几个）。</summary>
        public int Count { get { return slots != null ? slots.Count : 0; } }

        /// <summary>Hub 上实际有几个槽；**没有 Hub 时是 0**。</summary>
        public int SlotCount { get { return hub != null ? hub.SlotCount : 0; } }

        /// <summary>第 <paramref name="index"/> 项的名字；越界返回空串。</summary>
        public string KeyAt(int index)
        {
            if (slots == null || index < 0 || index >= slots.Count || slots[index] == null) return "";
            return slots[index].key;
        }

        /// <summary>名字 → 下标。找不到返回 **−1**。生成器与编辑器用；运行期也可以用来做一次解析。</summary>
        public int IndexOf(string key)
        {
            if (slots == null || string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && string.Equals(slots[i].key, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>名字的快照（**顺序即下标**）。运行期要用的时候拿它，别每次去问表。</summary>
        public string[] BuildKeySnapshot()
        {
            int count = Count;
            var keys = new string[count];
            for (int i = 0; i < count; i++) keys[i] = KeyAt(i);
            return keys;
        }

        // ── 读写（都是"解析下标之后交给 Hub"）─────────────────────────────────────

        /// <summary>按下标读；没有 Hub 或越界返回 0。</summary>
        public float GetFloat(int index)
        {
            return hub != null ? hub.GetFloat(index) : 0f;
        }

        /// <summary>按名字读。每帧都要读的话，在 `Start` 里 <see cref="IndexOf"/> 一次再按下标读。</summary>
        public float GetFloat(string key)
        {
            return GetFloat(IndexOf(key));
        }

        /// <summary>
        /// 按下标写（越界什么都不做）。
        /// ⚠️ 槽要**先被开出来**才写得进去：写手用 <see cref="HoFaceSemanticHub.ClaimSlot"/> 开，
        /// 节点/中继用 <see cref="HoFaceSemanticHub.Reserve"/> 开。编辑期 Hub 是空的（那是常态），
        /// 这时候直接写会**静默丢掉** —— 消费方要写就先自己 `hub.Reserve(i + 1)`。
        /// </summary>
        public void SetFloat(int index, float value)
        {
            if (hub != null) hub.SetFloat(index, value);
        }

        /// <summary>按名字写。</summary>
        public void SetFloat(string key, float value)
        {
            SetFloat(IndexOf(key), value);
        }

        /// <summary>
        /// 把整片槽**清成中性**。中性值就是 **0**，而且是**静态的规则、不是每槽一项数据**
        /// —— 这张表只是"下标是什么意思"的连接规则，不携带值也不携带默认值。
        /// （⚠️ `0.5` 那种"中立位"是 VBridger 私有的约定，我们**不采用**：见
        /// `docs/PARAMETER_HO.md` §2。ARKit 与 VTS 官方追踪参数的中性都是 0。）
        /// 丢脸 / 进场 / 手动重置时用；等价于 <see cref="HoFaceSemanticHub.Zero"/>。
        /// </summary>
        public void ApplyNeutral()
        {
            if (hub != null) hub.Zero();
        }

        /// <summary>给人看的一行状态（面板 / 日志用）。</summary>
        public string Summary()
        {
            string hubText = hub != null ? hub.name + "（" + hub.SlotCount + " 槽）" : "**没填 Hub**";
            int named = 0;
            for (int i = 0; i < Count; i++) if (!string.IsNullOrEmpty(KeyAt(i))) named++;
            return name + " · " + hubText + " · 表 " + named + " 项";
        }

        // ── 编辑器 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 添加组件那一刻（编辑器里 `Add Component` / `Reset`）如果**同一个物体**上有 Hub，就顺手填上引用。
        /// **只在引用为空时填、只找同一个物体** —— 不搜子层级、不建任何东西、永不覆盖已填的引用。
        /// </summary>
        private void Reset()
        {
            if (hub == null) hub = GetComponent<HoFaceSemanticHub>();
        }

        [ContextMenu("清成中性（全部写 0）")]
        private void ApplyNeutralFromMenu()
        {
            ApplyNeutral();
        }

        private void OnValidate()
        {
            // 这里**只查重名**（那会静默毁掉"名字 → 下标"，运行期表现是"某个参数永远不动"）。
            //
            // ⚠️ 这里**故意不放在 `#if UNITY_EDITOR` 里**：这个文件要**逐字节**同步进 mod 程序集，
            // 条件编译块会让两边不一致。`OnValidate` 在播放器里不会被调用，留着无害。
            //
            // ⚠️ **曾经还在这里报过两件事，都删了**（2026-09-26 现场）：
            // ① "表比 Hub 的槽多" —— 那是**旧设计**的约束（Hub 要预先按表定长）。现在槽是
            //    **写的人在运行期按名字开的**（写手 `ClaimSlot`、节点与中继 `Reserve`），
            //    所以**编辑期 Hub 是空的才是常态**；这条警告每次都误报，还给了错的建议
            //    （"把 values 调大"）。
            // ② "没填 Hub" —— 一呢，`Reset()` 之外用户拖 Hub 之前它必然报一次；二呢，
            //    **检查器插件会偷偷 new 一个本组件**去读字段默认值（vInspector 的
            //    "Dummy object for fetching default variable values…"），那种临时对象上
            //    永远没填 Hub，于是每画一次检查器就刷一条跟用户无关的警告。
            //    真正要紧的那两处本来就会报：面板那一栏的黄字、以及中继/节点的状态行
            //    （"角色上没有 Connector（或它没填 Hub）"）—— 在**它真的挡住事**的时候报。
            if (slots == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.key)) continue;
                if (!seen.Add(slot.key))
                    Debug.LogError("[Ho 面捕] 动态参数连接器「" + name + "」有重名 key：" + slot.key
                        + "（第 " + i + " 项）。重名会让「名字 → 下标」指向错的那个槽。", this);
            }
        }
    }
}
