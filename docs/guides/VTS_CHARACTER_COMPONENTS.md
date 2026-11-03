# VTS 角色组件使用

角色预制件持有 `.hoface.value.json`、`.hoface.ir.json` 和运行组件。外部输入经标定后交给角色接收器；表情由 `HoVtsIrController` 求值，姿态可继续使用 Warudo 官方节点。

> 本页说明角色侧正式运行基线。作者编辑和实时制作统一使用 [HoFaceStudio](../../Tools~/HoFaceStudio/README.md)；Warudo Mod 不接作者调试桥接。

## 在 Unity 配置角色

1. 在场景或预制件编辑模式中选择角色根物体，执行 **HoUnityTools → 面捕 → 为选中角色创建 VTS 组件**。
2. 新建的 `HoVTS` 物体挂接收器；其子物体挂 `HoVtsIrController` 与 `HoVtsLookAt`。一键创建会把角色根物体下已有形态键网格预填到 IR 的“驱动网格”列表；列表仍可手动增删。[VTS 果冻眼](VTS_JELLY_EYE.md) 可作为同级子物体手动添加，瞳孔与各层高光分别配置一个组件。已有组件时工具会停止，直接编辑现有组件即可。创建支持 Undo。
3. 在接收器引用可复用的 value JSON，在 `HoVtsIrController` 引用 IR JSON。IR 只输出形态键名与权重；组件只写列表内所有网格上的同名键，缺键跳过，不扫描运行时场景。旧 `HoVtsController` 只作为迁移兼容。
4. 需要 Unity 作者预览时，在 Play 模式挂 `HoFaceStudioRawInput`，HoFaceStudio 发送 raw；Unity 侧 value、LookAt 和其他消费者继续运行。
5. 保存预制件，两个 JSON 和运行组件引用随正常 Character Mod 打包。

规则编辑在 HoFaceStudio 中完成；多个角色可以复用同一份 value/IR 文件。

若继续使用官方姿态链，HoVtsLookAt 保持“自动运行”关闭。UMod 实测会把组件的 enabled=false 恢复成 true，所以不能只依赖组件标题上的勾选框。以后改用自有头眼姿态时，再明确启用自动运行并解除官方链对同一骨骼的写入。自有组件只写头眼旋转，不把头位置当作全身根位移。

## Unity 调试

打开 HoFaceStudio，选择 value/IR 文件并连接 VTS。Unity 侧只在 Play 模式显示接收器状态和 HoFaceStudio raw 入口；旧 Unity 面捕调试窗口已退役。

组件通过 **Add Component → HoUnityTools → 面捕** 分类可见：VTS 参数接收器、VTS 表情 IR、VTS 头眼姿态、VTS 果冻眼。创建入口是 **HoUnityTools → 面捕 → 为选中角色创建 VTS 组件**，会在角色下建立 HoVTS 空物体并填写基础引用，网格列表由作者显式指定；果冻组件按需手动添加。

- value/IR 的阶段读数、覆盖、隔离、录制和回放由 HoFaceStudio 提供；录制通过 AI/自动化接口也可控制。
- Unity 接收器只显示连接、来源、追踪和超时；失去输入后角色组件回到规则中性值并停止写入。

## Warudo 连接

表情链：

~~~text
HoFaceVTS接收器.原始值 → HoFaceVTS参数标定.原始值
HoFaceVTS参数标定.标定后原始值 → HoVTS角色输入.标定后原始值
HoFaceVTS接收器.新鲜 → HoVTS角色输入.输入新鲜
~~~

HoVTS角色输入选择对应角色。节点自动求值，不需额外执行线，不再填写规则 JSON 或控制器 Bundle。

角色组件直接接收标定后的完整 VTS 字典，HoVtsIrController 从接收器读取 value 输出，HoVtsLookAt 读取姿态参数；正式蓝图不再需要 HoFaceVTS姿态输出节点。官方姿态链仍可作为外部兼容方案自行接入，但不属于角色输入主链。旧控制器表情写入路线不要与 IR 组件同时启用。

输入标定保存在 Warudo 蓝图中。标定按输入范围映射、乘倍率、最后限制输出范围；正负姿态的倍率以零点为中心。FaceFound、Hotkey、Timestamp 与未知键透传。

## 当前交付

HoFT 独立 Bundle 页面、旧 Unity 面捕调试窗口、旧 JSON 编辑器和影子求值已退役。HoFaceStudio、HoVtsIrController、HoFaceStudioRawInput 和 WTD 角色迁移的实际 Mod 构建及 SDK 产物加载记录见 [WTD 接入记录](../face-tracking/WTD_COMPONENT_MIGRATION.md)。后续 Build Mod 与 Warudo 客户端实测由用户执行。
