param(
    [string]$Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe',
    [string]$Project = ''
)
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $Project) {
    $Project = Join-Path $taskRepo ('Temp~\BirpSceneMSAA-' + [Guid]::NewGuid().ToString('N'))
}
$Project = [IO.Path]::GetFullPath($Project)
if (Test-Path -LiteralPath $Project) { throw "Use a new isolated project: $Project" }
New-Item -ItemType Directory -Force -Path "$Project\Assets\Editor", "$Project\Packages", "$Project\ProjectSettings" | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SceneMSAAValidation.cs') -Destination "$Project\Assets\Editor"

# Isolate the toggle preference from the user's open editors.
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRepo 'Editor\BirpSceneMSAA\HoBirpSceneMSAA.cs'))
$taskSource = $taskSource.Replace('"com.hollow.hounitytools.birp-scene-msaa"',
    ('"com.hollow.hounitytools.birp-scene-msaa.validation.' + [Guid]::NewGuid().ToString('N') + '"'))
[IO.File]::WriteAllText("$Project\Assets\Editor\HoBirpSceneMSAA.cs", $taskSource)
[IO.File]::WriteAllText("$Project\Packages\manifest.json", '{"dependencies":{"com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
$taskEditorVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($Unity).ProductVersion
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt", "m_EditorVersion: $taskEditorVersion`n")
$taskLog = $Project + '.log'
$taskArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'SceneMSAAValidation.Run', '-logFile', ('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $Unity -ArgumentList $taskArguments -PassThru -Wait -WindowStyle Hidden
$taskReport = Join-Path $Project 'scene-msaa-results.txt'
if (Test-Path -LiteralPath $taskReport) { Get-Content -LiteralPath $taskReport }
if ($taskProcess.ExitCode -ne 0) { throw "Unity validation failed: $taskLog" }
if (-not (Test-Path -LiteralPath $taskReport)) { throw "Unity did not produce a report: $taskLog" }
Write-Output "Report: $taskReport"
