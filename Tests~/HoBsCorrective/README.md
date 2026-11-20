# HoFBX BS corrective v5 checks

This test suite builds its own scene; it does not open or modify BreakWarudo or any user project.

From this repository, run:

```powershell
./Tests~/HoBsCorrective/run_validation.ps1 -UnityPath 'D:/Unity/2022.3.22f1/Editor/Unity.exe' -BlenderPath 'D:/Blender/blender-4.5.8-windows-x64/blender.exe'
```

Blender must have the local HoTools add-on installed. Pass `-BlenderPath` to regenerate both the v5 numerical fixtures and the real FBX fixtures. Omitting it reuses existing local FBX fixtures as well as the committed numerical fixtures; a fresh checkout needs Blender. The disposable Unity project, output and logs live under ignored `Temp~/BsCorrectiveValidation*`. A successful engine run exits with zero and writes `BS_CORRECTIVE_UNITY_OK`; the runner removes the previous result before launching.

`blender_bs_authoring_probe.py` builds two poses and four outputs across two meshes, exercises the HoFBX sidecar exporter, and records 106 actual Blender driver samples plus 128 independent quaternion cases. `blender_bs_fbx_probe.py` additionally exports direct and MCH fixtures with nontrivial parent/bind orientation, and invokes the user-facing `ho.final_fbx_export` operator with its metadata manifest and temporary constraint cleanup. Generated FBX assets remain under ignored `Temp~/`.

`HoBsCorrectiveChecks.cs` validates:

- v5 generated pose/output links, independent pose/radius/weight and four runtime outputs;
- MCH measurement-bone mapping and authored source-bone preservation;
- parity with 106 Blender samples, with and without parent rotation, plus 128 boundary/twist/compound quaternion cases;
- independent 45°/60°/90° poses at 100° radius, stacked outputs above total 1, neutral zero and no neighbor interference;
- shared bone reads and per-pose caches across meshes, same-frame radius/weight invalidation and preview cleanup;
- real imported FBX source and MCH transforms, automatic Y_X handedness conversion and bind-rotation removal, including the full HoFBX operator output;
- repeated import preserving manual rules, read-only preview, and missing-reference failure;
- rejection of duplicate point links/targets, invalid weights/radii/quaternions, nonidentity baseline, neutral corrective poses and old schema versions. Different BS outputs may intentionally share a sampled pose.

## Yuqiu Character Parity

The character check uses the Humanoid Debug add-on's installed `VRM/VRMA_12.anim`, samples both Blender upper-arm driver outputs, and exports the open blend through HoFBX with the operator's default options. It does not save the blend. Exported assets and the disposable Unity project stay under ignored `Temp~/`.

```powershell
$blender = 'D:/Blender/blender-4.5.8-windows-x64/blender.exe'
$blend = 'E:/BaiduSyncdisk/在做工程/渲染研究/_VR/羽啾/羽啾.blend'
& $blender --background --enable-autoexec $blend --python-exit-code 1 --python (Resolve-Path 'Tests~/HoBsCorrective/blender_character_parity_probe.py') -- --output 'Temp~/YuqiuBsParity'
& 'Tests~/HoBsCorrective/run_character_parity.ps1' -UnityPath 'D:/Unity/Unity 2021.3.45f2/Editor/Unity.exe'
```

The Unity run imports the HoFBX Humanoid mapping, executes Unity's `AvatarSetupTool.MakePoseValid` Enforce T-Pose solver, writes the resulting reference skeleton back to the FBX importer, and verifies the solver against a temporary perturbed reference pose. It then applies the matching constraint IR and BS sidecar, checks all 60 targets, replays the sampled Blender quaternions, and plays the same Humanoid clip through the imported Avatar for 899 frames. A result of `DIFFERENCES` separates BS-kernel replay from Humanoid retarget parity: compare `tPoseApplied`, `tPoseErrorBefore` / `tPoseErrorAfter`, `replayMaxWeightError`, `mchSourceMaxAngle`, and `animationMaxWeightError`.

The full character run also exercises the default HoFBX pose-application preprocessor. That step may replace shape-key mesh objects; the exporter preserves rule target names and rebinds the authored pointers before writing BS metadata. `HoTools/tests/test_blender_bs_export_targets.py` covers that recovery in a small scene.

The disposable project compiles the actual corrective component, final-pose loop, face-output ownership helper, inspector, metadata importer and BS import-preview partial against Unity 2022.3.22f1. `PreviewHost.cs` supplies only the preview partial's existing `targetRig` host field; this is a focused component/import test, not a build of the entire user project.

Each output independently evaluates `weight*S(1-d/radius)*S((b+c-d)/(2*c))`, where S is smoothstep, d is the shortest quaternion angle to this pose, b is the angle to identity, and c is this pose's angle to identity. Outputs are not normalized against one another. Neutral-side attenuation preserves the fixed zero baseline; the radius is a maximum support boundary, not an equal-weight contour in every direction.

The v5 sidecar declares `LOCAL / quaternion-xyzw / radian / independent / smoothstep-compact / quaternion-angle / triangle-smoothstep`. Unity imports only the referenced point for each output. HoFBX Y_X mirrors local quaternion Y/Z signs; Unity captures each measurement bone's original FBX bind rotation. Both direct bones and MCH followers read `inverse(bind)*local`, which measures rotation in the authored bone's own axes. `local*inverse(bind)` instead measures motion in parent axes and gives incorrect weights on rest-oriented MCH bones. This calibration is internal; no user parameter is added. Parent and object transformations are excluded.

## Saved BREAK_URP scene

