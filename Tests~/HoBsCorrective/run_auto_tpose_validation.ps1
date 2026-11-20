param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$BlenderPath
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fixtures = Join-Path $repo 'Temp~/AutoTPoseFixtures'
$project = Join-Path $repo 'Temp~/AutoTPoseValidation'
$editor = Join-Path $project 'Assets/Editor'
$fixtureAssets = Join-Path $project 'Assets/Fixtures'
New-Item -ItemType Directory -Force -Path $editor,$fixtureAssets,(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings') | Out-Null
& $BlenderPath --background --factory-startup --enable-autoexec --python-exit-code 1 --python (Join-Path $PSScriptRoot 'blender_auto_tpose_probe.py') -- $fixtures
if ($LASTEXITCODE -ne 0) { throw 'Blender automatic T-Pose fixture generation failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'Editor/HoFbxImport/HoFbxHumanoidImportUtility.cs'),(Join-Path $PSScriptRoot 'HoAutoTPoseChecks.cs') -Destination $editor -Force
Get-ChildItem -LiteralPath $fixtures -File | Copy-Item -Destination $fixtureAssets -Force
Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Value '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}' -Encoding utf8
$result = Join-Path $project 'auto_tpose_results.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$log = Join-Path $repo 'Temp~/AutoTPoseValidation.log'
$arguments = @('-batchmode','-nographics','-projectPath',('"'+$project+'"'),'-executeMethod','HoAutoTPoseChecks.Run','-logFile',('"'+$log+'"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $result)) { throw "Automatic T-Pose comparison failed; inspect $log" }
Get-Content -LiteralPath $result
