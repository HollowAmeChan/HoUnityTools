using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    // An explicit migration boundary. Neither startup nor ordinary reimport invokes Apply.
    public static class HoPmxSpecializedMigration
    {
        public static void RunAuditLegacy()
        {
            string output = Environment.GetEnvironmentVariable("HO_PMX_MIGRATION_PLAN");
            if (string.IsNullOrEmpty(output) || File.Exists(output) || Path.GetFullPath(output).StartsWith(ProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Set HO_PMX_MIGRATION_PLAN to a new external file.");
            var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                && p.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) && AssetImporter.GetAtPath(p) is HoPmxImporter importer
                && importer.materialProfile == HoPmxMaterialProfile.LilToon).ToArray();
            WriteAudit(paths, output);
            Debug.Log("HO_PMX_LEGACY_AUDIT_OK importers=" + paths.Length);
        }

        public static void RunApplyPlanned()
        {
            Apply(Environment.GetEnvironmentVariable("HO_PMX_MIGRATION_PLAN") ?? throw new InvalidOperationException("Set HO_PMX_MIGRATION_PLAN."),
                Environment.GetEnvironmentVariable("HO_PMX_MIGRATION_BACKUP") ?? throw new InvalidOperationException("Set HO_PMX_MIGRATION_BACKUP."));
            Debug.Log("HO_PMX_SPECIALIZED_MIGRATION_OK");
        }

        [Serializable] public sealed class Plan
        {
            public int version = 1;
            public string projectRoot;
            public Row[] importers;
        }
        [Serializable] public sealed class Row
        {
            public string path;
            public string guid;
            public string sourceSha256;
            public string metaSha256;
            public bool explicitProfile;
            public HoPmxMaterialProfile currentProfile;
            public HoPmxMaterialProfile targetProfile = HoPmxMaterialProfile.SpecializedUrp;
            public Slot[] slots;
        }
        [Serializable] public sealed class Slot
        {
            public int materialIndex;
            public string sourceName;
            public HoPmxMaterialFamily targetFamily;
            public string reason;
            public bool reviewed;
        }

        private sealed class Work
        {
            internal Row Row;
            internal HoPmxImporter Importer;
            internal string Remaps;
            internal Dictionary<string, long> MaterialIds;
        }

        public static Plan Audit(string[] assetPaths)
        {
            if (assetPaths == null) throw new ArgumentNullException(nameof(assetPaths));
            var rows = new List<Row>();
            foreach (string path in assetPaths.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                var importer = RequireImporter(path);
                var doc = HoPmxReader.Read(Absolute(path));
                string meta = File.ReadAllText(Absolute(path + ".meta"));
                var slots = new List<Slot>();
                for (int i = 0; i < doc.Materials.Count; ++i)
                {
                    var source = doc.Materials[i];
                    var info = new HoPmxMaterialInfo { index = i, sourceName = source.Name, memo = source.Comment };
                    HoPmxSpecializedMaterials.ResolveFamily(info, importer.materialSettings.Find(x => x.materialIndex == i));
                    slots.Add(new Slot { materialIndex = i, sourceName = source.Name, targetFamily = info.targetFamily,
                        reason = info.familyReason, reviewed = false });
                }
                rows.Add(new Row { path = path, guid = AssetDatabase.AssetPathToGUID(path),
                    sourceSha256 = Hash(Absolute(path)), metaSha256 = Hash(Absolute(path + ".meta")),
                    explicitProfile = System.Text.RegularExpressions.Regex.IsMatch(meta, @"(?m)^  materialProfile:"),
                    currentProfile = importer.materialProfile, slots = slots.ToArray() });
            }
            return new Plan { projectRoot = ProjectRoot, importers = rows.ToArray() };
        }

        public static void WriteAudit(string[] assetPaths, string outputPath)
        {
            File.WriteAllText(outputPath, JsonUtility.ToJson(Audit(assetPaths), true));
        }

        // Review the JSON plan and set each slot's family/reviewed before calling this API.
        // Existing Environment profile 1 is deliberately outside this legacy upgrade operation.
        public static void Apply(string planPath, string backupDirectory)
        {
            var plan = ReadPlan(planPath);
            string backup = Path.GetFullPath(backupDirectory ?? throw new ArgumentNullException(nameof(backupDirectory)));
            if (Within(backup, ProjectRoot) || Directory.Exists(backup) || File.Exists(backup))
                throw new InvalidOperationException("备份目录必须是工程外尚不存在的新目录。");
            var work = new List<Work>();
            foreach (var row in plan.importers)
            {
                var importer = RequireImporter(row.path);
                if (row.currentProfile != HoPmxMaterialProfile.LilToon || row.targetProfile != HoPmxMaterialProfile.SpecializedUrp ||
                    importer.materialProfile != row.currentProfile)
                    throw new InvalidOperationException("只接受明确的 Legacy 0 → Specialized 2 迁移：" + row.path);
                if (EditorUtility.IsDirty(importer) || AssetDatabase.AssetPathToGUID(row.path) != row.guid ||
                    Hash(Absolute(row.path)) != row.sourceSha256 || Hash(Absolute(row.path + ".meta")) != row.metaSha256)
                    throw new InvalidOperationException("审计后来源、GUID、导入设置或 meta 已变化，请重新审计：" + row.path);
                var doc = HoPmxReader.Read(Absolute(row.path));
                if (row.slots == null || row.slots.Length != doc.Materials.Count ||
                    row.slots.Select(x => x.materialIndex).Distinct().Count() != doc.Materials.Count)
                    throw new InvalidOperationException("迁移计划必须覆盖且只覆盖每个原始材质槽：" + row.path);
                foreach (var slot in row.slots)
                {
                    if (!slot.reviewed || slot.materialIndex < 0 || slot.materialIndex >= doc.Materials.Count ||
                        slot.sourceName != doc.Materials[slot.materialIndex].Name || slot.targetFamily == HoPmxMaterialFamily.Auto ||
                        !Enum.IsDefined(typeof(HoPmxMaterialFamily), slot.targetFamily))
                        throw new InvalidOperationException("材质槽未复核或来源不符：" + row.path + " #" + slot.materialIndex);
                    RequireInstalledFamily(slot.targetFamily);
                }
                work.Add(new Work { Row = row, Importer = importer, Remaps = Remaps(importer), MaterialIds = MaterialIds(row.path) });
            }
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "plan.json"), JsonUtility.ToJson(plan, true));
            foreach (var item in work) File.Copy(Absolute(item.Row.path + ".meta"), Path.Combine(backup, item.Row.guid + ".meta"));
            try
            {
                foreach (var item in work)
                {
                    foreach (var slot in item.Row.slots)
                    {
                        var setting = item.Importer.materialSettings.Find(x => x.materialIndex == slot.materialIndex);
                        if (setting == null)
                        {
                            setting = new HoPmxMaterialSetting { materialIndex = slot.materialIndex };
                            item.Importer.materialSettings.Add(setting);
                        }
                        setting.targetFamily = slot.targetFamily;
                    }
                    item.Importer.materialProfile = HoPmxMaterialProfile.SpecializedUrp;
                    EditorUtility.SetDirty(item.Importer);
                    item.Importer.SaveAndReimport();
                    var current = RequireImporter(item.Row.path);
                    if (AssetDatabase.AssetPathToGUID(item.Row.path) != item.Row.guid || Remaps(current) != item.Remaps)
                        throw new InvalidOperationException("GUID 或外部材质关联发生变化：" + item.Row.path);
                    var ids = MaterialIds(item.Row.path);
                    if (item.MaterialIds.Any(x => !ids.TryGetValue(x.Key, out long value) || value != x.Value))
                        throw new InvalidOperationException("生成材质标识/fileID 发生变化：" + item.Row.path);
                    var report = AssetDatabase.LoadAllAssetsAtPath(item.Row.path).OfType<HoPmxImportReport>().FirstOrDefault();
                    if (report == null || report.materialProfile != HoPmxMaterialProfile.SpecializedUrp ||
                        report.materials.Count != item.Row.slots.Length || report.materials.Any(x => x.material == null))
                        throw new InvalidOperationException("专职材质重导入未完成，请检查导入错误：" + item.Row.path);
                }
                File.WriteAllText(Path.Combine(backup, "completed.json"), JsonUtility.ToJson(Audit(work.Select(x => x.Row.path).ToArray()), true));
            }
            catch
            {
                // Restore every selected meta, including the row whose reimport failed.
                foreach (var item in work)
                    File.Copy(Path.Combine(backup, item.Row.guid + ".meta"), Absolute(item.Row.path + ".meta"), true);
                foreach (var item in work) AssetDatabase.ImportAsset(item.Row.path, ImportAssetOptions.ForceUpdate);
                throw;
            }
        }

        public static void Restore(string backupDirectory)
        {
            string backup = Path.GetFullPath(backupDirectory);
            var original = ReadPlan(Path.Combine(backup, "plan.json"));
            var completed = ReadPlan(Path.Combine(backup, "completed.json"));
            if (!original.importers.Select(x => x.path + "|" + x.guid).OrderBy(x => x).SequenceEqual(
                completed.importers.Select(x => x.path + "|" + x.guid).OrderBy(x => x)))
                throw new InvalidOperationException("备份原始/完成列表不匹配。");
            foreach (var row in completed.importers)
                if (AssetDatabase.AssetPathToGUID(row.path) != row.guid || Hash(Absolute(row.path + ".meta")) != row.metaSha256 ||
                    Hash(Absolute(row.path)) != row.sourceSha256 || EditorUtility.IsDirty(RequireImporter(row.path)))
                    throw new InvalidOperationException("迁移后设置已变化，拒绝覆盖；请手工合并备份：" + row.path);
            foreach (var row in original.importers)
                if (Hash(Path.Combine(backup, row.guid + ".meta")) != row.metaSha256)
                    throw new InvalidOperationException("备份校验失败：" + row.path);
            foreach (var row in original.importers)
                File.Copy(Path.Combine(backup, row.guid + ".meta"), Absolute(row.path + ".meta"), true);
            foreach (var row in original.importers) AssetDatabase.ImportAsset(row.path, ImportAssetOptions.ForceUpdate);
        }

        private static Plan ReadPlan(string path)
        {
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(path));
            if (plan == null || plan.version != 1 || plan.importers == null || plan.importers.Length == 0 ||
                !string.Equals(Path.GetFullPath(plan.projectRoot ?? ""), ProjectRoot, StringComparison.OrdinalIgnoreCase) ||
                plan.importers.Select(x => x.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.importers.Length)
                throw new InvalidOperationException("迁移计划版本、工程或资产列表不符。");
            return plan;
        }
        private static HoPmxImporter RequireImporter(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) || !Within(Absolute(path), Absolute("Assets")))
                throw new InvalidOperationException("只接受当前工程 Assets 内 PMX：" + path);
            return AssetImporter.GetAtPath(path) as HoPmxImporter ?? throw new InvalidOperationException("不是 Ho PMX 导入器：" + path);
        }
        private static void RequireInstalledFamily(HoPmxMaterialFamily family)
        {
            string package = family == HoPmxMaterialFamily.Eye || family == HoPmxMaterialFamily.Skin ? "skin-urp-stylized" : family.ToString().ToLowerInvariant() + "-urp";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.hoshader." + package + "/Runtime/Shaders/" + family + ".shader");
            if (shader == null || shader.name != "HoShader/" + family + " URP")
                throw new InvalidOperationException("迁移前请安装目标家族包：" + family);
        }
        private static Dictionary<string, long> MaterialIds(string path)
        {
            var ids = new Dictionary<string, long>();
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string _, out long id))
                    ids.Add(material.GetTag("HO_PMX_MaterialIndex", false), id);
            return ids;
        }
        private static string Remaps(HoPmxImporter importer) => string.Join("\n", importer.GetExternalObjectMap()
            .Select(x => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x.Value, out string guid, out long id);
                return x.Key.type.FullName + "|" + x.Key.name + "|" + guid + "|" + id; }).OrderBy(x => x, StringComparer.Ordinal));
        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(ProjectRoot, path));
        private static bool Within(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }
}
