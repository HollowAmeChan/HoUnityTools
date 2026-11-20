param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$BlenderPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'Temp~\BsCorrectiveValidation'
$editor = Join-Path $project 'Assets\Editor'
$runtime = Join-Path $project 'Assets\Runtime'
New-Item -ItemType Directory -Force -Path $editor,$runtime,(Join-Path $project 'ProjectSettings'),(Join-Path $project 'Packages') | Out-Null
if ($BlenderPath) {
    & $BlenderPath --background --factory-startup --enable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'blender_bs_authoring_probe.py') -- --output (Join-Path $PSScriptRoot 'bs_authoring_results.json')
    if ($LASTEXITCODE -ne 0) { throw 'Blender fixture generation failed.' }
    $fbxFixtures = Join-Path $repo 'Temp~\BsCorrectiveFbxFixtures'
    & $BlenderPath --background --factory-startup --enable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'blender_bs_fbx_probe.py') -- --output $fbxFixtures
    if ($LASTEXITCODE -ne 0) { throw 'Blender FBX transform fixtures failed.' }
    New-Item -ItemType Directory -Force -Path (Join-Path $editor 'FbxFixtures') | Out-Null
    Get-ChildItem -LiteralPath $fbxFixtures -File | Copy-Item -Destination (Join-Path $editor 'FbxFixtures') -Force
}
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.22f1' -Encoding utf8
Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Value '{"dependencies":{}}' -Encoding utf8
foreach ($relative in @('Runtime\Constraints\HoBsCorrective.cs','Runtime\Constraints\HoBsCorrective.UpdateLoop.cs','Runtime\FaceTracking\HoFaceOutputOwnership.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $runtime -Force
}
foreach ($relative in @('Editor\HoFbxImport\HoFbxBsCorrectiveImportUtility.cs','Editor\HoFbxImport\HoFbxImportProcessingWindow.BsCorrective.cs','Editor\Constraints\HoBsCorrectiveEditor.cs','Tests~\HoBsCorrective\HoBsCorrectiveChecks.cs','Tests~\HoBsCorrective\bs_authoring_results.json','Tests~\HoBsCorrective\bs_authoring_samples.json','Tests~\HoBsCorrective\bs_independent_cases.json')) {
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $editor -Force
}
# This supplies the existing host contract while compiling the real preview partial.
Set-Content -LiteralPath (Join-Path $editor 'PreviewHost.cs') -Encoding utf8 -Value 'namespace Hollow.HoUnityTools.Editor.RigConstraints { internal sealed partial class HoFbxImportProcessingWindow : UnityEditor.EditorWindow { private UnityEngine.GameObject targetRig; } }'
$result = Join-Path $project 'results.txt'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$log = Join-Path $repo 'Temp~\BsCorrectiveValidation.log'
$unityArgs = @('-batchmode','-nographics','-projectPath',('"' + $project + '"'),'-executeMethod','HoBsCorrectiveChecks.Run','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $result)) { throw "Unity validation failed; inspect $log" }
Get-Content -LiteralPath $result
