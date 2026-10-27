using System;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.Editor.Pmx;

internal static class SyntheticValidation
{
    private static int assertions;

    internal static int Run()
    {
        assertions = 0;
        int countOffset, indexOffset;
        byte[] v21 = Fixture(true, false, out countOffset, out indexOffset);
        HoPmxDocument document = Read(v21);
        Assert(document.Version > 2.05f, "PMX 2.1 version");
        Assert(document.Vertices[0].WeightType == 4, "QDEF payload");
        Assert(document.Vertices[0].AdditionalUvs.Length == 4, "Four vec4 UV channels");
        Assert(document.Morphs.Count == 11 && document.Morphs[9].GroupOffsets.Count == 1 &&
            document.Morphs[10].ImpulseOffsets.Count == 1, "All eleven morph layouts");
        Assert(document.SoftBodyCount == 1 && document.JointCount == 6 && document.RigidBodyCount == 1,
            "Six joint types and soft-body anchors/pins");
        Assert(document.Textures[0].Length > 128, "Preserve long texture paths");
        Assert(document.Warnings.Count == 0, "Consume extended header bytes and all known sections");

        byte[] v20 = Fixture(false, true, out countOffset, out indexOffset);
        document = Read(v20);
        Assert(document.Vertices[0].WeightType == 3, "SDEF payload");
        Assert(document.Name == "模型模型", "UTF-16 text");
        Assert(document.Morphs.Count == 9 && document.SoftBodyCount == 0, "PMX 2.0 layout");

        byte[] bad = (byte[])v20.Clone();
        bad[countOffset] = 255; bad[countOffset + 1] = 255;
        bad[countOffset + 2] = 255; bad[countOffset + 3] = 127;
        Reject(bad, "hostile vertex allocation count");
        bad = (byte[])v20.Clone(); bad[indexOffset] = 99;
        Reject(bad, "out-of-range vertex reference");
        bad = new byte[v21.Length - 1]; Array.Copy(v21, bad, bad.Length);
        Reject(bad, "truncated soft-body pin");
        bad = (byte[])v21.Clone(); bad[0] = 0;
        Reject(bad, "invalid signature");
        Console.WriteLine("SYNTHETIC PASS " + assertions + " assertions");
        return assertions;
    }

