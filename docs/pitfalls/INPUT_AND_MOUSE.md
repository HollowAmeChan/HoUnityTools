# 鼠标与指针输入（注视约束）

正本：[注视约束](../完善的功能/LOOKAT_CONSTRAINT.md)。

## 1. 失焦时鼠标位置是 `(0,0)`，别当真

Input System 在**窗口失焦 / 无数据**时会把鼠标位置报成 **(0,0)**。直接采信就是"指针有效但停在画面角落"：
视线跳到角落，而且**丢失判定永远不会触发**（它认为指针一直有数据）。
所以 `(0,0)` 一律当"没有数据"；鼠标不在相机 `pixelRect` 里（跑到别的窗口上）同样按丢失处理。

## 2. 基准方向不能用相机的 `forward`

症状：平转角一路顶到 **±150° 以上**，头部被限位卡住、眼睛吃满。
原因：`forward` 是**射进屏幕里**的方向，而角色是面对相机的 —— 用它等于让角色"看向自己背后"。
怎么办：在观察者坐标系里构造方向 `(tan(yaw), tan(pitch), −1)`（−Z 朝向观察者、+X 观察者右、+Y 上），
再走正常分解流程。鼠标在屏幕中心 = 看向观察者。

## 3. 用"射线交点"当鼠标目标会出两种怪象

- 取**远点** → 角色看向镜头**背后**。
- 取**近点**（离眼睛最近）→ 在角色附近半径趋零，**鼠标一动方向就乱摆**。

这是 v6 踩过的坑，换成了 VRM 生态的**映射**：归一化鼠标偏移 × 相机半 FOV × 灵敏度，基准是"看镜头"。
`Raycast` 是物理目标点，不存在这个歧义。

## 4. 相机要手动指定

运行时**不自动猜相机**：`Camera.main` 依赖 `MainCamera` 标签（很多测试场景没有），
按"像素面积最大"猜也会挑错。空的时候只警告一次，角度映射退回角色相对坐标系。
面板有「填入场景里的相机」按钮，但那也是你显式点的一下。

## 5. 参考系退回组件自己的 transform → 读数与限位全部失准

组件自己的 transform 往往是骨骼 / 空物体，**轴向随机**：看起来像"平转 150° 以上"，
限位也跟着错。空着时默认取 **Animator 所在物体（角色根）**的朝向 —— 那才是角色的面向。

## 6. Scene 视图鼠标要"保活"

编辑器把「鼠标位置 + Scene 视图相机」写进 `HoMousePointer.EditorPointer`（运行时程序集不能引用 UnityEditor，
所以是单向喂数据），并用 `EditorApplication.update` 保活 ——
**Scene 视图不重绘时位置事件不来**，不保活的话时间戳会过期、鼠标控制会突然掉回 Game 视图。

## 7. 采样要带"这次用的相机"

采样结果里有 `HoPointerSample.camera`，准星 / 射线 / 角度摇杆三种取法都跟着同一个相机走 ——
否则会出现"方向按 A 相机算、鼠标位置按 B 相机读"的错位。

## 8. 别用引擎的 `ENABLE_INPUT_SYSTEM` 门控 `using UnityEngine.InputSystem`

症状（宿主工程里必现）：

```
Runtime/Constraints/HoMousePointer.cs(4,19): error CS0234:
The type or namespace name 'InputSystem' does not exist in the namespace 'UnityEngine'
(are you missing an assembly reference?)
```

原因不是"没装 Input System 包"，而是**两个宏说的不是一回事**：

- `ENABLE_INPUT_SYSTEM` 是**引擎全局**宏，跟着项目的 active input handling / 包安装走，
  它跟"`HoUnityTools.Runtime` 这个程序集能不能引用到 `Unity.InputSystem` 程序集"毫无关系；
- 于是"包在项目里 → 宏亮 → `using UnityEngine.InputSystem;` 被编进来 → 但 asmdef 那条引用没落地"
  就成了 CS0234。反过来也踩得到：`.research/UnityFaceValidation` 那次真实编译里
  `HoUnityTools.Runtime.rsp` 只定义了 `ENABLE_LEGACY_INPUT_MANAGER`、**没有** `ENABLE_INPUT_SYSTEM`，
  而同一个编译命令行里 `Unity.InputSystem.ref.dll` 是实打实引用进来的 —— 可见两者互不决定。

正解：**用 `versionDefines` 让"包真在"才定义自己的宏**，`using` 和用法都挂它。
`Editor/HoUnityTools.Editor.asmdef` 对 URP 用的就是这一套（`HO_URP_AVAILABLE`），Runtime 这边补上：

```json
"versionDefines": [
  { "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "HO_INPUT_SYSTEM" }
]
```

（`expression` 用 `1.0.0` 这种"单版本=不低于它"的写法，语法确定有效；别用空串。）

实测含义（Unity 2021.3 / 2022.3 / 6000.3 一致）：包没装 → 宏不定义，`#if` 整段不编，`using` 不会出现，**编译永远过**；
包在（≥1.0.0）→ 宏定义，走 `Pointer.current`。旧 Input 那半边继续用引擎宏 `ENABLE_LEGACY_INPUT_MANAGER` 门控
（新输入独占模式下读 `Input.mousePosition` 会直接抛异常，所以"没开旧 Input 就整段不编"本来就是对的）。

两条路都没有时（没包 + 没旧 Input Manager）本类退化成"永远采不到指针"，
只留一条 `#warning` 在编译时提示一次 —— 不再让宿主工程的编译直接挂掉。
