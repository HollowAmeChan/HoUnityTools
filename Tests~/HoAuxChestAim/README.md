# HoAux 胸部刚性附件：物理后求值回归

本测试对照 Blender 的 Armature 多骨 LBS 参考点、Aim 方向骨及最终胸饰控制骨，
并检查 Unity 实际开始渲染时的最终姿态。Blender 探针及运行时对照不保存源工程或场景。
正式配置动作仅持久化角色的 HoAux 求值阶段，并保留配置前的备份。
原始数据与报告只写入 Git 忽略的 `.research/`。

## 复现原因

WTD 的胸骨由 Magica BoneCloth 驱动。Magica 每帧 EarlyUpdate 先恢复动画骨姿态，
在普通 LateUpdate 之后的 PreLateUpdate 执行模拟并写回骨骼。
HoAux 原来只在普通 LateUpdate 求值，因此 Armature / Aim / Copy 栈持续读到物理前的输入。
单纯把 MonoBehaviour 的执行顺序从 32000 继续调大，仍到不了 Magica 的 PlayerLoop 阶段。

正式方案将通用依赖图的求值阶段显式保存在组件中：`WithLayers` 保持原行为，
`FinalPose` 在 PostLateUpdate 开始、蒙皮之前执行。每组每帧只在所选阶段执行一次，
从其控制通道的绑定输入开始，不把前一次加权结果再次累加。
分层 Parent / Twist / Fan 保持原时机；`Manual` 模式由宿主显式调用。
最初用于确认根因的“物理后补跑”不作为正式运行策略。

### MCH 与分层时序约定

MCH 作为原始控制变换的缓存/中间结果；角色设计上不通过其子级层级传播最终变形，
依赖 MCH 的约束按依赖关系在后面读取结果。此次修复不移动 MCH 父级层，
不后置整个 `EvaluateNow()`，也不重新执行 Parent / Twist / Fan。
最终姿态阶段仅恢复通用栈自己拥有的通道并求值该组，不重复运行已经执行的分层规则。
新增或合并约束时应记录读者/写者及阶段；不能只调大 MonoBehaviour 的执行顺序来替代物理阶段。

## Blender 只读探针

```powershell
& 'D:\Blender\blender-5.2.0-windows-x64\blender.exe' `
  --background --factory-startup --disable-autoexec `
  'E:\BaiduSyncdisk\在做工程\渲染研究\泳装WTD\WTD-summer.blend' `
  --python-exit-code 1 --python "$PSScriptRoot\inspect_blender.py" -- `
  'D:\Unity_Fork\HoUnityTools\.research\HoAuxChestAim-20261101\blender-probe.json'
```

探针在内存中冻结动画并扰动左右胸骨，导出最终约束骨矩阵、原始约束和胸饰蒙皮分组。
不保存源工程，也不执行工程内自动脚本。

## Unity 实际物理与渲染阶段

将 `HoAuxChestAudit.cs` 临时复制到项目 Editor 目录并等待编译。
当前场景应是已保存的 WTD MMD 影片；测试直接在该场景进入 Play，不重建资产。
往 `Library/HoAuxChest/request.json` 写入：

```json
{
  "id": "unique-request-id",
  "output": "D:/Unity_Fork/HoUnityTools/.research/HoAuxChestAim-20261101/report.json",
  "frames": 120,
  "disableFinalPass": false,
  "correctAfterPhysics": false,
  "manualRig": false
}
```

- `disableFinalPass=true` 选择旧 `WithLayers` 阶段；`false` 选择正式 `FinalPose` 阶段。
  该测试字段保留初次根因调查时的名称，不对应正式组件的补跑开关。
- `manualRig=true` 只在物理前显式调用 `EvaluateNow`，验证不会自动抢占手动模式。
- `correctAfterPhysics=false` 在 Magica 回调内临时求一次参考结果后还原，
  由正式 PlayerLoop 决定实际渲染姿态；不把测试自身的修正当作正式修复。
- 报告逐帧记录物理前、物理后、参考重算以及 `beginCameraRendering` 的姿态残差，
  同时检查重算没有改写物理输入胸骨、实际饰品蒙皮引用、求值帧号及每帧执行次数。
- `protected*Error` 检查不属于通用栈控制子树的全部骨骼，包括所有 MCH：
  局部位置/旋转/缩放及世界矩阵在通用栈刷新前后应保持不变。
  `mchChildren` 只记录实际层级情况，不因旧模型存在子级就擅自重排骨架。
- 结束会释放播放倍率、离屏相机目标和 Magica 回调，返回 Edit。
  看 `Library/HoAuxChest/result.json`，不要在未完成时重复提交。
- 完成后移除本次临时复制的 Editor 脚本及其 `.meta`，保留这里的测试源。
- 如需持久化配置，可另发只包含 `id`、`configurePrefab`（Assets 下的 Prefab 路径）、
  `sourceArmature` 的请求。该动作按骨架来源精确选中一个 HoAux，设置 `FinalPose`，
  不重新导入约束；备份写入 `Library/HoAuxChest/prefab-before-phase-config.prefab`。

## 2026-11-01 实测

BREAK_URP，花吐电気 EX-HAPPY，WTD 泳装，实际 Magica 模拟与 MMD 播放：

- 初次根因定位：101 个有效采样，物理前的胸饰残差最大约 3.598° / 6.013 mm；
  使用同帧物理结果重算后残差为 0。
- 正式单次阶段回归：`WithLayers` / `FinalPose` / `Manual` 各 61 个有效渲染采样，
  三种情况每帧的通用栈执行次数都为 1。
- `FinalPose` 的渲染前位置、角度残差均为 0，求值帧号与渲染帧全部一致；
  `WithLayers` 保留旧结果，残差仍约 3.598° / 6.013 mm。
- Manual 由探针在物理前显式调用；阶段回调没有补算或抢占。
- 作为物理输入的左右胸骨，在通用栈求值前后的旋转改变量均为 0。
- 实际启用的“上衣蝴蝶结 (1)”有 2186 个 Unity 渲染顶点受输出骨驱动。
- 全部 53 根 MCH 无子级；通用控制子树以外的 1293 个节点，局部位置/旋转/缩放和世界矩阵改变量均为 0。

这是本角色该约束链的同帧时序回归，不代表所有 Blender 约束类型或其它物理系统均已验证。
