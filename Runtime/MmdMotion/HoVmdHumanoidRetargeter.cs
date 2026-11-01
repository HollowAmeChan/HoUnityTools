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
                TransferPoleBend(foot, targetSpace.rotation * sourceInverse);
                // FK and the pole both describe this complete reference sample.
                // Filtering against a previous interactive IK plane makes a
                // timeline seek depend on which frames were visited beforehand.
                foot.targetIk.ResetTemporalState();
                foot.targetIk.SolveNow(suppressAutomaticUpdate: true);
                foot.targetIk.Effector.rotation = footRotation;
            }
        }

        private static void TransferPoleBend(Foot foot, Quaternion sourceToTarget)
        {
            if (foot.targetIk.Pole == null || foot.sourceIk.Links.Count != 2 ||
                foot.targetIk.Links.Count != 2) return;
            HoIkConstraint.Link hinge = foot.sourceIk.Links[0];
            Transform sourceKnee = hinge.bone, sourceRoot = foot.sourceIk.Links[1].bone;
            Transform targetKnee = foot.targetIk.Links[0].bone, targetRoot = foot.targetIk.Links[1].bone;
            if (sourceKnee == null || sourceRoot == null || targetKnee == null ||
                targetRoot == null || foot.targetIk.Effector == null) return;

            Vector3 sourceAxis = foot.sourceIk.Effector.position - sourceRoot.position;
            Vector3 targetAxis = foot.targetIk.Target.position - targetRoot.position;
            if (sourceAxis.sqrMagnitude < 1e-10f || targetAxis.sqrMagnitude < 1e-10f) return;
            Vector3 sourceBend = Vector3.ProjectOnPlane(sourceKnee.position - sourceRoot.position, sourceAxis);
            if (sourceBend.sqrMagnitude < sourceAxis.sqrMagnitude * 1e-8f)
            {
                int enabledAxes = (hinge.useX ? 1 : 0) + (hinge.useY ? 1 : 0) + (hinge.useZ ? 1 : 0);
                if (hinge.useAxisMask && enabledAxes == 1)
                {
                    Vector3 localAxis = hinge.useX ? Vector3.right : hinge.useY ? Vector3.up : Vector3.forward;
                    sourceBend = Vector3.Cross(sourceAxis, sourceKnee.rotation * localAxis);
                    float minimum = hinge.useX ? hinge.localEulerMin.x : hinge.useY ? hinge.localEulerMin.y : hinge.localEulerMin.z;
                    float maximum = hinge.useX ? hinge.localEulerMax.x : hinge.useY ? hinge.localEulerMax.y : hinge.localEulerMax.z;
                    if (hinge.useLocalEulerLimits && maximum <= 0 && minimum < 0) sourceBend = -sourceBend;
                }
                else sourceBend = Vector3.ProjectOnPlane(sourceRoot.forward, sourceAxis);
            }
            if (sourceBend.sqrMagnitude < 1e-10f) return;

            // Transfer the bend frame, not an absolute knee point. Different
            // segment proportions can put a mapped source knee on the target
            // hip/ankle axis and make the projected Pole reverse by 180 degrees.
            Quaternion swing = Quaternion.FromToRotation(sourceToTarget * sourceAxis, targetAxis);
            Vector3 bend = Vector3.ProjectOnPlane(swing * (sourceToTarget * sourceBend), targetAxis);
            if (bend.sqrMagnitude < 1e-10f) return;
            float length = Vector3.Distance(targetRoot.position, targetKnee.position) +
                Vector3.Distance(targetKnee.position, foot.targetIk.Effector.position);
            foot.targetIk.Pole.position = targetRoot.position + bend.normalized * length;
        }
    }
}
