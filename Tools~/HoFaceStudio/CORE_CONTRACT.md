# 当前核心契约

2026-11-03，首个核心里程碑。此文件记录已实现范围，不代表桌面、传输或角色 IR 接入已经完成。

## 代码复用

`HoFace.Core.csproj` 链接 `Runtime/FaceTracking/` 的实际规则与协议源码，没有复制第二份表达式或修饰符实现。

`HO_FACE_PORTABLE` 只为独立编译选择无 Unity 依赖的数学、曲线与显示属性适配。便携程序集目标为 `netstandard2.0`，没有 UnityEngine 引用。物理网格引用属于宿主序列化，不进入核心。

曲线只支持现有 JSON 保存的非加权 Hermite 切线，不增加 Unity weighted tangents 或 wrap modes。端点钳制和空曲线直通维持原语义。

`HoFaceValueSession` 用显式单调时间、会话内帧序号与随机源。相同时间不重复推进，倒退时间拒绝；Reset 恢复可重放状态。旧调用入口保持兼容。

## value

新文件使用 `format: "ho-face-value"`、`version: 1`，行与曲线沿用旧规则数据。读取继续兼容旧 `ho-face-middleware`；旧写入入口仍写旧格式，新入口 `HoFaceValueDocument.Write` 写新格式。

新格式拒绝未知版本。旧格式保留原读取兼容行为，不宣称已经替所有旧版本完成严格迁移。

## IR

新文件使用 `format: "ho-face-ir"`、`version: 2`。完整可运行的小样见 `samples/`。

- `parameters`：声明参数名、默认值及编辑范围；编辑范围不隐式钳制输入。
- `targets`：逻辑形态键名称 `id` 与最终输出范围，不包含 mesh、对象名、路径或 slot。
- `nodes`：`pose`、`blend1d`、`blend2d`、`direct`，由 `root` 指定入口。
- Pose 权重按 target id 绑定；同一姿势内重复目标拒绝，省略的拥有目标贡献为零。
- 1D 阈值不要求文件内排序，端点外取边界姿势，重复阈值拒绝。
- 2D 使用 Cartesian gradient-band 影响值并归一化，拒绝重合坐标；没有以 Delaunay 重心插值替代。
- Direct 默认为不归一化相加；可显式 `normalize`。权重参数与常量权重相乘，负贡献权重视为零。
- 姿势贡献先组合，再按目标范围最终钳制；`UnclampedValues` 用于检查贡献。极端数值在浮点域中饱和保护。
- 图必须无环，嵌套上限 64 层；引用、稳定 id、参数与目标范围均有校验。

求值器使用构造时的图数据；修改作品后需要建立并验证新求值器。UI 不能直接修改正在运行的快照。

Blender HoFace 的对象列表和 Unity 组件的 `SkinnedMeshRenderer` 列表各自决定写入范围。同一输出写到清单内所有网格的同名键，缺键跳过；适配器不扫描场景。`hoface.preview` v2 只传逻辑输出权重，不传网格绑定。旧 IR/预览 v1 需要显式迁移，不能按新语义静默读取。

## 验证

```powershell
dotnet run --project tests/HoFace.Core.Checks/HoFace.Core.Checks.csproj -- D:/Unity_Fork/HoUnityTools
```

真实 Unity 对照使用带有 `.ho-vts-mirror-validation` 标记的隔离工程：

```powershell
powershell -File D:/Unity_Fork/HoUnityTools/Tests~/VtsComponents/RunValueParity.ps1 `
  -Project D:/Unity_Fork/HoUnityTools/.research/HoVtsMirror-2021
```

对照探针在 Unity 中加载独立编译的便携程序集，用同一输入与时钟比较生产 JSON 的完整参数输出，并比较原生曲线与便携曲线。隔离工程和二进制不进入版本控制。
