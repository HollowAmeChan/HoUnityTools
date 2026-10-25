# Rig Constraint IR v3 导入

HoTools 的正式字段定义见其 `Exporter/IR_V3.md`。HoFBX 导入处理现在接受 IR v2/v3；旧 v2 的 unknown 不自动升级。

## 已知类型与后端

v3 的 Copy Location、Copy Rotation、Armature 进入 DIRECT_CONSTRAINT。参数超出当前能力时显示“已识别但不支持／数据无效”，保留源事实。

有 Ho 属性标记且签名匹配的相邻 Damped Track＋Locked Track 形成独立 AIM_BINDING。Aim 只旋转 owner，使用该操作进入时的位置、target、up 和带符号轴；与网格三角形、LBS 和位置绑定无关。

| 语义 | 内置 | HoAuxRig | VRC |
| --- | --- | --- | --- |
| Copy Location | PositionConstraint | 位置插值 | VRCPositionConstraint |
| Copy Rotation | RotationConstraint | Slerp | VRCRotationConstraint |
| Armature | 多源 Parent 刚性近似 | 加权仿射矩阵 | 多源 VRCParent 刚性近似 |
| Aim | AimConstraint | 独立旋转求解 | VRCAimConstraint |

内置 0.3 旋转插值已测与 Blender Slerp 有差异；非端点／非 0.5 的原生旋转混合标为近似。VRC 也不预先保证 Blender Slerp 等价。Armature 的多源 Parent 映射不保留完整缩放／剪切及动态约束前父级语义。

## 使用边界

- 通用复制首版支持当前骨架、完整 XYZ、WORLD→WORLD、无反相／offset／tail／B-Bone；旋转为 REPLACE。
- Armature 首版为线性混合、无 Preserve Volume／Envelope／Current Location、完整或零影响度。原始多源权重在后端以双精度总和归一化。
- owner 基础输入来自静置局部姿态与动态父级，要求单位 poseBasis、FULL 继承，不支持 owner 独立动画；复制位置／Armature 不作用于连接骨。来源骨与父级可以动画。
- Ho 不跨既有 Aux/MCH 层与新通用栈反馈重排。不能表示的剪切、退化旋转输入或 Aim 会诊断，不当成精确 TRS。
- 需要与最终 FBX 绑定状态配套，不能把另一份应用姿态／MCH 前后的 JSON 混用。HoFBX 的 Y/X 骨轴声明也用于稀疏骨架校验。

未标记 Damped／Locked 继续 unknown。位置和 Aim 分开认领：普通 Copy Location 不需要 Aim 标记；Aim 也可以完全没有位置约束。

已有手工跟踪对可先在 Blender 姿态模式选中对应骨骼，在骨骼属性的 HoTools 面板点击“登记现有跟踪对为 Aim”，再重新导出。仅登记元数据，不按名字自动猜测，不改现有姿态和位置约束。

## 预览、重导入与清理

预览保留原始栈序，区分可映射、原生近似、设置不支持、数据无效、静音／零影响和 unknown。同 owner 有影响当前通道的未知栈屏障时，不跨过它折叠。独立 Copy Location 可与仅写旋转的未标记 Damped／Locked 共存，执行端只拥有位置，不恢复或覆盖外部旋转／缩放。来源骨仍有未实现约束时，会提示读取 Unity 当前姿态，不能据导入数量宣称整张图完整。

阶段 helper 与 VRC 挂点分别记录所有权；不会覆盖用户同类型标准约束。重导入、切模式、清理会保留手工子物体及未登记组件。装配前检查骨引用、绑定、环和 VRC API；意外失败通过同一撤销组恢复先前导入。全部已知约束显式静音／零影响时允许清除先前活动导入。

HoAuxRig 继续保留旧层与更新模式，v3 通用栈另存于同一组件；引用及绑定矩阵可序列化，丢失引用时按保存路径恢复。没有新增面向 VRC 的自定义运行时脚本。

## 验证

`Tests~/ConstraintIRV3/Run.ps1` 新建隔离 Unity 工程。覆盖 v2/v3 验证、Aim 标记、Prefab 引用恢复、顺序复制、动态父级、稀疏骨架、重复导入、VRC 多源／overflow 序列化、手工内容保护和装配异常回滚。

真实 WTD fixture 由 HoTools 的 `tests/export_wtd_ir_v3_fixture.py` 在后台提取 C1 闭包及实际蝴蝶结网格，不保存原工程。数值检查包括首次／稳定求值；非均匀父缩放压力样本单列，不算等价通过。

VRC API shim 仅验证装配路径，不代替真实 SDK／客户端测试。Warudo 的宿主时序及物理链仍需实机验证。三点／LBS 生成工具不属于本轮实现。