    // Synthetic format fixtures contain no model or texture data copied from user assets.
    private static byte[] Fixture(bool v21, bool utf16, out int countOffset, out int indexOffset)
    {
        using (var memory = new MemoryStream())
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
        {
            writer.Write(new byte[] { 0x50, 0x4d, 0x58, 0x20 });
            writer.Write(v21 ? 2.1f : 2.0f);
            writer.Write((byte)9); writer.Write((byte)(utf16 ? 0 : 1)); writer.Write((byte)4);
            Bytes(writer, 6, 1); writer.Write((byte)244); // One unknown extended header byte.
            Text(writer, "模型模型", utf16); Text(writer, "model", utf16);
            Text(writer, "comment", utf16); Text(writer, "", utf16);
            countOffset = (int)memory.Position;
            writer.Write(1);
            Floats(writer, 3); Floats(writer, 3, 1); Floats(writer, 2); Floats(writer, 16);
            writer.Write((byte)(v21 ? 4 : 3));
            if (v21)
            {
                writer.Write((sbyte)0); Bytes(writer, 3, 255);
                writer.Write(1f); Floats(writer, 3);
            }
            else
            {
                writer.Write((sbyte)0); writer.Write((sbyte)-1);
                writer.Write(1f); Floats(writer, 9);
            }
            writer.Write(1f);
            writer.Write(3); indexOffset = (int)memory.Position; Bytes(writer, 3, 0);
            writer.Write(1); Text(writer, new string('a', 200) + "/texture.png", utf16);

            writer.Write(1);
            Text(writer, "材质", utf16); Text(writer, "material", utf16);
            Floats(writer, 4, 1); Floats(writer, 3); writer.Write(20f); Floats(writer, 3, .5f);
            writer.Write((byte)31); Floats(writer, 4, 1); writer.Write(1f);
            writer.Write((sbyte)0); writer.Write((sbyte)-1); writer.Write((byte)0);
            writer.Write((byte)1); writer.Write((byte)9); Text(writer, "memo", utf16); writer.Write(3);

            // Bone with tail, append, fixed axis, local axes, external parent and limited IK.
            writer.Write(1); Text(writer, "bone", utf16); Text(writer, "", utf16);
            Floats(writer, 3); writer.Write((sbyte)-1); writer.Write(0); writer.Write((ushort)0x2f21);
            writer.Write((sbyte)-1); writer.Write((sbyte)-1); writer.Write(.5f);
            Floats(writer, 3, 1); Floats(writer, 6); writer.Write(3);
            writer.Write((sbyte)0); writer.Write(4); writer.Write(.1f); writer.Write(1);
            writer.Write((sbyte)0); writer.Write((byte)1); Floats(writer, 6);

            writer.Write(v21 ? 11 : 9);
            for (byte type = 0; type < (v21 ? 11 : 9); type++)
            {
                Text(writer, "morph" + type, utf16); Text(writer, "", utf16);
                writer.Write((byte)4); writer.Write(type); writer.Write(1);
                writer.Write((byte)(type == 0 || type == 9 ? 1 : 0));
                switch (type)
                {
                    case 0: case 9: writer.Write(.5f); break;
                    case 1: Floats(writer, 3); break;
                    case 2: Floats(writer, 7); break;
                    case 3: case 4: case 5: case 6: case 7: Floats(writer, 4); break;
                    case 8: writer.Write((byte)0); Floats(writer, 28, 1); break;
                    case 10: writer.Write((byte)1); Floats(writer, 6); break;
                }
            }
            writer.Write(1); Text(writer, "root", utf16); Text(writer, "", utf16);
            writer.Write((byte)1); writer.Write(2);
            writer.Write((byte)0); writer.Write((sbyte)0);
            writer.Write((byte)1); writer.Write((sbyte)0);

            writer.Write(1); Text(writer, "rigid", utf16); Text(writer, "", utf16);
            writer.Write((sbyte)0); writer.Write((byte)0); writer.Write((ushort)0);
            writer.Write((byte)1); Floats(writer, 9); Floats(writer, 5); writer.Write((byte)1);
            writer.Write(v21 ? 6 : 1);
            for (byte type = 0; type < (v21 ? 6 : 1); type++)
            {
                Text(writer, "joint", utf16); Text(writer, "", utf16);
                writer.Write(type); writer.Write((sbyte)0); writer.Write((sbyte)0); Floats(writer, 24);
            }
            if (v21)
            {
                writer.Write(1); Text(writer, "soft", utf16); Text(writer, "", utf16);
                writer.Write((byte)0); writer.Write((sbyte)0); writer.Write((byte)0);
                writer.Write((ushort)0); writer.Write((byte)0); writer.Write(0); writer.Write(0);
                writer.Write(1f); writer.Write(.05f); writer.Write(0);
                Floats(writer, 18);
                for (int i = 0; i < 4; i++) writer.Write(0);
                Floats(writer, 3, 1);
                writer.Write(1); writer.Write((sbyte)0); writer.Write((byte)0); writer.Write((byte)1);
                writer.Write(1); writer.Write((byte)0);
            }
            return memory.ToArray();
        }
    }

    private static void Text(BinaryWriter writer, string value, bool utf16)
    {
        byte[] bytes = (utf16 ? Encoding.Unicode : Encoding.UTF8).GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static void Floats(BinaryWriter writer, int count, float value = 0)
    { for (int i = 0; i < count; i++) writer.Write(value); }
    private static void Bytes(BinaryWriter writer, int count, byte value)
    { for (int i = 0; i < count; i++) writer.Write(value); }
    private static HoPmxDocument Read(byte[] data)
    { using (var memory = new MemoryStream(data)) return HoPmxReader.Read(memory); }
    private static void Assert(bool condition, string label)
    { if (!condition) throw new Exception(label); ++assertions; }
    private static void Reject(byte[] data, string label)
    {
        try { Read(data); }
        catch (InvalidDataException) { ++assertions; return; }
        throw new Exception("Parser accepted " + label);
    }
}
