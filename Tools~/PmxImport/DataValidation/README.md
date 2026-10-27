# PMX 图片与 Ray 数据验证

此工具直接编译导入器实际使用的 `Editor/PmxImport/Ray/*.cs` 和 `HoPmxSourceImage.cs`。需要 .NET 10 SDK；不依赖 Unity、GPU 或额外 NuGet 包。

在 HoUnityTools 仓库根目录运行：

```powershell
dotnet run --project Tools~/PmxImport/DataValidation
```

默认全部使用生成的合成数据，不包含第三方模型、图片或 shader。验证内容：

- 37 个 BMP/TGA 原始字节用例：24/32 位、行填充、上下／左右原点、灰度、Alpha、RLE、截断、越界与尺寸检查。核对输出为 bottom-left RGBA，通道不做 Gamma 转换。
- Ray 宏常量／条件表达式、注释与嵌套 include、启用贴图依赖、禁用占位文件、多 EMD 歧义和显式选择、未识别 FX 的保守处理。
- Shininess／Roughness 到 Smoothness 的公式，以及 Metallic／Smoothness 的乘法／幂次缩放。

可额外只读验证本机云景千山配布包：

```powershell
dotnet run --project Tools~/PmxImport/DataValidation -- --ray-pmx "E:/path/to/云景千山v2.pmx"
```

该选项针对已调查的云景千山版本检查 43 个材质槽、25 个独立 FX、21 个法线配置、20 个金属配置、2 个专用 Cutout，以及全部 Smoothness 使用 MaterialPower。只读取传入路径附近的 EMD／FX／有效贴图引用，不写源包；测试素材不随仓库分发。

成功返回退出码 0；断言失败返回 1；参数错误返回 2。合成 FX 写入系统临时目录并在测试结束后清理。

这些测试验证数据读取及字节保真，不代替 Unity 材质、透明排序、切线基底或实际渲染验证。
