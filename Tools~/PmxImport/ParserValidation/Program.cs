using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Hollow.HoUnityTools.Editor.Pmx;

internal static class Program
{
    private static int Main(string[] args)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string output = null;
        try
        {
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "--root")
                    foreach (string path in Directory.GetFiles(args[++i], "*.pmx", SearchOption.AllDirectories))
                        files.Add(Path.GetFullPath(path));
                else if (args[i] == "--file") files.Add(Path.GetFullPath(args[++i]));
                else if (args[i] == "--output") output = Path.GetFullPath(args[++i]);
                else throw new ArgumentException("Unknown argument: " + args[i]);
            }
            int syntheticAssertions = SyntheticValidation.Run();
            var failures = new List<object>();
            var versions = new Dictionary<string, int>();
            var warnings = new List<object>();
            var timing = Stopwatch.StartNew();
            long totalVertices = 0, totalTriangles = 0;
            int unchangedSources = 0;
            foreach (string path in files)
            {
                try
                {
                    var before = new FileInfo(path);
                    long length = before.Length;
                    DateTime modified = before.LastWriteTimeUtc;
                    HoPmxDocument document = HoPmxReader.Read(path);
                    var after = new FileInfo(path);
                    if (length != after.Length || modified != after.LastWriteTimeUtc)
                        throw new IOException("Source changed during validation.");
                    ++unchangedSources;
                    totalVertices += document.Vertices.Count;
                    totalTriangles += document.Indices.Length / 3;
                    string version = document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    versions.TryGetValue(version, out int count);
                    versions[version] = count + 1;
                    if (document.Warnings.Count != 0) warnings.Add(new { path, messages = document.Warnings });
                    Console.WriteLine("PASS " + path);
                }
                catch (Exception exception)
                {
                    failures.Add(new { path, error = exception.Message });
                    Console.WriteLine("FAIL " + path + ": " + exception.Message);
                }
                // Several supplied stages contain hundreds of thousands of vertices. Do not
                // retain parsed documents when validating a large, unrelated model library.
                GC.Collect();
            }
            var result = new
            {
                timestampUtc = DateTime.UtcNow.ToString("O"),
                method = "Pure C# parser validation; no Unity launch or source modification",
                syntheticAssertions,
                files = files.Count,
                passed = files.Count - failures.Count,
                sourceStatsUnchanged = unchangedSources,
                versions,
                totalVertices,
                totalTriangles,
                seconds = timing.Elapsed.TotalSeconds,
                warnings,
                failures
            };
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            if (output != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, json);
            }
            Console.WriteLine(json);
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
