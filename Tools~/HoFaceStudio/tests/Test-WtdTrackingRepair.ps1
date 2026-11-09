# Exercise the migrated pair using the same Value and IR evaluators as Studio.
param(
    [Parameter(Mandatory)][string]$BeforeValue,
    [Parameter(Mandatory)][string]$BeforeIr,
    [Parameter(Mandatory)][string]$Value,
    [Parameter(Mandatory)][string]$Ir,
    [string]$CoreDll = (Join-Path $PSScriptRoot '../src/HoFace.Core/bin/Release/netstandard2.0/HoFace.Core.dll'),
    [string]$RestMonitor,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Resolve-Path -LiteralPath $CoreDll).Path

$script:checks = 0
function Check([bool]$Pass, [string]$Message) {
    $script:checks++
    if (!$Pass) { throw $Message }
}
function Near([double]$Actual, [double]$Expected, [string]$Message) {
    Check ([Math]::Abs($Actual - $Expected) -lt 0.0001) "${Message}: $Actual != $Expected"
}
function Read-Value([string]$Path) {
    $profile = $null; $reason = $null
    $ok = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueDocument]::TryParse(
        [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path), [ref]$profile, [ref]$reason)
    Check $ok "Value parse failed: $reason"
    $probe = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueSession]::new($profile)
    Check ([string]::IsNullOrEmpty($probe.Evaluator.Error)) "Value expression error: $($probe.Evaluator.Error)"
    $profile
}
function Read-Ir([string]$Path) {
    $document = $null; $reason = $null
    $ok = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceIrDocument]::TryParse(
        [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path), [ref]$document, [ref]$reason)
    Check $ok "IR parse failed: $reason"
    $document
}
function Raw([hashtable]$Channels) {
    $d = [Collections.Generic.Dictionary[string,single]]::new([StringComparer]::Ordinal)
    foreach ($key in $Channels.Keys) { $d.Add($key, [single]$Channels[$key]) }
    return ,$d
}
function Sample($Profile, $Graph, [hashtable]$Channels) {
    $session = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueSession]::new($Profile)
    $session.Evaluate((Raw $Channels), 0)
    $Graph.Evaluate($session.Values)
    @{ values = $session.Values; weights = [Collections.Generic.Dictionary[string,single]]::new($Graph.Values) }
}
function EyelidSum($Weights, [string]$Side, [string]$Label) {
    $sum = 0.0
    foreach ($pair in $Weights.GetEnumerator()) {
        if ($pair.Key.StartsWith('HO-眼情感-') -and $pair.Key.EndsWith($Label + $Side)) { $sum += $pair.Value }
    }
    $sum
}

$oldProfile = Read-Value $BeforeValue
$profile = Read-Value $Value
$oldDocument = Read-Ir $BeforeIr
$document = Read-Ir $Ir
$oldGraph = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceIrEvaluator]::new($oldDocument)
$graph = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceIrEvaluator]::new($document)
$oldValues = Get-Content -Raw -LiteralPath $BeforeValue | ConvertFrom-Json -AsHashtable -Depth 100
$newValues = Get-Content -Raw -LiteralPath $Value | ConvertFrom-Json -AsHashtable -Depth 100
$oldIrJson = Get-Content -Raw -LiteralPath $BeforeIr | ConvertFrom-Json -AsHashtable -Depth 100
$newIrJson = Get-Content -Raw -LiteralPath $Ir | ConvertFrom-Json -AsHashtable -Depth 100
$retiredParams = '^(Pupil/|Iris/|Highlight[12]/|Gate/EyeDetails$)'
$retiredShapes = '^HO-(瞳孔|眼仁|高光)'
$eyeOrWidth = '^(HO-眼情感-|HO-嘴宽-)'
$changed = @('Mouth/Width', 'Eye/Left/Open', 'Eye/Left/Emotion', 'Eye/Left/Anger',
    'Eye/Right/Open', 'Eye/Right/Emotion', 'Eye/Right/Anger')
