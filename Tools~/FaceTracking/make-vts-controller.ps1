# make-vts-controller.ps1 -- 生成新控制器骨架 PTP_CTR_Face_VTS.controller（动画留空）
#
# 形状：见 docs/VTS_HQ_CONTROLLER.md §3 / §10（根 Direct → 4 个区域 Direct → 表 / 1D 副本树 / 变体 → 槽位）。
# 为什么是"从老控制器取模板"：.controller 是 Unity 自己的序列化格式，字段顺序/字段集照抄 Unity 写出来的那份
# 最不容易被导入器挑刺（我手上没有第二份能当模板的资产）。
#
# 槽位 = **一个空动画片段**（2026-09-27 用户定）：每个槽位都生成一份以槽位名命名的空 `.anim`
# 放进 `$ClipFolder`，控制器按 GUID 指过去。于是
#   * 混合树里每一格都显示**语义名字**（不再是 None），整张表一眼可读；
#   * 「槽位名 = 片段名」这条约定从第一天就成立 —— 姿势烘焙直接往这些文件里写；
#   * 空片段不写任何曲线 ⇒ 运行期行为与"空 Motion"完全相同（权重只由坐标决定）。
param(
    [string] $Template = 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\PTP_CTR_Face_ARKit.controller',
    [string] $Out      = 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\PTP_CTR_Face_VTS.controller',
    [string] $Profile  = 'D:\Unity_Fork\HoUnityTools\Editor\FaceTracking\Profiles\ho-iPhoneVTS.hoface.json',
    [string] $ClipFolder = 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\Animations',
    [switch] $NoClips,
    [switch] $ForceClips
)
$ErrorActionPreference = 'Stop'

# ── 参数：profile 里所有 `Ho/Drive/*` 行 + W/One（门控行的默认值：门 = 1、其余 = 0）────
# 2026-09-28 傍晚：profile 现在是 **40 行** ⇒ 40 + W/One 1 + 表情门 2 = **43 个参数**
#   （上午删 `MouthWidth`/`Mouth/X`（§5.7.22）→ 下午按「整嘴平移 + 嘴角 3×3」重建（§5.7.23）→
#    傍晚用户判定「收缩舒张不必拆左右、一根轴 3 状态就够，左右交给整嘴平移」⇒ 删掉嘴角那两根轴，
#    `MouthWidth` 以 **1D 3 格**回来（轴是既有的 `Mouth/Pucker`），见 §5.7.24）。
#   ⚠️ 参数表本来就是**从 profile 反推**的（下面 `$axisRows`）⇒ profile 改了这里自动跟着变。
# 其中两条是**形态契约行**（`Ho/Drive/Gate/MouthStyle` 默认 1、`Ho/Drive/Style/InvertedV` 默认 0）：
# 中间层把内部形态行（`Ho/Style/*`，控制器看不见）转发成控制器参数，控制器只负责接它们
# —— 见下面的 `$regionWeights`（形态门挂在 `MouthRegion` 的两个孩子上 —— 2026-09-28 `MouthWidth` 删掉后由三变二）
#    与 `$simple1D`/`$tables`（两条形态子树）。

$prof = Get-Content -LiteralPath $Profile -Encoding UTF8 -Raw | ConvertFrom-Json
$gates = [ordered]@{}
foreach ($g in @('Mouth', 'Eye', 'Brow', 'Nose')) { $gates["Ho/Drive/Gate/$g"] = 1.0 }
$params = [ordered]@{}
foreach ($k in $gates.Keys) { $params[$k] = $gates[$k] }
$params['Ho/Drive/W/One'] = 1.0
$axisRows = @($prof.outputs | Where-Object { $_.parameter -like 'Ho/Drive/*' } | Sort-Object parameter)
foreach ($row in $axisRows) { $params[$row.parameter] = if ($row.parameter -like 'Ho/Drive/Gate/*') { 1.0 } else { 0.0 } }

