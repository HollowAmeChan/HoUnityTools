using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数连接器**：把 <see cref="HoFaceSemanticHub"/> 上的值**挂出去给消费方用**的那个引用。
    ///
    /// 【它是什么】消费方（作者的脚本 / 材质控制 / 蓝图）**只填这一个引用**，然后**按名字**读：
    /// <c>connector.GetFloat("MouthOpen")</c>。值在 Hub 上，Connector 是把手 —— 换面捕方案时消费方一行都不用改。
    ///
    /// 【⚠️ 它不再有一张"槽表"】（2026-09-26 用户定：**删掉表**）
    /// 名字**不属于**这里：谁写谁声明 —— 控制器里的
    /// <see cref="HoFaceSemanticWriterBehaviour"/> 在运行期用
    /// <see cref="HoFaceSemanticHub.ClaimSlot"/> 开槽并写下名字，那些名字就长在 Hub 的 `names` 上。
    /// 于是：**想知道有哪些名字，跑一遍看 Hub（或日志里"新声明"那一行）**，或者两边事先约定好名字。
    /// 这也正是它"像个连接器"的原因：这里是接口，名字的定义在写的人手上。
    ///
    /// ⚠️ 代价要认：**没有表就没有"名字对不上"的校验点**了。写手把 `MouthX` 敲成 `Mouthx` 时，
    /// 那条值会**安静地**开出一个新槽（`Mouthx`），而读 `MouthX` 的人只会拿到 0。
    /// 现在发现它的唯一办法就是"跑一遍看名字"—— 节点与中继会在**新声明**出现时写一行日志点名。
    ///
    /// 【怎么用】
    ///   · 角色 mod：在一个空物体（例如 `Character/SemanticHub`）上挂 **Hub**（存值），再把本组件挂在
    ///     **同一个物体**上（添加组件那一刻 `Reset` 会自动填好引用）。
    ///   · 消费方：填本组件，`GetFloat("名字")`；每帧都要读的话在 `Start` 里解析一次更省（但要先跑起来
    ///     让名字被声明出来）。
    ///   · 要写（比如材质控制器自己生产一个语义）：`SetFloat("名字", v)` —— 名字没声明过时会**当场开一格**。
    ///
    /// ⚠️ **类型要逐字节同步进 mod 程序集**（`.research/sync-modcore.ps1`，只有命名空间不同），
    /// 所以里面**不要写 `#if UNITY_EDITOR`**，字段一律 public（要序列化进预制件）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Semantic Connector")]
    public sealed class HoFaceSemanticConnector : MonoBehaviour
    {
        [Tooltip("存值的那个 Hub。挂在同一个物体上时，添加本组件那一刻会自动填上。")]
        public HoFaceSemanticHub hub;

        /// <summary>
        /// 按名字读当前值。名字还没被声明过（或没有 Hub）时返回 0。
        /// 名字 → 槽位由 <see cref="HoFaceSemanticHub.IndexOfName"/> 现查（名字通常只有几个到几十个，
        /// 但**每帧都要读同一个名字**的话，自己缓存一次下标更省）。
        /// </summary>
        public float GetFloat(string name)
        {
            if (hub == null || string.IsNullOrEmpty(name)) return 0f;
            int index = hub.IndexOfName(name);
            return index >= 0 ? hub.GetFloat(index) : 0f;
        }

        /// <summary>
        /// 按名字写。名字**还没被声明过时会当场开一格**（与写手同一个机制：谁写谁声明），
        /// 所以消费方永远写得进去 —— 名字为空（或没有 Hub）时返回 false。
        /// </summary>
        public bool SetFloat(string name, float value)
        {
            if (hub == null || string.IsNullOrEmpty(name)) return false;
            int index = hub.ClaimSlot(name);
            if (index < 0) return false;
            hub.SetFloat(index, value);
            return true;
        }

        /// <summary>给人看的一行状态（调试面板 / 日志用）。</summary>
        public string Summary()
        {
            return hub != null ? name + " → " + hub.Summary() : name + " → **没填 Hub**";
        }

        /// <summary>
        /// 添加组件那一刻（编辑器里 `Add Component` / `Reset`）如果**同一个物体**上有 Hub，就顺手填上引用。
        /// **只在引用为空时填、只找同一个物体** —— 不搜子层级、不建任何东西、永不覆盖已填的引用。
        /// </summary>
        private void Reset()
        {
            if (hub == null) hub = GetComponent<HoFaceSemanticHub>();
        }
    }
}
