using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.RigConstraints
{
    /// <summary>
    /// MMD-style IK for a single end effector: analytic two-bone hinges with
    /// a Pole, and CCD for other chains.
    ///
    /// The chain is ordered from the effector's parent towards the root. A
    /// typical MMD ankle effector uses knee, thigh as its links. The component deliberately
    /// runs after Animator output and before any later presentation code, so
    /// VMD bone curves remain the input pose and this constraint supplies the
    /// IK solve on top.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32100)]
    [AddComponentMenu("HoUnityTools/Constraints/Ho IK Constraint")]
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
        [SerializeField, Range(0.0f, 180.0f), Tooltip("Pole 平面每次求解允许的最大扭转角；目标穿过 Pole 轴时逐步转向，避免膝盖突然翻面。首次求解直接对齐。")]
        private float poleMaxStepAngle = 15.0f;
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
        private readonly HashSet<Transform> validatedLinks = new HashSet<Transform>();
        private Transform hingeReferenceBone;
        private Transform hingeReferenceRoot;
        private Transform hingeReferencePole;
        private Vector3 hingeReferenceAxis;
        private float hingeBendSign;
        private bool hasPolePlane;
        private Transform polePlaneParent;
        private Vector3 polePlaneDirection;
        private Vector3 polePlaneBend;

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
        public float PoleMaxStepAngle { get => poleMaxStepAngle; set => poleMaxStepAngle = Mathf.Clamp(value, 0.0f, 180.0f); }
        public bool RestoreBindRotation { get => restoreBindRotation; set => restoreBindRotation = value; }
        public bool UsePole { get => usePole; set => usePole = value; }
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
            poleMaxStepAngle = Mathf.Clamp(poleMaxStepAngle, 0.0f, 180.0f);
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
            hingeReferenceBone = null;
            hingeBendSign = 0.0f;
            hasPolePlane = false;
            if (usePole && pole != null && links.Count == 2 &&
                links[0] != null && links[0].bone != null && links[1] != null && links[1].bone != null &&
                effector != null && links[0].useAxisMask && EnabledAxisCount(links[0]) == 1)
                CaptureHingeReference(links[0], links[1].bone);
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
            if (TrySolveTwoBoneHinge())
                return;
            SeedStraightChain();

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
            validatedLinks.Clear();
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
                if (!validatedLinks.Add(link.bone))
                {
                    SetError("An IK chain cannot contain the same link twice.");
                    return false;
                }
                if (target.IsChildOf(link.bone))
                {
                    SetError("IK target is inside the solved chain; moving a link would move its own goal. Use an external control.");
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

            // A hinge must solve the angle in its own rotation plane. Projecting
            // the axis of an unconstrained FromToRotation keeps the wrong angle
            // and can overshoot badly when the goal is outside that plane.
            if (link.useAxisMask && EnabledAxisCount(link) == 1)
            {
                Vector3 localAxis = link.useX ? Vector3.right : link.useY ? Vector3.up : Vector3.forward;
                Vector3 worldAxis = link.bone.rotation * localAxis;
                Vector3 from = Vector3.ProjectOnPlane(toEffector, worldAxis);
                Vector3 to = Vector3.ProjectOnPlane(toTarget, worldAxis);
                if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return;
                float hingeStep = Mathf.Clamp(SignedAngle(from, to, worldAxis), -maxStepAngle, maxStepAngle) * weight;
                link.bone.rotation = Quaternion.AngleAxis(hingeStep, worldAxis) * link.bone.rotation;
                if (link.useLocalEulerLimits) ClampLocalEuler(link);
                return;
            }
            Vector3 fromDirection = toEffector.normalized;
            Vector3 toDirection = toTarget.normalized;
            Vector3 axis = Vector3.Cross(fromDirection, toDirection);
            float sine = axis.magnitude;
            float cosine = Mathf.Clamp(Vector3.Dot(fromDirection, toDirection), -1.0f, 1.0f);
            if (sine < 1e-7f)
            {
                if (cosine > 0) return;
                // Opposite directions have no unique axis. Pick a stable plane.
                axis = Vector3.Cross(fromDirection, link.bone.up);
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(fromDirection, link.bone.right);
            }
            float angle = Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg;
            float step = Mathf.Min(angle, maxStepAngle) * weight;
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
            Vector3 vector = new Vector3(delta.x, delta.y, delta.z);
            if (delta.w < 0.0f) vector = -vector;
            float sineHalf = vector.magnitude;
            if (sineHalf < 1e-8f) return link.bone.rotation;
            float angle = 2.0f * Mathf.Atan2(sineHalf, Mathf.Abs(delta.w)) * Mathf.Rad2Deg;
            Vector3 localAxis = Quaternion.Inverse(link.bone.rotation) * (vector / sineHalf);
            localAxis = new Vector3(link.useX ? localAxis.x : 0.0f, link.useY ? localAxis.y : 0.0f, link.useZ ? localAxis.z : 0.0f);
            if (localAxis.sqrMagnitude < 1e-10f) return link.bone.rotation;
            return Quaternion.AngleAxis(angle * Mathf.Clamp01(localAxis.magnitude), link.bone.rotation * localAxis.normalized) * link.bone.rotation;
        }

        private static int EnabledAxisCount(Link link) => (link.useX ? 1 : 0) + (link.useY ? 1 : 0) + (link.useZ ? 1 : 0);

        private static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            // atan2 retains small corrections; acos(dot) rounds to zero before
            // the configured sub-millimetre endpoint tolerance is reached.
            return Mathf.Atan2(Vector3.Dot(axis.normalized, Vector3.Cross(from, to)), Vector3.Dot(from, to)) * Mathf.Rad2Deg;
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

        private bool TrySolveTwoBoneHinge()
        {
            // The common ankle -> knee -> thigh case has a closed-form solve.
            // CCD alone can pick either hinge branch as the goal crosses behind
            // the leg, flipping thigh twist by 180 degrees despite a small
            // endpoint error. Preserve the authored bend branch explicitly.
            if (!usePole || pole == null || links.Count != 2 || maxStepAngle <= 0.0f)
                return false;
            Link hinge = links[0], rootLink = links[1];
            if (!hinge.useAxisMask || EnabledAxisCount(hinge) != 1 ||
                (rootLink.useAxisMask && EnabledAxisCount(rootLink) != 3) || rootLink.useLocalEulerLimits)
                return false;
            Transform joint = hinge.bone, root = rootLink.bone;
            if (joint.parent != root || effector.parent != joint || !HasUniformScale(root) || !HasUniformScale(joint))
                return false;

            Vector3 localAxis = hinge.useX ? Vector3.right : hinge.useY ? Vector3.up : Vector3.forward;
            Vector3 worldAxis = joint.rotation * localAxis;
            Vector3 upper = joint.position - root.position;
            Vector3 lower = effector.position - joint.position;
            if (upper.sqrMagnitude < 1e-10f || lower.sqrMagnitude < 1e-10f)
                return false;
            if (hingeReferenceBone != joint || hingeReferenceRoot != root || hingeReferencePole != pole ||
                hingeReferenceAxis != localAxis || hingeBendSign == 0.0f)
                CaptureHingeReference(hinge, root);

            Vector3 goalOffset = target.position - root.position;
            Vector3 upperPlane = Vector3.ProjectOnPlane(upper, worldAxis);
            Vector3 lowerPlane = Vector3.ProjectOnPlane(lower, worldAxis);
            float a = Vector3.Dot(upperPlane, lowerPlane);
            float b = Vector3.Dot(upperPlane, Vector3.Cross(worldAxis, lowerPlane));
            float radius = Mathf.Sqrt(a * a + b * b);
            if (radius < 1e-10f)
                return false;
            float constant = Vector3.Dot(upper, worldAxis) * Vector3.Dot(lower, worldAxis);
            float desiredDot = (goalOffset.sqrMagnitude - upper.sqrMagnitude - lower.sqrMagnitude) * 0.5f;
            float phase = Mathf.Atan2(b, a);
            float bend = Mathf.Acos(Mathf.Clamp((desiredDot - constant) / radius, -1.0f, 1.0f));
            float delta = NormalizeAngle((phase + hingeBendSign * bend) * Mathf.Rad2Deg);
            if (hinge.useLocalEulerLimits)
            {
                Quaternion preferred = joint.localRotation * Quaternion.AngleAxis(delta, localAxis);
                float alternateDelta = NormalizeAngle((phase - hingeBendSign * bend) * Mathf.Rad2Deg);
                Quaternion alternate = joint.localRotation * Quaternion.AngleAxis(alternateDelta, localAxis);
                if (!IsWithinLimits(hinge, preferred) && IsWithinLimits(hinge, alternate))
                {
                    delta = alternateDelta;
                    hingeBendSign = -hingeBendSign;
                }
            }
            float angleBudget = maxStepAngle * Mathf.Max(1, iterations);
            delta = Mathf.Clamp(delta, -angleBudget, angleBudget);
            Quaternion hingeBefore = joint.rotation;
            joint.rotation = Quaternion.AngleAxis(delta, worldAxis) * hingeBefore;
            if (hinge.useLocalEulerLimits) ClampLocalEuler(hinge);
            joint.rotation = Quaternion.Slerp(hingeBefore, joint.rotation, weight);

            Vector3 currentEnd = effector.position - root.position;
            if (currentEnd.sqrMagnitude < 1e-10f) return true;
            Vector3 direction = goalOffset.sqrMagnitude > 1e-10f ? goalOffset.normalized : currentEnd.normalized;
            Vector3 currentBend = Vector3.ProjectOnPlane(joint.position - root.position, currentEnd);
            if (currentBend.sqrMagnitude < upper.sqrMagnitude * 1e-8f)
                currentBend = Vector3.Cross(currentEnd, joint.rotation * localAxis) * hingeBendSign;
            if (currentBend.sqrMagnitude < 1e-10f)
                currentBend = Vector3.Cross(currentEnd, root.up);
            if (currentBend.sqrMagnitude < 1e-10f)
                currentBend = Vector3.Cross(currentEnd, root.right);
            Vector3 desiredBend = Vector3.ProjectOnPlane(pole.position - root.position, direction);
            Vector3 planeDirection = currentEnd;
            Vector3 planeBend = currentBend;
            bool hasPreviousPlane = hasPolePlane && polePlaneParent == root.parent;
            if (hasPreviousPlane)
            {
                Quaternion parentRotation = root.parent != null ? root.parent.rotation : Quaternion.identity;
                planeDirection = parentRotation * polePlaneDirection;
                planeBend = parentRotation * polePlaneBend;
            }
            Quaternion swing = StableFromTo(planeDirection, direction, planeBend);
            Vector3 swungBend = Vector3.ProjectOnPlane(swing * planeBend, direction);
            if (desiredBend.sqrMagnitude < 1e-10f)
                desiredBend = swungBend;
            if (desiredBend.sqrMagnitude < 1e-10f)
                desiredBend = Vector3.Cross(direction, root.right);
            if (desiredBend.sqrMagnitude < 1e-10f)
                desiredBend = Vector3.Cross(direction, root.up);
            if (swungBend.sqrMagnitude < 1e-10f)
                swungBend = desiredBend;
            float poleLimit = hasPreviousPlane ? poleMaxStepAngle : 180.0f;
            float poleTwist = Mathf.Clamp(SignedAngle(swungBend, desiredBend, direction), -poleLimit, poleLimit);
            Vector3 solvedBend = Quaternion.AngleAxis(poleTwist, direction) * swungBend;
            Quaternion currentFrame = Quaternion.LookRotation(currentEnd, currentBend);
            Quaternion desiredFrame = Quaternion.LookRotation(direction, solvedBend);
            Quaternion solvedRoot = desiredFrame * Quaternion.Inverse(currentFrame) * root.rotation;
            Quaternion rootBefore = root.rotation;
            Quaternion limitedRoot = Quaternion.RotateTowards(rootBefore, solvedRoot, angleBudget);
            root.rotation = Quaternion.Slerp(rootBefore, limitedRoot, weight);
            Vector3 finalDirection = (effector.position - root.position).normalized;
            Vector3 finalBend = Vector3.ProjectOnPlane(joint.position - root.position, finalDirection);
            if (finalBend.sqrMagnitude < upper.sqrMagnitude * 1e-8f)
                finalBend = Vector3.Cross(finalDirection, joint.rotation * localAxis) * hingeBendSign;
            if (finalBend.sqrMagnitude > 1e-10f)
            {
                Quaternion toParent = root.parent != null ? Quaternion.Inverse(root.parent.rotation) : Quaternion.identity;
                polePlaneDirection = toParent * finalDirection;
                polePlaneBend = toParent * finalBend.normalized;
                polePlaneParent = root.parent;
                hasPolePlane = true;
            }
            return true;
        }

        private void CaptureHingeReference(Link hinge, Transform root)
        {
            Vector3 localAxis = hinge.useX ? Vector3.right : hinge.useY ? Vector3.up : Vector3.forward;
            Vector3 upper = hinge.bone.position - root.position;
            Vector3 lower = effector.position - hinge.bone.position;
            Vector3 worldAxis = hinge.bone.rotation * localAxis;
            float sign = Vector3.Dot(worldAxis, Vector3.Cross(upper, lower));
            if (Mathf.Abs(sign) < upper.magnitude * lower.magnitude * 1e-4f)
                sign = Vector3.Dot(pole.position - root.position, Vector3.Cross(upper + lower, worldAxis));
            hingeBendSign = sign < 0.0f ? -1.0f : 1.0f;
            hingeReferenceBone = hinge.bone;
            hingeReferenceRoot = root;
            hingeReferencePole = pole;
            hingeReferenceAxis = localAxis;
            hasPolePlane = false;
        }

        private static Quaternion StableFromTo(Vector3 from, Vector3 to, Vector3 oppositeAxis)
        {
            from.Normalize();
            to.Normalize();
            Vector3 axis = Vector3.Cross(from, to);
            float sine = axis.magnitude;
            float cosine = Mathf.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
            if (sine < 1e-7f)
            {
                if (cosine > 0.0f) return Quaternion.identity;
                axis = Vector3.ProjectOnPlane(oppositeAxis, from);
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(from, Vector3.right);
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(from, Vector3.up);
            }
            return Quaternion.AngleAxis(Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg, axis.normalized);
        }

        private static bool IsWithinLimits(Link link, Quaternion rotation)
        {
            Vector3 euler = NormalizeEuler(rotation.eulerAngles);
            const float epsilon = 0.001f;
            return euler.x >= link.localEulerMin.x - epsilon && euler.x <= link.localEulerMax.x + epsilon &&
                euler.y >= link.localEulerMin.y - epsilon && euler.y <= link.localEulerMax.y + epsilon &&
                euler.z >= link.localEulerMin.z - epsilon && euler.z <= link.localEulerMax.z + epsilon;
        }

        private static bool HasUniformScale(Transform bone)
        {
            Vector3 scale = bone.lossyScale;
            float largest = Mathf.Max(scale.x, scale.y, scale.z);
            return scale.x > 0.0f && scale.y > 0.0f && scale.z > 0.0f &&
                Mathf.Max(Mathf.Abs(scale.x - scale.y), Mathf.Abs(scale.x - scale.z)) <= largest * 1e-4f;
        }

        private void SeedStraightChain()
        {
            if (!usePole || pole == null || links.Count != 2)
                return;
            Link rootLink = links[1];
            Transform root = rootLink.bone;
            Transform joint = links[0].bone;
            Vector3 toJoint = joint.position - root.position;
            Vector3 toEnd = effector.position - root.position;
            float upperLength = toJoint.magnitude;
            float lowerLength = Vector3.Distance(joint.position, effector.position);
            if (upperLength < 1e-5f || lowerLength < 1e-5f || toEnd.sqrMagnitude < 1e-10f)
                return;
            if (Vector3.Dot(toJoint, toEnd) <= 0.0f ||
                Vector3.ProjectOnPlane(toJoint, toEnd).sqrMagnitude > upperLength * upperLength * 1e-8f)
                return;

            Vector3 toGoal = target.position - root.position;
            float distance = toGoal.magnitude;
            if (distance >= upperLength + lowerLength - tolerance)
                return;
            Vector3 direction = distance > 1e-5f ? toGoal / distance : toEnd.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole.position - root.position, direction);
            if (bend.sqrMagnitude < 1e-10f) return;

            // At full extension CCD has zero angular gradient for an inward
            // goal. Seed the root toward the triangle's Pole-side joint, then
            // let the normal CCD steps enforce all joint masks and limits.
            float triangleDistance = Mathf.Max(distance, 1e-5f);
            float along = Mathf.Clamp((upperLength * upperLength + triangleDistance * triangleDistance -
                lowerLength * lowerLength) / (2.0f * triangleDistance), -upperLength, upperLength);
            float height = Mathf.Sqrt(Mathf.Max(0.0f, upperLength * upperLength - along * along));
            Vector3 desiredJoint = direction * along + bend.normalized * height;
            Vector3 axis = Vector3.Cross(toJoint, desiredJoint);
            if (axis.sqrMagnitude < 1e-10f) return;
            float angle = Mathf.Atan2(axis.magnitude, Vector3.Dot(toJoint, desiredJoint)) * Mathf.Rad2Deg;
            Quaternion desired = Quaternion.AngleAxis(Mathf.Min(angle, maxStepAngle) * weight, axis.normalized) * root.rotation;
            if (rootLink.useAxisMask) desired = ApplyAxisMask(rootLink, desired);
            root.rotation = desired;
            if (rootLink.useLocalEulerLimits) ClampLocalEuler(rootLink);
        }

        private void ApplyPole()
        {
            if (links.Count < 2 || effector == null)
                return;
            Link rootLink = links[links.Count - 1];
            Transform root = rootLink.bone;
            Transform joint = links[links.Count - 2].bone;
            // Rotate the whole chain only once, around root -> actual effector.
            // The effector lies on this axis, so its solved position is preserved.
            // Rotating every link separately rotates around different pivots and
            // throws the effector away from the goal on each frame.
            Vector3 axis = effector.position - root.position;
            if (axis.sqrMagnitude < 1e-10f) return;
            Vector3 current = Vector3.ProjectOnPlane(joint.position - root.position, axis);
            Vector3 desired = Vector3.ProjectOnPlane(pole.position - root.position, axis);
            if (current.sqrMagnitude < 1e-10f)
                current = Vector3.ProjectOnPlane(Vector3.Cross(axis, root.up), axis);
            if (current.sqrMagnitude < 1e-10f || desired.sqrMagnitude < 1e-10f) return;
            float signed = Mathf.Clamp(SignedAngle(current, desired, axis), -poleMaxStepAngle, poleMaxStepAngle) * weight;
            Quaternion before = root.localRotation;
            float beforeError = (effector.position - target.position).sqrMagnitude;
            Quaternion desiredRotation = Quaternion.AngleAxis(signed, axis.normalized) * root.rotation;
            if (rootLink.useAxisMask) desiredRotation = ApplyAxisMask(rootLink, desiredRotation);
            root.rotation = desiredRotation;
            if (rootLink.useLocalEulerLimits) ClampLocalEuler(rootLink);
            // A restricted root or non-uniform ancestor scale can make the orbit
            // impossible. Prefer the valid CCD result to a destructive Pole step.
            if ((effector.position - target.position).sqrMagnitude > beforeError + 1e-9f) root.localRotation = before;
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
                Transform knee = links != null && links.Count > 1 && links[links.Count - 2] != null ? links[links.Count - 2].bone : null;
                if (knee != null) Gizmos.DrawLine(knee.position, pole.position);
            }
        }
    }
}