# ── 形状：树名 -> 定义 ────────────────────────────────────────────────────────
# 2D 表：X/Y 参数 + 每轴刻度（值 → 坐标）
$form  = @(-1.0, 0.0, 1.0)
# 下巴**左右**轴（`JawSide`）自己的刻度（2026-09-27 用户定「还是六个动画，只不过把最大值弄到 0.65 左右」）。
# ⚠️ 原来的 `$form`（−1 / 0 / +1）是**没实测就占位**的：实测在「微张、解放咬颌」那个状态下
#    单侧只到 **0.52**（咬紧时只有 0.02–0.04，而"只挤嘴角"的伪影反而有 0.05–0.2）⇒ 用 ±1 的话
#    真动作只把 X 推到 52%，作者画的左右那两格永远吃不饱。改成 ±0.65：0.52 ⇒ 80%。
$jawSide = @(-0.65, 0.0, 0.65)
# ⚠️ **`MouthJaw` 的"6 个手拉自由点"已退役**（2026-09-28 深夜用户判定：「左右两个点完全没必要」）。
#   那些点是 2026-09-27 用户在 Animator 窗口里拉的（真值留档）：
#     A3X0Y0 (-0.342,-0.015)   A3X1Y0 (0,0)           A3X2Y0 (0.34,-0.01)
#     A3X0Y1 (-0.541, 0.374)   A3X1Y1 (0.006, 0.902)  A3X2Y1 (0.534, 0.354)
#   为什么删得掉：① 下巴左右只在"微张/解放咬颌"时才到 0.52，咬紧时 0.02~0.04（"只挤嘴角"伪影 0.05~0.2）
#   ⇒「咬合 × 侧偏」两格物理上到不了；② 留左右两点就得让"张满·中"靠 50/50 混、或牺牲 Y 的分辨率 ⇒ 不值。
#   ⇒ 下巴只留上下两档（`$jawOpen` = 0 / 0.75），树从 2D 6 格降成 **1D 2 格**（见 `$simple1D`）。
#   `Ho/Drive/Mouth/JawSide` 照旧发布（出口；以后想要回来再建 2D 表 + 手拉点）。
$open  = @(0.0, 0.5, 1.0)
# Mouth/Open 专用刻度（2026-09-27 用户实测定）：手机"张满"只到 0.75、"半张"0.4 附近
# （0.9/1.0 会被投影到顶行，实测逐位相同）。
# ⚠️ **负侧一格都不要**（用户定）：`Open` 的负值只由"卷唇"驱动，而那整族已经搬去 `MouthLipRoll`
# （轴就是 mouthRollUpper/Lower），所以这里只留 0 / .4 / .75。
# 曲线保留 ±0.02 死区（撇嘴只到 -0.01/0；负值一律钳到 Y0 = 闭）。
# 只给 MouthCore 的 Y 用。
$openMeasured = @(0.0, 0.4, 0.75)
$two   = @(-1.0, 1.0)      # 两档：负端 / 正端
$zero1 = @(0.0, 1.0)       # 两档：0 / 满
# 下巴竖直轴（Mouth/Jaw）的两档（2026-09-27）：**0 = 闭 / 咬合**、**+0.75 = 张满**（待标定）。
# ⚠️ 轴的表达式是双极的（`jawOpen − mouthClose`）：负侧（咬合 / 咀嚼）在数据上仍然有，但**钳到 0 这一档**；
# 等咀嚼实测（`jawOpen` / `mouthClose` 的振幅与频率）再决定要不要为「咬合」单开第三档。
$jawOpen = @(0.0, 0.75)
# ⭐ **舌头：1D 4 格，不是 2D 表**（2026-09-28 深夜用户四句话定形）：
#   「其实也只需要一个 1d 树分三段就行了吧」→「不行我感觉还是要五点」→
#   「5 点不是你想的那样，我想的是**默认态在左下角**，的四点」→「你改少点吧，四个状态差不多」
#   = **默认（左下角）+ 沿对角线 3 步**。姿势沿 (伸出量, 张开量) 的对角线走
#   （舌出来多少、嘴就张多少 ⇒ 才不穿模）⇒ 在树上就是**一根轴**（伸出量）上的 4 个状态，
#   "张嘴"由作者**烘进每段片段**里。
#   ⚠️ 别建成 2D 表：① 那些点在 2D 里**共线**（实测共线/退化点集不可预测、会出负权重）；
#     ② 把 `jawOpen` 当第二根轴会让"只张嘴不伸舌"把舌头也带出来 ✗。
#   ⚠️ 刻度 0 / 0.3333 / 0.6667 / 1 是**占位**（等分；`tongueOut` 从来没实测过）。
$tongueTicks = @(0.0, 0.3333, 0.6667, 1.0)
# MouthCore 的 X（Form）专用刻度：**四档 −1 / 0 / 0.75 / 1**（2026-09-27 实测两端 + 2026-09-28 傍晚补 sad 列）。
# 「常态笑」≈ 0.75、「大笑」= 1；**−1 = 苦**（sad 折进 `Form` 负半轴之后负侧终于有语义 ⇒ 用户定「四列，补 3 格」，
# 要画的是「苦 × 闭 / 半张 / 张满」）。
# —— 两个都是真实状态，各要一个采样点 ⇒ 三档（0 / 0.75 / 1）。
# **负侧现在的归属**（2026-09-28 傍晚重排）：**苦 = 这张表的 −1 列本身**；噘 → 倒V 形态（加了 funnel 门，
# 只认"真撅"）；卷唇/咬唇 → `MouthCoreRoll` 变体；嘴宽 → `MouthWidth`（1D 3 格）。
#   （当天在这块反复了三版：§5.7.22 / §5.7.23 / §5.7.24。）
$formSmile = @(-1.0, 0.0, 0.75, 1.0)
# ⇒ MouthCore 是"笑 × 张嘴"两块的**正值矩形**，只挖掉**顶行中间那一格**（2026-09-27 用户定）：
#   嘴张到最大时**半笑与全笑已经分辨不出来** ⇒ (Form 0.75 × Open 0.75) 没有独立语义。
#   ⚠️ 顶行左右两个角**留着**："张嘴不笑"（0 × 0.75）与"大笑张嘴"（1 × 0.75）都是真实状态，
#   而且留着它们表就仍是**完整矩形**（挖掉的是矩形内部的一点，不是圈上的角）⇒ 出界照旧是干净的钳制。
$mouthCoreSkip = @('2,2')
# ⚠️ **2026-09-28 当天三版之后的刻度**（41 段 + 9 段补录实测；上午那版 2D 的 `MouthWidth` 已删）：
#    · `Mouth/X`（整嘴左右平移）**±0.95**：整嘴右移 `mouthLeft` 0.963~0.967 / 左移 `mouthRight` 0.967~0.976
#      ⇒ 满档 0.96；曲线里带 **±0.20 死区**把「撇嘴」（0.047~0.136）压成 0（缝 +0.594）。
#    · `Mouth/Y`（整嘴上下）**只有 0 / +1 两档**（负侧到不了，见上面的注释）。
#    · `MouthWidth`（1D 3 格）**−0.95 / −0.16 / +0.95** = 窄 收嘴不撅 / 中 静态 / 宽 抿嘴嘴宽。
#    · `MouthCore` 的 X 是**四列**（−1 / 0 / 0.75 / 1）：−1 列 = 苦；挖掉的那格索引 `(2,2)`。
#    ⚠️ 槽位名按**索引**编 ⇒ 挪刻度**不改名、不新增片段**（命名权威 §5）；但**换轴（token）会换槽位名**
#      ⇒ 旧片段变孤儿、由孤儿清理删掉（`MouthCorner` 那一批就是）。
$shiftXMeasured = @(-0.95, 0.0, 0.95)
# ⚠️ **整嘴平移的 Y 只有两档**（2026-09-28 傍晚用户实测「shift 根本不会往下移动，只有 6 个点的状态」）：
#    负侧（闭唇下颌下拉）在真机上到不了 / 被咀嚼抢 ⇒ 跟 `MouthJaw` 的 Y 一样**钳到 0 那一档** ⇒ 3×2 = 6 格。
$shiftYMeasured = @(0.0, 1.0)
# ⚠️ **`MouthShift` 挖掉"上移 × 侧移"那 2 格**（2026-09-28 深夜用户问「同理 shift 的两个角点是不是也能删了」
#   ⇒ 对，但删的是**上移那两个**（`(0,1)` 左上 / `(2,1)` 右上），不是下面那两个）。
#   理由（跟 jaw 的「咬合 × 侧偏」同一类 —— 次要维的信号只存在于主要的某一档）：`Mouth/Y` 正侧是**噘嘴判据**
#   （要 `mouthPucker ≥ 0.45`），而「整嘴平移」那几段实测 `mouthPucker` 只有 **0.22~0.37** ⇒ Y = 0；
#   反过来噘嘴时嘴是**居中**的（`dimple` 0.072 ⇒ `Mouth/X` ≈ 0）⇒「上移 × 侧移」设备上到不了 ✓
#   ⇒ 剩 **4 格**（下排三点 + 上中一点 = T 形）；"上移+侧移"的输入投影到 T 的两条斜边 ⇒ 一半侧移 + 一半上移。
#   ⚠️ **不能删下面那两个**：下排左/右就是「整嘴左右移动」本体（实测 `mouthLeft/Right` 0.963~0.976）。
$mouthShiftSkip = @('0,1', '2,1')
# **`MouthWidth`（1D 3 格）** 的刻度：轴 = 既有的 `Ho/Drive/Mouth/Pucker`（`2×dimple − pucker`）。
# 9 段补录实测（静态 / 抿嘴嘴宽 / 收嘴不撅）= **−0.95 窄 / −0.16 中 / +0.95 宽**。
# ⚠️ 另一根候选 `(press + shrug)/2` 把「收嘴不撅」排在静态**之下**（0.09 < 0.17）⇒ 反的、弃用。
# ⚠️ 这根轴是 `2×(dimpleL + dimpleR) − pucker`（**2×和**，不是 2×均值）⇒ 宽端量纲是 2（抿嘴嘴宽 = 1.996）。
$mouthWidthMeasured = @(-0.93, -0.09, 2.0)

