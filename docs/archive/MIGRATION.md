# 旧 Hotools 源码迁移说明

## 迁移日期

2026/07/01

## 迁移内容

- `Hotools/Editor/AnimationClipProcessorWindow.cs` → `Editor/AnimationTools/AnimationClipProcessorWindow.cs`，命名空间 `Hollow.HoUnityTools.Editor`。
  （2026-09-25 更新：那个窗口已并进 `HoUnityTools/动画工具` 的「轨道处理」栏 —— 逻辑搬进
  `Editor/AnimationTools/HoAnimationClipProcessor.cs`，窗口本身删了。）
- `Hotools/Runtime/DataTypes/ConstraintConfig.cs` → `Runtime/RigConstraints/ConstraintImport/ConstraintConfig.cs`，命名空间 `Hollow.HoUnityTools.RigConstraints.Import`。
- 旧标准约束导入窗口合并进中控：`Editor/HoFbxImport/HoFbxImportProcessingWindow.cs`，命名空间 `Hollow.HoUnityTools.Editor.RigConstraints`。

## 删除的旧文件

- `Hotools/` 目录（已完全删除）
- `Hotools.meta` 文件
