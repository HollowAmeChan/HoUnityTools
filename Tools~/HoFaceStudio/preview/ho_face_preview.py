"""HoFaceStudio UDP preview adapter for Blender.

This adapter applies final weights only. It never reads .hoface.value.json or
.hoface.ir.json and never evaluates rules. The desktop process owns that work.

Usage inside Blender:
  1. Run this file from the Text Editor, or install it as an add-on.
  2. Open the HoFace Preview panel in the 3D View sidebar.
  3. Start listening on port 21414.
  4. Name Blender objects to match IR target ``slot`` values.
"""

import json
import socket
import time

import bpy


def _state(scene):
    return getattr(scene, "ho_face_preview_state", None)


def _close_socket(scene):
    state = _state(scene)
    if state is None:
        return
    sock = getattr(state, "socket", None)
    if sock is not None:
        sock.close()
    state.socket = None
    state.last_status = "已停止"


def _apply_packet(scene, packet):
    if packet.get("type") != "hoface.preview" or packet.get("version") != 1:
        return
    targets = packet.get("targets") or []
    weights = packet.get("weights") or {}
    tracked = bool(packet.get("tracked", False))
    for target in targets:
        target_id = target.get("id")
        slot = target.get("slot") or ""
        shape = target.get("shape") or ""
        if not target_id or not shape:
            continue
        obj = bpy.data.objects.get(slot)
        if obj is None or obj.data is None or not getattr(obj.data, "shape_keys", None):
            continue
        key = obj.data.shape_keys.key_blocks.get(shape)
        if key is None:
            continue
        value = float(weights.get(target_id, 0.0)) if tracked else 0.0
        key.value = max(key.slider_min, min(key.slider_max, value))


def _poll():
    scene = bpy.context.scene
    state = _state(scene)
    if state is None or state.socket is None:
        return 0.03
    try:
        while True:
            payload, _address = state.socket.recvfrom(1024 * 1024)
            packet = json.loads(payload.decode("utf-8"))
            _apply_packet(scene, packet)
            state.last_sequence = int(packet.get("sequence", state.last_sequence))
            state.last_status = "运行中 · 序号 {}".format(state.last_sequence)
    except BlockingIOError:
        pass
    except Exception as error:
        state.last_status = "错误：" + str(error)
    for area in bpy.context.screen.areas if bpy.context.screen else []:
        if area.type == "VIEW_3D":
            area.tag_redraw()
    return 0.03


class HO_PG_FacePreviewState(bpy.types.PropertyGroup):
    port: bpy.props.IntProperty(name="端口", default=21414, min=1, max=65535)
    last_status: bpy.props.StringProperty(name="状态", default="未启动")
    last_sequence: bpy.props.IntProperty(name="序号", default=0)
    socket = None


class HO_OT_FacePreviewStart(bpy.types.Operator):
    bl_idname = "ho.face_preview_start"
    bl_label = "开始 HoFace 预览"

    def execute(self, context):
        _close_socket(context.scene)
        state = _state(context.scene)
        state.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        state.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        state.socket.bind(("127.0.0.1", state.port))
        state.socket.setblocking(False)
        state.last_status = "监听 127.0.0.1:{}".format(state.port)
        if not bpy.app.timers.is_registered(_poll):
            bpy.app.timers.register(_poll, first_interval=0.03, persistent=True)
        return {"FINISHED"}


class HO_OT_FacePreviewStop(bpy.types.Operator):
    bl_idname = "ho.face_preview_stop"
    bl_label = "停止 HoFace 预览"

    def execute(self, context):
        _close_socket(context.scene)
        return {"FINISHED"}


class HO_PT_FacePreview(bpy.types.Panel):
    bl_idname = "HO_PT_face_preview"
    bl_label = "HoFace 预览"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "HoFace"

    def draw(self, context):
        layout = self.layout
        state = _state(context.scene)
        layout.prop(state, "port")
        row = layout.row(align=True)
        row.operator(HO_OT_FacePreviewStart.bl_idname, icon="PLAY")
        row.operator(HO_OT_FacePreviewStop.bl_idname, icon="PAUSE")
        layout.label(text=state.last_status)
        layout.label(text="IR slot 必须对应 Blender 物体名")


CLASSES = (HO_PG_FacePreviewState, HO_OT_FacePreviewStart,
           HO_OT_FacePreviewStop, HO_PT_FacePreview)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ho_face_preview_state = bpy.props.PointerProperty(
        type=HO_PG_FacePreviewState)


def unregister():
    if hasattr(bpy.types.Scene, "ho_face_preview_state"):
        for scene in bpy.data.scenes:
            _close_socket(scene)
        del bpy.types.Scene.ho_face_preview_state
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    try:
        unregister()
    except Exception:
        pass
    register()
