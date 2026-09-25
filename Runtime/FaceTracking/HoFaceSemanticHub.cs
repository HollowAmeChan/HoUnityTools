using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **动态参数的一格：名字 + 值**（一个二元组）。
    ///
    /// 为什么是"一格一个元组"而不是"两个平行数组"（2026-09-27 用户定）：
    /// 以前 `float[] values` + `string[] names` 是两个数组**按下标对齐**，那个形状只为两件事服务 ——
    /// ① **动画曲线**能绑到 `values.Array.data[i]`；② 老式"纯按下标写"那条路。
    /// 两条都删了（曲线写不了 Hub、写的人一律按名字），于是"两个数组必须等长、下标必须对齐"
    /// 就成了纯粹的下标漂移风险，没有任何好处。**一个列表，一格一个元组**，没有平行不变量可破坏。
    ///
    /// ⚠️ 字段名别叫 `name`（撞 `UnityEngine.Object.name`）—— 所以这里叫 `key`。
    /// </summary>
    [Serializable]
    public struct HoFaceSemanticSlot
    {
        [Tooltip("参数名（写的人当场声明的，例如 `jawOpen` / `Ho/Drive/Gate/Lip`）。")]
        public string key;
        [Tooltip("当前值。")]
        public float value;
    }

    /// <summary>
    /// **动态参数 Hub**：某个角色实例上、这些参数**当前**是多少的那片**纯存值区域**。
    /// 它也是**唯一的组件** —— 以前旁边还挂一个 `HoFaceSemanticConnector`（"把手"），
    /// 2026-09-27 删了：槽表删掉之后它就只剩"再指一次 Hub"，而按名字读/写本来就该长在这里。
    ///
    /// 【它只干三件事】存值、记名字、按名字读/写。
    /// **没有表、没有资产、没有配置** —— 格子是**写的人**在运行期开的（谁写谁开），名字也是他填的。
    ///
    /// 【谁写：**谁都能写，它是一个"脚本写的工作台"**】（2026-09-26 用户纠正过说法）
    ///   · **我们的**写者是**中间层**：
    ///     — **Unity 侧**：调试会话（`HoFaceAnimationSession`）每帧把**中间层算出来的输出行**
    ///       （配置文件里那些 `参数名 = 曲线(表达式(源键…))`）按名字写进来（**可选开关，默认关**）；
    ///     — **Warudo 侧**：「HoFace写动态参数」节点（`HoFaceHubWriteNode`）把**参数处理 / 合并字典**
    ///       那份字典按名字写进来。
    ///     两边写的是**同一份东西**（中间层的输出 = 那些 `参数名`），所以"面板里看到什么、Warudo 里就是什么"。
    ///   · **别人也能写**：作者自己挂在角色上的组件、别的 mod 的脚本，`SetFloat(名字, 值)` 就行。
    ///     所以"Hub 里能有什么"是**产品边界**（我们提供中间层这一条），不是**机制边界**。
    ///   · ⚠️ **唯一写不进去的是动画 clip**：曲线写 `values.<i>` 那条路 2026-09-26 删了
    ///     （绑定是静态字符串 ⇒ 作者必须先知道下标，细节见 `docs/FACE_TRACKING_DYNAMIC_PARAMETERS.md` §5.1）。
    ///
    /// 【曾经还有一个"跑在控制器里"的写者】控制器状态上的状态机行为
    /// （`HoFaceSemanticWriterBehaviour`，2026-09-26 上午加、下午删）：它的活中间层本来就能干，
    /// 留着就是两份真相 + 一个只在 bundle 里跑、编辑器里看不见的写者。
    ///
    /// 【值不属于任何资产】值就是运行期状态，进程结束就没了；这个组件不写盘，也没有"退出播放时存回去"。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Semantic Hub")]
    public sealed class HoFaceSemanticHub : MonoBehaviour
    {
        /// <summary>
        /// 全部格子：**一格一个 `(名字, 值)` 元组**，顺序 = 谁先声明谁在前。
        /// `public` 是有意的：它既是本组件的对外读面，也是**写的人直接写**的那个列表
        /// （Unity 要序列化它，mod 侧同一份代码要能跑）。
        /// </summary>
        [Tooltip("全部动态参数（名字 + 值）。格子由写的人运行期开出来，这里不用手填。")]
        public List<HoFaceSemanticSlot> parameters = new List<HoFaceSemanticSlot>();

        /// <summary>有几格。</summary>
        public int Count { get { return parameters != null ? parameters.Count : 0; } }

        // ── 读 ──────────────────────────────────────────────────────────────────

        /// <summary>名字 → 下标（线性找；没有返回 −1）。运行期偶尔用一次，别每帧调。</summary>
        public int IndexOf(string name)
        {
            if (parameters == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < parameters.Count; i++)
            {
                string key = parameters[i].key;
                if (key != null && string.Equals(key, name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary>按名字读；没有这一格（或越界）返回 0。</summary>
        public float GetFloat(string name)
        {
            int index = IndexOf(name);
            return index >= 0 ? parameters[index].value : 0f;
        }

        /// <summary>第 <paramref name="index"/> 格的名字（越界返回空串）。给读表格的人用（面板 / 写动态参数）。</summary>
        public string NameAt(int index)
        {
            if (parameters == null || index < 0 || index >= parameters.Count) return "";
            string key = parameters[index].key;
            return key != null ? key : "";
        }

        /// <summary>第 <paramref name="index"/> 格的值（越界返回 0）。</summary>
        public float ValueAt(int index)
        {
            if (parameters == null || index < 0 || index >= parameters.Count) return 0f;
            return parameters[index].value;
        }

        // ── 写 ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// **按名字写**：没有这一格就**当场开一格**（谁写谁声明），名字为空返回 false。
        /// 这是"写的人声明自己有什么参数"的唯一入口 —— 所以**不需要任何人预先知道下标**。
        /// </summary>
        public bool SetFloat(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (parameters == null) parameters = new List<HoFaceSemanticSlot>();

            int index = IndexOf(name);
            if (index < 0)
            {
                parameters.Add(new HoFaceSemanticSlot { key = name, value = value });
                return true;
            }

            var slot = parameters[index];
            slot.value = value;
            parameters[index] = slot;
            return true;
        }

        /// <summary>把所有格子的值清零（丢脸 / 进场 / 手动重置）。名字留着。</summary>
        public void Zero()
        {
            if (parameters == null) return;
            for (int i = 0; i < parameters.Count; i++)
            {
                var slot = parameters[i];
                slot.value = 0f;
                parameters[i] = slot;
            }
        }

        /// <summary>给人看的一行状态（调试面板 / 日志用）。</summary>
        public string Summary()
        {
            int named = 0;
            if (parameters != null)
                for (int i = 0; i < parameters.Count; i++)
                    if (!string.IsNullOrEmpty(parameters[i].key)) named++;
            return name + " · " + Count + " 格（" + named + " 个有名字）";
        }
    }
}
