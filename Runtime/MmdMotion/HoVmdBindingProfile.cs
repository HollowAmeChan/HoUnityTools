using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>Saved source-name to target-path mapping for a Unity FBX/GLTF character.</summary>
    [CreateAssetMenu(menuName = "Ho/MMD/VMD Binding Profile", fileName = "VMD Binding Profile")]
    public sealed class HoVmdBindingProfile : ScriptableObject
    {
        [Serializable] public sealed class BoneMap { public string sourceName; public string targetPath; }
        [Serializable] public sealed class MorphMap { public string sourceName; public string rendererPath; public string blendShapeName; }
        [Serializable] public sealed class IkMap { public string sourceName; public string constraintPath; public string constraintName; }

        [Tooltip("Optional model identity from the VMD header or the author's README.")]
        public string sourceModel;
        public List<BoneMap> bones = new List<BoneMap>();
        public List<MorphMap> morphs = new List<MorphMap>();
        public List<IkMap> ik = new List<IkMap>();

        public bool TryBone(string sourceName, out string targetPath)
        {
            for (int i = 0; i < bones.Count; i++)
                if (bones[i] != null && bones[i].sourceName == sourceName)
                { targetPath = bones[i].targetPath; return true; }
            targetPath = null; return false;
        }

        public bool TryIk(string sourceName, out IkMap result)
        {
            for (int i = 0; i < ik.Count; i++)
                if (ik[i] != null && ik[i].sourceName == sourceName)
                { result = ik[i]; return true; }
            result = null; return false;
        }
    }
}
