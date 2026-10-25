# IR v3 regression

This harness copies the production constraint importer, runtime, and their small
dependencies into a **new isolated Unity project**. It does not edit the open user scene.

Generate the fixture with Blender and the user's WTD source file:

```powershell
& 'D:\Blender\blender-4.5.8-windows-x64\blender.exe' --background --factory-startup --disable-autoexec 'PATH\WTD-summer.blend' --python-exit-code 1 --python 'PATH\HoTools\tests\export_wtd_ir_v3_fixture.py' -- 'ABSOLUTE_FIXTURE_DIRECTORY'
```

The exporter selects the C1 dependency closure, tags its existing tracking pairs
as generic rotation-only Aim, samples 60 deterministic poses and 24 actual mesh
vertices, and writes a raw-rest FBX plus v3 JSON. It never saves the source .blend.
Other experiments in that source file are outside this fixture; production IR
export still retains their facts and unknown constraints.

Run from PowerShell:

```powershell
./Run.ps1 -Fixture 'ABSOLUTE_FIXTURE_DIRECTORY' -TestVrcApiShim
```

The default editor is Unity 6000.3.15f1; `-Unity` can select another installation.
`-Project` must point to a new directory. Omitting it uses this repo's ignored
`.research/` workspace. The report and log are written next to that project.

The checks cover:

- v2 compatibility, v3 ownership uniqueness, invalid Aim rejection;
- reference-path restoration from a serialized Ho prefab;
- standard and Ho copy stacks at 0.3 with dynamic parents and object transforms;
- collinear/sparse binding data with the declared HoFBX Y/X axis convention;
- repeated import without helper leaks and preservation of manual host content;
- multi-source weights, per-source offsets, Aim's zero-weight dependencies,
  and source-list overflow through the VRC reflection/serialization bridge;
- rollback when a shim SDK configuration method deliberately throws;
- the real 12-bone/13-raw-constraint WTD C1 graph and actual skinned mesh.

Native/VRC Parent mapping is intentionally **not** asserted to equal Blender
Armature. Standard's non-half rotation interpolation is checked against its
native contract and its deviation from Blender is recorded. Ho uses Slerp.
The nonuniform-parent-scale pose is a diagnostic case, not an equality pass.

The optional VRC classes are test doubles, installed only inside this isolated
project. A shim pass proves API mapping and ownership behavior, **not VRChat
SDK/client numerical compatibility**. Do not copy the shim into a real avatar project.
