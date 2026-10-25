param(
    [string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project=''
)
$ErrorActionPreference='Stop'
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(-not $Project){$Project=Join-Path $taskRepo ('.research\TransformConstraints-'+[Guid]::NewGuid().ToString('N'))}
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated validation project.'}
New-Item -ItemType Directory -Force -Path "$Project\Assets\Editor","$Project\Packages","$Project\ProjectSettings"|Out-Null
Copy-Item -LiteralPath "$taskRepo\Runtime\TransformConstraints" -Destination "$Project\Assets" -Recurse
Copy-Item -LiteralPath "$taskRepo\Editor\TransformConstraints" -Destination "$Project\Assets\Editor" -Recurse
Copy-Item -LiteralPath "$PSScriptRoot\ConstraintChecks.cs","$PSScriptRoot\ConstraintPlayProbe.cs" -Destination "$Project\Assets"
Copy-Item -LiteralPath "$PSScriptRoot\ConstraintValidation.cs" -Destination "$Project\Assets\Editor"
[IO.File]::WriteAllText("$Project\Packages\manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$taskLog=$Project+'.log'
$taskArgs=@('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','ConstraintValidation.Run','-logFile',('"'+$taskLog+'"'))
$taskProcess=Start-Process -FilePath $Unity -ArgumentList $taskArgs -PassThru -Wait -WindowStyle Hidden
if($taskProcess.ExitCode -ne 0){throw "Unity validation failed. See $taskLog"}
$taskResult=Join-Path $Project 'results.json'
if(-not(Test-Path -LiteralPath $taskResult)){throw "No result. See $taskLog"}
$taskReport=Get-Content -LiteralPath $taskResult -Raw|ConvertFrom-Json
if($taskReport.failures.Count -gt 0){throw ($taskReport.failures -join "`n")}
if($taskReport.passed.Count -eq 0){throw 'No assertions were executed.'}
Write-Output "PASS $($taskReport.passed.Count) assertions. Report: $taskResult. Log: $taskLog"
