# 文档与编码：乱码是怎么来的、怎么不再踩

## 约定

| 文件 | 编码 |
| --- | --- |
| `docs/**/*.md` | UTF-8 **带 BOM** |
| `.cs` | UTF-8 **不带 BOM** |

BOM 不是强迫症：无 BOM 的 UTF-8 在被设成 ANSI/GBK 默认的查看器里会整篇乱码
（`编辑器` → `缂栬緫鍣?`）。这仓库的文档给别的工具和别的查看器看，所以一律带 BOM。

## 1. `edit` 工具会**悄悄吃掉 BOM**

症状：改完一份 `.md`，别人说"你这文件又乱码了"，而你自己看是好的。
原因：很多写入路径（包括 agent 的 `edit` 工具）按无 BOM 重新落盘。
怎么办：改完文档**按字节**把 BOM 补回去，并顺便确认没有替换字符：

```powershell
$b = [IO.File]::ReadAllBytes($f)
if (-not ($b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)) {
  $out = New-Object byte[] ($b.Length + 3); $out[0]=0xEF; $out[1]=0xBB; $out[2]=0xBF
  [Array]::Copy($b, 0, $out, 3, $b.Length); [IO.File]::WriteAllBytes($f, $out)
}
$t = [IO.File]::ReadAllText($f, [Text.Encoding]::UTF8)
([regex]::Matches($t, [char]0xFFFD)).Count      # 必须是 0
```

**同一个写入路径还会顺手改另外两样**，所以改完 `.md` 要核**四件事**（BOM / `U+FFFD` / 行尾 / 空行）：

| 症状 | 原因 | 判据 |
| --- | --- | --- |
| 整个文件在 `git diff` 里全变了 | 写回时把 `\n` 规范化成了 `\r\n`（`[IO.File]::WriteAllText` 在 .NET Framework 下就会） | 数一遍 CR：`([regex]::Matches($t,"`r")).Count`；本来是纯 LF 就把它 `Replace("`r`n","`n")` 复位 |
| 两行**粘成一行**（例如表头与 `| --- |` 合成一行） | `edit` 的 `old_string` 以换行结尾、`new_string` 不以（或反过来）时，尾部空行被吃掉 | 改多行块时让 old/new 的**首尾空行一致**；改完扫一眼那几行 |
| 中文变问号 / 掉字 | 把中文塞进了命令行 | 改中文只用 `edit`/`write` 写字面量（见 §2.1） |

多 worker 并行改 `docs/` 时，还要在收尾**重新**验一次这些（可能有人在你之后动过同一个文件）。

## 2. 别用 PowerShell 文本 cmdlet 批量改中文

`Get-Content -Raw | Set-Content` 会按当前代码页重新编码，**曾经一次弄坏 5 个文件**。
必须批量替换时走字节安全的写法：

```powershell
$t = [IO.File]::ReadAllText($f, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($f, $t.Replace('旧', '新'), (New-Object Text.UTF8Encoding($true)))  # md 用 $true
```

`.cs` 用 `UTF8Encoding($false)` —— 别顺手给脚本文件加 BOM，跟同目录其它脚本保持一致。

### 2.1 更阴的一种：**写在命令里的中文本身**会被弄坏

症状：明明只做了一次字符串替换，改完却出现"**日期 → 期期**""**详见 [面捕 → 详见 面面捕**""**要精确 → 精精确**"
这种**掉一个字 / 多一个字**的错位，`U+FFFD` 还是 0，所以编码检查看不出来。
原因：内联的 PowerShell 命令文本经过一层编码往返时把中文弄坏了 —— 坏的是**命令里的字面量**，不是目标文件。

怎么办：
- 改中文**只用 `edit` 工具**（按 UTF-8 字面量匹配），不要往命令行里塞中文。
- 非要脚本批量改，就把脚本**写成文件**（同样是 UTF-8）再执行，别内联。
- 改完随手扫一眼那几行：`Select-String -Path <file> -Pattern '<你改过的关键词>'`。

### 2.2 脚本文件（`.ps1`）里的中文：写成 ASCII 最省事

症状：一个带中文注释与中文字符串的 `.ps1` 跑起来报
`字符串缺少终止符: "`（或者中文全变成 `涓?` 这种）。

原因：**PowerShell 5.1 按 ANSI/GBK 读没有 BOM 的 `.ps1`** —— `.md` 我们是特意带 BOM 的，
`.ps1`/`.cs` 又不带，于是同一个"UTF-8 无 BOM"在这里就成了 bug。

怎么办：**`.research/` 下的一次性脚本一律写成纯 ASCII**（注释也英文）；
真要写中文，就给 `.ps1` 加 BOM。`.cs` 不受影响（编译器按 UTF-8 读）。

## 3. 控制台里的乱码**通常不是文件坏了**

`Get-Content` / `git show` / Unity 日志在默认代码页下会把正常的 UTF-8 中文显示成
`瑙傚療`、`馃槃` 之类。**先按字节确认真假**再动手"修"：

```powershell
[IO.File]::ReadAllText($f, [Text.Encoding]::UTF8)   # 0 个 U+FFFD + 中文正常 = 文件没问题
```

Unity 的日志同理：`[IO.File]::ReadAllLines($log, [Text.Encoding]::UTF8)` 才是正确读法
（见 [批处理验证](VALIDATION_LOOP.md)）。

## 4. 文档里的数字要能对回现场

一条实测结论写进文档时，**带上"在哪能重跑出来"**（用例名 / 脚本 / 探针日志行）。
删文档时先确认它引用的东西还在 —— 陈旧的往往不是结论，而是"结论引用的那个地方"。
