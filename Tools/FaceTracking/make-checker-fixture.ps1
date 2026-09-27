# make-checker-fixture.ps1 -- 造一份"形状正确"的控制器夹具，用来验证 check-controller.ps1 的**通过路径**
#
# 为什么需要：check-controller 是拿旧土豆控制器当"失败样本"调出来的（它缺 35 个参数、缺树）。
# 只测失败路径的话，新加的检查（槽位命名 / 1D 阈值 / 每个状态的 WD）可能有**假阳性**却没人知道。
# 这份夹具按设计稿 §10 的形状造一小块（根 Direct → 区域 Direct → 1D 开关 → 2D 表 + 槽位），
# 跑 checker 时应当**只剩"缺树"**，不该出现槽位/阈值/WD/参数类的错。
#
# ⚠️ 它只保证"我们的解析器认"，不保证 Unity 能加载（字段是手写的最小子集）。
param([string] $Out = 'D:\Unity_Fork\HoUnityTools\.research\fixture-controller.controller')
$ErrorActionPreference = 'Stop'

$profile = 'D:\Unity_Fork\HoUnityTools\Editor\FaceTracking\Profiles\ho-iPhoneVTS.hoface.json'
$prof = Get-Content -LiteralPath $profile -Encoding UTF8 -Raw | ConvertFrom-Json

# ── 参数：profile 的 37 行 + W/One + 2 个表情门 ─────────────────────────────────
$params = [ordered]@{}
foreach ($row in $prof.outputs) {
    $n = $row.parameter
    if ($n -notlike 'Ho/Drive/*') { continue }
    $params[$n] = if ($n -like 'Ho/Drive/Gate/*') { 1.0 } else { 0.0 }
}
$params['Ho/Drive/W/One'] = 1.0
$params['Ho/Drive/Gate/Expr/Smile'] = 0.0
$params['Ho/Drive/Gate/Expr/Angry'] = 0.0

$ids = @{}
$next = 206000001
# ⚠️ `$script:` 前缀是必须的：PowerShell 函数里写 `$next++` 只会改**函数局部**的副本，
# 于是每个文档都会拿到同一个 fileID（第一次跑就撞上了：6 棵树被折叠成 1 棵）。
function NewId([string]$key) {
    if (-not $script:ids.ContainsKey($key)) { $script:ids[$key] = $script:next; $script:next++ }
    return $script:ids[$key]
}

$sb = New-Object System.Text.StringBuilder
function W([string]$line) { [void]$sb.AppendLine($line) }

W '%YAML 1.1'
W '%TAG !u! tag:unity3d.com,2011:'

# AnimatorController (91)
W "--- !u!91 &9100000"
W "AnimatorController:"
W "  m_ObjectHideFlags: 0"
W "  m_Name: FixtureFaceController"
W "  m_AnimatorParameters:"
foreach ($n in $params.Keys) {
    W "  - m_Name: $n"
    W "    m_Type: 1"
    W "    m_DefaultFloat: $($params[$n])"
    W "    m_DefaultInt: 0"
    W "    m_DefaultBool: 0"
    W "    m_Controller: {fileID: 9100000}"
}
W "  m_AnimatorLayers:"
W "  - serializedVersion: 5"
W "    m_Name: Ho/00 Drive"
W "    m_StateMachine: {fileID: 110700000}"
W "    m_Mask: {fileID: 0}"
W "    m_Motions: []"
W "    m_Behaviours: []"
W "    m_IKPass: 0"
W "    m_SyncedLayerAffectsTiming: 0"
W "    m_Controller: {fileID: 9100000}"

# AnimatorStateMachine (1107)
W "--- !u!1107 &110700000"
W "AnimatorStateMachine:"
W "  m_ObjectHideFlags: 0"
W "  m_Name: Ho/00 Drive"
W "  m_ChildStates:"
W "  - serializedVersion: 1"
W "    m_State: {fileID: 110200000}"
W "    m_Position: {x: 300, y: 100}"
W "  m_ChildStateMachines: []"
W "  m_AnyStateTransitions: []"
W "  m_EntryTransitions: []"
W "  m_StateMachineTransitions: {}"
W "  m_StateMachineBehaviours: []"
W "  m_AnyStatePosition: {x: 50, y: 20, z: 0}"
W "  m_EntryPosition: {x: 50, y: 120, z: 0}"
W "  m_ExitPosition: {x: 800, y: 120, z: 0}"
W "  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}"
W "  m_DefaultState: {fileID: 110200000}"

