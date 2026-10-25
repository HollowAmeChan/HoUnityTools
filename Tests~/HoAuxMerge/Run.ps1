param(
    [string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [Parameter(Mandatory=$true)][string]$Project
)
$ErrorActionPreference='Stop'
$taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$Project=[IO.Path]::GetFullPath($Project)
if(Test-Path -LiteralPath $Project){throw 'Use a new isolated project.'}
New-Item -ItemType Directory -Force -Path "$Project\Assets\Editor","$Project\Packages","$Project\ProjectSettings"|Out-Null
Copy-Item -LiteralPath "$taskRepo\Runtime\CombineArmature" -Destination "$Project\Assets\CombineRuntime" -Recurse
Copy-Item -LiteralPath "$taskRepo\Editor\CombineArmature" -Destination "$Project\Assets\Editor\Combine" -Recurse
New-Item -ItemType Directory -Force -Path "$Project\Assets\HoAux"|Out-Null
Copy-Item -Path "$taskRepo\Runtime\RigConstraints\HoAuxRig\*.cs" -Destination "$Project\Assets\HoAux"
Copy-Item -LiteralPath "$PSScriptRoot\HoAuxMergeProbe.cs" -Destination "$Project\Assets\HoAux"
Copy-Item -LiteralPath "$PSScriptRoot\HoAuxMergeValidation.cs" -Destination "$Project\Assets\Editor"
[IO.File]::WriteAllText("$Project\Packages\manifest.json",'{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}')
[IO.File]::WriteAllText("$Project\ProjectSettings\ProjectVersion.txt","m_EditorVersion: 6000.3.15f1`n")
$taskProcess=Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics','-projectPath',('"'+$Project+'"'),'-executeMethod','HoAuxMergeValidation.Run','-logFile',('"'+$Project+'.log"')) -WindowStyle Hidden -PassThru -Wait
if(Test-Path -LiteralPath "$Project\merge-results.json"){Get-Content -LiteralPath "$Project\merge-results.json" -Raw}
if($taskProcess.ExitCode -ne 0){throw "Merge validation failed. See $Project.log"}
