# HoFBX BS corrective v3 checks

This test suite builds its own scene; it does not open or modify BreakWarudo or any user project.

From this repository, run:

```powershell
./Tests~/HoBsCorrective/run_validation.ps1 -UnityPath 'D:/Unity/2022.3.22f1/Editor/Unity.exe' -BlenderPath 'D:/Blender/blender-4.5.8-windows-x64/blender.exe'
```

Blender must have the local HoTools add-on installed. Omit `-BlenderPath` to use the committed v3 fixtures. The disposable Unity project, output and logs live under ignored `Temp~/BsCorrectiveValidation*`. A successful engine run exits with zero and writes `BS_CORRECTIVE_UNITY_OK`; the runner removes the previous result before launching.

`blender_bs_authoring_probe.py` builds two poses, two tasks and five outputs across two meshes, including two distinct keys on the same mesh. It tests the actual HoFBX sidecar exporter and records six quaternion input/output samples from real Blender drivers in `bs_authoring_samples.json`.

`HoBsCorrectiveChecks.cs` validates:

- v3 point/task links and five flattened runtime outputs;
- MCH measurement-bone mapping and authored source-bone preservation;
- parity with all six Blender samples, with and without parent rotation;
- shared Gaussian sample caches, same-frame radius invalidation and preview cleanup;
- repeated import preserving manual rules, read-only preview, and missing-reference failure;
- rejection of duplicate point links, duplicate targets, nonzero baseline, zero duplicate samples and old schema versions.

The disposable project compiles the actual corrective component, final-pose loop, face-output ownership helper, inspector, metadata importer and BS import-preview partial against Unity 2022.3.22f1. `PreviewHost.cs` supplies only the preview partial's existing `targetRig` host field; this is a focused component/import test, not a build of the entire user project.

The Unity inspector exposes a fixed zero baseline. Blender authoring edits point/task associations and target ranges; the Unity import expands each task target to one central component row with a one-hot control-point vector. Targets sharing a bone, radius and sample positions reuse their Gaussian kernels. Kernel matrices and coefficients use double precision; renderer weights remain Unity's 0..100 floats.
