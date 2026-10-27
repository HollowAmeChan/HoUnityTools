# PMX 原生导入器参考

调查时间：2026-10-28。目标是 Ho lilToon 静态场景的直接导入、原生 Inspector、相对贴图及材质提取。以下仓库已拉取到本机研究目录；完整外部仓库与模型不进入产品包。

| 参考 | 核对版本/许可证 | 采用的设计参考与边界 |
| --- | --- | --- |
| [UnityMMDTools](https://github.com/CandidumGames/UnityMMDTools) | db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4，MIT | 原生 ScriptedImporter、Inspector、外部材质 remap；Y180 旋转与 UV V 翻转。其完整管线耦合 Collections/Mathematics、动画和原生物理，未整体引入 |
| [unity-mmd-loader](https://github.com/yohawing/unity-mmd-loader) | 8928de9，MIT | Unity 6 原生导入工作流及坐标转换交叉核对；未引入其 native runtime/shader |
| [libmmd-for-unity](https://github.com/x3bits/libmmd-for-unity) | BSD-3-Clause | 作为既有运行时加载方案比较；目标为运行时及 Bullet，与本工具静态资产入口不同 |
| [mmd-for-unity](https://github.com/mmd-for-unity-proj/mmd-for-unity) | BSD-3-Clause | 历史 Unity MMD 导入实现参考，没有引入代码 |

HoPmxReader 按 PMX 作者规格自行实现，未复制上述解析器代码。核对发现 UnityMMDTools parser 有 Unity 类型与 FixedString 耦合、2.1 特殊数据支持边界，无法直接作为本功能独立的小型解析层。

本机格式原件：D:/_SOFTWARE/MMD_FLOWS/PmxEditor_0273/Lib/PMX仕様/PMX仕様.txt。

主要上游源码入口：

- [UMT ScriptedImporter](https://github.com/CandidumGames/UnityMMDTools/blob/db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4/Editor/Importers/PMXScriptedImporter.cs)
- [UMT Material Remap](https://github.com/CandidumGames/UnityMMDTools/blob/db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4/Editor/Importers/PMXMaterialRemapEditor.cs)
- [UMT PMX Reader](https://github.com/CandidumGames/UnityMMDTools/blob/db35d9cb80ad57a8b2cbd40ad737a3c7bbe2d6c4/Runtime/PMX/PMXReader.cs)
- [Unity ScriptedImporter](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/assetimporters/scriptedimporter)
- [Unity AddRemap](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/assetimporter/addremap)

已知 Ray 的宏、采样和高光系数转换按源包随附 FX/FXSUB 核对，作为兼容协议读取；不会在 Unity 执行外部 FX 程序。
