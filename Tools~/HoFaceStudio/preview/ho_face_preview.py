"""HoFaceStudio UDP preview adapter for Blender.

This adapter applies final weights only. It never reads .hoface.value.json or
.hoface.ir.json and never evaluates rules. The desktop process owns that work.

Usage inside Blender:
  1. Run this file from the Text Editor, or install it as an add-on.
  2. Open the HoFace Preview panel in the 3D View sidebar.
  3. Start listening on port 21414.
  4. Add the meshes to the explicit preview target list.
"""

import json
import socket

import bpy
from bpy.props import BoolProperty, CollectionProperty, IntProperty, PointerProperty
from bpy.types import Operator, Panel, PropertyGroup, UIList

_sockets = {}


def _state(scene):
    return getattr(scene, "ho_face_preview_state", None)


def _close_socket(scene):
    state = _state(scene)
    sock = _sockets.pop(scene.as_pointer(), None)
    if sock is not None:
        sock.close()
    if state is not None:
        state.last_status = "已停止"
    if not _sockets and bpy.app.timers.is_registered(_poll):
        bpy.app.timers.unregister(_poll)


def _apply_packet(scene, packet):
    if packet.get("type") != "hoface.preview" or packet.get("version") != 2:
        return
    weights = packet.get("weights") or {}
    tracked = bool(packet.get("tracked", False))
    for target in scene.ho_face_preview_targets:
        obj = target.object
        if not target.enabled or obj is None or obj.type != "MESH":
            continue
        shape_keys = getattr(getattr(obj, "data", None), "shape_keys", None)
        if shape_keys is None:
            continue
        for target_id, weight in weights.items():
            key = shape_keys.key_blocks.get(target_id)
            if key is None:
                continue
            value = float(weight) if tracked else 0.0
            key.value = max(key.slider_min, min(key.slider_max, value))


def _poll():
    scene = bpy.context.scene
    state = _state(scene)
    sock = _sockets.get(scene.as_pointer())
    if state is None or sock is None:
        return 0.03 if _sockets else None
    try:
        while True:
            payload, _address = sock.recvfrom(1024 * 1024)
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


class HO_PG_FacePreviewTarget(PropertyGroup):
    object: PointerProperty(type=bpy.types.Object, name="网格")
    enabled: BoolProperty(name="启用", default=True)


class HO_UL_FacePreviewTargets(UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        row = layout.row(align=True)
        row.prop(item, "enabled", text="")
        row.prop(item, "object", text="", emboss=False, icon="MESH_DATA")


class HO_OT_FacePreviewAddSelected(Operator):
    bl_idname = "ho.face_preview_add_selected"
    bl_label = "添加选中网格"

    def execute(self, context):
        targets = context.scene.ho_face_preview_targets
        existing = {item.object for item in targets if item.object}
        for obj in context.selected_objects:
            if obj.type == "MESH" and obj not in existing:
                targets.add().object = obj
                existing.add(obj)
        return {"FINISHED"}


class HO_OT_FacePreviewRemoveTarget(Operator):
    bl_idname = "ho.face_preview_remove_target"
    bl_label = "移除网格"

    def execute(self, context):
        scene = context.scene
        index = scene.ho_face_preview_target_index
        if 0 <= index < len(scene.ho_face_preview_targets):
            scene.ho_face_preview_targets.remove(index)
            scene.ho_face_preview_target_index = min(index, max(0, len(scene.ho_face_preview_targets) - 1))
        return {"FINISHED"}


class HO_OT_FacePreviewStart(bpy.types.Operator):
    bl_idname = "ho.face_preview_start"
    bl_label = "开始 HoFace 预览"

    def execute(self, context):
        scene = context.scene
        _close_socket(scene)
        state = _state(scene)
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(("127.0.0.1", state.port))
        sock.setblocking(False)
        _sockets[scene.as_pointer()] = sock
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
        box = layout.box()
        box.label(text="驱动物体")
        box.template_list("HO_UL_FacePreviewTargets", "", context.scene, "ho_face_preview_targets",
                          context.scene, "ho_face_preview_target_index", rows=3)
        row = box.row(align=True)
        row.operator(HO_OT_FacePreviewAddSelected.bl_idname, text="添加选中", icon="ADD")
        row.operator(HO_OT_FacePreviewRemoveTarget.bl_idname, text="", icon="REMOVE")
        box.label(text="IR target ID 按同名形态键写入")


CLASSES = (HO_PG_FacePreviewState, HO_PG_FacePreviewTarget, HO_UL_FacePreviewTargets,
           HO_OT_FacePreviewAddSelected, HO_OT_FacePreviewRemoveTarget,
           HO_OT_FacePreviewStart, HO_OT_FacePreviewStop, HO_PT_FacePreview)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ho_face_preview_state = bpy.props.PointerProperty(
        type=HO_PG_FacePreviewState)
    bpy.types.Scene.ho_face_preview_targets = CollectionProperty(type=HO_PG_FacePreviewTarget)
    bpy.types.Scene.ho_face_preview_target_index = IntProperty(default=0)


def unregister():
    if hasattr(bpy.types.Scene, "ho_face_preview_state"):
        for scene in bpy.data.scenes:
            _close_socket(scene)
        del bpy.types.Scene.ho_face_preview_state
    if hasattr(bpy.types.Scene, "ho_face_preview_target_index"):
        del bpy.types.Scene.ho_face_preview_target_index
    if hasattr(bpy.types.Scene, "ho_face_preview_targets"):
        del bpy.types.Scene.ho_face_preview_targets
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    try:
        unregister()
    except Exception:
        pass
    register()
