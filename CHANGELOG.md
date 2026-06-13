# Changelog

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
