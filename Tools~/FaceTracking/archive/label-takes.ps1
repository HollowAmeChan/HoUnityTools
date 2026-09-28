# label-takes.ps1 -- insert `=== <label>` lines into a raw "record 5 s" dump, one label per take.
#
# WHY a script: the panel's Console dump has no labels (it cannot know what the person was doing);
# the analyzer groups takes by the `=== label` line. Labels come from a UTF-8 payload file so this
# script stays pure ASCII (PS 5.1 reads a BOM-less .ps1 as ANSI -> Chinese literals would break it).
param(
    [Parameter(Mandatory = $true)][string] $Raw,
    [Parameter(Mandatory = $true)][string] $Labels,
    [Parameter(Mandatory = $true)][string] $Out,
    [int] $PerGroup = 1
)

$ErrorActionPreference = 'Stop'
$header = [regex]'^\[[^\]]+\] \d\d:\d\d:\d\d'
# ⚠️ 局部变量别叫 `$labels` / `$out` —— PowerShell 变量名**大小写不敏感**，会和 `$Labels` / `$Out`
# 这两个 `[string]` 参数撞名：数组被静默强制成字符串（`.Count` = 1 ⇒ "more takes than labels"）。
$labelList = @([System.IO.File]::ReadAllLines($Labels) | Where-Object { $_.Trim().Length -gt 0 })
$lines = [System.IO.File]::ReadAllLines($Raw)

$list = New-Object System.Collections.Generic.List[string]
$take = 0
foreach ($line in $lines) {
    if ($header.IsMatch($line)) {
        if ($take % $PerGroup -eq 0) {
            $index = [int]($take / $PerGroup)
            if ($index -ge $labelList.Count) { throw ('more takes than labels: ' + $index) }
            $list.Add('')
            $list.Add('=== ' + $labelList[$index])
        }
        $take++
    }
    $list.Add($line)
}
if ($take -eq 0) { throw 'no record blocks found in the raw dump' }
# `-PerGroup 3` = 每组一个标签（8 条），`-PerGroup 1` = 每段一个标签（24 条）；两种都合法 ⇒ 只报数
Write-Output ('takes=' + $take + ' groups=' + [Math]::Ceiling($take / $PerGroup) + ' labels=' + $labelList.Count)
[System.IO.File]::WriteAllLines($Out, $list.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('takes=' + $take + ' -> ' + $Out)