# ⚠️ **`MouthCore` 现在也是"手工拉的自由点集"**（2026-09-27 用户第二次改，读回来的真值）——
#   他改的是 X1 / X2 那两列（笑那侧），X0 那列没动。**8 个点全给**，坐标一律以这里为准：
#       A3X0Y0 (0,0)          A3X1Y0 (0.482,-0.017)   A3X2Y0 (0.907,-0.024)
#       A3X0Y1 (0,0.4)        A3X1Y1 (0.47,0.336)     A3X2Y1 (0.968,0.267)
#       A3X0Y2 (0,0.75)       （A3X1Y2 挖掉）          A3X2Y2 (0.926,0.596)
#   ⇒ 上面那两条刻度（`$formSmile` / `$openMeasured`）降级成**索引骨架**（槽位名只带 X 的档数）。
#   ⚠️ **变体表 `MouthCoreRoll` 用的是同一份 `over`** ⇒ 两个生成器天然同步；
#      **但资产是他手动改的**，所以每次同步完都要把主版那 8 个坐标**镜像到猫嘴版**
#      （检查器现在有一条"变体/副本必须与主版逐点一致"的硬检查，见 check-controller.ps1）。
$mouthCoreOver = @{
    # 苦那一列（Form −1，2026-09-28 傍晚新增）：与 X0 列对称，作者可再拉
    '0,0' = @(-0.5, 0.0)
    '0,1' = @(-0.5, 0.4)
    '0,2' = @(-0.5, 0.75)
    # 原来那 8 个（用户手拉的真值；索引整体 +1，因为左边多了一列苦）
    '1,0' = @(0.0, 0.0)
    '2,0' = @(0.482, -0.017)
    '3,0' = @(0.907, -0.024)
    '1,1' = @(0.0, 0.4)
    '2,1' = @(0.47, 0.336)
    '3,1' = @(0.968, 0.267)
    '1,2' = @(0.0, 0.75)
    '3,2' = @(0.926, 0.596)
}
# ⚠️ **2026-09-28 砍过 3 格又撤回了 —— 变体表与主版一样保持 8 格**（用户定）。
#    当时看图觉得"猫嘴开着时 `Open` 顶行（0.6 / 0.75）与『中性形 + 张嘴』到不了"（`X0Y1`/`X0Y2`/`X2Y2`），
#    但**常开**（直接写 `Ho/Drive/Style/CatMouth` ≥ 0.30，见 VTS_HQ_CONTROLLER §2.2）会把嘴钉在猫嘴版上，
#    那时任意 Form × Open 组合都会被采到 ⇒ 少一格就是"没烘的那几根键回默认、嘴塌"（WD 开）。
#    用户原话：「还是要留着，因为如果用户加了常开，那些范围还是会采到的」。
#    ⚠️ 教训：**"轴上到不了" ≠ "不会被采到"** —— 变体表的钥匙是那根轴，而常开能绕过轴直接把钥匙转到底。
# 卷唇开关的两个阈值（**2026-09-28 实测重定**，A 批 24 段 + B 批 12 段）：`0.15` = 起点、`0.30` = 猫嘴满档。
# ⚠️ 判据那一次也换了（见 profile 里 `Ho/Drive/Style/CatMouth` 那一行）：`下唇卷 × clamp((嘴角方向 − 0.12)/0.10, 0, 1)`（膝 **2026-09-28 由 0.20 降到 0.12**：用户实机报数 —— 做住 / 张嘴 / 大张 的嘴角方向 0.50 / 0.25 / 0.17，膝 0.20 时后两档只拿到门 0.5 / 0 ⇒ 读数 0.05 / 0，擦着释放线 0.02 ⇒「张嘴立刻就掉回去」）
#    ⇒ **读数换了量纲**，旧的 `0.02 / 0.12`（配"√(上×下) × 死区 × 下颌增益"那套旧读数）不能再用。
#    猫嘴三段实测 **0.434~0.460**（全过满档）；最坏的非目标（常态笑：嘴角门开着但下唇几乎不卷）**0.075**。
$rollTicks = @(0.15, 0.30)
# 1D 片段表（一根轴、孩子是动画片段）：刻度值就是阈值本身（`m_UseAutomaticThresholds: 0`）。
# ⚠️ **2026-09-27 起下巴前伸不做树了**（用户定：「把这个 arkit 输入贬成只用来辅助的变量，
#   他还是参与 arkit 直通就行，我们直接不做这个轴了，中心放到 jaw 的下左右上」）——
#   `Ho/Drive/Mouth/Forward` **那一行留着照旧发布**（当辅助变量/出口，见 §5.4.1 那五条毛病：
#   要张嘴才有值 / 张嘴又给值 / 噘嘴给 0.05 / 满档要噘嘴+前顶 / 安卓不发 —— VB 也是整根丢掉），
#   但**没有树消费它**，跟 `Cheek/*`、`Gaze/*` 一个待遇。那棵 2 格的 `MouthForward` 连同
#   两个片段一起删掉（孤儿规则会清理）。
$simple1D = [ordered]@{
    # 鼻子上顶（2026-09-27 用户定：颊不要、鼻只留这一个状态）—— 只要"不顶 / 顶"两格；
    # 它连带的内眼睑上抬 / 眯眼 / 眉毛内下移由物理共动带出（eyeSquint / browDown 本来就会一起动）。
    'NoseUp'       = @{ p = 'Ho/Drive/Nose/Up';       v = @(0.0, 0.7);       t = 'Up' }   # 0.7 = 实测（挤眼+鼻上抬 5 秒：avg 0.70 / max 0.75）
    # 嘴宽（2026-09-28 傍晚：`MouthWidth` 以 **1D 3 格**回来）—— 轴是**既有的** `Ho/Drive/Mouth/Pucker`。
    'MouthWidth'   = @{ p = 'Ho/Drive/Mouth/Pucker';  v = $mouthWidthMeasured; t = 'Pucker' }
    # 倒V（2026-09-27 加）：**一个固定姿势** —— 形态门在中间层是"维持"出来的 0/1 ⇒ 两档阈值就是 0 / 1。
    # ⚠️ 它吃的是**契约行** `Ho/Drive/Style/InvertedV`（控制器看不见内部行 `Ho/Style/InvertedV`）。
    'InvertedV'    = @{ p = 'Ho/Drive/Style/InvertedV'; v = @(0.0, 1.0);      t = 'InvertedV' }
    # 下巴（2026-09-28 深夜）：**1D 2 格**（咬合/闭 ↔ 张开）—— 见上面 `$mouthJawOver` 位置的退役说明。
    'MouthJaw'     = @{ p = 'Ho/Drive/Mouth/Jaw';      v = $jawOpen;          t = 'Jaw' }
    # 舌头（2026-09-28 深夜）：**1D 4 格** —— 默认（不出舌，左下角）→ 3 步 → 舌伸满；
    # 每格自己把该有的张嘴量烘进去（穿模由作者那 4 段负责）。见上面 `$tongueTicks` 的注释。
    # 轴借 `Ho/Drive/Mouth/TongueL`（那两根本来就是 tongueOut 的占位；做「歪舌头」时量轴换新的 `TongueOut`，
    # 这 4 格的名字与位置都不用动）。
    'MouthTongue'  = @{ p = 'Ho/Drive/Mouth/TongueL';  v = $tongueTicks;      t = 'Tongue' }
}
$tables = [ordered]@{
    'MouthCore'   = @{ x = 'Ho/Drive/Mouth/Form';  y = 'Ho/Drive/Mouth/Open';  xv = $formSmile;  yv = $openMeasured;  xt = 'Form';     yt = 'Open'; skip = $mouthCoreSkip; over = $mouthCoreOver }
    # ⚠️ **下巴 2026-09-28 深夜从 2D 6 格降成 1D 2 格**（只留上下）—— 见上面 `$simple1D` 的 `MouthJaw`。

    # 整嘴平移（2026-09-28 下午新建，傍晚收成 **3×2 = 6 格**）：X = 左右（±0.95）· Y = 上下（0 / +1，负侧钳到 0）。
    'MouthShift'  = @{ x = 'Ho/Drive/Mouth/X'; y = 'Ho/Drive/Mouth/Y'; xv = $shiftXMeasured; yv = $shiftYMeasured; xt = 'LeftRight'; yt = 'UpDown'; skip = $mouthShiftSkip }
    # 舌头（2026-09-28 深夜）：**不在 `$tables` 里** —— 它是 1D 5 格，见上面 `$simple1D` 的 `MouthTongue`。
    # ⚠️ **注视两棵树删掉了**（2026-09-27 用户定「warudo 有单独的 lookat 节点做这个事情，是有 ik 的」）——
    #     4 根 `Ho/Drive/Gaze/*` 轴照旧发布（出口，谁要用谁取）。
    # ⚠️ **颊两棵树删掉了**（2026-09-27 用户定「脸颊鼻子其实不太能在二次元角色上表现」）；
    #     鼻只留一个状态 ⇒ 挪到上面的 1D 片段表（`NoseUp`）。
    # 颊轴（2026-09-27 加）：**鼓嘴**形态用的 2D 表 —— X = 左颊 / Y = 右颊，各两档（不鼓 / 鼓）= 4 格。
    # ⚠️ 与上面删掉的那两棵"颊"树**不是一回事**：那两棵吃 ARKit 的 `cheekSquint`（二次元表现不了），
    #    这张吃的是 `Ho/Drive/Cheek/Left|Right/Puff`（两条**鼓嘴颊轴**，由中间层发布：单边鼓时
    #    嘴唇被推过去 ⇒ `mouthLeft/Right` 0.67~0.87 把左右分开，双鼓时它们 ≈0.03）。姿势还没画（空片段）。
    'Cheek'       = @{ x = 'Ho/Drive/Cheek/Left/Puff'; y = 'Ho/Drive/Cheek/Right/Puff'; xv = $zero1; yv = $zero1; xt = 'PuffL'; yt = 'PuffR' }
}
# Direct 张量积表（2026-09-28 深夜）：眼睑 = 4 情绪 × 3 闭合 的状态点，每格挂一个门
# `Ho/Drive/Lid/<侧>/Gate/<情绪><闭合>`（= w情绪 × w闭合，11 个相加恒 = 1 ⇒ 输出是 11 张姿势的凸组合）。
# "睁大×喜" 不建点：喜证据里那道"不许睁大"的门让它恒为 0（§5.7.36）。
$directTables = [ordered]@{
    'LidL' = @{ side = 'Left';  cells = @('NeutralClosed', 'NeutralOpen', 'NeutralWide', 'HappyClosed', 'HappyOpen', 'AngerClosed', 'AngerOpen', 'AngerWide', 'SadClosed', 'SadOpen', 'SadWide') }
    'LidR' = @{ side = 'Right'; cells = @('NeutralClosed', 'NeutralOpen', 'NeutralWide', 'HappyClosed', 'HappyOpen', 'AngerClosed', 'AngerOpen', 'AngerWide', 'SadClosed', 'SadOpen', 'SadWide') }
}
# 副本表（按键表情）：轴与主版一致。⚠️ **嘴没有副本**（2026-09-27 用户定）：
# 「按键表情版本身对于嘴张嘴笑没有意义」—— 夸张的笑嘴 = `Form` 更大，轴上已经够得到，
# 所以 `MouthCoreExpr` 与它的 1D 开关 `MouthCoreSwitch` 整个删掉（眼/眉的副本照留，那两族
# 表达的是轴上到不了的"性质"：笑眼、怒眉）。
$copies = [ordered]@{ }
# 变体表（**轴驱动的整表分叉**，不是按键表情副本）：与主版同轴、同刻度、同稀疏格，只是整套姿势换成变体版。
# 2026-09-27：卷唇不再当"残差车道"，改成**在两张"笑 × 张"2D 表之间分叉** ——
#   静息嘴（`MouthCore`）↔ 猫嘴版（`MouthCoreRoll`），由 `Ho/Drive/Style/CatMouth` 交叉淡入。
# 这样那个时刻的嘴是**作者画过的两张整嘴表**按权重淡入 ⇒ 混态有人负责，而不是几条残差相加。
# ⚠️ 代价（用户已认）：变体表被选中时必须是**完整嘴姿势** —— WD 开着，没烘的格会让那几根键回默认（嘴塌），
#    所以"作者要画整张嘴"，工具不替他拷贝。
$variants = [ordered]@{ 'MouthCoreRoll' = 'MouthCore' }
# 区域 -> 直接挂在它下面的东西（表名或开关名）
$regions = [ordered]@{
    # ⚠️ 嘴这一格挂的是**开关**（`MouthCoreRollSwitch`），它下面才是两张整嘴表。
    #    这类"删了开关/换了层级忘了重挂"会让整张表变孤儿树（树在、槽位名也对，但状态走不到它）
    #    —— 这坑真栽过一次，是片段探针先撞出来的，检查脚本现在也会从根走一遍可达性。
    'MouthRegion'    = @('MouthCoreRollSwitch', 'MouthJaw', 'MouthShift', 'MouthWidth', 'MouthTongue', 'InvertedV', 'Cheek')
    # 2026-09-27：左右眼并成一个区域（注视两棵树没了，每边只剩眼睑开关）；颊 → 鼻（只剩"鼻子上顶"一个状态）
    'EyeRegion'      = @('LidL', 'LidR')
    'NoseRegion'     = @('NoseUp')
}
$gateOfRegion = [ordered]@{ 'MouthRegion' = 'Mouth'; 'EyeRegion' = 'Eye'; 'NoseRegion' = 'Nose' }
# 区域 -> 子节点挂哪个**权重参数**（默认恒 1 = `Ho/Drive/W/One`）。
# 2026-09-27：`MouthRegion` 里那三个"张嘴 × 笑"的孩子改挂**形态门** `Ho/Drive/Gate/MouthStyle` ——
#   （2026-09-28 当天反复：三→二→三→（傍晚）`MouthCorner` 换成 1D 的 `MouthWidth`，仍是三个）
# 倒V / 鼓嘴亮起来时把整块权重压到 0（中间层把内部行 `Ho/Style/MouthGate` 转发成这个参数）。
# ⚠️ `MouthJaw` / `MouthTongue` 保持恒 1（下巴 / 舌头跟风格化不冲突），两条形态子树也恒 1
#    （它们自己就是"被门放行的东西"，再挂门就套娃了）。
$wOne = 'Ho/Drive/W/One'
$regionWeights = @{
    'MouthRegion' = @{
        'MouthCoreRollSwitch' = 'Ho/Drive/Gate/MouthStyle'

        'MouthShift'          = 'Ho/Drive/Gate/MouthStyle'
        'MouthWidth'          = 'Ho/Drive/Gate/MouthStyle'


    }
}
# 1D 副本树 -> 主版 / 副本 / 用哪个表情门（**按键表情**那一类：门默认 0、中间层不写）
$switches = [ordered]@{
}
# 轴驱动的变体开关（1D 树，两个孩子都是**整张表**，阈值显式写死）。
# 现在只有嘴：`Mouth/Roll` 在 静息嘴 / 猫嘴版 之间分叉（`$rollTicks` = 0.02 / 0.18）。
$variantSwitches = [ordered]@{
    'MouthCoreRollSwitch' = @{ main = 'MouthCore'; variant = 'MouthCoreRoll'; param = 'Ho/Drive/Style/CatMouth'; thr = $rollTicks }
}
# ⚠️ 开关阈值必须显式写死（`m_UseAutomaticThresholds: 0`）：自动模式会**忽略**存下来的阈值、
#    在 [m_MinThreshold, m_MaxThreshold] = [0,1] 上把两档平摊成 0 与 1 ⇒ `0.02 / 0.18` 会被静默改掉。

