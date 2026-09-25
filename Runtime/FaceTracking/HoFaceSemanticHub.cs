using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数 Hub**：某个角色实例上、这些语义参数**当前**是多少的运行期槽。
    ///
    /// 【跟 <see cref="HoFaceSemanticAsset"/> 的分工】
    /// 资产 = 定义（有哪些名字、中性多少、范围多大）；本组件 = 值（这一帧 MouthX 是多少）。
    /// 组件引用资产，**下标一一对应**（`entries[i]` ↔ `values[i]`）。
    ///
    /// 【为什么值是 `float[]` 而不是一堆 `public float MouthX`】
    /// 三个理由，缺一不可：
    ///   ① **控制器要能写进来**。控制器在 bundle 里跑，而 bundle 里放不了我们自己写的组件类型
    ///      —— 给控制器用的是一份**代理**，代理上没有名字、只有值。数组 + 下标是"不需要名字也能写"的形式。
    ///   ② **运行期读不需要反射**：UMod 禁 `System.Reflection`，一堆字段名是读不出来的，下标可以。
    ///   ③ **加减参数不用改代码**：加一个语义 = 资产里加一项，代码一行不动
    ///      （一堆 `public float` 就得改类、改所有引用它的地方）。
    /// 名字 ↔ 下标 的映射在 `asset` 上，**只在编辑器与生成期用** —— 这正合"名字不进运行期解释"那条规矩。
    ///
    /// 【怎么用】
    ///   · 角色 mod：在一个空物体（例如 `Character/SemanticHub`）上挂本组件，`asset` 指向那份动态参数资产。
    ///   · 消费方（作者自己的脚本 / 材质控制 / 蓝图）：**也填同一个资产**，运行时通过
    ///     <see cref="GetFloat(int)"/> / <see cref="GetFloat(string)"/> 取当前值 —— 它不需要认识控制器。
    ///
    /// 【⚠️ 值不属于资产】
    /// 资产是 Project 里的共享定义，**不要把这一帧的值写回资产**（那就成了"所有角色共用一个当前值"）。
    /// 本组件也**不会**在退出播放时把值写回资产；值就是运行期状态，进程结束就没了。
    /// </summary>
    [DisallowMultipleComponent]
    // ⚠️ AddComponentMenu 的写法与本包其余组件一致：根 `HoUnityTools`（**没有空格**）、
    // 分类与组件名**都用英文**、组件名带 `Ho ` 前缀。中文标题在这个菜单里很扎眼
    // （它跟 Animation / Layout / Mesh 那些英文分类并列），而且根名多两个空格会**多出一整个
    // 顶层分类** `Ho Unity Tools`，与 `HoUnityTools` 并存 —— 真发生过。
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Semantic Hub")]
    public sealed class HoFaceSemanticHub : MonoBehaviour
    {
        [Tooltip("引用那份「动态参数资产」—— 它定义有哪些语义参数，顺序即下标。")]
        public HoFaceSemanticAsset asset;

        /// <summary>
        /// 当前值，**下标与 <see cref="asset"/> 的 entries 一一对应**（Unity 序列化 + 可被动画写）。
        ///
        /// `public` 是有意的：它既是本组件的对外读面，也是**控制器曲线要写进去的那个属性**
        /// （`path = "SemanticHub"`、`type = HoFaceSemanticHub`、`propertyName = "values.<i>"`）。
        /// </summary>
        [Tooltip("当前值。下标对应资产里的 entries；控制器动画直接写这里。")]
        public float[] values;

        // ── 运行期 ──────────────────────────────────────────────────────────────

        /// <summary>最近一次按资产对齐过的资产引用（资产被换掉时重新对齐）。</summary>
        private HoFaceSemanticAsset aligned;

        /// <summary>语义个数的真值来源。没有资产时用当前数组长度（那就是"只有值、没有名字"的代理形态）。</summary>
        public int Count
        {
            get
            {
                if (asset != null) return asset.Count;
                return values != null ? values.Length : 0;
            }
        }

        /// <summary>
        /// 按资产把 <see cref="values"/> 对齐到正确长度并填中性值。
        /// **幂等**：长度对且资产没换就直接返回（不覆盖已经在动的值）。资产**换了**才重新对齐 ——
        /// 换资产会让下标含义整体改变，旧值留着没有意义。
        /// </summary>
        public void AlignToAsset()
        {
            if (asset == null) return;
            int count = asset.Count;

            if (values != null && values.Length == count && ReferenceEquals(aligned, asset)) return;

            var next = new float[count];
            asset.FillNeutral(next);
            values = next;
            aligned = asset;
        }

        /// <summary>按下标取值；越界返回 0（0 = 中性，跟"没有这个槽"同义）。</summary>
        public float GetFloat(int index)
        {
            if (values == null || index < 0 || index >= values.Length) return 0f;
            return values[index];
        }

        /// <summary>按下标写值。**给 Unity 侧调试会话用**（控制器路径由动画曲线直接写 <see cref="values"/>）。</summary>
        public void SetFloat(int index, float value)
        {
            if (values == null || index < 0 || index >= values.Length) return;
            values[index] = value;
        }

        /// <summary>
        /// 按名字取值。名字 → 下标 要过一次资产（**每次调用都查表**）。
        /// 消费方每帧都要读的话，**在 `Start` 里用 <see cref="IndexOf"/> 解析一次、之后只按下标读**。
        /// </summary>
        public float GetFloat(string key)
        {
            return GetFloat(IndexOf(key));
        }

        /// <summary>按名字写值（同 <see cref="GetFloat(string)"/> 的提示）。</summary>
        public void SetFloat(string key, float value)
        {
            SetFloat(IndexOf(key), value);
        }

        /// <summary>名字 → 下标；没有资产或找不到返回 −1。</summary>
        public int IndexOf(string key)
        {
            return asset != null ? asset.IndexOf(key) : -1;
        }

        /// <summary>下标 → 名字；没有资产或越界返回空串（**这就是"只有值没有名字"的代理形态**）。</summary>
        public string KeyAt(int index)
        {
            return asset != null ? asset.KeyAt(index) : "";
        }

        /// <summary>把所有槽恢复成资产里的中性值（进场、丢脸、手动重置时用）。</summary>
        public void ResetToNeutral()
        {
            if (asset != null) asset.FillNeutral(values);
            else if (values != null) for (int i = 0; i < values.Length; i++) values[i] = 0f;
        }

        /// <summary>给人看的一行状态（调试面板 / 日志用）。</summary>
        public string Summary()
        {
            string assetName = asset != null ? asset.Summary() : "（没填资产）";
            return name + " · " + assetName + " · 值 " + (values != null ? values.Length : 0) + " 个";
        }

        private void OnEnable()
        {
            // 进播放（或第一次启用）时对齐一次，免得数组长度跟资产不一致、下标全错位。
            AlignToAsset();
        }

        private void OnValidate()
        {
            // 在编辑器里改资产引用之后立刻对齐，**不用等进播放** —— 否则面板上看到的下标是旧的。
            if (!Application.isPlaying) AlignToAsset();
        }
    }
}
