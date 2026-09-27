# filter-takes.ps1 -- 把若干语料合并，并**按标签子串剔除**某些段（比如"鼓嘴"那几组：
# 它们被形态门关着，混进"撇嘴 vs 非撇嘴"的统计里会把 mouthLeft/Right 顶到 0.87，结论就废了）。
#
# 用法：
#   pwsh -File Tools~/FaceTracking/filter-takes.ps1 -In takes-roll.txt,takes-bite.txt -Out takes-x.txt -Exclude ex.txt
# -Exclude 文件：UTF-8，一行一个"标签子串"（命中就整段丢掉）。
param(
    [Parameter(Mandatory = $true)][string[]] $In,
    [Parameter(Mandatory = $true)][string] $Out,
    [string] $Root = 'D:\Unity_Fork\HoUnityTools\.research',
    [string] $Exclude = ''
)
$ErrorActionPreference = 'Stop'
$drop = @()
if ($Exclude -ne '' -and (Test-Path -LiteralPath $Exclude)) {
    $drop = @([IO.File]::ReadAllLines($Exclude) | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object { $_.Trim() })
}

$sb = New-Object System.Text.StringBuilder
$kept = 0; $skipped = 0
foreach ($name in $In) {
    $path = Join-Path $Root $name
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $lines = [IO.File]::ReadAllLines($path)
    $i = 0
    while ($i -lt $lines.Count) {
        if ($lines[$i] -notmatch '^===\s*(.+?)\s*$') { $i++; continue }
        $label = $Matches[1]
        $j = $i + 1
        while ($j -lt $lines.Count -and $lines[$j] -notmatch '^===\s') { $j++ }
        $skip = $false
        foreach ($d in $drop) { if ($label.Contains($d)) { $skip = $true; break } }
        if ($skip) { $skipped++ } else {
            for ($k = $i; $k -lt $j; $k++) { [void]$sb.AppendLine($lines[$k]) }
            [void]$sb.AppendLine('')
            $kept++
        }
        $i = $j
    }
}
[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
'kept ' + $kept + ' takes, skipped ' + $skipped + ' -> ' + $Out
