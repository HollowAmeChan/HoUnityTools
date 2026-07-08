using Hollow.HoUnityTools;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    [CustomEditor(typeof(HoSceneToGameViewSync))]
    internal sealed class HoSceneToGameViewSyncEditor : UnityEditor.Editor
    {
        private GUIStyle _primaryButtonStyle;
        private string _lastSyncMessage;
        private MessageType _lastSyncMessageType = MessageType.Info;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty autoSync = serializedObject.FindProperty("enableSync");

            DrawHeader(autoSync);
            EditorGUILayout.Space(6f);
            DrawManualSync();
            EditorGUILayout.Space(6f);
            DrawSyncChannels();
            EditorGUILayout.Space(6f);
            DrawAutoSync(autoSync);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader(SerializedProperty autoSync)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene 视图吸附相机", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("此组件只控制当前 GameObject 上的 Camera。日常使用建议手动吸附，需要持续跟随时再开启自动同步。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(3f);
                DrawState(autoSync);
            }
        }

        private void DrawState(SerializedProperty autoSync)
        {
            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox($"已选择 {targets.Length} 个同步组件。每个组件只会修改自己所在 GameObject 上的 Camera。", MessageType.Info);
                return;
            }

            HoSceneToGameViewSync sync = target as HoSceneToGameViewSync;
            if (sync == null)
            {
                EditorGUILayout.HelpBox("未找到同步组件。", MessageType.Error);
                return;
            }

            Camera camera = sync.AttachedCamera;
            if (camera == null)
            {
                EditorGUILayout.HelpBox("当前 GameObject 上没有 Camera，无法吸附。", MessageType.Error);
                return;
            }

            if (!sync.gameObject.activeInHierarchy)
            {
                EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。GameObject 未激活，自动同步暂停；仍可手动吸附。", MessageType.Warning);
                return;
            }

            if (!sync.enabled)
            {
                EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。组件未勾选，自动同步暂停；仍可手动吸附。", MessageType.None);
                return;
            }

            if (!autoSync.hasMultipleDifferentValues && !autoSync.boolValue)
            {
                EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。当前为手动吸附模式，没有后台自动同步。", MessageType.Info);
                return;
            }

            if (EditorApplication.isPlaying && !sync.syncInPlayMode)
            {
                EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。播放模式下自动同步已按设置暂停。", MessageType.Warning);
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。未找到可用的 Scene 视图相机。", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox($"目标 Camera：{camera.name}。自动同步运行中。", MessageType.Info);
        }

        private void DrawManualSync()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("手动吸附", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("把最近活动的 Scene 视图相机应用到此 Camera。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);

                if (GUILayout.Button(new GUIContent("吸附当前 Scene 视图", "将当前 Scene 视图相机的位置、旋转、FOV 和裁切平面按下方选项应用到此 Camera。"), PrimaryButtonStyle))
                    SyncSelectedTargets();

                if (!string.IsNullOrEmpty(_lastSyncMessage))
                    EditorGUILayout.HelpBox(_lastSyncMessage, _lastSyncMessageType);
            }
        }

        private void DrawSyncChannels()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("吸附内容", EditorStyles.boldLabel);
                DrawToggleLeft(serializedObject.FindProperty("syncPosition"), new GUIContent("位置", "同步相机位置"));
                DrawToggleLeft(serializedObject.FindProperty("syncRotation"), new GUIContent("旋转", "同步相机旋转"));
                DrawToggleLeft(serializedObject.FindProperty("syncFOV"), new GUIContent("视野 FOV", "同步相机视野"));
                DrawToggleLeft(serializedObject.FindProperty("syncClippingPlanes"), new GUIContent("近远裁切", "同步相机近远裁切平面"));
            }
        }

        private void DrawAutoSync(SerializedProperty autoSync)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("自动同步", EditorStyles.boldLabel);
                DrawToggleLeft(autoSync, new GUIContent("持续跟随 Scene 视图", "开启后，只有在组件启用且 GameObject 激活时才会持续同步。"));

                if (autoSync.hasMultipleDifferentValues || autoSync.boolValue)
                {
                    EditorGUILayout.Space(3f);
                    DrawToggleLeft(serializedObject.FindProperty("syncInPlayMode"), new GUIContent("播放模式下也自动同步", "在 Play Mode 中继续自动跟随 Scene 视图。"));
                    EditorGUILayout.Slider(serializedObject.FindProperty("syncFrequency"), 1f, 60f, new GUIContent("自动频率", "每秒自动同步次数"));
                }
                else
                {
                    EditorGUILayout.LabelField("关闭时不会有后台同步。手动吸附按钮始终可用。", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void SyncSelectedTargets()
        {
            serializedObject.ApplyModifiedProperties();

            int syncedCount = 0;
            HoSceneToGameViewSyncDriver.SyncResult firstFailure = HoSceneToGameViewSyncDriver.SyncResult.Synced;

            foreach (Object selectedTarget in targets)
            {
                if (!(selectedTarget is HoSceneToGameViewSync sync))
                    continue;

                HoSceneToGameViewSyncDriver.SyncResult result = HoSceneToGameViewSyncDriver.SyncNow(sync);
                if (result == HoSceneToGameViewSyncDriver.SyncResult.Synced)
                {
                    syncedCount++;
                }
                else if (firstFailure == HoSceneToGameViewSyncDriver.SyncResult.Synced)
                {
                    firstFailure = result;
                }
            }

            if (syncedCount == targets.Length)
            {
                _lastSyncMessage = syncedCount == 1 ? "已吸附到当前 Scene 视图。" : $"已吸附 {syncedCount} 个 Camera。";
                _lastSyncMessageType = MessageType.Info;
            }
            else if (syncedCount > 0)
            {
                _lastSyncMessage = $"已吸附 {syncedCount}/{targets.Length} 个 Camera。未完成原因：{GetResultText(firstFailure)}";
                _lastSyncMessageType = MessageType.Warning;
            }
            else
            {
                _lastSyncMessage = GetResultText(firstFailure);
                _lastSyncMessageType = MessageType.Warning;
            }

            Repaint();
        }

        private static string GetResultText(HoSceneToGameViewSyncDriver.SyncResult result)
        {
            switch (result)
            {
                case HoSceneToGameViewSyncDriver.SyncResult.MissingSceneView:
                    return "未找到可用的 Scene 视图相机。";
                case HoSceneToGameViewSyncDriver.SyncResult.MissingCamera:
                    return "当前 GameObject 上没有 Camera。";
                case HoSceneToGameViewSyncDriver.SyncResult.NoChannelsSelected:
                    return "至少需要勾选一个吸附内容。";
                case HoSceneToGameViewSyncDriver.SyncResult.MissingComponent:
                    return "未找到同步组件。";
                default:
                    return "吸附失败。";
            }
        }

        private void DrawToggleLeft(SerializedProperty property, GUIContent content)
        {
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.ToggleLeft(content, property.boolValue);
            if (EditorGUI.EndChangeCheck())
                property.boolValue = value;
            EditorGUI.showMixedValue = false;
        }

        private GUIStyle PrimaryButtonStyle
        {
            get
            {
                if (_primaryButtonStyle == null)
                {
                    _primaryButtonStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontStyle = FontStyle.Bold,
                        fixedHeight = 34f
                    };
                }

                return _primaryButtonStyle;
            }
        }
    }
}
