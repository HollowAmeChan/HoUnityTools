#if UNITY_EDITOR
using System;
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

        public static string SaveProfile(HoVmdAnimation animation, GameObject character, Transform skeleton)
        {
            string path = EditorUtility.SaveFilePanelInProject("保存 VMD 角色绑定配置", character.name + " VMD Binding", "asset", "此配置可被多个动作共用；路径相对于目标角色根。");
            if (string.IsNullOrEmpty(path)) return null;
            var profile = HoVmdBindingProfileBuilder.Build(animation, character, path, skeleton);
            EditorGUIUtility.PingObject(profile);
            return AssetDatabase.GetAssetPath(profile);
        }

        public static string WriteAudit(HoVmdAnimation animation, GameObject character, HoVmdBindingProfile profile, out HoVmdMorphAuditResult result)
        {
            string stem = animation.name + "-" + character.name;
            foreach (char invalid in Path.GetInvalidFileNameChars()) stem = stem.Replace(invalid, '_');
            string path = Path.GetFullPath(Path.Combine("Library/HoMmdStages/MorphAudit", stem + ".json"));
            result = HoVmdMorphAudit.Audit(animation, character, profile);
            HoVmdMorphAudit.WriteJson(animation, character, profile, path);
            Debug.Log($"VMD 表情检查：需求 {result.required}，精确 {result.exactMatches}，规范化 {result.normalizedMatches}，配置映射 {result.profileMatches}，缺失 {result.missing}，冲突 {result.conflicts}。报告：{path}", character);
            return path;
        }

        public static void DrawAudit(HoVmdMorphAuditResult result, string report)
        {
            if (result == null) return;
            EditorGUILayout.HelpBox($"表情需求 {result.required}：精确 {result.exactMatches} / 规范化候选 {result.normalizedMatches} / 配置映射 {result.profileMatches} / 缺失 {result.missing} / 冲突 {result.conflicts}", result.IsComplete ? MessageType.Info : MessageType.Warning);
            if (result.normalizedMatches > 0) EditorGUILayout.HelpBox("规范化候选需要生成绑定配置并赋给播放器，才会在播放时生效。", MessageType.Info);
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
        private HoVmdBindingProfile profile;
        private HoVmdMorphAuditResult audit;
        private string report;
        private bool showBones;
        public void Draw(HoVmdAnimation animation)
        {
            EditorGUILayout.LabelField("角色绑定与检查", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            character = (GameObject)EditorGUILayout.ObjectField(new GUIContent("目标角色根", "拖入场景角色或角色 prefab；绑定路径、网格和 IK 均相对于这个根。"), character, typeof(GameObject), true);
            skeleton = (Transform)EditorGUILayout.ObjectField(new GUIContent("主骨架（可选）", "多套骨架时指定主骨架，避免自动匹配到待合并的衣服骨架。WTD 使用 Armature-Swimsuit。"), skeleton, typeof(Transform), true);
            profile = (HoVmdBindingProfile)EditorGUILayout.ObjectField("角色绑定配置（可选）", profile, typeof(HoVmdBindingProfile), false);
            if (EditorGUI.EndChangeCheck()) { audit = null; report = null; }
            bool invalidSkeleton = character != null && skeleton != null && skeleton != character.transform && !skeleton.IsChildOf(character.transform);
            if (invalidSkeleton) EditorGUILayout.HelpBox("主骨架必须属于所选目标角色。请选择同一个实例内的引用。", MessageType.Warning);
            EditorGUILayout.HelpBox("可直接拖入角色，无需同时选中两个对象。生成结果是名称映射，尚不等于骨轴、比例和中心骨都已重定向。", MessageType.Info);
            using (new EditorGUI.DisabledScope(character == null || animation == null || invalidSkeleton))
            {
                if (GUILayout.Button("生成角色绑定配置…"))
                {
                    string saved = HoVmdInspectorGUI.SaveProfile(animation, character, skeleton);
                    if (saved != null) profile = AssetDatabase.LoadAssetAtPath<HoVmdBindingProfile>(saved);
                }
                if (GUILayout.Button("检查所选角色的表情依赖")) report = HoVmdInspectorGUI.WriteAudit(animation, character, profile, out audit);
            }
            HoVmdInspectorGUI.DrawAudit(audit, report);
            string[] names = HoVmdInspectorGUI.BoneNames(animation);
            showBones = EditorGUILayout.Foldout(showBones, $"动作中的骨骼轨道（{names.Length}）", true);
            if (showBones) foreach (string name in names) EditorGUILayout.SelectableLabel(name, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

}
#endif
