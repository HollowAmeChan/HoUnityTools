using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    // Raw PMX values: no coordinate, scale, material or runtime solver conversion occurs here.
    [Serializable]
    public struct HoPmxVector2
    {
        public float X, Y;
        public HoPmxVector2(float x, float y) { X = x; Y = y; }
    }

    [Serializable]
    public struct HoPmxVector3
    {
        public float X, Y, Z;
        public HoPmxVector3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    [Serializable]
    public struct HoPmxVector4
    {
        public float X, Y, Z, W;
        public HoPmxVector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    }

    [Serializable]
    public sealed class HoPmxDocument
    {
        public float Version;
        public string Name, EnglishName, Comment, EnglishComment;
        public int AdditionalUvCount;
        public List<HoPmxVertex> Vertices = new List<HoPmxVertex>();
        public int[] Indices = Array.Empty<int>();
        public List<string> Textures = new List<string>();
        public List<HoPmxMaterial> Materials = new List<HoPmxMaterial>();
        public List<HoPmxBone> Bones = new List<HoPmxBone>();
        public List<HoPmxMorph> Morphs = new List<HoPmxMorph>();
        public int DisplayFrameCount, RigidBodyCount, JointCount, SoftBodyCount;
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public struct HoPmxVertex
    {
        public HoPmxVector3 Position, Normal;
        public HoPmxVector2 Uv;
        public HoPmxVector4[] AdditionalUvs;
        public byte WeightType;
        public int Bone0, Bone1, Bone2, Bone3;
        public float Weight0, Weight1, Weight2, Weight3;
        public HoPmxVector3 SdefC, SdefR0, SdefR1;
        public float EdgeScale;
    }

    [Serializable]
    public sealed class HoPmxMaterial
    {
        public string Name, EnglishName;
        public HoPmxVector4 Diffuse;
        public HoPmxVector3 Specular;
        public float Shininess;
        public HoPmxVector3 Ambient;
        public byte Flags;
        public HoPmxVector4 EdgeColor;
        public float EdgeSize;
        public int TextureIndex, SphereTextureIndex;
        public byte SphereMode;
        public bool SharedToon;
        public int ToonIndex;
        public string Comment;
        public int IndexCount;
    }

    [Serializable]
    public sealed class HoPmxBone
    {
        public string Name, EnglishName;
        public HoPmxVector3 Position;
        public int ParentIndex, TransformLevel;
        public ushort Flags;
        public int TailIndex = -1;
        public HoPmxVector3 TailOffset;
        public int AppendParentIndex = -1;
        public float AppendRatio;
        public HoPmxVector3 FixedAxis, LocalAxisX, LocalAxisZ;
        public int ExternalParentKey;
        public int IkTargetIndex = -1, IkIterations;
        public float IkAngleLimit;
        public List<HoPmxIkLink> IkLinks = new List<HoPmxIkLink>();
    }

    [Serializable]
    public struct HoPmxIkLink
    {
        public int BoneIndex;
        public bool HasLimits;
        public HoPmxVector3 Minimum, Maximum;
    }

    [Serializable]
    public sealed class HoPmxMorph
    {
        public string Name, EnglishName;
        public byte Panel, Type;
        public List<HoPmxGroupMorphOffset> GroupOffsets = new List<HoPmxGroupMorphOffset>();
        public List<HoPmxVertexMorphOffset> VertexOffsets = new List<HoPmxVertexMorphOffset>();
        public List<HoPmxBoneMorphOffset> BoneOffsets = new List<HoPmxBoneMorphOffset>();
        public List<HoPmxUvMorphOffset> UvOffsets = new List<HoPmxUvMorphOffset>();
        public List<HoPmxMaterialMorphOffset> MaterialOffsets = new List<HoPmxMaterialMorphOffset>();
        public List<HoPmxImpulseMorphOffset> ImpulseOffsets = new List<HoPmxImpulseMorphOffset>();
    }

    [Serializable]
    public struct HoPmxGroupMorphOffset
    {
        public int MorphIndex;
        public float Weight;
    }

    [Serializable]
    public struct HoPmxVertexMorphOffset
    {
        public int VertexIndex;
        public HoPmxVector3 Offset;
    }

    [Serializable]
    public struct HoPmxBoneMorphOffset
    {
        public int BoneIndex;
        public HoPmxVector3 Translation;
        public HoPmxVector4 Rotation;
    }

    [Serializable]
    public struct HoPmxUvMorphOffset
    {
        public int VertexIndex;
        public HoPmxVector4 Offset;
    }

    [Serializable]
    public struct HoPmxMaterialMorphOffset
    {
        public int MaterialIndex;
        public byte Operation;
        public HoPmxVector4 Diffuse;
        public HoPmxVector3 Specular;
        public float Shininess;
        public HoPmxVector3 Ambient;
        public HoPmxVector4 EdgeColor;
        public float EdgeSize;
        public HoPmxVector4 TextureTint, SphereTint, ToonTint;
    }

    [Serializable]
    public struct HoPmxImpulseMorphOffset
    {
        public int RigidBodyIndex;
        public bool IsLocal;
        public HoPmxVector3 Velocity, Torque;
    }
}
