param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '../artifacts/verify-build')
)
$ErrorActionPreference = 'Stop'
$studioRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build = [IO.Path]::GetFullPath($BuildRoot)
$studioExe = Join-Path $build 'bin/HoFace.Studio/release/HoFaceStudio.exe'
$consoleExe = Join-Path $build 'bin/HoFace.Console/release/HoFace.Console.exe'
if (!(Test-Path -LiteralPath $studioExe)) {
    $studioExe = Join-Path $build 'HoFace.Studio/bin/Release/net10.0-windows/HoFaceStudio.exe'
    $consoleExe = Join-Path $build 'HoFace.Console/bin/Release/net10.0/HoFace.Console.exe'
}
if (!(Test-Path -LiteralPath $studioExe) -or !(Test-Path -LiteralPath $consoleExe)) {
    throw "Build first: dotnet build HoFaceStudio.slnx -c Release --artifacts-path `"$build`""
}
$pipeName = 'HoFaceStudio.Checks.' + [Guid]::NewGuid().ToString('N')
$work = Join-Path $studioRoot ('artifacts/automation-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$valuePath = Join-Path $work 'Mouth.hoface.value.json'
$irPath = Join-Path $work 'Mouth.hoface.ir.json'
Copy-Item -LiteralPath (Join-Path $studioRoot 'samples/Mouth.hoface.value.json') -Destination $valuePath
Copy-Item -LiteralPath (Join-Path $studioRoot 'samples/Mouth.hoface.ir.json') -Destination $irPath
$valueDraft = [System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($valuePath))
$derivedRule = [System.Text.Json.Nodes.JsonObject]::new()
$derivedRule['parameter'] = [System.Text.Json.Nodes.JsonValue]::Create('Mouth/Derived')
$derivedRule['expression'] = [System.Text.Json.Nodes.JsonValue]::Create('out("Mouth/Open") * 2')
$derivedRule['defaultValue'] = [System.Text.Json.Nodes.JsonValue]::Create(0)
$valueDraft['outputs'].AsArray().Add($derivedRule)
[IO.File]::WriteAllText($valuePath, $valueDraft.ToJsonString(), [Text.UTF8Encoding]::new($false))
$script:assertions = 0
function Assert-Check([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw $Label }
    $script:assertions++
}
function Get-FreeUdpPort {
    $socket = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    try { return ([Net.IPEndPoint]$socket.Client.LocalEndPoint).Port }
    finally { $socket.Dispose() }
}
function Send-VtsFrame([int]$Port, [double]$Jaw, [double]$SmileLeft, [double]$SmileRight) {
    $packet = '{"Timestamp":1,"FaceFound":true,"BlendShapes":[' +
        '{"k":"JawOpen","v":' + $Jaw.ToString([Globalization.CultureInfo]::InvariantCulture) + '},' +
        '{"k":"MouthSmileLeft","v":' + $SmileLeft.ToString([Globalization.CultureInfo]::InvariantCulture) + '},' +
        '{"k":"MouthSmileRight","v":' + $SmileRight.ToString([Globalization.CultureInfo]::InvariantCulture) + '}]}' + ''
    $bytes = [Text.Encoding]::UTF8.GetBytes($packet)
    $sender = [Net.Sockets.UdpClient]::new()
    try { $sender.Send($bytes, $bytes.Length, [Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $Port)) | Out-Null }
    finally { $sender.Dispose() }
}
function New-ChildInfo([string]$Exe) {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.Environment['HOFACE_STUDIO_PIPE'] = $pipeName
    $info.Environment['HOFACE_STUDIO_NO_WORKSPACE'] = '1'
    return $info
}
function Invoke-Cli([string[]]$CliArguments, [string]$StdinText = $null) {
    $info = New-ChildInfo $consoleExe
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $CliArguments) { $info.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($info)
    $outputTask = $child.StandardOutput.ReadToEndAsync()
    $errorTask = $child.StandardError.ReadToEndAsync()
    if ($null -ne $StdinText) { $child.StandardInput.Write($StdinText) }
    $child.StandardInput.Close()
    if (!$child.WaitForExit(15000)) { $child.Kill($true); throw 'CLI did not finish within 15 seconds.' }
    $text = $outputTask.GetAwaiter().GetResult().Trim()
    $stderrText = $errorTask.GetAwaiter().GetResult()
    if ($stderrText) { throw "Unexpected stderr: $stderrText" }
    $lines = @($text -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json -Depth 100 })
    return @{ exitCode = $child.ExitCode; lines = $lines; data = $lines[-1] }
}
function Call-Studio([string]$Command, [object]$Payload = @{}) {
    $result = Invoke-Cli @($Command, ($Payload | ConvertTo-Json -Depth 100 -Compress))
    if ($result.exitCode -ne 0 -or !$result.data.ok) { throw "$Command failed: $($result.data | ConvertTo-Json -Compress -Depth 8)" }
    return $result.data.result
}
$process = $null
$idlePipe = $null
try {
    $process = [Diagnostics.Process]::Start((New-ChildInfo $studioExe))
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $probe = Invoke-Cli @('status', '--timeout-ms', '500')
        if ($probe.exitCode -eq 0) { $ready = $true; break }
        if ($process.HasExited) { throw 'Studio exited before its automation pipe became ready.' }
    }
    Assert-Check $ready 'Automation startup'
    $help = Invoke-Cli @('--help')
    Assert-Check ($help.exitCode -eq 0 -and $help.data.result.commands.Count -ge 40) 'Offline CLI discovery'
    $invalid = Invoke-Cli @('status', '{bad}')
    Assert-Check ($invalid.exitCode -eq 2 -and $invalid.data.code -eq 'INVALID_JSON') 'Malformed JSON has structured error and exit code 2'
    $unknown = Invoke-Cli @('does-not-exist')
    Assert-Check ($unknown.exitCode -eq 2 -and $unknown.data.code -eq 'INVALID_ARGUMENT') 'Unknown CLI command is explicit'
    $notReady = Invoke-Cli @('document')
    Assert-Check ($notReady.exitCode -eq 1 -and $notReady.data.code -eq 'INVALID_STATE') 'Document precondition is explicit'
    $idlePipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    $idlePipe.Connect(1000)
    $state = Call-Studio 'status'
    Assert-Check (!$state.hasDocument) 'Idle client does not block a second client'
    $previewWithoutDocument = Invoke-Cli @('vtsStart', (@{ phoneIp='127.0.0.1'; phonePort=9; localPort=(Get-FreeUdpPort) } | ConvertTo-Json -Compress))
    Assert-Check ($previewWithoutDocument.exitCode -eq 1) 'Preview mode still protects its document prerequisite'
    $rawSink = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    try {
        $rawSink.Client.ReceiveTimeout = 1000
        $rawPort = ([Net.IPEndPoint]$rawSink.Client.LocalEndPoint).Port
        $null = Call-Studio 'mode' @{ value='RawForward'; rawForwardPort=$rawPort }
        $rawInputPort = Get-FreeUdpPort
        $rawConnected = Call-Studio 'vtsStart' @{ phoneIp='127.0.0.1'; phonePort=9; localPort=$rawInputPort }
        Assert-Check (!$rawConnected.hasDocument -and $rawConnected.input -like '*已绑定*') 'RawForward connects VTS without opening a work file'
        Send-VtsFrame $rawInputPort .4 .2 .3
        $rawPacketEndpoint = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        $rawPacket = ([Text.Encoding]::UTF8.GetString($rawSink.Receive([ref]$rawPacketEndpoint))) | ConvertFrom-Json
        Assert-Check ([Math]::Abs($rawPacket.values.JawOpen - .4) -lt .001) 'RawForward transmits a real VTS frame without a work file'
        $null = Call-Studio 'vtsStop'
        $null = Call-Studio 'mode' @{ value='Preview' }
    } finally { $rawSink.Dispose() }
    $state = Call-Studio 'open' @{ valuePath = $valuePath; irPath = $irPath }
    Assert-Check ($state.selectedValue -eq 'Mouth/Open') 'Open selects a useful first row'
    $items = Call-Studio 'list'
    Assert-Check ($items.inputs.Count -eq 3 -and $items.outputs.Count -eq 3 -and $items.nodes.Count -eq 5) 'AI can inspect all input/output/node definitions'
    $rootRule = $items.nodes | Where-Object { $_.id -eq 'mouth' }
    Assert-Check ($rootRule.children.Count -eq 4 -and $items.orphanNodes.Count -eq 0) 'IR list describes the root-reachable Blend Tree and separates orphans'
    $documentState = Call-Studio 'document'
    $irWithOrphan = [System.Text.Json.Nodes.JsonNode]::Parse($documentState.irText)
    $irWithOrphan['nodes'].AsArray().Add([System.Text.Json.Nodes.JsonNode]::Parse(
        '{"id":"unusedPose","name":"Unused","type":"pose","weights":[]}'))
    $null = Call-Studio 'text' @{ value=$documentState.valueText; ir=$irWithOrphan.ToJsonString() }
    Assert-Check ((Call-Studio 'list').orphanNodes.Count -eq 1) 'CLI reports rules not reachable from root'
    $null = Call-Studio 'clearIrOrphans'
    Assert-Check ((Call-Studio 'list').orphanNodes.Count -eq 0) 'CLI can remove root-unreachable rules'
    $newPose = Call-Studio 'addIrRule' @{ parentId='mouth'; type='pose'; name='CLI 新姿势' }
    $items = Call-Studio 'list'
    Assert-Check ($items.nodes.Count -eq 6 -and ($items.nodes | Where-Object id -eq 'mouth').children.Count -eq 5) 'CLI creates an aliased Pose beneath its Blend Tree'
    $null = Call-Studio 'deleteIrChild' @{ nodeId='mouth'; index=4 }
    $items = Call-Studio 'list'
    Assert-Check ($items.nodes.Count -eq 5) 'Deleting the stored Blend child prunes its unreferenced pose rule'
    $nestedBlend = Call-Studio 'addIrRule' @{ parentId='mouth'; type='blend2d'; name='嵌套树' }
    Assert-Check ((Call-Studio 'list').nodes.Count -eq 7) 'CLI adds a nested Blend Tree with its starter Pose'
    $null = Call-Studio 'deleteIrRule' @{ id=$nestedBlend.created }
    Assert-Check ((Call-Studio 'list').nodes.Count -eq 5) 'CLI deletes a nested Blend Tree and cleans its orphaned child'
    $capture = Call-Studio 'screenshot' @{ path = (Join-Path $work 'value.png') }
    Assert-Check ((Get-Item -LiteralPath $capture.path).Length -gt 5000) 'Background WPF screenshot'
    $irCompact = Call-Studio 'view' @{ width=1024; height=640; structureTab=1; centerTab=1 }
    $null = Call-Studio 'selectNode' @{ id='mouth' }
    $irTreeCapture = Call-Studio 'screenshot' @{ path = (Join-Path $work 'ir-tree-compact.png') }
    Assert-Check ($irCompact.width -ge 1024 -and (Get-Item -LiteralPath $irTreeCapture.path).Length -gt 5000) 'IR Blend Tree hierarchy and controls render at compact size'
    $null = Call-Studio 'view' @{ width=1380; height=860; structureTab=0; centerTab=0 }
    $null = Call-Studio 'selectValue' @{ parameter='Mouth/Open' }
    $modifierChain = @(
        @{ kind='smooth'; seconds=.08; steps=@() },
        @{ kind='steps'; seconds=0; steps=@(@{trigger=.5; target=1; hold=.2; threshold=.05; enterSeconds=.1; exitSeconds=.05}) }
    )
    $null = Call-Studio 'setValueModifiers' @{ parameter='Mouth/Open'; modifiers=$modifierChain }
    $modifierList = Call-Studio 'list' @{ filter='Mouth/Open' }
    $listedChain = $modifierList.outputs | Where-Object { $_.parameter -eq 'Mouth/Open' }
    Assert-Check ($listedChain.modifiers.Count -eq 2 -and $listedChain.modifiers[1].steps[0].enterSeconds -eq .1) 'CLI roundtrips ordered modifier and hold-step data'
    $modifierCapture = Call-Studio 'screenshot' @{ path = (Join-Path $work 'modifier-chain.png') }
    Assert-Check ((Get-Item -LiteralPath $modifierCapture.path).Length -gt 5000) 'Modifier chain editor renders with configured steps'
    $null = Call-Studio 'setValueModifiers' @{ parameter='Mouth/Open'; modifiers=[object[]]@() }
    $layout = Call-Studio 'view' @{ width = 1024; height = 640; structureWidth = 210; plotRatio = 0.42 }
    Assert-Check ($layout.plotWidth -ge 280 -and $layout.inspectorWidth -ge 320) 'Minimum window has usable resizable panels'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'compact.png') }
    $monitor = Call-Studio 'monitor'
    Assert-Check ($monitor.raw.Count -eq 3 -and $monitor.values.Count -eq 3 -and $monitor.weights.Count -eq 3) 'Realtime monitor starts with configured raw, value, and IR parameters'
    $null = Call-Studio 'view' @{ centerTab = 2; width = 1380; height = 860 }
    $inputPort = Get-FreeUdpPort
    $null = Call-Studio 'vtsStart' @{ phoneIp='127.0.0.1'; phonePort=9; localPort=$inputPort }
    Send-VtsFrame $inputPort .3 .2 .4
    $monitor = Call-Studio 'monitor'
    for ($attempt = 0; $attempt -lt 20 -and !($monitor.raw | Where-Object { $_.key -eq 'JawOpen' }).fresh; $attempt++) {
        Start-Sleep -Milliseconds 50
        $monitor = Call-Studio 'monitor'
    }
    $rawJaw = $monitor.raw | Where-Object { $_.key -eq 'JawOpen' }
    Assert-Check ($rawJaw.fresh -and [Math]::Abs($rawJaw.live - .3) -lt .001) 'Live monitor reads a real VTS UDP frame'
    Assert-Check ([Math]::Abs(($monitor.values | Where-Object { $_.key -eq 'Mouth/Derived' }).live - .6) -lt .001) 'Live output rule evaluates the real raw frame'
    $null = Call-Studio 'setMonitorOverride' @{ stage='raw'; parameter='JawOpen'; mode='1' }
    Send-VtsFrame $inputPort .15 .2 .4
    Start-Sleep -Milliseconds 80
    $monitor = Call-Studio 'monitor'
    $rawJaw = $monitor.raw | Where-Object { $_.key -eq 'JawOpen' }
    Assert-Check ([Math]::Abs($rawJaw.live - .15) -lt .001 -and $rawJaw.value -eq 1) 'Raw stage distinguishes physical and forwarded values'
    $null = Call-Studio 'setMonitorOverride' @{ stage='value'; parameter='Mouth/Open'; mode='1' }
    Send-VtsFrame $inputPort .15 .2 .4
    Start-Sleep -Milliseconds 80
    $monitor = Call-Studio 'monitor'
    $derived = $monitor.values | Where-Object { $_.key -eq 'Mouth/Derived' }
    Assert-Check ([Math]::Abs($derived.live - .3) -lt .001 -and [Math]::Abs($derived.value - 2) -lt .001) 'Upstream value pin propagates into the downstream out expression'
    $null = Call-Studio 'setMonitorOverride' @{ stage='value'; parameter='Mouth/Open'; mode='0' }
    Send-VtsFrame $inputPort .15 .2 .4
    Start-Sleep -Milliseconds 80
    $monitor = Call-Studio 'monitor'
    $derived = $monitor.values | Where-Object { $_.key -eq 'Mouth/Derived' }
    Assert-Check ([Math]::Abs($derived.live - .3) -lt .001 -and [Math]::Abs($derived.value) -lt .001) 'Changing the upstream value pin updates downstream evaluation without altering live readings'
    $null = Call-Studio 'setMonitorOverride' @{ stage='ir'; parameter='jawOpen'; mode='-1' }
    Send-VtsFrame $inputPort .15 .2 .4
    Start-Sleep -Milliseconds 80
    $monitor = Call-Studio 'monitor'
    $liveWeight = $monitor.weights | Where-Object { $_.key -eq 'jawOpen' }
    Assert-Check ($liveWeight.live -ge 0 -and $liveWeight.value -eq -1) 'IR pin preserves the clamped unmodified live target'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'realtime.png') }
    $compactMonitor = Call-Studio 'view' @{ width = 1024; height = 640; centerTab = 2 }
    Assert-Check ($compactMonitor.width -ge 1024 -and $compactMonitor.plotWidth -ge 280 -and $compactMonitor.inspectorWidth -ge 320) 'Realtime monitor fits the compact window while keeping its data scrollable'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'realtime-compact.png') }
    $null = Call-Studio 'view' @{ width = 1380; height = 860; centerTab = 2 }
    $null = Call-Studio 'clearMonitorOverrides'
    $null = Call-Studio 'vtsStop'
    $monitor = Call-Studio 'monitor'
    $releasedWeight = $monitor.weights | Where-Object { $_.key -eq 'jawOpen' }
    Assert-Check ($releasedWeight.value -eq $releasedWeight.live -and $releasedWeight.value -ge 0) 'Clearing pins restores normal range-clamped evaluation'
    $valueOverride = Call-Studio 'setMonitorOverride' @{ stage='value'; parameter='Mouth/Open'; mode='1' }
    Assert-Check (($valueOverride.values | Where-Object key -eq 'Mouth/Open').overridden) 'AI can set temporary per-parameter stage override'
    $null = Call-Studio 'clearMonitorOverrides' @{ stage='value' }
    $afterClear = Call-Studio 'monitor'
    Assert-Check (!($afterClear.values | Where-Object key -eq 'Mouth/Open').overridden) 'AI can restore live stage values'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'realtime-cleared.png') }
    $null = Call-Studio 'view' @{ centerTab = 0 }
    $null = Call-Studio 'view' @{ width = 1380; height = 860; reset = $true }
    $null = Call-Studio 'selectNode' @{ id = 'mouth' }
    $weights = Call-Studio 'cursor' @{ x = 1; y = 1 }
    Assert-Check ($weights.manualPreview -and $weights.weights.jawOpen -gt 0.99) 'Cursor drives actual IR evaluation'
    $replayPath = Join-Path $work 'neutral.jsonl'
    $replayText = '{"type":"hoface.record","version":1,"kind":"frame","time":0,"tracked":true,"values":{"JawOpen":0,"MouthSmileLeft":0,"MouthSmileRight":0}}' + "`n" +
        '{"type":"hoface.record","version":1,"kind":"frame","time":5,"tracked":true,"values":{"JawOpen":0,"MouthSmileLeft":0,"MouthSmileRight":0}}'
    [IO.File]::WriteAllText($replayPath, $replayText)
    $null = Call-Studio 'replayOpen' @{ path = $replayPath }
    $null = Call-Studio 'replayPlay'
    $state = Call-Studio 'status'
    Assert-Check ($state.replayIndex -gt 0) 'Replay input reaches the real WPF session'
    $held = Call-Studio 'diagnostics'
    Assert-Check ($held.manualPreview -and $held.weights.jawOpen -gt 0.99) 'Temporary override survives incoming neutral frames'
    $null = Call-Studio 'replayPause'
    $null = Call-Studio 'clearPreview'
    $cleared = Call-Studio 'diagnostics'
    Assert-Check (!$cleared.manualPreview -and $cleared.weights.jawOpen -eq 0) 'Clear preview restores actual source values'
    $null = Call-Studio 'cursor' @{ x = 1; y = 1 }
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'blend2d.png') }
    $null = Call-Studio 'selectNode' @{ id = 'smile' }
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'pose.png') }
    $focused = Call-Studio 'view' @{ showEditor = $false }
    Assert-Check (!$focused.showEditor -and $focused.plotWidth -eq 0) 'Middle editor collapses without consuming property space'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'pose-focus.png') }
    $null = Call-Studio 'view' @{ showEditor = $true }
    $null = Call-Studio 'selectValue' @{ parameter = 'Mouth/Open' }
    $null = Call-Studio 'setValueCurveKey' @{ parameter = 'Mouth/Open'; index = 1; value = 0.77 }
    $null = Call-Studio 'selectValue' @{ parameter = 'Mouth/Form' }
    $state = Call-Studio 'undo'
    Assert-Check (!$state.dirty -and $state.selectedValue -eq 'Mouth/Form') 'Undo preserves active selection over IPC'
    $state = Call-Studio 'redo'
    Assert-Check ($state.dirty -and $state.selectedValue -eq 'Mouth/Form') 'Redo preserves active selection over IPC'
    $null = Call-Studio 'setPoseWeight' @{ nodeId='open'; targetId='Custom/TypedTarget'; value=.35 }
    $items = Call-Studio 'list'
    Assert-Check (($items.nodes | Where-Object id -eq 'open').weights.target -contains 'Custom/TypedTarget') 'Pose CLI accepts a free-text target and declares it'
    $null = Call-Studio 'deletePoseWeight' @{ nodeId='open'; targetId='Custom/TypedTarget' }
    $items = Call-Studio 'list'
    Assert-Check (!(($items.nodes | Where-Object id -eq 'open').weights.target -contains 'Custom/TypedTarget')) 'Pose CLI deletes explicit stored weights and prunes unused target declarations'
    $protected = Invoke-Cli @('open', (@{valuePath=$valuePath;irPath=$irPath} | ConvertTo-Json -Compress))
    Assert-Check ($protected.exitCode -eq 1) 'Opening another document protects unsaved work'
    $protectedClose = Invoke-Cli @('shutdown')
    Assert-Check ($protectedClose.exitCode -eq 1 -and !$process.HasExited) 'Closing protects unsaved work'
    $null = Call-Studio 'save'
    $savedValue = [IO.File]::ReadAllText($valuePath) | ConvertFrom-Json -Depth 100
    Assert-Check ($savedValue.outputs[0].curve.keys[1].v -eq 0.77) 'AI edits persist to the backend document'
    $savedText = [IO.File]::ReadAllText($valuePath)
    [IO.File]::AppendAllText($valuePath, "`n")
    $aliasPath = $valuePath.Replace('\', '/').ToUpperInvariant()
    $conflict = Invoke-Cli @('save', (@{valuePath=$aliasPath} | ConvertTo-Json -Compress))
    Assert-Check ($conflict.exitCode -eq 1 -and $conflict.data.code -eq 'FILE_CONFLICT') 'Path spelling cannot bypass external edit protection'
    [IO.File]::WriteAllText($valuePath, $savedText, [Text.UTF8Encoding]::new($false))
    $requests = "{`"id`":`"one`",`"command`":`"status`"}`n{`"id`":`"two`",`"command`":`"selection`"}"
    $batch = Invoke-Cli @('--batch') $requests
    Assert-Check ($batch.exitCode -eq 0 -and $batch.lines.Count -eq 2 -and $batch.lines[1].id -eq 'two') 'NDJSON batch preserves request ids'
    $payloadFile = Join-Path $work 'payload.json'
    [IO.File]::WriteAllText($payloadFile, '{"parameter":"Mouth/Open"}', [Text.UTF8Encoding]::new($false))
    $fromFile = Invoke-Cli @('selectValue', '--file', $payloadFile)
    Assert-Check ($fromFile.exitCode -eq 0 -and $fromFile.data.result.selectedValue -eq 'Mouth/Open') 'Payload file avoids Windows quoting issues'
    $fromStdin = Invoke-Cli @('selectNode', '--stdin') '{"id":"mouth"}'
    Assert-Check ($fromStdin.exitCode -eq 0 -and $fromStdin.data.result.selectedNode -eq 'mouth') 'Payload stdin'
    $null = Call-Studio 'draftText' @{ value = '{' }
    $null = Call-Studio 'view' @{ centerTab = 3 }
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'json-draft.png') }
    $badSave = Invoke-Cli @('save')
    Assert-Check ($badSave.exitCode -eq 1) 'Invalid text cannot overwrite saved files'
    $null = Call-Studio 'revertText'
    $null = Call-Studio 'view' @{ centerTab = 0; showConnection = $true; showFiles = $true }
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'settings.png') }
    $null = Call-Studio 'view' @{ showConnection = $false; showFiles = $false }
    $result = Call-Studio 'shutdown'
    Assert-Check $result.closing 'Shutdown responds before disconnecting'
    Assert-Check ($process.WaitForExit(5000)) 'Shutdown releases the process and pipe'
    Write-Output "Passed $script:assertions automation checks."
    Write-Output "Screenshots and test fixtures: $work"
}
finally {
    if ($null -ne $idlePipe) { $idlePipe.Dispose() }
    if ($null -ne $process -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
}
