using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoFloatingConstraint))]
    [CanEditMultipleObjects]
    internal sealed class HoFloatingConstraintEditor : HoMotionConstraintEditor
    {
        private static readonly string[] Presets = { "选择预设…", "清空", "光环", "武器", "背包", "无人机" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            int preset = EditorGUILayout.Popup("预设", 0, Presets);
            Field("oscillationSpace", "位移空间", "正弦和噪声位移使用世界轴或初始姿态的本地轴。");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("周期摆动", EditorStyles.boldLabel);
            Field("oscillationEnabled", "启用摆动");
            Field("oscillationFrequency", "频率（Hz）");
            Field("oscillationMultiplier", "幅度倍率");
            Field("oscillationPositionAmplitude", "位置幅度");
            Field("oscillationRotationAmplitude", "旋转幅度（度）");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("噪声", EditorStyles.boldLabel);
            Field("noiseEnabled", "启用噪声");
            Field("noiseFrequency", "变化速度");
            Field("noiseMultiplier", "幅度倍率");
            Field("noiseSeed", "随机种子");
            Field("noisePositionAmplitude", "位置幅度");
            Field("noiseRotationAmplitude", "旋转幅度（度）");
            serializedObject.ApplyModifiedProperties();

            if (preset > 0)
            {
                ApplyPreset(item => ApplyFloatingPreset(item, preset));
            }
        }

        private static void ApplyFloatingPreset(SerializedObject item, int preset)
        {
            bool oscillates = preset == 2;
            bool addsNoise = preset >= 3;
            Vector3 noisePosition = Vector3.zero;
            Vector3 noiseRotation = Vector3.zero;
            switch (preset)
            {
                case 3:
                    noisePosition = Vector3.one * 0.012f;
                    noiseRotation = Vector3.one * 0.35f;
                    break;
                case 4:
                    noisePosition = Vector3.one * 0.018f;
                    noiseRotation = Vector3.one * 0.25f;
                    break;
                case 5:
                    noisePosition = Vector3.one * 0.02f;
                    noiseRotation = new Vector3(0f, 0.45f, 0f);
                    break;
            }

            item.FindProperty("oscillationSpace").intValue = (int)HoFloatingConstraintSpace.World;
            item.FindProperty("oscillationEnabled").boolValue = oscillates;
            item.FindProperty("oscillationFrequency").floatValue = 0.35f;
            item.FindProperty("oscillationMultiplier").floatValue = 1f;
            item.FindProperty("oscillationPositionAmplitude").vector3Value = oscillates
                ? new Vector3(0f, 0.025f, 0f) : Vector3.zero;
            item.FindProperty("oscillationRotationAmplitude").vector3Value = oscillates
                ? new Vector3(0f, 0.3f, 0f) : Vector3.zero;
            item.FindProperty("noiseEnabled").boolValue = addsNoise;
            item.FindProperty("noiseFrequency").floatValue = 0.75f;
            item.FindProperty("noiseMultiplier").floatValue = 1f;
            item.FindProperty("noisePositionAmplitude").vector3Value = noisePosition;
            item.FindProperty("noiseRotationAmplitude").vector3Value = noiseRotation;
        }
    }
}
