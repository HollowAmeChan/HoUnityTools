param(
    [string]$UnityPath = 'D:/Unity/Unity 2021.3.45f2/Editor/Unity.exe',
    [string]$CharacterFolder = '',
    [string]$ProjectPath = '',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$CharacterFolder) { $CharacterFolder = Join-Path $repo 'Temp~/YuqiuBsParity' }
$project = if ($ProjectPath) { [IO.Path]::GetFullPath($ProjectPath) } else { Join-Path $repo ('Temp~/YuqiuBsParityUnity-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $project) { throw 'Use a new isolated project for v4 character parity.' }
$assets = Join-Path $project 'Assets'
$editor = Join-Path $assets 'Editor'
$runtime = Join-Path $assets 'Runtime'
$character = Join-Path $assets 'Character'
New-Item -ItemType Directory -Force -Path $editor,$runtime,$character,(Join-Path $project 'ProjectSettings'),(Join-Path $project 'Packages') | Out-Null
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2021.3.45f2' -Encoding utf8
Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Value '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}' -Encoding utf8
$runtimeFiles = @(
    'Runtime/Constraints/HoBsCorrective.cs','Runtime/Constraints/HoBsCorrective.UpdateLoop.cs',
    'Runtime/FaceTracking/HoFaceOutputOwnership.cs','Runtime/HoEditorOnly.cs',
    'Runtime/CombineArmature/IHoArmatureMergeParticipant.cs',
    'Tests~/HoBsCorrective/HoBsCharacterParityPlayer.cs'
)
$runtimeFiles += Get-ChildItem -LiteralPath (Join-Path $repo 'Runtime/RigConstraints/HoAuxRig'),(Join-Path $repo 'Runtime/RigConstraints/ConstraintImport'),(Join-Path $repo 'Runtime/RigConstraints/ConstraintIR') -Filter '*.cs' -File | ForEach-Object { $_.FullName }
foreach ($file in $runtimeFiles) {
    $source = if ([IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $repo $file }
    Copy-Item -LiteralPath $source -Destination $runtime -Force
}
foreach ($file in @(
    'Editor/HoFbxImport/HoFbxBsCorrectiveImportUtility.cs',
    'Editor/HoFbxImport/HoFbxHumanoidImportUtility.cs',
    'Editor/HoFbxImport/HoFbxImportProcessingWindow.cs',
    'Editor/HoFbxImport/HoFbxImportProcessingWindow.BsCorrective.cs',
    'Editor/HoFbxImport/HoFbxConstraintImportUtility.cs',
    'Editor/HoFbxImport/HoDirectConstraintImporter.cs',
    'Editor/HoFbxImport/HoDirectConstraintImporter.Apply.cs',
    'Editor/HoFbxImport/HoVrcConstraintBridge.cs','Editor/Utils/HoEditorStyles.cs',
    'Tests~/HoBsCorrective/HoBsCharacterParityChecks.cs'
)) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $editor -Force }
Copy-Item -LiteralPath (Join-Path $repo 'Editor/HoFbxImport/ConstraintIR') -Destination $editor -Recurse -Force
Set-Content -LiteralPath (Join-Path $editor 'BoneRendererHost.cs') -Encoding utf8 -Value @'
namespace Hollow.HoUnityTools.BoneRendering
{
    internal sealed class HoBoneRenderer : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Transform SkeletonRoot { get; set; }
        public UnityEngine.TextAsset GroupJson { get; set; }
        public void RefreshGroupJson() { }
    }
}
'@
if (Test-Path -LiteralPath (Join-Path $CharacterFolder 'Yuqiu.fbx')) {
    Copy-Item -LiteralPath (Join-Path $CharacterFolder 'Yuqiu.fbx') -Destination $character -Force
    Copy-Item -LiteralPath (Join-Path $CharacterFolder 'HoFBX') -Destination $character -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $CharacterFolder 'blender_samples.json') -Destination $character -Force
    $samples = Get-Content -LiteralPath (Join-Path $CharacterFolder 'blender_samples.json') -Raw -Encoding utf8 | ConvertFrom-Json
    Copy-Item -LiteralPath $samples.clip -Destination (Join-Path $character 'Preview.anim') -Force
    foreach ($frame in $samples.frames) {
        $frame.rotations = @($frame.rotations | ForEach-Object { [ordered]@{ x=$_[0]; y=$_[1]; z=$_[2]; w=$_[3] } })
    }
    $samples | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $character 'blender_samples.json') -Encoding utf8
}
if ($PrepareOnly) { Write-Output $project; return }
$result = Join-Path $project 'character_results.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$log = Join-Path $repo 'Temp~/YuqiuBsParity.Unity.log'
$arguments = @('-batchmode','-nographics','-projectPath',('"'+$project+'"'),'-executeMethod','HoBsCharacterParityChecks.Run','-logFile',('"'+$log+'"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $result)) { throw "Character parity failed; inspect $log" }
$summary = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
$summary | Select-Object unity,clip,status,avatarValid,avatarHuman,clipHuman,tPoseApplied,tPoseMappedBones,tPoseSolverVerified,tPoseErrorBefore,tPoseErrorAfter,tPoseChangedBones,frames,outputs,importedConstraints,nonemptyOutputs,replayMaxWeightError,animationMaxWeightError,animationMeanWeightError,animationMaxBoneAngle,sourceAnimationMaxBoneAngle,mchSourceMaxAngle
foreach ($issue in $summary.issues) { Write-Output $issue }
