#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.HoCamera;

namespace Hollow.HoUnityTools.Editor.HoCamera
{
    [CustomEditor(typeof(HoCamera))]
    public sealed class HoCameraEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            var player = (HoCamera)target;
            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(player.Asset == null))
            {
                if (GUILayout.Button("刷新 / 应用首帧"))
                {
                    Undo.RecordObject(player.TargetCamera, "Apply HoCamera snapshot");
                    player.ApplySnapshot();
                    EditorUtility.SetDirty(player.TargetCamera);
                }
                if (GUILayout.Button("应用当前帧"))
                {
                    Undo.RecordObject(player.TargetCamera, "Apply HoCamera frame");
                    player.ApplyAtCurrentFrame();
                    EditorUtility.SetDirty(player.TargetCamera);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
