using System;
using Hollow.HoUnityTools.Armatures;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Armatures
{
    [CustomEditor(typeof(CombineArmature))]
    internal sealed class CombineArmatureEditor : UnityEditor.Editor
    {
        private string validation;
        private MessageType validationType;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
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
            EditorGUILayout.HelpBox("挂在衣服骨架根，目标指向本体对应根。网格、换装开关和 Animator 放在该子树外。" +
                "仅处理 Play / 构建 / 烘焙副本，不改源 FBX 或动画资产。", MessageType.Info);
            var config = (CombineArmature)target;
            if (config.mode == CombineArmatureMode.Constraints)
                EditorGUILayout.HelpBox("生成 ParentConstraint，跟随位置和旋转，保留骨架及所有原路径。" +
                    "不复制骨骼缩放；需要动态缩放时请用直接合并。目标由 LateUpdate/物理等后续阶段改写时，需要验证求值顺序。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("检查骨骼匹配与引用"))
                {
                    var report = CombineArmatureProcessor.Validate(config);
                    validation = report.ToString();
                    validationType = report.IsValid ? MessageType.Info : MessageType.Error;
                }
                if (GUILayout.Button("生成已装配的角色副本"))
                {
                    Transform root = CombineArmatureProcessor.GetCharacterRoot(config);
                    if (root == null)
                    {
                        validation = "请先指定本体骨架根与完整角色根。";
                        validationType = MessageType.Error;
                    }
                    else
                    {
                        try
                        {
                            GameObject copy = CombineArmatureProcessor.BakeCopy(root.gameObject);
                            Undo.RegisterCreatedObjectUndo(copy, "Bake CombineArmature copy");
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
