# Material migration derived texture storage

`HoMigrationTextureBaker` keeps its six input-baking APIs. Storage defaults to
`HoMigrationTextureFormats.OutputProfile.Production`; the material converter and
transaction plan need no extra fields. Every texture is a separate native `.asset`
main object with an adjacent `.asset.recipe.json`. Existing user resources are never
overwritten. Source textures, importers and their Read/Write flags are unchanged.

Use `HoMigrationTextureFormats.UseProfile(OutputProfile.ExactPreview)` for an
explicit scoped reference run, or set `HO_MIGRATION_TEXTURE_FORMAT=ExactPreview`
for the Unity process. Invalid environment values fail instead of selecting a
different profile. ExactPreview retains readable RFloat/RGBAFloat. Production
serializes the new asset once, removes its CPU Readable copy, then saves again.
Pixel payload and GPU sampling survive a separate Editor process.

## Versioned production policy

Recipe version 2 includes storage policy version 1, requested profile, selected
native format, color interpretation, mip count, native payload bytes, maximum/RMS
errors, rejected candidates and tested cutoffs. Profile and policy version are
part of deterministic output identity, so a precise baseline and a production
output cannot silently reuse each other's resource. Output file/meta hashes still
protect reuse. A policy change must increment its version.

| Input meaning | Candidate order | Color interpretation |
|---|---|---|
| Coverage, thinness, extracted channel | R8, RHalf, RFloat | Linear numeric R |
| Base color / HSVG / base with final alpha | RGBA32, RGBAHalf, RGBAFloat | In a Linear project, RGBA32 RGB is explicitly IEC sRGB encoded; alpha stays linear. Half/Float have no sRGB decode. |
| Emission RGBA | RGBAHalf, RGBAFloat | Working-space HDR data; never clamped to UNorm |

UNorm is considered only for finite [0,1] values. Half is considered only inside
its finite range, including HDR values up to 65504; larger values retain Float.
Normals are not rebaked, packed or color converted by this policy. An extracted R
channel is still a scalar texture; it does not become a three-channel AO map.

Each candidate is GPU sampled at the texel centers of **every mip**, alongside
the Float reference. Selection requires:

- RGBA32 RGB in Linear projects: maximum encoded error at most
  `1/255 + 0.00002` (one sRGB code, including device decode precision). In Gamma
  projects: maximum working-space RGB error at most `1/510 + 0.00002`.
- Half RGB: maximum relative error at most `1/1024`, with a 0.001 denominator floor.
- All scalar/alpha candidates: maximum absolute error at most `1/510 + 0.00002`.
- No scalar/alpha positive-versus-zero changes. Coverage operations additionally
  require zero differences in `value >= cutoff` for the source `_Cutoff`,
  `_SubpassCutoff` and the 0.001 visibility floor, when within [0,1].

A failed candidate promotes to the next format; it does not clamp, erase tiny
coverage values or silently change a tested cutout. Float is the safe fallback.
The full source filter, U/V wrap, anisotropy and mip chain remain present. Every
reference mip is independently quantized: Unity's automatic sRGB native mip
generation did not match the Float reference closely enough in the checked host.

These checks are **not a proof over continuous bilinear/trilinear samples**, all
future cutoff values, animation, or source-runtime filtering equivalence. A baked
nonlinear alpha/HSVG operation already has the spatial approximation recorded by
the Baker. Changing a migrated cutoff requires reviewing/rebaking its coverage.
Half/8-bit tolerances are explicit production tradeoffs, not bit-exact conversion.
Use ExactPreview for numeric reference comparisons.
The measured result belongs to the baking graphics API/device. Reuse checks the
sealed resource and recipe identity; it does not rerun the GPU comparison on each
load or establish an error guarantee for every other runtime graphics device.

No block compression is selected automatically. `EvaluateBc7` is diagnostic only;
a deliberately difficult 16x16 color/coverage fixture produced 206 cutoff
differences, showing why a smaller byte count alone cannot approve BC7. This does
not establish a universal error bound for BC7 or forbid a separately validated
platform-specific compression policy.

## Capacity and validation evidence (2026-11-26)

The isolated full Float preview produced 49 textures: 21 Coverage, 11 HairBase,
10 Emission and 7 HSVG. Its summed native mip payload was **2689.33 MiB**, and
text-serialized native assets occupied **5378.72 MiB**. Force-text YAML stores
pixel bytes as hex, so on-disk asset size and runtime GPU payload are different.
All these exact reference textures retained a CPU Readable copy.

Three copied real 2048x2048 outputs were checked without modifying the reference
files/metas. HSVG chose RGBAHalf, Coverage chose RHalf, and Emission chose RGBAHalf:
their combined mip payload fell from **192 MiB to 96 MiB**. R8/8-bit RGBA were
correctly rejected when they erased small positive alpha values; the coverage R8
candidate also changed 196 tested cutout results. Half had no tested semantic
changes and maximum scalar/alpha error 0.000244141. Final production assets were
unreadable and survived a second Editor process. Full-project savings depend on
the recorded candidate decisions; do not assume every image can use 8-bit.

Private host: Unity 6000.3.15f1, Linear, D3D11, NVIDIA RTX 4060. The focused format
fixture passed 45 assertions, including sRGB RGBA32, R8, required Half/Float
promotion, HDR overflow, actual Baker recipe/reuse, and source-byte preservation.
The second-process check passed 30 assertions. The existing exact Baker regression
passed 325 assertions under the explicit ExactPreview profile.

## Reproduce in a disposable host

Copy `Editor/MaterialMigration/HoMigrationTextureBaker.cs`,
`HoMigrationTextureFormats.cs`, and
`Tests~/MaterialMigration/HoMigrationTextureFormatChecks.cs` into an Editor folder
of a **new Linear Unity project**. The CLI fixture requires a GPU; do not use
`-nographics`. Set `HO_FORMAT_FIXTURE=1`, choose an unused
`HO_FORMAT_FIXTURE_SUFFIX`, and set `HO_FORMAT_TEST_OUTPUT` to a JSON path outside
Assets. Run:

```powershell
unity run <private-project> --editor-path <Unity.exe> --log-file <run.log> --no-tail -- -force-d3d11 -executeMethod HoMigrationTextureFormatChecks.Run
unity run <private-project> --editor-path <Unity.exe> --log-file <reload.log> --no-tail -- -force-d3d11 -executeMethod HoMigrationTextureFormatChecks.Reload
```

For the extra real-data checks, put readable **copies** of precise Float outputs
under `Assets/B2TextureSamples`, naming them `real-color.asset`,
`real-coverage.asset`, and `real-emission.asset`. Do not copy source metas into a
host containing their original GUIDs. If the previous Baker fixture is present at
`Assets/HoMigrationBakerFixture2/Sources/Source.mat`, the fixture additionally
checks the public Baker entry, recipe and deterministic reuse. These optional
inputs are not required for the synthetic storage-policy tests. The JSON records
exactly which entries ran; the 45-assertion result above included all three real
inputs and the public Baker check.

To rerun the original formula regression, copy `HoMigrationBakerChecks.cs`, set
`HO_BAKER_FIXTURE=1`, use a fresh `HO_BAKER_FIXTURE_SUFFIX`, set
`HO_MIGRATION_TEXTURE_FORMAT=ExactPreview`, then execute
`HoMigrationBakerChecks.Run`. Remove that environment override for Production.
