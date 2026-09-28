# check-controller.ps1 -- 对着设计稿核一份手搭出来的 .controller
#
# 为什么需要它：中间层写参数那一步是"按控制器声明的口逐个查字典"，
# **名字敲错一个字符 = 那条值被静默丢掉**（面板只会说"不在控制器里"）。
# 所以参数名/默认值/门控接线这类东西值得机器核一遍，别靠眼睛。
#
# 用法：
#   powershell -File Tools~/FaceTracking/check-controller.ps1 -Path <x.controller>
#
# 退出码：0 = 参数与结构都过；1 = 有问题（逐条打出来）。
param(
    # 隔离模式：只查这份控制器**有**的东西（DIAG_* 那种单区控制器天然缺树/缺参数）。
    # 「缺」降级成注；树类型 / 轴接线 / 刻度与逐格坐标 / 槽位名 / 变体镜像 / WD / Normalize 照旧严格。
    [switch] $Isolation,
    [Parameter(Mandatory = $true)][string] $Path,
    [string] $Profile = 'D:\Unity_Fork\HoUnityTools\Editor\FaceTracking\Profiles\ho-iPhoneVTS.hoface.json',
    [string] $ClipFolder = 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\Animations'
)

$ErrorActionPreference = 'Stop'

# ── 外部片段表：槽位现在是"以槽位名命名的空 .anim"（控制器按 GUID 引用）──
# 这个脚本没有资产数据库，所以自己扫一遍 .anim.meta 的 guid 与 .anim 的 m_Name，
# 这样槽位名/坐标的核对才能真的跑到（否则子节点只会显示成"空 Motion"）。
$clipNames = @{}
if (Test-Path -LiteralPath $ClipFolder) {
    foreach ($meta in Get-ChildItem -LiteralPath $ClipFolder -Filter *.anim.meta) {
        $metaText = [System.IO.File]::ReadAllText($meta.FullName)
        $m = [regex]::Match($metaText, 'guid:\s*([0-9a-fA-F]{32})')
        if (-not $m.Success) { continue }
        $animPath = $meta.FullName.Substring(0, $meta.FullName.Length - 5)   # 去掉 .meta
        if (-not (Test-Path -LiteralPath $animPath)) { continue }
        $animText = [System.IO.File]::ReadAllText($animPath)
        $n = [regex]::Match($animText, '(?m)^\s*m_Name:\s*(.+?)\s*$')
        if ($n.Success) { $clipNames[$m.Groups[1].Value.ToLowerInvariant()] = $n.Groups[1].Value }
    }
}

# ── 期望值：43 个参数 = profile 里那 40 行 `Ho/Drive/*` + 1 个恒 1 权重 + 2 个表情门
#    （⚠️ 后三个**故意不写进 profile**：`W/One` 是"永远全量生效"的接线，两个 `Gate/Expr/*`
#     是按键表情副本的开关，都还不属于中间层的契约）──
#    2026-09-28 傍晚 profile 里有 40 行：30 轴 + 5 门（4 区域 + 1 形态门 `Gate/MouthStyle`）
#    + 4 切片权重 + 1 形态权重（`Ho/Drive/Style/InvertedV`）。默认值：门 = 1、其余 = 0。
#    （上午删掉 `Mouth/X`（§5.7.22）→ 下午重建它并加过嘴角那两根（§5.7.23）→ 傍晚删掉嘴角那两根、
#     `MouthWidth` 以 1D 3 格回来（§5.7.24）。）
$script:slotNames = Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'HoSlotNames.psd1')
$expected = @{}
$prof = Get-Content -LiteralPath $Profile -Encoding UTF8 -Raw | ConvertFrom-Json
foreach ($row in $prof.outputs) {
    $n = $row.parameter
    if ($n -notlike 'Ho/Drive/*') { continue }
    # 区域门 = 1；其余（轴 / 切片权重）= 0
    $expected[$n] = if ($n -like 'Ho/Drive/Gate/*' -and $n -notlike 'Ho/Drive/Gate/Expr/*') { 1.0 } else { 0.0 }
}
# Direct 的每个子节点都要挂参数 ⇒ "这一格永远全量生效"也得有个参数（默认 1，没人写）
$expected['Ho/Drive/W/One'] = 1.0

$regions = @('Mouth', 'Eye', 'Brow', 'Nose')

# 刻度比较用的小工具：PS 5.1 里 `[double]$_ - $v` 这种紧挨着的写法会被错误分词，所以统一走函数。
function HasNearValue($list, [double]$value) {
    foreach ($item in $list) { if ([math]::Abs([double]$item - $value) -lt 0.0001) { return $true } }
    return $false
}

