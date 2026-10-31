#if UNITY_EDITOR
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdCameraPlayer))]
    internal sealed class HoVmdCameraPlayerEditor : UnityEditor.Editor
    {
        private bool showAdvanced;
        private HoVmdCameraPlayer Player => (HoVmdCameraPlayer)target;
        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("VMD 摄像机播放", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("将此组件添加到 Camera。指定含摄像机轨道的 VMD，进入 Play 后播放；不需要配置角色播放器。", MessageType.Info);
            EditorGUILayout.PropertyField(P("animation"), new GUIContent("镜头 VMD"));
            EditorGUILayout.PropertyField(P("playOnEnable"), new GUIContent("运行时自动播放"));
            EditorGUILayout.PropertyField(P("loop"), new GUIContent("循环"));
            EditorGUILayout.PropertyField(P("frameRate"), new GUIContent("帧率"));
            EditorGUILayout.PropertyField(P("currentFrame"), new GUIContent("当前帧"));
            EditorGUILayout.PropertyField(P("mmdToUnityScale"), new GUIContent("场景比例"));
            EditorGUILayout.PropertyField(P("snapAdjacentFrames"), new GUIContent("相邻关键帧按跳切处理"));

            EditorGUILayout.Space(4);
            showAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvanced, "高级");
            if (showAdvanced)
            {
                EditorGUILayout.PropertyField(P("cameraTarget"), new GUIContent("轨道中心（可选）",
                    "留空时只在内部计算轨道中心；指定 Transform 可在运行时观察中心位置。"));
                EditorGUILayout.PropertyField(P("orthographicSizeMultiplier"), new GUIContent("正交缩放倍率"));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            serializedObject.ApplyModifiedProperties();

            if (Player.Animation == null)
                EditorGUILayout.HelpBox("请指定镜头 VMD 资产。", MessageType.Warning);
            else if (Player.Animation.cameraFrames.Count == 0)
                EditorGUILayout.HelpBox("所选 VMD 不包含摄像机轨道。", MessageType.Warning);

            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Player.Playing ? "暂停" : "播放"))
                        Player.Playing = !Player.Playing;
                    if (GUILayout.Button("从头播放"))
                    {
                        Player.CurrentFrame = 0.0f;
                        Player.Playing = true;
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("摄像机只在 Play 模式下受 VMD 控制。", EditorStyles.miniLabel);
            }
        }
    }
}
#endif
