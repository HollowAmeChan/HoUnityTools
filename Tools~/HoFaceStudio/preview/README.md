# Blender 预览适配器

`ho_face_preview.py` 只接收 HoFaceStudio 发出的最终权重帧：

```text
HoFaceStudio: VTS → value → IR → weights
                                       ↓ UDP 127.0.0.1:21414
Blender: HoFace 显式网格列表 + target ID 同名形态键 → 写入 key.value
```

IR target 是逻辑形态键输出名与权重范围，不包含对象路径。Blender 端不读取或求值 `.hoface.value.json` / `.hoface.ir.json`；它只遍历 HoFace 中显式配置的网格列表，并按 target ID 查找同名形态键。未列入的场景物体不会被扫描。

在 Blender 的 Text Editor 执行脚本后，打开 3D View 侧栏的 `HoFace` 页签，添加要驱动的网格并开始监听。HoFaceStudio 的“预览发送”默认发送到 `127.0.0.1:21414`；预览 v2 帧只携带 value/权重，不携带物体绑定。

后台适配器回归（无需打开 UI）。它会实际启动本机 UDP 监听器、发包并轮询 scene target：

```powershell
blender --background --factory-startup --python Tools~/HoFaceStudio/preview/tests/verify_preview_adapter.py
```

这是薄适配器原型，尚未接入 HoTools 插件注册表，也尚未覆盖骨骼、材质或多目标绑定。它只写最终权重，不参与求值。
