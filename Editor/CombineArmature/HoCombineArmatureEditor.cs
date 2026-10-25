using System;
using Hollow.HoUnityTools.Armatures;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Armatures
{
    [CustomEditor(typeof(HoCombineArmature))]
    internal sealed class HoCombineArmatureEditor : UnityEditor.Editor
    {
        private string validation;
        private MessageType validationType;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(new GUIContent("衣服骨架根（挂载处）", "组件自身所在的骨架根；不要挂在包含网格和 Animator 的 FBX 模型根上。"),
                    ((HoCombineArmature)target).transform, typeof(Transform), true);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetArmature"), new GUIContent("本体骨架根"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("characterRoot"), new GUIContent("角色根（可自动）"));
            SerializedProperty mode = serializedObject.FindProperty("mode");
            mode.enumValueIndex = EditorGUILayout.Popup("装配方式", mode.enumValueIndex,
                new[] { "直接合并", "保留层级 / 原生位置旋转约束" });
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bonePrefix"), new GUIContent("衣服骨骼前缀"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("boneSuffix"), new GUIContent("衣服骨骼后缀"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("processAutomatically"), new GUIContent("Play / 场景构建时处理"));
            if (EditorGUI.EndChangeCheck()) validation = null;
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("挂在衣服骨架根，目标指向本体对应根；角色根选同时包含两套骨架和网格的共同父对象。网格、换装开关和 Animator 放在衣服骨架子树外。" +
                "仅处理 Play / 构建 / 烘焙副本，不改源 FBX 或动画资产。", MessageType.Info);
            var config = (HoCombineArmature)target;
            if (config.mode == HoCombineArmatureMode.Constraints)
                EditorGUILayout.HelpBox("生成 ParentConstraint，跟随位置和旋转，保留骨架及所有原路径。" +
                    "不复制骨骼缩放；需要动态缩放时请用直接合并。目标由 LateUpdate/物理等后续阶段改写时，需要验证求值顺序。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("检查骨骼匹配与引用"))
                {
                    var report = HoCombineArmatureProcessor.Validate(config);
                    validation = report.ToString();
                    validationType = report.IsValid ? MessageType.Info : MessageType.Error;
                }
                if (GUILayout.Button("生成已装配的角色副本"))
                {
                    Transform root = HoCombineArmatureProcessor.GetCharacterRoot(config);
                    if (root == null)
                    {
                        validation = "请先指定本体骨架根与完整角色根。";
                        validationType = MessageType.Error;
                    }
                    else
                    {
                        try
                        {
                            GameObject copy = HoCombineArmatureProcessor.BakeCopy(root.gameObject);
                            Undo.RegisterCreatedObjectUndo(copy, "Bake HoCombineArmature copy");
                            Selection.activeGameObject = copy;
                            EditorGUIUtility.PingObject(copy);
                        }
                        catch (Exception exception)
                        {
                            validation = exception.Message;
                            validationType = MessageType.Error;
                            Debug.LogException(exception, config);
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(validation)) EditorGUILayout.HelpBox(validation, validationType);
        }
    }
}
