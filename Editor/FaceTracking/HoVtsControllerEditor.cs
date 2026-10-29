using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>Stores curve ownership with the role; no AnimationUtility is needed at runtime.</summary>
    [InitializeOnLoad]
    public static class HoVtsControllerBindings
    {
        static HoVtsControllerBindings()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                foreach (var component in Resources.FindObjectsOfTypeAll<HoVtsController>())
                {
                    if (!component.gameObject.scene.IsValid() || component.Controller == null) continue;
                    try { Prepare(component); }
                    catch (Exception error) { Debug.LogError(error.Message, component); }
                }
            };
        }

        public static void PrepareHierarchy(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<HoVtsController>(true)) Prepare(component);
        }

        public static void Prepare(HoVtsController component)
        {
            if (component.Controller == null) throw new InvalidOperationException(component.name + "：未指定 HoVtsController 动画控制器");
            if (component.OutputRoot == null) throw new InvalidOperationException(component.name + "：未指定 HoVtsController 输出根物体");
            var bindings = new List<HoVtsAnimationBinding>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in component.Controller.animationClips)
            {
                if (clip == null) continue;
                if (clip.isHumanMotion) throw new InvalidOperationException(clip.name + "：面部控制器不支持 Humanoid 肌肉动画，请使用 Transform 曲线");
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                    throw new InvalidOperationException(clip.name + "：面部控制器不支持对象引用动画曲线");
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    bool shape = binding.type == typeof(SkinnedMeshRenderer)
                        && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
                    bool transform = binding.type == typeof(Transform) && IsTransformProperty(binding.propertyName);
                    if (!shape && !transform)
                        throw new InvalidOperationException(clip.name + "：面部控制器不支持动画属性 " + binding.type.Name + "." + binding.propertyName);
                    Transform target = string.IsNullOrEmpty(binding.path) ? component.OutputRoot : component.OutputRoot.Find(binding.path);
                    if (target == null) throw new InvalidOperationException(clip.name + "：输出根下找不到 " + binding.path);
                    if (shape)
                    {
                        var renderer = target.GetComponent<SkinnedMeshRenderer>();
                        string key = binding.propertyName.Substring("blendShape.".Length);
                        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(key) < 0)
                            throw new InvalidOperationException(clip.name + "：目标缺少形态键 " + binding.path + "/" + key);
                    }
                    string identity = binding.path + "\n" + binding.type.FullName + "\n" + binding.propertyName;
                    if (unique.Add(identity)) bindings.Add(new HoVtsAnimationBinding(binding.path, binding.propertyName, shape));
                }
            }
            if (bindings.Count == 0) throw new InvalidOperationException(component.name + "：控制器没有可写入的形态键或 Transform 曲线");
            component.SetAnimationBindings(component.Controller, bindings.ToArray());
            EditorUtility.SetDirty(component);
        }

        private static bool IsTransformProperty(string property)
        {
            int dot = property.LastIndexOf('.');
            if (dot < 0 || dot != property.Length - 2) return false;
            char axis = property[property.Length - 1];
            string group = property.Substring(0, dot);
            if (group == "m_LocalRotation") return axis == 'x' || axis == 'y' || axis == 'z' || axis == 'w';
            return (axis == 'x' || axis == 'y' || axis == 'z') && (group == "m_LocalPosition" || group == "m_LocalScale"
                || group == "localEulerAnglesRaw" || group == "localEulerAnglesBaked" || group == "localEulerAngles");
        }
    }

    [CustomEditor(typeof(HoVtsController))]
    public sealed class HoVtsControllerEditor : UnityEditor.Editor
    {
        private string error;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("parameterReceiver"), new GUIContent("参数接收器"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"), new GUIContent("动画控制器"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outputRoot"), new GUIContent("输出根物体", "动画路径相对于这个物体，通常是角色根物体。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRun"), new GUIContent("自动运行"));
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            var component = (HoVtsController)target;
            bool refresh;
            using (new EditorGUI.DisabledScope(Application.isPlaying)) refresh = GUILayout.Button("刷新动画绑定");
            if (!Application.isPlaying && (changed || refresh))
            {
                try
                {
                    Undo.RecordObject(component, "刷新 VTS 动画绑定");
                    HoVtsControllerBindings.Prepare(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    error = null;
                }
                catch (Exception exception) { error = exception.Message; }
            }
            EditorGUILayout.LabelField("动画属性", component.BindingCount.ToString());
            if (Application.isPlaying) EditorGUILayout.LabelField("状态", component.Status);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
