# PMX 导入验证

工具实现仅使用 Unity Editor/Engine API。请在专用隔离工程运行验证。

## 原生导入与渲染

prepare-validation.py 在专用目录建立 Unity 验证项目，复制 PMX/有效依赖并保留相对路径，同时校验源文件与复制文件的 SHA256。默认样本来自本机素材库，不随仓库发布。

~~~powershell
python Tools~/PmxImport/prepare-validation.py
python Tools~/PmxImport/prepare-validation.py --sync-code-only
~~~

具体参数以脚本 --help 为准。验证项目默认是 D:/Unity_Fork/.codex_temp/HoPmxImportValidation，结果写入该项目的 Results/。

Unity 批处理入口：

~~~text
-executeMethod HoPmxImportValidation.Run
-executeMethod HoPmxImportValidation.RenderSmoke
~~~

Run 检查真实 PMX 导入、原始几何计数、相对路径、法线/UV、Ho lilToon、透明/双面/描边、Ray 法线/金属图、材质提取/重导关联、原生 Apply/Revert 与窗纸 UV 表情烘焙。

RenderSmoke 需要真实图形设备（Windows 使用 -force-d3d11，不加 -nographics），输出四份场景 PNG。空帧/洋红检查是基本烟测，仍需人工查看结果；不能据此声称完全重现 MMD 着色。

图片与结果分颜色空间/图形 API 保存，例如 Results/render-Gamma-Direct3D11/。改变颜色空间应在启动验证前完成，避免在 RenderSmoke 执行过程中触发项目重导入。当前通过的是 Gamma 基线；补充的 Linear 设备丢失与 Gamma 视角探针停滞另行记录，详见 [验证摘要](../../Tests~/PmxImport/ValidationResults-20261028.json)。文件可能来自较早一次运行，必须核对日志和时间，不能仅凭残留的 PASS 文件确认最新测试成功。

## 纯数据验证

Format 及 Ray 目录不依赖 Unity，可单独编译。ParserValidation 提供合成格式与畸形输入断言，以及本地目录批量解析入口。数据解析成功不等于 Unity 导入和外观均通过。

[DataValidation](DataValidation/README.md) 另提供 BMP/TGA 原始像素、Ray 宏/依赖/采样规则验证，以及可选的真实云景源包核对。

源素材只读，结果模型仅位于隔离项目；脚本不重新导入用户的 BREAK_URP 工程。
