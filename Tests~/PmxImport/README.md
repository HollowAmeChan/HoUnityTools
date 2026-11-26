# PMX importer validation

## Specialized URP, no lilToon

The new profile is `SpecializedUrp = 2`; `EnvironmentUrp = 1` and legacy `LilToon = 0` retain their serialized values. New sources with missing importer settings default to profile 2. An existing meta that omits `materialProfile` remains legacy 0. The importer version stays 2 so updating this entry point does not request a blanket reimport.

The material Inspector offers a per-slot `targetFamily` override. `Auto` recognizes authored `[ho:Cloth]`, `[ho:Hair]`, `[ho:Skin]`, `[ho:Eye]`, `[ho:Environment]` in the source material name/comment. Other source role names are provisional suggestions and report that review is needed; ambiguous slots provisionally use Cloth. Model and texture filenames do not determine family. External material remaps always retain the assigned assets.

Run synthetic regeneration checks with a new disposable project; these tests distribute no character assets:

```powershell
python Tools~/PmxImport/prepare-specialized-validation.py --project D:/Temp/HoPmxSpecializedValidation/Project
unity run D:/Temp/HoPmxSpecializedValidation/Project -- -nographics -executeMethod HoPmxSpecializedValidation.Run
```

The host uses stock URP and local Cloth/Hair/Skin/Environment packages, with no lilToon material package or Ho pipeline. It copies importer source plus the two pose/IK types used by character extraction; it does not install the full HoUnityTools dependency graph. Checks cover all five families, fresh defaults, existing implicit legacy settings, stable identifiers/fileIDs/GUID, external remaps, Eye Alpha-to-Coverage conversion and retained extracted coverage. Results are under the host's `Results/`.

Hair uses `_Opacity` for transparent coverage and ignores `BaseColor.a`. Solid/cutout Hair clips the base texture alpha directly. The importer maps transparent diffuse alpha to `_Opacity`; cutout diffuse alpha is multiplied into a deterministic derived base texture's A (RGBA32, RGB unchanged), with neutral destination tint alpha/opacity. The conversion retains source color space/sampling settings and regenerates that texture after extraction/remapping. Import information states the 8-bit coverage bake. The synthetic checks include transparent diffuse alpha0.35, cutout alpha multiplication and retained derived texture fileID.

After the data checks create their fixtures, run the real Hair Forward/DepthOnly coverage readback with a graphics device:

```powershell
unity run D:/Temp/HoPmxSpecializedValidation/Project -- -force-d3d11 -executeMethod HoPmxSpecializedValidation.RunHairCoverageGpu
```

This verifies transparent alpha0.35 and the same cutout pixels in the shipping Hair Forward and DepthOnly passes. It writes `Results/hair-coverage-gpu.json`.

Conversion limits are recorded as import warnings: PMX Ramp/Sphere behavior is not equivalent to dedicated URP lighting; outline pixel widths are only initial seeds; Skin rejects transparent blending and Eye uses separate linear coverage; independent Ray alpha sources and unsupported family-specific metal/smoothness/normal transforms require authoring review. Hair keeps its authored two-lobe defaults and does not reinterpret generic PBR maps as strand data. F0, skin scattering, fabric fibers/coating, strand maps, emission effects and environment lighting are not reconstructed from PMX material names. This is a regeneration entry point, not a visual-equivalence guarantee.

## Historical stage import validation

The validation project is created outside the source repository. It copies the seven locally audited PMX models and their relative image/FX dependencies; it does not modify the original files or the user's Unity project. Source models and textures are not distributed with these tests.

Prepare the project with the bundled Python runtime:

```powershell
python Tools~/PmxImport/prepare-validation.py
```

The defaults target `D:/Unity_Fork/.codex_temp/HoPmxImportValidation` and the existing local `pmx-source-audit.json`. `--project`, `--audit`, and `--repository` override those locations. Use `--sync-code-only` after editing the importer or tests.

Run the data and editor workflow validation with Unity 6000.3.15f1:

```powershell
Unity.exe -batchmode -nographics -projectPath D:/Unity_Fork/.codex_temp/HoPmxImportValidation -executeMethod HoPmxImportValidation.Run -logFile D:/Unity_Fork/.codex_temp/HoPmxImportValidation/Results/import.log
```

This checks all seven imported models, relative textures, source triangle/material counts, continuous transparency, outlines, known Ray material maps, native Inspector Apply/Revert, material extraction and remapping, preservation of an edited extracted material, and static UV-morph baking. A generated three-vertex PMX also checks the coordinate convention and relative `./` texture path.

Run the separate graphics smoke check without `-nographics`:

```powershell
Unity.exe -batchmode -force-d3d11 -projectPath D:/Unity_Fork/.codex_temp/HoPmxImportValidation -executeMethod HoPmxImportValidation.RenderSmoke -logFile D:/Unity_Fork/.codex_temp/HoPmxImportValidation/Results/render.log
```

It renders four scene models with the local Ho URP and Ho lilToon. PNGs and result reports are written under the isolated project's `Results/`. Pixel checks reject blank frames and substantial error-shader magenta; the images must also be inspected visually. These are import smoke checks, not author-render equivalence claims.
