# 文档链接检查

从仓库根运行：

```powershell
python Tools~/Documentation/check-links.py
```

脚本使用 Python 标准库和 Git，检查已跟踪及未被忽略的新 Markdown 文件中的本仓库文件/目录链接。存在悬空链接时返回非零退出码；跳过代码示例、远程 URL 和仅含锚点的链接。

它不验证标题锚点或远程网站，也不把仓库外的本机工作区路径算作可移植链接。此类路径以 `EXTERNAL_WORKSPACE` 单独报告；排错记录中的外部 Warudo 工程脚本属于这一类。

移动文档时须同时移动 `.meta` 并保留 GUID；`docs/**/*.md` 的 UTF-8 BOM 与字符有效性另行检查。维护约定见 [文档入口](../../docs/README.md)。
