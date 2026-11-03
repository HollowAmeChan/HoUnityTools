$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UnityScriptCopy.ps1')
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$scratch = Join-Path $package ('Temp~\UnityMetadataChecks\' + [guid]::NewGuid().ToString('N'))
$destination = Join-Path $scratch 'Imported'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$source = Join-Path $scratch 'Fixture.cs'
Set-Content -LiteralPath $source -Value 'public struct Fixture {}'
$checks = 0
foreach ($bad in @('1c35400d22ed410a9d5261e9580158fcb', ('g' * 32), '', ('a' * 31))) {
    Set-Content -LiteralPath ($source + '.meta') -Value ("fileFormatVersion: 2`nguid: " + $bad)
    try {
        Copy-HoUnityScript -Source $source -Destination $destination
        throw 'Malformed metadata was accepted.'
    } catch {
        if ($_.Exception.Message -notlike 'Invalid Unity script GUID*') { throw }
    }
    if (Test-Path -LiteralPath (Join-Path $destination 'Fixture.cs')) { throw 'Invalid metadata copied a partial script.' }
    $checks++
}
$guid = [guid]::NewGuid().ToString('N')
Set-Content -LiteralPath ($source + '.meta') -Value ("fileFormatVersion: 2`nguid: " + $guid)
Copy-HoUnityScript -Source $source -Destination $destination
if ((Get-HoUnityScriptGuid -Source (Join-Path $destination 'Fixture.cs')) -ne $guid) { throw 'Copy changed the source GUID.' }
if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination 'Fixture.cs')).Hash) {
    throw 'Copy changed the script contents.'
}
if ((Get-FileHash -LiteralPath ($source + '.meta')).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $destination 'Fixture.cs.meta')).Hash) { throw 'Copy changed the metadata contents.' }
$checks += 3
Write-Output "HO_UNITY_SCRIPT_COPY PASS checks=$checks"
