param(
    [string] $Project,
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Project) { $Project = Join-Path $package 'Temp~\VtsJellyValidation' }
if (-not (Test-Path -LiteralPath $Project)) {
    New-Item -ItemType Directory -Path $Project | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Project 'Assets'), (Join-Path $Project 'Packages'),
        (Join-Path $Project 'ProjectSettings') | Out-Null
    Set-Content -LiteralPath (Join-Path $Project '.ho-vts-jelly-validation') -Value 'Disposable HoVTS jelly validation'
    Set-Content -LiteralPath (Join-Path $Project 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 2021.3.45f2'
    Set-Content -LiteralPath (Join-Path $Project 'Packages\manifest.json') -Value '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}'
}
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not ((Test-Path -LiteralPath (Join-Path $Project '.ho-vts-jelly-validation')) -or
    (Test-Path -LiteralPath (Join-Path $Project '.ho-vts-ir-validation')))) {
    throw 'Disposable jelly/IR project marker missing; refusing to install validation scripts.'
}
$runtime = Join-Path $Project 'Assets\Runtime'
$editor = Join-Path $Project 'Assets\Editor'
New-Item -ItemType Directory -Force -Path $runtime, $editor | Out-Null
foreach ($name in @('HoVtsParameterReceiver.cs','HoVtsRuleEvaluator.cs','HoFaceMiddleware.cs',
    'HoFaceExpression.cs','HoFaceOutputTable.cs','HoFaceTrackingChannels.cs','HoFaceProfile.cs',
    'HoFaceProfileJson.cs','HoJson.cs','HoVtsIrController.cs','HoFaceOutputOwnership.cs',
    'HoFaceJelly.cs','HoVtsJellySpring.cs','HoVtsJellyEyeData.cs','HoVtsJellyEye.cs')) {
    Copy-Item -LiteralPath (Join-Path $package "Runtime\FaceTracking\$name") -Destination $runtime
}
Get-ChildItem -LiteralPath (Join-Path $package 'Runtime\FaceTracking\Core') -Filter '*.cs' |
    Copy-Item -Destination $runtime
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'JellyEyeValidation.cs') -Destination $editor
$inspector = Join-Path $package 'Editor\FaceTracking\HoVtsJellyEyeEditor.cs'
if (Test-Path -LiteralPath $inspector) { Copy-Item -LiteralPath $inspector -Destination $editor }
$log = Join-Path $Project 'jelly-eye.log'
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics',
    '-projectPath',('"' + $Project + '"'),'-executeMethod','HoVtsJellyEyeValidation.RunBatch',
    '-logFile',('"' + $log + '"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_VTS_JELLY|error CS|Exception:'
if ($process.ExitCode -ne 0) { throw "Unity jelly validation failed; see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'HO_VTS_JELLY PASS' -Quiet)) { throw 'Jelly PASS marker missing' }
