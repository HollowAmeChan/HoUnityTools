param([Parameter(Mandatory=$true)][string]$Fixture,[string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',[string]$Project='')
$ErrorActionPreference='Stop'
$directRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Project){$Project=Join-Path $directRepo ('.research/DirectV4-'+[Guid]::NewGuid().ToString('N'))}
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated validation project.'}
New-Item -ItemType Directory -Force -Path "$Project/Assets/Runtime","$Project/Assets/Editor","$Project/Assets/Fixture","$Project/Packages","$Project/ProjectSettings"|Out-Null
foreach($directPart in @('RigConstraints','BoneRenderer','CombineArmature')){Copy-Item -LiteralPath "$directRepo/Runtime/$directPart" -Destination "$Project/Assets/Runtime" -Recurse}
Copy-Item -LiteralPath "$directRepo/Runtime/HoEditorOnly.cs","$directRepo/Runtime/Constraints/HoBsCorrective.cs","$directRepo/Runtime/Constraints/HoBsCorrective.UpdateLoop.cs","$directRepo/Runtime/FaceTracking/HoFaceOutputOwnership.cs" -Destination "$Project/Assets/Runtime"
foreach($directPart in @('HoFbxImport','HoAuxRig','CombineArmature')){Copy-Item -LiteralPath "$directRepo/Editor/$directPart" -Destination "$Project/Assets/Editor" -Recurse}
Copy-Item -LiteralPath "$directRepo/Editor/Utils/HoEditorStyles.cs","$PSScriptRoot/DirectValidation.cs" -Destination "$Project/Assets/Editor"
Copy-Item -LiteralPath "$PSScriptRoot/DirectRuntimeProbe.cs" -Destination "$Project/Assets"
foreach($directFile in @('Direct.fbx','Direct_samples.json','Direct_v4.json')){Copy-Item -LiteralPath (Join-Path $Fixture $directFile) -Destination "$Project/Assets/Fixture"}
[IO.File]::WriteAllText("$Project/Packages/manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project/ProjectSettings/ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$directLog=$Project+'.log'
$directStarted=Get-Date
$directProcess=Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','DirectValidation.Run','-logFile',('"'+$directLog+'"')) -PassThru -WindowStyle Hidden
while(-not $directProcess.WaitForExit(1000)){
    if((Get-Date)-$directStarted -gt [TimeSpan]::FromMinutes(10)){throw "Direct numerical validation exceeded ten minutes: $directLog"}
}
if($directProcess.ExitCode -ne 0){throw "Direct numerical validation failed: $directLog"}
$directReport=Get-Content -LiteralPath "$Project/direct-results.json" -Raw|ConvertFrom-Json
if($directReport.failures.Count -gt 0 -or $directReport.measurements.Count -eq 0){throw ($directReport.failures -join "`n")}
Write-Output "PASS $($directReport.measurements.Count) direct measurements. Project=$Project"
