using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    public enum HoPmxAlphaMode { Auto, Opaque, Cutout, Transparent }

    [Serializable]
    public sealed class HoPmxMaterialSetting
    {
        public int materialIndex;
        public HoPmxAlphaMode alphaMode;
        [Range(0, 1)] public float alphaCutoff = 0.5f;
    }

    [Serializable]
    public sealed class HoPmxMorphSetting
    {
        public int morphIndex;
        [Range(0, 1)] public float weight;
    }

    // These are source and conversion facts, independent of generated/extracted Material assets.
    [Serializable]
    public sealed class HoPmxMaterialInfo
    {
        public int index;
        public string sourceName;
        public string identifier;
        public string memo;
        public Color diffuse;
        public Color ambient;
        public Color specular;
        public float shininess;
        public int sourceFlags;
        public Color edgeColor;
        public float edgeSize;
        public string baseTexturePath;
        public string toonTexturePath;
        public int sharedToonIndex = -1;
        public string sphereTexturePath;
        public int sphereMode;
        public string rayEffectPath;
        public HoPmxAlphaMode alphaMode;
        public string alphaReason;
        public float alphaCutoff;
        public Material material;
    }

    [Serializable]
    public sealed class HoPmxMorphInfo
    {
        public int index;
        public string sourceName;
        public int type;
        public string behavior;
    }

    public sealed class HoPmxImportReport : ScriptableObject
    {
        public string sourceName;
        public float pmxVersion;
        public int vertexCount;
        public int triangleCount;
        public int boneCount;
        public int rigidBodyCount;
        public int jointCount;
        public int softBodyCount;
        public string rayMappingPath;
        public List<HoPmxMaterialInfo> materials = new List<HoPmxMaterialInfo>();
        public List<HoPmxMorphInfo> morphs = new List<HoPmxMorphInfo>();
        public List<string> warnings = new List<string>();
        public List<string> dependencies = new List<string>();
    }
}
