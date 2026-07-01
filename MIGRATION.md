# 旧 Hotools 源码迁移说明

## 迁移日期
2026/07/01

## 迁移内容

### 1. 动画处理工具
**源文件**: `Hotools/Editor/AnimationClipProcessorWindow.cs`  
**目标位置**: `Editor/AnimationClipProcessorWindow.cs`

**改动**:
- 命名空间更新: `Hollow.HoUnityTools.Editor`
- 菜单路径更新: `HoUnityTools/动画处理`
- 添加了 XML 文档注释

**功能**: 优化动画文件，仅保留 Float 曲线，删除其他类型曲线（位置、旋转、缩放等）

---

### 2. 约束配置数据类型
**源文件**: `Hotools/Runtime/DataTypes/ConstraintConfig.cs`  
**目标位置**: `Runtime/RigConstraints/ConstraintImport/ConstraintConfig.cs`

**改动**:
- 命名空间更新: `Hollow.HoUnityTools.RigConstraints.Import`
- 添加了 XML 文档注释，明确标注用于骨架约束导入
- 使用独立的 `RigConstraints` 命名空间，专注于骨架处理

**架构考虑**:
- 使用 `RigConstraints` 命名空间明确表示专门处理骨架
- 与通用约束系统（Constraints/）完全独立
- 数据结构保持简单，专注于标准约束导入

---

### 3. 骨架约束导入工具
**源文件**: `Hotools/Editor/ConstraintImporterWindow.cs`  
**目标位置**: `Editor/RigConstraints/ConstraintImporterWindow.cs`

**改动**:
- 命名空间更新: `Hollow.HoUnityTools.Editor.RigConstraints`
- 菜单路径更新: `HoUnityTools/骨架约束/导入标准约束`
- 窗口标题更新为"骨架约束导入"
- 添加了骨架专用的功能说明
- 简化了描述，专注于骨架处理场景

**支持的约束类型**:
- Rotation → RotationConstraint
- Location → PositionConstraint
- Scale → ScaleConstraint
- Child → ParentConstraint

**功能**:
- 从 JSON 配置文件批量导入约束
- 锁定所有标准约束
- 清除所有标准约束

---

## 架构设计考虑

### 命名空间结构
```
Hollow.HoUnityTools
├── Constraints                   # 通用自定义约束系统
│   ├── HoFollowConstraint        # 跟随约束（正在开发）
│   └── HoFloatingConstraint      # 漂浮约束（正在开发）
├── RigConstraints                # 骨架专用约束系统
│   └── Import                    # 骨架约束导入
│       └── ConstraintConfig      # 配置数据结构
└── Editor
    ├── Constraints               # 通用约束编辑器
    │   ├── HoFollowConstraintEditor
    │   └── HoFloatingConstraintEditor
    └── RigConstraints            # 骨架约束工具
        └── ConstraintImporterWindow  # 骨架约束导入工具
```

### 未来扩展空间
1. **骨架约束系统（RigConstraints）**:
   - 当前：`ConstraintImporterWindow` 处理标准 Unity 约束导入
   - 未来：可添加更多骨架专用工具，如骨架验证、约束优化等
   - 可扩展 `ConstraintConfig` 支持更复杂的骨架配置结构

2. **通用约束系统（Constraints）**:
   - 自定义约束（HoFollowConstraint, HoFloatingConstraint）独立发展
   - 不限于骨架使用，可用于任意 GameObject
   - 与骨架约束系统完全独立，互不干扰

3. **菜单组织**:
   - `HoUnityTools/骨架约束/` - 骨架专用工具
   - 未来可添加 `HoUnityTools/约束/` - 通用约束工具
   - 清晰的功能分类

---

## 删除的旧文件
- `Hotools/` 目录（已完全删除）
- `Hotools.meta` 文件

---

## 注意事项
1. 所有代码已更新为新的命名空间规范
2. 菜单路径已统一为 `HoUnityTools/...`
3. 约束导入功能专注于标准 Unity 约束，不干扰自定义约束系统的开发
4. 使用 `Import` 子命名空间隔离，方便未来重构或扩展
