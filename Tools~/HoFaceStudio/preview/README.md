# Blender 预览适配器

`ho_face_preview.py` 只接收 HoFaceStudio 发出的最终权重帧：

```text
HoFaceStudio: VTS → value → IR → weights
                                       ↓ UDP 127.0.0.1:21414
Blender: slot 对应物体名，shape 对应形态键名 → 写入 key.value
```

Blender 端不读取或求值 `.hoface.value.json` / `.hoface.ir.json`，不会产生第二套规则实现。把 IR target 的 `slot` 填成 Blender 物体名即可；同一物体上按 `shape` 找形态键。

在 Blender 的 Text Editor 执行脚本后，打开 3D View 侧栏的 `HoFace` 页签，点击开始监听。HoFaceStudio 的“预览发送”默认发送到 `127.0.0.1:21414`。

这是薄适配器原型，尚未接入 HoTools 插件注册表，也尚未覆盖骨骼、材质或多目标绑定。它只写最终权重，不参与求值。
