# Ho Unity Tools

Small Unity tools packaged as a UPM package.

## Install

Requires Unity 6000.0 or newer.

Use Unity Package Manager with a local path:

```text
D:/Unity_Fork/HoUnityTools
```

Or add it to `Packages/manifest.json`:

```json
"com.hollow.hounitytools": "file:D:/Unity_Fork/HoUnityTools"
```

## Tools

### Ho 跟随约束

Add `HoFollowConstraint` to any GameObject, or use:

```text
GameObject > HoUnityTools > 约束 > 跟随约束
```

The component is a pure point-to-point Transform constraint. It follows a target without Rigidbody, Joint, PhysBone, bone-chain propagation, or physics solvers. It includes follow response, overshoot, position and rotation axis locks, rotation filtering, offset, optional soft limits, and selected gizmos. If no target is assigned, the component does not write to the Transform.

Use `保存初始变换` to cache the original local transform before applying presets. Restoring is explicit through `恢复初始变换`; removing the component does not automatically restore the Transform.

### Ho Floating Constraint

Add `HoFloatingConstraint` to any GameObject, or use:

```text
GameObject > HoUnityTools > 约束 > 漂浮约束
```

Use this component for targetless offset, breathing, and noise motion. It keeps its own initial Transform cache and can be stacked with `HoFollowConstraint`.

Inspector includes localized starting presets:

- `清空`
- `光环`
- `武器`
- `背包`
- `无人机`

### Scene To Game View Sync

Add `SceneToGameViewSync` to a Camera, or use:

```text
GameObject > Camera > Scene To Game View Sync Camera
```

The component syncs the last active Scene view camera position, rotation, FOV, and clipping planes to the target Camera.
