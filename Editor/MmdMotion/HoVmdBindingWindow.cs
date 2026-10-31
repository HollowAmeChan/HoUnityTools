#if UNITY_EDITOR
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    internal sealed class HoVmdBindingWindow : EditorWindow
    {
        private HoVmdAnimation animation;
        private readonly HoVmdBindingPanel binding = new HoVmdBindingPanel();
        private Vector2 scroll;
        public static void Open() => GetWindow<HoVmdBindingWindow>("VMD 轨道检查");
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            animation = (HoVmdAnimation)EditorGUILayout.ObjectField("VMD 动画", animation, typeof(HoVmdAnimation), false);
            binding.Draw(animation);
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
