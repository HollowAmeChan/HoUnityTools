using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 把"鼠标在 Scene 视图上的位置 + Scene 视图相机"每帧喂给运行时（<see cref="HoMousePointer.EditorPointer"/>）。
    ///
    /// 为什么需要：用鼠标调试时 Gizmo 只在 Scene 视图里画，而指针输入通常只能来自 Game 视图 ——
    /// 两边分开很别扭。喂了这份数据之后，鼠标放在 Scene 视图上就能直接调试：
    /// 观众视角临时换成 Scene 视图相机，注视点就是你鼠标指的那个位置，Gizmo 也在同一个视图里。
    ///
    /// 运行时程序集不能引用 UnityEditor，所以数据由这里单向写入。
    /// </summary>
    [InitializeOnLoad]
    internal static class HoSceneViewPointer
    {
        static HoSceneViewPointer()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>
        /// 保活：鼠标停在 Scene 视图上时，Scene 视图可能长时间不重绘（位置事件就不来了），
        /// 那样时间戳会过期、鼠标控制会突然掉回 Game 视图。这里只要"鼠标还在 Scene 视图上"
        /// 就刷新时间戳与相机；位置仍用最后一次鼠标移动时的值（没动过就是对的）。
        /// 鼠标一离开 Scene 视图就没人刷新了，0.5 秒后自然过期、回落到真实输入。
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (!(EditorWindow.mouseOverWindow is SceneView hovered) || hovered.camera == null)
            {
                return;
            }

            HoMousePointer.HoEditorPointer pointer = HoMousePointer.EditorPointer;
            if (!pointer.valid)
            {
                return;
            }

            pointer.camera = hovered.camera;
            pointer.timestamp = Time.realtimeSinceStartup;
            HoMousePointer.EditorPointer = pointer;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            Vector2 guiPoint = current.mousePosition;
            bool inside = sceneView.position.Contains(GUIUtility.GUIToScreenPoint(guiPoint));
            HoMousePointer.EditorPointer = new HoMousePointer.HoEditorPointer
            {
                valid = inside,
                camera = inside ? sceneView.camera : null,
                // GUI 坐标（左上原点）→ 屏幕像素（左下原点），ScreenPointToRay 要的是后者
                screenPosition = inside ? HandleUtility.GUIPointToScreenPixelCoordinate(guiPoint) : Vector2.zero,
                // 射线直接算好：GUI → 世界的换算（视口偏移 / DPI / 正交）交给 Unity，最可靠
                ray = inside ? HandleUtility.GUIPointToWorldRay(guiPoint) : default,
                hasRay = inside,
                timestamp = Time.realtimeSinceStartup
            };
        }
    }
}
