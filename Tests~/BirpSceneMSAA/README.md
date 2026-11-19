# BIRP Scene MSAA toolbar regression checks

Run in an isolated project with an installed Unity editor:

```powershell
& '.\Tests~\BirpSceneMSAA\Run.ps1' -Unity 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe'
& '.\Tests~\BirpSceneMSAA\Run.ps1' -Unity 'D:\Unity\2022.3.22f1\Editor\Unity.exe'
& '.\Tests~\BirpSceneMSAA\Run.ps1' -Unity 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe'
```

The checks instantiate two real SceneViews, verify the visible MSAA buttons and
top toolbar placement, migrate the previous version's saved layout, synchronize
the toggle state, and check that hiding or moving the overlay survives native
layout restoration and a script domain reload. Project Quality Settings and the
user's MSAA preference are not changed.

On Unity 6 the checks instead verify that the MSAA overlay, toolbar button, menu
and render callback are excluded, including after a script domain reload.

Results are written to `scene-msaa-results.txt` in the generated project under
`Temp~`. These checks run without a graphics device and do not measure the rendered
anti-aliasing result.
