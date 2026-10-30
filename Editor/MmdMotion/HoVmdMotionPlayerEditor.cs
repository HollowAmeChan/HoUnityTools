#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdMotionPlayer))]
    internal sealed class HoVmdMotionPlayerEditor : UnityEditor.Editor
    {
        private ReorderableList bones;
        private ReorderableList morphs;
        private Transform skeleton;
        private bool showBindings = true;
        private HoVmdMorphAuditResult audit;
        private string report;
        private HoVmdMotionPlayer Player => (HoVmdMotionPlayer)target;
        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        private void OnEnable()
        {
            bones = new ReorderableList(serializedObject, P("boneBindings"), true, true, true, true);
            bones.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "骨骼绑定 · VMD 源名称 → Unity 目标");
            bones.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 8;
            bones.drawElementCallback = (rect, index, active, focused) =>
            {
                var entry = bones.serializedProperty.GetArrayElementAtIndex(index);
                rect.height = EditorGUIUtility.singleLineHeight;
                var sourceRect = rect; sourceRect.width -= 48;
                EditorGUI.PropertyField(sourceRect, entry.FindPropertyRelative("vmdName"), new GUIContent("VMD 源骨骼名"));
                var menuRect = rect; menuRect.x = rect.xMax - 44; menuRect.width = 44;
                if (GUI.Button(menuRect, "轨道"))
                {
                    string propertyPath = entry.FindPropertyRelative("vmdName").propertyPath;
                    var menu = new GenericMenu();
                    foreach (string name in HoVmdInspectorGUI.BoneNames(Player.Animation))
                    {
                        string value = name;
                        menu.AddItem(new GUIContent(value.Replace("/", "／")), entry.FindPropertyRelative("vmdName").stringValue == value,
                            () => { serializedObject.Update(); serializedObject.FindProperty(propertyPath).stringValue = value; serializedObject.ApplyModifiedProperties(); });
                    }
                    if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("请先指定带骨骼轨道的 VMD"));
                    menu.DropDown(menuRect);
                }
                rect.y += EditorGUIUtility.singleLineHeight + 3;
                EditorGUI.PropertyField(rect, entry.FindPropertyRelative("target"), new GUIContent("Unity 目标骨骼"));
            };
            bones.onAddCallback = _ =>
            {
                int i = bones.serializedProperty.arraySize++;
                var e = bones.serializedProperty.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("vmdName").stringValue = "";
                e.FindPropertyRelative("target").objectReferenceValue = null;
            };
            morphs = new ReorderableList(serializedObject, P("morphBindings"), true, true, true, true);
            morphs.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "局部表情绑定（可选）");
            morphs.elementHeight = EditorGUIUtility.singleLineHeight * 3 + 10;
            morphs.drawElementCallback = (rect, i, a, f) =>
            {
                var e = morphs.serializedProperty.GetArrayElementAtIndex(i);
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, e.FindPropertyRelative("vmdName"), new GUIContent("VMD 表情名"));
                rect.y += rect.height + 3;
                EditorGUI.PropertyField(rect, e.FindPropertyRelative("renderer"), new GUIContent("目标蒙皮网格"));
                rect.y += rect.height + 3;
                EditorGUI.PropertyField(rect, e.FindPropertyRelative("blendShapeIndex"), new GUIContent("形态键索引", "-1 表示按 VMD 表情名查找；索引从 0 开始。异名映射建议使用角色绑定配置。"));
            };
            morphs.onAddCallback = _ =>
            {
                int i = morphs.serializedProperty.arraySize;
                morphs.serializedProperty.arraySize++;
                var e = morphs.serializedProperty.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("vmdName").stringValue = "";
                e.FindPropertyRelative("renderer").objectReferenceValue = null;
                e.FindPropertyRelative("blendShapeIndex").intValue = -1;
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("VMD 动作播放", EditorStyles.boldLabel);
            Field("animation", "VMD 动画");
            Field("modelRoot", "角色根", "名称搜索及配置中的相对路径以此为根；WTD 使用 WTD-BUILD，而不是单独的衣服骨架。留空使用组件所在物体。");
            Field("bindingProfile", "角色绑定配置", "可被多个动作复用。直接骨骼绑定优先，其次配置映射，最后精确同名匹配。");
            Field("frameRate", "帧率", "通常使用 30；按动作作者实际帧号和说明设置。");
            Field("playOnEnable", "启用时播放");
            Field("loop", "循环");
            Field("applyBones", "应用骨骼轨道");
            Field("applyMorphs", "应用表情轨道");
            Field("applyIkToggles", "应用 VMD 的 IK 开关");
            Field("currentFrame", "当前帧");
            using (new EditorGUI.DisabledScope(!Application.isPlaying)) Field("playing", "正在播放");
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Player.Playing ? "暂停" : "播放")) Player.Playing = !Player.Playing;
                    if (GUILayout.Button("从头播放")) { Player.CurrentFrame = 0; Player.Playing = true; }
                }
            }
            else EditorGUILayout.LabelField("配置完成后进入 Play 模式播放。", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("骨骼绑定左侧填写 VMD 中的日文源名，右侧拖入 Unity 骨骼；两边名称可以不同。同名骨骼无需逐项填写。脚 IK 轨道绑定控制点，脚踝轨道绑定 foot 骨。此播放器目前是 FK 增量与 IK 适配，尚未完成任意 FBX 的完整骨轴重定向。", MessageType.Info);
            if (Player.Animation != null && Player.Animation.boneFrames.Count == 0 && Player.Animation.cameraFrames.Count > 0)
                EditorGUILayout.HelpBox("这是镜头 VMD，请使用 VMD Camera Player。", MessageType.Warning);

            skeleton = (Transform)EditorGUILayout.ObjectField(new GUIContent("生成时使用主骨架", "仅用于初始映射生成；WTD 指定 Armature-Swimsuit，可避免选择 Armature-Miko。"), skeleton, typeof(Transform), true);
            bool invalid = skeleton != null && skeleton != Player.ModelRoot && !skeleton.IsChildOf(Player.ModelRoot);
            if (invalid) EditorGUILayout.HelpBox("主骨架必须属于播放器的角色根。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(Player.Animation == null || invalid))
            {
                if (GUILayout.Button("生成并使用角色绑定配置…"))
                {
                    string path = HoVmdInspectorGUI.SaveProfile(Player.Animation, Player.ModelRoot.gameObject, skeleton);
                    if (path != null)
                    {
                        Undo.RecordObject(Player, "设置 VMD 绑定配置");
                        Player.BindingProfile = AssetDatabase.LoadAssetAtPath<HoVmdBindingProfile>(path);
                        EditorUtility.SetDirty(Player);
                        if (PrefabUtility.IsPartOfPrefabInstance(Player)) PrefabUtility.RecordPrefabInstancePropertyModifications(Player);
                        serializedObject.Update();
                    }
                }
                if (GUILayout.Button("填充动作骨骼列表（保留已有绑定）")) FillBones();
                if (GUILayout.Button("检查角色表情依赖"))
                    report = HoVmdInspectorGUI.WriteAudit(Player.Animation, Player.ModelRoot.gameObject, Player.BindingProfile, out audit);
            }
            HoVmdInspectorGUI.DrawAudit(audit, report);
            showBindings = EditorGUILayout.Foldout(showBindings, "局部绑定与缺项", true);
            if (showBindings)
            {
                bones.DoLayoutList();
                DrawMissing();
                morphs.DoLayoutList();
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void FillBones()
        {
            Undo.RecordObject(Player, "填充 VMD 骨骼轨道");
            foreach (string name in HoVmdInspectorGUI.BoneNames(Player.Animation))
            {
                if (Player.BoneBindings.Any(x => x != null && x.vmdName == name)) continue;
                Player.BoneBindings.Add(new HoVmdMotionPlayer.BoneBinding { vmdName = name, target = Resolve(name) });
            }
            EditorUtility.SetDirty(Player);
            if (PrefabUtility.IsPartOfPrefabInstance(Player)) PrefabUtility.RecordPrefabInstancePropertyModifications(Player);
            serializedObject.Update();
        }

        private Transform Resolve(string source)
        {
            if (Player.BindingProfile != null && Player.BindingProfile.TryBone(source, out string path))
                return path == "." ? Player.ModelRoot : Player.ModelRoot.Find(path);
            var candidates = Player.ModelRoot.GetComponentsInChildren<Transform>(true).Where(x => x.name == source).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        private void DrawMissing()
        {
            if (Player.Animation == null) return;
            var missing = new List<string>();
            foreach (string source in HoVmdInspectorGUI.BoneNames(Player.Animation))
            {
                var direct = Player.BoneBindings.FirstOrDefault(x => x != null && x.vmdName == source && x.target != null);
                if (direct == null && Resolve(source) == null) missing.Add(source);
            }
            if (missing.Count > 0) EditorGUILayout.HelpBox("待绑定 / 重名轨道：" + string.Join("、", missing), MessageType.Warning);
            var duplicates = Player.BoneBindings.Where(x => x != null && x.target != null).GroupBy(x => x.target).Where(x => x.Count() > 1);
            foreach (var group in duplicates)
                EditorGUILayout.HelpBox("多个轨道写入同一骨骼 " + group.Key.name + "：" + string.Join("、", group.Select(x => x.vmdName)) + "。请使用独立控制点处理中心 / Groove，避免相互覆盖。", MessageType.Warning);
        }
        private void Field(string name, string label, string tooltip = null) => EditorGUILayout.PropertyField(P(name), new GUIContent(label, tooltip));
    }
}
#endif
