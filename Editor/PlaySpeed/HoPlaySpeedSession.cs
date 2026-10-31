using Hollow.HoUnityTools.PlaySpeed;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>
    /// Editor-side play-session baseline, independent of an open window. SessionState
    /// survives script reloads but is never written to scenes, prefabs or EditorPrefs.
    /// </summary>
    [InitializeOnLoad]
    internal static class HoPlaySpeedSession
    {
        private const string Prefix = "HoUnityTools.PlaySpeed.Session.";
        public static bool IsActive => SessionState.GetBool(Prefix + "Active", false);
        public static float OriginalSpeed => SessionState.GetFloat(Prefix + "Speed", 1f);
        public static float OriginalTimeScale => SessionState.GetFloat(Prefix + "TimeScale", 1f);

        static HoPlaySpeedSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
            // Restore manual takeover before runtime OnEnable requests after a
            // script reload. A fresh play explicitly clears this session flag.
            if (IsActive && EditorApplication.isPlaying &&
                SessionState.GetBool(Prefix + "Manual", false))
                HoPlaySpeedControl.SetPanelLocked(false);
            HoPlaySpeedControl.Changed += OnControlChanged;
        }

        internal static void BeginPlay()
        {
            if (IsActive) return;
            SessionState.SetFloat(Prefix + "Speed", HoPlaySpeedSettings.Data.speed);
            SessionState.SetFloat(Prefix + "TimeScale", Time.timeScale);
            SessionState.SetBool(Prefix + "Manual", false);
            SessionState.SetBool(Prefix + "Active", true);
        }

        private static void OnControlChanged()
        {
            if (IsActive)
                SessionState.SetBool(Prefix + "Manual", HoPlaySpeedControl.ManuallyUnlocked);
            foreach (var window in Resources.FindObjectsOfTypeAll<HoPlaySpeedWindow>())
                window.Repaint();
        }

        private static void OnUpdate()
        {
            if (!IsActive || !EditorApplication.isPlaying) return;
            HoPlaySpeedControl.Tick();
            if (HoPlaySpeedControl.IsPanelLocked)
                HoPlaySpeedSettings.Data.speed = Time.timeScale;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                BeginPlay();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode ||
                     change == PlayModeStateChange.EnteredEditMode)
            {
                EndPlay();
            }
        }

        internal static void EndPlay()
        {
            if (!IsActive) return;
            float originalScale = OriginalTimeScale;
            float originalSpeed = OriginalSpeed;
            HoPlaySpeedControl.EndSession();
            Time.timeScale = originalScale;
            if (HoPlaySpeedSettings.Data.resetOnPlayEnd)
                HoPlaySpeedSettings.Data.speed = originalSpeed;
            SessionState.SetBool(Prefix + "Active", false);
            SessionState.SetBool(Prefix + "Manual", false);
            HoPlaySpeedSettings.Save();
        }
    }
}