# 期望的树（tranche 1）：名字 -> 类型 + X/Y 参数（Direct 只写类型）
$expectedTrees = [ordered]@{
    'Ho/00 Drive Tree'  = @{ type = 'Direct' }
    'MouthRegion'       = @{ type = 'Direct' }
    'EyeRegion'         = @{ type = 'Direct' }
    'NoseRegion'        = @{ type = 'Direct' }
    'MouthCore'         = @{ type = 'FreeformCartesian2D'; x = 'Ho/Drive/Mouth/Form';  y = 'Ho/Drive/Mouth/Open' }
    # 下巴：X = 左右（JawSide）× Y = 上下（Jaw，含 mouthClose 的咬合侧，钳到 0）= 6 格
    # 下巴（2026-09-28 深夜）：**从 2D 6 格降成 1D 2 格**（咬合/闭 ↔ 张开）—— 见下面 `$simple1DSlot`。
    # ⚠️ `Ho/Drive/Mouth/JawSide` 照旧声明（出口），但**没有树消费**。
    'NoseUp'            = @{ type = 'Simple1D'; blend = 'Ho/Drive/Nose/Up' }

    # 整嘴平移（2026-09-28 下午新建、傍晚收成 3×2）：X = 左右 ±0.95 · Y = 上下 0 / +1（负侧钳到 0）
    'MouthShift'        = @{ type = 'FreeformCartesian2D'; x = 'Ho/Drive/Mouth/X'; y = 'Ho/Drive/Mouth/Y' }
    # 卷唇：**轴驱动的变体开关**（不是残差表）—— 开关在 静息嘴 / 猫嘴版 两张整嘴表之间分叉
    'MouthCoreRollSwitch' = @{ type = 'Simple1D'; blend = 'Ho/Drive/Style/CatMouth' }
    'MouthCoreRoll'     = @{ type = 'FreeformCartesian2D'; x = 'Ho/Drive/Mouth/Form';  y = 'Ho/Drive/Mouth/Open' }
    # ⭐ 舌头（2026-09-28 深夜重做成 **1D 5 格**）：默认（不出舌）+ 沿对角线 4 步，轴 = `Mouth/TongueL`。
    #    ⚠️ 别做成 2D 表：那 5 个点在 2D 里共线（退化点集实测不可预测、会出负权重）。
    'MouthTongue'       = @{ type = 'Simple1D'; blend = 'Ho/Drive/Mouth/TongueL' }
    'LidL'              = @{ type = 'Direct' }
    'LidR'              = @{ type = 'Direct' }
    # 副本表（1D 开关的两个孩子之一）：轴与主版一致，只是姿势不同。⚠️ **嘴没有副本**
    # （2026-09-27 删掉 `MouthCoreExpr` 与 `MouthCoreSwitch`：夸张的笑嘴 = `Form` 更大，轴上够得到）
    # 风格化形态（2026-09-27 加）：两条都是"一个固定姿势"，权重来自中间层的契约行 ——
    #   · `InvertedV`：1D，阈值 0 / 1（形态门是迟滞出来的 0/1），两格 = 中性 / 倒V 姿势
    #   · `Cheek`：2D 颊轴（左 × 右），4 格 = 都不鼓 / 只左 / 只右 / 双边
    'InvertedV'         = @{ type = 'Simple1D'; blend = 'Ho/Drive/Style/InvertedV' }
    # 嘴宽（2026-09-28 傍晚：`MouthWidth` 以 1D 3 格回来）—— 轴是**既有的** `Ho/Drive/Mouth/Pucker`
    'MouthWidth'        = @{ type = 'Simple1D'; blend = 'Ho/Drive/Mouth/Pucker' }
    'Cheek'             = @{ type = 'FreeformCartesian2D'; x = 'Ho/Drive/Cheek/Left/Puff'; y = 'Ho/Drive/Cheek/Right/Puff' }
}

