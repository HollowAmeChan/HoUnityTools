import importlib.util
import json
from pathlib import Path
import socket

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

    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    probe.bind(("127.0.0.1", 0))
    udp_port = probe.getsockname()[1]
    probe.close()
    entry.enabled = True
    scene.ho_face_preview_state.port = udp_port
    check(bpy.ops.ho.face_preview_start() == {"FINISHED"}, "Blender operator opens the loopback receiver")
    check(scene.as_pointer() in adapter._sockets, "Blender retains its active scene socket")
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sender.sendto(json.dumps({
        "type": "hoface.preview", "version": 2, "sequence": 41, "time": 1.5,
        "tracked": True, "values": {"jawOpen": 0.5}, "weights": {"JawOpen": 0.64},
    }).encode("utf-8"), ("127.0.0.1", udp_port))
    adapter._poll()
    check(abs(listed_key.value - 0.64) < 1e-6,
          "loopback UDP frame is polled and applied to Blender: value={}, status={}, sequence={}".format(
              listed_key.value, scene.ho_face_preview_state.last_status, scene.ho_face_preview_state.last_sequence))
    check(scene.ho_face_preview_state.last_sequence == 41
          and scene.ho_face_preview_state.last_status.endswith("41"),
          "loopback UDP updates the visible receiver status")
    sender.sendto(json.dumps({
        "type": "hoface.preview", "version": 2, "sequence": 42, "time": 1.6,
        "tracked": False, "values": {"jawOpen": 0.5}, "weights": {"JawOpen": 0.64},
    }).encode("utf-8"), ("127.0.0.1", udp_port))
    adapter._poll()
    check(abs(listed_key.value) < 1e-6, "loopback tracking-loss frame releases the Blender key")
    sender.close()
    check(bpy.ops.ho.face_preview_stop() == {"FINISHED"}, "Blender operator closes the loopback receiver")
    check(scene.as_pointer() not in adapter._sockets, "stopping removes the scene socket")
    check(not bpy.app.timers.is_registered(adapter._poll), "stopping the last listener removes the poll timer")
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
