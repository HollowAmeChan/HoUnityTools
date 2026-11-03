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
$script:assertions = 0
function Assert-Check([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw $Label }
    $script:assertions++
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
    $state = Call-Studio 'open' @{ valuePath = $valuePath; irPath = $irPath }
    Assert-Check ($state.selectedValue -eq 'Mouth/Open') 'Open selects a useful first row'
    $items = Call-Studio 'list'
    Assert-Check ($items.inputs.Count -eq 3 -and $items.outputs.Count -eq 2 -and $items.nodes.Count -eq 5) 'AI can inspect all input/output/node definitions'
    $capture = Call-Studio 'screenshot' @{ path = (Join-Path $work 'value.png') }
    Assert-Check ((Get-Item -LiteralPath $capture.path).Length -gt 5000) 'Background WPF screenshot'
    $layout = Call-Studio 'view' @{ width = 1024; height = 640; structureWidth = 210; plotRatio = 0.42 }
    Assert-Check ($layout.plotWidth -ge 280 -and $layout.inspectorWidth -ge 320) 'Minimum window has usable resizable panels'
    $null = Call-Studio 'screenshot' @{ path = (Join-Path $work 'compact.png') }
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
    $null = Call-Studio 'view' @{ centerTab = 1 }
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
