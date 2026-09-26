# Requires an existing disposable Unity project with HoUnityTools installed.
param(
    [Parameter(Mandatory=$true)][string] $Project,
    [Parameter(Mandatory=$true)][string] $Profile,
    [string] $ModCore = 'D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests\Mods-Ho\HoFaceTracking\Core',
    [string] $Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
$Profile = (Resolve-Path -LiteralPath $Profile).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-face-validation'))) {
    throw 'Disposable project marker missing; refusing to install test scripts.'
}
& (Join-Path $PSScriptRoot '..\SyncFaceModCore.ps1') -ModCore $ModCore -Check
$target = Join-Path $Project 'Assets\Editor\FaceRuleParity'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FaceRuleParity.cs') -Destination $target
foreach ($name in @('HoFaceChain.cs', 'HoFaceExpression.cs', 'HoFaceMiddleware.cs',
    'HoFaceOutputTable.cs', 'HoFaceProfile.cs', 'HoFaceProfileJson.cs', 'HoFaceTrackingChannels.cs', 'HoJson.cs')) {
    Copy-Item -LiteralPath (Join-Path $ModCore $name) -Destination $target
}
$log = Join-Path $Project 'face-rule-parity.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'FaceRuleParity.RunBatch', '-faceProfile', ('"' + $Profile + '"'),
    '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath $log | Select-String 'FACE_PARITY|error CS|Exception|Error:'
if ($process.ExitCode -ne 0) { throw "Unity parity validation failed ($($process.ExitCode)); see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'FACE_PARITY PASS' -Quiet)) { throw 'PASS marker missing' }
