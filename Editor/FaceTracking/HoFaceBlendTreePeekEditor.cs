using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 观察台的 Inspector：只有两个字段，所以这里只做两件事 —— **把字段名汉化**、把用法说清楚。
    /// （字段名默认是 `sourceAnimator` / `controller` 那种驼峰英文，长解释一律放 tooltip。）
    /// </summary>
    [CustomEditor(typeof(HoFaceBlendTreePeek))]
    internal sealed class HoFaceBlendTreePeekEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceAnimator"),
                new GUIContent("跟随的对象", "留空 = 跟随当前正在生效的面捕会话（影子台）。也可以拖别的 Animator 进来观察。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"),
                new GUIContent("观察用 controller", "留空最省事：直接用来源正在跑的那一个。"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "把正在生效的面捕会话（影子台）的参数每帧抄到这个 Animator 上，"
                + "于是能在 Animator 窗口里看见正在跑的混合树 —— 编辑器自带的混合树视图看不到这些，"
                + "因为真正的影子台是隐藏对象。\n\n"
                + "玩法：Play 并把面捕驱动起来 → 在 Hierarchy 里选中本物体 → 打开 Animator 窗口。\n"
                + "本物体不需要挂 Animator，运行时自己补一个。\n"
                + "它是无损的：这个 Animator 不驱动任何渲染器、也不读输出，姿势求值出来没有去处。",
                MessageType.None);
        }
    }
}
