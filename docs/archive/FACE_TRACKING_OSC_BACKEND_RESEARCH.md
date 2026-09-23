# 面捕的 OSC / VRCFT 后端：调查保留（不是现状）

> **已归档（2026-09-23）。** 这份是**未来后端**的调查记录：首期走的是 iFacialMocap 手机直连，
> VRCFT 与 VRC 后端**完全没有实施**（见 [调试组件设计](../FACE_TRACKING_DEBUGGER_DESIGN.md) §10 的"后续 B / 后续 C"）。
> 留在这里是因为，"将来真要接 VRCFT 时会撞上什么"这件事已经查过一遍了，别重查。

## 1. Av3Emulator 值得借鉴的部分

Av3Emulator 的 `LyumaAv3Osc` 提供网络接入，`LyumaAv3OSCSettings` 用 EditorWindow 展示设置并保存到 EditorPrefs；
`LyumaAv3Runtime` 用 AnimatorControllerPlayable 和 AnimationLayerMixerPlayable 执行角色动画，并处理 VRC 特殊行为。

| 已核对的能力 | 我们的取舍 |
| --- | --- |
| openSocket、disableOSC | 面板统一提供开始/停止接收，另设"暂停驱动"；两者语义分开 |
| 本地 UDP 9000、出站 IP 127.0.0.1、出站 UDP 9001 | 借鉴收发方向的显示；这些端口留给未来 VRCFT，手机首期使用 49983 |
| 目标 Avatar、转发给场景全部 Avatar | 选择角色组件；多角色显式订阅，首期默认一个活动角色 |
| 本地绑定信息、消息计数、已知路径、观察到的最小/最大值 | 保留，并补充每条映射的状态与时间戳 |
| resendAllParameters、Loopback 回复、逐消息日志 | 首期只借鉴可控日志；OSC 重发/回显不属于手机协议 |
| Scene OSC Gizmo、发送者 IP 显示 | 参数表优先；空间 Gizmo 不作为首期重点 |
| Animator To Debug、运行中检查参数与树 | 提供定位 Controller / State / Tree；独立 Playable 的参数必须从实际句柄读取 |
| VRC 参数驱动、Tracking Control、本地/远端等模拟 | 保留为可选兼容后端；不让这些依赖进入核心 |

源码：[OSC 组件](https://github.com/lyuma/Av3Emulator/blob/e015d6c94921cabda19cfdb119c15243fb3de93f/Runtime/Scripts/LyumaAv3Osc.cs)、
[全局面板](https://github.com/lyuma/Av3Emulator/blob/e015d6c94921cabda19cfdb119c15243fb3de93f/Editor/LyumaAv3OSCSettings.cs)、
[运行时](https://github.com/lyuma/Av3Emulator/blob/e015d6c94921cabda19cfdb119c15243fb3de93f/Runtime/Scripts/LyumaAv3Runtime.cs)。
这是源码能力调查，**不是本次已测试结论**。

## 2. VRCFT 链路（未来）

```text
捕捉模块 → VRCFT → OSC UDP → Unity 127.0.0.1:9000 → 参数表
                                ↑
                   首先只看包，再启用角色输出
```

- `FT` 是 Jerry 模板前缀，**不是** VRCFT 固定协议前缀；路由需保留完整地址。
- VRCFT 还包含有符号视线、Bool 状态和 EyeLid 复合范围，**不能**复用首期 ARKit 的全零中性规则。
- **仅打开 Socket 不保证 VRCFT 发出角色所需参数**：要区分三个状态 —— 模块没有数据、OSC 没到达、
  VRCFT 没把这些参数判为相关。
- 可用诊断路径：VRCFT 里启用 Force Relevancy，用 Float 参数做无角色配置联调；按实际收到的前缀选 Profile。
  它不提供二进制参数布局，也不能当作"完整原版 VRC 模板已兼容"的证明。
  优先让用户在 VRCFT 中切换，不在后台不透明地改对方全局设置。
- 历史 5.2.3.0 标签里 `OscService` 用默认入站 9001、出站 9000，并接受 `/vrcft/settings/forceRelevant` Bool；
  当前 master 保留该命令但重构了网络服务与发现过程。文档特别提示 **5.2.3.0 不应随意修改 OSC IP 或接收端口**，
  所以不能把"改 VRCFT IP/端口"写成通用教程。
- 高级模式才做参数相关性/发现（发布目标参数集合、类型、默认值、角色标识）。
  **OSCQuery 需要 HTTP 数据树、HOST_INFO 和 mDNS 等配套，不是多开一个 UDP 端口**；
  master 的 `MulticastDnsService.ResolveVrChatClient` 会过滤 `VRChat-Client` / `ChilloutVR-GameClient` 服务名前缀，
  普通名为 HoUnityTools 的服务未必会被发现。

因此"自动发现"必须单独验证：目标版本走哪条发现路径、输出目的地是否随查询更新。
需要特定兼容服务名时应在界面标明，并验证与真 VRChat 同开时的选择行为。
**不改写真实 VRChat 用户目录，也不伪造用户已有的 Avatar 配置。**

依据：[VRCFT 使用文档](https://docs.vrcft.io/docs/vrcft-software/vrcft)、
[历史 OscService](https://github.com/benaclejames/VRCFaceTracking/blob/ad06f2967a2243d85ad161a397eb911522182c15/VRCFaceTracking.Core/OSC/OscService.cs)、
[master OscQueryService](https://github.com/benaclejames/VRCFaceTracking/blob/6432e6a8d85fa7ec5115fc725c6abcb6dbdd4f35/VRCFaceTracking.Core/Services/OscQueryService.cs)、
[发现过滤](https://github.com/benaclejames/VRCFaceTracking/blob/6432e6a8d85fa7ec5115fc725c6abcb6dbdd4f35/VRCFaceTracking.Core/mDNS/MulticastDnsService.cs)。
