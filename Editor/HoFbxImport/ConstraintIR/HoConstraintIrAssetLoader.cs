using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR
{
    /// <summary>One backend-specific load path for preview and apply. Cache identity includes source and registry bytes.</summary>
    public static class HoConstraintIrAssetLoader
    {
        [Serializable] private sealed class Header { public int schemaVersion; }
        public static ConstraintConfig Load(TextAsset asset, string backendId)
        {
            if (asset == null) throw new ArgumentNullException("asset");
            var header = JsonUtility.FromJson<Header>(asset.text);
            if (header == null || header.schemaVersion != 4) return JsonUtility.FromJson<ConstraintConfig>(asset.text);
            var registry = LoadDistributedRegistry();
            var document = HoConstraintIrV4Import.Read(asset.text, registry);
            // A fresh DTO prevents downstream legacy preparation from contaminating the next preview/apply.
            return HoConstraintIrV4LegacyAdapter.Project(document, registry, backendId).Config;
        }
        public static HoConstraintIrV4Registry LoadDistributedRegistry()
        {
            string path = AssetDatabase.FindAssets("HoConstraintIrV4Registry t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(x => x.EndsWith("/HoConstraintIrV4Registry.cs", StringComparison.Ordinal));
            if (string.IsNullOrEmpty(path)) throw new FileNotFoundException("Cannot locate the distributed constraint IR registry reader.");
            string registryPath = Path.Combine(Path.GetDirectoryName(path), "Registry/registry.v1.json");
            if (!File.Exists(registryPath)) throw new FileNotFoundException("The distributed constraint IR registry is absent; sync it from the Blender authority.", registryPath);
            return HoConstraintIrV4Registry.Load(File.ReadAllBytes(registryPath));
        }
        public static int SourceVersion(ConstraintConfig config) { return config != null && config.sourceContext != null ? 4 : config == null ? 0 : config.schemaVersion; }
        public static void PersistReport(ConstraintConfig config, GameObject rig, string backendId)
        {
            HoConstraintIrImportReports.instance.Record(config, rig, backendId);
            if (config.sourceContext == null) return;
            foreach (var marker in rig.GetComponentsInChildren<HoImportedConstraintMarker>(true))
                if (marker.sourceArmature == config.armatureName) { Undo.RecordObject(marker, "记录源 IR 版本"); marker.exporterVersion = "4"; EditorUtility.SetDirty(marker); }
        }
    }
}
