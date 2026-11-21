# 日期版本与安装包发布

本仓库沿用 HoTools 的简单日期发布号：`YYYYMMDD-HHMMSS`（UTC），例如
`20261121-120000`。Git 标签为 `v20261121-120000`，Release 标题为
`仓库名-20261121-120000`。同一个 Release 中的模块使用同一日期号。

## 自动与手动发布

- 推送默认分支 `master` 自动运行 `.github/workflows/release.yml`。
- 也可以在 GitHub Actions 的 **Auto Release Unity Installers > Run workflow** 手动运行。
- 手动运行时可留空 `version` 使用当前 UTC，或填同一个 `YYYYMMDD-HHMMSS` 为多个仓库标注一次配布批次。
- 只需仓库自带的 `GITHUB_TOKEN`，不需要 Unity license 或个人访问令牌。工作流使用 `contents: write`。
- 相同日期标签不得覆盖；重复发布请使用新的时间。只在默认分支发布。

## 给使用者的安装方式

从本仓库 GitHub **Releases** 下载上传的 `.tgz` 安装包，在 Unity 的 Package Manager
选择 **Install package from tarball**（旧版 **Add package from tarball**）。不要将
GitHub 自动附带的 Source code 压缩包当作安装包。

HoShader 的各模块分别打包，按项目使用的渲染管线选装。先安装项目需要的 HoURP / Config，
再安装材质和扩展；ZMD Viewer 应搭配同次 HoShader Release 的 ZMD URP 模块。
lilToon 与 lilToon-Ho 额外提供 `.unitypackage`，适合 Assets 导入。
同一个包只选一种安装方式；两份 lilToon 包名与 GUID 相同，只能安装其中一个。

## 查看和对齐版本

每个包根目录的 `version_info.json` 记录日期发布号、Git 标签、源提交、仓库与原始包版本。
工作区文件标记为 `dev`；打包时只在安装包内写入正式号，不改写工作区或提交回仓库。

安装 HoUnityTools 后，打开 **HoUnityTools > Ho 渲版本信息**，查看并复制当前项目的版本清单。
配布时保存该清单，使用者逐项核对仓库、日期号和 commit；`dev` 表示该包不是正式安装包。
Git URL 安装请锁定 Release 标签（`#v日期号`，保留原本的 `?path=`）；Git 源码里的
JSON 仍为 `dev`，版本窗口会另列 Git 修订值，请以锁定标签和 commit 核对。

日期号是独立的 Ho 发布版本。`package.json.version` 保留 URP / lilToon 等原始 SemVer，
依赖版本也不修改；它不是 Ho 日期发布号。同日号只能标识发布批次，不自动保证模块间兼容。

Release 同时提供 `release-manifest.json`（源提交、所有模块及依赖、文件 SHA-256）和
`SHA256SUMS.txt`。安装包只收集 Git 已跟踪的包文件，排除开发脚本、测试、缓存与本地未跟踪文件；
未下载的 Git LFS 指针会直接导致打包失败。

## 本地验证

Python 3.10+ 与 Git 即可，不需要第三方 Python 依赖。在仓库根目录执行：

```sh
python -m unittest discover -s .github/tests -v
python .github/scripts/build_release.py --version 20261121-120000
```

默认输出 `_dist/`；输出目录必须为空，可用 `--output` 指定其他目录。请从干净且已提交的
工作区构建；本地脚本读取已跟踪文件的当前内容，缺失的已跟踪文件会报错，工作区修改不会自动提交。
GitHub Actions 从触发提交的干净 checkout 构建。包目录清单在 `.github/release-config.json`。
共享脚本和合约测试在相关仓库内各保存一份，以便独立发布，不依赖另一个仓库的未锁定分支。

每个包还记录内容 `content_sha256`，可区分同一基准提交上的本地修改。
