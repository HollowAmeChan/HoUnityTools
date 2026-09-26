using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 观察台的 Inspector：**一个字段（要观察的 controller）** + **一行"现在到底在跟谁"的读数** + 一段用法说明。
    ///
    /// 以前这里还有一个「跟随的对象」（跟随别的 Animator）：用户不要那条路 ——
    /// **参数值的来源只有一个**：正在生效的面捕会话（`HoFaceShadowLink.Active`）。
    ///
    /// 为什么加那行读数（2026-09-27 用户问"我点的到底是运行中的还是资产里的、多个角色是谁的"）：
    /// 绑定关系以前只写在 `Debug.Log` 里 ⇒ 面板上看不出来。现在 Play 模式下 Inspector 直接写清
    /// 「跟谁 / 跑的哪份 controller / 抄了几个参数」，编辑模式与没有会话时也各写一句。
    /// </summary>
    [CustomEditor(typeof(HoFaceBlendTreePeek))]
    internal sealed class HoFaceBlendTreePeekEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"),
                new GUIContent("controller",
                    "要观察的那份控制器（填一个就行）。\n"
                    + "留空时退一步用来源正在跑的那个；想对比「资产里的树」与「运行中的树」时填资产里的那份。"));
            serializedObject.ApplyModifiedProperties();

            // ── 现在在跟谁（只读）────────────────────────────────────────────────
            var peek = (HoFaceBlendTreePeek)target;
            EditorGUILayout.Space();
            if (!Application.isPlaying)
            {
                HoConstraintEditorControls.Caption(
                    "编辑模式：本组件什么都不做。进 Play 并把面捕驱动起来后，这里会写清它在跟随谁。");
            }
            else if (peek.BoundSource == null)
            {
                HoConstraintEditorControls.Caption(
                    HoFaceBlendTreePeek.HasLiveSession
                        ? "正在绑定…（这一帧还没抄）"
                        : "还没有正在生效的面捕会话：去「面捕 · 调试面板」的「对象」段点「开始驱动」。");
            }
            else
            {
                bool guessed = peek.controller == null;
                HoConstraintEditorControls.Caption(
                    "跟随：" + peek.BoundSource.name
                    + " · 跑的是：" + (peek.BoundController != null ? peek.BoundController.name : "（没有 controller）")
                    + (guessed ? "（你没填 ⇒ 用了来源那份）" : "（你填的那份）")
                    + " · 抄 " + peek.CopiedParameterCount + " 个 float 参数");
                HoConstraintEditorControls.Caption(
                    "单位置：多角色同时调试时 `HoFaceShadowLink` 只留**最后登记**的那个影子（`HoFaceShadowLink.cs:13`）——"
                    + "所以这里看的永远是最后开始驱动的那个角色的树。");
            }
            if (Application.isPlaying) Repaint();   // 读数要动起来

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "把正在生效的面捕会话（影子台）的参数每帧抄到这个 Animator 上，"
                + "于是能在 Animator 窗口里看见正在跑的混合树 —— 编辑器自带的混合树视图看不到这些，"
                + "因为真正的影子台是隐藏对象。\n\n"
                + "玩法：填一个 controller → Play 并把面捕驱动起来 → 在 Hierarchy 里选中本物体 → 打开 Animator 窗口。\n"
                + "本物体不需要挂 Animator，运行时自己补一个（补的那个不在 Inspector 上占一行）。\n"
                + "⚠️ 在 Project 里**点 controller 资产**时，Animator 窗口显示的是**资产本身**（结构 + 参数默认值），"
                + "不是任何角色的运行状态；要看运行中的值，只能选中**跑着它的实例**（我们这个链里就是本物体）。\n"
                + "它是无损的：这个 Animator 不驱动任何渲染器、也不读输出，姿势求值出来没有去处。",
                MessageType.None);
        }
    }
}