Check (@($newValues.inputs | Where-Object { $_.parameter -match '^(EyeDetailsEnabled$|HoPupil|HoIris|HoHighlight)' }).Count -eq 0) 'Eye detail inputs remain.'
Check (@($newValues.outputs | Where-Object { $_.parameter -match $retiredParams -or $_.parameter -in @('eyeBlinkLeft','eyeBlinkRight') }).Count -eq 0) 'Retired effect axes or public blink remain.'
Check (@($newIrJson.parameters | Where-Object { $_.name -match $retiredParams }).Count -eq 0) 'Retired IR parameters remain.'
Check (@($newIrJson.targets | Where-Object { $_.id -match $retiredShapes }).Count -eq 0) 'Retired BS ownership remains.'
Check (@($newIrJson.nodes | Where-Object { $_.id -match '^graph\.(eye\.details|pupil|iris|highlight)' }).Count -eq 0) 'Retired branches remain.'
Check (@($newValues.inputs | Where-Object { $_.parameter -in @('eyeBlinkLeft','eyeBlinkRight') }).Count -eq 2) 'Canonical blink inputs were lost.'
function Compact($Object) { ConvertTo-Json -InputObject $Object -Compress -Depth 100 }
Check ((Compact @($oldValues.inputs | Where-Object { $_.parameter -notmatch '^(EyeDetailsEnabled$|HoPupil|HoIris|HoHighlight)' })) -ceq (Compact $newValues.inputs)) 'Unrelated input definitions changed.'
$oldNodes = @{}; foreach ($node in $oldIrJson.nodes) { $oldNodes[$node.id] = $node }
foreach ($node in $newIrJson.nodes) {
    if ($node.id -ne $newIrJson.root) { Check ((Compact $oldNodes[$node.id]) -ceq (Compact $node)) "A retained authored node changed: $($node.id)" }
}
$rootBefore = $oldNodes[$oldIrJson.root]
$rootAfter = $newIrJson.nodes | Where-Object id -eq $newIrJson.root
Check ((Compact @($rootBefore.children | Where-Object node -ne 'graph.eye.details')) -ceq (Compact $rootAfter.children)) 'An unrelated root branch or gate changed.'
Check ((Compact @($oldIrJson.targets | Where-Object id -notmatch $retiredShapes)) -ceq (Compact $newIrJson.targets)) 'An unrelated target declaration changed.'
Check ((Compact @($oldIrJson.parameters | Where-Object name -notmatch $retiredParams)) -ceq (Compact $newIrJson.parameters)) 'An unrelated IR parameter changed.'
Check ((Compact @($oldValues.outputs | Where-Object { $_.parameter -notmatch $retiredParams -and $_.parameter -notin $changed })) -ceq
    (Compact @($newValues.outputs | Where-Object parameter -notin $changed))) 'An unrelated Value rule changed.'

$neutral = Sample $profile $graph @{}
Near $neutral.values['Mouth/Width'] 0.5 'No-input mouth width is neutral'
Near $neutral.weights['HO-嘴宽-常'] 1 'No-input IR uses the normal width shape'
Near $neutral.weights['HO-嘴宽-缩'] 0 'No-input IR has no narrow shape'
$rest = @{ MouthDimpleLeft = 0.14113058; MouthDimpleRight = 0.1373033; MouthPucker = 0.20047982 }
if ($RestMonitor) {
    $rest = @{}
    $monitor = (Get-Content -Raw -LiteralPath $RestMonitor | ConvertFrom-Json -Depth 100).result
    foreach ($row in $monitor.raw) { if ($row.fresh) { $rest[$row.key] = $row.live } }
    Check ($rest.ContainsKey('MouthPucker') -and $rest.ContainsKey('MouthDimpleLeft') -and $rest.ContainsKey('MouthDimpleRight')) 'The supplied monitor has no fresh mouth-width inputs.'
}
$oldRest = Sample $oldProfile $oldGraph $rest
$newRest = Sample $profile $graph $rest
Near $newRest.values['Mouth/Width'] 0.5 'Captured rest goes to the center knot'
Near $newRest.weights['HO-嘴宽-常'] 1 'Captured rest gets full normal width'
Near $newRest.weights['HO-嘴宽-缩'] 0 'Captured rest has no narrow weight'
Near (Sample $profile $graph @{ MouthPucker = 0.93 }).weights['HO-嘴宽-缩'] 1 'Deliberate narrowing reaches its old end point'
Near (Sample $profile $graph @{ MouthDimpleLeft = 0.5; MouthDimpleRight = 0.5 }).weights['HO-嘴宽-宽'] 1 'Deliberate widening reaches its old end point'
$prev = 0.0
foreach ($step in -40..100) {
    $rawWidth = $step / 40.0
    $sample = if ($rawWidth -lt 0) { @{ MouthPucker = -$rawWidth } } else {
        @{ MouthDimpleLeft = $rawWidth / 4; MouthDimpleRight = $rawWidth / 4 }
    }
    $width = (Sample $profile $graph $sample).values['Mouth/Width']
    Check ($width -ge ($prev - 0.00001) -and $width -ge 0 -and $width -le 1) 'Width curve is not monotonic and normalized.'
    $prev = $width
}

