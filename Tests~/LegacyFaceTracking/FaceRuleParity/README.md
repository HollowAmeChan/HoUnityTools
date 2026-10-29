# 面捕规则跨端一致性回归

`FaceRuleParity.cs` 在同一个真实 Unity 引擎中运行包侧 `HoFaceAnimationSession` 的规则方法与
Mod 侧 `HoFaceChain`，两边分别解析同一份 JSON。没有替代曲线实现，也没有在测试里重写求值器。

```powershell
& .\Tests~\SyncFaceModCore.ps1           # 同步 10 份公共源码
& .\Tests~\SyncFaceModCore.ps1 -Check    # 只检查；源码漂移立即失败
& .\Tests~\FaceRuleParity\Run.ps1 `
  -Project .research\UnityFaceValidation2 `
  -Profile 'D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\ho-iPhoneVTS.hoface.json'
```

`Project` 必须是已经安装当前 HoUnityTools 包的独立验证工程，根目录必须有 `.ho-face-validation`
标记。脚本只向该工程复制测试及实际 Mod 求值源码，不启动用户正在工作的工程。
可用 `-ModCore`、`-Unity` 指定另一套 Mod 源码及 Unity 编辑器。

覆盖内容：

- 给定 JSON 的 1,200 帧中性、脉冲、逐通道、随机、缺键、断包及平滑衰减序列，含变化的时间步。
- 每帧逐条比较输入行、输出行和最终参数字典，数值容差 `1e-6`，禁止非有限结果。
- 同名输出就地覆盖、引用最近一条上方输出、曲线后取值、无前帧残留。
- 前向／缺失／首行自引用，以及输入行误用 `out()` 的诊断与默认值。
- 常量绕过曲线、首帧平滑、延迟、维持与迟滞、每行状态独立、极小值归零。
- 旧 `ARKit/` 出口名称转换仅发生在 Mod 的发布适配层；`out()` 始终使用 JSON 中的原名。

比较边界是 **JSON 规则层**：输入原始字典和时间相同，Unity 的通道桥接使用规则计算出的值。
Unity 专有的 Manual/Hold/Neutral/Release、预览钉值、额外通道曲线与断流回中性不是 JSON 字段，
不能把启用了这些调试选项的最终画面与纯 Warudo 参数处理节点当成相同输入。
两端真实引擎版本之间的浮点差异及 Warudo 播放器中的整条蓝图仍需部署后验证。

同步后还应运行 Mod 仓库 `tools/compile-check.ps1 -ModsRoots Mods-Ho`（真实 Warudo 程序集与
UMod 沙箱 lint），以及 `Tests~/FaceTrackingValidation.cs` 的完整播放回归。

2026-09-27 验证：用户指定 JSON 为 71 条输入、136 条输出、134 个最终参数；跨端测试
2,010 帧、417,343 次比较通过，Unity 播放回归输出 `HO_FACE_TESTS_ALL_PASSED`。
