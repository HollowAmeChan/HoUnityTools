using System;
using Hollow.HoUnityTools.Armatures;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollow.HoUnityTools.Editor.Armatures
{
    [InitializeOnLoad]
    internal sealed class CombineArmatureSceneProcessor : IProcessSceneWithReport
    {
        static CombineArmatureSceneProcessor()
        {
            EditorApplication.playModeStateChanged += CheckSceneReload;
        }

        private static void CheckSceneReload(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || !EditorSettings.enterPlayModeOptionsEnabled ||
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0) return;
            foreach (var config in Resources.FindObjectsOfTypeAll<CombineArmature>())
            {
                if (config == null || !config.enabled || !config.processAutomatically ||
                    EditorUtility.IsPersistent(config) || !config.gameObject.scene.IsValid() ||
                    !config.gameObject.scene.isLoaded || EditorSceneManagerIsPreview(config.gameObject.scene)) continue;
                Debug.LogError("[CombineArmature] 自动装配需要启用 Scene Reload，以便退出 Play 时恢复源层级。" +
                    "请开启场景重载，或关闭组件自动处理并使用烘焙副本。", config);
                EditorApplication.isPlaying = false;
                return;
            }
        }

        private static bool EditorSceneManagerIsPreview(Scene scene)
        {
            return UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene);
        }

        // Unity invokes this on the play-mode scene copy and on built scenes. Use the provided
        // scene, not GetActiveScene: additive scene processing need not target the active scene.
        // AssetBundle/prefab exporters must explicitly process their own disposable prefab copy.
        public int callbackOrder => -10000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && report == null) return;
            if (!scene.IsValid()) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                try
                {
                    CombineArmatureProcessor.ProcessHierarchy(root, true);
                }
                catch (Exception exception)
                {
                    if (report != null) throw new BuildFailedException(exception);
                    Debug.LogException(exception, root);
                    EditorApplication.isPlaying = false;
                }
            }
        }
    }
}
