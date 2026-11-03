using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    public sealed class HoFaceRawFrame
    {
        public Dictionary<string, float> Values;
        public bool Tracked;
        public double Time;
    }

    /// <summary>Shared parser for the Unity authoring raw-forward envelope.</summary>
    public static class HoFaceRawFrameJson
    {
        public const string Format = "hoface.raw";
        public const int Version = 1;

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
                reader.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "type": type = r.ReadString(); break;
                        case "version": version = r.ReadNumber(); break;
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
                if (values == null) values = new Dictionary<string, float>(StringComparer.Ordinal);
                if (double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                    throw new FormatException("裸值帧时间无效。");
                frame = new HoFaceRawFrame { Values = values, Tracked = tracked, Time = time };
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
    }
}