# ── id 分配（`$script:` 必须：函数里写 `$next++` 只会改局部副本）────────────────
$script:nextTree = 206000000
$script:treeIds = @{}
function TreeId([string]$name) {
    if (-not $script:treeIds.ContainsKey($name)) { $script:treeIds[$name] = $script:nextTree; $script:nextTree++ }
    return $script:treeIds[$name]
}
foreach ($n in @('Ho/00 Drive Tree') + $regions.Keys + $switches.Keys + $variantSwitches.Keys + $tables.Keys + $variants.Keys + $simple1D.Keys + $copies.Keys) { [void](TreeId $n) }

$sb = New-Object System.Text.StringBuilder
function W([string]$line) { [void]$sb.AppendLine($line) }

W '%YAML 1.1'
W '%TAG !u! tag:unity3d.com,2011:'

# ── AnimatorController（模板：老那份的字段集）────────────────────────────────
W '--- !u!91 &9100000'
W 'AnimatorController:'
W '  m_ObjectHideFlags: 0'
W '  m_CorrespondingSourceObject: {fileID: 0}'
W '  m_PrefabInstance: {fileID: 0}'
W '  m_PrefabAsset: {fileID: 0}'
W '  m_Name: PTP_CTR_Face_VTS'
W '  serializedVersion: 5'
W '  m_AnimatorParameters:'
foreach ($n in $params.Keys) {
    W "  - m_Name: $n"
    W '    m_Type: 1'
    W "    m_DefaultFloat: $($params[$n])"
    W '    m_DefaultInt: 0'
    W '    m_DefaultBool: 0'
    W '    m_Controller: {fileID: 9100000}'
}
W '  m_AnimatorLayers:'
W '  - serializedVersion: 5'
W '    m_Name: Ho/00 Drive'
W '    m_StateMachine: {fileID: 110700000}'
W '    m_Mask: {fileID: 0}'
W '    m_Motions: []'
W '    m_Behaviours: []'
W '    m_BlendingMode: 0'
W '    m_SyncedLayerIndex: -1'
W '    m_DefaultWeight: 1'
W '    m_IKPass: 0'
W '    m_SyncedLayerAffectsTiming: 0'
W '    m_Controller: {fileID: 9100000}'

