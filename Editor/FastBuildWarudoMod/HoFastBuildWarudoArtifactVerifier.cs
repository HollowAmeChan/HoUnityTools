#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    /// <summary>
    /// 构建产物复核时使用的“期望组件”。数据来源于 FastBuild 实际提交给 UMod 的临时
    /// Character.prefab，因此它代表产物里应该出现的组件集合，而不是源 Prefab 的猜测。
    /// </summary>
    [Serializable]
    internal sealed class HoFastBuildExpectedComponent
    {
        public string transformPath = string.Empty;
        public string typeName = string.Empty;
        public string sourcePath = string.Empty;
        public string sourceAssembly = string.Empty;
        public bool stagedRuntimeScript;
        public bool hostProvided;
    }

    [Serializable]
    internal sealed class HoFastBuildComponentVerdict
    {
        public string transformPath = string.Empty;
        public string typeName = string.Empty;
        public string recordedAssemblies = string.Empty;
        public string status = string.Empty;
        public string note = string.Empty;
    }

    /// <summary>
    /// 产物复核结果。字段刻意保持可被 Unity JsonUtility 序列化，方便窗口在域重载后继续显示。
    /// </summary>
    [Serializable]
    internal sealed class HoFastBuildArtifactVerification
    {
        public bool artifactFound;
        public string artifactPath = string.Empty;
        public string error = string.Empty;
        public string modAssemblyName = string.Empty;
        /// <summary>assemblymodules.dat 里记录的程序集名。这些才是随 Mod 一起分发、运行时能加载的程序集。</summary>
        public List<string> modAssemblyNames = new List<string>();
        public List<string> entryInventory = new List<string>();
        public string summary = string.Empty;
        public string report = string.Empty;
        public List<string> missingEntries = new List<string>();
        public List<string> packedAssets = new List<string>();
        public List<string> metadataStrings = new List<string>();
        public List<string> artifactAssemblies = new List<string>();
        public List<string> compiledTypes = new List<string>();
        public List<string> warnings = new List<string>();
        public List<HoFastBuildComponentVerdict> verdicts = new List<HoFastBuildComponentVerdict>();
        public int recordCount;
        public int matchedCount;
        public int missingCount;
        public int reviewCount;
        public bool usedFallbackScan;
        /// <summary>关键条目读不出来，本次复核结论不完整，不能当成通过。</summary>
        public bool incomplete;
        /// <summary>有 FastBuild 复制过的脚本没有进入 Mod 程序集。</summary>
        public bool hasUnlinkedStagedComponent;
        public bool buildLogFound;
        public string buildLogPath = string.Empty;
        /// <summary>构建日志里出现了本次产物的临时目录 id，说明它属于这次构建，而不是被后续构建覆盖。</summary>
        public bool buildLogMatchesArtifact;
        public int buildLogCompiledSourceCount;
        public int buildLogExportedScriptCount;
        public bool buildLogHighlightsTruncated;
        public List<string> buildLogHighlights = new List<string>();

        public bool HasProblems
        {
            get { return missingCount > 0 || reviewCount > 0 || incomplete || !string.IsNullOrEmpty(error); }
        }
    }

    /// <summary>
    /// 直接读取 .warudo 二进制产物，确认组件是否真的随 Mod 一起打包并被正确链接。
    ///
    /// 这里不加载产物中的程序集，也不依赖 Unity API：所有结论都来自对产物字节的解析，
    /// 因此可以在构建结束后、清理临时目录之前安全执行。
    /// </summary>
    internal static class HoFastBuildArtifactVerifier
    {
        internal const string VerdictOk = "OK";
        internal const string VerdictMissing = "缺失";
        internal const string VerdictReview = "待确认";

        private const string UmodeMagic = "UMOD";
        private const uint MetadataSignature = 0x424A5342;
        private const int ZipLocalHeaderSignature = 0x04034B50;
        private const int ZipSearchLimit = 64;
        private const long MaxSingleEntryBytes = 512L * 1024L * 1024L;
        private const int MaxRecordStringLength = 4096;
        private const int MaxBuildLogHighlights = 10;
        private const string ModCompiledAssemblyPrefix = "umod-compiled";

        private static readonly string[] RequiredEntries =
        {
            "modinfo.dat",
            "sharedassets.bin",
            "sharedassets.meta",
            "assemblymodules.dat",
        };

        /// <summary>
        /// 只做产物自检，不比对期望组件。用于“没有临时 Prefab 时”的重新复核。
        /// </summary>
        internal static HoFastBuildArtifactVerification Verify(string artifactPath)
        {
            return Verify(artifactPath, null, string.Empty);
        }

        internal static HoFastBuildArtifactVerification Verify(
            string artifactPath,
            IList<HoFastBuildExpectedComponent> expectedComponents,
            string expectedModName)
        {
            return Verify(artifactPath, expectedComponents, expectedModName, null);
        }

        internal static HoFastBuildArtifactVerification Verify(
            string artifactPath,
            IList<HoFastBuildExpectedComponent> expectedComponents,
            string expectedModName,
            string buildLogPath)
        {
            var result = new HoFastBuildArtifactVerification
            {
                artifactPath = artifactPath ?? string.Empty,
            };

            List<ScriptRecord> records = null;
            try
            {
                records = InspectArtifact(result, artifactPath, expectedModName);
            }
            catch (Exception exception)
            {
                result.error = exception.Message;
                result.warnings.Add("产物解析中断：" + exception.Message);
            }

            try
            {
                CompareComponents(result, expectedComponents, artifactPath, records);
            }
            catch (Exception exception)
            {
                result.warnings.Add("组件比对中断：" + exception.Message);
            }

            try
            {
                ReadBuildLog(result, buildLogPath);
            }
            catch (Exception exception)
            {
                result.warnings.Add("构建日志解析中断：" + exception.Message);
            }

            result.summary = BuildSummary(result);
            result.report = BuildReport(result);
            return result;
        }

        /// <summary>
        /// 读取 UMod 自己的 Build.log。它记录“哪些脚本被纳入编译”，
        /// 是区分“产物里没有程序集是因为 FastBuild 没复制源码”还是“UMod 没编译”的唯一直接证据。
        /// </summary>
        private static void ReadBuildLog(HoFastBuildArtifactVerification result, string buildLogPath)
        {
            if (string.IsNullOrEmpty(buildLogPath) || !File.Exists(buildLogPath))
                return;

            string[] lines = File.ReadAllLines(buildLogPath);
            result.buildLogFound = true;
            result.buildLogPath = buildLogPath;

            bool truncatedHighlights = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index] ?? string.Empty;
                if (line.IndexOf("Adding source file to build:", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.buildLogCompiledSourceCount++;
                else if (line.IndexOf("will be compiled into a managed assembly for export", StringComparison.OrdinalIgnoreCase) >= 0)
                    result.buildLogExportedScriptCount++;

                if (!IsBuildLogHighlight(line))
                    continue;

                if (result.buildLogHighlights.Count < MaxBuildLogHighlights)
                    result.buildLogHighlights.Add(line.Trim());
                else
                    truncatedHighlights = true;
            }

            result.buildLogHighlightsTruncated = truncatedHighlights;

            // Build.log 每次构建都会被覆盖；用临时目录的 build id 确认它属于本次产物。
            string buildId = ExtractBuildId(result);
            if (!string.IsNullOrEmpty(buildId))
            {
                for (int index = 0; index < lines.Length; index++)
                {
                    if (lines[index] != null &&
                        lines[index].IndexOf(buildId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.buildLogMatchesArtifact = true;
                        break;
                    }
                }
            }
        }

        private static string ExtractBuildId(HoFastBuildArtifactVerification result)
        {
            for (int index = 0; index < result.packedAssets.Count; index++)
            {
                string asset = result.packedAssets[index] ?? string.Empty;
                int marker = asset.IndexOf("/HoFastBuildWarudoModTemp/", StringComparison.OrdinalIgnoreCase);
                if (marker < 0)
                    continue;

                string rest = asset.Substring(marker + "/HoFastBuildWarudoModTemp/".Length);
                int slash = rest.IndexOf('/');
                return slash < 0 ? rest : rest.Substring(0, slash);
            }

            return string.Empty;
        }

        private static bool IsBuildLogHighlight(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;

            if (line.IndexOf("not in the .csproj", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (line.IndexOf("error CS", StringComparison.Ordinal) >= 0)
                return true;
            if (line.IndexOf("Compile failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (line.IndexOf("Compilation failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (line.IndexOf("BUILD FAILED", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return HasNonZeroErrorCount(line);
        }

        private static bool HasNonZeroErrorCount(string line)
        {
            int index = line.IndexOf("Errors:", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            int cursor = index + "Errors:".Length;
            while (cursor < line.Length && line[cursor] == ' ')
                cursor++;

            int value = 0;
            bool any = false;
            while (cursor < line.Length && char.IsDigit(line[cursor]))
            {
                value = value * 10 + (line[cursor] - '0');
                any = true;
                cursor++;
            }

            return any && value > 0;
        }

        private static List<ScriptRecord> InspectArtifact(
            HoFastBuildArtifactVerification result,
            string artifactPath,
            string expectedModName)
        {
            if (string.IsNullOrEmpty(artifactPath) || !File.Exists(artifactPath))
            {
                result.error = "找不到构建产物文件。";
                return null;
            }

            result.artifactFound = true;
            using (var file = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int zipStart = FindZipStart(file);
                if (zipStart < 0)
                {
                    result.error = "产物头部不是 UMod 容器（找不到 ZIP 段）。";
                    return null;
                }

                using (var bounded = new BoundedStream(file, zipStart, file.Length - zipStart))
                using (var archive = new ZipArchive(bounded, ZipArchiveMode.Read))
                {
                    var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (!entries.ContainsKey(entry.FullName))
                            entries.Add(entry.FullName, entry);
                        result.entryInventory.Add(entry.FullName + " " + DescribeEntrySize(entry));
                    }

                    foreach (string required in RequiredEntries)
                    {
                        if (!entries.ContainsKey(required))
                            result.missingEntries.Add(required);
                    }

                    ReadPackedAssets(entries, result);
                    ReadModInfo(entries, result, expectedModName);
                    ReadAssemblyModules(entries, result);
                    return ReadScriptRecords(entries, result);
                }
            }
        }

        private static int FindZipStart(Stream stream)
        {
            var header = new byte[ZipSearchLimit];
            stream.Seek(0, SeekOrigin.Begin);
            int read = stream.Read(header, 0, header.Length);
            if (read < 8)
                return -1;

            if (!MatchesAscii(header, 0, UmodeMagic))
                return -1;

            for (int offset = 4; offset + 4 <= read; offset++)
            {
                if (ReadUInt32(header, offset) == ZipLocalHeaderSignature)
                    return offset;
            }

            return -1;
        }

        private static void ReadPackedAssets(
            Dictionary<string, ZipArchiveEntry> entries,
            HoFastBuildArtifactVerification result)
        {
            byte[] data = ReadEntry(entries, "sharedassets.meta", result);
            if (data == null)
            {
                result.warnings.Add("sharedassets.meta 无法读取，跳过打包资源清单检查。");
                return;
            }

            try
            {
                int offset = 0;
                string root = ReadSevenBitString(data, ref offset);
                if (!string.IsNullOrEmpty(root))
                    result.packedAssets.Add(root);

                if (offset + 4 > data.Length)
                    return;

                int count = ReadInt32(data, offset);
                offset += 4;
                if (count < 0 || count > 100000)
                    return;

                for (int index = 0; index < count && offset < data.Length; index++)
                {
                    string assetPath = ReadSevenBitString(data, ref offset);
                    if (!string.IsNullOrEmpty(assetPath))
                        result.packedAssets.Add(assetPath);
                }

                bool hasCharacterPrefab = false;
                for (int index = 0; index < result.packedAssets.Count; index++)
                {
                    if (result.packedAssets[index].EndsWith("/Character.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCharacterPrefab = true;
                        break;
                    }
                }

                if (!hasCharacterPrefab)
                {
                    result.warnings.Add(
                        "sharedassets.meta 的打包清单里没有 Character.prefab，产物可能缺少角色根节点。");
                }
            }
            catch (Exception exception)
            {
                result.warnings.Add("sharedassets.meta 解析失败：" + exception.Message);
            }
        }

        private static void ReadModInfo(
            Dictionary<string, ZipArchiveEntry> entries,
            HoFastBuildArtifactVerification result,
            string expectedModName)
        {
            byte[] data = ReadEntry(entries, "modinfo.dat", result);
            if (data == null)
                return;

            try
            {
                int offset = 4 + 8; // "UMOD" + 8 字节版本信息
                for (int index = 0; index < 16 && offset < data.Length; index++)
                {
                    // 该段由若干 int32 与长度前缀字符串混排，这里只做保守抽样，
                    // 目的仅是确认产物里的 Mod 名称与当前配置一致。
                    int value = ReadInt32(data, offset);
                    if (value >= 0 && value <= data.Length - offset - 4)
                    {
                        offset += 4;
                        continue;
                    }

                    string text = ReadSevenBitString(data, ref offset);
                    if (!string.IsNullOrEmpty(text))
                        result.metadataStrings.Add(text);
                    else if (offset >= data.Length)
                        break;
                    else
                        offset++;
                }
            }
            catch (Exception exception)
            {
                result.warnings.Add("modinfo.dat 解析失败：" + exception.Message);
            }

            if (!string.IsNullOrEmpty(expectedModName) && result.metadataStrings.Count > 0 &&
                !result.metadataStrings.Contains(expectedModName))
            {
                result.warnings.Add(
                    "产物的 Mod 元数据里没有出现当前配置的 Mod 名称“" + expectedModName + "”，请确认构建输出与 ExportProfile 一致。");
            }
        }

        private static void ReadAssemblyModules(
            Dictionary<string, ZipArchiveEntry> entries,
            HoFastBuildArtifactVerification result)
        {
            byte[] data = ReadEntry(entries, "assemblymodules.dat", result);
            if (data == null)
            {
                // 缺条目由“缺少条目”行说明，读取失败由 ReadEntry 的警告说明，这里只标记结论不完整。
                result.incomplete = true;
                return;
            }

            try
            {
                int offset = 0;
                int moduleCount = data.Length >= 4 ? ReadInt32(data, offset) : 0;
                offset += 4;
                if (moduleCount < 0 || moduleCount > 64)
                {
                    result.incomplete = true;
                    result.warnings.Add(
                        "assemblymodules.dat 长度 " + data.Length + " 字节，但模块数读出来是 " +
                        moduleCount + "，格式与预期不符。");
                    return;
                }

                if (moduleCount == 0)
                {
                    result.incomplete = true;
                    result.warnings.Add(
                        "assemblymodules.dat 里没有任何运行时程序集（长度 " + data.Length +
                        " 字节），本次构建很可能没有编译 Mod 脚本。");
                    return;
                }

                for (int index = 0; index < moduleCount && offset < data.Length; index++)
                {
                    string moduleName = ReadSevenBitString(data, ref offset);
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        result.modAssemblyNames.Add(moduleName);
                        if (string.IsNullOrEmpty(result.modAssemblyName))
                            result.modAssemblyName = moduleName;
                    }

                    int mzOffset = FindPeImage(data, offset);
                    if (mzOffset < 0)
                        break;

                    int peHeaderOffset = GetPeHeaderOffset(data, mzOffset);
                    if (peHeaderOffset < 0)
                        break;

                    int peEnd = GetPeImageEnd(data, mzOffset);
                    if (peEnd <= peHeaderOffset || peEnd > data.Length)
                        peEnd = data.Length;

                    foreach (string typeName in ReadTypeDefNames(data, peHeaderOffset, peEnd, mzOffset))
                    {
                        if (!result.compiledTypes.Contains(typeName))
                            result.compiledTypes.Add(typeName);
                    }

                    offset = peEnd;
                }
            }
            catch (Exception exception)
            {
                result.warnings.Add("assemblymodules.dat 解析失败：" + exception.Message);
            }
        }

        private static List<ScriptRecord> ReadScriptRecords(
            Dictionary<string, ZipArchiveEntry> entries,
            HoFastBuildArtifactVerification result)
        {
            byte[] data = ReadEntry(entries, "sharedassets.bin", result);
            if (data == null)
            {
                result.incomplete = true;
                result.warnings.Add("sharedassets.bin 无法读取，无法确认组件的程序集链接。");
                return null;
            }

            List<ScriptRecord> records = ScanScriptRecords(data);
            result.recordCount = records.Count;

            var assemblies = new List<string>();
            for (int index = 0; index < records.Count; index++)
            {
                string assembly = records[index].assembly;
                if (!assemblies.Contains(assembly))
                    assemblies.Add(assembly);
            }

            assemblies.Sort(StringComparer.OrdinalIgnoreCase);
            result.artifactAssemblies.AddRange(assemblies);
            return records;
        }

        private static void CompareComponents(
            HoFastBuildArtifactVerification result,
            IList<HoFastBuildExpectedComponent> expectedComponents,
            string artifactPath,
            List<ScriptRecord> records)
        {
            if (expectedComponents == null || expectedComponents.Count == 0)
                return;

            if (records == null)
                records = new List<ScriptRecord>();

            byte[] fallbackData = null;

            foreach (HoFastBuildExpectedComponent expected in expectedComponents)
            {
                if (expected == null || string.IsNullOrEmpty(expected.typeName))
                    continue;

                var verdict = new HoFastBuildComponentVerdict
                {
                    transformPath = expected.transformPath,
                    typeName = expected.typeName,
                };

                var matchedAssemblies = new List<string>();
                for (int index = 0; index < records.Count; index++)
                {
                    if (!string.Equals(records[index].typeName, expected.typeName, StringComparison.Ordinal))
                        continue;
                    if (!matchedAssemblies.Contains(records[index].assembly))
                        matchedAssemblies.Add(records[index].assembly);
                }

                if (matchedAssemblies.Count == 0 && records.Count == 0)
                {
                    // 记录表扫描为空时退化为纯字符串探测，结论标记为“待确认”，
                    // 不能把解析失败误报成“组件缺失”。
                    if (fallbackData == null && !string.IsNullOrEmpty(artifactPath) && File.Exists(artifactPath))
                        fallbackData = ReadSharedAssetsRaw(artifactPath);

                    if (fallbackData != null && ContainsAscii(fallbackData, expected.typeName))
                    {
                        result.usedFallbackScan = true;
                        verdict.status = VerdictReview;
                        verdict.note = "只匹配到类型名，未解析出程序集";
                        result.reviewCount++;
                        result.verdicts.Add(verdict);
                        continue;
                    }
                }

                if (matchedAssemblies.Count == 0)
                {
                    verdict.status = VerdictMissing;
                    verdict.note = "产物中没有该组件";
                    result.missingCount++;
                    result.verdicts.Add(verdict);
                    continue;
                }

                verdict.recordedAssemblies = string.Join(", ", matchedAssemblies.ToArray());
                bool modCompiled = false;
                for (int index = 0; index < matchedAssemblies.Count; index++)
                {
                    if (IsModCompiledAssembly(result, matchedAssemblies[index]))
                        modCompiled = true;
                }

                if (expected.stagedRuntimeScript)
                {
                    if (!modCompiled)
                    {
                        // 具体原因交给报告末尾的提示统一说明，组件行只留结论。
                        verdict.status = VerdictReview;
                        verdict.note = "不在 Mod 程序集内";
                        result.hasUnlinkedStagedComponent = true;
                        result.reviewCount++;
                    }
                    else if (expected.typeName.IndexOf('+') < 0 &&
                             result.compiledTypes.Count > 0 &&
                             !result.compiledTypes.Contains(expected.typeName))
                    {
                        verdict.status = VerdictReview;
                        verdict.note = "程序集类型表里没有该类型";
                        result.reviewCount++;
                    }
                    else
                    {
                        verdict.status = VerdictOk;
                        verdict.note = "已编译进 Mod 程序集";
                        result.matchedCount++;
                    }

                    result.verdicts.Add(verdict);
                    continue;
                }

                if (modCompiled)
                {
                    verdict.status = VerdictOk;
                    verdict.note = "随构建一起编译";
                    result.matchedCount++;
                }
                else if (!string.IsNullOrEmpty(expected.sourceAssembly) &&
                         string.Equals(expected.sourceAssembly, matchedAssemblies[0], StringComparison.Ordinal) &&
                         !expected.hostProvided &&
                         !IsUnityProvidedAssembly(matchedAssemblies[0]))
                {
                    verdict.status = VerdictReview;
                    verdict.note = "保留原程序集，仅宿主自带时可加载";
                    result.reviewCount++;
                }
                else
                {
                    verdict.status = VerdictOk;
                    verdict.note = "宿主提供";
                    result.matchedCount++;
                }

                result.verdicts.Add(verdict);
            }
        }

        /// <summary>
        /// 判断一个程序集是否会随本次 Mod 分发。
        /// 优先用 assemblymodules.dat 里实际列出的模块名，这样换 UMod 版本、编译程序集改名也不会误判；
        /// 只有读不到模块清单时才退回 umod-compiled 前缀这个约定。
        /// </summary>
        private static bool IsModCompiledAssembly(HoFastBuildArtifactVerification result, string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            for (int index = 0; index < result.modAssemblyNames.Count; index++)
            {
                if (string.Equals(result.modAssemblyNames[index], assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (result.modAssemblyNames.Count > 0)
                return false;

            return assemblyName.StartsWith(ModCompiledAssemblyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnityProvidedAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            return assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assemblyName, "netstandard", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSummary(HoFastBuildArtifactVerification result)
        {
            if (!result.artifactFound)
                return "产物复核未执行：" + (string.IsNullOrEmpty(result.error) ? "未找到产物。" : result.error);

            var builder = new StringBuilder();
            builder.Append("产物复核：");

            int total = result.verdicts.Count;
            if (total == 0)
            {
                builder.Append("仅检查容器结构");
            }
            else
            {
                builder.Append("组件 ").Append(result.matchedCount).Append('/').Append(total).Append(" 已确认");
                if (result.missingCount > 0)
                    builder.Append("，").Append(result.missingCount).Append(" 缺失");
                if (result.reviewCount > 0)
                    builder.Append("，").Append(result.reviewCount).Append(" 待确认");
            }

            if (result.incomplete)
                builder.Append("（结论不完整）");
            builder.Append("。");

            if (result.missingEntries.Count > 0)
                builder.Append("缺少 ").Append(string.Join("、", result.missingEntries.ToArray())).Append("。");

            return builder.ToString();
        }

        private static string BuildReport(HoFastBuildArtifactVerification result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[HoUnityTools] FastBuild 产物复核");
            builder.Append("  产物：").AppendLine(string.IsNullOrEmpty(result.artifactPath) ? "(未指定)" : result.artifactPath);

            if (!result.artifactFound)
            {
                builder.Append("  结果：失败 - ").AppendLine(result.error);
                return builder.ToString();
            }

            if (result.missingEntries.Count > 0)
                builder.Append("  缺少条目：").AppendLine(string.Join(", ", result.missingEntries.ToArray()));

            // 只在出问题时才展开条目明细，正常报告保持简短。
            if (result.entryInventory.Count > 0 && result.missingEntries.Count > 0)
                builder.Append("  条目明细：").AppendLine(string.Join(" | ", result.entryInventory.ToArray()));

            if (result.modAssemblyNames.Count > 0)
                builder.Append("  Mod 程序集：").AppendLine(string.Join(", ", result.modAssemblyNames.ToArray()));

            builder.Append("  编译类型：").Append(result.compiledTypes.Count).AppendLine(" 个");
            builder.Append("  程序集记录：").Append(result.recordCount).AppendLine(" 条");

            if (result.buildLogFound)
            {
                builder.Append("  构建日志：识别 ")
                    .Append(result.buildLogExportedScriptCount)
                    .Append(" 个脚本，实际加入编译 ")
                    .Append(result.buildLogCompiledSourceCount)
                    .AppendLine(" 个" + (result.buildLogMatchesArtifact ? "（属于本次构建）" : "（可能已被后续构建覆盖）"));
            }

            if (result.metadataStrings.Count > 0)
                builder.Append("  元数据：").AppendLine(string.Join(" | ", result.metadataStrings.ToArray()));
            if (result.packedAssets.Count > 0)
                builder.Append("  打包资源：").Append(result.packedAssets.Count).AppendLine(" 项");

            if (result.verdicts.Count > 0)
            {
                builder.Append("  组件复核（").Append(result.matchedCount).Append('/').Append(result.verdicts.Count)
                    .AppendLine(" 已确认）：");
                for (int index = 0; index < result.verdicts.Count; index++)
                {
                    HoFastBuildComponentVerdict verdict = result.verdicts[index];
                    builder.Append("    [").Append(verdict.status).Append("] ")
                        .Append(verdict.transformPath)
                        .Append(" / ").Append(verdict.typeName);
                    if (!string.IsNullOrEmpty(verdict.recordedAssemblies))
                        builder.Append(" -> ").Append(verdict.recordedAssemblies);
                    if (!string.IsNullOrEmpty(verdict.note))
                        builder.Append("（").Append(verdict.note).Append("）");
                    builder.AppendLine();
                }
            }

            // 具体原因只在一处说明，避免每个组件重复同一段长文案。
            if (result.hasUnlinkedStagedComponent)
            {
                builder.AppendLine("  提示：FastBuild 复制过的脚本没有进入 Mod 程序集。检查 Build.log 是否出现");
                builder.AppendLine("        “not in the .csproj file and will not be compiled”，以及依赖列表里这些脚本是否已勾选。");
            }

            if (result.incomplete && result.modAssemblyNames.Count == 0)
                builder.AppendLine("  提示：产物里没有 assemblymodules.dat，说明这次构建没有产出运行时程序集。");

            if (result.buildLogFound && result.buildLogMatchesArtifact &&
                result.buildLogCompiledSourceCount == 0)
            {
                if (result.buildLogExportedScriptCount > 0)
                {
                    builder.Append("  提示：构建日志里扫描到 ")
                        .Append(result.buildLogExportedScriptCount)
                        .AppendLine(" 个脚本，但 Compile Scripts 阶段一个都没有加入编译。");
                }
                else
                {
                    builder.AppendLine("  提示：构建日志里没有找到任何待编译脚本，UMod 没有发现 Mod 里的源码。");
                }
            }

            if (result.buildLogHighlights.Count > 0)
            {
                builder.AppendLine("  构建日志关键行：");
                for (int index = 0; index < result.buildLogHighlights.Count; index++)
                    builder.Append("    ").AppendLine(result.buildLogHighlights[index]);
                if (result.buildLogHighlightsTruncated)
                    builder.AppendLine("    …（其余略）");
            }

            for (int index = 0; index < result.warnings.Count; index++)
                builder.Append("  警告：").AppendLine(result.warnings[index]);

            return builder.ToString();
        }

        #region 产物字节扫描

        private sealed class ScriptRecord
        {
            public string assembly = string.Empty;
            public string typeName = string.Empty;
        }

        /// <summary>
        /// UMod 的 Linker 会为每个 MonoBehaviour 写入
        /// [int32 程序集显示名长度][程序集显示名][补齐到 4 字节]
        /// [int32 类型全名长度][类型全名]
        /// 这里按该结构扫描 sharedassets.bin，得到“产物实际链接了哪些组件类型”。
        /// </summary>
        private static List<ScriptRecord> ScanScriptRecords(byte[] data)
        {
            var records = new List<ScriptRecord>();
            int length = data.Length;
            int offset = 0;

            while (offset + 8 <= length)
            {
                if ((offset & 3) != 0)
                {
                    offset++;
                    continue;
                }

                int assemblyLength = ReadInt32(data, offset);
                if (assemblyLength < 16 || assemblyLength > MaxRecordStringLength ||
                    offset + 4 + assemblyLength > length)
                {
                    offset++;
                    continue;
                }

                string assembly = ReadPrintableAscii(data, offset + 4, assemblyLength);
                if (assembly == null ||
                    assembly.IndexOf(", Version=", StringComparison.Ordinal) < 0 ||
                    assembly.IndexOf("PublicKeyToken=", StringComparison.Ordinal) < 0)
                {
                    offset++;
                    continue;
                }

                int typeOffset = Align4(offset + 4 + assemblyLength);
                if (typeOffset + 4 > length)
                    break;

                int typeLength = ReadInt32(data, typeOffset);
                if (typeLength < 2 || typeLength > MaxRecordStringLength ||
                    typeOffset + 4 + typeLength > length)
                {
                    offset++;
                    continue;
                }

                string typeName = ReadPrintableAscii(data, typeOffset + 4, typeLength);
                if (typeName == null || !IsPlausibleTypeName(typeName))
                {
                    offset++;
                    continue;
                }

                var record = new ScriptRecord
                {
                    assembly = StripAssemblyDetails(assembly),
                    typeName = typeName,
                };
                records.Add(record);
                offset = Align4(typeOffset + 4 + typeLength);
            }

            return records;
        }

        private static bool IsPlausibleTypeName(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool allowed = char.IsLetterOrDigit(character) ||
                               character == '.' || character == '_' || character == '+' ||
                               character == '`' || character == '<' || character == '>' ||
                               character == ',' || character == '[' || character == ']' ||
                               character == ' ' || character == '=' || character == '\\' ||
                               character == '(' || character == ')' || character == '-' ||
                               character == '/';
                if (!allowed)
                    return false;
            }

            return true;
        }

        private static string StripAssemblyDetails(string assemblyDisplayName)
        {
            int separator = assemblyDisplayName.IndexOf(',');
            return separator < 0 ? assemblyDisplayName : assemblyDisplayName.Substring(0, separator);
        }

        private static byte[] ReadSharedAssetsRaw(string artifactPath)
        {
            try
            {
                using (var file = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int zipStart = FindZipStart(file);
                    if (zipStart < 0)
                        return null;

                    using (var bounded = new BoundedStream(file, zipStart, file.Length - zipStart))
                    using (var archive = new ZipArchive(bounded, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry entry = archive.GetEntry("sharedassets.bin");
                        return entry == null ? null : ReadEntry(entry, null);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool ContainsAscii(byte[] data, string value)
        {
            if (data == null || string.IsNullOrEmpty(value))
                return false;

            byte[] needle = Encoding.UTF8.GetBytes(value);
            int limit = data.Length - needle.Length;
            for (int offset = 0; offset <= limit; offset++)
            {
                if (data[offset] != needle[0])
                    continue;

                bool matched = true;
                for (int index = 1; index < needle.Length; index++)
                {
                    if (data[offset + index] != needle[index])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                    return true;
            }

            return false;
        }

        #endregion

        #region ECMA-335 类型表

        /// <summary>
        /// 读取 PE 映像的 TypeDef 表，用于确认运行时类型真的被编译进了 Mod 程序集。
        /// 这是文档里“检查 assemblymodules.dat 类型表”的自动化版本，不需要反编译工具。
        /// </summary>
        private static List<string> ReadTypeDefNames(byte[] data, int peHeaderOffset, int peEnd, int imageBase)
        {
            var names = new List<string>();
            int peLimit = Math.Min(peEnd, data.Length);
            if (peHeaderOffset < 0 || peHeaderOffset + 24 > peLimit ||
                ReadUInt32(data, peHeaderOffset) != 0x00004550)
                return names;

            int optionalHeaderSize = ReadUInt16(data, peHeaderOffset + 20);
            int optionalHeader = peHeaderOffset + 24;
            int sectionCount = ReadUInt16(data, peHeaderOffset + 6);
            int sectionStart = optionalHeader + optionalHeaderSize;
            if (sectionCount <= 0 || sectionStart + sectionCount * 40 > peLimit)
                return names;

            int magic = ReadUInt16(data, optionalHeader);
            int dataDirectories;
            if (magic == 0x10B)
                dataDirectories = optionalHeader + 96;
            else if (magic == 0x20B)
                dataDirectories = optionalHeader + 112;
            else
                return names;

            int cliDirectory = dataDirectories + 14 * 8;
            if (cliDirectory + 8 > peLimit)
                return names;

            int cliRva = ReadInt32(data, cliDirectory);
            int cliOffset = RvaToOffset(data, sectionStart, sectionCount, cliRva, imageBase);
            if (cliOffset < 0 || cliOffset + 16 > peLimit)
                return names;

            int metadataRva = ReadInt32(data, cliOffset + 8);
            int metadataOffset = RvaToOffset(data, sectionStart, sectionCount, metadataRva, imageBase);
            if (metadataOffset < 0 || metadataOffset + 20 > peLimit)
                return names;

            if (ReadUInt32(data, metadataOffset) != MetadataSignature)
                return names;

            int versionLength = ReadInt32(data, metadataOffset + 12);
            if (versionLength < 0 || versionLength > 256)
                return names;

            int streamHeader = metadataOffset + Align4(16 + versionLength);
            if (streamHeader + 4 > peLimit)
                return names;

            int streamCount = ReadUInt16(data, streamHeader + 2);
            int cursor = streamHeader + 4;
            int stringsOffset = -1;
            int tableOffset = -1;
            int tableSize = 0;

            for (int index = 0; index < streamCount && cursor + 8 <= peLimit; index++)
            {
                int streamOffset = ReadInt32(data, cursor);
                int streamSize = ReadInt32(data, cursor + 4);
                int nameStart = cursor + 8;
                int nameEnd = nameStart;
                while (nameEnd < peLimit && data[nameEnd] != 0)
                    nameEnd++;

                string streamName = ReadPrintableAscii(data, nameStart, nameEnd - nameStart) ?? string.Empty;
                if (string.Equals(streamName, "#Strings", StringComparison.Ordinal))
                    stringsOffset = metadataOffset + streamOffset;
                else if (string.Equals(streamName, "#~", StringComparison.Ordinal) ||
                         string.Equals(streamName, "#-", StringComparison.Ordinal))
                {
                    tableOffset = metadataOffset + streamOffset;
                    tableSize = streamSize;
                }

                // 流名称按元数据根对齐补齐，元数据根本身不保证 4 字节对齐。
                cursor = metadataOffset + Align4(nameEnd + 1 - metadataOffset);
            }

            if (stringsOffset < 0 || tableOffset < 0 || tableOffset + tableSize > peLimit)
                return names;

            return ParseTypeDefTable(data, tableOffset, tableSize, stringsOffset);
        }

        private static List<string> ParseTypeDefTable(byte[] data, int tableOffset, int tableSize, int stringsOffset)
        {
            var names = new List<string>();
            if (tableOffset + 24 > data.Length)
                return names;

            int heapSizes = data[tableOffset + 6];
            ulong valid = ReadUInt64(data, tableOffset + 8);
            int cursor = tableOffset + 24;

            var rowCounts = new int[64];
            for (int table = 0; table < 64; table++)
            {
                if ((valid & (1UL << table)) == 0)
                    continue;

                if (cursor + 4 > tableOffset + tableSize)
                    return names;

                rowCounts[table] = ReadInt32(data, cursor);
                if (rowCounts[table] < 0)
                    return names;
                cursor += 4;
            }

            int stringIndexSize = (heapSizes & 0x01) != 0 ? 4 : 2;
            int guidIndexSize = (heapSizes & 0x02) != 0 ? 4 : 2;

            // TypeDef 之前只有 Module(0x00) 与 TypeRef(0x01) 两张表。
            int moduleRowSize = 2 + stringIndexSize + guidIndexSize * 3;
            int typeRefRowSize = CodedIndexSize(2, new[] { 0x00, 0x1A, 0x23, 0x01 }, rowCounts) +
                                 stringIndexSize * 2;
            int typeDefStart = cursor + rowCounts[0x00] * moduleRowSize + rowCounts[0x01] * typeRefRowSize;

            int fieldIndexSize = rowCounts[0x04] < 0x10000 ? 2 : 4;
            int methodIndexSize = rowCounts[0x06] < 0x10000 ? 2 : 4;
            int typeDefRowSize = 4 + stringIndexSize * 2 +
                                 CodedIndexSize(2, new[] { 0x02, 0x01, 0x1B }, rowCounts) +
                                 fieldIndexSize + methodIndexSize;

            int typeDefCount = rowCounts[0x02];
            if (typeDefCount <= 0 || typeDefRowSize <= 0)
                return names;

            if (typeDefStart + typeDefCount * typeDefRowSize > tableOffset + tableSize)
                return names;

            for (int index = 0; index < typeDefCount; index++)
            {
                int row = typeDefStart + index * typeDefRowSize;
                string typeName = ReadStringHeap(data, stringsOffset, ReadIndex(data, row + 4, stringIndexSize));
                string typeNamespace = ReadStringHeap(data, stringsOffset, ReadIndex(data, row + 4 + stringIndexSize, stringIndexSize));
                if (string.IsNullOrEmpty(typeName))
                    continue;

                string fullName = string.IsNullOrEmpty(typeNamespace) ? typeName : typeNamespace + "." + typeName;
                names.Add(fullName);
            }

            return names;
        }

        private static int CodedIndexSize(int tagBits, int[] tables, int[] rowCounts)
        {
            int max = 0;
            for (int index = 0; index < tables.Length; index++)
            {
                int table = tables[index];
                if (table >= 0 && table < rowCounts.Length && rowCounts[table] > max)
                    max = rowCounts[table];
            }

            return max < (1 << (16 - tagBits)) ? 2 : 4;
        }

        private static int ReadIndex(byte[] data, int offset, int size)
        {
            return size == 2 ? ReadUInt16(data, offset) : ReadInt32(data, offset);
        }

        private static string ReadStringHeap(byte[] data, int stringsOffset, int index)
        {
            int start = stringsOffset + index;
            if (index <= 0 || start < 0 || start >= data.Length)
                return string.Empty;

            int end = start;
            while (end < data.Length && data[end] != 0)
                end++;

            return end == start ? string.Empty : Encoding.UTF8.GetString(data, start, end - start);
        }

        private static int RvaToOffset(byte[] data, int sectionStart, int sectionCount, int rva, int imageBase)
        {
            for (int index = 0; index < sectionCount; index++)
            {
                int section = sectionStart + index * 40;
                if (section + 40 > data.Length)
                    return -1;

                int virtualSize = ReadInt32(data, section + 8);
                int virtualAddress = ReadInt32(data, section + 12);
                int rawSize = ReadInt32(data, section + 16);
                int rawPointer = ReadInt32(data, section + 20);
                int span = Math.Max(virtualSize, rawSize);
                if (rva >= virtualAddress && rva < virtualAddress + span)
                    return imageBase + rawPointer + (rva - virtualAddress);
            }

            return -1;
        }

        /// <summary>返回 MZ 映像中 PE 头相对整个字节数组的偏移，失败返回 -1。</summary>
        private static int GetPeHeaderOffset(byte[] data, int mzOffset)
        {
            if (mzOffset < 0 || mzOffset + 0x40 > data.Length)
                return -1;

            int lfanew = ReadInt32(data, mzOffset + 0x3C);
            int peHeaderOffset = mzOffset + lfanew;
            if (lfanew <= 0 || peHeaderOffset + 24 > data.Length)
                return -1;

            return ReadUInt32(data, peHeaderOffset) == 0x00004550 ? peHeaderOffset : -1;
        }

        /// <summary>按 PE 节表推算映像结束位置（相对整个字节数组），用于定位下一个模块。</summary>
        private static int GetPeImageEnd(byte[] data, int mzOffset)
        {
            int peHeaderOffset = GetPeHeaderOffset(data, mzOffset);
            if (peHeaderOffset < 0)
                return -1;

            int sectionCount = ReadUInt16(data, peHeaderOffset + 6);
            int optionalHeaderSize = ReadUInt16(data, peHeaderOffset + 20);
            int sectionStart = peHeaderOffset + 24 + optionalHeaderSize;
            if (sectionCount <= 0 || sectionStart + sectionCount * 40 > data.Length)
                return -1;

            int imageLength = sectionStart - mzOffset + sectionCount * 40;
            for (int index = 0; index < sectionCount; index++)
            {
                int section = sectionStart + index * 40;
                int rawSize = ReadInt32(data, section + 16);
                int rawPointer = ReadInt32(data, section + 20);
                if (rawSize < 0 || rawPointer < 0)
                    continue;

                imageLength = Math.Max(imageLength, rawPointer + rawSize);
            }

            return mzOffset + imageLength;
        }

        private static int FindPeImage(byte[] data, int start)
        {
            for (int offset = Math.Max(0, start); offset + 0x40 <= data.Length; offset++)
            {
                if (data[offset] != 0x4D || data[offset + 1] != 0x5A)
                    continue;

                int lfanew = ReadInt32(data, offset + 0x3C);
                if (lfanew <= 0 || offset + lfanew + 4 > data.Length)
                    continue;
                if (ReadUInt32(data, offset + lfanew) == 0x00004550)
                    return offset;
            }

            return -1;
        }

        #endregion

        #region 基础读取

        private sealed class BoundedStream : Stream
        {
            private readonly Stream inner;
            private readonly long baseOffset;
            private readonly long boundedLength;
            private long position;

            internal BoundedStream(Stream inner, long baseOffset, long boundedLength)
            {
                this.inner = inner;
                this.baseOffset = baseOffset;
                this.boundedLength = boundedLength;
                position = 0;
                inner.Seek(baseOffset, SeekOrigin.Begin);
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return true; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { return boundedLength; }
            }

            public override long Position
            {
                get { return position; }
                set { position = value; }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count <= 0 || position >= boundedLength)
                    return 0;

                long remaining = boundedLength - position;
                if (count > remaining)
                    count = (int)remaining;

                inner.Seek(baseOffset + position, SeekOrigin.Begin);
                int total = 0;
                while (total < count)
                {
                    int read = inner.Read(buffer, offset + total, count - total);
                    if (read <= 0)
                        break;
                    total += read;
                }

                position += total;
                return total;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long target;
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        target = offset;
                        break;
                    case SeekOrigin.Current:
                        target = position + offset;
                        break;
                    case SeekOrigin.End:
                        target = boundedLength + offset;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(origin));
                }

                position = target;
                return position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }

        private static byte[] ReadEntry(
            Dictionary<string, ZipArchiveEntry> entries,
            string name,
            HoFastBuildArtifactVerification result)
        {
            ZipArchiveEntry entry;
            if (!entries.TryGetValue(name, out entry))
                return null;

            return ReadEntry(entry, result);
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry, HoFastBuildArtifactVerification result)
        {
            try
            {
                if (entry.Length > MaxSingleEntryBytes)
                {
                    if (result != null)
                        result.warnings.Add(entry.FullName + " 超过复核读取上限，已跳过。");
                    return null;
                }

                using (Stream stream = entry.Open())
                {
                    // 字符 Mod 的 sharedassets.bin 可能上百 MB，长度已知时直接按长度分配，
                    // 避免 MemoryStream + ToArray 造成双份占用。
                    if (entry.Length >= 0 && entry.Length <= int.MaxValue)
                    {
                        var buffer = new byte[(int)entry.Length];
                        int filled = 0;
                        while (filled < buffer.Length)
                        {
                            int read = stream.Read(buffer, filled, buffer.Length - filled);
                            if (read <= 0)
                                break;
                            filled += read;
                        }

                        if (filled == buffer.Length)
                            return buffer;

                        var truncated = new byte[filled];
                        Array.Copy(buffer, truncated, filled);
                        return truncated;
                    }

                    using (var memory = new MemoryStream())
                    {
                        var chunk = new byte[81920];
                        int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                            memory.Write(chunk, 0, read);
                        return memory.ToArray();
                    }
                }
            }
            catch (Exception exception)
            {
                if (result != null)
                {
                    result.warnings.Add(
                        entry.FullName + " 读取失败：" + exception.GetType().Name + " - " + exception.Message +
                        "（声明长度 " + DescribeLength(entry) + "）");
                }
                return null;
            }
        }

        private static string DescribeLength(ZipArchiveEntry entry)
        {
            try
            {
                return entry.Length + " 字节，压缩后 " + entry.CompressedLength + " 字节";
            }
            catch (Exception)
            {
                return "未知";
            }
        }

        private static string DescribeEntrySize(ZipArchiveEntry entry)
        {
            try
            {
                return entry.Length.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string ReadSevenBitString(byte[] data, ref int offset)
        {
            if (offset >= data.Length)
                return string.Empty;

            int length = 0;
            int shift = 0;
            while (offset < data.Length)
            {
                byte value = data[offset++];
                length |= (value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
                shift += 7;
                if (shift > 28)
                    return string.Empty;
            }

            if (length <= 0 || offset + length > data.Length)
                return string.Empty;

            string text = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return text;
        }

        private static string ReadPrintableAscii(byte[] data, int offset, int count)
        {
            if (count <= 0 || offset < 0 || offset + count > data.Length)
                return null;

            for (int index = 0; index < count; index++)
            {
                byte value = data[offset + index];
                if (value < 32 || value > 126)
                    return null;
            }

            return Encoding.ASCII.GetString(data, offset, count);
        }

        private static bool MatchesAscii(byte[] data, int offset, string value)
        {
            if (offset + value.Length > data.Length)
                return false;

            for (int index = 0; index < value.Length; index++)
            {
                if (data[offset + index] != (byte)value[index])
                    return false;
            }

            return true;
        }

        private static int Align4(int value)
        {
            return (value + 3) & ~3;
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            ulong low = ReadUInt32(data, offset);
            ulong high = ReadUInt32(data, offset + 4);
            return low | (high << 32);
        }

        #endregion
    }
}
#endif
