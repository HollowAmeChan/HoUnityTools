using System;
using System.IO;
using System.Text;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    /// <summary>
    /// Bounded PMX 2.0/2.1 reader. Reads source values unchanged, including complete names and paths.
    /// Field layout follows the PMX author specification (PmxEditor 0.2.7.3, PMX仕様.txt).
    /// UnityMMDTools was researched as an MIT-licensed interoperability reference; no runtime is required.
    /// </summary>
    public static class HoPmxReader
    {
        public static HoPmxDocument Read(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072))
                return Read(stream);
        }

        public static HoPmxDocument Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("PMX source must be a readable, seekable stream.", nameof(stream));
            using (var reader = new Reader(stream)) return reader.ReadDocument();
        }

        private sealed class Reader : IDisposable
        {
            private const int MaximumElements = 16000000;
            private const int MaximumTextBytes = 16777216;
            private readonly BinaryReader input;
            private Encoding encoding;
            private byte vertexSize, textureSize, materialSize, boneSize, morphSize, rigidSize;
            private HoPmxDocument document;
            private string section = "header";
            private long Remaining { get { return input.BaseStream.Length - input.BaseStream.Position; } }

            internal Reader(Stream stream) { input = new BinaryReader(stream, Encoding.UTF8, true); }
            public void Dispose() { input.Dispose(); }

            internal HoPmxDocument ReadDocument()
            {
                try
                {
                    document = new HoPmxDocument();
                    Header();
                    section = "model information";
                    document.Name = Text(); document.EnglishName = Text();
                    document.Comment = Text(); document.EnglishComment = Text();
                    Vertices(); Indices(); Textures(); Materials(); Bones(); Morphs();
                    DisplayFrames(); RigidBodies(); Joints();
                    if (document.Version > 2.05f) SoftBodies();
                    ValidateReferences();
                    if (Remaining != 0)
                        document.Warnings.Add("PMX has " + Remaining + " trailing bytes after its declared sections.");
                    return document;
                }
                catch (EndOfStreamException exception)
                {
                    throw Error("Unexpected end of file", exception);
                }
                catch (DecoderFallbackException exception)
                {
                    throw Error("Invalid encoded text", exception);
                }
                catch (InvalidDataException exception)
                {
                    if (exception.Message.StartsWith("PMX ", StringComparison.Ordinal)) throw;
                    throw Error(exception.Message, exception);
                }
            }

            private void Header()
            {
                Need(9);
                if (input.ReadByte() != 0x50 || input.ReadByte() != 0x4d || input.ReadByte() != 0x58 || input.ReadByte() != 0x20)
                    throw Error("Expected the PMX signature");
                document.Version = Float();
                if (Math.Abs(document.Version - 2.0f) > 0.0001f && Math.Abs(document.Version - 2.1f) > 0.0001f)
                    throw Error("Only PMX 2.0 and 2.1 are supported; found " + document.Version);
                byte dataSize = input.ReadByte();
                if (dataSize < 8) throw Error("Header data length is less than 8");
                Need(dataSize);
                byte textEncoding = input.ReadByte();
                if (textEncoding > 1) throw Error("Invalid text encoding " + textEncoding);
                encoding = textEncoding == 0 ? (Encoding)new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
                document.AdditionalUvCount = input.ReadByte();
                if (document.AdditionalUvCount > 4) throw Error("Additional UV count exceeds 4");
                vertexSize = IndexSize(); textureSize = IndexSize(); materialSize = IndexSize();
                boneSize = IndexSize(); morphSize = IndexSize(); rigidSize = IndexSize();
                Skip(dataSize - 8);
            }

            private byte IndexSize()
            {
                byte result = input.ReadByte();
                if (result != 1 && result != 2 && result != 4) throw Error("Index byte width must be 1, 2 or 4");
                return result;
            }

            private void Vertices()
            {
                section = "vertices";
                int minimum = 32 + document.AdditionalUvCount * 16 + 1 + boneSize + 4;
                int count = Count(minimum);
                document.Vertices.Capacity = count;
                for (int n = 0; n < count; n++)
                {
                    var v = new HoPmxVertex
                    {
                        Position = Vec3(), Normal = Vec3(), Uv = Vec2(),
                        Bone0 = -1, Bone1 = -1, Bone2 = -1, Bone3 = -1,
                        AdditionalUvs = document.AdditionalUvCount == 0 ? Array.Empty<HoPmxVector4>() : new HoPmxVector4[document.AdditionalUvCount]
                    };
                    for (int uv = 0; uv < v.AdditionalUvs.Length; uv++) v.AdditionalUvs[uv] = Vec4();
                    v.WeightType = input.ReadByte();
                    switch (v.WeightType)
                    {
                        case 0: v.Bone0 = Index(boneSize); v.Weight0 = 1; break;
                        case 1:
                        case 3:
                            v.Bone0 = Index(boneSize); v.Bone1 = Index(boneSize);
                            v.Weight0 = Float(); v.Weight1 = 1 - v.Weight0;
                            if (v.WeightType == 3) { v.SdefC = Vec3(); v.SdefR0 = Vec3(); v.SdefR1 = Vec3(); }
                            break;
                        case 2:
                        case 4:
                            if (v.WeightType == 4 && document.Version < 2.05f) throw Error("QDEF requires PMX 2.1");
                            v.Bone0 = Index(boneSize); v.Bone1 = Index(boneSize); v.Bone2 = Index(boneSize); v.Bone3 = Index(boneSize);
                            v.Weight0 = Float(); v.Weight1 = Float(); v.Weight2 = Float(); v.Weight3 = Float();
                            break;
                        default: throw Error("Unknown vertex weight type " + v.WeightType + " at vertex " + n);
                    }
                    v.EdgeScale = Float();
                    document.Vertices.Add(v);
                }
            }

            private void Indices()
            {
                section = "triangle indices";
                int count = Count(vertexSize);
                if (count % 3 != 0) throw Error("Triangle index count is not a multiple of three");
                document.Indices = new int[count];
                for (int n = 0; n < count; n++) document.Indices[n] = VertexIndex();
            }

            private void Textures()
            {
                section = "texture paths";
                int count = Count(4); document.Textures.Capacity = count;
                for (int n = 0; n < count; n++) document.Textures.Add(Text());
            }

            private void Materials()
            {
                section = "materials";
                int count = Count(8 + 65 + textureSize * 2 + 2 + 1 + 4 + 4);
                document.Materials.Capacity = count;
                long indexTotal = 0;
                for (int n = 0; n < count; n++)
                {
                    var m = new HoPmxMaterial
                    {
                        Name = Text(), EnglishName = Text(), Diffuse = Vec4(), Specular = Vec3(),
                        Shininess = Float(), Ambient = Vec3(), Flags = input.ReadByte(),
                        EdgeColor = Vec4(), EdgeSize = Float(), TextureIndex = Index(textureSize),
                        SphereTextureIndex = Index(textureSize), SphereMode = input.ReadByte()
                    };
                    if (m.SphereMode > 3) throw Error("Unknown sphere mode on material " + n);
                    m.SharedToon = Flag();
                    m.ToonIndex = m.SharedToon ? input.ReadByte() : Index(textureSize);
                    if (m.SharedToon && m.ToonIndex > 9) throw Error("Shared toon index exceeds 9 on material " + n);
                    m.Comment = Text(); m.IndexCount = input.ReadInt32();
                    if (m.IndexCount < 0 || m.IndexCount % 3 != 0) throw Error("Invalid index count on material " + n);
                    indexTotal += m.IndexCount;
                    if (indexTotal > document.Indices.Length) throw Error("Material index ranges exceed triangle index count");
                    document.Materials.Add(m);
                }
                if (indexTotal != document.Indices.Length) throw Error("Material index ranges do not cover the triangle index buffer");
            }

            private void Bones()
            {
                section = "bones";
                int count = Count(8 + 12 + boneSize + 4 + 2 + Math.Min(boneSize, (byte)12));
                document.Bones.Capacity = count;
                for (int n = 0; n < count; n++)
                {
                    var b = new HoPmxBone
                    {
                        Name = Text(), EnglishName = Text(), Position = Vec3(), ParentIndex = Index(boneSize),
                        TransformLevel = input.ReadInt32(), Flags = input.ReadUInt16()
                    };
                    if ((b.Flags & 0x0001) != 0) b.TailIndex = Index(boneSize); else b.TailOffset = Vec3();
                    if ((b.Flags & 0x0300) != 0) { b.AppendParentIndex = Index(boneSize); b.AppendRatio = Float(); }
                    if ((b.Flags & 0x0400) != 0) b.FixedAxis = Vec3();
                    if ((b.Flags & 0x0800) != 0) { b.LocalAxisX = Vec3(); b.LocalAxisZ = Vec3(); }
                    if ((b.Flags & 0x2000) != 0) b.ExternalParentKey = input.ReadInt32();
                    if ((b.Flags & 0x0020) != 0)
                    {
                        b.IkTargetIndex = Index(boneSize); b.IkIterations = input.ReadInt32(); b.IkAngleLimit = Float();
                        if (b.IkIterations < 0) throw Error("Negative IK iteration count");
                        int links = Count(boneSize + 1); b.IkLinks.Capacity = links;
                        for (int k = 0; k < links; k++)
                        {
                            var link = new HoPmxIkLink { BoneIndex = Index(boneSize), HasLimits = Flag() };
                            if (link.HasLimits) { link.Minimum = Vec3(); link.Maximum = Vec3(); }
                            b.IkLinks.Add(link);
                        }
                    }
                    document.Bones.Add(b);
                }
            }

            private void Morphs()
            {
                section = "morphs";
                int count = Count(14); document.Morphs.Capacity = count;
                for (int n = 0; n < count; n++)
                {
                    var m = new HoPmxMorph { Name = Text(), EnglishName = Text(), Panel = input.ReadByte(), Type = input.ReadByte() };
                    int minimum;
                    switch (m.Type)
                    {
                        case 0: case 9: minimum = morphSize + 4; break;
                        case 1: minimum = vertexSize + 12; break;
                        case 2: minimum = boneSize + 28; break;
                        case 3: case 4: case 5: case 6: case 7: minimum = vertexSize + 16; break;
                        case 8: minimum = materialSize + 113; break;
                        case 10: minimum = rigidSize + 25; break;
                        default: throw Error("Unknown morph type " + m.Type);
                    }
                    if (m.Type >= 9 && document.Version < 2.05f) throw Error("Morph type " + m.Type + " requires PMX 2.1");
                    int offsets = Count(minimum);
                    for (int k = 0; k < offsets; k++)
                    {
                        switch (m.Type)
                        {
                            case 0: case 9:
                                m.GroupOffsets.Add(new HoPmxGroupMorphOffset { MorphIndex = Index(morphSize), Weight = Float() }); break;
                            case 1:
                                m.VertexOffsets.Add(new HoPmxVertexMorphOffset { VertexIndex = VertexIndex(), Offset = Vec3() }); break;
                            case 2:
                                m.BoneOffsets.Add(new HoPmxBoneMorphOffset { BoneIndex = Index(boneSize), Translation = Vec3(), Rotation = Vec4() }); break;
                            case 3: case 4: case 5: case 6: case 7:
                                m.UvOffsets.Add(new HoPmxUvMorphOffset { VertexIndex = VertexIndex(), Offset = Vec4() }); break;
                            case 8:
                                var offset = new HoPmxMaterialMorphOffset
                                {
                                    MaterialIndex = Index(materialSize), Operation = input.ReadByte(), Diffuse = Vec4(), Specular = Vec3(),
                                    Shininess = Float(), Ambient = Vec3(), EdgeColor = Vec4(), EdgeSize = Float(),
                                    TextureTint = Vec4(), SphereTint = Vec4(), ToonTint = Vec4()
                                };
                                if (offset.Operation > 1) throw Error("Unknown material morph operation " + offset.Operation);
                                m.MaterialOffsets.Add(offset); break;
                            case 10:
                                m.ImpulseOffsets.Add(new HoPmxImpulseMorphOffset { RigidBodyIndex = Index(rigidSize), IsLocal = Flag(), Velocity = Vec3(), Torque = Vec3() }); break;
                        }
                    }
                    document.Morphs.Add(m);
                }
            }

            private void DisplayFrames()
            {
                section = "display frames";
                document.DisplayFrameCount = Count(13);
                for (int n = 0; n < document.DisplayFrameCount; n++)
                {
                    Text(); Text(); Flag();
                    int count = Count(1 + Math.Min(boneSize, morphSize));
                    for (int k = 0; k < count; k++)
                    {
                        bool morph = Flag();
                        int index = Index(morph ? morphSize : boneSize);
                        Reference(index, morph ? document.Morphs.Count : document.Bones.Count, true, "display element");
                    }
                }
            }

            private void RigidBodies()
            {
                section = "rigid bodies";
                document.RigidBodyCount = Count(8 + boneSize + 1 + 2 + 1 + 36 + 20 + 1);
                for (int n = 0; n < document.RigidBodyCount; n++)
                {
                    Text(); Text(); Reference(Index(boneSize), document.Bones.Count, true, "rigid bone");
                    input.ReadByte(); input.ReadUInt16(); byte shape = input.ReadByte();
                    if (shape > 2) throw Error("Unknown rigid body shape " + shape);
                    Vec3(); Vec3(); Vec3();
                    for (int k = 0; k < 5; k++) Float();
                    byte mode = input.ReadByte();
                    if (mode > 2) throw Error("Unknown rigid body mode " + mode);
                }
            }

            private void Joints()
            {
                section = "joints";
                document.JointCount = Count(9 + rigidSize * 2 + 96);
                for (int n = 0; n < document.JointCount; n++)
                {
                    Text(); Text(); byte type = input.ReadByte();
                    if (type > 5 || (document.Version < 2.05f && type != 0)) throw Error("Unsupported joint type " + type);
                    Reference(Index(rigidSize), document.RigidBodyCount, true, "joint body A");
                    Reference(Index(rigidSize), document.RigidBodyCount, true, "joint body B");
                    for (int k = 0; k < 8; k++) Vec3();
                }
            }

            private void SoftBodies()
            {
                section = "soft bodies";
                document.SoftBodyCount = Count(8 + 1 + materialSize + 4 + 20 + 84 + 8);
                for (int n = 0; n < document.SoftBodyCount; n++)
                {
                    Text(); Text(); byte shape = input.ReadByte();
                    if (shape > 1) throw Error("Unknown soft body shape " + shape);
                    Reference(Index(materialSize), document.Materials.Count, false, "soft body material");
                    input.ReadByte(); input.ReadUInt16(); input.ReadByte();
                    input.ReadInt32(); input.ReadInt32(); Float(); Float(); input.ReadInt32();
                    for (int k = 0; k < 18; k++) Float();
                    for (int k = 0; k < 4; k++) input.ReadInt32();
                    for (int k = 0; k < 3; k++) Float();
                    int anchors = Count(rigidSize + vertexSize + 1);
                    for (int k = 0; k < anchors; k++)
                    {
                        Reference(Index(rigidSize), document.RigidBodyCount, false, "soft body anchor");
                        VertexIndex(); Flag();
                    }
                    int pins = Count(vertexSize);
                    for (int k = 0; k < pins; k++) VertexIndex();
                }
            }

            private void ValidateReferences()
            {
                section = "reference validation";
                foreach (var m in document.Materials)
                {
                    Reference(m.TextureIndex, document.Textures.Count, true, "material base texture");
                    Reference(m.SphereTextureIndex, document.Textures.Count, true, "material sphere texture");
                    if (!m.SharedToon) Reference(m.ToonIndex, document.Textures.Count, true, "material toon texture");
                }
                foreach (var v in document.Vertices)
                {
                    Reference(v.Bone0, document.Bones.Count, true, "vertex bone 0");
                    Reference(v.Bone1, document.Bones.Count, true, "vertex bone 1");
                    Reference(v.Bone2, document.Bones.Count, true, "vertex bone 2");
                    Reference(v.Bone3, document.Bones.Count, true, "vertex bone 3");
                }
                foreach (var b in document.Bones)
                {
                    Reference(b.ParentIndex, document.Bones.Count, true, "bone parent");
                    Reference(b.TailIndex, document.Bones.Count, true, "bone tail");
                    Reference(b.AppendParentIndex, document.Bones.Count, true, "bone append");
                    Reference(b.IkTargetIndex, document.Bones.Count, true, "IK target");
                    foreach (var link in b.IkLinks) Reference(link.BoneIndex, document.Bones.Count, true, "IK link");
                }
                foreach (var m in document.Morphs)
                {
                    foreach (var o in m.GroupOffsets) Reference(o.MorphIndex, document.Morphs.Count, true, "group morph");
                    foreach (var o in m.MaterialOffsets) Reference(o.MaterialIndex, document.Materials.Count, true, "material morph");
                    foreach (var o in m.BoneOffsets) Reference(o.BoneIndex, document.Bones.Count, true, "bone morph");
                    foreach (var o in m.ImpulseOffsets) Reference(o.RigidBodyIndex, document.RigidBodyCount, true, "impulse morph");
                }
            }

            private void Reference(int index, int count, bool optional, string label)
            {
                if (optional && index == -1) return;
                if (index < 0 || index >= count) throw Error(label + " index " + index + " is outside 0.." + (count - 1));
            }

            private int VertexIndex()
            {
                uint value = vertexSize == 1 ? input.ReadByte() : vertexSize == 2 ? input.ReadUInt16() : input.ReadUInt32();
                if (value >= document.Vertices.Count) throw Error("Vertex index " + value + " exceeds vertex count " + document.Vertices.Count);
                return (int)value;
            }

            private int Index(byte size)
            {
                if (size == 1) return input.ReadSByte();
                if (size == 2) return input.ReadInt16();
                return input.ReadInt32();
            }

            private bool Flag()
            {
                byte value = input.ReadByte();
                if (value > 1) throw Error("Expected a 0/1 flag, found " + value);
                return value == 1;
            }

            private int Count(int minimumBytes)
            {
                int count = input.ReadInt32();
                if (count < 0 || count > MaximumElements) throw Error("Invalid element count " + count);
                if ((long)count * minimumBytes > Remaining) throw Error("Element count " + count + " exceeds remaining file data");
                return count;
            }

            private string Text()
            {
                int bytes = input.ReadInt32();
                if (bytes < 0 || bytes > MaximumTextBytes) throw Error("Invalid text byte length " + bytes);
                if (encoding is UnicodeEncoding && (bytes & 1) != 0) throw Error("Odd UTF-16 text byte length");
                Need(bytes);
                return encoding.GetString(input.ReadBytes(bytes));
            }

            private float Float()
            {
                float value = input.ReadSingle();
                if (float.IsNaN(value) || float.IsInfinity(value)) throw Error("Non-finite floating point value");
                return value;
            }

            private HoPmxVector2 Vec2() { return new HoPmxVector2(Float(), Float()); }
            private HoPmxVector3 Vec3() { return new HoPmxVector3(Float(), Float(), Float()); }
            private HoPmxVector4 Vec4() { return new HoPmxVector4(Float(), Float(), Float(), Float()); }
            private void Need(long bytes) { if (bytes < 0 || bytes > Remaining) throw new EndOfStreamException(); }
            private void Skip(int bytes) { Need(bytes); input.BaseStream.Seek(bytes, SeekOrigin.Current); }
            private InvalidDataException Error(string message, Exception cause = null)
            {
                return new InvalidDataException("PMX " + section + " at byte " + input.BaseStream.Position + ": " + message + ".", cause);
            }
        }
    }
}