# ── 状态机（一个状态）────────────────────────────────────────────────────────
W '--- !u!1107 &110700000'
W 'AnimatorStateMachine:'
W '  serializedVersion: 6'
W '  m_ObjectHideFlags: 1'
W '  m_CorrespondingSourceObject: {fileID: 0}'
W '  m_PrefabInstance: {fileID: 0}'
W '  m_PrefabAsset: {fileID: 0}'
W '  m_Name: Ho/00 Drive'
W '  m_ChildStates:'
W '  - serializedVersion: 1'
W '    m_State: {fileID: 110200000}'
W '    m_Position: {x: 310, y: 110, z: 0}'
W '  m_ChildStateMachines: []'
W '  m_AnyStateTransitions: []'
W '  m_EntryTransitions: []'
W '  m_StateMachineTransitions: {}'
W '  m_StateMachineBehaviours: []'
W '  m_AnyStatePosition: {x: 50, y: 20, z: 0}'
W '  m_EntryPosition: {x: 50, y: 120, z: 0}'
W '  m_ExitPosition: {x: 800, y: 120, z: 0}'
W '  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}'
W '  m_DefaultState: {fileID: 110200000}'

# ── 状态（WD 必须开：动作里有 Direct）────────────────────────────────────────
W '--- !u!1102 &110200000'
W 'AnimatorState:'
W '  serializedVersion: 6'
W '  m_ObjectHideFlags: 1'
W '  m_CorrespondingSourceObject: {fileID: 0}'
W '  m_PrefabInstance: {fileID: 0}'
W '  m_PrefabAsset: {fileID: 0}'
W '  m_Name: Drive'
W '  m_Speed: 1'
W '  m_CycleOffset: 0'
W '  m_Transitions: []'
W '  m_StateMachineBehaviours: []'
W '  m_Position: {x: 50, y: 50, z: 0}'
W '  m_IKOnFeet: 0'
W '  m_WriteDefaultValues: 1'
W '  m_Mirror: 0'
W '  m_SpeedParameterActive: 0'
W '  m_MirrorParameterActive: 0'
W '  m_CycleOffsetParameterActive: 0'
W '  m_TimeParameterActive: 0'
W "  m_Motion: {fileID: $(TreeId 'Ho/00 Drive Tree')}"
W '  m_Tag: '
W '  m_SpeedParameter: '
W '  m_MirrorParameter: '
W '  m_CycleOffsetParameter: '
W '  m_TimeParameter: '

