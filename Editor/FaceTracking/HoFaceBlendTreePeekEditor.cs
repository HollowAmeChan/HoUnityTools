using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 观察台的 Inspector：**只有一个字段（要观察的 controller）** + 一段用法说明（2026-09-27 简化）。
    ///
    /// 以前这里还有一个「跟随的对象」（跟随别的 Animator）：用户不要那条路 ——
    /// **参数值的来源只有一个**：正在生效的面捕会话（`HoFaceShadowLink.Active`），
    /// 所以面板上只留"填一个 controller"这件事。
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

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "把正在生效的面捕会话（影子台）的参数每帧抄到这个 Animator 上，"
                + "于是能在 Animator 窗口里看见正在跑的混合树 —— 编辑器自带的混合树视图看不到这些，"
                + "因为真正的影子台是隐藏对象。\n\n"
                + "玩法：填一个 controller → Play 并把面捕驱动起来 → 在 Hierarchy 里选中本物体 → 打开 Animator 窗口。\n"
                + "本物体不需要挂 Animator，运行时自己补一个（补的那个不在 Inspector 上占一行）。\n"
                + "它是无损的：这个 Animator 不驱动任何渲染器、也不读输出，姿势求值出来没有去处。",
                MessageType.None);
        }
    }
}
