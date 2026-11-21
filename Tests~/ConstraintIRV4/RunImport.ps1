param([Parameter(Mandatory=$true)][string]$Fixture,[string]$OperatorFixture='',[string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',[string]$Project='')
$ErrorActionPreference='Stop'
$importRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Project){$Project=Join-Path $importRepo ('.research/IRv4Import-'+[Guid]::NewGuid().ToString('N'))}
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated project.'}
New-Item -ItemType Directory -Force -Path "$Project/Assets/Runtime","$Project/Assets/Editor","$Project/Assets/Fixture","$Project/Packages","$Project/ProjectSettings"|Out-Null
foreach($importPart in @('RigConstraints','BoneRenderer','CombineArmature')){Copy-Item -LiteralPath "$importRepo/Runtime/$importPart" -Destination "$Project/Assets/Runtime" -Recurse}
Copy-Item -LiteralPath "$importRepo/Runtime/HoEditorOnly.cs","$importRepo/Runtime/Constraints/HoBsCorrective.cs","$importRepo/Runtime/Constraints/HoBsCorrective.UpdateLoop.cs","$importRepo/Runtime/FaceTracking/HoFaceOutputOwnership.cs" -Destination "$Project/Assets/Runtime"
foreach($importPart in @('HoFbxImport','HoAuxRig','CombineArmature')){Copy-Item -LiteralPath "$importRepo/Editor/$importPart" -Destination "$Project/Assets/Editor" -Recurse}
Copy-Item -LiteralPath "$importRepo/Editor/Utils/HoEditorStyles.cs","$PSScriptRoot/UnityImportValidation.cs" -Destination "$Project/Assets/Editor"
Copy-Item -LiteralPath $Fixture -Destination "$Project/Assets/Fixture/toy.v4.json"
if($OperatorFixture){Copy-Item -LiteralPath $OperatorFixture -Destination "$Project/Assets/Fixture/Operator" -Recurse}
[IO.File]::WriteAllText("$Project/Packages/manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project/ProjectSettings/ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$importLog=$Project+'.log'
$importArgs=@('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','UnityImportValidation.Run','-logFile',('"'+$importLog+'"'))
$importProcess=Start-Process -FilePath $Unity -ArgumentList $importArgs -PassThru -WindowStyle Hidden
Write-Output "Started Unity import validation. PID=$($importProcess.Id); Project=$Project; Log=$importLog"
while(-not $importProcess.WaitForExit(1000)){}
if($importProcess.ExitCode -ne 0){throw "Unity import validation failed. See $importLog"}
$importResult=Join-Path $Project 'v4-import-results.json'
if(-not(Test-Path -LiteralPath $importResult)){throw "Missing result. See $importLog"}
$importReport=Get-Content -LiteralPath $importResult -Raw|ConvertFrom-Json
if($importReport.failures.Count -gt 0){throw ($importReport.failures -join "`n")}
Write-Output "PASS $($importReport.checks) Unity v4 import checks. Project=$Project"
