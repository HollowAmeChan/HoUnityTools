using System;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.Editor.PlaySpeed;
using Hollow.HoUnityTools.PlaySpeed;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Staged into an isolated project's Assets/Editor by Run.ps1.
[InitializeOnLoad]
public static class PlaySpeedValidation
{
    private const string Prefix = "HoPlaySpeedValidation.";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static PlaySpeedValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        try
        {
            CheckPanelReset();
            HoPlaySpeedSettings.Data.speed = .25f;
            HoPlaySpeedSettings.Data.resetOnPlayEnd = true;
            Time.timeScale = 1;
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Cycle", 0);
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Play speed regression").AddComponent<PlaySpeedProbe>();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/PlaySpeed.unity");
            EditorApplication.isPlaying = true;
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            try { CheckRuntimeControl(); }
            catch (Exception error) { Fail(error); }
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            // Validate after production playModeStateChanged handlers finish.
            EditorApplication.delayCall += CheckExitAndRepeat;
        }
    }

    private static void CheckRuntimeControl()
    {
        Check(HoPlaySpeedSession.IsActive, "Session baseline exists without a panel");
        Near(HoPlaySpeedSession.OriginalSpeed, .25f, "Original desired speed");
        Near(HoPlaySpeedSession.OriginalTimeScale, 1f, "Original actual speed");
        Check(!HoPlaySpeedControl.HasOverride && !HoPlaySpeedControl.ManuallyUnlocked,
            "Fresh play has no stale request or manual takeover");

        var root = new GameObject("Named automation source");
        var source = root.AddComponent<HoPlaySpeedOverride>();
        source.Speed = .5f;
        Check(HoPlaySpeedControl.IsPanelLocked, "Component automatically locks on play enable");
        Check(HoPlaySpeedControl.SourceName.Contains(nameof(HoPlaySpeedOverride)) &&
            HoPlaySpeedControl.SourceName.Contains(root.name), "Source identifies script and object");
        var snapshot = JsonUtility.ToJson(source);
        var window = ScriptableObject.CreateInstance<HoPlaySpeedWindow>();
        var data = HoPlaySpeedSettings.Data;
        var type = typeof(HoPlaySpeedWindow);
        Near(Time.timeScale, .5f, "Opening panel respects automation request");
        data.speed = .125f;
        type.GetMethod("ApplySpeed", PrivateInstance).Invoke(window, new object[] { data });
        Near(Time.timeScale, .5f, "Disabled panel cannot overwrite automation");
        string info = (string)type.GetMethod("ControlInfo", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Check(info.Contains(nameof(HoPlaySpeedOverride)), "Info names requesting script");
        data.speed = .5f;
        HoPlaySpeedSettings.NotifyChanged();
        var saved = JsonUtility.FromJson<HoPlaySpeedSettingsData>(EditorPrefs.GetString(HoPlaySpeedSettings.PrefsKey));
        Near(saved.speed, .25f, "Saving preferences in play preserves original desired speed");
        UnityEngine.Object.DestroyImmediate(window);
        Near(Time.timeScale, .5f, "Closing panel does not stop automation");

        HoPlaySpeedControl.SetPanelLocked(false);
        Check(!HoPlaySpeedControl.IsPanelLocked && HoPlaySpeedControl.ManuallyUnlocked, "Manual unlock takes control");
        Time.timeScale = .125f;
        for (int i = 0; i < 3; i++)
        {
            source.Apply();
            HoPlaySpeedControl.Tick();
        }
        Near(Time.timeScale, .125f, "Repeated automation cannot reclaim manual control");
        Check(snapshot == JsonUtility.ToJson(source) && source.LockPanel,
            "Manual unlock does not change serialized scene configuration");
        info = (string)type.GetMethod("ControlInfo", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Check(info.Contains("手动解锁") && info.Contains(nameof(HoPlaySpeedOverride)), "Info records takeover and source");

        var other = new GameObject("Another automation");
        HoPlaySpeedControl.SetOverride(other, 2f);
        HoPlaySpeedControl.Release(source);
        Check(HoPlaySpeedControl.Owner == other, "Stale owner cannot release a newer request");
        Near(Time.timeScale, .125f, "New automation also respects manual takeover");
        Time.timeScale = .125f;
        HoPlaySpeedControl.SetPanelLocked(true);
        Near(Time.timeScale, 2f, "Relock explicitly resumes requested speed");
        Time.timeScale = .75f;
        HoPlaySpeedControl.Tick();
        Near(Time.timeScale, 2f, "Locked request works with panel closed");
        HoPlaySpeedControl.Release(other);
        Near(Time.timeScale, 1f, "Releasing automation restores the original speed");
        Check(!HoPlaySpeedControl.HasOverride && !HoPlaySpeedControl.IsPanelLocked, "Release clears source");
        UnityEngine.Object.DestroyImmediate(other);

        HoPlaySpeedControl.SetPanelLocked(false);
        Time.timeScale = .125f;
        HoPlaySpeedControl.SetPanelLocked(true);
        Time.timeScale = .625f;
        HoPlaySpeedControl.Tick();
        Near(Time.timeScale, .125f, "Manual lock without automation freezes current speed");
        HoPlaySpeedControl.SetPanelLocked(false);
        HoPlaySpeedControl.SetOverride(source, .5f);
        Near(Time.timeScale, .125f, "Manual takeover survives release and a new source");

        // End the session with a live request and manual takeover, so play exit
        // tests clear both when domain reload is enabled AND disabled.
        source.Apply();
        HoPlaySpeedControl.SetPanelLocked(false);
        data.speed = .375f;
        // Start may already have run when EnteredPlayMode is delivered. Leave
        // the following frame at the first BGM test's requested speed.
        Time.timeScale = 1f;
        Debug.Log("PASS playback ownership, info, lock, manual takeover, preferences and panel lifecycle");
    }

    private static void CheckExitAndRepeat()
    {
        try
        {
            Check(!HoPlaySpeedSession.IsActive, "Play exit clears editor session");
            Check(!HoPlaySpeedControl.HasOverride && !HoPlaySpeedControl.IsPanelLocked &&
                !HoPlaySpeedControl.ManuallyUnlocked, "Play exit clears all transient lock state");
            Near(Time.timeScale, 1f, "Play exit restores original actual speed");
            Near(HoPlaySpeedSettings.Data.speed, .25f, "Auto reset restores original desired speed");
            var saved = JsonUtility.FromJson<HoPlaySpeedSettingsData>(EditorPrefs.GetString(HoPlaySpeedSettings.PrefsKey));
            Near(saved.speed, .25f, "Persisted speed matches pre-play value");
            Check(!EditorSceneManager.GetActiveScene().isDirty, "Debug changes do not dirty the edit scene");
            File.AppendAllText("playspeed-results.txt", "\nPASS playback ownership and play exit, cycle " +
                SessionState.GetInt(Prefix + "Cycle", 0));
            if (SessionState.GetInt(Prefix + "Cycle", 0) == 0)
            {
                SessionState.SetInt(Prefix + "Cycle", 1);
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.isPlaying = true;
            }
            else
            {
                SessionState.SetBool(Prefix + "Active", false);
                File.AppendAllText("playspeed-results.txt", "\nPASS both domain reload modes and all lock regressions.");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }

    private static void Near(float actual, float expected, string label)
    {
        Check(Mathf.Abs(actual - expected) < .00001f, label + ": expected " + expected + ", actual " + actual);
    }

    private static void Fail(Exception error)
    {
        SessionState.SetBool(Prefix + "Active", false);
        File.AppendAllText("playspeed-results.txt", "\nFAIL " + error);
        Debug.LogException(error);
        EditorApplication.Exit(1);
    }

    private static void CheckPanelReset()
    {
        var window = ScriptableObject.CreateInstance<HoPlaySpeedWindow>();
        float previousScale = Time.timeScale;
        try
        {
            var type = typeof(HoPlaySpeedWindow);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("armed", flags).SetValue(window, true);
            type.GetField("originalTimeScale", flags).SetValue(window, 1f);
            type.GetField("originalSpeed", flags).SetValue(window, .25f);
            var data = HoPlaySpeedSettings.Data;
            data.speed = .5f;
            type.GetMethod("RestoreOriginal", flags).Invoke(window, new object[] { data, true });
            type.GetMethod("ApplySpeed", flags).Invoke(window, new object[] { data });
            if (Mathf.Abs(Time.timeScale - 1) > .00001f || Mathf.Abs(data.speed - 1) > .00001f)
                throw new Exception("Reset was overwritten by the pre-play desired speed.");

            type.GetMethod("ExitPlay", flags).Invoke(window, null);
            if (Mathf.Abs(data.speed - .25f) > .00001f)
                throw new Exception("Play exit did not restore the pre-play desired speed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
            Time.timeScale = previousScale;
        }
    }
}
