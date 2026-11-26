Private GPU fixture only.

Run Unity CLI on D:/Temp/HoMaterialMigrationB1-20261126/Project with -force-d3d12 -executeMethod ShadowResponseGpuChecks.Run. Requires a fresh Assets/__ShadowResponseGpuReadback directory and report path (Run intentionally refuses overwrite). Current proof is D:/Temp/ho-shadow-response-gpu-validation.json and .log.

The 17 actual input records are frozen in dependency-hashes.json. Curves are produced with the existing private migration Curve/Shade/Band authoring implementation, then the public actual Baker writes RGBAHalf. GPU probe uses a separately frozen old EffectBand/fixed-blur shade implementation and the actual local blend HLSL; all comparisons read actual render targets. This validates shadow response core, not complete Forward scene rendering.

Versioned source17-inputs.json is the immutable actual input corpus. Copy the fixture and this file into a private host, or set equivalent task-local paths before running. Never run fixture asset creation in production.

Prepare the frozen blend include before running: copy `HoShader/Effect_URP/Runtime/Shaders/effect_blend.hlsl` into the private host at `Assets/HoShadowResponseSnapshot/Effect_URP/Runtime/Shaders/effect_blend.hlsl`. Verify the copied file against the blend SHA256 in `dependency-hashes.json`; it is a source fixture dependency, not a production asset. Preserve the snapshot path because ShadowResponseGpuProbe.shader includes it directly.
