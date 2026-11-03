param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-vts-mirror-validation'))) {
    throw 'Disposable mirror project marker missing.'
}
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $PSScriptRoot 'RunMirror.ps1') -Project $Project -Unity $Unity
$projectFile = Join-Path $package 'Tools~\HoFaceStudio\src\HoFace.Core\HoFace.Core.csproj'
& dotnet build $projectFile -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Portable core build failed' }
Copy-Item -LiteralPath (Join-Path $package 'Tools~\HoFaceStudio\src\HoFace.Core\bin\Release\netstandard2.0\HoFace.Core.dll') -Destination $Project
$runtime = Join-Path $Project 'Assets\RuntimeCore'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Get-ChildItem -LiteralPath (Join-Path $package 'Runtime\FaceTracking\Core') -Filter '*.cs' |
    Copy-Item -Destination $runtime
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ValueParityValidation.cs') -Destination (Join-Path $Project 'Assets\Editor')
$log = Join-Path $Project 'value-parity.log'
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics',
    '-projectPath',('"' + $Project + '"'),'-executeMethod','HoFaceValueParityValidation.RunBatch',
    '-logFile',('"' + $log + '"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_FACE_VALUE_PARITY|error CS|Exception:'
if ($process.ExitCode -ne 0) { throw "Unity value parity failed; see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'HO_FACE_VALUE_PARITY PASS' -Quiet)) { throw 'Parity PASS marker missing' }
