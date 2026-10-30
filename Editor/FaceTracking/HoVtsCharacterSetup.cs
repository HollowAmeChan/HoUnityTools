using System;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>Explicit authoring action; input adapters and debuggers never create components.</summary>
    public static class HoVtsCharacterSetup
    {
        [MenuItem("GameObject/HoUnityTools/创建 VTS 角色组件", false, 30)]
        [MenuItem("HoUnityTools/面捕/为选中角色创建 VTS 组件", false, 5)]
        private static void CreateFromSelection()
        {
            try
            {
                var controller = Create(Selection.activeGameObject);
                Selection.activeGameObject = controller.gameObject;
                EditorGUIUtility.PingObject(controller);
            }
            catch (Exception error) { Debug.LogError("[Ho VTS] " + error.Message); }
        }

        public static HoVtsController Create(GameObject root)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请在编辑模式配置角色组件。");
            if (root == null || EditorUtility.IsPersistent(root) || !root.scene.IsValid())
                throw new InvalidOperationException("请在场景或预制件编辑模式中选择角色根物体。");
            if (root.GetComponentInChildren<HoVtsParameterReceiver>(true) != null
                || root.GetComponentInChildren<HoVtsController>(true) != null || root.GetComponentInChildren<HoVtsLookAt>(true) != null)
                throw new InvalidOperationException("所选对象下已有 VTS 组件，请编辑现有组件。");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("创建 VTS 角色组件");
            try
            {
                var host = new GameObject("HoVTS");
                Undo.RegisterCreatedObjectUndo(host, "创建 VTS 组件物体");
                Undo.SetTransformParent(host.transform, root.transform, "挂到角色根物体");
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;
                var receiver = Undo.AddComponent<HoVtsParameterReceiver>(host);
                var controller = Undo.AddComponent<HoVtsController>(host);
                controller.ParameterReceiver = receiver;
                controller.OutputRoot = root.transform;
                var pose = Undo.AddComponent<HoVtsLookAt>(host);
                pose.ParameterReceiver = receiver;
                pose.ReferenceFrame = root.transform;
                pose.AutoRun = false;
                pose.enabled = false;
                var animator = root.GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    pose.UpperBody = animator.GetBoneTransform(HumanBodyBones.Chest)
                        ?? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                        ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                    pose.Head = animator.GetBoneTransform(HumanBodyBones.Head);
                    pose.LeftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                    pose.RightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
                }
                EditorUtility.SetDirty(receiver); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(pose);
                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.CollapseUndoOperations(group);
                return controller;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
    }
}
