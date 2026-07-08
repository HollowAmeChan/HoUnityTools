using Hollow.HoUnityTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    internal static class HoSceneToGameViewSyncMenu
    {
        [MenuItem("GameObject/HoUnityTools/Ho Scene To Game View Sync Camera", false, 10)]
        private static void CreateSyncCamera(MenuCommand menuCommand)
        {
            GameObject cameraObject = new GameObject("Ho Scene To Game Sync Camera");
            GameObjectUtility.SetParentAndAlign(cameraObject, menuCommand.context as GameObject);

            Camera camera = cameraObject.AddComponent<Camera>();
            HoSceneToGameViewSync sync = cameraObject.AddComponent<HoSceneToGameViewSync>();
            sync.enableSync = false;

            if (GameObject.FindGameObjectWithTag("MainCamera") == null)
                camera.tag = "MainCamera";

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                cameraObject.transform.SetPositionAndRotation(sceneView.camera.transform.position, sceneView.camera.transform.rotation);
                camera.fieldOfView = sceneView.camera.fieldOfView;
                camera.nearClipPlane = sceneView.camera.nearClipPlane;
                camera.farClipPlane = sceneView.camera.farClipPlane;
            }

            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Scene To Game View Sync Camera");
            Selection.activeGameObject = cameraObject;
            EditorSceneManager.MarkSceneDirty(cameraObject.scene);

            Debug.Log("已创建 Scene 视图吸附相机，自动同步默认关闭。");
        }
    }
}
