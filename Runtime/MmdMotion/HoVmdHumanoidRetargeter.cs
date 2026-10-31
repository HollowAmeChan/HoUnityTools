using System;
using System.Collections.Generic;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>
    /// Transfers a sampled MMD reference rig to an existing character, preserving
    /// the reference's independent upper/lower body and the character's proportions.
    /// Rest data is captured explicitly, never from a currently animated frame.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/MMD/VMD Humanoid Retargeter")]
    public sealed class HoVmdHumanoidRetargeter : MonoBehaviour
    {
        [Serializable] public sealed class Bone
        {
            public Transform source, target, appendRotation;
            public Quaternion rotationOffset = Quaternion.identity;
        }
        [Serializable] public sealed class Foot
        {
            public HoIkConstraint sourceIk, targetIk;
            public Vector3 sourceRestPosition, targetRestPosition;
        }

        [SerializeField, InspectorName("MMD 参考空间")] private Transform sourceSpace;
        [SerializeField, InspectorName("角色空间")] private Transform targetSpace;
        [SerializeField, InspectorName("参考骨盆")] private Transform sourceHips;
        [SerializeField, InspectorName("角色骨盆")] private Transform targetHips;
        [SerializeField, Min(.001f), InspectorName("动作位移比例")] private float translationScale = 1;
        [SerializeField] private Vector3 sourceRestHips, targetRestHips;
        [SerializeField, InspectorName("骨骼映射")] private List<Bone> bones = new List<Bone>();
        [SerializeField, InspectorName("脚部 IK 映射")] private List<Foot> feet = new List<Foot>();

        public Transform SourceSpace { get => sourceSpace; set => sourceSpace = value; }
        public Transform TargetSpace { get => targetSpace; set => targetSpace = value; }
        public Transform SourceHips { get => sourceHips; set => sourceHips = value; }
        public Transform TargetHips { get => targetHips; set => targetHips = value; }
        public float TranslationScale { get => translationScale; set => translationScale = Mathf.Max(.001f, value); }
        public IList<Bone> Bones => bones;
        public IList<Foot> Feet => feet;

        public void CaptureRestPositions()
        {
            if (sourceSpace == null || targetSpace == null || sourceHips == null || targetHips == null) return;
            sourceRestHips = sourceSpace.InverseTransformPoint(sourceHips.position);
            targetRestHips = targetSpace.InverseTransformPoint(targetHips.position);
            foreach (Foot foot in feet)
            {
                if (foot.sourceIk == null || foot.targetIk == null ||
                    foot.sourceIk.Effector == null || foot.targetIk.Effector == null) continue;
                foot.sourceRestPosition = sourceSpace.InverseTransformPoint(foot.sourceIk.Effector.position);
                foot.targetRestPosition = targetSpace.InverseTransformPoint(foot.targetIk.Effector.position);
            }
        }

        public void ApplyPose()
        {
            if (sourceSpace == null || targetSpace == null) return;
            Quaternion sourceInverse = Quaternion.Inverse(sourceSpace.rotation);
            // The list is stored in parent-before-child target hierarchy order.
            foreach (Bone bone in bones)
            {
                if (bone.source == null || bone.target == null) continue;
                Quaternion sampled = bone.source.rotation;
                if (bone.appendRotation != null)
                    sampled = bone.source.parent.rotation * bone.appendRotation.localRotation * bone.source.localRotation;
                bone.target.rotation = targetSpace.rotation * sourceInverse * sampled * bone.rotationOffset;
            }
            if (sourceHips != null && targetHips != null)
                targetHips.position = targetSpace.TransformPoint(targetRestHips +
                    (sourceSpace.InverseTransformPoint(sourceHips.position) - sourceRestHips) * translationScale);

            // Re-solve in the target proportions; copying reference joint rotations
            // alone cannot keep a differently proportioned character's feet planted.
            foreach (Foot foot in feet)
            {
                if (foot.sourceIk == null || foot.targetIk == null || foot.targetIk.Target == null ||
                    foot.sourceIk.Effector == null) continue;
                foot.targetIk.SolveEnabled = foot.sourceIk.SolveEnabled;
                if (!foot.sourceIk.SolveEnabled) continue;
                // Reference toe IK has already determined the reference foot's
                // orientation. A second toe-position solve using differently
                // proportioned feet would tilt the target ankle again.
                if (foot.sourceIk.Links.Count == 1)
                {
                    foot.targetIk.SolveEnabled = false;
                    continue;
                }
                foot.targetIk.Target.position = targetSpace.TransformPoint(foot.targetRestPosition +
                    (sourceSpace.InverseTransformPoint(foot.sourceIk.Effector.position) - foot.sourceRestPosition) * translationScale);
                Quaternion footRotation = foot.targetIk.Effector.rotation;
                if (foot.targetIk.Pole != null && foot.sourceIk.Links.Count == 2 &&
                    foot.sourceIk.Links[0]?.bone != null)
                    foot.targetIk.Pole.position = targetSpace.TransformPoint(targetRestHips +
                        (sourceSpace.InverseTransformPoint(foot.sourceIk.Links[0].bone.position) - sourceRestHips) * translationScale);
                foot.targetIk.SolveNow();
                foot.targetIk.Effector.rotation = footRotation;
            }
        }
    }
}
