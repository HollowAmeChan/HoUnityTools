# list-take-labels.ps1 -- 列出各语料里的段标签（ASCII 脚本，标签是数据里的）
param([string] $Root = 'D:\Unity_Fork\HoUnityTools\.research')
$ErrorActionPreference = 'Stop'
foreach ($f in (Get-ChildItem -LiteralPath $Root -Filter 'takes*.txt' | Sort-Object Name)) {
    if ($f.Name -like '*-raw*' -or $f.Name -like 'takes-all*' -or $f.Name -eq 'takes-sample.txt') { continue }
    $labels = New-Object System.Collections.Generic.List[string]
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        if ($line -match '^===\s*(.+?)\s*$') { $labels.Add($Matches[1]) }
    }
    if ($labels.Count -eq 0) { continue }
    ($f.Name + ' (' + $labels.Count + '): ' + (($labels | Sort-Object -Unique) -join ' / '))
}
