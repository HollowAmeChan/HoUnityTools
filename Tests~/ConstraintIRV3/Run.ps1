param(
    [Parameter(Mandatory=$true)][string]$Fixture,
    [string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project='',
    [switch]$TestVrcApiShim,
    [string]$FullConfig='',
    [string]$FullModel=''
)
$ErrorActionPreference='Stop'
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(-not $Project){$Project=Join-Path $taskRepo ('.research\IRv3-'+[Guid]::NewGuid().ToString('N'))}
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated validation project.'}
New-Item -ItemType Directory -Force -Path "$Project\Assets\Runtime","$Project\Assets\Editor","$Project\Assets\Fixture","$Project\Packages","$Project\ProjectSettings"|Out-Null
Copy-Item -LiteralPath "$taskRepo\Runtime\RigConstraints","$taskRepo\Runtime\BoneRenderer" -Destination "$Project\Assets\Runtime" -Recurse
Copy-Item -LiteralPath "$taskRepo\Runtime\HoEditorOnly.cs" -Destination "$Project\Assets\Runtime"
Copy-Item -LiteralPath "$taskRepo\Editor\HoFbxImport","$taskRepo\Editor\HoAuxRig" -Destination "$Project\Assets\Editor" -Recurse
Copy-Item -LiteralPath "$taskRepo\Editor\Utils\HoEditorStyles.cs","$PSScriptRoot\V3Validation.cs" -Destination "$Project\Assets\Editor"
Copy-Item -LiteralPath "$PSScriptRoot\V3RuntimeProbe.cs" -Destination "$Project\Assets"
foreach($taskName in @('ChestAddon.fbx','samples.json','original_constraint_ir.json')){Copy-Item -LiteralPath (Join-Path $Fixture $taskName) -Destination "$Project\Assets\Fixture"}
if($FullConfig -or $FullModel){
    if(-not $FullConfig -or -not $FullModel){throw 'FullConfig and FullModel must be provided together.'}
    Copy-Item -LiteralPath $FullConfig -Destination "$Project\Assets\Fixture\full_ir.json"
    Copy-Item -LiteralPath $FullModel -Destination "$Project\Assets\Fixture\FullModel.fbx"
}
if($TestVrcApiShim){
    New-Item -ItemType Directory -Force -Path "$Project\Assets\VrcApiShim"|Out-Null
    Copy-Item -LiteralPath "$PSScriptRoot\VrcApiShim.cs" -Destination "$Project\Assets\VrcApiShim"
    foreach($taskType in @('Position','Rotation','Scale','Parent','Aim')){
        [IO.File]::WriteAllText("$Project\Assets\VrcApiShim\VRC${taskType}Constraint.cs","namespace VRC.SDK3.Dynamics.Constraint.Components { public class VRC${taskType}Constraint : VRCConstraint {} }")
    }
}
[IO.File]::WriteAllText("$Project\Packages\manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$taskLog=Join-Path (Split-Path -Parent $Project) ((Split-Path -Leaf $Project)+'.log')
$taskArgs=@('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','V3Validation.Run','-logFile',('"'+$taskLog+'"'))
$taskStarted=Get-Date
$taskProcess=Start-Process -FilePath $Unity -ArgumentList $taskArgs -PassThru -Wait -WindowStyle Hidden
if($taskProcess.ExitCode -ne 0){throw "Unity validation failed. See $taskLog"}
$taskResult=Join-Path (Split-Path -Parent $Project) 'v3-runtime-results.json'
if(-not(Test-Path -LiteralPath $taskResult)){throw "No result. See $taskLog"}
if((Get-Item -LiteralPath $taskResult).LastWriteTime -lt $taskStarted){throw 'Result was not refreshed.'}
$taskReport=Get-Content -LiteralPath $taskResult -Raw|ConvertFrom-Json
if($taskReport.failures.Count -gt 0){throw ($taskReport.failures -join "`n")}
if($taskReport.measurements.Count -eq 0){throw 'Validation produced no measurements.'}
Write-Output "PASS $($taskReport.measurements.Count) measurements. Report: $taskResult. Log: $taskLog"
