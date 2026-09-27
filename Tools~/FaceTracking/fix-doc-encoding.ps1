# fix-doc-encoding.ps1 -- put the doc files back to "UTF-8 with BOM + CRLF" after an edit-tool pass.
#
# WHY: the `edit` / `write` tools drop the BOM (and my appended blocks used bare LF). The repo
# convention for docs/**/*.md is UTF-8 *with* BOM + CRLF (see docs/pitfalls/DOCS_ENCODING.md);
# a missing BOM shows up as mojibake in ANSI-default viewers, mixed line endings as diff noise.
#
# ASCII-only on purpose (PS 5.1 reads a BOM-less .ps1 as ANSI).
param(
    [Parameter(Mandatory = $true)][string[]] $Files,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
foreach ($f in $Files) {
    if (-not (Test-Path -LiteralPath $f)) { Write-Output ('MISSING ' + $f); continue }
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    $crlf = ([regex]::Matches($text, "`r`n")).Count
    $lf = ([regex]::Matches($text, "`n")).Count - $crlf
    $replacement = ([regex]::Matches($text, [string][char]0xFFFD)).Count
    $clean = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
    $changed = (-not $hasBom) -or ($lf -gt 0)
    Write-Output ("{0}: bom={1} crlf={2} bare-lf={3} replacement-char={4} -> {5}" -f `
        (Split-Path $f -Leaf), $hasBom, $crlf, $lf, $replacement, $(if ($changed) { 'FIX' } else { 'ok' }))
    if ($Apply -and $changed) {
        [System.IO.File]::WriteAllText($f, $clean, (New-Object System.Text.UTF8Encoding($true)))
    }
}
if (-not $Apply) { Write-Output 'dry run -- pass -Apply' }