$wink = @{ EyeBlinkLeft = 0.86; EyeSquintLeft = 0.4; BrowDownLeft = 0.83; BrowDownRight = 0.16 }
$oldWink = Sample $oldProfile $oldGraph $wink
$newWink = Sample $profile $graph $wink
Near $oldWink.weights['HO-眼情感-愤怒睁眼Left'] 1 'Regression reproduces angry open on the old left wink rule'
foreach ($side in @('Left', 'Right')) {
    $other = if ($side -eq 'Left') { 'Right' } else { 'Left' }
    $rawWink = @{ "EyeBlink$side" = 0.86; "EyeSquint$side" = 0.4; "BrowDown$side" = 0.83; "BrowDown$other" = 0.16 }
    foreach ($wideNoise in @(0, 0.2, 1)) {
        $rawWink["EyeWide$side"] = $wideNoise
        $sample = Sample $profile $graph $rawWink
        Near $sample.values["Eye/$side/Open"] 2 "Strong $side blink reaches the actual closed knot"
        Near $sample.values["Eye/$side/Anger"] 0 "Unilateral brow movement does not turn the wink into anger"
        Near $sample.weights["HO-眼情感-常态紧闭$side"] 1 "The real $side closed BS is fully selected"
        Near (EyelidSum $sample.weights $side '睁眼') 0 "No open pose leaks into a full wink"
        Near $sample.values["Eye/$other/Open"] 0 "The opposite eye remains open"
    }
    $rawWink["Eye${side}Enabled"] = 0
    Near (EyelidSum (Sample $profile $graph $rawWink).weights $side '紧闭') 0 "The $side eyelid Gate is preserved"
    $smile = Sample $profile $graph @{ "EyeBlink$side" = 0.47; "EyeSquint$side" = 0.103 }
    Check ($smile.values["Eye/$side/Open"] -lt 0.05 -and $smile.values["Eye/$side/Emotion"] -gt 0.05) 'Old smile-squint baseline must not turn into a blink.'
    Near (Sample $profile $graph @{ "EyeWide$side" = 1 }).weights["HO-眼情感-常态张大$side"] 1 'Wide still reaches its original negative-axis pose'
    Near (Sample $profile $graph @{ "BrowDown$side" = 0.92 }).weights["HO-眼情感-愤怒睁眼$side"] 1 'Real open-eye anger still works'
}
$both = Sample $profile $graph @{ EyeBlinkLeft = 1; EyeBlinkRight = 1; EyeSquintLeft = 0.8; EyeSquintRight = 0.8 }
Near $both.weights['HO-眼情感-常态紧闭Left'] 1 'Bilateral blink reaches full left closure'
Near $both.weights['HO-眼情感-常态紧闭Right'] 1 'Bilateral blink reaches full right closure'
$angryBlink = Sample $profile $graph @{ EyeBlinkLeft = 1; EyeBlinkRight = 1; BrowDownLeft = 0.92; BrowDownRight = 0.95 }
Near $angryBlink.weights['HO-眼情感-愤怒紧闭Left'] 1 'Bilateral brow evidence still allows an angry closed pose'
Near $angryBlink.weights['HO-眼情感-愤怒紧闭Right'] 1 'Bilateral brow evidence still allows an angry closed pose'
foreach ($side in @('Left','Right')) {
    $prev = 0.0
    foreach ($step in 0..100) {
        $sample = Sample $profile $graph @{ "EyeBlink$side" = ($step / 100.0); "EyeSquint$side" = 0.4; "BrowDown$side" = 0.9 }
        $closure = $sample.values["Eye/$side/Open"]
        Check ($closure -ge ($prev - 0.00001) -and $closure -ge 0 -and $closure -le 2) 'Blink is not monotonic.'
        if ($step -ge 80) {
            Near $closure 2 'Strong blink does not stall at the half-closed knot'
            Near $sample.weights["HO-眼情感-愤怒睁眼$side"] 0 'Full blink cannot select angry open'
        }
        $prev = $closure
    }
}

