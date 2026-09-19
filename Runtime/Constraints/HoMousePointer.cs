using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 鼠标/指针采样。项目里只启用 Input System 包时（activeInputHandler = 1）旧的
    /// UnityEngine.Input 会直接抛异常，所以默认走 Input System，并保留旧 Input 分支给
    /// Warudo 之类宿主；两者都不可用时返回 invalid，组件会保持上一次的目标。
    /// </summary>
    public static class HoMousePointer
    {
        /// <summary>
        /// 编辑器专用：Scene 视图里的鼠标位置、相机与**射线**，由编辑器程序集每帧写入
        /// （运行时程序集不能引用 UnityEditor，所以走这个静态口子；构建里恒为 invalid）。
        /// 射线用 HandleUtility.GUIPointToWorldRay 算好再传进来 —— 它把 Scene 视图的
        /// 视口偏移、DPI 缩放、正交/透视都处理掉了，比"自己拿 ScreenPointToRay 反算"可靠。
        /// </summary>
        public struct HoEditorPointer
        {
            public bool valid;
            public Camera camera;
            public Vector2 screenPosition;
            public Ray ray;
            public bool hasRay;
            public float timestamp;
        }

        /// <summary>Scene 视图鼠标（编辑器程序集写入；见 <see cref="HoEditorPointer"/>）。</summary>
        public static HoEditorPointer EditorPointer;

        /// <summary>Scene 视图鼠标的有效期（秒）：超过就当没有，回落到真实输入。</summary>
        private const float EditorPointerLifetime = 0.5f;
        /// <summary>
        /// 编辑器辅助：替用户在场景里挑一个"看起来是观众视角"的相机（渲染到屏幕、启用的、像素面积最大）。
        /// **只在面板按钮/一键装配里调用** —— 运行时不再自动猜相机，一律用组件上手动指定的那个。
        /// </summary>
        public static Camera FindSceneCamera(Camera preferred)
        {
            if (preferred != null)
            {
                return preferred;
            }

            Camera main = Camera.main;
            if (main != null)
            {
                return main;
            }

#if UNITY_2023_1_OR_NEWER
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            Camera[] cameras = Object.FindObjectsOfType<Camera>();
#endif
            Camera best = null;
            float bestArea = -1.0f;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // 只考虑渲染到屏幕的（targetTexture 非空的是 RT / 反射相机）
                if (camera.targetTexture != null)
                {
                    continue;
                }

                Rect pixelRect = camera.pixelRect;
                float area = pixelRect.width * pixelRect.height;
                if (area > bestArea)
                {
                    best = camera;
                    bestArea = area;
                }
            }

            return best;
        }

        public static bool TryReadPointer(HoLookAtInputSource source, out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            bool useInputSystem = source == HoLookAtInputSource.InputSystem;
            bool useLegacy = source == HoLookAtInputSource.LegacyInput;
            if (source == HoLookAtInputSource.Auto)
            {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                useInputSystem = true;
#elif ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
                useLegacy = true;
#else
                useInputSystem = true;
                useLegacy = true;
#endif
            }

#if ENABLE_INPUT_SYSTEM
            if (useInputSystem)
            {
                Pointer pointer = Pointer.current;
                if (pointer != null)
                {
                    screenPosition = pointer.position.ReadValue();
                    return true;
                }

                if (!useLegacy)
                {
                    return false;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (useLegacy)
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            return false;
        }

        /// <summary>
        /// 采样鼠标：角度摇杆模式只需要屏幕位置；准星/射线模式再算世界点。
        /// 准星模式取的是"鼠标射线上离角色支点最近的那个点"（也就是角色所在深度的那一点），
        /// 于是从支点看向它就正好落在鼠标指的位置上 —— 这是"精确指向鼠标"的关键。
        /// </summary>
        public static HoPointerSample Sample(in HoMouseSettings settings)
        {
            HoPointerSample sample = default;
            HoMouseSettings resolved = settings;

            if (!TryResolvePointer(ref resolved, out Vector2 screenPosition, out Ray pointerRay, out bool hasPointerRay))
            {
                return sample;
            }

            sample.valid = true;
            sample.camera = resolved.camera;
            sample.screenPosition = screenPosition;

            if (resolved.sampleMode == HoLookAtMouseSampleMode.AngleMap)
            {
                return sample;
            }

            // 鼠标位置 → 世界：用相机射线（投影矩阵的逆；Scene 视图那边由编辑器直接给射线）
            Ray ray;
            if (hasPointerRay)
            {
                ray = pointerRay;
            }
            else if (resolved.camera != null)
            {
                ray = resolved.camera.ScreenPointToRay(screenPosition);
            }
            else
            {
                return sample;
            }

            sample.ray = ray;
            sample.hasRay = true;

            if (resolved.sampleMode == HoLookAtMouseSampleMode.CursorPoint)
            {
                // 准星：取射线上**离眼睛最近**的那个点（正好在角色所在的深度上）。
                // 从两眼中点看向它，视线就会穿过鼠标所在的那个像素 —— 这就是"鼠标指哪看哪"的定义。
                float depth = Vector3.Dot(resolved.pivot - ray.origin, ray.direction);
                depth = Mathf.Max(Mathf.Max(0.1f, resolved.distance * 0.1f), depth);
                sample.hasWorldPoint = true;
                sample.worldPoint = ray.GetPoint(depth);
                return sample;
            }

            sample.hasWorldPoint = true;
            sample.worldPoint = Physics.Raycast(ray, out RaycastHit hit, 1000.0f, resolved.raycastMask)
                ? hit.point
                : ray.GetPoint(Mathf.Max(0.1f, resolved.distance));
            return sample;
        }

        /// <summary>
        /// 取这一次要用哪个指针：编辑器里鼠标在 Scene 视图上时优先用它（连相机与射线一起换掉），
        /// 否则用真实的鼠标/指针输入。相机会写回 <paramref name="settings"/>。
        /// </summary>
        private static bool TryResolvePointer(
            ref HoMouseSettings settings,
            out Vector2 screenPosition,
            out Ray ray,
            out bool hasRay)
        {
            ray = default;
            hasRay = false;

#if UNITY_EDITOR
            if (settings.useSceneViewMouse
                && EditorPointer.valid
                && Time.realtimeSinceStartup - EditorPointer.timestamp < EditorPointerLifetime)
            {
                settings.camera = EditorPointer.camera;
                screenPosition = EditorPointer.screenPosition;
                ray = EditorPointer.ray;
                hasRay = EditorPointer.hasRay;
                return true;
            }
#endif

            return TryReadPointer(settings.inputSource, out screenPosition);
        }

        /// <summary>把屏幕位置换算成归一化的角度偏移（-1..1），带中心死区。</summary>
        public static Vector2 ScreenToAngleOffset(Camera camera, Vector2 screenPosition, float deadZone)
        {
            float width = Screen.width;
            float height = Screen.height;
            if (camera != null)
            {
                Rect pixelRect = camera.pixelRect;
                width = Mathf.Max(1.0f, pixelRect.width);
                height = Mathf.Max(1.0f, pixelRect.height);
                screenPosition -= pixelRect.position;
            }

            float normalizedX = Mathf.Clamp(screenPosition.x / width * 2.0f - 1.0f, -1.0f, 1.0f);
            float normalizedY = Mathf.Clamp(screenPosition.y / height * 2.0f - 1.0f, -1.0f, 1.0f);

            float dead = Mathf.Clamp(deadZone, 0.0f, 0.45f);
            return new Vector2(ApplyDeadZone(normalizedX, dead), ApplyDeadZone(normalizedY, dead));
        }

        private static float ApplyDeadZone(float value, float deadZone)
        {
            if (deadZone <= 0.0f)
            {
                return value;
            }

            float magnitude = Mathf.Abs(value);
            if (magnitude <= deadZone)
            {
                return 0.0f;
            }

            return Mathf.Sign(value) * (magnitude - deadZone) / (1.0f - deadZone);
        }
    }
}
