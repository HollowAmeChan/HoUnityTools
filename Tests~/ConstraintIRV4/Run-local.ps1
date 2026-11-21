param(
    [Parameter(Mandatory=$true)][string]$Fixture,
    [string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project=''
)
$ErrorActionPreference='Stop'
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(-not $Project){$Project=Join-Path $taskRepo ('.research\CopyRLocal-'+[Guid]::NewGuid().ToString('N'))}
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated validation project.'}
New-Item -ItemType Directory -Force -Path "$Project\Assets\Runtime","$Project\Assets\Editor","$Project\Assets\Fixture","$Project\Packages","$Project\ProjectSettings"|Out-Null
foreach($taskPart in @('RigConstraints','BoneRenderer','CombineArmature')){Copy-Item -LiteralPath "$taskRepo\Runtime\$taskPart" -Destination "$Project\Assets\Runtime" -Recurse}
Copy-Item -LiteralPath "$taskRepo\Runtime\HoEditorOnly.cs" -Destination "$Project\Assets\Runtime"
Copy-Item -LiteralPath "$taskRepo\Runtime\Constraints\HoBsCorrective.cs" -Destination "$Project\Assets\Runtime"
Copy-Item -LiteralPath "$taskRepo\Runtime\Constraints\HoBsCorrective.UpdateLoop.cs","$taskRepo\Runtime\FaceTracking\HoFaceOutputOwnership.cs" -Destination "$Project\Assets\Runtime"
foreach($taskPart in @('HoFbxImport','HoAuxRig','CombineArmature')){Copy-Item -LiteralPath "$taskRepo\Editor\$taskPart" -Destination "$Project\Assets\Editor" -Recurse}
Copy-Item -LiteralPath "$taskRepo\Editor\Utils\HoEditorStyles.cs","$PSScriptRoot\LocalRotationValidation.cs" -Destination "$Project\Assets\Editor"
Copy-Item -LiteralPath "$PSScriptRoot\LocalRotationProbe.cs" -Destination "$Project\Assets"
foreach($taskVariant in @('Raw','Mch')){
    foreach($taskSuffix in @('.fbx','_samples.json')){Copy-Item -LiteralPath (Join-Path $Fixture ($taskVariant+$taskSuffix)) -Destination "$Project\Assets\Fixture"}
    Copy-Item -LiteralPath (Join-Path $Fixture ($taskVariant+'_v4.json')) -Destination "$Project\Assets\Fixture"
}
[IO.File]::WriteAllText("$Project\Packages\manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$taskLog=Join-Path (Split-Path -Parent $Project) ((Split-Path -Leaf $Project)+'.log')
$taskArgs=@('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','LocalRotationValidation.Run','-logFile',('"'+$taskLog+'"'))
$taskStarted=Get-Date
$taskProcess=Start-Process -FilePath $Unity -ArgumentList $taskArgs -PassThru -WindowStyle Hidden
while(-not $taskProcess.WaitForExit(1000)){
    if((Get-Date)-$taskStarted -gt [TimeSpan]::FromMinutes(10)){throw "Unity local validation exceeded ten minutes. Process=$($taskProcess.Id); log=$taskLog"}
}
if($taskProcess.ExitCode -ne 0){throw "Unity validation failed. See $taskLog"}
$taskResult=Join-Path $Project 'local-rotation-results.json'
if(-not(Test-Path -LiteralPath $taskResult)){throw "No result. See $taskLog"}
if((Get-Item -LiteralPath $taskResult).LastWriteTime -lt $taskStarted){throw 'Result was not refreshed.'}
$taskReport=Get-Content -LiteralPath $taskResult -Raw|ConvertFrom-Json
if($taskReport.failures.Count -gt 0){throw ($taskReport.failures -join "`n")}
if($taskReport.measurements.Count -eq 0){throw 'Validation produced no measurements.'}
Write-Output "PASS $($taskReport.measurements.Count) measurements. Report: $taskResult. Log: $taskLog"
