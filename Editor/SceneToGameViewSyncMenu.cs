using Hollow.HoUnityTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    internal static class SceneToGameViewSyncMenu
    {
        [MenuItem("GameObject/HoUnityTools/Scene To Game View Sync Camera", false, 10)]
        private static void CreateSyncCamera(MenuCommand menuCommand)
        {
            GameObject cameraObject = new GameObject("Scene To Game Sync Camera");
            GameObjectUtility.SetParentAndAlign(cameraObject, menuCommand.context as GameObject);

            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<SceneToGameViewSync>();

            if (GameObject.FindGameObjectWithTag("MainCamera") == null)
                camera.tag = "MainCamera";

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                cameraObject.transform.SetPositionAndRotation(sceneView.camera.transform.position, sceneView.camera.transform.rotation);
                camera.fieldOfView = sceneView.camera.fieldOfView;
            }

            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Scene To Game View Sync Camera");
            Selection.activeGameObject = cameraObject;
            EditorSceneManager.MarkSceneDirty(cameraObject.scene);

            Debug.Log("已创建Scene To Game View同步相机！");
        }
    }
}
