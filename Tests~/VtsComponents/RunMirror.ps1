param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe',
    [string] $ModCore = 'D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests\Mods-Ho\HoFaceTracking\Core',
    [string] $CharacterProfile = 'D:\Unity_Project\BreakWarudo\Assets\Hollow\WTD\FT\ho-iPhoneVTS.arkit.hoface.json'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-vts-mirror-validation'))) {
    throw 'Disposable mirror project marker missing; refusing to copy validation sources.'
}
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runtime = Join-Path $Project 'Assets\Runtime'
$mod = Join-Path $Project 'Assets\Mod'
$editor = Join-Path $Project 'Assets\Editor'
$profiles = Join-Path $Project 'Assets\Profiles'
New-Item -ItemType Directory -Force -Path $runtime, $mod, $editor, $profiles | Out-Null
foreach ($name in @('HoVtsLookAt','HoVtsParameterReceiver','HoVtsRuleEvaluator','HoFaceMiddleware',
    'HoFaceExpression','HoFaceOutputTable','HoFaceTrackingChannels','HoFaceProfile','HoFaceProfileJson','HoJson','HoVtsPacket')) {
    Copy-Item -LiteralPath (Join-Path $package "Runtime\FaceTracking\$name.cs") -Destination $runtime
}
foreach ($name in @('HoFaceInputState','HoFaceVtsRamp','HoVtsIphoneReceiver','HoVtsApiServer','HoVtsApiPacket','HoVtsPacket','HoJson')) {
    Copy-Item -LiteralPath (Join-Path $ModCore "$name.cs") -Destination $mod
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MirrorValidation.cs') -Destination $editor
Copy-Item -LiteralPath (Join-Path $package 'Editor\FaceTracking\Profiles\ho-iPhoneVTS.arkit.hoface.json') -Destination (Join-Path $profiles 'Template.json')
Copy-Item -LiteralPath $CharacterProfile -Destination (Join-Path $profiles 'Character.json')

# Compile the three actual editor calibration methods in isolation. The rest of
# InputHub needs the entire editor UI/network environment, unrelated to pose math.
# Do not replace these methods with test-owned copies of the same algorithm.
$hub = [IO.File]::ReadAllText((Join-Path $package 'Editor\FaceTracking\HoFaceInputHub.cs'))
$methods = foreach ($name in @('ApplyPoseCalibration','NormalizeAngle','PoseRotation')) {
    $match = [regex]::Match($hub, "(?ms)^        private static [^\r\n]+ $name\([^\r\n]*\)\r?\n        \{.*?^        \}")
    if (-not $match.Success) { throw "Editor calibration method not found: $name" }
    $match.Value
}
$probe = @'
using System.Collections.Generic;
using UnityEngine;
public static class HoEditorCalibrationProbe {
    private static readonly Dictionary<string,float> CalibrationOffsets = new Dictionary<string,float>();
    public static void SetOffsets(Dictionary<string,float> values) {
        CalibrationOffsets.Clear();
        foreach(var pair in values) CalibrationOffsets[pair.Key] = pair.Value;
    }
    public static void Apply(Dictionary<string,float> values) { ApplyPoseCalibration(values); }
'@
[IO.File]::WriteAllText((Join-Path $editor 'HoEditorCalibrationProbe.cs'), ($probe + "`n" + ($methods -join "`n") + "`n}"))
$log = Join-Path $Project 'vts-mirror.log'
$arguments = @('-batchmode','-nographics','-projectPath',('"' + $Project + '"'),
    '-executeMethod','HoVtsMirrorValidation.RunBatch','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_VTS_MIRROR|error CS|Exception|Error:'
if ($process.ExitCode -ne 0) { throw "Unity mirror validation failed ($($process.ExitCode)); see $log" }
if (-not (Select-String -LiteralPath $log -Pattern 'HO_VTS_MIRROR PASS' -Quiet)) { throw 'Mirror PASS marker missing' }
