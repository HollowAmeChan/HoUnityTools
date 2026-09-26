# sync-modcore.ps1
#
# Copies the face middle-layer runtime sources from the HoUnityTools package into the
# Warudo mod workspace, so the editor panel and the Warudo runtime evaluate the *same*
# code. Only the namespace changes.
#
# Why a script and not a symlink/junction: Unity's asset database does not follow
# junctions reliably, and the two projects are separate Unity projects.
#
# ASCII-only on purpose: PowerShell 5.1 reads .ps1 as ANSI unless there is a BOM, so any
# non-ASCII byte in this file becomes mojibake. The Chinese explanation lives in
# Mods-Ho/HoFaceTracking/Core/PORTED.md inside the mod workspace.

param(
    [string] $PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $ModCore     = 'D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests\Mods-Ho\HoFaceTracking\Core',
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

# source (in the package) -> destination (in the mod). Same file name.
$files = @(
    'HoFaceOutputTable.cs',       # sequential reads and in-place output overrides
    'HoFaceExpression.cs',        # the expression evaluator (parse once, evaluate per frame)
    'HoFaceMiddleware.cs',        # row objects, curve transfer, modifier kinds (no built-in defaults: deleted)
    'HoFaceProfile.cs',           # the *.hoface.json format: name + entry points
    'HoJson.cs',                  # the shared minimal JSON reader (JsonUtility drops nested lists)
    'HoVtsPacket.cs',             # VTS iPhone wire format: request builder + payload parser (pure, testable)
    'HoFaceProfileJson.cs',       # our own JSON reader/writer (JsonUtility drops the list fields in a player)
    'HoFaceTrackingChannels.cs',  # the 52 canonical ARKit names
    'HoFaceNaming.cs',            # parameter naming rules
    # The dynamic-parameter Hub (single component since 2026-09-27). The CHARACTER PREFAB carries it, and
    # that prefab is authored in a mod workspace -- so both assemblies need the SAME type, byte-identical.
    # It only uses UnityEngine (MonoBehaviour + List<struct>); verified to compile and lint clean inside a
    # mod assembly. NOTE: deliberately NO #if UNITY_EDITOR inside it -- this script only prepends a header
    # and rewrites the namespace, so conditional blocks diverge.
    # 2026-09-26: the vocabulary became a COMPONENT (Connector), not a ScriptableObject asset -- there is
    # no .asset file anymore; the slot TABLE was deleted too (whoever writes claims the slot by name).
    # 2026-09-27: the Connector itself is GONE (it only re-pointed at the hub once the table was gone),
    # and the hub is now ONE list of (key, value) tuples instead of two parallel arrays (the curve path
    # that needed `float[] values` was deleted 2026-09-26).
    'HoFaceSemanticHub.cs'              # the (key, value) list: opened by whoever writes, read/written by name
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