function WriteTree([string]$name, [int]$blendType, [string]$bp, [string]$bpy, [array]$kids, [switch]$ManualThresholds) {
    W "--- !u!206 &$(TreeId $name)"
    W 'BlendTree:'
    W '  m_ObjectHideFlags: 0'
    W '  m_CorrespondingSourceObject: {fileID: 0}'
    W '  m_PrefabInstance: {fileID: 0}'
    W '  m_PrefabAsset: {fileID: 0}'
    W "  m_Name: $name"
    W '  m_Childs:'
    $t = 0
    foreach ($k in $kids) {
        # 显式阈值（1D 表用）：门控组件永远写 0/1，但我们想让"卷唇量"落在 0 / 0.5 / 1 三档
        $threshold = if ($ManualThresholds -and $null -ne $k.thr) { $k.thr } else { $t }
        W '  - serializedVersion: 2'
        W "    m_Motion: $($k.motion)"
        W "    m_Threshold: $threshold"
        W "    m_Position: {x: $($k.x), y: $($k.y)}"
        W '    m_TimeScale: 1'
        W '    m_CycleOffset: 0'
        W "    m_DirectBlendParameter: $($k.direct)"
        W '    m_Mirror: 0'
        $t++
    }
    W "  m_BlendParameter: $bp"
    W "  m_BlendParameterY: $bpy"
    W '  m_MinThreshold: 0'
    W '  m_MaxThreshold: 1'
    # ⚠️ `Auto = 1` 时 Unity 会**忽略**上面写的阈值、在 [0,1] 上平均分配；1D 表要显式三档就关掉它
    W $(if ($ManualThresholds) { '  m_UseAutomaticThresholds: 0' } else { '  m_UseAutomaticThresholds: 1' })
    W '  m_NormalizedBlendValues: 0'
    W "  m_BlendType: $blendType"
}

# 槽位（空动画片段）：
$script:clips = New-Object System.Collections.Generic.List[object]

