# 批处理验证这套用例怎么跑、以及它会在哪绊你

```powershell
# 1) 把用例拷进一次性工程（工程根必须有 .ho-face-validation 这个空文件当标记）
Copy-Item "Tests~\FaceTrackingValidation.cs" "$proj\Assets\Editor\FaceTrackingValidation.cs" -Force

# 2) 跑；日志别放以点开头的目录
& "C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe" -batchmode -nographics `
  -projectPath $proj -executeMethod HoFaceTrackingValidation.RunBatch -logFile "$env:TEMP\val.log"

# 3) 看结果
$c = [IO.File]::ReadAllLines("$env:TEMP\val.log", [Text.Encoding]::UTF8)
$c | Select-String "error CS"                      # 编译不过 → 一条断言都不会跑
$c | Select-String "HO_FACE_TEST" | Select-Object -Last 3
```

成功标记是 **`HO_FACE_TESTS_ALL_PASSED`**；失败会抛 `HO_FACE_TEST_FAILED: <断言名>` 并 `Exit(1)`。

## 会绊人的地方

| 症状 | 原因 / 怎么办 |
| --- | --- |
| `-logFile .research\val.log` → **`.research is not a valid directory name`** | Unity 不接受点开头的日志目录。用 `$env:TEMP`。 |
| `Disposable project marker missing.` | 一次性工程根少了 `.ho-face-validation` 空文件（防手滑写到真工程上）。 |
| 最后一行出现 `HO_FACE_TEST_SKIPPED: receiver tests`，而不是失败 | **UDP 49983 被占了**（Warudo、本机面捕面板、另一个 Unity 编辑器都算）。用 `Get-NetUDPEndpoint -LocalPort 49983` 确认；这是环境问题，用例故意跳过而不是假装通过。 |
| 一条断言都没有，只有别人的 `error CS` | 一次性工程里混进了**别的 worker 正在写的**用例文件（`HoAnimationPreviewValidation.cs` 之类）。用自己的一份工程副本（例如 `.research/UnityFaceValidation2`），别去动共享那份。 |
| 输出里中文全是乱码 | 只是**读日志的方式**：PS 默认按 ANSI 读。按 UTF-8 读（见上面第 3 步）。文件本身没问题。 |
| 第一次跑要几分钟 | 全新工程要全量导入 + 编译；之后每次十几秒。 |
| 播放阶段的用例超时 | 用例靠场景里的 Animator 跑；没有相机时必须 `AlwaysAnimate`（脚本里已设），以及 49983 不能用。 |
| `-batchmode` 起不来 / 一直等授权 | Unity 的授权客户端有**全局互斥量**：本机已经开着一个 Unity 编辑器时跑不了批处理。关掉编辑器，或换机器 / 换许可。 |

## 一次性工程怎么搭

`Packages/manifest.json` 里 `"com.hollow.hounitytools": "file:D:/Unity_Fork/HoUnityTools"` 指回本仓库，
`Assets/Editor/` 只放用例脚本；其余资产（网格、控制器）由用例自己建。
工程目录本身属于**草稿**：放在 `.research/` 下（gitignore），别提交。
