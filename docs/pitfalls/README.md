# 踩过的坑

这里放**具体咬过我们一口的东西**：症状 → 原因 → 怎么办。

判据是"当时真的发生了、而且下次还会发生"。能用一段知识讲清楚的东西写在对应文档里；
这里只留那些会让人白花半天、或者**让我们下过错误结论**的。

| 文件 | 什么时候来看 |
| --- | --- |
| [Unity YAML 与转储](UNITY_YAML_AND_DUMPS.md) | 要直接读 `.controller` / `.anim` 原文，或写脚本解析它们 |
| [混合树的坑](BLEND_TREE_TRAPS.md) | 摆树、看权重、怀疑"为什么不归一化 / 为什么发散" |
| [形态键输出](SHAPE_KEY_OUTPUT.md) | 键怎么都是 100、关掉规则不回 0、左右族/内外族混用 |
| [Animator IK 与更新时机](UNITY_IK_AND_TIMING.md) | 头不动、IK 收不到、尾巴一阵一阵抽搐、和布料抢骨头 |
| [鼠标与指针输入](INPUT_AND_MOUSE.md) | 注视约束的鼠标目标：失焦、基准方向、射线交点、参考系 |
| [液体 shader 契约](LIQUID_SHADER.md) | 摆锤/液面：长帧 NaN、atan2、坐标系、MPB、缩放 |
| [Unity 资产与编辑器](UNITY_ASSET_PITFALLS.md) | 复制或重写资产、改别人的 `.anim`、按名字对资产 |
| [编辑器 UI 与 Playable API](EDITOR_UI_AND_API.md) | 面板排版不对齐、`[Header]` 画两遍、`PlayableGraph.IsValid()` |
| [批处理验证](VALIDATION_LOOP.md) | 跑 `Tests~/` 那套用例，或结果不对劲时 |
| [Warudo 打包、工具链与系统脚本](BUILD_AND_TOOLING.md) | Mod 产物 Missing Script、`.csproj`、UMod 安全审查、提权脚本 |
| [文档与编码](DOCS_ENCODING.md) | 改带中文的 `.md` / `.cs`，或看到乱码 |
| [仓库与提交](REPO_AND_GIT.md) | 提交、并行改动、`.research/` 这些东西怎么处理 |

**写新条目的格式**：一句症状（怎么发现的）→ 一句原因 → 一句怎么办。
带数字的（实测值、报错原文）尽量原样保留 —— 它们才是这条记录的价值。

**一条挑假设的方法**：复核一份外部资产或别人的实现时，别通读一遍结构；
挑**"我们的模型依赖、但没直接量过"**的那个假设去打（例如"引擎会不会替我钳制"），一击即中。
通读只会重新确认已经知道的东西。
