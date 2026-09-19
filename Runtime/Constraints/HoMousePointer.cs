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

        /// <summary>采样鼠标：角度映射模式只需要屏幕位置；世界点/射线模式再算世界点。</summary>
        public static HoPointerSample Sample(in HoMouseSettings settings)
        {
            HoPointerSample sample = default;
            if (!TryReadPointer(settings.inputSource, out Vector2 screenPosition))
            {
                return sample;
            }

            sample.valid = true;
            sample.screenPosition = screenPosition;

            Camera camera = settings.camera != null ? settings.camera : Camera.main;
            if (settings.sampleMode != HoLookAtMouseSampleMode.AngleMap && camera != null)
            {
                Ray ray = camera.ScreenPointToRay(screenPosition);
                switch (settings.sampleMode)
                {
                    case HoLookAtMouseSampleMode.Raycast:
                    {
                        if (Physics.Raycast(ray, out RaycastHit hit, 1000.0f, settings.raycastMask))
                        {
                            sample.hasWorldPoint = true;
                            sample.worldPoint = hit.point;
                        }

                        break;
                    }

                    default:
                    {
                        if (settings.projectToPlane)
                        {
                            Plane plane = new Plane(Vector3.up, new Vector3(0.0f, settings.planeHeight, 0.0f));
                            if (plane.Raycast(ray, out float enter))
                            {
                                sample.hasWorldPoint = true;
                                sample.worldPoint = ray.GetPoint(enter);
                            }
                        }
                        else
                        {
                            sample.hasWorldPoint = true;
                            sample.worldPoint = ray.GetPoint(settings.distance);
                        }

                        break;
                    }
                }
            }

            return sample;
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
