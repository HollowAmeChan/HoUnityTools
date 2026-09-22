# Ho 约束面板设计系统

约束类组件的 Inspector（眨眼 / 注视 / 摆锤…）共用一套**栅格 + 自绘控件**。
设计稿：`.design/blink-panel.html`（浏览器直接打开，左边是目标外观，右边是令牌清单）。

## 为什么不用 `EditorGUILayout.*` 直接堆

| 问题 | 表现 | 根因 |
| --- | --- | --- |
| 同一列在不同行宽度不同 | 面板看着"乱"，控件左右不对齐 | GUILayout 按**剩余宽度百分比**分配空间，label 默认吃掉面板 40% |
| 一个 `0.35` 占半行 | 数字输入框被拉得很长 | 同上：字段是"可伸缩"的，没人给它定宽 |
| 横条/滑杆铺满整行 | 一个标量占掉一行，纵向浪费 | Unity 的 `Slider` 天生铺满 |
| 层次不清 | 标题、参数、读数、说明都是同一号灰字 | 没定义文字分级 |

所以：**面板里不再用自动布局排参数行**，每一行自己算矩形。

## 令牌（`HoConstraintEditorTheme`）

尺寸：

| 令牌 | 值 | 用途 |
| --- | --- | --- |
| `RowHeight` / `RowHeightTight` | 20 / 18 | 参数行 / 紧凑行 |
| `ControlHeight` | 18 | 控件本体高度（行内垂直居中） |
| `Gutter` | 6 | 控件间距 |
| `Indent` | 12 | 一层缩进 |
| `SectionHeight` | 28 | 分区头 |
| `LabelWidth` / `Sm` / `Xs` | 56 / 30 / 18 | 标签列三档 |
| `FieldWidth` | 42 | 数字格 |

颜色：卡片 `#3F3F3F`、描边 `#2C2C2C`、输入井 `#2E2E2E`、文字三级（主 / 次 / 弱）、
分区强调色（网格蓝 / 眨眼绿 / 规则紫 / 调试灰）、读数色（驱动青 / 输出绿）、提醒橙、错误红。
全部有浅色皮肤分支。

样式：`Label / Value / Caption / Bold / SectionTitle / SectionSummary / Field / FieldMissing /
NumberField / SegmentOn / SegmentOff / Button / ButtonPrimary / ButtonDanger / Card / CardAlt / IconButton`。

贴图：运行时生成的 4×4（九宫格描边）与 64×1（分区头渐变），`HideFlags.HideAndDontSave`，**不落盘、不进包**。

## 控件（`HoConstraintEditorControls`）

| 控件 | 说明 |
| --- | --- |
| `Section(ref bool, 标题, 摘要, 强调色)` | 28px 分区头：左侧 3px 色条 + 主色渐变 + 右摘要，整行可点折叠 |
| `Card()` / `Card(alt)` | 1px 描边卡片，自动内边距；规则/目标各一张 |
| `Row(tight)` / `Indent()` | 一行 / 一层缩进 |
| `Next(w)` / `NextFlexible(min)` / `NextAuto(文字, 样式)` | 申请固定 / 伸缩 / 按内容宽度 |
| `Label / Caption / ValueText` | 三级文字 |
| `NumberField(value, unit)` | 定宽数字格，单位画在尾部（不占输入区） |
| `Toggle(文字, 值)` | 14px 方块 + 勾 |
| `Segmented(rect, 下标, 选项)` | **2~4 个选项直接点**，比下拉少一次点击 |
| `Dropdown(rect, 下标, 选项)` | 选项多时（如 ramp 预设 7 档）用菜单 |
| `MiniSlider(值, min, max, 默认值)` | 10px 矮滑杆，拖动改值、双击回默认 |
| `Meter(rect, 值, min, max, 色, ghost)` | 实时横条：单极从左长、双极从中轴长；`ghost` 画弹簧前的原始值刻度 |
| `MeterRow(...)` | `标签 [横条] 数值` 一整套，固定宽度，可塞在行尾 |
| `KeyField(...)` | 键名输入 + ▾ 菜单 + **状态点**（绿点带绑定数 / 红叉 = 网格上没这个键） |
| `Button / IconButton / InlineFoldout / Pill / Separator` | 其余零件 |

## 排版规则（改面板时照这个来）

1. 一行最多 3~4 个控件；每个控件要么定宽（`Next(w)`），要么是行内唯一一个伸缩项（`NextFlexible`）。
2. 标签只用三档宽度；标签文字短（2~3 字），完整含义写 tooltip。
3. 读数（横条 / 值）挂在一行的**行尾**，不单独占行；要说明的读数进工具提示。
4. 不常动的参数一律进「细节」折叠；分区默认折叠，摘要里写清关键状态（键名、条数、开关）。
5. 状态用颜色说（绿 = 解析到了、红 = 缺失、橙 = 输入框描边），不要用一长串文字。
6. 新增面板时不要各写各的颜色和宽度，一律从这里取。

## 迁移进度

| 面板 | 状态 |
| --- | --- |
| `HoBlinkConstraintEditor` | ✅ 已按本系统重写 |
| `HoLookAtConstraintEditor` / `HoPendulumConstraintEditor` | ⏳ 仍用旧的 `HoConstraintEditorSectionGui`（分区头 + `NarrowLabels`），后续迁移 |
