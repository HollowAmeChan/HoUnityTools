using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>Reservations prevent Ho shape writers (including their cleanup) overwriting a tracked property.</summary>
    public static class HoFaceOutputOwnership
    {
        private static readonly Dictionary<(SkinnedMeshRenderer, int), object> Owners = new Dictionary<(SkinnedMeshRenderer, int), object>();

        public static bool IsReserved(SkinnedMeshRenderer mesh, int index) => mesh != null && Owners.ContainsKey((mesh, index));

        public static void Reserve(SkinnedMeshRenderer mesh, int index, object owner)
        {
            if (Owners.TryGetValue((mesh, index), out object existing) && !ReferenceEquals(existing, owner))
                throw new InvalidOperationException("此形态键已由另一个面捕会话接管。");
            Owners[(mesh, index)] = owner;
        }

        public static void Release(object owner)
        {
            var remove = new List<(SkinnedMeshRenderer, int)>();
            foreach (var pair in Owners) if (ReferenceEquals(pair.Value, owner)) remove.Add(pair.Key);
            foreach (var key in remove) Owners.Remove(key);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Owners.Clear();
    }
}
