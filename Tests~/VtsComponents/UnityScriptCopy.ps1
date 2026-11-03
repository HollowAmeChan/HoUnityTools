function Get-HoUnityScriptGuid {
    param([Parameter(Mandatory=$true)][string] $Source)
    $meta = $Source + '.meta'
    if (-not (Test-Path -LiteralPath $meta -PathType Leaf)) {
        throw "Missing Unity script metadata: $meta"
    }
    $text = Get-Content -LiteralPath $meta -Raw
    $guidMatches = [regex]::Matches($text, '(?m)^guid:[ \t]*(\S+)[ \t]*\r?$')
    if ($guidMatches.Count -ne 1 -or $guidMatches[0].Groups[1].Value -notmatch '^[0-9a-f]{32}$') {
        throw "Invalid Unity script GUID (expected exactly 32 hexadecimal digits): $meta"
    }
    return $guidMatches[0].Groups[1].Value
}

function Copy-HoUnityScript {
    param(
        [Parameter(Mandatory=$true)][string] $Source,
        [Parameter(Mandatory=$true)][string] $Destination
    )
    [void](Get-HoUnityScriptGuid -Source $Source)
    Copy-Item -LiteralPath $Source -Destination $Destination
    Copy-Item -LiteralPath ($Source + '.meta') -Destination $Destination
}
