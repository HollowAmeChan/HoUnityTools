param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project = '',
    [string]$Method = 'CombineArmatureValidation.RunBatch',
    [switch]$EditorMode
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$editorDirectory = Split-Path -Parent $Unity
$versionDirectory = Split-Path -Parent $editorDirectory
$editorVersion = [regex]::Match((Split-Path -Leaf $versionDirectory), '\d{4}\.\d+\.\d+[abfp]\d+').Value
if (-not $editorVersion) { throw 'Cannot determine Unity editor version from its installation path.' }
if (-not $Project) {
    $Project = Join-Path $repoRoot ".research\CombineArmature-$editorVersion"
}
$Project = [IO.Path]::GetFullPath($Project)
if (Test-Path -LiteralPath $Project) { throw 'Validation project must be a new directory.' }
New-Item -ItemType Directory -Path "$Project\Assets\Editor", "$Project\Assets\Probe", "$Project\Packages", "$Project\ProjectSettings" -Force | Out-Null
# Copy just the new standalone assemblies, proving no VRC/NDMF/MA/URP/InputSystem dependency.
Copy-Item -LiteralPath "$repoRoot\Runtime\CombineArmature" -Destination "$Project\Assets\CombineArmatureRuntime" -Recurse
Copy-Item -LiteralPath "$repoRoot\Editor\CombineArmature" -Destination "$Project\Assets\CombineArmatureEditor" -Recurse
Copy-Item -LiteralPath "$PSScriptRoot\CombineArmatureValidation.cs" -Destination "$Project\Assets\Editor"
Copy-Item -LiteralPath "$PSScriptRoot\CombineArmatureTestReferences.cs" -Destination "$Project\Assets\Probe"
$manifest = '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}'
[IO.File]::WriteAllText("$Project\Packages\manifest.json", $manifest)
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt", "m_EditorVersion: $editorVersion`n")
$logPath = Join-Path $env:TEMP ('HoCombine-' + [Guid]::NewGuid().ToString('N') + '.log')
$arguments = @('-projectPath', "`"$Project`"", '-executeMethod', $Method, '-logFile', "`"$logPath`"")
if (-not $EditorMode) { $arguments = @('-batchmode', '-nographics') + $arguments }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
Write-Output "Unity exit: $($process.ExitCode)"
Write-Output "Log: $logPath"
if (-not (Test-Path -LiteralPath "$Project\combine-armature-result.txt")) { throw "Validation did not run. Read $logPath" }
$result = Get-Content -LiteralPath "$Project\combine-armature-result.txt" -Raw
Write-Output $result
if (-not $result.StartsWith('PASS ')) { throw "Validation failed. Read $logPath" }
if ($process.ExitCode -ne 0) { throw "Unity validation failed. Read $logPath" }
