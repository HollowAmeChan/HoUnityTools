# WarudoModUtils

此目录存放 Warudo **运行时可用的工具组件**。代码保持运行时独立，不依赖 `UnityEditor`
或 Warudo 私有程序集，方便由 Warudo 的资源面板 / 蓝图传参。

| 组件 | 菜单路径 | 说明 |
|---|---|---|
| `BoneDebug/HoRuntimeBoneDebugRenderer.cs` | `HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Bone Debug Renderer` | 运行时绘制骨架骨链与轴向，可按骨骼集合过滤 |
| `HoWarudoBlendShapeBillboard.cs` | `HoUnityTools/Warudo Mod Utils/HoWarudo Blend Shape Billboard` | 在场景里显示混合形状监视屏 |

`RuntimeDrawing/Resources/HoRuntimeDebugLine.shader` 是骨骼调试渲染器用的画线着色器。

---

## 已删除：`HoWarudoRuntimeHub`

原先这里还有一个 `HoWarudoRuntimeHub`，定位是"所有运行时组件的统一入口 + 运行时 IMGUI 中控窗口"，
配套 `IHoWarudoRuntimeModule`（模块接口）、`HoWarudoRuntimeGUIContext`（GUI 控件上下文）
和 `HoWarudoRuntimeHubDesign.md`（设计文档）。

**整块删掉了**，原因是：**在 Warudo 已经能构建任意类型 Mod 之后，这层没有存在意义。**
FastBuild 可以把运行时组件直接打进 Mod，参数由 Warudo 自己的资源面板 / 蓝图来调，
不需要再自建一套中控窗口和模块注册表。

`HoRuntimeBoneDebugRenderer` 原来是作为模块接入 Hub 的，现已**解耦成普通 `MonoBehaviour`**：
去掉了 `Id` / `DisplayName` / `Order`、`Register` / `Unregister` 调用和 `DrawRuntimeGUI()`。
它自身的能力（骨骼采集、网格重建、集合过滤、CustomEditor、调试线 shader）全部保留。

---

## `HoWarudoBlendShapeBillboard`

指定一个 `SkinnedMeshRenderer` 后，它会在世界中显示文字监视屏，并读取该网格当前的 Blend Shape
实际权重。屏幕直接使用组件 Transform 的位置和朝向，可将组件挂到角色旁边的空物体上，
也可选择启用面向相机。名称和值分别对齐成列，每列默认最多显示 20 行，更多条目向右扩展；
每列最大行数可在组件中调整。
组件不使用置顶绘制。
