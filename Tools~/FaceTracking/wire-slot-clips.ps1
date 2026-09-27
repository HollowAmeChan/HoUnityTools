# wire-slot-clips.ps1 -- 扫一遍工程里"与槽位同名"的片段，按名字挂进对应的叶子 Direct 树
#
# 为什么需要它：叶子 Direct 树生成出来是**空槽**（一个 m_Motion: {fileID: 0} 的孩子，权重已挂 W/One）。
# 你把状态动画命名成**跟槽位同名**（名字就取 HoSlotNames.psd1 里的词，例如 `嘴平闭` / `眼喜睁`），
# 这个脚本扫一遍工程把 GUID 填进那个空槽 —— 幂等：已经挂了内容的槽**跳过不覆盖**。
#
# 用法：
#   powershell -File Tools~/FaceTracking/wire-slot-clips.ps1              # dry run（只报）
#   powershell -File Tools~/FaceTracking/wire-slot-clips.ps1 -Apply       # 真写
param(
    [string] $Controller = 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\PTP_CTR_Face_VTS.controller',
    [string] $SearchRoot = 'D:\Unity_Project\BREAK_URP\Assets',
    [switch] $Apply
)
$ErrorActionPreference = 'Stop'

# ① 槽位名（唯一来源：HoSlotNames.psd1）
$names = Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'HoSlotNames.psd1')
$slots = @()
foreach ($k in $names.Keys) {
    $tbl = $names[$k]
    if ($tbl.Count -gt 0 -and $tbl[0] -is [System.Array]) { foreach ($c in $tbl) { $slots += $c } }
    else { $slots += $tbl }
}
$slots = @($slots | Where-Object { $_ } | Sort-Object -Unique)
"槽位名 $($slots.Count) 个"

# ② 找同名片段的 GUID
$guidOf = @{}
foreach ($f in Get-ChildItem -LiteralPath $SearchRoot -Recurse -Filter *.anim -ErrorAction SilentlyContinue) {
    if ($slots -notcontains $f.BaseName) { continue }
    if ($guidOf.ContainsKey($f.BaseName)) {
        "  [!] 重名片段：$($f.BaseName)（这一个在 $($f.DirectoryName)，已忽略）"
        continue
    }
    $meta = $f.FullName + '.meta'
    if (-not (Test-Path -LiteralPath $meta)) { "  [!] 没有 .meta：$($f.FullName)"; continue }
    $m = Select-String -LiteralPath $meta -Pattern '^guid: ([0-9a-f]{32})' | Select-Object -First 1
    if (-not $m) { "  [!] meta 里没 guid：$meta"; continue }
    $guidOf[$f.BaseName] = $m.Matches[0].Groups[1].Value
}
"工程里找到的同名片段 $($guidOf.Count) 个"

# ③ 逐棵树：把空槽的 m_Motion 换成片段引用（已经有内容的跳过）
$text = [System.IO.File]::ReadAllText($Controller)
$filled = 0; $already = 0; $noClip = 0; $noTree = 0
foreach ($slot in $slots) {
    if (-not $guidOf.ContainsKey($slot)) { $noClip++; continue }
    $i = $text.IndexOf('m_Name: ' + $slot)
    if ($i -lt 0) { $noTree++; "  [!] 控制器里没有这棵树：$slot"; continue }
    $j = $text.IndexOf('m_Name: ', $i + 1)
    if ($j -lt 0) { $j = $text.Length }
    $block = $text.Substring($i, $j - $i)
    $k = $block.IndexOf('m_Motion: ')
    if ($k -lt 0) { "  [!] 这棵树没有孩子：$slot"; continue }
    $e = $block.IndexOf("`n", $k)
    if ($e -lt 0) { $e = $block.Length }
    $line = $block.Substring($k, $e - $k)
    if ($line -notmatch '\{fileID: 0\}') { $already++; continue }   # 已有内容 ⇒ 不覆盖
    $newLine = 'm_Motion: {fileID: 7400000, guid: ' + $guidOf[$slot] + ', type: 2}'
    $text = $text.Substring(0, $i + $k) + $newLine + $text.Substring($i + $e)
    $filled++
}
"挂上 $filled · 已有内容跳过 $already · 没有同名片段 $noClip · 控制器里没这棵树 $noTree"

if ($Apply) {
    [System.IO.File]::WriteAllText($Controller, $text, (New-Object System.Text.UTF8Encoding($false)))
    'WROTE ' + $Controller
} else {
    '（dry run；确认数字没问题再加 -Apply）'
}
