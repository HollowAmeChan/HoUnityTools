# One-shot probe helper (ASCII only).
# Mirrors .research/sync-modcore.ps1's transformation so the probe compiles the SAME
# sources the mod does -- no hand transcription, no re-implementation.
#
# Usage:  powershell -File sync.ps1 -ModCore <dir> -Out <dir>
param(
    [Parameter(Mandatory = $true)][string]$ModCore,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'

# Files needed to run the parameter layer. Subset of the sync script's $files.
$files = @(
    'HoFaceExpression.cs'
    'HoFaceMiddleware.cs'
    'HoFaceProfile.cs'
    'HoJson.cs'
    'HoFaceProfileJson.cs'
    'HoFaceTrackingChannels.cs'
    'HoFaceNaming.cs'
    'HoFaceChain.cs'
    'HoFaceOutputTable.cs'
)

if (-not (Test-Path -LiteralPath $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }

foreach ($name in $files) {
    $src = Join-Path $ModCore $name
    if (-not (Test-Path -LiteralPath $src)) { throw "missing source: $src" }

    $text = [System.IO.File]::ReadAllText($src)
    $text = $text -replace [regex]::Escape('namespace HoFaceTracking.Core'), 'namespace Hollow.HoUnityTools.FaceTracking'
    $text = $text -replace "`r`n", "`n"

    $target = Join-Path $Out $name
    # No BOM (same rule the mod .cs files follow).
    [System.IO.File]::WriteAllText($target, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  {0}" -f $name) | Out-Null
}

Write-Host ("synced {0} files -> {1}" -f $files.Count, $Out)
