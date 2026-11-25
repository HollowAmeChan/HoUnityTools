using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    public static class HoPmxEnvironmentCutover
    {
        private const string Menu = "Tools/HoUnityTools/PMX/Environment URP Cutover/";
        private const string ShaderPath = "Packages/com.hoshader.environment-urp/Runtime/Shaders/Environment.shader";
        private const string ShaderName = "HoShader/Environment URP";
        private const string BackupName = "pmx-environment-backup.json";
        private static readonly Regex ProfileLine = new(@"(?m)^  materialProfile:[ \t]*([0-9]+)[ \t]*\r?\n");
        private static readonly Regex MaterialRemap = new(
            @"(?m)^ +name: PMX_Material_(\d+)[ \t]*\r?\n +second: \{fileID:[ \t]*(-?\d+),[ \t]*guid:[ \t]*([0-9a-f]{32}),");
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Serializable]
        private sealed class Index
        {
            public int version = 0;
            public IndexRow[] sceneReferencedPmxImporters = null;
            public IndexRow[] alternateStageSourceImporters = null;
        }

        [Serializable]
        private sealed class IndexRow
        {
            public string path = null;
            public string guid = null;
            public string metaPath = null;
            public string metaSha256 = null;
            public int currentProfile = 0;
            public int targetProfile = 0;
            public string[] sceneReferences = null;
        }

        [Serializable]
        private sealed class Backup
        {
            public string projectRoot;
            public BackupRow[] importers;
        }

        [Serializable]
        private sealed class BackupRow
        {
            public string path;
            public string guid;
            public string originalSha256;
            public string migratedSha256;
        }

        private sealed class Work
        {
            internal IndexRow Row;
            internal HoPmxImporter Importer;
            internal Dictionary<int, Map> Maps;
            internal int[] RenderedSlots;
        }

        private readonly struct Map
        {
            internal readonly string Guid;
            internal readonly long FileId;

            internal Map(string guid, long fileId)
            {
                Guid = guid;
                FileId = fileId;
            }
        }

        [MenuItem(Menu + "Preview scene importers")]
        private static void PreviewMenu() => MenuAction(false, false);

        [MenuItem(Menu + "Apply scene importers")]
        private static void ApplyMenu() => MenuAction(true, false);

        [MenuItem(Menu + "Apply scene and alternate stage importers")]
        private static void ApplyAllMenu() => MenuAction(true, true);

        public static void PreviewFromCommandLine()
        {
            Run(Argument("-hoPmxCutoverIndex"), HasArgument("-hoPmxIncludeAlternates"), null);
        }

        public static void ApplyFromCommandLine()
        {
            Run(Argument("-hoPmxCutoverIndex"), HasArgument("-hoPmxIncludeAlternates"), Argument("-hoPmxCutoverBackup"));
        }

        public static void RestoreFromCommandLine()
        {
            Restore(Argument("-hoPmxCutoverBackup"));
        }

        private static void MenuAction(bool apply, bool alternates)
        {
            string plan = EditorUtility.OpenFilePanel("Select PMX cutover index", "", "json");
            if (string.IsNullOrEmpty(plan))
                return;
            if (!apply)
            {
                Run(plan, alternates, null);
                return;
            }

            string backupParent = EditorUtility.OpenFolderPanel("Select external backup location", "", "");
            if (string.IsNullOrEmpty(backupParent))
                return;
            string backup = Path.Combine(backupParent, "PmxEnvironment-" + Guid.NewGuid().ToString("N"));
            Run(plan, alternates, backup);
        }

        private static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length || arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing " + name);
            return arguments[index + 1];
        }

        private static void Run(string indexPath, bool includeAlternates, string destination)
        {
            Index index = JsonUtility.FromJson<Index>(File.ReadAllText(indexPath));
            if (index == null || index.version != 1 || index.sceneReferencedPmxImporters == null ||
                includeAlternates && index.alternateStageSourceImporters == null)
                throw new InvalidDataException("Expected the PMX lists in cutover index version 1.");

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || shader.name != ShaderName || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Install a compiling version of com.hoshader.environment-urp before PMX cutover.");

            var work = new List<Work>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            void Collect(IEnumerable<IndexRow> rows, bool alternate)
            {
                foreach (IndexRow row in rows)
                {
                    try
                    {
                        if (row == null || !paths.Add(row.path) || !guids.Add(row.guid))
                            throw new InvalidDataException("Duplicate or missing PMX path or GUID.");
                        work.Add(Validate(row, shader, alternate));
                    }
                    catch (Exception error)
                    {
                        errors.Add((row?.path ?? "<empty row>") + " • " + error.Message);
                    }
                }
            }

            Collect(index.sceneReferencedPmxImporters, false);
            if (includeAlternates)
                Collect(index.alternateStageSourceImporters, true);
            if (errors.Count != 0)
                throw new InvalidOperationException("PMX cutover preflight failed:\n" + string.Join("\n", errors));
            if (work.Count == 0)
                throw new InvalidOperationException("The PMX cutover index selected no stage importers.");

            Debug.Log($"[PMX Environment URP] {work.Count} eligible importers with {work.Sum(item => item.Maps.Count)} external remaps. " +
                $"{work.Sum(item => item.RenderedSlots.Count(slot => !item.Maps.ContainsKey(slot)))} rendered slots will be generated. " +
                $"Alternates included: {includeAlternates}.");
            if (destination == null)
                return;

            Apply(work, shader, ExternalDirectory(destination));
        }

        private static Work Validate(IndexRow row, Shader shader, bool alternate)
        {
            string absolute = ProjectPath(row.path);
            if (row.metaPath != row.path + ".meta" ||
                row.currentProfile != (int)HoPmxMaterialProfile.LilToon ||
                row.targetProfile != (int)HoPmxMaterialProfile.EnvironmentUrp ||
                (row.sceneReferences?.Length > 0) == alternate)
                throw new InvalidDataException("The PMX role, profile transition or metadata path is incompatible.");

            string savedMetadata = ReadExact(absolute + ".meta", row.metaSha256);
            if (ReadProfile(savedMetadata) != row.currentProfile)
                throw new InvalidDataException("Serialized PMX profile differs from the index.");
            HoPmxImporter importer = LoadImporter(row.path, row.guid);
            if ((int)importer.materialProfile != row.currentProfile || EditorUtility.IsDirty(importer))
                throw new InvalidOperationException("The current importer profile differs from the index or has unsaved changes.");

            Dictionary<int, Map> maps = ReadMaps(savedMetadata);
            VerifyMaps(importer, maps, shader);
            var source = HoPmxReader.Read(absolute);
            int[] rendered = Enumerable.Range(0, source.Materials.Count).Where(slot => source.Materials[slot].IndexCount > 0).ToArray();
            if (maps.Keys.Any(slot => slot >= source.Materials.Count))
                throw new InvalidDataException("An external remap refers to an unknown source material slot.");
            if (!alternate && rendered.Any(slot => !maps.ContainsKey(slot)))
                throw new InvalidDataException("A scene-referenced PMX contains a rendered slot without an audited external material.");

            return new Work { Row = row, Importer = importer, Maps = maps, RenderedSlots = rendered };
        }

        private static void Apply(List<Work> work, Shader shader, string directory)
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new IOException("Use an empty backup directory. Restore an existing backup before retrying its apply.");
            Directory.CreateDirectory(directory);
            var save = new Backup
            {
                projectRoot = ProjectRoot,
                importers = work.Select(item => new BackupRow
                {
                    path = item.Row.path,
                    guid = item.Row.guid,
                    originalSha256 = item.Row.metaSha256,
                }).ToArray(),
            };
            foreach (BackupRow row in save.importers)
            {
                string original = ProjectPath(row.path) + ".meta";
                string copy = BackupPath(directory, row.path);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                byte[] bytes = File.ReadAllBytes(original);
                CheckHash(bytes, row.originalSha256, original);
                File.WriteAllBytes(copy, bytes);
            }
            string manifest = Path.Combine(directory, BackupName);
            SaveBackup(manifest, save);

            var changed = new List<BackupRow>();
            try
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int index = 0; index < work.Count; index++)
                    {
                        Work item = work[index];
                        BackupRow row = save.importers[index];
                        string path = ProjectPath(row.path) + ".meta";
                        string original = ReadExact(path, row.originalSha256);
                        changed.Add(row);
                        item.Importer.materialProfile = HoPmxMaterialProfile.EnvironmentUrp;
                        EditorUtility.SetDirty(item.Importer);
                        AssetDatabase.WriteImportSettingsIfDirty(row.path);
                        byte[] updated = File.ReadAllBytes(path);
                        row.migratedSha256 = Hash(updated);
                        SaveBackup(manifest, save);
                        if (!OnlyProfileChanged(original, Decode(updated)))
                            throw new InvalidDataException("PMX metadata changed outside materialProfile: " + row.path);
                        AssetDatabase.ImportAsset(row.path, ImportAssetOptions.ForceUpdate);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                foreach (Work item in work)
                    VerifyResult(item, shader);
            }
            catch (Exception failure)
            {
                try
                {
                    RestoreRows(directory, changed, changed);
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException("PMX cutover and rollback failed. Backups are in " + directory, failure, restoreFailure);
                }
                throw;
            }

            Debug.Log($"[PMX Environment URP] Updated and verified {work.Count} importers. Backup: {directory}");
        }

        private static void VerifyResult(Work item, Shader shader)
        {
            HoPmxImporter importer = LoadImporter(item.Row.path, item.Row.guid);
            if (importer.materialProfile != HoPmxMaterialProfile.EnvironmentUrp)
                throw new InvalidDataException("PMX importer did not retain Environment URP: " + item.Row.path);
            VerifyMaps(importer, item.Maps, shader);
            var report = AssetDatabase.LoadAllAssetsAtPath(item.Row.path).OfType<HoPmxImportReport>().FirstOrDefault();
            if (report == null || report.materialProfile != HoPmxMaterialProfile.EnvironmentUrp)
                throw new InvalidDataException("PMX import did not produce an Environment URP report: " + item.Row.path);
            foreach (int slot in item.RenderedSlots)
            {
                if (slot >= report.materials.Count || report.materials[slot].material == null ||
                    report.materials[slot].material.shader != shader)
                    throw new InvalidDataException("Imported PMX rendered slot " + slot + " did not use Environment URP: " + item.Row.path);
            }
        }

        private static Dictionary<int, Map> ReadMaps(string metadata)
        {
            var result = new Dictionary<int, Map>();
            foreach (Match match in MaterialRemap.Matches(metadata))
            {
                int slot = int.Parse(match.Groups[1].Value);
                result.Add(slot, new Map(match.Groups[3].Value, long.Parse(match.Groups[2].Value)));
            }
            return result;
        }

        private static void VerifyMaps(HoPmxImporter importer, Dictionary<int, Map> expected, Shader shader)
        {
            var actual = importer.GetExternalObjectMap();
            if (actual.Count != expected.Count)
                throw new InvalidDataException("External remap count differs from the serialized PMX metadata.");
            foreach (var pair in expected)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), HoPmxImporter.MaterialIdentifier(pair.Key));
                if (!actual.TryGetValue(id, out var value) || !(value is Material material) || material == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long fileId) ||
                    guid != pair.Value.Guid || fileId != pair.Value.FileId)
                    throw new InvalidDataException("External remap identity changed: " + id.name);
                if (material.shader != shader)
                    throw new InvalidDataException("Migrate this external material first: " + AssetDatabase.GetAssetPath(material));
            }
        }

        private static void Restore(string directory)
        {
            directory = ExternalDirectory(directory);
            Backup save = JsonUtility.FromJson<Backup>(File.ReadAllText(Path.Combine(directory, BackupName)));
            if (save == null || save.importers == null || !string.Equals(save.projectRoot, ProjectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup does not belong to this project's PMX importers.");

            var changed = new List<BackupRow>();
            foreach (BackupRow row in save.importers)
            {
                string path = ProjectPath(row.path) + ".meta";
                HoPmxImporter importer = LoadImporter(row.path, row.guid);
                if (EditorUtility.IsDirty(importer))
                    throw new InvalidOperationException("The importer has unsaved changes: " + row.path);
                string original = ReadExact(BackupPath(directory, row.path), row.originalSha256);
                byte[] current = File.ReadAllBytes(path);
                string hash = Hash(current);
                if (hash == row.originalSha256)
                    continue;
                if (hash != row.migratedSha256 && !OnlyProfileChanged(original, Decode(current)))
                    throw new IOException("The current PMX metadata differs from this cutover backup: " + row.path);
                changed.Add(row);
            }

            RestoreRows(directory, changed, save.importers);
            foreach (BackupRow row in save.importers)
            {
                ReadExact(ProjectPath(row.path) + ".meta", row.originalSha256);
                if (LoadImporter(row.path, row.guid).materialProfile != HoPmxMaterialProfile.LilToon)
                    throw new InvalidDataException("The restored PMX importer did not reload its original profile: " + row.path);
            }
            Debug.Log($"[PMX Environment URP] Restored {changed.Count} metadata files and refreshed {save.importers.Length} importers.");
        }

        private static void RestoreRows(string directory, IEnumerable<BackupRow> changed, IEnumerable<BackupRow> imports)
        {
            foreach (BackupRow row in changed)
            {
                string backup = BackupPath(directory, row.path);
                byte[] bytes = File.ReadAllBytes(backup);
                CheckHash(bytes, row.originalSha256, backup);
                File.WriteAllBytes(ProjectPath(row.path) + ".meta", bytes);
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (BackupRow row in imports)
                    AssetDatabase.ImportAsset(row.path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static HoPmxImporter LoadImporter(string path, string guid)
        {
            if (AssetDatabase.AssetPathToGUID(path) != guid || !(AssetImporter.GetAtPath(path) is HoPmxImporter importer))
                throw new InvalidDataException("Unity asset GUID or HoPmxImporter does not match: " + path);
            return importer;
        }

        private static int ReadProfile(string metadata)
        {
            Match match = ProfileLine.Match(metadata);
            return match.Success ? int.Parse(match.Groups[1].Value) : (int)HoPmxMaterialProfile.LilToon;
        }

        private static bool OnlyProfileChanged(string original, string updated)
        {
            return ReadProfile(original) == (int)HoPmxMaterialProfile.LilToon &&
                ReadProfile(updated) == (int)HoPmxMaterialProfile.EnvironmentUrp &&
                ProfileLine.Matches(original).Count <= 1 && ProfileLine.Matches(updated).Count == 1 &&
                ProfileLine.Replace(original, "") == ProfileLine.Replace(updated, "");
        }

        private static string ProjectPath(string path) => PmxPath(ProjectRoot, path);
        private static string BackupPath(string directory, string path) => PmxPath(directory, path) + ".meta";

        private static string PmxPath(string root, string path)
        {
            string[] parts = path?.Split('/');
            if (parts == null || parts.Length < 5 || parts[0] != "Assets" || parts[1] != "00动画" ||
                !(parts[2] is "MMD场景" or "MMD动画场景" or "HO静帧场景") ||
                !path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) ||
                parts.Any(part => part is "" or "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException("Expected a canonical stage PMX asset path: " + path);
            return Path.Combine(root, path);
        }

        private static string ExternalDirectory(string directory)
        {
            string path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(path, ProjectRoot, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The PMX cutover backup must be outside the Unity project.");
            return path;
        }

        private static string ReadExact(string path, string expected)
        {
            byte[] bytes = File.ReadAllBytes(path);
            CheckHash(bytes, expected, path);
            return Decode(bytes);
        }

        private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        private static void CheckHash(byte[] bytes, string expected, string path)
        {
            if (string.IsNullOrEmpty(expected) || Hash(bytes) != expected)
                throw new IOException("PMX metadata checksum changed: " + path);
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void SaveBackup(string path, Backup save)
        {
            string temporary = path + ".writing";
            File.WriteAllText(temporary, JsonUtility.ToJson(save, true) + "\n");
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
    }
}

