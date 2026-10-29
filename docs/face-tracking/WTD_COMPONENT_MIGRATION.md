# WTD 角色组件接入记录

日期：2026-10-30。工作工程：D:/Unity_Project/BreakWarudo，Unity 2021.3.45f2。

## 已保存的角色配置

预制件 Assets/Hollow/WTD/WTD-BUILD.prefab 新增 HoVTS 空物体：

- HoVtsParameterReceiver：内嵌 FT/ho-iPhoneVTS.arkit.hoface.json 的 74 条输入与 66 条输出规则。
- HoVtsController：显式引用该接收器、FT/CTR_Face_ARKit.controller 和角色输出根物体。控制器有 104 个动画片段，共 208 个不同的动画属性绑定，全部经过目标路径与形态键验证。
- HoVtsLookAt：引用同一接收器，姿态参考为角色根，骨骼为 head、eye.L、eye.R；组件禁用且 AutoRun=false，继续由现有官方姿态链控制头眼。

控制器的 54 个参数中，52 个由 ARKit 输出驱动，Ho/Drive/W/One 与 Ho/Drive/Gate/Arkit 保留控制器默认值 1。原动画、控制器、规则 JSON 未修改，原 Animator 状态和控制器引用未修改，预制件 GUID 未修改。

迁移前完整预制件备份：D:/Unity_Project/BreakWarudo/Logs/HoVtsMigration/20261029-161439/WTD-BUILD.prefab，旁边保留原 .meta 和 result.txt。目录时间使用 UTC。逐条比较发现原有 1688 条 YAML 对象记录全部一致，仅新增 5 条记录（空物体、Transform、三个组件）。

主工程原本的 WTD 预制件与整个 FT 文件夹均为 Git 未跟踪资产。本次没有将这些既有作者资产批量纳入提交；实际角色修改已保存在上述工作工程中，包仓库提交迁移探针和本记录。

## 原生播放验证

通过 Tests~/VtsComponents/WtdMigration.cs 的 RunPlayBatch 在真实工作工程进入播放模式，实例化迁移后的 WTD。测试禁用角色原有 Animator 和其他行为，只保留接收器及独立控制器链，避免身体动画或其他脚本污染表情测试。

依次将 52 个 VTS 表情输入从 0 推到 1、再回到 0。52/52 通道均产生形态键变化；每次独立求值都成功，绑定属性数仍为 208。释放输入后独立求值图释放、停止写入。结果标记：HO_VTS_WTD_MIGRATION PASS、HO_VTS_WTD_RUNTIME PASS。

报告在主工程 Logs/vts-wtd-runtime-result.txt；迁移和播放日志分别为 Logs/vts-wtd-migration.log、Logs/vts-wtd-runtime.log。一次性 Editor 执行脚本已移除。

## 文件指纹

SHA-256：

| 文件 | 值 |
| --- | --- |
| WTD-BUILD.prefab | CAC83B3597002FD891F77BBA72CAA39D6E7F6FF633DC5CCEF91873D68C50BB92 |
| WTD-BUILD.prefab.meta | 376D13D80492B81E7D7ED4640A2B50C6FAB9E8353D9F5D99366B38F0E710742A |
| CTR_Face_ARKit.controller | 1612CCFFA29FA5278950FE285FED1891525E5D77B11820C485EE5A71B6CC270B |
| ho-iPhoneVTS.arkit.hoface.json | 3958D4851F409FB00ED72C7E33A5E2C74944570DCBA734EA662BE01152345648 |

## 实际 Character Mod 构建与加载

Tests~/VtsComponents/WtdPackaging.cs 在实际工作工程中调用生产 FastBuild 的依赖收集、暂存源码、预制件准备与重绑、UMod 官方构建和产物复核。

- 57/57 个脚本实际加入编译，193 个类型。
- 33/33 个组件链接确认，三个 HoVts 组件均在实际角色 Mod 程序集中。
- 导出副本保留 208 个动画属性、74/66 条规则及组件间引用。
- UMod 产物由第二个 Unity 进程实际加载；使用分发的角色 Mod 类型和生产 HoVtsFrameDelivery 交付输入。
- 禁用原 Animator 和其他写入者后，52/52 表情通道在真实 Mod 模型中产生形态键变化；释放输入后求值图停止。
- 构建后源预制件与构建前快照完全相同，ExportSettings 字节级恢复一致，Assets 中暂存源码数为 0；一次性 Editor 执行脚本已清除。

产物：D:/Unity_Project/BreakWarudo/Logs/HoVtsWtdPackaging/Artifacts/WTD.warudo。

SHA-256：C367972FCD45B538F31192CCF60EA4745BB26EFE470FFA6765A1F9042022AD1C。

构建报告：Logs/HoVtsWtdPackaging/result.txt；实际加载报告：Logs/HoVtsWtdPackaging/runtime-result.txt。标记为 HO_VTS_WTD_PACKAGING PASS 与 HO_VTS_WTD_MOD_RUNTIME PASS。失败尝试的证据保留在 Logs 下相邻的验证归档目录。

### UMod 启用状态问题

第一次实际产物加载发现，源预制件中 enabled=false 的 HoVtsLookAt 在 UMod 实例中变为 enabled=true。因此角色当前以显式序列化的 AutoRun=false 作为保留官方姿态链的运行开关，源组件勾选框也保持关闭。重新构建后实际产物确认 Behaviour.enabled=True、AutoRun=False，姿态不会自动运行。作者日后启用自有姿态时，需要明确设置 AutoRun=true，并先解除官方节点对同一骨骼的写入。

## 尚未证明的项目

后续 Build Mod 和 Warudo 客户端测试按用户要求由用户执行。输入插件也已通过官方 UMod 构建，87 个类型中确认包含新角色输入、标定、姿态输出和交付器，并确认相应节点注册。标定倍率已修正为围绕参数零点缩放、最后限制输出范围，14 项数值回归通过。

本轮曾将验证产物复制到本机 Warudo 安装目录，旧 WTD.warudo 与 HoFaceTracking.warudo 保存在 D:/Unity_Project/BreakWarudo/Logs/HoVtsClientDeployment/20261029-163749。新插件指纹为 E202BFBDF0B64900373402E776C8FB980B06A1AC9DD9C26E7EC6B7E2A3A970AE。首次直接启动客户端因 Steam API 初始化失败停在启动阶段，随后已停止该测试进程，未完成客户端验收，也未修改蓝图。用户提出自行测试后，不再继续客户端启动和验收操作。

WTD 的实际 Character Mod 构建与 SDK 内产物加载已验证。Warudo 客户端加载、现有蓝图切换到 HoVTS角色输入，以及设备驱动下表情与官方姿态共存仍需继续验收。Unity 中实际 UMod 加载不等于 Warudo 客户端验收。
