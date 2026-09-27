# check-payload-pairs.ps1 -- for each #PAIR in a payload, report whether its OLD occurs in the target
# file (and how many times). Read-only; the point is to see WHICH pair misses without the throwing
# script's exception swallowing the diagnostics.
param(
    [Parameter(Mandatory = $true)][string] $Path,
    [Parameter(Mandatory = $true)][string] $Payload
)
$ErrorActionPreference = 'Stop'
$nl = "`n"
$raw = [IO.File]::ReadAllText($Path).Replace("`r`n", $nl)
$lines = [IO.File]::ReadAllText($Payload).Replace("`r`n", $nl) -split $nl

$i = 0; $n = 0
while ($i -lt $lines.Count) {
    if ($lines[$i].Trim() -ne '#PAIR') { $i++; continue }
    $i++
    if ($i -ge $lines.Count -or $lines[$i].Trim() -ne '<<<OLD') { throw ('#PAIR without <<<OLD at line ' + ($i + 1)) }
    $i++
    $old = New-Object System.Collections.Generic.List[string]
    while ($i -lt $lines.Count -and $lines[$i].Trim() -ne '>>>NEW') { $old.Add($lines[$i]); $i++ }
    if ($i -ge $lines.Count) { throw 'missing >>>NEW' }
    $i++
    while ($i -lt $lines.Count -and $lines[$i].Trim() -ne '#PAIR') { $i++ }
    $n++
    $text = ($old.ToArray() -join $nl)
    $hits = ([regex]::Matches($raw, [regex]::Escape($text))).Count
    $head = ($text -split $nl)[0]
    '{0,2}  hits={1}  {2}' -f $n, $hits, $head.Substring(0, [Math]::Min(90, $head.Length))
}
'pairs=' + $n
