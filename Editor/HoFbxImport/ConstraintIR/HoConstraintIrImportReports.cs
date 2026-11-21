using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR
{
    /// <summary>Project-local provenance. Never referenced by a scene, prefab, player, or Warudo asset bundle.</summary>
    [FilePath("ProjectSettings/HoUnityTools/ConstraintIRImportReports.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class HoConstraintIrImportReports : ScriptableSingleton<HoConstraintIrImportReports>
    {
        [Serializable] public sealed class Entry
        {
            public string objectId, sourceArmature, backendId, inputFingerprint, registrySha256;
            public int sourceSchemaVersion;
            public string authoritativeSourceJson, selectedImplementationsJson, diagnosticsJson;
        }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public Entry Find(GameObject rig)
        { return entries.Find(x => x.objectId == ObjectId(rig)); }
        public void Record(ConstraintConfig config, GameObject rig, string backend)
        {
            Undo.RecordObject(this, "记录约束 IR 导入报告");
            string id = ObjectId(rig); var context = config.sourceContext;
            entries.RemoveAll(x => x.objectId == id);
            if (context != null) entries.Add(new Entry { objectId = id, sourceArmature = config.armatureName, backendId = backend, sourceSchemaVersion = 4,
                inputFingerprint = context.SourceFingerprint, registrySha256 = context.RegistrySha256, authoritativeSourceJson = context.Document.ToJson(),
                selectedImplementationsJson = context.SelectedImplementationsJson, diagnosticsJson = context.DiagnosticsJson });
            Save(true);
        }
        public void Remove(GameObject rig) { Undo.RecordObject(this, "清除约束 IR 导入报告"); entries.RemoveAll(x => x.objectId == ObjectId(rig)); Save(true); }
        private void OnEnable() { Undo.undoRedoPerformed -= SaveAfterUndo; Undo.undoRedoPerformed += SaveAfterUndo; }
        private void OnDisable() { Undo.undoRedoPerformed -= SaveAfterUndo; }
        private void SaveAfterUndo() { Save(true); }
        private static string ObjectId(GameObject rig)
        { return string.IsNullOrEmpty(rig.scene.path) ? "unsaved-scene:" + rig.scene.handle + ":" + rig.GetInstanceID() : GlobalObjectId.GetGlobalObjectIdSlow(rig).ToString(); }
    }
}
