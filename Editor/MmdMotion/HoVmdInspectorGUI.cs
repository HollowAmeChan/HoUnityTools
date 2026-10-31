#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    internal static class HoVmdInspectorGUI
    {
        public static void DrawSummary(HoVmdAnimation animation)
        {
            EditorGUILayout.LabelField("源模型名", animation.modelName);
            EditorGUILayout.LabelField("文件签名", animation.signature);
            EditorGUILayout.LabelField("骨骼关键帧数", animation.boneFrames.Count.ToString());
            EditorGUILayout.LabelField("表情关键帧数", animation.morphFrames.Count.ToString());
            EditorGUILayout.LabelField("表情轨道数", animation.morphFrames.Select(x => x.morphName).Distinct().Count().ToString());
            EditorGUILayout.LabelField("镜头关键帧数", animation.cameraFrames.Count.ToString());
            EditorGUILayout.LabelField("灯光关键帧数", animation.lightFrames.Count.ToString());
            EditorGUILayout.LabelField("显示 / IK 开关帧数", animation.showIkFrames.Count.ToString());
            EditorGUILayout.LabelField("骨骼末帧", animation.LastBoneFrame.ToString());
            EditorGUILayout.LabelField("表情末帧", animation.LastMorphFrame.ToString());
            EditorGUILayout.LabelField("镜头末帧", animation.LastCameraFrame.ToString());
        }

        public static string[] BoneNames(HoVmdAnimation animation) => animation == null ? Array.Empty<string>() :
            animation.boneFrames.Select(x => x.boneName).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        public static string WriteAudit(HoVmdAnimation animation, GameObject character, HoVmdBindingProfile profile,
            out HoVmdMorphAuditResult result, IEnumerable<HoVmdMotionPlayer.MorphBinding> localBindings = null)
        {
            string stem = animation.name + "-" + character.name;
            foreach (char invalid in Path.GetInvalidFileNameChars()) stem = stem.Replace(invalid, '_');
            string path = Path.GetFullPath(Path.Combine("Library/HoMmdStages/MorphAudit", stem + ".json"));
            result = HoVmdMorphAudit.Audit(animation, character, profile, localBindings);
            HoVmdMorphAudit.WriteJson(animation, character, profile, path, localBindings);
            Debug.Log($"VMD 表情检查：需求 {result.required}，本地映射 {result.localMatches}，精确 {result.exactMatches}，规范化 {result.normalizedMatches}，配置映射 {result.profileMatches}，缺失 {result.missing}，冲突 {result.conflicts}。报告：{path}", character);
            return path;
        }

        public static void DrawAudit(HoVmdMorphAuditResult result, string report, bool localMappings = false)
        {
            if (result == null) return;
            EditorGUILayout.HelpBox($"表情需求 {result.required}：组件映射 {result.localMatches} / 精确 {result.exactMatches} / 规范化候选 {result.normalizedMatches} / 配置映射 {result.profileMatches} / 缺失 {result.missing} / 冲突 {result.conflicts}", result.IsComplete ? MessageType.Info : MessageType.Warning);
            if (result.normalizedMatches > 0)
                EditorGUILayout.HelpBox(localMappings
                    ? "规范化匹配由当前播放器实例内的表情映射使用，不会生成外部配置文件。"
                    : "规范化名称表示候选；在 VMD 动作播放器中检查并确认实际表情目标。", MessageType.Info);
            Names("缺失表情", result.missingNames);
            Names("冲突表情", result.conflictNames);
            Names("失效配置映射", result.profileConflictNames);
            if (!string.IsNullOrEmpty(report) && GUILayout.Button("定位检查报告")) EditorUtility.RevealInFinder(report);
        }
        private static void Names(string title, string[] names)
        {
            if (names != null && names.Length > 0) EditorGUILayout.HelpBox(title + "：" + string.Join("、", names), MessageType.None);
        }
    }

    internal sealed class HoVmdBindingPanel
    {
        private GameObject character;
        private Transform skeleton;
        private HoVmdMorphAuditResult audit;
        private string report;
        private bool showBones;
        public void Draw(HoVmdAnimation animation)
        {
            EditorGUILayout.LabelField("VMD 轨道与表情检查", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            character = (GameObject)EditorGUILayout.ObjectField(new GUIContent("目标角色根", "拖入场景角色或角色 prefab；绑定路径、网格和 IK 均相对于这个根。"), character, typeof(GameObject), true);
            skeleton = (Transform)EditorGUILayout.ObjectField(new GUIContent("主骨架（可选）", "多套骨架时指定主骨架，避免自动匹配到待合并的衣服骨架。WTD 使用 Armature-Swimsuit。"), skeleton, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck()) { audit = null; report = null; }
            bool invalidSkeleton = character != null && skeleton != null && skeleton != character.transform && !skeleton.IsChildOf(character.transform);
            if (invalidSkeleton) EditorGUILayout.HelpBox("主骨架必须属于所选目标角色。请选择同一个实例内的引用。", MessageType.Warning);
            EditorGUILayout.HelpBox("此面板只检查 VMD 轨道与角色资源。实际动作映射和播放设置保存在 VMD Motion Player 组件上。", MessageType.Info);
            using (new EditorGUI.DisabledScope(character == null || animation == null || invalidSkeleton))
            {
                if (GUILayout.Button("检查所选角色的表情依赖"))
                    report = HoVmdInspectorGUI.WriteAudit(animation, character, null, out audit);
            }
            HoVmdInspectorGUI.DrawAudit(audit, report);
            string[] names = HoVmdInspectorGUI.BoneNames(animation);
            showBones = EditorGUILayout.Foldout(showBones, $"动作中的骨骼轨道（{names.Length}）", true);
            if (showBones) foreach (string name in names) EditorGUILayout.SelectableLabel(name, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

}
#endif
