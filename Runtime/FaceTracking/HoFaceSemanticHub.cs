using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数 Hub**：某个角色实例上、这些语义参数**当前**是多少的那片**纯存值区域**。
    ///
    /// 【它只干一件事】
    /// 存值。**它不认识任何名字** —— 没有资产引用、不认识
    /// <see cref="HoFaceSemanticConnector"/>、也没有按名字读写的口子。
    /// 名字 ↔ 下标 的映射在 Connector 上（那是**给人和生成器看**的一层）。
    ///
    /// 【为什么是"预留固定槽位"而不是"按表定长"】
    /// ① **控制器要能写进来**：控制器在 bundle 里跑，而 bundle 里放不了我们自己写的组件类型
    ///    —— 给控制器用的是一份**代理**，代理上没有名字、只有值。数组 + 下标是"不需要名字也能写"的形式。
    /// ② **运行期读不能反射**：UMod 禁 `System.Reflection`，一堆 `public float MouthX` 是读不出来的，
    ///    下标可以。
    /// ③ **长度不能跟别的组件耦合**：一旦"表有几项 ⇒ 数组多长"，Hub 就依赖 Connector
    ///    （于是代理那份、bundle 那份都要有表），而且改表会**静默改写正在动的值**。
    ///    这里改成**一开始就预留 <see cref="DefaultSlotCount"/> 个槽**：
    ///    Hub 自己就是完整的，谁都不需要告诉它多长。
    ///    ⚠️ 代价要记住：**下标越界时 Unity 写动画不报错、值就是不动** ——
    ///    所以写入口（节点 / 面板）必须自己检查下标，并在状态里说清"越界了几个"。
    ///
    /// 【为什么值不写成一堆 `public float MouthX`】
    /// 加一个语义 = 表里加一项，**代码一行不动**；而一堆字段就得改类、改所有引用它的地方。
    ///
    /// 【谁写、谁读】
    ///   · **写**：控制器曲线（`path = Hub 所在层级`、`type = HoFaceSemanticHub`、
    ///     `propertyName = "values.<i>"`）、Unity 侧调试会话（`SetFloat(i, v)`）、
    ///     Warudo 侧的「HoFace写动态参数」节点（按下标写）。
    ///   · **读**：消费方（作者的脚本 / 材质控制 / 蓝图）通过
    ///     <see cref="HoFaceSemanticConnector"/> 按名字读（`connector.GetFloat("MouthOpen")`）；
    ///     运行期要按名字反复读的，**在 `Start` 里解析一次下标、之后只按下标读**。
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
        /// 新建（或数组被清空）时预留多少个槽。**128 是个预算，不是协议** ——
        /// 控制器的曲线写 `values.<i>`，越界的写不进去（Unity 不报错），
        /// 所以这里给足余量；真不够时在 Inspector 里把数组调大即可。
        /// </summary>
        public const int DefaultSlotCount = 128;

        /// <summary>
        /// 当前值。**下标就是语义槽的编号**（0 起，含义由 <see cref="HoFaceSemanticConnector"/> 的槽表解释）。
        ///
        /// `public` 是有意的：它既是本组件的对外读面，也是**控制器曲线要写进去的那个属性**
        /// （`propertyName = "values.<i>"`）。
        /// </summary>
        [Tooltip("当前值。下标就是语义槽编号（0 起）；含义见 HoFaceSemanticConnector 的槽表。控制器曲线直接写这里。")]
        public float[] values = new float[DefaultSlotCount];

        /// <summary>槽位数（= 数组长度）。数组为空时是 0。</summary>
        public int SlotCount { get { return values != null ? values.Length : 0; } }

        /// <summary>按下标取值；越界返回 0。</summary>
        public float GetFloat(int index)
        {
            if (values == null || index < 0 || index >= values.Length) return 0f;
            return values[index];
        }

        /// <summary>按下标写值；越界**什么都不做**（调用方要自己检查 <see cref="SlotCount"/> 并报出来）。</summary>
        public void SetFloat(int index, float value)
        {
            if (values == null || index < 0 || index >= values.Length) return;
            values[index] = value;
        }

        /// <summary>把所有槽清零。丢脸 / 进场 / 手动重置时用。</summary>
        public void Zero()
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) values[i] = 0f;
        }

        /// <summary>
        /// 数组为空时补成 <see cref="DefaultSlotCount"/> 个槽。**只在空的时候补** ——
        /// 已经调过长度的（比如作者故意设成 32）不碰：那是有意的设置，不是错误。
        /// </summary>
        public void EnsureSlots()
        {
            if (values == null || values.Length == 0) values = new float[DefaultSlotCount];
        }

        /// <summary>给人看的一行状态（调试面板 / 日志用）。</summary>
        public string Summary()
        {
            return name + " · " + SlotCount + " 个槽";
        }

        private void OnEnable()
        {
            EnsureSlots();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) EnsureSlots();
        }
    }
}
