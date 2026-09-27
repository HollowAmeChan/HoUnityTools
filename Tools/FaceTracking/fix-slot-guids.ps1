# fix-slot-guids.ps1 -- put slot clips back on the "GUID = md5('ho-face-slot:<slot name>')" scheme.
#
# WHY: `Tools/FaceTracking/make-vts-controller.ps1` does not have an asset database -- it derives each slot
# clip's GUID from the slot name and writes both the `.anim.meta` and the controller reference from
# it. Six clips (the two `InvertedV` + four `Cheek` cells, added 2026-09-27) were created on disk
# with editor-generated random GUIDs instead, so a future regeneration would reference GUIDs that
# point at nothing. This rewrites the meta + every controller reference to the derived value.
#
# ASCII-only text (a BOM-less .ps1 is read as ANSI by PS 5.1, so a Chinese *path* must come in as an
# argument, not as a literal in here).
param(
    [Parameter(Mandatory = $true)][string] $ClipFolder,
    [Parameter(Mandatory = $true)][string] $Controller,
    [Parameter(Mandatory = $true)][string[]] $Names,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$md5 = [System.Security.Cryptography.MD5]::Create()
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Read/write helpers that keep whatever BOM the file already had.
function ReadText([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    return @{ Text = $text; Bom = $hasBom }
}
function WriteText([string]$path, [string]$text, [bool]$bom) {
    if ($bom) { [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true))) }
    else { [System.IO.File]::WriteAllText($path, $text, $utf8NoBom) }
}

$controllerFile = ReadText $Controller
$controllerText = $controllerFile.Text
$applied = 0
foreach ($name in $Names) {
    $guid = ([System.BitConverter]::ToString($md5.ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes('ho-face-slot:' + $name))) -replace '-', '').ToLowerInvariant()
    $metaPath = Join-Path $ClipFolder ($name + '.anim.meta')
    if (-not (Test-Path -LiteralPath $metaPath)) { throw ('no meta for slot clip: ' + $name) }
    $metaFile = ReadText $metaPath
    $m = [regex]::Match($metaFile.Text, 'guid:\s*([0-9a-fA-F]{32})')
    if (-not $m.Success) { throw ('no guid line in ' + $metaPath) }
    $old = $m.Groups[1].Value.ToLowerInvariant()
    $refs = ([regex]::Matches($controllerText, [regex]::Escape($old))).Count
    Write-Output ("{0}: {1} -> {2}  (controller refs {3})" -f $name, $old, $guid, $refs)
    if ($refs -ne 1) { throw ('expected exactly 1 controller reference for ' + $name + ', found ' + $refs) }
    if ($old -eq $guid) { continue }
    if ($Apply) {
        WriteText $metaPath ($metaFile.Text -replace [regex]::Escape($old), $guid) $metaFile.Bom
        $controllerText = $controllerText -replace [regex]::Escape($old), $guid
        $applied++
    }
}
if ($Apply) {
    WriteText $Controller $controllerText $controllerFile.Bom
    Write-Output ('rewrote ' + $applied + ' guid(s); controller written')
} else {
    Write-Output 'dry run -- pass -Apply to write'
}
