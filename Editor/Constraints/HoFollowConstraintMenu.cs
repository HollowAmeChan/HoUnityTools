using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    internal static class HoFollowConstraintMenu
    {
        [MenuItem("GameObject/HoUnityTools/约束/跟随约束", false, 10)]
        private static void CreateFollowConstraint(MenuCommand menuCommand)
        {
            GameObject context = menuCommand.context as GameObject;
            GameObject targetObject = context != null ? context : Selection.activeGameObject;

            if (targetObject == null)
            {
                targetObject = new GameObject("Ho 跟随约束");
                GameObjectUtility.SetParentAndAlign(targetObject, context);
                Undo.RegisterCreatedObjectUndo(targetObject, "创建 Ho 跟随约束");
            }
            else
            {
                Undo.RecordObject(targetObject, "添加 Ho 跟随约束");
            }

            if (targetObject.GetComponent<HoFollowConstraint>() == null)
            {
                Undo.AddComponent<HoFollowConstraint>(targetObject);
            }

            Selection.activeGameObject = targetObject;
            EditorSceneManager.MarkSceneDirty(targetObject.scene);
        }
    }
}
