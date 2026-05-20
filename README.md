# Ho Unity Tools

Small Unity tools packaged as a UPM package.

## Install

Use Unity Package Manager with a local path:

```text
D:/Unity_Fork/HoUnityTools
```

Or add it to `Packages/manifest.json`:

```json
"com.hollow.hounitytools": "file:D:/Unity_Fork/HoUnityTools"
```

## Tools

### Scene To Game View Sync

Add `SceneToGameViewSync` to a Camera, or use:

```text
GameObject > Camera > Scene To Game View Sync Camera
```

The component syncs the last active Scene view camera position, rotation, and FOV to the target Camera.
