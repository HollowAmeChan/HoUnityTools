using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.PlaySpeed
{
    /// <summary>
    /// Session-only playback speed ownership for automation. Pass the requesting
    /// component as owner so the editor can identify it. Manual unlock wins until
    /// the user locks again or the next play session starts.
    /// </summary>
    public static class HoPlaySpeedControl
    {
        public const float MaxSpeed = 100f;

        private static Object owner;
        private static float speed = 1f;
        private static bool lockRequested;
        private static bool manualLock;
        private static bool manuallyUnlocked;
        private static float manuallyLockedSpeed;
        private static bool hasOriginalTimeScale;
        private static float originalTimeScale;
        private static HoPlaySpeedDriver driver;

        public static event Action Changed;
        public static Object Owner => owner;
        public static bool HasOverride => owner != null;
        public static float RequestedSpeed => speed;
        public static bool ManuallyUnlocked => manuallyUnlocked;
        public static bool IsManuallyLocked => manualLock;
        public static bool IsPanelLocked => Application.isPlaying &&
            (manualLock || (HasOverride && lockRequested && !manuallyUnlocked));
        public static string SourceName => owner == null ? string.Empty :
            owner.GetType().Name + "（" + owner.name + "）";

        /// <summary>
        /// Request an explicit speed. The latest owner wins; releasing a previous
        /// owner cannot release the current one. Repeated calls respect manual unlock.
        /// </summary>
        public static void SetOverride(Object source, float value, bool lockPanel = true)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Speed must be finite.");
            if (!Application.isPlaying) return;

            if (!hasOriginalTimeScale)
            {
                originalTimeScale = Time.timeScale;
                hasOriginalTimeScale = true;
            }

            value = Mathf.Clamp(value, 0f, MaxSpeed);
            bool changed = owner != source || speed != value || lockRequested != lockPanel;
            owner = source;
            speed = value;
            lockRequested = lockPanel;
            EnsureDriver();
            if (!manuallyUnlocked && Time.timeScale != speed) Time.timeScale = speed;
            if (changed) Changed?.Invoke();
        }

        /// <summary>Release only this owner's request; no scene or prefab fields are changed.</summary>
        public static void Release(Object source)
        {
            if (!ReferenceEquals(owner, source)) return;
            ClearOverride();
        }

        /// <summary>
        /// Toggle the panel lock. Unlock is a session-wide manual takeover, so an
        /// automation script calling SetOverride every frame cannot immediately relock.
        /// </summary>
        public static void SetPanelLocked(bool locked)
        {
            if (!Application.isPlaying) return;
            manuallyUnlocked = !locked;
            manualLock = locked && !(HasOverride && lockRequested);
            if (manualLock) manuallyLockedSpeed = Time.timeScale;
            Tick();
            Changed?.Invoke();
        }

        /// <summary>Enforce an active request even when the editor panel is hidden.</summary>
        public static void Tick()
        {
            if (!Application.isPlaying) return;
            if (owner == null)
            {
                if (manualLock)
                {
                    if (Time.timeScale != manuallyLockedSpeed) Time.timeScale = manuallyLockedSpeed;
                }
                else if (hasOriginalTimeScale && !manuallyUnlocked)
                {
                    ClearOverride();
                }
                return;
            }
            if (IsPanelLocked && HasOverride && !manuallyUnlocked && Time.timeScale != speed)
                Time.timeScale = speed;
        }

        /// <summary>Clear transient state at play exit. Also used when domain reload is disabled.</summary>
        public static void EndSession()
        {
            if (hasOriginalTimeScale) Time.timeScale = originalTimeScale;
            owner = null;
            speed = 1f;
            lockRequested = false;
            manualLock = false;
            manuallyUnlocked = false;
            manuallyLockedSpeed = 1f;
            hasOriginalTimeScale = false;
            if (driver != null)
            {
                var host = driver.gameObject;
                driver = null;
                if (Application.isPlaying) Object.Destroy(host);
                else Object.DestroyImmediate(host);
            }
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginSession()
        {
            // The editor owns the pre-play baseline. Never restore stale static
            // values here when entering with domain reload disabled.
            hasOriginalTimeScale = false;
            EndSession();
        }

        private static void ClearOverride()
        {
            if (manualLock)
            {
                manuallyLockedSpeed = Time.timeScale;
            }
            else if (hasOriginalTimeScale && !manuallyUnlocked)
            {
                Time.timeScale = originalTimeScale;
            }
            owner = null;
            lockRequested = false;
            if (!manualLock && !manuallyUnlocked) hasOriginalTimeScale = false;
            Changed?.Invoke();
        }

        private static void EnsureDriver()
        {
            if (driver != null) return;
            var host = new GameObject("[HoPlaySpeedControl]") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            driver = host.AddComponent<HoPlaySpeedDriver>();
        }

        internal static void RegisterDriver(HoPlaySpeedDriver instance)
        {
            driver = instance;
        }
    }
}
