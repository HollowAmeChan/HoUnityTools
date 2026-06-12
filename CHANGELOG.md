# Changelog

## 0.2.0

- Added `HoFollowConstraint`, a point-to-point Transform constraint for stylized followers such as halos, floating props, drones, and attachment effects.
- Added a custom inspector with foldout sections, presets, runtime readout, and gizmo controls for `HoFollowConstraint`.
- Allowed offset, oscillation, and noise to evaluate from the local anchor pose when no target is assigned, preserving parent motion.
- Composed offset after the base follow solve so it behaves as a stable additive layer in both targeted and targetless modes.
- Added a `清空` preset that resets follow settings, axis locks, offsets, oscillation, noise, and limits to a neutral state.
- Added explicit initial Transform cache controls; presets save the current Transform before changing settings, and restore only happens through the inspector button.
- Updated the package target to Unity 6000.0.

## 0.1.0

- Added Scene To Game View Sync camera tool.
