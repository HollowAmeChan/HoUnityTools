#if UNITY_EDITOR
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdAnimation))]
    internal sealed class HoVmdAnimationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            HoVmdInspectorGUI.DrawSummary((HoVmdAnimation)target);
            EditorGUILayout.HelpBox("这里是导入结果预览。角色绑定和表情检查在上方 VMD 导入面板中操作。", MessageType.Info);
        }
    }
}
#endif
