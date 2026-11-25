using System;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    public enum HoMigrationFamily { Cloth, Hair, Skin, Eye, Environment }

    [Serializable] public sealed class HoMaterialMigrationPlan
    {
        public int schemaVersion = 1;
        public string projectRoot, batchId, sourceInventoryPath, sourceInventorySha256, nativeAuditPath, nativeAuditSha256;
        public string[] globalRetirementNotes = Array.Empty<string>();
        public HoMaterialMigrationRow[] rows = Array.Empty<HoMaterialMigrationRow>();
        public HoMaterialMigrationSlotAction[] slotActions = Array.Empty<HoMaterialMigrationSlotAction>();
    }
    [Serializable] public sealed class HoMaterialMigrationRow
    {
        public string assetPath, sourceGuid, sourceSha256, sourceMetaSha256;
        public long sourceLocalId = 2100000;
        public string sourceShaderGuid, sourceShaderPath, sourceShaderSha256, targetFamily;
        public bool reviewed, acceptFamilyModelChanges, provisional, requiresSlotFork;
        public string[] acceptedDifferences = Array.Empty<string>(), rationale = Array.Empty<string>();
    }
    [Serializable] public sealed class HoMaterialMigrationSlotAction
    {
        public string kind, containerPath, containerGuid, containerSha256, containerMetaSha256;
        public string rendererGlobalId, rendererPath, rendererType;
        public int componentIndex, slot;
        public string expectedMaterialGuid, targetSourceGuid, targetFamily, targetAssetPath;
        public long expectedMaterialLocalId = 2100000;
        public bool reviewed;
        public string[] rationale = Array.Empty<string>();
    }
}
