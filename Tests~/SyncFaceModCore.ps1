# Synchronize only the shared protocol parser sources.
# Role rules, controller evaluation and hubs are not copied into the input plugin.
# ASCII-only for Windows PowerShell 5.1.

param(
    [string] $PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $ModCore     = 'D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests\Mods-Ho\HoFaceTracking\Core',
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

# source (in the package) -> destination (in the mod). Same file name.
$files = @(
    'HoJson.cs',       # protocol JSON reader only; rules belong to character components
    'HoVtsPacket.cs'   # complete VTS iPhone wire packet
)

$fromNamespace = 'Hollow.HoUnityTools.FaceTracking'
$toNamespace   = 'HoFaceTracking.Core'

# One-line ASCII marker so an editor of the copied file knows it is not the master.
$header = @(
    '// ============================================================================',
    '// PORTED FILE - do not edit here.',
    '// Master: HoUnityTools/Runtime/FaceTracking/<same file name>',
    '// Re-sync: see Core/PORTED.md (script: Tests~/SyncFaceModCore.ps1)',
    '// Only the namespace differs; the code is otherwise byte-identical.',
    '// ============================================================================',
    ''
)

if (-not (Test-Path -LiteralPath $ModCore)) {
    if ($Check) { throw "missing mod core: $ModCore" }
    New-Item -ItemType Directory -Path $ModCore -Force | Out-Null
}

foreach ($name in $files) {
    $source = Join-Path (Join-Path $PackageRoot 'Runtime\FaceTracking') $name
    if (-not (Test-Path -LiteralPath $source)) { throw "missing source: $source" }

    # Read as bytes so we control the BOM ourselves (mod .cs files carry no BOM).
    $bytes = [System.IO.File]::ReadAllBytes($source)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    } else {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    }

    $text = $text.Replace($fromNamespace, $toNamespace)

    # Line endings must be *consistent inside each file*, or Unity warns on every import
    # ("Some are Mac OS X (UNIX) and some are Windows") and stack traces get wrong lines.
    # Convention in this project: every mod .cs is LF (checked: all of Mods/ is LF), so
    # normalize to LF and keep the header LF as well.
    $text = $text -replace "`r`n", "`n"
    $text = $text -replace "`r", "`n"
    $out  = ($header -join "`n") + "`n" + $text

    $target = Join-Path $ModCore $name
    if ($Check) {
        if (-not (Test-Path -LiteralPath $target)) { throw "missing port: $target" }
        $actual = [System.IO.File]::ReadAllText($target).Replace("`r`n", "`n")
        if ($actual -cne $out) { throw "source drift: $name (run Tests~/SyncFaceModCore.ps1)" }
        Write-Output ("identical " + $name)
        continue
    }
    [System.IO.File]::WriteAllText($target, $out, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ("synced " + $name)
}
