namespace Hollow.HoUnityTools.RigConstraints.ConstraintIR
{
    /// <summary>Nonserialized provenance for a backend execution plan built from IR v4.</summary>
    public sealed class HoConstraintIrImportContext
    {
        public HoConstraintIrV4Document Document;
        public string BackendId, SourceFingerprint, RegistrySha256;
        public string SelectedImplementationsJson, DiagnosticsJson;
        public int SourceCount { get { return Document == null ? 0 : Document.Sources.Count; } }
    }
}
