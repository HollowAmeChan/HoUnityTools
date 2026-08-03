# Ho Unity Tools

本 Unity 包联动 [HollowAmeChan/HoTools](https://github.com/HollowAmeChan/HoTools) Blender 插件。


## 面板

| 功能 | 入口 |
| --- | --- |
| 动画处理 | `HoUnityTools/动画处理` |
| FBX 导入处理中控 | `HoUnityTools/HoFBX导入处理` |

选中 FBX 资产后，也可以使用 `Assets/HoUnityTools/HoFBX导入处理` 打开同一个 FBX 面板并自动扫描相邻配置文件。这是面板的上下文快捷入口，不是另一套功能。

## 组件

在 Inspector 中使用 `Add Component`，组件路径如下：

| 组件 | Add Component 路径 |
| --- | --- |
| 跟随约束 | `HoUnityTools/Constraints/Ho Follow Constraint` |
| 漂浮约束 | `HoUnityTools/Constraints/Ho Floating Constraint` |
| Scene/Game View 相机同步 | `HoUnityTools/Ho Scene To Game View Sync` |
| 骨骼绘制器 | `HoUnityTools/Ho Bone Renderer` |

FBX 导入处理中控可以根据配置自动添加骨骼绘制器和 Unity 标准约束。`HoImportedConstraintMarker` 是导入器内部标记组件，不应手动添加。
