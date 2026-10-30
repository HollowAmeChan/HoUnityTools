#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.MmdMotion;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdAnimation))]
    internal sealed class HoVmdAnimationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var animation=(HoVmdAnimation)target;
            EditorGUILayout.LabelField("VMD model", animation.modelName);
            EditorGUILayout.LabelField("Signature", animation.signature);
            EditorGUILayout.LabelField("Bone frames", animation.boneFrames.Count.ToString());
            EditorGUILayout.LabelField("Morph frames", animation.morphFrames.Count.ToString());
            EditorGUILayout.LabelField("Camera frames", animation.cameraFrames.Count.ToString());
            EditorGUILayout.LabelField("Light frames", animation.lightFrames.Count.ToString());
            EditorGUILayout.LabelField("Show/IK frames", animation.showIkFrames.Count.ToString());
            EditorGUILayout.LabelField("Last bone frame", animation.LastBoneFrame.ToString());
            EditorGUILayout.LabelField("Last camera frame", animation.LastCameraFrame.ToString());
            EditorGUILayout.HelpBox("将动作绑定到模型时，先确认骨骼名称、形态键和每个 IK 控制器的动作级开关。", MessageType.Info);
        }
    }
}
#endif
