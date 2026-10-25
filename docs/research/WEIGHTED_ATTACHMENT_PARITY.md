# 手工加权附着框架：Blender／Unity 验证

日期：2026-10-25。状态：可行性原型，生产 HoFBX 导入器尚未接线。

契约提案正本放在 HoTools 仓库的 `Exporter/WEIGHTED_ATTACHMENT_CONSTRAINT_CONTRACT.md`。本页记录 Unity 侧结果和后续接线边界。

后续认领方案改为三类常用约束先直通，见 [常用约束直通方案](DIRECT_CONSTRAINT_IMPORT_PLAN.md)；完整 frame 标记解析不再作为 Copy／Armature 类型认领的前置条件。未标记 Damped／Locked 仍保持 unknown。

## 样例与验收标准

源为 `WTD-summer.blend / Armature / CTR_C1_ChestAddon` 的磁盘版本。参考点是作者手工填写权重的虚拟点；不要求对应真实网格顶点，也不改作者的 chest 父级。

提取 12 根骨、13 条约束，以及受输出骨控制的“上衣蝴蝶结”网格（原始顶点数 1911）。Blender 4.5.8 LTS 生成预期值，Unity 6000.3.15f1 实际导入 FBX 并在 PlayMode 求值；CPU BakeMesh 校验 24 个真实网格探针。

60 组姿态中，59 组旋转／平移／均匀缩放属于本轮支持范围，1 组非均匀缩放是单列的压力样本。每组比较第一轮和第六轮原生约束求值。root_motion 另叠加外层物体的平移、旋转和均匀缩放。

| 路线 | 非压力样本最大位置误差（含网格） | 最大朝向误差 | 结论 |
| --- | ---: | ---: | --- |
| 保留原样：Armature 点原型 + Unity 原生 Position/Aim/Rotation | 约 0.01264 mm | 约 0.01528° | 通过，两种采样时机一致 |
| 固定绑定点对照：Unity 纯原生约束 | 约 0.01137 mm | 约 0.01684° | 通过对应的 Blender 对照副本；不是原工程等价替换 |

位置阈值 0.02 mm、角度阈值 0.05°、缩放向量误差阈值 0.0002。数值断言通过。非均匀缩放原样路线的网格最大偏差约 3.42 mm，暂不保证等价。

## 原样路线为何需要点求值器

Blender 的 Armature 约束对约束前的 owner 位置应用加权目标骨变换。当前 REF 骨保留 chest 父级，因此不能改成各源骨下的固定偏移点后仍宣称原样导出。

验证原型 `HoArmaturePointProbe` 仅求四根 REF 的位置，保留父级的约束前影响；后续位置、Aim 和双源旋转混合由真实 Unity 原生约束计算。所有绑定矩阵和点在 rigSpace 计算，外层物体变换只施加一次。

它是实验脚本，不是已经加入本包的生产运行时组件，不可作为自定义脚本上传 VRC Avatar。

## 时序与轴向

- 左侧框架使用 L→P、L→U，右侧使用 P→R、P→U。实际 Aim X、Up -Z。保存作者取点顺序及带符号轴，不根据名称镜像。
- 原生 Aim 的 upTarget 同时以零权重 source 记录依赖。此次发现只填写 WorldUpObject 会造成动态输入时读到旧参考点；补依赖后第一轮与稳定结果一致。
- 自定义点求值安装在 `ConstraintManagerUpdate` 之前。单独放到 LateUpdate 曾造成下游读旧点，不能照搬。
- 网格校验使用 BakeMesh 的 scale compensation 后再 TransformPoint，避免把外层均匀缩放算两次。

## 当前导入器的缺口

1. Blender IR v2 会完整记录这 13 条 unknownConstraints，其中 Armature 多源位于 references.targets。
2. Unity 的 BlenderConstraintInfo 未读取 references，parameters 也缺少 Armature／跟踪轴字段。
3. BuildNeutralIrPlan 只认领已有 Parent/MCH、Twist、Fan 等关系；该样例目前仍会跳过。
4. 应新增有边界的 weighted-attachment-frame profile、完整图识别与逐后端能力预览；不要恢复任意未知约束的盲目导入。
5. HoFBX 的应用姿态、MCH、轴基底和绑定矩阵采样阶段必须一致；本次只验证原始静置闭包 FBX，默认预处理组合待补回归。

## 未验证范围

- 未运行 VRC SDK／客户端，未证明保留动态 owner 父级的 Armature 点已能用 VRC 原生约束完整降级。
- 未启动 Warudo，未验证其动捕／PhysBone／其他物理组件的时序。
- 本机 Unity 2021.3.18f1 因许可证未激活没有运行，不能据此声称兼容旧版本。
- 无非均匀缩放、负缩放、剪切、退化三角框架、任意影响度／部分轴／跨骨架的通用保证。

验证包提供两份 Prefab：`authored_armature` 为原样候选，含自定义点原型；`native_fixed_bind` 是对照组。原 `.blend`、用户打开的 Unity 场景和现有导入器均未修改。

参考：[Unity 原生约束求值阶段](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerLoop.PreLateUpdate.ConstraintManagerUpdate.html)、[BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html)。