# 槽位名 -> 稳定的 GUID（md5）：重新生成时同名片段拿到同一个 GUID，控制器里的引用不会乱跳。
function SlotGuid([string]$name) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes('ho-face-slot:' + $name)
    return ([System.BitConverter]::ToString($md5.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
}

# 叶子 Direct 树的名字：唯一来源 HoSlotNames.psd1（改词只改那个文件）
$script:slotNames = Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'HoSlotNames.psd1')
function SlotDisplay([string]$tree, [int]$i, [int]$j) {
    $t = $script:slotNames[$tree]
    if ($null -eq $t) { throw "HoSlotNames.psd1 里没有 $tree 的名字" }
    if ($t.Count -gt 0 -and $t[0] -is [System.Array]) { return $t[$i][$j] }
    return $t[$i]
}

function SlotKids([string]$tree, [array]$xv, [array]$yv, [string]$xt, [string]$yt, $skip = $null, $over = $null) {
    $kids = @()
    foreach ($j in 0..($yv.Count - 1)) {
        foreach ($i in 0..($xv.Count - 1)) {
            # 稀疏表：跳过"物理上到不了"的格子（不给它建片段，圈也就跟着斜切）
            if ($skip -and ($skip -contains "$i,$j")) { continue }
            # 逐格坐标覆盖（`over['i,j'] = @(x, y)`）：实测说这一格不在刻度值上时单独给坐标。
            # ⚠️ 槽位名是**按索引**编的（`A3X<i>Y<j>`），所以挪坐标**不改名、不用重烘**。
            $x = $xv[$i]
            $y = $yv[$j]
            if ($over -and $over.ContainsKey("$i,$j")) { $x = $over["$i,$j"][0]; $y = $over["$i,$j"][1] }
            $name = ("{0}__{1}__{2}__A{3}X{4}Y{5}" -f $tree, $xt, $yt, $xv.Count, $i, $j)
            # 叶子 = 一棵**空**的 Direct 树（中文状态名来自 HoSlotNames.psd1），孩子由作者手填
            $display = SlotDisplay $tree $i $j
            if (-not $display) { continue }
            WriteTree $display 4 'Blend' 'Blend' @(@{ motion = '{fileID: 0}'; x = 0; y = 0; direct = $wOne })
            $kids += @{
                motion = "{fileID: $(TreeId $display)}"
                x      = $x
                y      = $y
                direct = 'Blend'
                name   = $display
            }
        }
    }
    return $kids
}

# 1D 表的槽位（`<树名>__<轴语义>__A<n>X<i>`，见命名权威 §5）：阈值就是刻度值本身。
function SlotKids1D([string]$tree, [array]$xv, [string]$xt) {
    $kids = @()
    for ($i = 0; $i -lt $xv.Count; $i++) {
        $name = ("{0}__{1}__A{2}X{3}" -f $tree, $xt, $xv.Count, $i)
        $display = SlotDisplay $tree $i 0
        if (-not $display) { continue }
        WriteTree $display 4 'Blend' 'Blend' @(@{ motion = '{fileID: 0}'; x = 0; y = 0; direct = $wOne })
        $kids += @{
            motion = "{fileID: $(TreeId $display)}"
            x      = 0
            y      = 0
            direct = 'Blend'
            thr    = $xv[$i]
            name   = $display
        }
    }
    return $kids
}

# Direct 表的槽位（`<树名>__Gate__<情绪><闭合>`）：每个孩子挂自己的门参数。
function SlotKidsDirect([string]$tree, [string]$side, [array]$cells) {
    $kids = @()
    foreach ($cell in $cells) {
        $name = ("{0}__Gate__{1}" -f $tree, $cell)
        $display = SlotDisplay $tree $cells.IndexOf($cell) 0
        if (-not $display) { continue }
        WriteTree $display 4 'Blend' 'Blend' @(@{ motion = '{fileID: 0}'; x = 0; y = 0; direct = $wOne })
        $kids += @{
            motion = "{fileID: $(TreeId $display)}"
            x      = 0
            y      = 0
            direct = "Ho/Drive/Lid/$side/Gate/$cell"
            name   = $display
        }
    }
    return $kids
}

# ── 输出树：先 2D 表（含副本与变体）、1D 片段表，再开关，再区域，最后根 ────────
foreach ($t in $directTables.Keys) {
    $spec = $directTables[$t]
    WriteTree $t 4 'Blend' 'Blend' (SlotKidsDirect $t $spec.side $spec.cells)
}
foreach ($t in $tables.Keys) {
    $spec = $tables[$t]
    WriteTree $t 3 $spec.x $spec.y (SlotKids $t $spec.xv $spec.yv $spec.xt $spec.yt $spec.skip $spec.over)
}
foreach ($t in $simple1D.Keys) {
    $spec = $simple1D[$t]
    WriteTree $t 0 $spec.p 'Blend' (SlotKids1D $t $spec.v $spec.t) -ManualThresholds
}
foreach ($c in $copies.Keys) {
    $spec = $tables[$copies[$c]]
    WriteTree $c 3 $spec.x $spec.y (SlotKids $c $spec.xv $spec.yv $spec.xt $spec.yt $spec.skip $spec.over)
}
# 变体表：与主版**同轴同刻度同稀疏格**（只是姿势换整套），所以直接复用主版的 spec
foreach ($v in $variants.Keys) {
    $spec = $tables[$variants[$v]]
    WriteTree $v 3 $spec.x $spec.y (SlotKids $v $spec.xv $spec.yv $spec.xt $spec.yt $spec.skip $spec.over)
}
foreach ($sw in $switches.Keys) {
    $s = $switches[$sw]
    WriteTree $sw 0 "Ho/Drive/Gate/Expr/$($s.gate)" 'Blend' @(
        @{ motion = "{fileID: $(TreeId $s.main)}"; x = 0; y = 0; direct = 'Blend' },
        @{ motion = "{fileID: $(TreeId $s.copy)}"; x = 0; y = 0; direct = 'Blend' })
}
# 轴驱动的变体开关：两个孩子都是整表，阈值 = 该轴上的实测档位
foreach ($sw in $variantSwitches.Keys) {
    $s = $variantSwitches[$sw]
    WriteTree $sw 0 $s.param 'Blend' @(
        @{ motion = "{fileID: $(TreeId $s.main)}";    x = 0; y = 0; direct = 'Blend'; thr = $s.thr[0] },
        @{ motion = "{fileID: $(TreeId $s.variant)}"; x = 0; y = 0; direct = 'Blend'; thr = $s.thr[1] }
    ) -ManualThresholds
}
foreach ($r in $regions.Keys) {
    $kids = @()
    foreach ($child in $regions[$r]) {
        # 权重参数：默认恒 1；`$regionWeights` 里点名的那几个改挂形态门（见上面的注释）
        $w = $wOne
        if ($regionWeights.ContainsKey($r) -and $regionWeights[$r].ContainsKey($child)) { $w = $regionWeights[$r][$child] }
        $kids += @{ motion = "{fileID: $(TreeId $child)}"; x = 0; y = 0; direct = $w }
    }
    WriteTree $r 4 'Blend' 'Blend' $kids
}
$rootKids = @()
foreach ($r in $regions.Keys) { $rootKids += @{ motion = "{fileID: $(TreeId $r)}"; x = 0; y = 0; direct = "Ho/Drive/Gate/$($gateOfRegion[$r])" } }
WriteTree 'Ho/00 Drive Tree' 4 'Blend' 'Blend' $rootKids

