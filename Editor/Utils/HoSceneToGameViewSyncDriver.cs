using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    [InitializeOnLoad]
    internal static class HoSceneToGameViewSyncDriver
    {
        private static readonly HashSet<HoSceneToGameViewSync> s_SyncComponents =
            new HashSet<HoSceneToGameViewSync>();
        private static bool s_RefreshQueued;
        private static bool s_UpdateSubscribed;

        internal enum SyncResult
        {
            Synced,
            MissingComponent,
            MissingSceneView,
            MissingCamera,
            NoChannelsSelected
        }

        static HoSceneToGameViewSyncDriver()
        {
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            RefreshRegistry();
        }

        internal static SyncResult SyncNow(HoSceneToGameViewSync sync)
        {
            if (sync == null)
                return SyncResult.MissingComponent;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                return SyncResult.MissingSceneView;

            Camera targetCamera = sync.AttachedCamera;
            if (targetCamera == null)
                return SyncResult.MissingCamera;

            if (!HasAnySyncChannel(sync))
                return SyncResult.NoChannelsSelected;

            Undo.RecordObject(targetCamera.transform, "Snap Scene View To Camera");
            Undo.RecordObject(targetCamera, "Snap Scene View To Camera");

            ApplySceneCamera(sync, targetCamera, sceneView.camera, true);
            MarkCameraDirty(targetCamera);
            return SyncResult.Synced;
        }

        /// <summary>反向吸附：把目标 Camera 的视角搬到最近活动的 Scene 视图上。</summary>
        internal static SyncResult SnapSceneViewToCamera(HoSceneToGameViewSync sync)
        {
            if (sync == null)
                return SyncResult.MissingComponent;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
                return SyncResult.MissingSceneView;

            Camera sourceCamera = sync.AttachedCamera;
            if (sourceCamera == null)
                return SyncResult.MissingCamera;

            if (!HasAnySyncChannel(sync))
                return SyncResult.NoChannelsSelected;

            ApplyCameraToSceneView(sync, sourceCamera, sceneView);

            sceneView.Repaint();
            return SyncResult.Synced;
        }

        private static void ApplyCameraToSceneView(HoSceneToGameViewSync sync, Camera sourceCamera, SceneView sceneView)
        {
            Transform sourceTransform = sourceCamera.transform;

            if (sync.syncPosition || sync.syncRotation)
            {
                // Scene 视图相机落在 pivot 沿视线后退 cameraDistance 的位置，
                // 所以先把 pivot 推到相机前方，最终取景相机正好压在相机 Transform 上。
                float distance = Mathf.Max(0.01f, sceneView.cameraDistance);
                if (sync.syncPosition)
                    sceneView.pivot = sourceTransform.position + sourceTransform.forward * distance;

                if (sync.syncRotation)
                    sceneView.rotation = sourceTransform.rotation;
            }

            SceneView.CameraSettings settings = sceneView.cameraSettings;
            bool settingsChanged = false;

            if (sync.syncFOV)
            {
                // 透视/正交属于"视野"本身，不跟着切换的话取景对不上。
                sceneView.orthographic = sourceCamera.orthographic;
                if (sourceCamera.orthographic)
                {
                    sceneView.size = Mathf.Max(0.01f, sourceCamera.orthographicSize);
                }
                else
                {
                    settings.fieldOfView = Mathf.Clamp(sourceCamera.fieldOfView, 1f, 179f);
                    settingsChanged = true;
                }
            }

            if (sync.syncClippingPlanes)
            {
                // dynamicClip 打开时会覆盖 near/far，必须先关掉才写得进去。
                settings.dynamicClip = false;
                settings.nearClip = Mathf.Max(0.01f, sourceCamera.nearClipPlane);
                settings.farClip = Mathf.Max(0.02f, sourceCamera.farClipPlane);
                settingsChanged = true;
            }

            if (settingsChanged)
                sceneView.cameraSettings = settings;
        }

        private static void OnEditorUpdate()
        {
            if (s_SyncComponents.Count == 0)
            {
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;
            Camera sceneCamera = sceneView == null ? null : sceneView.camera;

            List<HoSceneToGameViewSync> staleComponents = null;
            foreach (HoSceneToGameViewSync sync in s_SyncComponents)
            {
                if (sync == null)
                {
                    if (staleComponents == null)
                        staleComponents = new List<HoSceneToGameViewSync>();

                    staleComponents.Add(sync);
                    continue;
                }

                UpdateSync(sync, sceneCamera);
            }

            if (staleComponents != null)
            {
                foreach (HoSceneToGameViewSync stale in staleComponents)
                    s_SyncComponents.Remove(stale);

                UpdateEditorUpdateSubscription();
            }
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            QueueRefresh();
        }

        private static void OnSceneClosed(UnityEngine.SceneManagement.Scene scene)
        {
            QueueRefresh();
        }

        private static void QueueRefresh()
        {
            if (s_RefreshQueued)
                return;

            s_RefreshQueued = true;
            EditorApplication.delayCall += RefreshRegistry;
        }

        private static void RefreshRegistry()
        {
            s_RefreshQueued = false;
            s_SyncComponents.Clear();

            foreach (HoSceneToGameViewSync sync in Object.FindObjectsByType<HoSceneToGameViewSync>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (sync != null)
                    s_SyncComponents.Add(sync);
            }

            UpdateEditorUpdateSubscription();
        }

        private static void UpdateEditorUpdateSubscription()
        {
            if (s_SyncComponents.Count > 0 && !s_UpdateSubscribed)
            {
                EditorApplication.update += OnEditorUpdate;
                s_UpdateSubscribed = true;
            }
            else if (s_SyncComponents.Count == 0 && s_UpdateSubscribed)
            {
                EditorApplication.update -= OnEditorUpdate;
                s_UpdateSubscribed = false;
            }
        }

        private static void UpdateSync(HoSceneToGameViewSync sync, Camera sceneCamera)
        {
            if (sync == null || !sync.isActiveAndEnabled)
                return;

            if (!sync.enableSync)
                return;

            if (EditorApplication.isPlaying && !sync.syncInPlayMode)
                return;

            float frequency = Mathf.Max(1f, sync.syncFrequency);
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - sync.LastSyncTime < 1f / frequency)
                return;

            sync.LastSyncTime = currentTime;

            if (sceneCamera == null)
                return;

            Camera targetCamera = sync.AttachedCamera;
            if (targetCamera == null)
                return;

            if (!HasAnySyncChannel(sync))
                return;

            if (ApplySceneCamera(sync, targetCamera, sceneCamera, false) && !EditorApplication.isPlaying)
                MarkCameraDirty(targetCamera);
        }

        private static bool HasAnySyncChannel(HoSceneToGameViewSync sync)
        {
            return sync.syncPosition || sync.syncRotation || sync.syncFOV || sync.syncClippingPlanes;
        }

        private static bool ApplySceneCamera(HoSceneToGameViewSync sync, Camera targetCamera, Camera sceneCamera, bool force)
        {
            bool positionChanged = force
                || sync.LastScenePosition != sceneCamera.transform.position
                || targetCamera.transform.position != sceneCamera.transform.position;
            bool rotationChanged = force
                || sync.LastSceneRotation != sceneCamera.transform.rotation
                || targetCamera.transform.rotation != sceneCamera.transform.rotation;
            bool fovChanged = force
                || !Mathf.Approximately(sync.LastSceneFOV, sceneCamera.fieldOfView)
                || !Mathf.Approximately(targetCamera.fieldOfView, sceneCamera.fieldOfView);
            bool clippingPlanesChanged = force
                || !Mathf.Approximately(sync.LastSceneNearClipPlane, sceneCamera.nearClipPlane)
                || !Mathf.Approximately(sync.LastSceneFarClipPlane, sceneCamera.farClipPlane)
                || !Mathf.Approximately(targetCamera.nearClipPlane, sceneCamera.nearClipPlane)
                || !Mathf.Approximately(targetCamera.farClipPlane, sceneCamera.farClipPlane);
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

            if (sync.syncClippingPlanes && clippingPlanesChanged)
            {
                targetCamera.nearClipPlane = sceneCamera.nearClipPlane;
                targetCamera.farClipPlane = sceneCamera.farClipPlane;
                sync.LastSceneNearClipPlane = sceneCamera.nearClipPlane;
                sync.LastSceneFarClipPlane = sceneCamera.farClipPlane;
                changed = true;
            }

            return changed;
        }

        private static void MarkCameraDirty(Camera camera)
        {
            EditorUtility.SetDirty(camera.transform);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.gameObject);
            if (camera.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
