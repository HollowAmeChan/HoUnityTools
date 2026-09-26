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

# 另一类批处理：**控制器骨架生成器**探针（不跑用例，只建一份骨架并打形状）
& "…\Unity.exe" -batchmode -nographics -projectPath $proj `
  -executeMethod HoControllerBuilderProbe.RunBatch -logFile "$env:TEMP\build.log"
# 期望：HO_BUILD params=44 … HO_BUILD trees=27 … HO_BUILD_DONE（形状表见 VTS_HQ_CONTROLLER §5.6）
```

成功标记是 **`HO_FACE_TESTS_ALL_PASSED`**；失败会抛 `HO_FACE_TEST_FAILED: <断言名>` 并 `Exit(1)`。

两个阶段的实时 UDP 断言（第 3 段与最后一段）如果**几帧内没驱动上来**，用例会自己打一条 **`HO_LIVE`**：
接收端统计（包数 / 坏包 / 来源被拒 / 错误原文）+ 合并后的线名值 + 输入行的落点。
它按第 5 / 第 120 帧各打一条；**一切正常时不会打**（值在第一两帧就到位了）。看到它就直接照那几项往下切：
`packets=0` 看 socket/端口，`hasJawWire=False` 看协议解析，`hubJaw=0.9` 但 `weight` 不动看时钟与输入行。

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
| `-batchmode` 起不来 / 一直等授权 | Unity 的授权客户端有**全局互斥量**：本机已经开着一个 Unity 编辑器时**可能**跑不了批处理。关掉编辑器，或换机器 / 换许可。⚠️ 但它**不总是**互斥：2026-09-27 晚上 BREAK_URP 的编辑器一直开着（PID 27308），两次批处理（骨架探针 + 用例）都正常跑完 —— 起不来时再按这条处理，别提前放弃。 |
| `&\ "…\Unity.exe" …` **立刻返回**、`$LASTEXITCODE` 是空的 | Unity 是 GUI 子系统程序，PowerShell 的 `&` **不等它**。于是"后台任务已完成"不代表 Unity 跑完了，还会连带出一堆假象。用 `Start-Process -FilePath $unity -ArgumentList $a -Wait -PassThru -NoNewWindow`，再打 `$p.ExitCode`。 |
| 读日志报 `文件正由另一进程使用` | Unity 还活着、日志还开着。要么等它退出（看进程），要么用共享读：`[IO.File]::Open($log,'Open','Read','ReadWrite')` 再 `StreamReader.ReadToEnd()`。 |
| `ArgumentOutOfRangeException … UnityEditor.Search.SearchDatabase` | **不是我们的错**，是 Unity 自己的搜索库在批处理里起索引时的噪声。看别的堆栈帧确认。 |
| `HO_FACE_TEST_FAILED: jaw blend interpolation actual=0 expected=60`（**80 条 PASS 之后**中止） | **存量问题，不是最后那次改动弄坏的**：用例自己搭的那份源控制器声明的是 `ARKit/<shape>` 参数，而发货的出口行现在是**裸规范名** ⇒ 混合树读到 0。修它要动 `Tests~`，还没修（2026-09-27：在更早的 commit `8fb061e` 的 profile 上同样复现）。看到这条就别往下找回归了。 |

## 一次性工程怎么搭

`Packages/manifest.json` 里 `"com.hollow.hounitytools": "file:D:/Unity_Fork/HoUnityTools"` 指回本仓库，
`Assets/Editor/` 只放用例脚本；其余资产（网格、控制器）由用例自己建。
工程目录本身属于**草稿**：放在 `.research/` 下（gitignore），别提交。
