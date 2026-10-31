param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $Project) { $Project = Join-Path $repo ('.research\PlaySpeed-' + [Guid]::NewGuid().ToString('N')) }
$Project = [IO.Path]::GetFullPath($Project)
if (Test-Path -LiteralPath $Project) { throw 'Use a new isolated validation project.' }
New-Item -ItemType Directory -Force -Path "$Project\Assets\Editor", "$Project\Packages", "$Project\ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$repo\Runtime\MmdMotion" -Destination "$Project\Assets" -Recurse
Copy-Item -LiteralPath "$repo\Runtime\PlaySpeed" -Destination "$Project\Assets" -Recurse
Copy-Item -LiteralPath "$repo\Runtime\RigConstraints\HoIK" -Destination "$Project\Assets" -Recurse
Copy-Item -LiteralPath "$repo\Editor\PlaySpeed" -Destination "$Project\Assets\Editor" -Recurse
Copy-Item -LiteralPath "$repo\Editor\Constraints\HoConstraintEditorControls.cs", "$repo\Editor\Constraints\HoConstraintEditorTheme.cs" -Destination "$Project\Assets\Editor"
Copy-Item -LiteralPath "$PSScriptRoot\PlaySpeedProbe.cs" -Destination "$Project\Assets"
Copy-Item -LiteralPath "$PSScriptRoot\PlaySpeedValidation.cs" -Destination "$Project\Assets\Editor"
# Do not touch the user's EditorPrefs while another editor has the tool open.
$settingsPath = "$Project\Assets\Editor\PlaySpeed\HoPlaySpeedSettings.cs"
$settings = [IO.File]::ReadAllText($settingsPath).Replace(
    '"com.hollow.hounitytools.playspeed"',
    ('"com.hollow.hounitytools.playspeed.validation.' + [Guid]::NewGuid().ToString('N') + '"'))
[IO.File]::WriteAllText($settingsPath, $settings)
[IO.File]::WriteAllText("$Project\Packages\manifest.json", '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
$version = Split-Path (Split-Path (Split-Path $Unity -Parent) -Parent) -Leaf
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt", "m_EditorVersion: $version`n")
$log = $Project + '.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'), '-executeMethod', 'PlaySpeedValidation.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
$result = Join-Path $Project 'playspeed-results.txt'
if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
if ($process.ExitCode -ne 0) { throw "Unity validation failed. See $log" }
if (-not (Test-Path -LiteralPath $result)) { throw "No validation report. See $log" }
Write-Output "Report: $result"
