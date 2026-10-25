using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Armatures
{
    /// <summary>
    /// Captures semantic binding data before an editor merge. The returned callback
    /// runs after reference remapping and hierarchy changes, before authoring markers
    /// are removed. The map includes retained offset bones and the source root.
    /// Implementations must not bake the current evaluated pose into their bindings.
    /// </summary>
    public interface IHoArmatureMergeParticipant
    {
        Action PrepareArmatureMerge(IReadOnlyDictionary<Transform, Transform> replacements);
    }
}