# ── 解析 .controller（YAML 文本；只需要参数表、层状态与混合树）──────────────────
$text = Get-Content -LiteralPath $Path -Encoding UTF8 -Raw
$chunks = $text -split "(?m)^--- !u!"
$docs = @{}
foreach ($c in $chunks) {
    # ⚠️ id 可以是**负数**（Unity 里子资产的 64 位局部 id 就是负的：老控制器那两个状态机就是）
    if ($c -notmatch '^(-?\d+) &(-?\d+)') { continue }
    $docs[$Matches[2]] = [pscustomobject]@{ Class = $Matches[1]; Text = $c }
}
function Unquote([string]$v) {
    # Unity 把非 ASCII 的名字写成 **YAML 双引号标量 + \uXXXX 转义**（`m_Name: "\u5634\u5DE6\u79FB"`）——
    # 那是合法 YAML，Unity 自己会解成「嘴左移」⇒ 检查器不解就会把每个中文槽位名都判成
    # 「不在清单里」。2026-09-29 修：之前那 28 条假抱怨（槽位名 + 逐格移位找不到）全出在这里。
    if ($null -eq $v) { return $v }
    $s = $v.Trim()
    if ($s.Length -ge 2 -and $s.StartsWith('"') -and $s.EndsWith('"')) {
        $s = $s.Substring(1, $s.Length - 2)
        $s = [regex]::Replace($s, '\\u([0-9A-Fa-f]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
        $s = $s.Replace('\"', '"').Replace('\n', "`n").Replace('\\', '\')
    }
    return $s
}
function Field([string]$t, [string]$name) {
    $m = [regex]::Match($t, "(?m)^\s*${name}:\s*(.*)$")
    if ($m.Success) { Unquote $m.Groups[1].Value } else { $null }
}
$blendNames = @{ '0' = 'Simple1D'; '1' = 'SimpleDirectional2D'; '2' = 'FreeformDirectional2D'; '3' = 'FreeformCartesian2D'; '4' = 'Direct' }

$problems = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

# 参数表
$declared = @{}
$machines = @()
foreach ($id in $docs.Keys) {
    $t = $docs[$id].Text
    if ($docs[$id].Class -eq '91') {
        foreach ($m in [regex]::Matches($t, '(?ms)- m_Name:\s*(\S+)\s*\n\s*m_Type:\s*(\d+)\s*\n\s*m_DefaultFloat:\s*(\S+)')) {
            $declared[$m.Groups[1].Value] = @{ Type = $m.Groups[2].Value; Default = [double]$m.Groups[3].Value }
        }
        if ($t -match 'm_AnimatorParameters:\s*\[\s*\]') { $notes.Add('参数表是空的') }
    }
    if ($docs[$id].Class -eq '1107') { $machines += $docs[$id] }
}

# 参数核对
foreach ($n in ($expected.Keys | Sort-Object)) {
    if (-not $declared.ContainsKey($n)) { if ($Isolation) { $notes.Add("缺参数：$n") } else { $problems.Add("缺参数：$n") }; continue }
    if ($declared[$n].Type -ne '1') { $problems.Add("参数 $n 不是 Float（type=$($declared[$n].Type)）") }
    if ([Math]::Abs($declared[$n].Default - $expected[$n]) -gt 0.0001) {
        $problems.Add("参数 $n 默认值应为 $($expected[$n])，实际 $($declared[$n].Default)")
    }
}
$extra = $declared.Keys | Where-Object { -not $expected.ContainsKey($_) }
if ($extra) { $notes.Add('控制器里还有别的参数（不一定是错，可能给别的来源用）：' + ($extra -join ', ')) }

# Behaviour / Write Defaults
foreach ($m in $machines) {
    $t = $m.Text
    $beh = [regex]::Matches($t, '(?ms)m_Behaviours:\s*\n((?:\s*-\s*\{fileID: 0\}\s*\n)*)')
    foreach ($b in $beh) { if ($b.Groups[1].Value -notmatch 'fileID: 0') { $problems.Add('状态机上有 Behaviour（Compile 会拒绝）') } }
}
if ($text -match 'm_StateMachineBehaviours:\s*\n\s*-') { $problems.Add('控制器里有 StateMachineBehaviour（Compile 会拒绝）') }

# 每个状态：**动作里含 Direct 树时 WD 必须开**（引擎语义：WD 关 + Direct = 逐帧发散，实测 98.98 → 246.28 → 1059.33）
$stateCount = 0
foreach ($id in $docs.Keys) {
    if ($docs[$id].Class -ne '1102') { continue }
    $stateCount++
    $t = $docs[$id].Text
    $wd = Field $t 'm_WriteDefaultValues'
    $motion = Field $t 'm_Motion'
    $fid = if ($motion -match 'fileID:\s*(-?\d+)') { $Matches[1] } else { $null }
    if ($fid -eq '0') { $fid = $null }
    $containsDirect = $false
    if ($fid -and $docs.ContainsKey($fid)) {
        $queue = New-Object System.Collections.Generic.Queue[string]
        $queue.Enqueue($fid)
        while ($queue.Count -gt 0) {
            $cur = $queue.Dequeue()
            if (-not $docs.ContainsKey($cur)) { continue }
            $ct = $docs[$cur].Text
            if ((Field $ct 'm_BlendType') -eq '4') { $containsDirect = $true; break }
            foreach ($mm in [regex]::Matches($ct, '(?m)m_Motion:\s*\{fileID:\s*(-?\d+)')) {
                if ($mm.Groups[1].Value -ne '0') { $queue.Enqueue($mm.Groups[1].Value) }
            }
        }
    }
    if ($containsDirect -and $wd -ne '1') {
        $problems.Add("状态「$((Field $t 'm_Name'))」的动作里有 Direct 树，但 Write Defaults = $wd（必须 1，否则逐帧发散）")
    }
}
if ($stateCount -eq 0) { $notes.Add('这份文件里没有 AnimatorState（不像是控制器资产？）') }

# 混合树核对
$trees = @{}
foreach ($id in $docs.Keys) {
    if ($docs[$id].Class -ne '206') { continue }
    $t = $docs[$id].Text
    $name = Field $t 'm_Name'
    $children = @()
    foreach ($cb in [regex]::Matches($t, '(?ms)^  - serializedVersion:.*?(?=^  - serializedVersion:|\z)')) {
        $ct = $cb.Value
        $motion = Field $ct 'm_Motion'
        $fid = if ($motion -match 'fileID:\s*(-?\d+)') { $Matches[1] } else { $null }
        if ($fid -eq '0') { $fid = $null }
        $childName = if ($fid -and $docs.ContainsKey($fid)) { Field $docs[$fid].Text 'm_Name' } else { '<空 Motion>' }
        # 外部片段（`{fileID: 7400000, guid: …, type: 2}`）：按 GUID 查 $clipNames
        if ($childName -eq '<空 Motion>' -and $motion -match 'guid:\s*([0-9a-fA-F]{32})') {
            $childName = if ($clipNames.ContainsKey($Matches[1].ToLowerInvariant())) { $clipNames[$Matches[1].ToLowerInvariant()] } else { '<外部片段?>' }
        }
        $pos = [regex]::Match($ct, 'm_Position:\s*\{\s*x:\s*(-?[0-9.]+),\s*y:\s*(-?[0-9.]+)\s*\}')
        $posX = if ($pos.Success) { [double]$pos.Groups[1].Value } else { [double]::NaN }
        $posY = if ($pos.Success) { [double]$pos.Groups[2].Value } else { [double]::NaN }
        $children += [pscustomobject]@{
            Name      = $childName
            Direct    = Field $ct 'm_DirectBlendParameter'
            Threshold = Field $ct 'm_Threshold'
            PosX      = $posX
            PosY      = $posY
        }
    }
    $trees[$name] = [pscustomobject]@{
        Type = $blendNames[(Field $t 'm_BlendType')]
        X    = Field $t 'm_BlendParameter'
        Y    = Field $t 'm_BlendParameterY'
        Kids = $children
        Normalized = Field $t 'm_NormalizedBlendValues'
    }
}

# ⚠️ **Normalize Blend Values 必须全关**（生成器写 `m_NormalizedBlendValues: 0`）：
#    Direct 树是**加法、不归一化**（实测：同一属性 0.6 + 0.8 → **140**；0.6×100 + 0.8×200 = 220）
#    —— 区域 Direct 的每个孩子权重都是 `W/One` = 1，各条车道（整嘴表 / 嘴角残差 / 下巴 / 舌头…）
#    正是靠**相加**叠成一个姿势的。一旦有人在 Inspector 里把它勾上，孩子会被除以权重和
#    ⇒ **整个区域被摊薄**（MouthRegion 下 4 个孩子会变 0.25）。1D/2D 树上它是空操作（实测逐位相同），
#    所以这里对全部树统一要求 0。
$normalizedOn = @($trees.Keys | Where-Object { $trees[$_].Normalized -ne '0' })
if ($normalizedOn.Count -gt 0) {
    $problems.Add("有树勾了 Normalize Blend Values（应为 0，Direct 靠加法叠车道）：" + ($normalizedOn -join ', '))
}

foreach ($name in $expectedTrees.Keys) {
    $want = $expectedTrees[$name]
    if (-not $trees.ContainsKey($name)) { if ($Isolation) { $notes.Add("缺树：$name（$($want.type)）") } else { $problems.Add("缺树：$name（$($want.type)）") }; continue }
    $got = $trees[$name]
    if ($got.Type -ne $want.type) { $problems.Add("树 $name 类型应为 $($want.type)，实际 $($got.Type)") }
    if ($want.x -and $got.X -ne $want.x) { $problems.Add("树 $name 的 X 参数应为 $($want.x)，实际 $($got.X)") }
    if ($want.y -and $got.Y -ne $want.y) { $problems.Add("树 $name 的 Y 参数应为 $($want.y)，实际 $($got.Y)") }
    if ($want.blend -and $got.X -ne $want.blend) { $problems.Add("树 $name 的 blendParameter 应为 $($want.blend)，实际 $($got.X)") }
    if ($got.Type -eq 'Direct') {
        $noParam = @($got.Kids | Where-Object { [string]::IsNullOrEmpty($_.Direct) -or $_.Direct -eq 'Blend' })
        if ($noParam.Count -gt 0) { $problems.Add("Direct 树 $name 有 $($noParam.Count) 个子节点没挂参数（不挂就不参与混合）") }
    }
}

# ── 可达性（2026-09-27 加，因为真栽过一次）：从根树走一遍，**每棵期望的树都必须走得到**。
# 删掉 `MouthCoreSwitch` 那次忘了把 `MouthCore` 直接挂回区域 ⇒ 那张基础嘴表变成孤儿树
# （8 个片段状态走不到、嘴整块丢姿势），而上面那些检查**一条都不会响**（树在、槽位名也对）。
$reachable = @{}
if ($trees.ContainsKey('Ho/00 Drive Tree')) {
    $queue = New-Object System.Collections.Generic.Queue[string]
    $queue.Enqueue('Ho/00 Drive Tree')
    $reachable['Ho/00 Drive Tree'] = $true
    while ($queue.Count -gt 0) {
        $cur = $queue.Dequeue()
        if (-not $trees.ContainsKey($cur)) { continue }
        foreach ($kid in $trees[$cur].Kids) {
            if (-not $trees.ContainsKey($kid.Name)) { continue }   # 叶子（片段）不是树
            if ($reachable.ContainsKey($kid.Name)) { continue }
            $reachable[$kid.Name] = $true
            $queue.Enqueue($kid.Name)
        }
    }
    # ⚠️ 只报**文件里存在、但从根走不到**的；缺席的树「缺树」那条已经报过（别重复）
    $orphans = @($expectedTrees.Keys | Where-Object { $trees.ContainsKey($_) -and -not $reachable.ContainsKey($_) })
    if ($orphans.Count -gt 0) {
        $problems.Add('这些树从根走不到（孤儿树 ⇒ 姿势整块丢）：' + ($orphans -join ', '))
    }
}

# 区域门：根 Direct 的子节点必须挂 5 个区域门之一
if ($trees.ContainsKey('Ho/00 Drive Tree')) {
    $root = $trees['Ho/00 Drive Tree']
    $usedGates = @($root.Kids | ForEach-Object { $_.Direct } | Where-Object { $_ } | Sort-Object -Unique)
    $missingGates = @($regions | Where-Object { $usedGates -notcontains "Ho/Drive/Gate/$_" })
    if ($missingGates.Count -gt 0) { $notes.Add('根 Direct 没用到这些区域门（可能还没接）：' + ($missingGates -join ', ')) }
}

# ── 区域 Direct 的子节点权重（2026-09-27 加）：**风格化门真正落地的地方**。
# 中间层只把值发出来，压权重这一步在控制器里 —— 接错一个孩子，形态亮起来时"
# 不是整块张嘴笑被关掉，而是某半张脸被关掉"，而上面所有检查都不会响（孩子都在、参数也都存在）。
# `MouthRegion` 里三个孩子（`MouthCoreRollSwitch` / `MouthShift` / `MouthWidth`）挂形态门
#   （2026-09-28 当天反复：三→二→三→傍晚 `MouthCorner` 换成 1D 的 `MouthWidth`，仍是三个）
# `Ho/Drive/Gate/MouthStyle`；`MouthJaw` / `MouthTongue` 与两条形态子树保持恒 1（`Ho/Drive/W/One`）。
$regionChildWeight = [ordered]@{
    'Ho/00 Drive Tree' = [ordered]@{
        'MouthRegion' = 'Ho/Drive/Gate/Mouth'; 'EyeRegion' = 'Ho/Drive/Gate/Eye'
        'NoseRegion' = 'Ho/Drive/Gate/Nose'
    }
    'MouthRegion' = [ordered]@{
        'MouthCoreRollSwitch' = 'Ho/Drive/Gate/MouthStyle'; 'MouthJaw'    = 'Ho/Drive/W/One'
        'MouthShift'          = 'Ho/Drive/Gate/MouthStyle'; 'MouthWidth'  = 'Ho/Drive/Gate/MouthStyle'
        'MouthTongue'         = 'Ho/Drive/W/One';           'InvertedV'   = 'Ho/Drive/W/One'
        'Cheek'               = 'Ho/Drive/W/One'
    }
    'EyeRegion'  = [ordered]@{ 'LidL' = 'Ho/Drive/W/One'; 'LidR' = 'Ho/Drive/W/One' }
    'NoseRegion' = [ordered]@{ 'NoseUp' = 'Ho/Drive/W/One' }
}
foreach ($tree in $regionChildWeight.Keys) {
    if (-not $trees.ContainsKey($tree)) { continue }   # 缺树已经报过
    $wanted = $regionChildWeight[$tree]
    foreach ($kidName in $wanted.Keys) {
        $kid = @($trees[$tree].Kids | Where-Object { $_.Name -eq $kidName })
        if ($kid.Count -ne 1) {
            $msg = "区域 $tree 里找不到子节点 $kidName（或不止一个）"
            if ($Isolation) { $notes.Add($msg) } else { $problems.Add($msg) }
            continue
        }
        if ($kid[0].Direct -ne $wanted[$kidName]) {
            $problems.Add("区域 $tree 的子节点 $kidName 的权重参数应为 $($wanted[$kidName])，实际 $($kid[0].Direct)")
        }
    }
    $extraKids = @($trees[$tree].Kids | Where-Object { $_.Name -ne '<空 Motion>' -and -not $wanted.Contains($_.Name) })
    if ($extraKids.Count -gt 0) {
        $problems.Add("区域 $tree 有清单外的子节点：" + (($extraKids | ForEach-Object { $_.Name }) -join ', '))
    }
}

# 槽位核对：2D 表的子节点必须**按命名规则**叫（清单见设计稿 §10.1；这里只核合规与进度，
# **不要求摆满** —— 契约 §F 允许没摆的格复用基础片段，所以"缺"只报进度不报错）
$slotSpec = [ordered]@{
    # ⚠️ 2026-09-28 傍晚：Form 从三档变**四档**（−1 / 0 / 0.75 / 1，−1 = 苦）⇒ 槽位名 `A4X<i>`、挖掉的格 `(2,2)`
    'MouthCore'   = @{ x = 'Form';     y = 'Open';      a = 4; ys = @(0, 1, 2); skip = @('2,2') }


    # 整嘴平移（2026-09-28 傍晚收成 **3×2 = 6 格**：X 三档 × Y 两档，负侧钳到 0）
    # ⚠️ 2026-09-28 深夜：**挖掉"上移 × 侧移"两格**（用户「同理 shift 的两个角点是不是也能删了」）
    #    —— 删的是 `(0,1)` 左上 / `(2,1)` 右上（噘嘴时嘴居中 ⇒ `Mouth/X` ≈ 0）。下面那排左/右是本体，不能删。
    'MouthShift'  = @{ x = 'LeftRight'; y = 'UpDown';   a = 3; ys = @(0, 1); skip = @('0,1', '2,1') }
    # 舌头（2026-09-28 深夜）：**不在 2D 清单里** —— 它是 1D 5 格，见下面 `$simple1DSlot` 的 `MouthTongue`。
    # 下巴（2026-09-28 深夜用户定）：2D **T 形 4 格** —— 左右只在「张开」那档有真信号，两个角不建
    'MouthJaw'    = @{ x = 'JawSide'; y = 'Jaw'; a = 3; ys = @(0, 1); skip = @('0,0', '2,0') }
    'LidL'        = @{ direct = @('NeutralClosed', 'NeutralOpen', 'NeutralWide', 'HappyClosed', 'HappyOpen', 'AngerClosed', 'AngerOpen', 'AngerWide', 'SadClosed', 'SadOpen', 'SadWide') }
    'LidR'        = @{ direct = @('NeutralClosed', 'NeutralOpen', 'NeutralWide', 'HappyClosed', 'HappyOpen', 'AngerClosed', 'AngerOpen', 'AngerWide', 'SadClosed', 'SadOpen', 'SadWide') }
    # 颊轴（2026-09-27 加）：两根轴各两档 ⇒ 4 格。⚠️ 姿势还没画（现在是空片段），槽位名先钉死。
    'Cheek'       = @{ x = 'PuffL';    y = 'PuffR';     a = 2; ys = @(0, 1) }
}
# 副本表的槽位规则与主版一致（槽位名前缀换成 `<主版>Expr`）。⚠️ 嘴 2026-09-27 起没有副本
$slotCopyOf = [ordered]@{ }
# 变体表（轴驱动的整表分叉）：与主版**同轴、同刻度、同稀疏格**，槽位规则也一样。
# ⚠️ 2026-09-28：试过给猫嘴版"自己再挖 3 格"，**已撤回** —— 常开（直接写 `Mouth/Roll ≥ 0.30`）
#    会把任意 Form × Open 采到，少一格就是嘴塌。变体与主版保持**同一份稀疏格**。
$slotCopyOf['MouthCoreRoll'] = 'MouthCore'
foreach ($c in $slotCopyOf.Keys) { $slotSpec[$c] = $slotSpec[$slotCopyOf[$c]] }

# 1D **片段**表（一根轴、孩子是动画片段）：现在**只有 `NoseUp`**。
# ⚠️ 2026-09-27 起 `MouthForward`（下巴前伸）**不做树了**（用户定：「把这个 arkit 输入贬成只用来
#   辅助的变量，他还是参与 arkit 直通就行，我们直接不做这个轴了，中心放到 jaw 的下左右上」）——
#   轴行 `Ho/Drive/Mouth/Forward` 照旧发布（当出口/辅助变量），但没有树消费它。
# 留着这个清单是因为一维形状还多（命名权威的例子 `MouthShrugBase__Shrug__A3X1`），回来时不用重写。
# ⚠️ 1D 表的阈值必须显式写死；鼻子上顶 / 倒V / 嘴宽（2026-09-28 傍晚加）都是这张表的用户。
$simple1DSlot = [ordered]@{
    'NoseUp'       = @{ t = 'Up';       a = 2; thr = @(0.0, 1.0) }
    # 倒V（2026-09-27 加）：形态门是**迟滞出来的 0/1** ⇒ 两档阈值就是 0 / 1。⚠️ 姿势还没画。
    'InvertedV'    = @{ t = 'InvertedV'; a = 2; thr = @(0.0, 1.0) }
    # 嘴宽（2026-09-28 傍晚）：**1D 3 格**，刻度就是实测的三档（窄 / 中 / 宽），轴 = 既有的 `Mouth/Pucker`
    'MouthWidth'   = @{ t = 'Pucker';    a = 3; thr = @(-0.93, -0.09, 2.0) }
    # 舌头（2026-09-28 深夜）：**1D 4 格** —— 默认（不出舌，左下角）+ 沿对角线 3 步；轴借 `Mouth/TongueL`。
    # ⚠️ 4 个刻度是**占位**（等分；`tongueOut` 还没实测过）。
    'MouthTongue'  = @{ t = 'Tongue';    a = 4; thr = @(0.0, 0.3333, 0.6667, 1.0) }
    # 下巴（2026-09-28 深夜）：**1D 2 格**（咬合/闭 ↔ 张开），轴 = `Mouth/Jaw`；刻度就是原 2D 表的 Y 两档。
}

# 刻度核对（2026-09-27 加）：坐标是**语义**（轴值 → 姿势），所以它必须等于生成器里那套刻度。
# 现在"按实测收圈"的有两处：MouthCore 的 Y（手机实测张满只到 0.75、半张 0.4 ⇒ 0 / 0.4 / 0.75）
# 与 MouthJaw 的 X（下巴左右实测单侧只到 0.52 ⇒ 刻度 ±0.65，原来摆的 ±1 够不着）。
# ⚠️ 改生成器的刻度表时这里要一起改 —— 它同时是"资产到底有没有被重新生成"的探针。
$scaleOf = [ordered]@{
    # ⚠️ MouthCore 的 Y 多一个 **0.6**：右上角（大笑 × 张满）是**逐格移位**过去的（下面 $exactPos 专门核它），
    #    其余格仍在 0 / 0.4 / 0.75 上 —— 所以这一列是"刻度 ∪ 移位值"，移位那格由 $exactPos 钉死。
    # ⚠️ MouthCore 也是**手工拉的自由点集**（8 个点全拉过）⇒ 这一列列的是 8 个点用到的全部坐标值，
    #    真正的规格在下面的 $exactPos 里（8 个点逐个钉死）。
    # ⭐ 这 11 格就是**现行权威**（原 `DIAG_MouthCore_Aligned11.controller` 那份隔离件按规矩验完即删，2026-09-29；
    #    生成器的 `$mouthCoreOver` 与这里是同一组值）
    'MouthCore'   = @{ x = @(-0.5, -0.4225, -0.345, -0.048, -0.03, -0.012, 0.382, 0.3905, 0.776, 0.811, 0.846); y = @(-0.007, -0.0035, 0, 0.25, 0.32825, 0.4065, 0.463, 0.507, 0.813, 0.926) }
    # ⚠️ 下巴 2026-09-28 深夜降成 **1D 2 格** ⇒ 不在 2D 刻度表里（6 个手拉点已退役，见 `$simple1DSlot`）。
    # 整嘴平移（2026-09-28 下午新建、傍晚收成 **3×2**）：X ±0.95（满档 0.96）· Y **只有 0 / +1 两档**
    'MouthShift'  = @{ x = @(-0.95, 0.0, 0.95); y = @(0.0, 1.0) }


    # 舌头（2026-09-28 深夜）：**不在 2D 刻度清单里** —— 1D 5 格的阈值在 `$simple1DSlot` / `$scaleOf1D`。
    'LidL'        = @{ x = @(-1.0, 0.0, 1.0); y = @(-1.0, 0.0, 1.0) }
    'LidR'        = @{ x = @(-1.0, 0.0, 1.0); y = @(-1.0, 0.0, 1.0) }
    # 颊轴：0 / 1 两档（轴本身就是 0~1 的形状权重）⇒ 4 个点的坐标只能是 0 / 1
    'Cheek'       = @{ x = @(0.0, 1.0);       y = @(0.0, 1.0) }
    # 下巴：JawSide 实测单侧只到 0.52（±0.65 老刻度够不着）· Jaw 0 / 0.75
    'MouthJaw'    = @{ x = @(-0.52, 0.0, 0.52); y = @(0.0, 0.75) }
}
foreach ($c in $slotCopyOf.Keys) { $scaleOf[$c] = $scaleOf[$slotCopyOf[$c]] }

foreach ($name in $slotSpec.Keys) {
    if (-not $trees.ContainsKey($name)) { continue }   # 缺树已经报过
    $spec = $slotSpec[$name]
    if ($spec.direct) {
        $wantD = New-Object System.Collections.Generic.List[string]
        $tblD = $script:slotNames[$name]
        for ($k = 0; $k -lt $spec.direct.Count; $k++) { if ($tblD[$k]) { $wantD.Add($tblD[$k]) } }
        $kidsD = @($trees[$name].Kids | Where-Object { $_.Name -ne '<空 Motion>' -and $_.Name -ne '?' } | ForEach-Object { $_.Name })
        $wrongD = @($kidsD | Where-Object { $wantD -notcontains $_ })
        if ($wrongD.Count -gt 0) { $problems.Add("Direct 表 $name 的槽位名不在清单里（$($wrongD.Count) 个）：$($wrongD -join ', ')") }
        $notes.Add("槽位 $name（Direct）：已摆 $($kidsD.Count)/$($wantD.Count)")
        continue
    }
    $want = New-Object System.Collections.Generic.List[string]
    $tbl = $script:slotNames[$name]
    if ($null -eq $tbl) { $problems.Add("HoSlotNames.psd1 里没有 $name 的名字") }
    else {
        $is2D = ($tbl.Count -gt 0 -and $tbl[0] -is [System.Array])
        foreach ($j in $spec.ys) {
            foreach ($i in 0..($spec.a - 1)) {
                if ($spec.skip -and ($spec.skip -contains "$i,$j")) { continue }
                $nm = if ($is2D) { $tbl[$i][$j] } else { $tbl[$i] }
                if ($nm) { $want.Add($nm) }
            }
        }
    }
    $kids = @($trees[$name].Kids | Where-Object { $_.Name -ne '<空 Motion>' -and $_.Name -ne '?' } | ForEach-Object { $_.Name })
    $empty = @($trees[$name].Kids | Where-Object { $_.Name -eq '<空 Motion>' }).Count
    $wrong = @($kids | Where-Object { $want -notcontains $_ })
    if ($wrong.Count -gt 0) { $problems.Add("树 $name 的槽位名不在清单里（$($wrong.Count) 个）：$($wrong -join ', ')") }
    $notes.Add("槽位 $name：已摆 $($kids.Count)/$($want.Count)" + $(if ($empty -gt 0) { "（另有 $empty 个空 Motion 占位）" } else { "" }))

    $scale = $scaleOf[$name]
    # ⚠️ 骨架里每个槽位都是**空 Motion**（没有名字可依），作者填了片段之后才按槽位名对上 ——
    # 所以这里核的是**坐标集合**：出现过的刻度必须都在清单里；满表（子节点数 = 槽位数）时集合要相等。
    $gotX = @($trees[$name].Kids | ForEach-Object { [double]$_.PosX } | Where-Object { -not [double]::IsNaN($_) })
    $gotY = @($trees[$name].Kids | ForEach-Object { [double]$_.PosY } | Where-Object { -not [double]::IsNaN($_) })
    $unexpectedX = @()
    foreach ($v in $gotX) { if (-not (HasNearValue $scale.x $v)) { $unexpectedX += $v } }
    $unexpectedY = @()
    foreach ($v in $gotY) { if (-not (HasNearValue $scale.y $v)) { $unexpectedY += $v } }
    if ($unexpectedX.Count -gt 0) { $problems.Add("树 $name 有不在刻度清单里的 X 坐标：" + (($unexpectedX | Sort-Object -Unique) -join ', ')) }
    if ($unexpectedY.Count -gt 0) { $problems.Add("树 $name 有不在刻度清单里的 Y 坐标：" + (($unexpectedY | Sort-Object -Unique) -join ', ')) }
    if ($trees[$name].Kids.Count -eq $want.Count) {
        foreach ($wantValue in $scale.x) {
            if (-not (HasNearValue $gotX ([double]$wantValue))) { $problems.Add("树 $name 少了 X 刻度 $wantValue（满表时每个刻度都要有格子）") }
        }
        foreach ($wantValue in $scale.y) {
            if (-not (HasNearValue $gotY ([double]$wantValue))) { $problems.Add("树 $name 少了 Y 刻度 $wantValue（满表时每个刻度都要有格子）") }
        }
    }
}

# 逐格移位：这些槽位**故意不在刻度值上**（实测定），坐标在这里钉死 ——
# 生成器（文本端 `over` / Unity 端 `Override`）改刻度或挪点时，这一条会立刻响。
$exactPos = [ordered]@{
    # ⭐ **权威 = 就是下面这组值**（2026-09-29 用户定：分块隔离的控制器才是真验收基准；那份
    #    `DIAG_MouthCore_Aligned11.controller` 已按"验完即删"的规矩删掉，`tree-dump.py` 读出的坐标落在这里）。
    #    ⚠️ 用户挪点之后：**先读资产 → 再改生成器的 `$mouthCoreOver` → 最后改这里**（顺序不能反）。
    '嘴苦闭'          = @(-0.5, 0)
    '嘴苦半张'         = @(-0.4225, 0.463)
    '嘴苦满'          = @(-0.345, 0.926)
    '嘴平闭'          = @(-0.012, 0)
    '嘴平半张'         = @(-0.03, 0.4065)
    '嘴平满'          = @(-0.048, 0.813)
    '嘴笑闭'          = @(0.382, -0.0035)
    '嘴笑半张'         = @(0.3905, 0.32825)
    '嘴大笑闭'         = @(0.776, -0.007)
    '嘴大笑半张'        = @(0.811, 0.25)
    '嘴大笑满'         = @(0.846, 0.507)
}
foreach ($slot in $exactPos.Keys) {
    $tree = $null
    foreach ($tn in $script:slotNames.Keys) {
        $tbl = $script:slotNames[$tn]
        $flat = @()
        if ($tbl.Count -gt 0 -and $tbl[0] -is [System.Array]) { foreach ($c in $tbl) { $flat += $c } } else { $flat = @($tbl) }
        if ($flat -contains $slot) { $tree = $tn; break }
    }
    if (-not $tree -or -not $trees.ContainsKey($tree)) { continue }
    $kid = @($trees[$tree].Kids | Where-Object { $_.Name -eq $slot })
    if ($kid.Count -ne 1) { $problems.Add("逐格移位的槽位 $slot 在树 $tree 里找不到（或不止一个）"); continue }
    $want = $exactPos[$slot]
    if (-not (HasNearValue @([double]$kid[0].PosX) ([double]$want[0])) -or
        -not (HasNearValue @([double]$kid[0].PosY) ([double]$want[1]))) {
        $problems.Add("槽位 $slot 的坐标应为 ($($want[0]), $($want[1]))，实际 ($($kid[0].PosX), $($kid[0].PosY))")
    }
}


# ⚠️ **变体 / 副本必须与主版逐点一致**（2026-09-27 加）：变体表（`MouthCoreRoll` = 猫嘴版）与副本表
#    （`LidLExpr` 等）语义上是"**同一张表、换一套姿势**" ⇒ 轴、刻度、稀疏格、**逐点坐标**都必须一样。
#    为什么值得单开一条：用户是**在 Animator 窗口里手拉主版**的，变体那一份**不会跟着动** ——
#    2026-09-27 就真发生了（拉了 `MouthCore` 5 个点，`MouthCoreRoll` 还是旧的，两棵树对不上）。
#    配对方式是**按槽位后缀**（`A3X<i>Y<j>`），不比名字前缀。
foreach ($copy in $slotCopyOf.Keys) {
    $mainName = $slotCopyOf[$copy]
    if (-not $trees.ContainsKey($copy) -or -not $trees.ContainsKey($mainName)) { continue }
    $mainKid = @{}
    foreach ($kid in $trees[$mainName].Kids) {
        if ($kid.Name -eq '<空 Motion>') { continue }
        $i = $kid.Name.IndexOf('__')
        if ($i -ge 0) { $mainKid[$kid.Name.Substring($i + 2)] = $kid }
    }
    foreach ($kid in $trees[$copy].Kids) {
        if ($kid.Name -eq '<空 Motion>') { continue }
        $i = $kid.Name.IndexOf('__')
        if ($i -lt 0) { continue }
        $suffix = $kid.Name.Substring($i + 2)
        if (-not $mainKid.ContainsKey($suffix)) {
            $problems.Add("$copy 的槽位 $($kid.Name) 在主版 $mainName 里没有对应点")
            continue
        }
        $m = $mainKid[$suffix]
        if (-not (HasNearValue @([double]$m.PosX) ([double]$kid.PosX)) -or
            -not (HasNearValue @([double]$m.PosY) ([double]$kid.PosY))) {
            $problems.Add("$copy 的 $suffix 坐标 ($($kid.PosX), $($kid.PosY)) 与主版 $mainName 的 ($($m.PosX), $($m.PosY)) 不一致")
        }
    }
    $notes.Add("变体/副本 $copy 与主版 $mainName 逐点核对过了")
}

# 1D 表的槽位：名字按 `<树名>__<轴段词>__A<n>X<i>`，阈值必须正好是实测那两档
foreach ($name in $simple1DSlot.Keys) {
    if (-not $trees.ContainsKey($name)) { continue }   # 缺树已经报过
    $spec = $simple1DSlot[$name]
    if ($trees[$name].Type -ne 'Simple1D') { $problems.Add("1D 表 $name 类型应为 Simple1D，实际 $($trees[$name].Type)") }
    $want = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $spec.a; $i++) { $want.Add(("{0}" -f $script:slotNames[$name][$i])) }
    $names = @($trees[$name].Kids | Where-Object { $_.Name -ne '<空 Motion>' } | ForEach-Object { $_.Name })
    $wrong = @($names | Where-Object { $want -notcontains $_ })
    if ($wrong.Count -gt 0) { $problems.Add("1D 表 $name 的槽位名不在清单里（$($wrong.Count) 个）：$($wrong -join ', ')") }
    $notes.Add("槽位 $name（1D）：已摆 $($names.Count)/$($want.Count)")
    $thr = @($trees[$name].Kids | ForEach-Object { [double]$_.Threshold } | Sort-Object)
    foreach ($wantValue in $spec.thr) {
        if (-not (HasNearValue $thr ([double]$wantValue))) { $problems.Add("1D 表 $name 少了阈值 $wantValue（实际 $($thr -join ', ')）") }
    }
    foreach ($v in $thr) {
        if (-not (HasNearValue $spec.thr $v)) { $problems.Add("1D 表 $name 有不在刻度清单里的阈值：$v") }
    }
}

# 1D 开关：两个子节点必须是**两个具体的表**，阈值按类型核 ——
#   · 按键表情副本：阈值 0 / 1（门）
#   · 轴驱动的变体：阈值 = 该轴上的**实测档位**（卷唇 0.02 / 0.18），必须显式写死
$switchSpec = [ordered]@{
    'MouthCoreRollSwitch' = @{ kids = @('MouthCore', 'MouthCoreRoll');           thr = @(0.15, 0.30); kind = '轴驱动变体（卷唇→猫嘴；2026-09-28 判据换成 下唇卷×嘴角门 之后重定）' }
}
foreach ($sw in $switchSpec.Keys) {
    if (-not $trees.ContainsKey($sw)) { continue }
    $want = $switchSpec[$sw]
    $kids = @($trees[$sw].Kids)
    if ($kids.Count -ne 2) { $problems.Add("1D 开关 $sw 应有 2 个子节点，实际 $($kids.Count)"); continue }
    $thr = @($kids | ForEach-Object { [double]$_.Threshold } | Sort-Object)
    if (-not (HasNearValue @($thr[0]) ([double]$want.thr[0])) -or -not (HasNearValue @($thr[1]) ([double]$want.thr[1]))) {
        $problems.Add("1D 开关 $sw（$($want.kind)）的两个阈值应为 $($want.thr -join ' 与 ')，实际 $($thr -join ', ')")
    }
    $names = @($kids | ForEach-Object { $_.Name })
    foreach ($w in $want.kids) { if ($names -notcontains $w) { $problems.Add("1D 开关 $sw 少了子节点 $w（实际 $($names -join ', ')）") } }
}

# ⭐ **槽位总数 = 95**（43 参数 / 27 树这一版）。变动账：上午 92→83（删 `MouthWidth` 9 格）⇒
#    下午 `MouthShift` 3×2（6）+ `MouthCorner` 3×3（9）⇒ 92；傍晚 `MouthCorner` 换成 1D 的 `MouthWidth`（3）、
#    `MouthShift` 9→6、`MouthCore`（+变体）各 8→11 ⇒ **89**；2026-09-28 深夜舌头重做
#    （2D 4 格 → 试过 2D 5 格 → 定成 **1D 4 格**：默认 + 沿对角线 3 步）⇒ 仍是 89；
#    随后**下巴从 2D 6 格降成 1D 2 格**（用户「左右两个点完全没必要」：咬紧时侧偏只有 0.02~0.04）⇒ 85；
#    再**整嘴平移从 3×2 挖成 4 格 T 形**（删「上移 × 侧移」两角）⇒ 83；
#    2026-09-28 深夜**眼睑第二轴 Squint -> Form（2 档 -> 3 档）** ⇒ 眼睑四棵树各 6 -> 9 ⇒ **95**。
#    它是"资产到底有没有被重新生成 / 有没有人偷偷加删格"的总闸 —— 单独一格出错时上面那些
#    逐树检查已经报过了，这一条只兜底"整棵树不见了"这类错。
$slotTotal = 0
foreach ($name in $slotSpec.Keys) {
    if (-not $trees.ContainsKey($name)) { continue }
    $slotTotal += @($trees[$name].Kids | Where-Object { $_.Name -ne '<空 Motion>' -and $_.Name -ne '?' }).Count
}
foreach ($name in $simple1DSlot.Keys) {
    if (-not $trees.ContainsKey($name)) { continue }
    $slotTotal += @($trees[$name].Kids | Where-Object { $_.Name -ne '<空 Motion>' -and $_.Name -ne '?' }).Count
}
if ($slotTotal -eq 0) { $notes.Add("叶子 Direct 树全部留空（新架构）：作者手填，槽位片段数不再核对") }
else { $notes.Add("槽位片段 $slotTotal 个（作者手填的）") }

# ── 报告 ─────────────────────────────────────────────────────────────────────
"=== $Path ==="
"参数：声明 $($declared.Count) 个 · 期望 $($expected.Count) 个 · 树（BlendTree）：$($trees.Count) 棵 · 槽位：$($slotTotal) 个（期望 95）"



if ($notes.Count -gt 0) { foreach ($n in $notes) { "  [注] $n" } }
if ($problems.Count -eq 0) {
    "  参数与结构都对得上（tranche 1 的 $($expectedTrees.Count) 棵树都在）"
    exit 0
}
foreach ($p in $problems) { "  [缺/错] $p" }
"合计 $($problems.Count) 条"
exit 1
