# fix-bom.ps1 -- re-add the UTF-8 BOM to Tools~/FaceTracking/*.ps1 files that lost it.
#
# WHY: the `edit` / `write` tools write UTF-8 without a BOM, and PowerShell 5.1 reads a .ps1
# as ANSI when there is no BOM -- Chinese comments then turn into mojibake and the script
# fails to PARSE (not just prints garbage). This has bitten the harness three times.
# This file is deliberately ASCII-only so it parses no matter how it was written.
#
# Usage: pwsh -File Tools~/FaceTracking/fix-bom.ps1 [-Path <dir>]
param([string] $Path = (Join-Path $PSScriptRoot '.'))

$fixed = 0; $ok = 0
foreach ($f in Get-ChildItem -LiteralPath $Path -Filter '*.ps1' -File) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) { $ok++; continue }
    $new = New-Object byte[] ($b.Length + 3)
    [Array]::Copy([byte[]](0xEF, 0xBB, 0xBF), 0, $new, 0, 3)
    [Array]::Copy($b, 0, $new, 3, $b.Length)
    [System.IO.File]::WriteAllBytes($f.FullName, $new)
    $fixed++
    Write-Output ("BOM re-added: " + $f.Name)
}
Write-Output ("ps1 files: " + ($fixed + $ok) + "  fixed=" + $fixed + "  already-ok=" + $ok)
