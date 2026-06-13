using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    internal static class HoFloatingConstraintMenu
    {
        [MenuItem("GameObject/HoUnityTools/约束/漂浮约束", false, 11)]
        private static void CreateFloatingConstraint(MenuCommand menuCommand)
        {
            GameObject context = menuCommand.context as GameObject;
            GameObject targetObject = context != null ? context : Selection.activeGameObject;

            if (targetObject == null)
            {
                targetObject = new GameObject("Ho 漂浮约束");
                GameObjectUtility.SetParentAndAlign(targetObject, context);
                Undo.RegisterCreatedObjectUndo(targetObject, "创建 Ho 漂浮约束");
            }
            else
            {
                Undo.RecordObject(targetObject, "添加 Ho 漂浮约束");
            }

            if (targetObject.GetComponent<HoFloatingConstraint>() == null)
            {
                Undo.AddComponent<HoFloatingConstraint>(targetObject);
            }

            Selection.activeGameObject = targetObject;
            EditorSceneManager.MarkSceneDirty(targetObject.scene);
        }
    }
}
