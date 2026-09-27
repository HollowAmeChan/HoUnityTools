# extract-takes-from-log.ps1 -- 从 Unity 的 Editor.log 里把 `[Ho 面捕统计]` 的段子捞出来。
#
# 为什么要这个：用户录完是在 Unity 控制台里（面板的「记 5 秒」直接 Debug.Log），
# 以前是靠手动复制；这份脚本改成直接从 Editor.log 里按"最后一个块"往回取 N 段。
#
# ⚠️ Unity 开着的时候 Editor.log 是被独占写的 ⇒ 必须 FileShare.ReadWrite 才能读。
# ⚠️ 日志会滚动（老块会消失），所以"捞晚了"就真没了 —— 本脚本只读不删。
#
# 用法：
#   pwsh -File Tools~/FaceTracking/extract-takes-from-log.ps1                 # 报告最近 30 段
#   pwsh -File Tools~/FaceTracking/extract-takes-from-log.ps1 -Count 18 -Out .research/takes-kubite.raw.txt
param(
    [string] $Log = (Join-Path $env:LOCALAPPDATA 'Unity\Editor\Editor.log'),
    [int] $Count = 30,
    [string] $Out = '',
    [string] $Marker = '配置输入行'      # 只要输入行那批（原始设备线）
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Log)) { throw ("找不到日志：" + $Log) }

# FileShare.ReadWrite：Unity 正开着也能读
$fs = New-Object System.IO.FileStream($Log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
$text = $sr.ReadToEnd()
$sr.Close(); $fs.Close()
"日志 " + [math]::Round($text.Length / 1MB, 2) + " MB"

$lines = $text -split "`r?`n"
$blocks = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -notmatch '\[Ho 面捕统计\]' -or $lines[$i] -notmatch [regex]::Escape($Marker)) { continue }
    # 块的结尾 = `[Ho 面捕统计] 完` 那一行（含）—— 之后的堆栈/文件名行不要
    $end = $i
    for ($j = $i; $j -lt [Math]::Min($i + 200, $lines.Count); $j++) {
        if ($lines[$j] -match '\[Ho 面捕统计\] 完') { $end = $j; break }
    }
    $stamp = ([regex]::Match($lines[$i], '\d\d:\d\d:\d\d')).Value
    $moved = ([regex]::Match($lines[$i], '动过\s*(\d+)')).Groups[1].Value
    $blocks.Add([pscustomobject]@{ Start = $i; End = $end; Stamp = $stamp; Moved = $moved; Text = ($lines[$i..$end] -join "`r`n") })
}

"捞到 $($blocks.Count) 段（Marker=$Marker）"
$take = if ($blocks.Count -gt $Count) { $blocks[($blocks.Count - $Count)..($blocks.Count - 1)] } else { $blocks }
$take | ForEach-Object { "  $($_.Stamp)  动过 $($_.Moved) 行  ($($_.Text.Length) 字符)" }

if ($Out -ne '' -and $take.Count -gt 0) {
    $sb = New-Object System.Text.StringBuilder
    # ⚠️ **不要**自己加 ===  行：label-takes.ps1 负责插标签，两边都加会变成每段两条标签
    foreach ($b in $take) { [void]$sb.AppendLine($b.Text); [void]$sb.AppendLine('') }
    [IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    "写到 -> $Out（$($take.Count) 段）"
}
