param(
    [Parameter(Mandatory=$true)][string]$Fixture,
    [string]$Unity='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [string]$Project='',
    [switch]$UseV4,
    [switch]$RegistryTestOverride,
    [string]$Python='C:\Users\hhh12\AppData\Local\Programs\Python\Python313\python.exe'
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
    foreach($taskSuffix in @('.fbx','_samples.json','_runtime_ir.json')){Copy-Item -LiteralPath (Join-Path $Fixture ($taskVariant+$taskSuffix)) -Destination "$Project\Assets\Fixture"}
    if($UseV4){Copy-Item -LiteralPath (Join-Path $Fixture ($taskVariant+'_v4.json')) -Destination "$Project\Assets\Fixture"}
}
if($RegistryTestOverride -and -not $UseV4){throw 'RegistryTestOverride requires UseV4.'}
if($UseV4){
    [IO.File]::WriteAllText("$Project\Assets\Fixture\use-v4.txt",'Real v4 TextAsset reader, registry, adapter and production import window path.')
    if($RegistryTestOverride){
        # Only the new disposable project is changed. The authority and this
        # repository's distributed production registry remain byte-for-byte intact.
        $taskOverride=@'
import hashlib,json,sys
from pathlib import Path
root=Path(sys.argv[1])
path=root/'Assets/Editor/HoFbxImport/ConstraintIR/Registry/registry.v1.json'
registry=json.loads(path.read_text(encoding='utf-8'))
for item in registry['implementations']:
    if item['id'] in ('hoaux.copy_rotation.local','hoaux.copy_rotation.owner_orient'):
        item['availability']='IMPLEMENTED'
        item['fidelity']='EXACT'
        item['verification']={'status':'UNTESTED','scope':'TEST_OVERRIDE_ONLY','evidence':[]}
raw=(json.dumps(registry,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
path.write_bytes(raw)
digest=hashlib.sha256(raw).hexdigest()
for variant in ('Raw','Mch'):
    source=root/'Assets/Fixture'/f'{variant}_v4.json'
    document=json.loads(source.read_text(encoding='utf-8'))
    document['registry']={'id':registry['id'],'version':registry['version'],'sha256':digest}
    # Existing generated MCH uses its deliberately explicit legacy policy.
    kinds={s['semanticId']:s['semanticType'] for s in document['semantics']}
    for binding in document['implementationBindings']:
        if kinds[binding['semanticId']]=='MCH':binding['fallbackPolicy']='ALLOW_LEGACY'
    payload={key:value for key,value in document.items() if key not in ('exportTime','inputFingerprint')}
    canonical=json.dumps(payload,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode('utf-8')
    document['inputFingerprint']=hashlib.sha256(canonical).hexdigest()
    source.write_text(json.dumps(document,ensure_ascii=False,indent=2),encoding='utf-8',newline='\n')
(root/'registry-test-override.txt').write_text('TEST ONLY: local contracts released in this isolated project registry. SHA256 '+digest,encoding='utf-8')
print('TEST_REGISTRY_OVERRIDE',digest)
'@
        & $Python -c $taskOverride $Project
        if($LASTEXITCODE -ne 0){throw 'Test-only registry/fixture preparation failed.'}
    }
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