foreach ($fps in @(30, 60, 120)) {
    $session = [Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueSession]::new($profile)
    $session.Evaluate((Raw @{}), 0)
    for ($frame = 1; $frame -le 12; $frame++) { $session.Evaluate((Raw $wink), $frame / [double]$fps) }
    $graph.Evaluate($session.Values)
    Near $graph.Values['HO-眼情感-常态紧闭Left'] 1 "Stateful wink settles at closed at ${fps}fps"
    Near $graph.Values['HO-眼情感-愤怒睁眼Left'] 0 "Stateful wink does not stay on angry open at ${fps}fps"
    # An absent channel is held by the Value session; an actual release sends zeros.
    $release = @{ EyeBlinkLeft = 0; EyeSquintLeft = 0; BrowDownLeft = 0; BrowDownRight = 0 }
    for ($frame = 13; $frame -le 24; $frame++) { $session.Evaluate((Raw $release), $frame / [double]$fps) }
    $graph.Evaluate($session.Values)
    Near $graph.Values['HO-眼情感-常态睁眼Left'] 1 "Release returns to open at ${fps}fps"
}

# Regression guard for unrelated mouth, jaw, tongue, nose, cheek and gates.
$random = [Random]::new(3911)
foreach ($trial in 1..200) {
    $channels = @{}
    foreach ($row in $profile.inputs) {
        if ($row.expression -match '^\w+$') { $channels[$row.expression] = $random.NextDouble() }
    }
    $previous = Sample $oldProfile $oldGraph $channels
    $current = Sample $profile $graph $channels
    foreach ($pair in $current.values.GetEnumerator()) {
        if ($pair.Key -notin $changed) { Near $pair.Value $previous.values[$pair.Key] "Unrelated Value $($pair.Key)" }
    }
    foreach ($pair in $current.weights.GetEnumerator()) {
        if ($pair.Key -notmatch $eyeOrWidth) { Near $pair.Value $previous.weights[$pair.Key] "Unrelated BS $($pair.Key)" }
    }
}
$report = [ordered]@{
    assertions = $script:checks
    unaffectedComparisons = 200
    counts = @{ inputs = $newValues.inputs.Count; outputs = $newValues.outputs.Count; targets = $newIrJson.targets.Count; nodes = $newIrJson.nodes.Count }
    oldRestWidth = $oldRest.values['Mouth/Width']
    newRestWidth = $newRest.values['Mouth/Width']
    oldRestNarrow = $oldRest.weights['HO-嘴宽-缩']
    newRestNarrow = $newRest.weights['HO-嘴宽-缩']
    cases = @(
        @{ name = 'rest'; before = $oldRest.weights; after = $newRest.weights }
        @{ name = 'left-wink-repro'; before = $oldWink.weights; after = $newWink.weights }
    )
}
if ($ReportPath) { $report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $ReportPath -Encoding utf8 }
"WTD_TRACKING_REPAIR PASS assertions=$($script:checks) restWidth=$($report.oldRestWidth)->$($report.newRestWidth) narrow=$($report.oldRestNarrow)->$($report.newRestNarrow)"
