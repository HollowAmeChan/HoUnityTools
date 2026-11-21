using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.RigConstraints.ConstraintIR
{
    /// <summary>Nonserialized planning context attached only to a disposable legacy execution view.</summary>
    public sealed class HoConstraintIrImportContext
    {
        public HoConstraintIrV4Document Document;
        public string BackendId, SourceFingerprint, RegistrySha256;
        public string SelectedImplementationsJson, DiagnosticsJson;
        public readonly Dictionary<string, string> UnsupportedConstraints = new Dictionary<string, string>(StringComparer.Ordinal);
        public int SourceCount { get { return Document == null ? 0 : Document.Sources.Count; } }
    }
}
