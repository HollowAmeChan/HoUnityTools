# IR v4 reader and compatibility projection

Run the standard-library C# validation without Unity or external packages:

```powershell
./RunReader.ps1 -Registry 'ABSOLUTE_PATH_TO_HOTOOLS/RigConstraints/registry/registry.v1.json'
```

The local harness requires the installed .NET 10 SDK. Production source uses C#
7.3 and standard library APIs, and does not depend on that SDK at runtime.
Build products go to the repository's ignored `.research/ConstraintIRV4Reader/`.

The runtime DOM retains every source/semantic/contract extension field and the
original JSON number token; it does not route the source document through
`JsonUtility` or a fixed DTO. Whitespace is normalized on serialization.
Canonical input fingerprints sort keys by Unicode code point, emit compact UTF-8
JSON, and exclude only the root `exportTime` and `inputFingerprint` fields.
Numbers exported by the Python codec retain their canonical spelling.

Editor integration:

```csharp
var registry = HoConstraintIrV4Registry.Load(registryAsset.bytes);
var document = HoConstraintIrV4Import.Read(sourceAsset.text, registry);
var projection = HoConstraintIrV4LegacyAdapter.Project(document, registry, "HoAux");
ConstraintConfig executionView = projection.Config;
```

The editor types live in
`Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR`; the DOM and shape reader
live in `Hollow.HoUnityTools.RigConstraints.ConstraintIR`.

`projection.Document` retains v4 unchanged. `Config` is a disposable v3 execution
view for the existing import planner. The caller must still run that planner's
scene, Animator, stack-barrier, ownership and backend-API checks before assembly.
The projection does not instantiate Unity objects, touch assets, select a host,
or reinterpret any v2/v3 unknown constraint.

The authority registry is supplied by the caller and validated against the
document's exact file-byte SHA-256. There is no hardcoded Blender installation
path in production. Unknown implementation IDs, versions, registry mismatches,
stale fingerprints, missing references, cycles and duplicate execution fail.
An unsupported candidate remains a diagnostic and execution barrier. An
unsupported locked implementation fails without selecting a replacement.

Only explicit v4 semantics and applicable implemented contracts enter the view.
An unregistered Copy Rotation source stays UnknownSemantic. Object-owner
constraints remain in the source DOM and diagnostics, and never drive the
armature root. PLANNED contracts are not executable. Native approximations
require `ALLOW_APPROXIMATE`; legacy Twist/Fan behavior requires `ALLOW_LEGACY`.
Verification evidence is reported independently of implementation fidelity.

Aim, Twist, Fan and MCH are projected into the old runtime's corresponding
relationship views. Multiple composite groups on one owner cannot fit the old
one-Aux-per-bone DTO and fail explicitly. Large `BoneTools/hoAux` source data and
Drivers are not read or adapted.

The tests cover JSON grammar/Unicode/number precision, v4 shape and registry
contracts, source/semantic reference integrity, input hashes, Copy R registration,
binding overlays, composite projections, planned and locked implementations,
fallback policy and dependency rejection. Optional `-Fixture` additionally reads
a Python-produced v4 document and checks fingerprint interoperability and source
extension preservation. This suite does not replace Unity compilation, scene
assembly, numerical runtime or real VRC SDK/client validation.
