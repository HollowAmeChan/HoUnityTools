#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    internal sealed partial class HoFbxImportProcessingWindow
    {
        [SerializeField] private TextAsset bsCorrectiveJson;
        [SerializeField] private bool applyBsCorrective;
        [SerializeField] private Transform bsCorrectiveRoot;
        [SerializeField] private bool bsCorrectivePreviewExpanded = true;

        private void DrawBsCorrectiveRootField()
        {
            using (new EditorGUI.DisabledScope(!applyBsCorrective))
            {
                bsCorrectiveRoot = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent("BS 修型中控物体", "HoBsCorrective 中控组件的挂载物体，留空使用目标骨架。重导只替换同一 JSON 来源的规则，保留手动项。"),
                    bsCorrectiveRoot, typeof(Transform), true);
            }
        }

        private void DrawBsCorrectiveImportPreview()
        {
            bool valid = HoFbxBsCorrectiveImportUtility.TryPreview(bsCorrectiveJson, targetRig, out int count, out string error);
            bsCorrectivePreviewExpanded = EditorGUILayout.Foldout(
                bsCorrectivePreviewExpanded, $"BS 修型目标预览 · {count} 个目标", true);
            if (!bsCorrectivePreviewExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                if (!valid)
                {
                    EditorGUILayout.HelpBox(error, bsCorrectiveJson == null ? MessageType.Info : MessageType.Error);
                    return;
                }
                EditorGUILayout.HelpBox(
                    "集中导入 HoBsCorrective；按骨骼局部四元数夹角独立驱动每个 BS，多个修型叠加。父级变换不会直接改变驱动输入。", MessageType.Info);
                var config = HoFbxBsCorrectiveImportUtility.Parse(bsCorrectiveJson);
                foreach (HoFbxBsCorrectiveImportUtility.Rule rule in config.rules)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            (rule.enabled ? string.Empty : "[停用] ") + rule.bone + " → " + rule.tasks.Length + " 个修型姿势",
                            EditorStyles.boldLabel);
                        foreach (HoFbxBsCorrectiveImportUtility.Task task in rule.tasks)
                        {
                            EditorGUILayout.LabelField(
                                (task.enabled ? string.Empty : "[停用] ") + task.bsName +
                                " · 姿势点 " + task.pointId + $" · 权重 {task.weight:0.###}",
                                EditorStyles.miniBoldLabel);
                            foreach (HoFbxBsCorrectiveImportUtility.Target target in task.targets)
                                EditorGUILayout.LabelField("目标", target.mesh + " / " + target.shapeKey);
                        }
                        if (rule.rbf != null)
                            EditorGUILayout.LabelField($"独立角度衰减 · 姿态 {rule.rbf.controlPoints?.Length ?? 0} · 各自持有影响半径",
                                EditorStyles.wordWrappedMiniLabel);
                    }
                }
                if (targetRig == null)
                    EditorGUILayout.HelpBox("指定目标骨架后，将检查所有骨骼、网格及形态键引用。", MessageType.Info);
            }
        }

        private bool ValidateBsCorrectiveImport()
        {
            if (!HoFbxBsCorrectiveImportUtility.TryPreview(bsCorrectiveJson, targetRig, out _, out string error))
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", error, "确定");
                return false;
            }
            if (Application.isPlaying || targetRig == null || EditorUtility.IsPersistent(targetRig) || !targetRig.scene.IsValid())
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "BS 修型只能应用到编辑模式的场景实例或 Prefab 内容。", "确定");
                return false;
            }
            if (bsCorrectiveRoot != null && (EditorUtility.IsPersistent(bsCorrectiveRoot) ||
                !bsCorrectiveRoot.gameObject.scene.IsValid() || bsCorrectiveRoot.gameObject.scene != targetRig.scene))
            {
                EditorUtility.DisplayDialog("HoFBX导入处理", "BS 修型中控物体必须位于目标骨架所在的场景或 Prefab 内容中。", "确定");
                return false;
            }
            return true;
        }
    }
}
#endif
