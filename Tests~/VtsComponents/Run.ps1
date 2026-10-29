param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-vts-validation'))) {
    throw 'Disposable project marker missing; refusing to install validation scripts.'
}
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runtime = Join-Path $Project 'Assets\VtsRuntime'
$editor = Join-Path $Project 'Assets\Editor'
New-Item -ItemType Directory -Force -Path $runtime, $editor | Out-Null
foreach ($name in @('HoVtsController.cs','HoVtsParameterReceiver.cs','HoVtsRuleEvaluator.cs',
    'HoFaceMiddleware.cs','HoFaceExpression.cs','HoFaceOutputTable.cs','HoFaceTrackingChannels.cs')) {
    Copy-Item -LiteralPath (Join-Path $packageRoot "Runtime\FaceTracking\$name") -Destination $runtime
}
Copy-Item -LiteralPath (Join-Path $packageRoot 'Editor\FaceTracking\HoVtsControllerEditor.cs') -Destination $editor
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ControllerValidation.cs') -Destination $editor
$log = Join-Path $Project 'vts-controller.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'HoVtsControllerValidation.RunBatch', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_VTS_CONTROLLER|error CS|Exception|Error:'
if ($process.ExitCode -ne 0) { throw "Unity VTS validation failed ($($process.ExitCode)); see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'HO_VTS_CONTROLLER PASS' -Quiet)) { throw 'PASS marker missing' }
