using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **语义写手（状态机行为）**：控制器在某个状态里跑的时候，把**算出来的中间值**按**名字**
    /// 写进影子上的 <see cref="HoFaceSemanticHub"/>。挂在状态上（`Add Behaviour`），不用画曲线。
    ///
    /// 【为什么不是"用动画曲线写 Hub"】（2026-09-26 改，这是本节的重点）
    /// 曲线那条路的绑定是**静态字符串**（`propertyName = "values.<i>"`），于是：
    /// ① 写动画的人**必须先知道下标**；② Unity 的 Animation 窗口里数组元素显示成
    /// `Element 0`/`Element 1`…**没有名字**；③ 槽表一旦重排，旧曲线**静默写错槽**；
    /// ④ 数组长度必须在打包前猜对（越界写**不报错、值就是不动**）。
    /// 换成"状态机里的代码"之后这四条**同时消失**：名字靠 <see cref="HoFaceSemanticHub.ClaimSlot"/>
    /// 当场开槽，没有下标、没有数组元素路径、没有长度魔法数字。
    /// （生态里就是这么做的：ChilloutVR 的 `Animator Driver` 状态机行为是同一个思路。）
    ///
    /// 【⚠️ 它读的是 **Animator 参数**，不是姿态】
    /// 表达式里的变量 = **这个控制器的 Animator 参数名**（float / int / bool，别的类型当 0）。
    /// 所以"中间值"要么本来就是一个参数，要么**用表达式从参数算出来**（这正是它比姿态更好的地方：
    /// 为了把一个值拿出来，不用真的去摆一个 marker 形态键）。
    /// 想读**姿态**（某个形态键的权重 / 某根骨头）现在还没做 —— 那需要每个条目多带一个"源"描述，
    /// 等真有这个需求再加（见 `docs/FACE_TRACKING_DYNAMIC_PARAMETERS.md` §5.3）。
    ///
    /// 【语义名怎么定】
    /// 每个条目自己填 `slot`（语义名，例如 `MouthX`）。它进的是**影子 Hub 的名字表**，
    /// 由 Warudo 侧采出来后按名字交给角色的
    /// <see cref="HoFaceSemanticConnector"/> 槽表去解释 ⇒ **两边的顺序不用一致**，
    /// 名字对不上会在「HoFace写动态参数」的 `状态` 里被点名。
    ///
    /// 【什么时候写】
    /// `OnStateEnter` + `OnStateUpdate`（状态激活期间每帧）。**状态退出时不清零** ——
    /// 和 Unity 动画的语义一致（没人再写它，就保持最后的值）；要"丢脸清中性"由下游那条链负责。
    ///
    /// ⚠️ **类型要逐字节同步进 mod 程序集**（`.research/sync-modcore.ps1`，只有命名空间不同）：
    /// 不要写 `#if UNITY_EDITOR`，字段一律 public（要序列化进控制器资产）。
    /// （它**不需要** `[AddComponentMenu]`：状态机行为是在状态的 Inspector 里 `Add Behaviour` 挂的，
    /// 不出现在物体的 Add Component 菜单里。）
    /// </summary>
    public sealed class HoFaceSemanticWriterBehaviour : StateMachineBehaviour
    {
        /// <summary>一个"要写出去的语义"：目标槽名 + 它的值怎么算。</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("语义名（写进 Hub 的名字表）。要和角色的 HoFaceSemanticConnector 槽表里的名字对得上。")]
            public string slot = "";

            [Tooltip("表达式：变量就是这个控制器的 Animator 参数名。例：`MouthX`、`jawOpen * 0.5 + smile * 0.5`。")]
            public string expression = "";
        }

        /// <summary>要写出去的语义。**顺序不重要**（槽是按名字开的，谁先声明谁在前面）。</summary>
        public List<Entry> entries = new List<Entry>();

        // ── 缓存（每帧不重新解析、不重新查表）────────────────────────────────────
        private Animator current;                       // 本次 Write 的 animator（给 Lookup 用）
        private Animator hubAnimator;                   // 缓存下面那个 hub 是"哪个 animator 的"
        private HoFaceSemanticHub hub;                  // 找到的影子 Hub（每个 animator 各自找一次）
        private Func<string, float> lookup;             // Lookup 的方法组，避免每帧分配委托
        private HoFaceExpression[] parsed;              // 解析好的表达式（按条目下标）
        private string[] parsedText;                    // 上面那份对应的原文（改了才重解析）
        private string[] parameterNames;                // 控制器参数名（用来判断"这个名字能不能读"）
        private AnimatorControllerParameterType[] parameterKinds;
        private bool parametersCached;
        private readonly List<string> reported = new List<string>();   // 报过一次就别每帧刷屏

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            Write(animator);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            Write(animator);
        }

        // ── 干活 ────────────────────────────────────────────────────────────────

        private void Write(Animator animator)
        {
            if (animator == null || entries == null || entries.Count == 0) return;

            // ⚠️ **状态机行为的实例是"每个控制器资产一份"、不是每个 Animator 一份**（Unity 的行为）：
            // 同一个控制器被两个 Animator 同时跑时，这个对象是共享的。所以缓存必须**跟着 animator 走**，
            // 否则第二个 Animator 会拿到第一个的 Hub / 参数表 —— 表现是"另一台角色的值写到这台身上"。
            if (hubAnimator != animator)
            {
                hubAnimator = animator;
                hub = animator.GetComponentInChildren<HoFaceSemanticHub>(true);
                parametersCached = false;
            }

            if (hub == null)
            {
                // ⚠️ **这里故意不报。** 没有 Hub = "这个 Animator 不是语义 rig"，而不是错误：
                //   · 混合树观察台（`HoFaceBlendTreePeek`）会自己补一个 Animator 去跑同一个 controller
                //     —— 那是**预览台**，它身上从来没有 Hub（2026-09-26 现场：用户把观察台的
                //     `controller` 填成了这个 face controller，于是每次进播放都刷一条警告）；
                //   · 别的地方（例如拿同一份 controller 做别的事）同理。
                // 真正**挡住事**的两个地方本来就会报，而且是在该报的时候报：
                //   · Unity 侧：会话的中继会写"影子 Hub 还是空的 ⇒ 写手没被调用，或条目是空的"；
                //   · Warudo 侧：`HoFaceController` 的状态里会点名"影子 rig 上没有 HoFaceSemanticHub"。
                // 所以这里只是"没事可做"，静静地走开。
                return;
            }

            current = animator;
            if (lookup == null) lookup = Lookup;
            if (!parametersCached) CacheParameters(animator);
            CacheExpressions();

            // 波浪类函数（`triangle` 之类）按帧推进 —— 表达式求值器用这个静态量当"现在第几帧"。
            HoFaceExpression.Frame = Time.frameCount;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null) continue;

                int index = hub.ClaimSlot(entry.slot);
                if (index < 0)
                {
                    ReportOnce("empty-slot-" + i, "第 " + i + " 条的语义名是空的 ⇒ 这个值没有名字，写不进去（也没法被下游认领）。");
                    continue;
                }

                HoFaceExpression expression = parsed != null && i < parsed.Length ? parsed[i] : null;
                hub.values[index] = expression != null ? expression.Evaluate(lookup) : 0f;
            }
        }

        /// <summary>表达式里的变量：先当 Animator 参数读；不是参数就当 0（并**报一次**，不静默）。</summary>
        private float Lookup(string name)
        {
            if (string.IsNullOrEmpty(name) || current == null) return 0f;

            int at = IndexOfParameter(name);
            if (at < 0)
            {
                ReportOnce("var-" + name, "表达式里的 `" + name + "` 不是这个控制器的参数（也不是任何已知的内置函数）⇒ 一律当 0。");
                return 0f;
            }

            switch (parameterKinds[at])
            {
                case AnimatorControllerParameterType.Float: return current.GetFloat(name);
                case AnimatorControllerParameterType.Int: return current.GetInteger(name);
                case AnimatorControllerParameterType.Bool: return current.GetBool(name) ? 1f : 0f;
                default: return 0f;   // Trigger 读不出"当前值"，当 0
            }
        }

        private int IndexOfParameter(string name)
        {
            if (parameterNames == null) return -1;
            for (int i = 0; i < parameterNames.Length; i++)
                if (string.Equals(parameterNames[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// 把控制器的参数名与类型抄一份。**必须抄**：`Animator.GetFloat` 碰到不存在的名字会**打错误日志**，
        /// 而表达式里写错一个名字是常事 —— 我们宁可自己报一句清楚的中文，也不要每帧刷 Unity 的红字。
        /// </summary>
        private void CacheParameters(Animator animator)
        {
            parametersCached = true;
            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters == null) return;

            parameterNames = new string[parameters.Length];
            parameterKinds = new AnimatorControllerParameterType[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameterNames[i] = parameters[i].name;
                parameterKinds[i] = parameters[i].type;
            }
        }

        /// <summary>解析条目里的表达式（原文没变就不重解析）。解析失败的那条**每帧写 0**，但只报一次。</summary>
        private void CacheExpressions()
        {
            if (parsed == null || parsed.Length != entries.Count)
            {
                parsed = new HoFaceExpression[entries.Count];
                parsedText = new string[entries.Count];
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                string text = entry != null ? entry.expression : null;
                if (string.Equals(parsedText[i], text, StringComparison.Ordinal)) continue;

                parsedText[i] = text;
                parsed[i] = null;
                if (string.IsNullOrEmpty(text)) continue;

                HoFaceExpression expression;
                string error;
                if (HoFaceExpression.TryParse(text, out expression, out error)) parsed[i] = expression;
                else ReportOnce("expr-" + i, "第 " + i + " 条的表达式解析失败（这一格每帧写 0）：" + error);
            }
        }

        /// <summary>同一条消息只报一次（`Debug.LogWarning`），别每帧刷屏。</summary>
        private void ReportOnce(string key, string message)
        {
            if (reported.Contains(key)) return;
            if (reported.Count >= 16) return;      // 报够了就闭嘴（别把日志淹了）
            reported.Add(key);
            Debug.LogWarning("[Ho 面捕] 语义写手「" + name + "」：" + message);
        }
    }
}