[System.IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
"controller -> $Out"

# ── 空动画片段：每个槽位一份 `<槽位名>.anim` + `.meta`（GUID 由槽位名决定，稳定）────────
$clipCount = 0
if (-not $NoClips) {
    if (-not (Test-Path -LiteralPath $ClipFolder)) { [void](New-Item -ItemType Directory -Force -Path $ClipFolder) }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    foreach ($clip in $script:clips) {
        $clipPath = Join-Path $ClipFolder ($clip.Name + '.anim')
        # ⚠️ **已存在的片段不覆盖**：作者可能已经把姿势烘进去了。要重新铺一遍得显式给 -ForceClips。
        if ((Test-Path -LiteralPath $clipPath) -and -not $ForceClips) { $clipKept++; continue }
        $lines = @(
            '%YAML 1.1',
            '%TAG !u! tag:unity3d.com,2011:',
            '--- !u!74 &7400000',
            'AnimationClip:',
            '  m_ObjectHideFlags: 0',
            '  m_CorrespondingSourceObject: {fileID: 0}',
            '  m_PrefabInstance: {fileID: 0}',
            '  m_PrefabAsset: {fileID: 0}',
            "  m_Name: $($clip.Name)",
            '  serializedVersion: 7',
            '  m_Legacy: 0',
            '  m_Compressed: 0',
            '  m_UseHighQualityCurve: 1',
            '  m_RotationCurves: []',
            '  m_CompressedRotationCurves: []',
            '  m_EulerCurves: []',
            '  m_PositionCurves: []',
            '  m_ScaleCurves: []',
            '  m_FloatCurves: []',
            '  m_PPtrCurves: []',
            '  m_SampleRate: 60',
            '  m_WrapMode: 0',
            '  m_Bounds:',
            '    m_Center: {x: 0, y: 0, z: 0}',
            '    m_Extent: {x: 0, y: 0, z: 0}',
            '  m_ClipBindingConstant:',
            '    genericBindings: []',
            '    pptrCurveMapping: []',
            '  m_AnimationClipSettings:',
            '    serializedVersion: 2',
            '    m_AdditiveReferencePoseClip: {fileID: 0}',
            '    m_AdditiveReferencePoseTime: 0',
            '    m_StartTime: 0',
            '    m_StopTime: 0.016666668',
            '    m_OrientationOffsetY: 0',
            '    m_Level: 0',
            '    m_CycleOffset: 0',
            '    m_HasAdditiveReferencePose: 0',
            '    m_LoopTime: 0',
            '    m_LoopBlend: 0',
            '    m_LoopBlendOrientation: 0',
            '    m_LoopBlendPositionY: 0',
            '    m_LoopBlendPositionXZ: 0',
            '    m_KeepOriginalOrientation: 0',
            '    m_KeepOriginalPositionY: 1',
            '    m_KeepOriginalPositionXZ: 0',
            '    m_HeightFromFeet: 0',
            '    m_Mirror: 0',
            '  m_EditorCurves: []',
            '  m_EulerEditorCurves: []',
            '  m_HasGenericRootTransform: 0',
            '  m_HasMotionFloatCurves: 0',
            '  m_Events: []'
        )
        [System.IO.File]::WriteAllLines($clipPath, $lines, $utf8)
        # 带上 .meta（GUID 必须与控制器里的引用一致；Unity 直接认这种写好的 meta）
        $meta = @(
            'fileFormatVersion: 2',
            "guid: $($clip.Guid)",
            'NativeFormatImporter:',
            '  externalObjects: {}',
            '  mainObjectFileID: 7400000',
            '  userData: ',
            '  assetBundleName: ',
            '  assetBundleVariant: '
        )
        [System.IO.File]::WriteAllLines(($clipPath + '.meta'), $meta, $utf8)
        $clipCount++
    }

    # 孤儿片段：表变稀疏（挖掉死角）之后，被去掉的格子会留下没人引用的 `.anim` ⇒ 删掉。
    # 只删**符合槽位命名**的文件（`*__A*X<i>[Y<j>].anim`），别的文件一律不碰。
    $live = @{}
    foreach ($clip in $script:clips) { $live[$clip.Name + '.anim'] = $true }
    $orphans = 0
    foreach ($file in Get-ChildItem -LiteralPath $ClipFolder -Filter '*.anim') {
        if ($file.Name -notmatch '__A\d+X\d+(Y\d+)?\.anim$') { continue }
        if ($live.ContainsKey($file.Name)) { continue }
        Remove-Item -LiteralPath $file.FullName -Force
        if (Test-Path -LiteralPath ($file.FullName + '.meta')) { Remove-Item -LiteralPath ($file.FullName + '.meta') -Force }
        $orphans++
    }
    "clips -> $ClipFolder  (新建 $clipCount · 保留已有 $clipKept · 删掉孤儿 $orphans)"
}
"  params=$($params.Count) trees=$($script:treeIds.Count) slots=$($script:clips.Count) clips=$clipCount kept=$clipKept"
