param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-vts-ir-validation'))) {
    throw 'Disposable IR project marker missing.'
}
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runtime = Join-Path $Project 'Assets\Runtime'
$editor = Join-Path $Project 'Assets\Editor'
New-Item -ItemType Directory -Force -Path $runtime, $editor | Out-Null
$obsoleteEditorAdapter = Join-Path $editor 'HoFaceStudioRawInput.cs'
foreach ($obsoletePath in @($obsoleteEditorAdapter, ($obsoleteEditorAdapter + '.meta'))) {
    if (Test-Path -LiteralPath $obsoletePath) {
        Remove-Item -LiteralPath $obsoletePath -Force
    }
}
foreach ($name in @('HoVtsController.cs','HoVtsLookAt.cs','HoVtsParameterReceiver.cs','HoVtsRuleEvaluator.cs','HoFaceMiddleware.cs',
    'HoFaceExpression.cs','HoFaceOutputTable.cs','HoFaceTrackingChannels.cs','HoFaceProfile.cs',
    'HoFaceProfileJson.cs','HoJson.cs','HoVtsIrController.cs','HoFaceStudioRawInput.cs')) {
    Copy-Item -LiteralPath (Join-Path $package "Runtime\FaceTracking\$name") -Destination $runtime
}
Get-ChildItem -LiteralPath (Join-Path $package 'Runtime\FaceTracking\Core') -Filter '*.cs' |
    Copy-Item -Destination $runtime
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'IrComponentValidation.cs') -Destination $editor
Copy-Item -LiteralPath (Join-Path $package 'Editor\FaceTracking\HoVtsCharacterSetup.cs') -Destination $editor
$log = Join-Path $Project 'ir-component.log'
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics',
    '-projectPath',('"' + $Project + '"'),'-executeMethod','HoVtsIrComponentValidation.RunBatch',
    '-logFile',('"' + $log + '"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_VTS_IR_COMPONENT|error CS|Exception:'
if ($process.ExitCode -ne 0) { throw "Unity IR component validation failed; see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'HO_VTS_IR_COMPONENT PASS' -Quiet)) { throw 'IR component PASS marker missing' }
