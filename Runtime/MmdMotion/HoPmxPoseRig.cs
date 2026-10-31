using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>PMX append channels for a non-physics pose reference rig.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/MMD/PMX Pose Rig")]
    public sealed class HoPmxPoseRig : MonoBehaviour
    {
        [Serializable] public sealed class Append
        {
            public Transform owner, source;
            public float ratio = 1;
            public bool rotation, position;
            public Vector3 restPosition, sourceRestPosition;
            public Quaternion restRotation = Quaternion.identity, sourceRestRotation = Quaternion.identity;
            [NonSerialized] public Vector3 sampledPosition;
            [NonSerialized] public Quaternion sampledRotation;
        }
        [SerializeField] private List<Append> appends = new List<Append>();
        public IList<Append> Appends => appends;
        bool sampled;

        public void BeginSample()
        {
            sampled = false;
            foreach (var item in appends)
            {
                if (item.owner == null) continue;
                if (item.rotation) item.owner.localRotation = item.restRotation;
                if (item.position) item.owner.localPosition = item.restPosition;
            }
        }
        public void CaptureInputsAndEvaluate()
        {
            foreach (var item in appends)
            {
                if (item.owner == null) continue;
                item.sampledRotation = item.owner.localRotation;
                item.sampledPosition = item.owner.localPosition;
            }
            sampled = true;
            EvaluateAppends();
        }
        public void EvaluateAppends()
        {
            if (!sampled) return;
            foreach (var item in appends)
            {
                if (item.owner == null || item.source == null) continue;
                if (item.rotation)
                {
                    Quaternion delta = item.source.localRotation * Quaternion.Inverse(item.sourceRestRotation);
                    item.owner.localRotation = item.sampledRotation * Quaternion.SlerpUnclamped(Quaternion.identity, delta, item.ratio);
                }
                if (item.position)
                    item.owner.localPosition = item.sampledPosition +
                        (item.source.localPosition - item.sourceRestPosition) * item.ratio;
            }
        }
    }
}
