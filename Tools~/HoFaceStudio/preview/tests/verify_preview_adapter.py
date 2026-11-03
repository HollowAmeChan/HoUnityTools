import importlib.util
from pathlib import Path

import bpy


adapter_path = Path(__file__).resolve().parents[1] / "ho_face_preview.py"
spec = importlib.util.spec_from_file_location("ho_face_preview_validation", adapter_path)
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)
checks = 0


def check(condition, message):
    global checks
    checks += 1
    if not condition:
        raise AssertionError(message)


def make_mesh(name, key_name):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.shape_key_add(name="Basis")
    return obj, obj.shape_key_add(name=key_name)


try:
    adapter.register()
    scene = bpy.context.scene
    listed, listed_key = make_mesh("HoFaceListed", "JawOpen")
    unlisted, unlisted_key = make_mesh("HoFaceUnlisted", "JawOpen")
    unlisted_key.value = 0.33
    entry = scene.ho_face_preview_targets.add()
    entry.object = listed

    adapter._apply_packet(scene, {
        "type": "hoface.preview",
        "version": 2,
        "tracked": True,
        "weights": {"JawOpen": 0.72},
    })
    check(abs(listed_key.value - 0.72) < 1e-6, "final weight writes to listed mesh")
    check(abs(unlisted_key.value - 0.33) < 1e-6, "unlisted mesh is untouched")

    adapter._apply_packet(scene, {
        "type": "hoface.preview",
        "version": 2,
        "tracked": False,
        "weights": {"JawOpen": 0.72},
    })
    check(abs(listed_key.value) < 1e-6, "untracked release clears the owned target")

    listed_key.value = 0.25
    entry.enabled = False
    adapter._apply_packet(scene, {
        "type": "hoface.preview",
        "version": 2,
        "tracked": True,
        "weights": {"JawOpen": 0.9},
    })
    check(abs(listed_key.value - 0.25) < 1e-6, "disabled target is untouched")

    adapter._apply_packet(scene, {
        "type": "hoface.preview",
        "version": 1,
        "tracked": True,
        "weights": {"JawOpen": 0.9},
    })
    check(abs(listed_key.value - 0.25) < 1e-6, "old preview protocol is ignored")
finally:
    adapter.unregister()
    for object_name in ("HoFaceListed", "HoFaceUnlisted"):
        obj = bpy.data.objects.get(object_name)
        if obj is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh_name in ("HoFaceListed", "HoFaceUnlisted"):
        mesh = bpy.data.meshes.get(mesh_name)
        if mesh is not None:
            bpy.data.meshes.remove(mesh)

print("HO_FACE_BLENDER_PASS assertions={}".format(checks))
