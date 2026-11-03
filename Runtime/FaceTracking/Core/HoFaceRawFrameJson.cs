using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    public sealed class HoFaceRawFrame
    {
        public Dictionary<string, float> Values;
        public bool Tracked;
        public double Time;
        public long Sequence;
    }

    public sealed class HoFaceRawAck
    {
        public long Sequence;
        public bool Accepted;
        public string Status;
    }

    /// <summary>Shared parser for the Unity authoring raw-forward envelope.</summary>
    public static class HoFaceRawFrameJson
    {
        public const string Format = "hoface.raw";
        public const int Version = 1;
        public const string AckFormat = "hoface.raw.ack";
        public const int AckVersion = 1;

        public static bool TryParse(string json, out HoFaceRawFrame frame, out string error)
        {
            frame = null; error = null;
            try
            {
                var reader = new HoJsonReader(json ?? "");
                string type = null;
                double version = 0;
                var values = (Dictionary<string, float>)null;
                bool tracked = false;
                double time = 0;
                double sequence = 0;
                reader.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "type": type = r.ReadString(); break;
                        case "version": version = r.ReadNumber(); break;
                        case "sequence": sequence = r.ReadNumber(); break;
                        case "time": time = r.ReadNumber(); break;
                        case "tracked": tracked = r.ReadBool(); break;
                        case "values":
                            values = new Dictionary<string, float>(StringComparer.Ordinal);
                            r.ReadObject((name, value) => values[name] = value.ReadFloat());
                            break;
                        default: r.SkipValue(); break;
                    }
                });
                if (type != Format || version != Version) throw new FormatException("不是支持的 hoface.raw v1 帧。");
                if (double.IsNaN(sequence) || double.IsInfinity(sequence) || sequence < 1
                    || sequence > 9007199254740991d || Math.Floor(sequence) != sequence)
                    throw new FormatException("裸值帧序号无效。");
                if (values == null) values = new Dictionary<string, float>(StringComparer.Ordinal);
                if (double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                    throw new FormatException("裸值帧时间无效。");
                frame = new HoFaceRawFrame { Values = values, Tracked = tracked, Time = time, Sequence = (long)sequence };
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public static string WriteAck(long sequence, bool accepted, string status)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            return "{\"type\":\"" + AckFormat + "\",\"version\":" + AckVersion
                + ",\"sequence\":" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"accepted\":" + (accepted ? "true" : "false")
                + ",\"status\":" + HoFaceProfileJson.Quote(status ?? string.Empty) + "}";
        }

        public static bool TryParseAck(string json, out HoFaceRawAck ack, out string error)
        {
            ack = null; error = null;
            try
            {
                var reader = new HoJsonReader(json ?? string.Empty);
                string type = null, status = string.Empty;
                double version = 0, sequence = 0;
                bool accepted = false;
                reader.ReadObject((key, value) =>
                {
                    switch (key)
                    {
                        case "type": type = value.ReadString(); break;
                        case "version": version = value.ReadNumber(); break;
                        case "sequence": sequence = value.ReadNumber(); break;
                        case "accepted": accepted = value.ReadBool(); break;
                        case "status": status = value.ReadString(); break;
                        default: value.SkipValue(); break;
                    }
                });
                if (type != AckFormat || version != AckVersion)
                    throw new FormatException("不是支持的 hoface.raw.ack v1 回执。");
                if (double.IsNaN(sequence) || double.IsInfinity(sequence) || sequence < 1
                    || sequence > 9007199254740991d || Math.Floor(sequence) != sequence)
                    throw new FormatException("裸值回执序号无效。");
                ack = new HoFaceRawAck { Sequence = (long)sequence, Accepted = accepted, Status = status };
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
    }
}
