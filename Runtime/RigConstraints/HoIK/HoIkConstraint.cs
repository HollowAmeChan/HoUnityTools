using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints
{
    /// <summary>
    /// MMD-style CCD IK constraint for a single end effector.
    ///
    /// The chain is ordered from the effector's parent towards the root. A
    /// typical MMD foot chain is ankle, knee, thigh. The component deliberately
    /// runs after Animator output and before any later presentation code, so
    /// VMD bone curves remain the input pose and this constraint supplies the
    /// IK solve on top.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32100)]
    [AddComponentMenu("HoUnityTools/Constraints/CCD IK")]
    public sealed class HoIkConstraint : MonoBehaviour
    {
        public enum UpdateMode
        {
            LateUpdate,
            Manual,
        }

        [Serializable]
        public sealed class Link
        {
            [Tooltip("One link in the chain. Put the effector parent first, then its parent, and so on.")]
            public Transform bone;

            [Tooltip("Clamp this link's local Euler rotation after each CCD step. Useful for MMD knee/elbow limits.")]
            public bool useLocalEulerLimits;

            public Vector3 localEulerMin = new Vector3(-180.0f, -180.0f, -180.0f);
            public Vector3 localEulerMax = new Vector3(180.0f, 180.0f, 180.0f);

            [Tooltip("Optional axis mask in the link's local rotation space. A disabled axis is restored after solving.")]
            public bool useAxisMask;
            public bool useX = true;
            public bool useY = true;
            public bool useZ = true;

            [HideInInspector] public Quaternion bindLocalRotation = Quaternion.identity;
        }

        [Header("Targets")]
        [SerializeField, Tooltip("MMD show/IK tracks use this name to toggle the constraint. Empty falls back to the GameObject name.")]
        private string ikName = string.Empty;
        [SerializeField, Tooltip("The bone that must reach the target. Usually the foot/hand/toe bone.")]
        private Transform effector;
        [SerializeField, Tooltip("World-space IK goal. It may be animated by another rig or a VMD prop/controller.")]
        private Transform target;
        [SerializeField, Tooltip("Optional pole direction. This is used to stabilize knees and elbows after CCD.")]
        private Transform pole;

        [Header("Chain")]
        [SerializeField]
        private List<Link> links = new List<Link>();

        [Header("Solve")]
        [SerializeField] private UpdateMode updateMode = UpdateMode.LateUpdate;
        [SerializeField, Range(0.0f, 1.0f)] private float weight = 1.0f;
        [SerializeField] private bool solveEnabled = true;
        [SerializeField, Min(1)] private int iterations = 12;
        [SerializeField, Min(0.0f)] private float tolerance = 0.0005f;
        [SerializeField, Range(0.0f, 180.0f)] private float maxStepAngle = 45.0f;
        [SerializeField, Tooltip("Restore each link's bind local rotation before solving. Leave off when an animation clip owns the link pose.")]
        private bool restoreBindRotation;
        [SerializeField, Tooltip("Use the pole after CCD to keep a knee/elbow plane stable.")]
        private bool usePole = true;
        [Header("Scene Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField, Min(0.001f)] private float gizmoSize = 0.04f;
        [SerializeField] private Color chainColor = new Color(0.15f, 0.85f, 1.0f, 0.9f);
        [SerializeField] private Color targetColor = new Color(1.0f, 0.75f, 0.1f, 0.95f);
        [SerializeField] private Color poleColor = new Color(1.0f, 0.25f, 0.55f, 0.95f);

        private bool hasBindPose;
        private string lastError;
        private readonly HashSet<string> warned = new HashSet<string>();

        public Transform Effector { get => effector; set => effector = value; }
        public string IkName { get => string.IsNullOrEmpty(ikName) ? name : ikName; set => ikName = value ?? string.Empty; }
        public Transform Target { get => target; set => target = value; }
        public Transform Pole { get => pole; set => pole = value; }
        public IList<Link> Links => links;
        public UpdateMode Mode { get => updateMode; set => updateMode = value; }
        public float Weight { get => weight; set => weight = Mathf.Clamp01(value); }
        public bool SolveEnabled { get => solveEnabled; set => solveEnabled = value; }
        public int Iterations { get => iterations; set => iterations = Mathf.Max(1, value); }
        public float Tolerance { get => tolerance; set => tolerance = Mathf.Max(0.0f, value); }
        public float MaxStepAngle { get => maxStepAngle; set => maxStepAngle = Mathf.Clamp(value, 0.0f, 180.0f); }
        public bool RestoreBindRotation { get => restoreBindRotation; set => restoreBindRotation = value; }
        public string LastError => lastError;
        public bool DrawGizmos { get => drawGizmos; set => drawGizmos = value; }

        private void OnEnable()
        {
            CaptureBindPose();
        }

        private void OnValidate()
        {
            iterations = Mathf.Max(1, iterations);
            tolerance = Mathf.Max(0.0f, tolerance);
            maxStepAngle = Mathf.Clamp(maxStepAngle, 0.0f, 180.0f);
            weight = Mathf.Clamp01(weight);
            if (!Application.isPlaying)
                CaptureBindPose();
        }

        private void LateUpdate()
        {
            if (Application.isPlaying && updateMode == UpdateMode.LateUpdate)
                SolveNow();
        }

        /// <summary>Capture current local rotations for optional bind restoration.</summary>
        public void CaptureBindPose()
        {
            if (links == null) links = new List<Link>();
            for (int i = 0; i < links.Count; i++)
            {
                Link link = links[i];
                if (link != null && link.bone != null)
                    link.bindLocalRotation = link.bone.localRotation;
            }
            hasBindPose = true;
        }

        /// <summary>Run CCD once. Runtime callers can invoke this after animation evaluation.</summary>
        public void SolveNow()
        {
            lastError = null;
            if (!enabled || !solveEnabled || weight <= 0.0f)
                return;
            if (effector == null || target == null)
            {
                SetError("Effector or target is missing.");
                return;
            }
            if (links == null || links.Count == 0)
            {
                SetError("No IK links are configured.");
                return;
            }
            if (!ValidateChain())
                return;
            if (restoreBindRotation && hasBindPose)
                RestoreBindRotations();

            int count = Mathf.Max(1, iterations);
            float epsilon = Mathf.Max(0.0f, tolerance);
            for (int iteration = 0; iteration < count; iteration++)
            {
                if ((effector.position - target.position).sqrMagnitude <= epsilon * epsilon)
                    break;
                for (int index = 0; index < links.Count; index++)
                {
                    Link link = links[index];
                    if (link == null || link.bone == null)
                        continue;
                    SolveLink(link);
                    if ((effector.position - target.position).sqrMagnitude <= epsilon * epsilon)
                        break;
                }
            }

            if (usePole && pole != null)
                ApplyPole();
        }

        private bool ValidateChain()
        {
            Transform previous = effector;
            for (int i = 0; i < links.Count; i++)
            {
                Link link = links[i];
                if (link == null || link.bone == null)
                {
                    SetError("A chain link is missing.");
                    return false;
                }
                if (link.bone == effector || link.bone == target)
                {
                    SetError("A chain link cannot be the effector or target.");
                    return false;
                }
                if (previous != null && !previous.IsChildOf(link.bone))
                {
                    SetError("IK links must be ordered from effector parent toward the root.");
                    return false;
                }
                previous = link.bone;
            }
            return true;
        }

        private void SolveLink(Link link)
        {
            Vector3 pivot = link.bone.position;
            Vector3 toEffector = effector.position - pivot;
            Vector3 toTarget = target.position - pivot;
            if (toEffector.sqrMagnitude < 1e-10f || toTarget.sqrMagnitude < 1e-10f)
                return;

            Quaternion delta = Quaternion.FromToRotation(toEffector, toTarget);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(angle) || axis.sqrMagnitude < 1e-10f)
                return;
            if (angle > 180.0f) angle -= 360.0f;
            float step = Mathf.Min(Mathf.Abs(angle), maxStepAngle) * Mathf.Sign(angle) * weight;
            Quaternion desired = Quaternion.AngleAxis(step, axis.normalized) * link.bone.rotation;

            if (link.useAxisMask)
                desired = ApplyAxisMask(link, desired);
            link.bone.rotation = desired;
            if (link.useLocalEulerLimits)
                ClampLocalEuler(link);
        }

        private Quaternion ApplyAxisMask(Link link, Quaternion desired)
        {
            Quaternion delta = desired * Quaternion.Inverse(link.bone.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            Vector3 localAxis = Quaternion.Inverse(link.bone.rotation) * axis;
            localAxis = new Vector3(link.useX ? localAxis.x : 0.0f, link.useY ? localAxis.y : 0.0f, link.useZ ? localAxis.z : 0.0f);
            if (localAxis.sqrMagnitude < 1e-10f) return link.bone.rotation;
            return Quaternion.AngleAxis(angle, link.bone.rotation * localAxis.normalized) * link.bone.rotation;
        }

        private static void ClampLocalEuler(Link link)
        {
            Vector3 euler = NormalizeEuler(link.bone.localEulerAngles);
            euler.x = Mathf.Clamp(euler.x, link.localEulerMin.x, link.localEulerMax.x);
            euler.y = Mathf.Clamp(euler.y, link.localEulerMin.y, link.localEulerMax.y);
            euler.z = Mathf.Clamp(euler.z, link.localEulerMin.z, link.localEulerMax.z);
            link.bone.localEulerAngles = euler;
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360.0f;
            return angle > 180.0f ? angle - 360.0f : angle;
        }

        private void ApplyPole()
        {
            if (links.Count == 0 || links[0] == null || links[0].bone == null)
                return;
            // With the documented order (ankle, knee, thigh), the second link
            // is the actual knee/elbow pivot. Fall back to the first link for a
            // one-link toe/hand chain.
            Transform knee = links.Count > 1 && links[1] != null && links[1].bone != null
                ? links[1].bone
                : links[0].bone;
            Vector3 axis = target.position - knee.position;
            if (axis.sqrMagnitude < 1e-10f) return;
            Vector3 current = Vector3.ProjectOnPlane(effector.position - knee.position, axis);
            Vector3 desired = Vector3.ProjectOnPlane(pole.position - knee.position, axis);
            if (current.sqrMagnitude < 1e-10f || desired.sqrMagnitude < 1e-10f) return;
            float signed = Vector3.SignedAngle(current, desired, axis.normalized) * weight;
            Quaternion correction = Quaternion.AngleAxis(signed, axis.normalized);
            for (int i = 0; i < links.Count; i++)
                if (links[i] != null && links[i].bone != null)
                    links[i].bone.rotation = correction * links[i].bone.rotation;
        }

        private void RestoreBindRotations()
        {
            for (int i = 0; i < links.Count; i++)
            {
                Link link = links[i];
                if (link != null && link.bone != null)
                    link.bone.localRotation = link.bindLocalRotation;
            }
        }

        private void SetError(string message)
        {
            lastError = message;
            if (warned.Add(message))
                Debug.LogWarning("HoIkConstraint (" + name + "): " + message, this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            float size = Mathf.Max(0.001f, gizmoSize);
            if (effector != null)
            {
                Gizmos.color = chainColor;
                Gizmos.DrawWireSphere(effector.position, size);
                Gizmos.DrawLine(transform.position, effector.position);
            }
            if (links != null)
            {
                Gizmos.color = chainColor;
                Transform previous = effector;
                for (int i = 0; i < links.Count; i++)
                {
                    Transform bone = links[i] != null ? links[i].bone : null;
                    if (bone == null) continue;
                    Gizmos.DrawWireSphere(bone.position, size * (i == 0 ? 1.25f : 0.9f));
                    if (previous != null) Gizmos.DrawLine(previous.position, bone.position);
                    previous = bone;
                }
            }
            if (target != null)
            {
                Gizmos.color = targetColor;
                Gizmos.DrawWireSphere(target.position, size * 1.5f);
                if (effector != null) Gizmos.DrawLine(effector.position, target.position);
            }
            if (pole != null)
            {
                Gizmos.color = poleColor;
                Gizmos.DrawWireSphere(pole.position, size * 1.35f);
                Transform knee = links != null && links.Count > 1 && links[1] != null ? links[1].bone : null;
                if (knee != null) Gizmos.DrawLine(knee.position, pole.position);
            }
        }
    }
}