# AnimatorState (1102) -- WD 必须开（动作里有 Direct）
W "--- !u!1102 &110200000"
W "AnimatorState:"
W "  m_ObjectHideFlags: 0"
W "  m_Name: Drive"
W "  m_Speed: 1"
W "  m_CycleOffset: 0"
W "  m_Transitions: []"
W "  m_StateMachineBehaviours: []"
W "  m_Position: {x: 50, y: 50, z: 0}"
W "  m_IKOnFeet: 0"
W "  m_WriteDefaultValues: 1"
W "  m_Mirror: 0"
W "  m_SpeedParameterActive: 0"
W "  m_MirrorParameterActive: 0"
W "  m_CycleOffsetParameterActive: 0"
W "  m_TimeParameterActive: 0"
W "  m_Motion: {fileID: $(NewId 'Ho/00 Drive Tree')}"
W "  m_Tag: "
W "  m_SpeedParameter: "
W "  m_MirrorParameter: "
W "  m_CycleOffsetParameter: "
W "  m_TimeParameter: "

function Tree([string]$name, [string]$type, [string]$blend, [string]$blendY, [array]$kids) {
    $id = NewId $name
    W "--- !u!206 &$id"
    W "BlendTree:"
    W "  m_ObjectHideFlags: 0"
    W "  m_Name: $name"
    W "  m_Children:"
    foreach ($k in $kids) {
        W "  - serializedVersion: 2"
        W "    m_Motion: {fileID: $($k.motion)}"
        W "    m_Threshold: $($k.threshold)"
        W "    m_Position: {x: $($k.x), y: $($k.y)}"
        W "    m_TimeScale: 1"
        W "    m_CycleOffset: 0"
        W "    m_DirectBlendParameter: $($k.direct)"
        W "    m_Mirror: 0"
    }
    W "  m_BlendParameter: $blend"
    W "  m_BlendParameterY: $blendY"
    W "  m_UseAutomaticThresholds: 1"
    W "  m_NormalizedBlendValues: 0"
    W "  m_BlendType: $type"
    W "  m_MinThreshold: 0"
    W "  m_MaxThreshold: 1"
    return $id
}

function Slot([string]$tree, [string]$x, [string]$y, [int]$a, [int]$i, [int]$j) {
    return "$($tree)__$($x)__$($y)__A$a" + "X$i" + "Y$j"
}

# 叶子槽位（夹具里用空 Motion 占位；名字必须合规）
function SlotKids([string]$tree, [string]$x, [string]$y, [int]$a, [array]$ys) {
    $kids = @()
    $n = 0
    foreach ($j in $ys) { foreach ($i in 0..($a - 1)) {
        $kids += @{ motion = '{fileID: 0}'; threshold = $n; x = $i; y = $j; direct = 'Blend'; name = (Slot $tree $x $y $a $i $j) }
        $n++
    } }
    return $kids
}

# 先占好 id（顺序无关，只要彼此引用对）
$mouthCoreId     = NewId 'MouthCore'
$mouthCoreExprId = NewId 'MouthCoreExpr'
$mouthJawId      = NewId 'MouthJaw'
$switchId        = NewId 'MouthCoreSwitch'
$regionId        = NewId 'MouthRegion'
$rootId          = NewId 'Ho/00 Drive Tree'

[void](Tree 'MouthCore' '3' 'Ho/Drive/Mouth/Form' 'Ho/Drive/Mouth/Open' (SlotKids 'MouthCore' 'Form' 'Open' 3 @(0, 1, 2)))
[void](Tree 'MouthCoreExpr' '3' 'Ho/Drive/Mouth/Form' 'Ho/Drive/Mouth/Open' (SlotKids 'MouthCoreExpr' 'Form' 'Open' 3 @(0, 1, 2)))
[void](Tree 'MouthJaw' '3' 'Ho/Drive/Mouth/Jaw' 'Ho/Drive/Mouth/Forward' (SlotKids 'MouthJaw' 'Jaw' 'Forward' 3 @(0)))
[void](Tree 'MouthCoreSwitch' '0' 'Ho/Drive/Gate/Expr/Smile' 'Blend' @(
        @{ motion = "{fileID: $mouthCoreId}"; threshold = 0; x = 0; y = 0; direct = 'Blend' },
        @{ motion = "{fileID: $mouthCoreExprId}"; threshold = 1; x = 0; y = 0; direct = 'Blend' }))
[void](Tree 'MouthRegion' '4' 'Blend' 'Blend' @(
        @{ motion = "{fileID: $switchId}"; threshold = 0; x = 0; y = 0; direct = 'Ho/Drive/W/One' },
        @{ motion = "{fileID: $mouthJawId}"; threshold = 1; x = 0; y = 0; direct = 'Ho/Drive/W/One' }))
[void](Tree 'Ho/00 Drive Tree' '4' 'Blend' 'Blend' @(
        @{ motion = "{fileID: $regionId}"; threshold = 0; x = 0; y = 0; direct = 'Ho/Drive/Gate/Mouth' }))

[System.IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
"fixture -> $Out ($($params.Count) params)"
