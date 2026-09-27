# apply-doc-edits.ps1 -- apply a list of literal (old -> new) replacements to a UTF-8 text file.
#
# WHY: long Chinese command lines get mangled on the way into pwsh (the command text is decoded as
# ANSI), so anything with non-ASCII belongs in a payload FILE. This script is ASCII-only and only
# moves text around.
#
# Payload format (UTF-8, LF or CRLF, blank lines are data):
#   #PAIR
#   <<<OLD
#   ...old text (may be several lines)...
#   >>>NEW
#   ...new text (may be several lines)...
#   #PAIR
#   ...
# Each old text must appear EXACTLY once (the script refuses otherwise and writes nothing).
# Newlines are normalised while matching (LF vs CRLF), and the target file keeps its own style.
#
# Usage: pwsh -File Tools~/FaceTracking/apply-doc-edits.ps1 -Path <file> -Payload <file> [-Apply]
param(
    [Parameter(Mandatory = $true)][string] $Path,
    [Parameter(Mandatory = $true)][string] $Payload,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$nl = "`n"

$raw = [IO.File]::ReadAllText($Payload).Replace("`r`n", $nl)
$lines = $raw -split $nl
$pairs = New-Object System.Collections.Generic.List[object]
$i = 0
while ($i -lt $lines.Count) {
    if ($lines[$i].Trim() -ne '#PAIR') { $i++; continue }
    $i++
    if ($i -ge $lines.Count -or $lines[$i].Trim() -ne '<<<OLD') { throw ("#PAIR 后面必须是 <<<OLD（第 " + ($i + 1) + " 行）") }
    $i++
    $oldLines = New-Object System.Collections.Generic.List[string]
    while ($i -lt $lines.Count -and $lines[$i].Trim() -ne '>>>NEW') { $oldLines.Add($lines[$i]); $i++ }
    if ($i -ge $lines.Count) { throw '少了 >>>NEW' }
    $i++
    $newLines = New-Object System.Collections.Generic.List[string]
    while ($i -lt $lines.Count -and $lines[$i].Trim() -ne '#PAIR') { $newLines.Add($lines[$i]); $i++ }
    $pairs.Add([pscustomobject]@{ Old = ($oldLines -join $nl); New = ($newLines -join $nl) })
}
if ($pairs.Count -eq 0) { throw 'payload 里一对都没有' }

$fileRaw = [IO.File]::ReadAllText($Path)
$bytes = [IO.File]::ReadAllBytes($Path)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
$crlf = $fileRaw.Contains("`r`n")
$text = $fileRaw.Replace("`r`n", $nl)

$problems = New-Object System.Collections.Generic.List[string]
foreach ($pair in $pairs) {
    $hits = ([regex]::Matches($text, [regex]::Escape($pair.Old))).Count
    if ($hits -ne 1) {
        $head = $pair.Old.Split($nl)[0]
        $problems.Add("hits=$hits :: " + $head.Substring(0, [Math]::Min(70, $head.Length)))
        continue
    }
    $text = $text.Replace($pair.Old, $pair.New)
}
if ($problems.Count -gt 0) {
    foreach ($p in $problems) { "MISS $p" }
    throw ("有 " + $problems.Count + " 对没对上（没写盘）")
}

"$([IO.Path]::GetFileName($Path)): $($pairs.Count) 对都对上了（CRLF=$crlf，BOM=$hasBom，$($fileRaw.Length) -> $($text.Length) 字符）"
if (-not $Apply) { 'dry run -- 要写盘加 -Apply'; exit 0 }
$out = if ($crlf) { $text.Replace($nl, "`r`n") } else { $text }
[IO.File]::WriteAllText($Path, $out, (New-Object System.Text.UTF8Encoding($hasBom)))
"已写入：$Path"
