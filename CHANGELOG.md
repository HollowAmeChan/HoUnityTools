# Changelog

## Unreleased

- Added a "还原骨架到 Prefab 姿态" action that reverts every bone Transform's prefab overrides (equivalent to right-clicking a Transform in the Inspector and choosing Revert), restoring position, rotation, and scale to the prefab's stored values. Editor-only, since it relies on the prefab instance relationship; the target rig must be a prefab instance.
- Imported rig constraints are now tagged with a `HoImportedConstraintMarker` component recording the source armature, export time, and exporter version. A new "安全清除（仅删除导入的约束）" action removes only tool-generated constraints and leaves hand-authored constraints untouched.
- Constraint import no longer overwrites pre-existing user constraints of the same type on a bone; such bones are skipped with a warning. Re-importing tool-managed constraints is idempotent (old sources are cleared and rebuilt).
- The destructive "清除全部约束" now also removes orphaned import markers so rig state stays consistent.
- `ConstraintConfig` parses the HoTools Blender export format v1.0 (`version`, `exportTime`, `armatureName`, plus per-constraint `semantic`, `fanType`, `sourceBone`, `space`, and `axes` fields). Only format v1.0 is supported; the legacy flat format is no longer accepted.
- Rig constraint import honors per-axis flags directly: twist constraints lock the Y axis (X|Z only), fan and generic constraints use all axes.

## 0.2.0

- Added `HoFollowConstraint`, a point-to-point Transform constraint for stylized followers such as halos, floating props, drones, and attachment effects.
- Added a custom inspector with foldout sections, presets, runtime readout, and gizmo controls for `HoFollowConstraint`.
- Added `HoFloatingConstraint` for targetless offset, breathing, and noise motion.
- Split breathing and noise motion out of `HoFollowConstraint`; follow is now target-driven only.
- Composed follow offset after the base follow solve so it behaves as a stable additive layer.
- Moved the `光环`, `武器`, `背包`, and `无人机` motion presets to `HoFloatingConstraint`.
- Kept only a `清空` preset on `HoFollowConstraint` for resetting follow settings, axis locks, offsets, and limits.
- Added explicit initial Transform cache controls; presets save the current Transform before changing settings, and restore only happens through the inspector button.
- Updated the package target to Unity 6000.0.

## 0.1.0

- Added Scene To Game View Sync camera tool.
