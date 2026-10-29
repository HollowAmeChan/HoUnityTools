# 旧 HoFT Bundle 导出记录

此路线已移除。以下为删除前的历史说明，不是当前操作步骤。当前方案见 [VTS 角色组件使用](../guides/VTS_CHARACTER_COMPONENTS.md)。

## HoFT 页：控制器 → AssetBundle（面捕，旧路线）

> 已被新规划取代。新系统把控制器和中间层放进角色预制件的 HoVtsController / HoVtsParameterReceiver 组件，不再依赖 HoFT AssetBundle。此节保留作旧角色迁移和历史复现参考；新角色不要继续扩展这条路线。新架构见 [VTS 角色组件架构](../face-tracking/VTS_COMPONENT_ARCHITECTURE.md)。

窗口第三个页签，跟角色/其他 Mod 的构建流程**没有关系**（不碰 ExportSettings、不调 UMod 构建）：
它把一个 `AnimatorController` 和**它驱动的那套预制体**打成一个 `AssetBundle` 文件，给 Warudo 的
「HoFace控制求解」节点用。为什么必须是 bundle：运行时读不了 `.controller`（编辑器格式），
而运行时**枚举不了一个 `AnimationClip` 的绑定**，所以包里必须带原配的那套层级（详见
[面捕路线](../archive/FACE_TRACKING_WARUDO_ROUTE.md) §2.0.2）。

| 输入 | 说明 |
| --- | --- |
| 控制器 | 纯 `AnimatorController`（不接受 `AnimatorOverrideController`） |
| 绑定预制体 | **这个控制器真正驱动的那套预制体**。作者最清楚是哪个，硬猜不可靠 |
| 输出目录 | 可以是**工程外**的绝对路径 —— Warudo 的插件沙箱就在工程外（就在那儿选） |
| 文件名 | `*.bundle`；Warudo 节点那个下拉列的就是沙箱里的这个名字 |

**"绑定预制体"那一栏为什么必要**：控制器和 rig 常常不是同一个资产，靠"扫绑定反推"只覆盖
"两者恰好放在一起"的顺利情况；反推失败时打出来的是个**空 bundle**，而运行时只会静默采不到东西。
指定之后还会拿它做一次**绑定校验**：把每条曲线绑定的 `path` 在这份预制体里 `Transform.Find` 一遍，
解析不到的点名列出来。这一步值钱，因为运行时 `GetBlendShapeWeight` 是**按名字**取权重的 ——
控制器写了 `blendShape.foo` 而网格上没有 `foo`，Unity **不报错**，那一格永远是 0。
⚠️ 校验**只验路径，验不了形态键名字**（名字对不对只能等 Warudo 侧自检行里的 `shapes[…]` 报）。
留空则退回"按绑定自动找"（`GuessRig`），并在结果里明确警告这是猜的。

实现：`Editor/FastBuildWarudoMod/HoFTBundleBuilder.cs`（打包 + 校验）与
`HoFastBuildWarudoModWindow.HoFT.cs`（页面）。打包用 `ChunkBasedCompression`（LZ4）——
Warudo 侧走 `AssetBundle.LoadFromMemory`，那份读得了；**别用默认 LZMA**。
⚠️ AssetBundle 与 Unity 版本绑定：必须在 **2021.3.45f2**（= Warudo 本体版本）里打。
⚠️ 打完必须把文件放到插件沙箱，再在节点上按「重读控制器」（同名文件被替换时 `Prepare` 认不出来）。
