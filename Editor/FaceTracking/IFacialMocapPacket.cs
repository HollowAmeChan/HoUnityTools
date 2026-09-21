using System;
using System.Globalization;
using Hollow.HoUnityTools.FaceTracking;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>One immutable-after-publication datagram. No Unity API on the receiving thread.</summary>
    public sealed class IFacialMocapPacket
    {
        public readonly float[] Values = new float[52];
        public readonly bool[] Present = new bool[52];
        public float[] Head, LeftEye, RightEye;
        public int ShapeCount, UnknownCount, InvalidCount;

        public static bool TryParse(string text, out IFacialMocapPacket packet)
        {
            packet = new IFacialMocapPacket();
            if (string.IsNullOrEmpty(text) || text.Length > 16384) return false;
            string[] fields = text.Split('|');
            if (fields.Length > 256) return false;
            foreach (string raw in fields)
            {
                string field = raw.Trim();
                if (field.Length == 0) continue;
                if (field.StartsWith("=head#", StringComparison.Ordinal) || field.StartsWith("head#", StringComparison.Ordinal))
                    packet.Head = ParsePose(field, 6, packet);
                else if (field.StartsWith("leftEye#", StringComparison.Ordinal)) packet.LeftEye = ParsePose(field, 3, packet);
                else if (field.StartsWith("rightEye#", StringComparison.Ordinal)) packet.RightEye = ParsePose(field, 3, packet);
                else
                {
                    int separator = field.IndexOf('&');
                    if (separator < 0) separator = field.IndexOf('-');
                    if (separator <= 0) { packet.InvalidCount++; continue; }
                    int index = HoFaceTrackingChannels.IndexOf(field.Substring(0, separator));
                    if (index < 0) { packet.UnknownCount++; continue; }
                    if (!TryNumber(field.Substring(separator + 1), out float value)) { packet.InvalidCount++; continue; }
                    if (!packet.Present[index]) packet.ShapeCount++;
                    packet.Present[index] = true;
                    packet.Values[index] = value / 100f;
                }
            }
            return packet.ShapeCount > 0;
        }

        private static float[] ParsePose(string field, int count, IFacialMocapPacket packet)
        {
            string[] fields = field.Substring(field.IndexOf('#') + 1).Split(',');
            if (fields.Length != count) { packet.InvalidCount++; return null; }
            var values = new float[count];
            for (int i = 0; i < count; i++)
                if (!TryNumber(fields[i], out values[i])) { packet.InvalidCount++; return null; }
            return values;
        }

        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
