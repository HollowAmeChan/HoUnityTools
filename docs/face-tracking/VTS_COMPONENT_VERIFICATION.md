# VTS 角色组件验证记录

日期：2026-10-29。本文只记录实际执行结果；最终迁移目标见 [组件架构](VTS_COMPONENT_ARCHITECTURE.md)。

## 环境与入口

- Unity 2021.3.45f2。
- 原工程使用的 Warudo SDK 包：Warudo-Mod-Tool-0.14.4.8，UMod 构建元数据版本 2.9.9 / 0.14.4。
- Tests~/VtsComponents/Run.ps1：轻量、带 .ho-vts-validation 标记的一次性工程，真实 Unity 播放模式验证。
- Tests~/VtsComponents/RunPackaging.ps1：带 .ho-vts-packaging-validation 标记的一次性 SDK 工程，真实 FastBuild、UMod 构建及产物加载。
- 构建输出仅在验证工程的 Artifacts 中，不覆盖 Warudo 安装目录里的角色或插件。

本次 SDK 验证工程为 D:/Unity_Fork/HoUnityTools/.research/HoVtsPackaging-2021。

## Unity 播放模式

105 项断言通过，覆盖：

- 完整输入字典复制、未知键、有正负值的姿态、非有限值处理。
- 新鲜度与 FaceFound、丢追规则中性值、来源释放、多接收器歧义、禁用状态。
- 自动 LateUpdate 超时、原始数据保留、包间平滑/延迟/维持、拒绝倒退时钟、重复读取不推进状态。
- 延迟到期前不提前发布未来值；适配器停发后无需再送一帧，也能在真实帧循环中使头姿回中性。
- 混合树驱动、常量默认参数、零值写回、位移/缩放按轴写入、四元数动画。
- 未接管的形态键和骨骼属性保留、组件释放与恢复、多角色隔离、预制件序列化。
- 无 Animator/Avatar 的头眼旋转、参考坐标系与不同骨骼轴向、500 次重复求值无累加。
- 动态基准姿势、独立交还头部/双眼、按秒平滑、丢追回中性、缺轴保持、显式参数层选择。

## 真实 UMod 构建

调用生产 FastBuild 的源码闭包收集、暂存、准备预制件、脚本重绑、官方构建和产物复核流程。

- 实际加入编译：57/57 个运行时脚本。
- 编译类型：193 个。
- HoVtsParameterReceiver、HoVtsController、HoVtsLookAt 三个挂载均确认链接到本次 Mod 程序集。
- 规则、控制器引用、输入中枢引用、输出根与头眼骨骼引用在重绑后保留。
- 运行时源码通过 UMod 官方构建及安全检查。
- 复核完成后清理暂存脚本，不留下与后续构建冲突的重复类型。

本次产物：Artifacts/HoVtsComponentsValidation.warudo。

SHA-256：36501653D80FE6209C172074013985D7D1209BD1A706A988C1CBEB349B91D43E。

## 加载产物后运行

在第二个 Unity 进程进入播放模式，通过 UMod.Mod.Load 实际加载上述产物并实例化 Character。

- 三个组件使用产物里的 Mod 类型，与包内同全名类型确实不是同一个 Type。
- 没有 Missing Script，三个组件来自同一个角色 Mod 程序集。
- 生产 HoVtsFrameDelivery 由独立程序集发送，实际 Mod 接收器确认接收。
- 角色包内规则把 JawOpen=0.6 求值为 Drive=0.6，发送方复用字典不会改掉角色已收的数据。
- 包内控制器与动画在角色 Animator 禁用时仍能写出形态键 50。
- 姿态组件从同一接收器读取头姿并写入角色自己的骨骼。
- 释放输入后控制器和姿态组件停止写入，形态键与骨骼恢复。

结果标记：HO_VTS_PACKAGING PASS、HO_VTS_MOD_RUNTIME PASS。

## 仍需完成

本记录不证明 WTD 在 Warudo 客户端中的实际表现，也不覆盖旧调试界面迁移、设备标定默认范围或旧路线清理。这些项目仍按架构文档继续推进。
