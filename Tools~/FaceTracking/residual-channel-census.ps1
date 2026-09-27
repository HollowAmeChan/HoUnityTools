# residual-channel-census.ps1 -- per-action-group census of the mouth channels that matter for
# "corner four blocks / upper-lip spread / lower-lip spread / side-pout" (2026-09-28).
#
# Reads the 5-second take corpora (text dumps from the panel) and, for each action group
# (label minus its trailing index), prints the per-take AVERAGE of the channels of interest as a
# min~max range across the takes of that group. Pure data, no interpretation, ASCII script.
#
# Usage: pwsh -File Tools~/FaceTracking/residual-channel-census.ps1 [-Out <file>]
param(
    [string] $Root = 'D:\Unity_Fork\HoUnityTools\.research',
    [string] $Out  = 'D:\Unity_Fork\HoUnityTools\.research\residual-channel-census.md',
    [string[]] $Corpora = @()      # 留空 = 用下面那份默认清单；也可以只喂一批（例如新录的苦+张嘴）
)

$ErrorActionPreference = 'Stop'
# 个别语料（合并文件）会重复同一段，用"标签+第一行统计"去重
if ($Corpora.Count -eq 0) {
    $Corpora = @('takes.txt', 'takes-cheek.txt', 'takes-roll.txt', 'takes-roll2.txt', 'takes-bite.txt', 'takes-bite2.txt', 'takes-cat3.txt')
}

$channels = @(
    'mouthSmileLeft', 'mouthSmileRight', 'mouthFrownLeft', 'mouthFrownRight',
    'mouthDimpleLeft', 'mouthDimpleRight', 'mouthStretchLeft', 'mouthStretchRight',
    'mouthUpperUpLeft', 'mouthUpperUpRight', 'mouthLowerDownLeft', 'mouthLowerDownRight',
    'mouthPressLeft', 'mouthPressRight', 'mouthShrugUpper', 'mouthShrugLower',
    'mouthLeft', 'mouthRight', 'mouthRollUpper', 'mouthRollLower',
    'mouthPucker', 'mouthFunnel', 'mouthClose', 'jawOpen', 'cheekPuff'
)

# 段 -> @{ 通道 = @(各段的 avg) }
$groups = @{}
$seenTakes = @{}
foreach ($file in $Corpora) {
    $path = Join-Path $Root $file
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $label = $null
    $skipThisTake = $false
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -match '^===\s*(.+?)\s*$') { $label = $Matches[1]; continue }
        # ⚠️ **段尾那行也要匹配到 `面捕统计]`**（`[Ho 面捕统计] 完（…）`）—— 第一版没排除它，于是每段
        #    被算成两段（且同一标签的段尾行文本相同 ⇒ 去重后每组只多 1），"N 段"那栏因此虚高（值仍对）。
        if ($line -match '面捕统计\]\s*完') { continue }
        if ($line -match '面捕统计\]') {

            # ⚠️ 去重必须在**段**这一层做（合并语料里同一段会出现两次）；
            #    上一版把 key 判在每一行上，于是每段只记下第一根通道。
            $key = $label + '|' + $line
            if ($seenTakes.ContainsKey($key)) { $skipThisTake = $true } else { $seenTakes[$key] = $true; $skipThisTake = $false }
            continue
        }
        if ($skipThisTake) { continue }
        if ($line -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)\s+(-?[0-9.]+)') { continue }
        if ($null -eq $label) { continue }
        $name = $Matches[1]
        if ($channels -notcontains $name) { continue }
        $group = ($label -replace '\d+$', '')
        if (-not $groups.ContainsKey($group)) { $groups[$group] = @{} }
        if (-not $groups[$group].ContainsKey($name)) { $groups[$group][$name] = New-Object System.Collections.Generic.List[double] }
        $groups[$group][$name].Add([double]$Matches[3])   # avg 列
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 嘴角四块 / 唇外扩 / 撇嘴 —— 通道普查（每段的段内均值，跨段取 min~max）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('（脚本：`Tools~/FaceTracking/residual-channel-census.ps1`；只统计动过的行 —— 面板会省略"波动 ≤0.01 且 |均值| ≤0.01"的行，')
[void]$sb.AppendLine('所以"没列出来"就等于"那一项 ≈ 0"。）')
[void]$sb.AppendLine('')
foreach ($group in ($groups.Keys | Sort-Object)) {
    [void]$sb.AppendLine('## ' + $group)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 通道 | 段内均值 min~max（' + $groups[$group].Values[0].Count + ' 段） |')
    [void]$sb.AppendLine('| --- | --- |')
    foreach ($name in $channels) {
        if (-not $groups[$group].ContainsKey($name)) { continue }
        $vals = $groups[$group][$name]
        $lo = ($vals | Measure-Object -Minimum).Minimum
        $hi = ($vals | Measure-Object -Maximum).Maximum
        [void]$sb.AppendLine(('| `{0}` | {1:F4} ~ {2:F4} |' -f $name, $lo, $hi))
    }
    [void]$sb.AppendLine('')
}
[IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
'census -> ' + $Out
'组数：' + $groups.Count + ' · 段数（去重后）：' + $seenTakes.Count
