# HoWarudoRuntimeHub 设计

## 目标

`HoWarudoRuntimeHub` 是 Warudo Mod 的统一运行时入口。它不只负责调试窗口，也负责承载运行时组件的参数控制、事件交互以及后续的蓝图数据桥接。

组件不应该各自创建窗口。组件只提供自己的运行时参数界面，由 Hub 统一显示、布局和管理。

## 非目标

- 不依赖 `UnityEditor`。
- 不依赖 Warudo 私有程序集。
- 不在每个组件中创建独立的 `GUI.Window`。
- 不通过反射强行调用 Warudo 内部 UI 或输入实现。

## 总体结构

```text
HoWarudoRuntimeHub
|- 唯一浮动入口
|- 中控窗口
|  |- 工具栏、搜索、折叠和滚动
|  |- 运行时模块列表
|  `- 当前模块的参数界面
|- Runtime Module Registry
|- Runtime Binding Registry
`- Input Capture State
```

### Hub

Hub 是场景中的单例运行时组件，负责：

- 创建和维护唯一的 IMGUI 窗口。
- 注册、注销和发现运行时模块。
- 管理模块顺序、显示名称和实例归属。
- 维护窗口状态、当前选项卡、搜索内容和折叠状态。
- 为蓝图或其他运行时脚本提供统一参数入口。
- 在模块绘制失败时隔离异常，避免一个模块破坏整个窗口。

### Runtime Module

每个需要出现在 Hub 中的组件实现一个统一模块接口。模块不创建自己的窗口，只绘制自己的内容：

```csharp
public interface IHoWarudoRuntimeModule
{
    string Id { get; }
    string DisplayName { get; }
    int Order { get; }

    void DrawRuntimeGUI(HoWarudoRuntimeGUIContext context);
}
```

模块应当在 `OnEnable` 时注册，在 `OnDisable` 或 `OnDestroy` 时注销。Hub 管理的是组件实例，而不是脚本类型，因此同一个脚本挂在多个角色上时可以分别控制。

原型中的 `HoWarudoRuntimeHub.IRuntimePanel` 只是验证用接口，正式实现时应移到独立的运行时接口文件中。

## UI 结构

```text
HoHub launcher
`- HoWarudoRuntimeHub window
   |- Header: title, active target, close button
   |- Toolbar: search, refresh, collapse all
   |- Module list
   |  |- Bone Debug
   |  |- Aux Rig / Constraints
   |  `- Other runtime modules
   `- Selected module content
```

模块只使用 Hub 提供的布局上下文和控件样式，例如开关、滑条、枚举选择、颜色和折叠栏。这样所有组件拥有一致的外观，后续更换皮肤时也不需要修改每个组件的布局代码。

## 组件接入

### Bone Debug

`HoRuntimeBoneDebugRenderer` 接入后负责提供：

- 显示骨链和轴向。
- 线宽、轴长和颜色。
- 相机、Overlay 和绘制时机状态。
- 骨骼集合过滤。
- 当前收集到的骨骼数量和可见数量。

集合过滤应复用 `HoBoneGroupSet` 现有的判断逻辑，不在运行时 UI 中重新实现一套过滤规则。运行时只修改隐藏集合、显示集合或过滤文本，然后请求 Renderer 重建可见节点。

### HoRig / Constraints

约束组件接入后可以按组件实例显示：

- 启用状态和权重。
- 当前约束目标。
- 轴向开关。
- 拉伸、偏移和约束结果状态。

每个约束组件仍然保留自己的参数和计算逻辑，Hub 只负责集中显示和修改。

## 蓝图桥接

蓝图桥接不应直接依赖 UI 控件。Hub 后续提供独立的参数注册表：

```text
RegisterBool(id, getter, setter)
RegisterFloat(id, getter, setter)
RegisterEnum(id, getter, setter)
RegisterAction(id, callback)
```

UI 和蓝图都通过同一组稳定的参数 ID 访问组件状态。这样关闭 UI 后，蓝图仍然可以控制组件；组件也不需要知道参数来自 UI 还是蓝图。

## 输入处理

IMGUI 的 `Event.current.Use()` 可以阻止部分 IMGUI/EventSystem 层的事件，但不能保证阻止 Warudo 直接读取底层 `InputSystem` 的相机控制。

当前输入处理分为两层：

1. Hub 维护 `InputCaptureState`，窗口内按下、拖动、释放和滚轮时进行捕获。
2. 如果将 Input System 方案放入单独的 Warudo 专用程序集，可以尝试监听 `InputSystem.onEvent`，只将 Hub 区域内的鼠标位移和滚轮事件标记为 `handled`。
3. 如果 Warudo 提供公开的输入屏蔽或鼠标事件消费接口，再将捕获状态接入 Warudo 相机输入链。

`InputSystem.onEvent` 不是 Warudo 专用接口，只能阻止 Input System 继续更新鼠标状态，不能保证拦截已经在其他层读取的输入。它不能直接加入通用的 `HoUnityTools.Runtime` 程序集，否则会给不含 Input System 引用的工程造成编译错误。不采用修改全局鼠标状态、反射调用私有字段或禁用整个输入系统的方式。

## 实施顺序

1. 保留当前 Hub 原型，确认 Mod 构建和运行时显示正常。
2. 完成窗口输入捕获的最后一次可行性验证。
3. 把 `IRuntimePanel` 提取为独立接口和 GUI 上下文。
4. 接入 `HoRuntimeBoneDebugRenderer`，先实现显示开关和集合过滤。
5. 接入 `HoAuxRig` 及其他约束组件。
6. 统一 IMGUI 样式、模块搜索、折叠和实例筛选。
7. 增加 Runtime Binding Registry，接入蓝图参数和事件。

## 当前状态

- `HoWarudoRuntimeHub` 原型已提交并能在 Warudo 运行时显示交互窗口。
- 原型中的测试按钮用于验证上传后的输入链路。
- 已评估 Input System 鼠标位移/滚轮事件拦截方案；当前未放入通用运行时程序集，避免引入强制依赖。
- Bone Debug 和 HoRig 尚未正式注册到 Hub。
- 当前窗口样式仍是 Unity 默认 IMGUI 样式，后续统一处理。
