# Ho Unity Tools

Hollow 的 Unity 小工具集，以 UPM 包形式发布。

## 安装

需要 Unity 2021.3.18 或更新版本。

在 Unity Package Manager 中通过本地路径安装：

```text
D:/Unity_Fork/HoUnityTools
```

或添加到 `Packages/manifest.json`：

```json
"com.hollow.hounitytools": "file:D:/Unity_Fork/HoUnityTools"
```

## 工具

### Ho 跟随约束（Ho Follow Constraint）

给任意 GameObject 添加 `HoFollowConstraint`，或使用菜单：

```text
GameObject > HoUnityTools > Constraints > Ho Follow Constraint
```

这是一个纯粹的点对点 Transform 约束，不依赖 Rigidbody、Joint、PhysBone、骨骼链传导或物理求解器即可跟随目标。支持跟随响应、超调、位置与旋转轴锁定、旋转过滤、偏移、可选的柔性限制以及若干 Gizmo。未指定目标时，组件不会写入 Transform。

应用预设前，可用 `保存初始变换` 缓存原始的本地变换。恢复需要显式点击 `恢复初始变换`；移除组件不会自动还原 Transform。

### Ho 漂浮约束（Ho Floating Constraint）

给任意 GameObject 添加 `HoFloatingConstraint`，或使用菜单：

```text
GameObject > HoUnityTools > Constraints > Ho Floating Constraint
```

用于无目标的偏移、呼吸和噪声运动。它自带初始 Transform 缓存，可与 `HoFollowConstraint` 叠加使用。

Inspector 提供以下起始预设：

- `清空`
- `光环`
- `武器`
- `背包`
- `无人机`

### Ho 骨骼渲染器（Ho Bone Renderer）

给骨架根节点所在的 GameObject 添加 `HoBoneRenderer`，或使用菜单：

```text
Component > HoUnityTools > Ho Bone Renderer
```

把骨架根拖入组件即可在 Scene 视图显示全部骨骼，可在场景中点击骨骼直接选中对应的 GameObject。支持链接从 Blender 骨骼集合导出的 JSON 分组文件，按集合逐组开关显示；未归入任何集合的骨骼（如自动生成的 end 骨）会归入 Other 兜底组。

### Ho 同步摄像机（Ho Scene To Game View Sync）

给相机添加 `HoSceneToGameViewSync`，或使用菜单：

```text
GameObject > HoUnityTools > Ho Scene To Game View Sync Camera
```

该组件挂在 Camera 所在的 GameObject 上，只控制这个 Camera。默认不做后台同步，Inspector 中点击 `吸附当前 Scene 视图`，即可把最近活动的 Scene 视图相机的位置、旋转、FOV 与裁切平面应用过来。

如果确实需要实时跟随，可在 Inspector 中开启 `持续跟随 Scene 视图`。组件未勾选、GameObject 未激活或播放模式下未允许同步时，自动同步会暂停。

### 动画处理

菜单 `HoUnityTools > 动画处理` 打开工具窗口，可将动画剪辑（.anim）中除 Float 曲线外的其他曲线全部删除，用于精简动画文件。

### 骨架约束导入

菜单 `HoUnityTools > 骨架约束 > 导入标准约束` 打开工具窗口，可从 JSON 配置批量为骨架导入 Unity 标准约束（Rotation / Position / Scale / Parent），并支持一键锁定或清除全部约束。
