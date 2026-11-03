using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HoFace.Transport
{
    public sealed class HoFaceRecordedFrame
    {
        public double time;
        public bool tracked;
        public Dictionary<string, float> values = new Dictionary<string, float>();
    }

    public sealed class HoFaceRecordingWriter : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
        public string Path { get; }
        public int Frames { get; private set; }
        public HoFaceRecordingWriter(string path, string valuePath, string irPath)
        {
            Path = System.IO.Path.GetFullPath(path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                type = "hoface.record", version = 1, kind = "header", valuePath, irPath,
                startedUtc = DateTime.UtcNow.ToString("O")
            }, json));
            writer.Flush();
        }
        public void Write(HoFaceRecordedFrame frame)
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                type = "hoface.record", version = 1, kind = "frame",
                frame.time, frame.tracked, frame.values
            }, json));
            Frames++;
        }
        public void Dispose() { writer?.Flush(); writer?.Dispose(); }
    }

    public static class HoFaceRecordingReader
    {
        public static List<HoFaceRecordedFrame> Read(string path)
        {
            var result = new List<HoFaceRecordedFrame>();
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != "hoface.record"
                    || root.GetProperty("version").GetInt32() != 1)
                    throw new FormatException("不支持的 HoFace 录制格式。");
                if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "frame") continue;
                var frame = new HoFaceRecordedFrame
                {
                    time = root.GetProperty("time").GetDouble(),
                    tracked = root.GetProperty("tracked").GetBoolean()
                };
                foreach (var value in root.GetProperty("values").EnumerateObject())
                    frame.values[value.Name] = value.Value.GetSingle();
                result.Add(frame);
            }
            result.Sort((a, b) => a.time.CompareTo(b.time));
            return result;
        }
    }
}
