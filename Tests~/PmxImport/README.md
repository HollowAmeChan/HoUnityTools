# PMX importer validation

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