`prepare_scene_parity.py` copies the saved Yuqiu scene, FBX and its exact importer/Avatar settings, prefab, HoFBX metadata, and the scene's actual `VRMA_01.anim` into ignored `Temp~/YuqiuSceneParityUnity`. The original project and blend are never saved by the probes. `HoBsSceneParityChecks.Run` audits saved rules against the current FBX and sidecar, then runs the scene's actual `HoAnimationPreviewer`, HoAux and BS PlayerLoop in Unity 6.3. `HoBsSceneParityPlayer` reads the previous frame after the normal final-pose pass, rather than manually substituting constraint/BS evaluation order.

The Blender sampler accepts `--clip`, `--samples-only` and `--step` so the compared clip and times match the saved scene. `blender_character_geometry_probe.py` and `compare_character_geometry.py` compare original evaluated shape geometry with Unity's imported mesh, including seam-split vertices. Optional `-hoBsRender` on the Unity command line writes before/fixed shaded mesh renders; each variant bakes its actual skin before rendering to avoid reusing the previous GPU skinning buffer.

The 2026-11-20 saved-scene run found and fixed the MCH input basis error. Across 119 times and 60 outputs, matching-input weight error is below 5e-7; same-animation maximum weight error fell from 0.796638 to 0.134200. The remaining 2.38 degree input offset matches the difference between Blender's cached T-Pose and the saved Unity Avatar T-Pose (about 2.376 degrees). This is reported as a reference-pose difference, not a successful identical-animation comparison. Garment shape deltas match within 1e-5 model units; four strap keys are empty in the original blend as well.

### Updated direct-bone export

The user's later export disables MCH. The full `VRMA_01` check now covers 709 frames and 60 outputs (42,540 weights per direction). Blender inputs replayed in Unity agree within 6.56e-7. `blender_character_input_replay.py` additionally feeds the actual measured Unity rotations back through the original Blender native drivers: maximum error 4.77e-7, mean error 1.02e-8. All original authoring parameters match the new HoFBX sidecar exactly. Imported corrective vertex deltas agree within 1e-5 model units.

Same-time animation inputs still differ by up to 2.378 degrees and produce a maximum weight difference of 0.134631. This is an animation input / reference-pose difference; matching-input corrective evaluation passes in both directions. Do not compensate it in the BS kernel or declare identical-animation parity.

Use `prepare_scene_parity.py --project <disposable-project> --samples <blender-sample-folder>` to keep runs separate. `-hoBsRefreshImport` on the Unity command line rebuilds the copied scene's BS bindings using the current sidecar before testing. The saved original scene still contained 60 missing old MCH references after the new export, so this run explicitly used that option in the temporary project. The probes never save the original scene. Numerical results are under ignored `Temp~/YuqiuNoMchParity20261120/`.

`blender_character_input_replay.py` accepts `--unity <scene_results.json> --samples <blender_samples.json> --output <report.json>` after Blender's `--`. It must run on the original blend with HoTools enabled. No render is needed for these checks.

### Humanoid T-Pose metadata

HoFBX Humanoid JSON v2 adds optional `armatures[].tPose` beside the existing bone mapping. The export captures Blender's saved calibration, rebases against the final exported rest hierarchy, and records rest-relative TRS with explicit Y_X coordinates. The normal Unity Humanoid import calls `HoFbxHumanoidImportUtility` to build `HumanDescription.skeleton` and the Avatar in one operation. No new metadata file or user switch is needed. The original model rest transforms and mesh bind matrices remain unchanged.

`blender_humanoid_tpose_probe.py -- <output-folder>` builds direct/MCH FBX fixtures with rotated/scaled armature objects, nontrivial bone rest orientations, translation/rotation/scale calibration and centimetre units. Copy them to `Assets/TPoseFixtures` in the disposable project; `HoHumanoidTPoseChecks.CheckFixtures` compares reconstructed reference locals with a separately exported posed FBX. It passes in Unity 2022.3 and 6.3. `ApplyAndAudit` also checks repeat-import stability, unchanged model/mesh bind poses and invalid coordinate rejection.

The Blender automatic T-Pose solver also follows Unity's directional tolerances, traversal, mapped child joints, parent direction frame, hand comparison plane, all 30 finger joints, and pelvis positioning. `run_auto_tpose_validation.ps1 -UnityPath <Unity.exe> -BlenderPath <blender.exe>` builds three independent body/hand rigs, exports their rest FBX and computed reference metadata, then compares with the installed Unity editor's actual `AvatarSetupTool.MakePoseValid`. Fixtures cover rotated/scaled objects, nonzero rolls, bent fingers, an unmapped intermediate arm bone, bone tails that differ from joint positions and optional missing mappings. Maximum accepted difference is .005° / .00001 model units; remaining Unity pose error must agree within .01. Real Yuqiu validation measured a maximum local rotation difference of .000253° across 53 mappings and zero Unity alignment error. Terminal bones without children use Blender's explicit tail, matching HoFBX's exported leaf nodes.

For the saved-scene check, replace only the disposable copy's Humanoid sidecar with the newly exported v2 JSON and add `-hoBsImportHumanoid` to `HoBsSceneParityChecks.Run`. This applies the production Humanoid importer before opening the copied scene. `-hoBsRefreshImport` additionally rebuilds stale BS references in that copy. The new reference importer does not call Unity's Enforce T-Pose solver. The 2026-11-20 run over all 709 frames reduced same-animation source-angle error from 2.378° to 0.01165° and maximum weight error from 0.134631 to 0.000312806 (0.0313 percentage points). Matching-input BS error remains below 7e-7; remaining retarget differences are reported explicitly.

Bone input and same-pose multi-mesh output caches depend on local values and parameters, not frame number. Evaluation uses double precision internally; renderer weights remain Unity 0..100 floats. The Inspector exposes each pose's radius in degrees and keeps quaternion values serialized. No matrix solve or one-hot sample array remains.
