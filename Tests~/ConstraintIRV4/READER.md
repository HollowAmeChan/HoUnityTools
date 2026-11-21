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

## Unity entry point and durable reports

`HoConstraintIrAssetLoader.Load(TextAsset, backendId)` is the shared path used by
the HoFBX window's preview, candidate labels, apply, and automation utility.
It loads the distributed `Editor/HoFbxImport/ConstraintIR/Registry/registry.v1.json`
by its actual bytes and creates a fresh execution view on every call. Mutable
legacy DTOs are not cached or reused between preview and apply.

Sync the authority without reserializing it, and verify drift in CI:

```powershell
./SyncRegistry.ps1 -Authority 'ABSOLUTE_HOTOOLS/RigConstraints/registry/registry.v1.json'
./SyncRegistry.ps1 -Authority 'ABSOLUTE_HOTOOLS/RigConstraints/registry/registry.v1.json' -Check
```

The registry file has a local `.gitattributes` rule enforcing LF. The sync check
compares complete bytes, including the final newline.

Unsupported sources remain execution barriers in the legacy view, while the
window labels them as known-but-unsupported rather than UnknownSemantic. An
unsupported-only or Object-owner-only import cannot clear a prior assembly.
Explicit empty snapshots and wholly muted/zero-influence snapshots may clear
previous managed imports. Contract validation happens before existing components
are removed.

Successful v4 imports record the complete authoritative document, source hash,
registry hash, selected implementation IDs/versions/fidelity/verification and
diagnostics in `ProjectSettings/HoUnityTools/ConstraintIRImportReports.asset`.
This editor `ScriptableSingleton` is independent of runtime prefabs and is not
included in player or Warudo build inputs. The window displays the same selected
contracts and diagnostics. Report changes participate in Undo; no source JSON is
added to a runtime component. Unsaved-scene records use session identity; save a
scene before import for durable object identity.

Local Copy Rotation rejects authoring pose/rest changes that are not represented
by a compensated final binding. Reference comparisons include each armature's
object transform, so a pure object/bone basis conversion is not mistaken for an
authored rest change. `exportOptions.appliedConstraints=true` prevents replaying
constraints already applied into the exported rest state.
Authoring and final owner/target records must explicitly declare
`inheritRotation=true` and `inheritScale=FULL`; missing v4 facts are unsupported.
The parent identity and world rest reference must also remain unchanged. The
legacy v2/v3 DTO defaults rotation inheritance to true without reinterpreting
those old documents through the new admission gate.

The controlled `conditions.rigSpace=FINITE_POSITIVE_UNIFORM_TRS` condition is
admitted only for HoAux direct Copy Location/Rotation, Armature and Aim contracts.
The runtime owns the actual rig-root matrix check before restore/evaluation;
planning does not claim a scene transform was checked from the JSON alone.
Unknown enum values, wrong JSON types or other backend/semantic combinations are
unsupported. Reader tests exercise both the recognized enum and rejection paths.

`RunImport.ps1 -Fixture <toy.v4.json> [-OperatorFixture <HoFBX operator directory>]`
creates a new isolated Unity project. It covers actual window/automation import,
backend switching, repeat import, report persistence/Undo, preflight protection,
Prefab serialization without audit payload, real HoFBX MCH references, and empty
v4/v3 compatibility. The separate local-rotation harness checks numerical output.

Final acceptance (2026-11-21): actual HoFBX default-v4 fixtures from Blender
4.5.8 and 5.2.0 each passed 36 Unity import checks and 16,188 local runtime
measurements, without registry overrides. Maximum position/angle/scale errors
were 1.477e-6 m / 0.05059 degrees / 3.046e-6. Thirty invalid-root scenarios check
diagnostics, unchanged owners, finite transforms and recovery parity. Legacy v3
runtime regression passed 8,160 measurements. VRC evidence remains shim-only.
The authority SHA-256 is
`46c54dff620bbc6d2239c11f55c6bd66a13fc0f18fb29b270513544fff1553be`.
