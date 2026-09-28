# 面捕分工现状核对：中间层并行供给控制器与动态参数Hub

调查日期：2026-09-26。读取时包仓库HEAD为`83ff921`，mod子仓库HEAD为`ac86e51`。
本轮只核对源码、实际调试配置、控制器资产和既有记录；没有修改运行时代码或重新构建角色/插件。

## 1. 当前路线已落在哪

**现在的产品路线是“中间层计算语义，控制器消费语义，Hub保存同一份语义值”。**
此前讨论的多控制器Hub、控制器产生复杂语义再回流，并不是当前实现方向。

```text
原始输入 → 中间层配置求值
                      ↓
             参数字典 + 手工/其他节点覆盖
                      ↓
                 最终参数字典
                 ├─→ 控制求解 → 动画姿态 → Warudo应用
                 └─→ 写动态参数 → 角色HoFaceSemanticHub → 消费脚本
```

如果需要覆盖，两条分支应从**同一份合并后的最终字典**分出；不要只覆盖控制器分支，否则Hub看到的是另一套值。

| 层 | 源码现状 | 证据 |
| --- | --- | --- |
| 中间层 | inputs/outputs表达式、曲线、平滑、分档、常量defaultValue；负责组合和生成语义值 | `Runtime/FaceTracking/HoFaceMiddleware.cs`；Unity会话；mod的`HoFaceChain` |
| 控制器 | 写入控制器已声明的参数后求值，消费gate/轴；不再采影子Hub作为语义输出 | `HoFaceAnimationSession`、mod的`HoFaceController` |
| Unity双路供值 | `outputValues`写影子Animator；`PublishSemantics`把同一批输出按名字写角色Hub，调试覆盖也会反映到对应输出行 | `Editor/FaceTracking/HoFaceAnimationSession.cs` |
| Hub | 一个MonoBehaviour；`List<HoFaceSemanticSlot>`，每格key/value；按名字GetFloat/SetFloat，写不存在的名字时追加 | `Runtime/FaceTracking/HoFaceSemanticHub.cs` |
| Warudo分叉 | 参数处理输出Dictionary；求解节点与写Hub节点分别消费；已有字典/键值对/合并节点供其他蓝图逻辑使用 | mod的`HoFaceParameterNode`、`HoFaceSolverNode`、`HoFaceHubWriteNode`、`HoStringFloatMergeNode` |

Hub是纯值工作台：没有控制器列表、执行阶段、参数表达式或物理积分。它本身不限定只能由中间层写，作者脚本也可SetFloat；我们提供的生产路径是中间层。
`HoFaceSemanticWriterBehaviour`、Connector、影子Hub采集与clip按静态下标写槽的产品路径均已删除。
Unity编译器仍拒绝StateMachineBehaviour；业务链不提供控制器内的语义生产器。

## 2. 实际正在调试的资产

读取 `D:/Unity_Project/BREAK_URP/Assets/HoFaceDebugSettings.json` 得到：

- 角色路径：`potato_build`。
- 控制器：`Assets/Hollow/土豆/FT/PTP_CTR_Face_VTS.controller`。
- profile：同目录`ho-iPhoneVTS.hoface.json`。
- `writeParameterHub=true`，当前保存配置已打开Unity写Hub；代码默认值仍为关闭。
- profile为 **67条输入、127条输出**，没有`ARKit/`前缀输出。

对该控制器的真实YAML按`m_BlendType`检查，得到**29棵树**：

| 类型 | 数量 | 当前用途 |
| --- | ---: | --- |
| FreeformCartesian2D | 18 | 姿势表及表情副本 |
| Simple1D | 5 | Mouth/Lid的Smile切换、Brow的Angry切换 |
| Direct | 6 | 一个总根＋Mouth/EyeLeft/EyeRight/Brow/Cheek五个区域汇总 |

总根用五个`Ho/Drive/Gate/*`区域门；区域内部挂`Ho/Drive/W/One`恒定权重；表情切换用`Ho/Drive/Gate/Expr/Smile`和`Angry`。
这符合“浅层姿势混合＋语义选择”的形状，没有把复杂判断铺成一套深层计算树。

