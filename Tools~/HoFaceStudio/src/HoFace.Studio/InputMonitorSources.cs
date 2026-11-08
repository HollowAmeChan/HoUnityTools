using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.HoUnityTools.FaceTracking;

namespace HoFace.Studio
{
    public sealed class ControlInputDefinition
    {
        public int Index { get; }
        public string Key { get; }
        public float DefaultValue { get; }
        public float Minimum { get; }
        public float Maximum { get; }
        public string[] Sources { get; }

        public ControlInputDefinition(int index, HoFaceOutput row, string[] sources)
        {
            Index = index;
            Key = row.parameter;
            DefaultValue = row.defaultValue;
            Sources = sources;
            var keys = row.curve?.keys;
            Minimum = keys != null && keys.Length > 0 ? keys.Min(k => k.time) : Math.Min(0, DefaultValue);
            Maximum = keys != null && keys.Length > 0 ? keys.Max(k => k.time) : Math.Max(1, DefaultValue);
            if (Maximum <= Minimum) Maximum = Minimum + 1;
        }
    }

    internal static class InputMonitorSources
    {
        // Classification only: actual wire spellings are taken from the profile
        // or received packet. Do not manufacture phone fields from this table.
        private static readonly HashSet<string> ShapeNames =
            new HashSet<string>(HoFaceTrackingChannels.Names, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> OtherPhoneFields = new HashSet<string>(
            new[] { "Rotation", "Position", "EyeLeft", "EyeRight" }
                .SelectMany(vector => new[] { vector + "_x", vector + "_y", vector + "_z" })
                .Concat(new[] { HoVtsPacket.FaceFoundKey, HoVtsPacket.TimestampKey, HoVtsPacket.HotkeyKey }),
            StringComparer.Ordinal);

        public static bool IsPhoneField(string key) =>
            key != null && (ShapeNames.Contains(key) || OtherPhoneFields.Contains(key));

        public static string[] Variables(HoFaceOutput row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.expression) ||
                !HoFaceExpression.TryParse(row.expression, out var parsed, out _)) return Array.Empty<string>();
            var names = new List<string>();
            parsed.CollectVariables(names);
            return names.Distinct(StringComparer.Ordinal).ToArray();
        }

        public static IReadOnlyList<ControlInputDefinition> Controls(HoFaceMiddleware profile) =>
            (profile?.inputs ?? new List<HoFaceOutput>())
                .Select((row, index) => new { row, index, sources = Variables(row) })
                .Where(x => x.row != null && !string.IsNullOrWhiteSpace(x.row.parameter) &&
                    (x.sources.Length == 0 || !x.sources.All(IsPhoneField)))
                .Select(x => new ControlInputDefinition(x.index, x.row, x.sources))
                .GroupBy(x => x.Key, StringComparer.Ordinal).Select(group => group.Last())
                .OrderBy(x => x.Index).ToArray();
    }
}
