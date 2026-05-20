using Hollow.HoUnityTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    [InitializeOnLoad]
    internal static class SceneToGameViewSyncDriver
    {
        static SceneToGameViewSyncDriver()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        internal static void SyncNow(SceneToGameViewSync sync)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                return;

            Camera targetCamera = sync.TargetCamera != null ? sync.TargetCamera : sync.GetComponent<Camera>();
            if (targetCamera == null)
                return;

            Undo.RecordObject(targetCamera.transform, "Sync Scene View Camera");
            Undo.RecordObject(targetCamera, "Sync Scene View Camera");

            ApplySceneCamera(sync, targetCamera, sceneView.camera, true);
            MarkCameraDirty(targetCamera);
        }

        private static void OnEditorUpdate()
        {
            foreach (SceneToGameViewSync sync in Object.FindObjectsByType<SceneToGameViewSync>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                UpdateSync(sync);
            }
        }

        private static void UpdateSync(SceneToGameViewSync sync)
        {
            if (!sync.enableSync)
                return;

            if (EditorApplication.isPlaying && !sync.syncInPlayMode)
                return;

            float frequency = Mathf.Max(1f, sync.syncFrequency);
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - sync.LastSyncTime < 1f / frequency)
                return;

            sync.LastSyncTime = currentTime;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                return;

            Camera targetCamera = sync.TargetCamera != null ? sync.TargetCamera : sync.GetComponent<Camera>();
            if (targetCamera == null)
                return;

            if (ApplySceneCamera(sync, targetCamera, sceneView.camera, false) && !EditorApplication.isPlaying)
                MarkCameraDirty(targetCamera);
        }

        private static bool ApplySceneCamera(SceneToGameViewSync sync, Camera targetCamera, Camera sceneCamera, bool force)
        {
            bool positionChanged = force || sync.LastScenePosition != sceneCamera.transform.position;
            bool rotationChanged = force || sync.LastSceneRotation != sceneCamera.transform.rotation;
            bool fovChanged = force || !Mathf.Approximately(sync.LastSceneFOV, sceneCamera.fieldOfView);
            bool changed = false;

            if (sync.syncPosition && positionChanged)
            {
                targetCamera.transform.position = sceneCamera.transform.position;
                sync.LastScenePosition = sceneCamera.transform.position;
                changed = true;
            }

            if (sync.syncRotation && rotationChanged)
            {
                targetCamera.transform.rotation = sceneCamera.transform.rotation;
                sync.LastSceneRotation = sceneCamera.transform.rotation;
                changed = true;
            }

            if (sync.syncFOV && fovChanged)
            {
                targetCamera.fieldOfView = sceneCamera.fieldOfView;
                sync.LastSceneFOV = sceneCamera.fieldOfView;
                changed = true;
            }

            return changed;
        }

        private static void MarkCameraDirty(Camera camera)
        {
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.gameObject);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