中间层已经生成四个MouthCore条件权重：`(1−F)(1−P)`、`F(1−P)`、`(1−F)P`、`FP`，F/P的整形公式均直接在输出行里。
**但本次实际控制器尚未消费这四根Slice参数，也未直接消费Funnel/Press两根轴。** 它们是已计算/已预留的数据，不代表条件切片姿势已经接上。

控制器使用但profile不写的只有：`Ho/Drive/W/One`和两个`Gate/Expr/*`，与当前作者约定一致：恒定权重走控制器默认值，表情门由按键/其他来源提供。

机器检查结果保存在`.research/current-split-audit.json`，生成脚本为`.research/current-split-audit.py`；没有生成或改动控制器。

## 3. 两端一致性：已确认与缺口

### 3.1 九份同步核心一致

去掉mod文件头、按既定规则转换命名空间后，以下九份源码逐字一致：
Expression、Middleware、Profile、ProfileJson、Json、VtsPacket、TrackingChannels、Naming、SemanticHub。
因此当前发现的执行差异，不能简单归为“忘记同步这九份文件”。

### 3.2 Delay执行仍不一致

Unity的`HoFaceAnimationSession.ApplyModifiers`已经处理Delay，并维护逐行FIFO。
mod的`HoFaceChain.ApplyModifiers`仍只处理Smooth/Steps，Delay落入default分支跳过。
两边共享的是修饰符数据类型，逐帧执行器却各自实现；**带Delay配置目前不能据此保证两端一致**。
这是本次确认的执行层差异；未在本轮修改。

### 3.3 历史ARKit前缀兼容仍在mod侧

Unity按输出行名字原样写Animator/Hub；mod的`HoFaceChain.OutputKey()`会把`ARKit/jawOpen`变成`jawOpen`。
当前实际127行profile没有这些前缀，所以不触发；旧配置仍有不同命名行为，不能笼统称所有profile无条件原样互换。

### 3.4 Hub的逐帧写入仍需实机闭环确认

mod写Hub节点的`Apply()`由`WrittenCount()`和`Status()`两个输出getter调用，文件中没有自己的OnUpdate或应用flow入口。
因此需要验证：**没有调试消费者、界面收起时，仍是否持续触发写入**。源码形状不能直接证明它已经是一个稳定的逐帧sink。
既有`FACE_TRACKING_DYNAMIC_PARAMETERS.md`也明确把Warudo节点→角色Hub的实机验证列为未完成。
本轮查看的Player.log只找到旧的“控制器影子里找Hub”记录，不能用于证明当前新路径成功。

### 3.5 Hub是存值，不负责有效性和自动释放

SetFloat写入后保留该值；空字典/缺键不会自动清零，写节点销毁也没有清理已写槽。
这符合Hub当前的纯存值定位，但断流/换源时需要由生产者明确给出有效gate、回退值或交还规则。
写Hub节点没有单独的“有脸”输入；中间层的“有脸”布尔不会自动变成所有Hub值的门控。
不能直接对多写者共享Hub调用全局Zero来替代按来源处理。

## 4. 下游消费者的完成度

本轮在本包Runtime中查找Hub类型与GetFloat调用，尚未找到果冻/摆锤等效果从Hub读取语义的实现。
`HoSpringConstraint.ReadInput()`仍读形态键，随后调用`HoFaceJelly.Step`；它不是Hub消费者。

因此当前可确认：**生产、控制器分支、Hub存值机制已经成形；“各种组件统一消费Hub语义”的扩展还没有在这份Runtime代码中落地。**
这不排除作者在其他角色工程里已经写了自定义消费者，本轮结论只覆盖所查两个仓库。

## 5. 文档里仍有历史描述需要区别

- `FACE_TRACKING_CONTROLLER_STRUCTURE.md`一部分仍写两门Eye/Lip、四个根子节点；实际本次资产已经是五区域门。
- “控制器天生没有参数输出”不应作为技术事实。前一轮已经验证Unity参数曲线可产生float；**当前选择是不使用它承担复杂语义生产**，这是产品边界。
- 动态Hub没有提供稳定的动画下标绑定，不应泛化成Unity引擎永远不能动画组件字段。

这些历史段落没有改变当前源码的分工。后续讨论应以“中间层算复杂逻辑，树做语义姿势混合，同份参数并行发布”这一版为准。
