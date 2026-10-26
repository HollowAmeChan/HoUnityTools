param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project = '',
    [string]$GdBuilder = '',
    [string]$Fixture = ''
)
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $GdBuilder) { $GdBuilder = Join-Path $taskRepo '..\lilToon-URP-Extensions\Runtime\GeometryData\Outline\HoGeometryDataOutlineCorrectionBuilder.cs' }
if (-not (Test-Path -LiteralPath $GdBuilder)) { throw 'Provide -GdBuilder pointing to the current GD reference implementation.' }
$taskVersion = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $Unity))
if (-not $Project) { $Project = Join-Path $taskRepo ('.research\FbxOutline-' + $taskVersion + '-' + [Guid]::NewGuid().ToString('N')) }
$Project = [IO.Path]::GetFullPath($Project)
if (Test-Path -LiteralPath $Project) { throw 'Use a new isolated validation project.' }
New-Item -ItemType Directory -Force -Path "$Project\Assets\Runtime", "$Project\Assets\Editor", "$Project\Packages", "$Project\ProjectSettings" | Out-Null
foreach ($taskDirectory in @('RigConstraints', 'BoneRenderer', 'CombineArmature')) {
    Copy-Item -LiteralPath "$taskRepo\Runtime\$taskDirectory" -Destination "$Project\Assets\Runtime" -Recurse
}
Copy-Item -LiteralPath "$taskRepo\Runtime\HoEditorOnly.cs" -Destination "$Project\Assets\Runtime"
foreach ($taskDirectory in @('HoFbxImport', 'HoAuxRig', 'CombineArmature')) {
    Copy-Item -LiteralPath "$taskRepo\Editor\$taskDirectory" -Destination "$Project\Assets\Editor" -Recurse
}
Copy-Item -LiteralPath "$taskRepo\Editor\Utils\HoEditorStyles.cs", "$PSScriptRoot\FbxOutlineValidation.cs", $GdBuilder -Destination "$Project\Assets\Editor"
if ($Fixture) { Copy-Item -LiteralPath $Fixture -Destination "$Project\Assets\Character.fbx" }
# Built-in pipeline only: no lilToon, Ho rendering, URP, VRC SDK, or test framework package.
[IO.File]::WriteAllText("$Project\Packages\manifest.json", '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt", "m_EditorVersion: $taskVersion`n")
$taskLog = "$Project.log"
$taskProcess = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'), '-executeMethod', 'FbxOutlineValidation.Run', '-logFile', ('"' + $taskLog + '"')) -PassThru -Wait -WindowStyle Hidden
if ($taskProcess.ExitCode -ne 0) { throw "Unity validation failed. See $taskLog" }
$taskResult = Join-Path $Project 'outline-results.txt'
if (-not (Test-Path -LiteralPath $taskResult)) { throw "Validation did not run. See $taskLog" }
Get-Content -LiteralPath $taskResult
Write-Output "Report: $taskResult"
Write-Output "Log: $taskLog"
