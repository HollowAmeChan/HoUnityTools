#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.MmdMotion;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdAnimation))]
    internal sealed class HoVmdAnimationEditor : UnityEditor.Editor
    {
        private HoVmdBindingProfile auditProfile;

        public override void OnInspectorGUI()
        {
            var animation=(HoVmdAnimation)target;
            EditorGUILayout.LabelField("VMD model", animation.modelName);
            EditorGUILayout.LabelField("Signature", animation.signature);
            EditorGUILayout.LabelField("Bone frames", animation.boneFrames.Count.ToString());
            EditorGUILayout.LabelField("Morph frames", animation.morphFrames.Count.ToString());
            EditorGUILayout.LabelField("Unique morph requirements", animation.morphFrames.Select(x => x.morphName).Distinct().Count().ToString());
            EditorGUILayout.LabelField("Camera frames", animation.cameraFrames.Count.ToString());
            EditorGUILayout.LabelField("Light frames", animation.lightFrames.Count.ToString());
            EditorGUILayout.LabelField("Show/IK frames", animation.showIkFrames.Count.ToString());
            EditorGUILayout.LabelField("Last bone frame", animation.LastBoneFrame.ToString());
            EditorGUILayout.LabelField("Last camera frame", animation.LastCameraFrame.ToString());
            EditorGUILayout.HelpBox("将动作绑定到模型时，先确认骨骼名称、形态键和每个 IK 控制器的动作级开关。", MessageType.Info);
            auditProfile = (HoVmdBindingProfile)EditorGUILayout.ObjectField(
                "Optional binding profile", auditProfile, typeof(HoVmdBindingProfile), false);
            if (GUILayout.Button("Audit morphs against selected character"))
            {
                GameObject target = Selection.activeGameObject;
                if (target == null)
                    EditorUtility.DisplayDialog("Ho VMD Morph Audit", "Select the target FBX/GLTF character GameObject first.", "OK");
                else
                {
                    string path = Path.Combine("Library/HoMmdStages/MorphAudit", animation.name + "-" + target.name + ".json");
                    HoVmdMorphAuditResult result = HoVmdMorphAudit.Audit(animation, target, auditProfile);
                    HoVmdMorphAudit.WriteJson(animation, target, auditProfile, path);
                    Debug.Log("Ho VMD morph audit: required=" + result.required + " exact=" + result.exactMatches + " normalized=" + result.normalizedMatches + " profile=" + result.profileMatches + " missing=" + result.missing + " conflicts=" + result.conflicts + " -> " + path, target);
                    if (result.missing > 0 || result.conflicts > 0) EditorUtility.DisplayDialog("Ho VMD Morph Audit", "Missing: " + result.missing + ", conflicts: " + result.conflicts + "\n" + path, "OK");
                }
            }
        }
    }
}
#endif
