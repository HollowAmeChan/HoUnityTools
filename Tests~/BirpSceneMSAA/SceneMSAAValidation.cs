using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
#if !UNITY_6000_0_OR_NEWER
using Hollow.HoUnityTools.Editor.BirpSceneMSAA;
#endif
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

// Run.ps1 stages this and the production script in an isolated BIRP project.
#if UNITY_6000_0_OR_NEWER
public static class SceneMSAAValidation
{
    private const string Prefix = "HoBirpSceneMSAAValidation.";
    private const string OverlayId = "hollow.hounitytools.BirpSceneMSAA";
    private static SceneView firstView;
    private static SceneView secondView;

    [InitializeOnLoadMethod]
    private static void OnReload()
    {
        if (SessionState.GetBool(Prefix + "ReloadPending", false))
            EditorApplication.delayCall += CheckAfterReload;
    }

    public static void Run()
    {
        SessionState.SetString(Prefix + "Report", "Unity: " + Application.unityVersion + "\n");
        Guard(() =>
        {
            firstView = ScriptableObject.CreateInstance<SceneView>();
            firstView.name = Prefix + "First";
            firstView.Show();
            secondView = ScriptableObject.CreateInstance<SceneView>();
            secondView.name = Prefix + "Second";
            secondView.Show();
            EditorApplication.delayCall += CheckInitialLayout;
        });
    }

    private static void CheckInitialLayout()
    {
        Guard(() =>
        {
            CheckNoFeature(firstView);
            CheckNoFeature(secondView);
            SessionState.SetBool(Prefix + "ReloadPending", true);
            EditorUtility.RequestScriptReload();
        });
    }

    private static void CheckAfterReload()
    {
        Guard(() =>
        {
            SessionState.SetBool(Prefix + "ReloadPending", false);
            var views = Resources.FindObjectsOfTypeAll<SceneView>();
            CheckNoFeature(views.Single(view => view.name == Prefix + "First"));
            CheckNoFeature(views.Single(view => view.name == Prefix + "Second"));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "scene-msaa-results.txt"),
                SessionState.GetString(Prefix + "Report", "") + "PASS\n");
            EditorApplication.Exit(0);
        });
    }

    private static void CheckNoFeature(SceneView view)
    {
        if (view.TryGetOverlay(OverlayId, out _))
            throw new Exception("Unity 6 must not register the BIRP Scene MSAA overlay.");
        if (!view.TryGetOverlay("unity-scene-view-toolbar", out var builtin) || !builtin.displayed)
            throw new Exception("Built-in SceneView controls must remain visible.");

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetType("Hollow.HoUnityTools.Editor.BirpSceneMSAA.HoBirpSceneMSAA") != null ||
                assembly.GetType("Hollow.HoUnityTools.Editor.BirpSceneMSAA.HoBirpSceneMSAAMenu") != null ||
                assembly.GetType("Hollow.HoUnityTools.Editor.BirpSceneMSAA.HoBirpSceneMSAAButton") != null)
                throw new Exception("Unity 6 must exclude the MSAA callback, menu and toolbar button.");
        SessionState.SetString(Prefix + "Report", SessionState.GetString(Prefix + "Report", "") +
            "PASS: Unity 6 has no MSAA overlay, button, menu or render callback; built-in toolbar is visible\n");
    }

    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            SessionState.SetBool(Prefix + "ReloadPending", false);
            Debug.LogException(exception);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "scene-msaa-results.txt"),
                SessionState.GetString(Prefix + "Report", "") + "FAIL: " + exception + "\n");
            EditorApplication.Exit(1);
        }
    }
}
#else
public static class SceneMSAAValidation
{
    private const string Prefix = "HoBirpSceneMSAAValidation.";
    private const string FirstViewName = Prefix + "First";
    private const string SecondViewName = Prefix + "Second";
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static SceneView firstView;
    private static SceneView secondView;

    [InitializeOnLoadMethod]
    private static void OnReload()
    {
        if (SessionState.GetBool(Prefix + "ReloadPending", false))
            EditorApplication.delayCall += CheckAfterReload;
    }

    public static void Run()
    {
        SessionState.SetString(Prefix + "Report", "Unity: " + Application.unityVersion + "\n");
        SessionState.SetInt(Prefix + "QualityAA", QualitySettings.antiAliasing);
        Guard(() =>
        {
            Check(HoBirpSceneMSAA.Supported, "Isolated project uses BIRP");
            firstView = CreateView(FirstViewName);
            secondView = CreateView(SecondViewName);
            EditorApplication.delayCall += CheckInitialLayout;
        });
    }

    private static SceneView CreateView(string viewName)
    {
        var view = ScriptableObject.CreateInstance<SceneView>();
        view.name = viewName;
        view.position = new Rect(100, 100, 1200, 800);
        view.Show();
        return view;
    }

    private static Overlay GetOverlay(SceneView view)
    {
        if (view == null || !view.TryGetOverlay(HoBirpSceneMSAAOverlay.Id, out var overlay))
            throw new Exception("SceneView did not register the MSAA overlay.");
        return overlay;
    }

    private static object GetCanvas(Overlay overlay) =>
        typeof(Overlay).GetProperty("canvas", InstanceMembers).GetValue(overlay);

    private static string GetContainer(Overlay overlay) =>
        ((VisualElement)typeof(Overlay).GetProperty("container", InstanceMembers).GetValue(overlay)).name;

    private static void CheckToolbar(Overlay overlay, string label)
    {
        Check(overlay.displayed && !overlay.floating && overlay.layout == Layout.HorizontalToolbar &&
              GetContainer(overlay) == "overlay-toolbar__top", label + ": visible in top horizontal toolbar");
        var root = (VisualElement)typeof(Overlay).GetProperty("rootVisualElement", InstanceMembers).GetValue(overlay);
        var buttons = root.Query<HoBirpSceneMSAAButton>().ToList();
        Check(buttons.Count == 1 && buttons[0].enabledSelf, label + ": one usable MSAA button");
    }

    private static void CheckInitialLayout()
    {
        Guard(() =>
        {
            var first = GetOverlay(firstView);
            var second = GetOverlay(secondView);
            CheckToolbar(first, "First SceneView");
            CheckToolbar(second, "Second SceneView");
            foreach (var view in new[] { firstView, secondView })
                Check(view.TryGetOverlay("unity-scene-view-toolbar", out var builtin) && builtin.displayed,
                    "Built-in SceneView controls remain visible");

            HoBirpSceneMSAA.Enabled = true;
            foreach (var overlay in new[] { first, second })
            {
                var root = (VisualElement)typeof(Overlay).GetProperty("rootVisualElement", InstanceMembers).GetValue(overlay);
                Check(root.Q<HoBirpSceneMSAAButton>().value, "Menu/toggle state synchronizes between SceneViews");
            }
            HoBirpSceneMSAA.Enabled = false;
            CheckQualitySettings();

#if !UNITY_2023_1_OR_NEWER
            // Reproduce the exact saved defaults emitted by the old implementation.
            var canvas = GetCanvas(first);
            var savedLayouts = (IList)canvas.GetType().GetField("m_SaveData", InstanceMembers).GetValue(canvas);
            for (var index = savedLayouts.Count - 1; index >= 0; index--)
            {
                var saved = savedLayouts[index];
                var savedId = (string)saved.GetType().GetField("id").GetValue(saved);
                if (savedId == HoBirpSceneMSAAOverlay.Id ||
                    savedId == HoBirpSceneMSAAOverlay.Id + ".placement-v1")
                    savedLayouts.RemoveAt(index);
            }
#if UNITY_2022_1_OR_NEWER
            var legacy = MakeSaveData(canvas, "overlay-container--right", Layout.Panel, true);
#else
            var legacy = MakeSaveData(canvas, "overlay-toolbar__top", Layout.Panel, false);
#endif
            savedLayouts.Add(legacy);
            canvas.GetType().GetMethod("RestoreOverlays", InstanceMembers).Invoke(canvas, null);
            CheckToolbar(first, "Migrated saved legacy layout");
#endif

            // A user can hide the toolbar and later choose a panel or floating layout.
            // None of those choices may be undone by another restore or a domain reload.
            first.displayed = false;
            Rebuild(first);
            Check(!first.displayed, "Hidden toolbar stays hidden after saving and restoring");
            RestoreLayout(first, "overlay-container--right", Layout.Panel, false);
            Rebuild(first);
            Check(!first.displayed && first.layout == Layout.Panel &&
                  GetContainer(first) == "overlay-container--right", "User's hidden panel layout is retained");
            second.Undock();
            Rebuild(second);
            Check(second.floating && second.displayed, "User's floating layout is retained");

            SessionState.SetBool(Prefix + "ReloadPending", true);
            EditorUtility.RequestScriptReload();
        });
    }

    private static object MakeSaveData(object canvas, string container, Layout layout, bool displayed)
    {
        var type = canvas.GetType().GetMethod("RestoreOverlay", InstanceMembers).GetParameters()[1].ParameterType;
        var data = Activator.CreateInstance(type);
        type.GetField("id", InstanceMembers).SetValue(data, HoBirpSceneMSAAOverlay.Id);
        type.GetField("containerId", InstanceMembers).SetValue(data, container);
        type.GetField("layout", InstanceMembers).SetValue(data, layout);
        type.GetField("displayed", InstanceMembers).SetValue(data, displayed);
        return data;
    }

    private static void RestoreLayout(Overlay overlay, string container, Layout layout, bool displayed)
    {
        var canvas = GetCanvas(overlay);
        var data = MakeSaveData(canvas, container, layout, displayed);
        canvas.GetType().GetMethod("RestoreOverlay", InstanceMembers).Invoke(canvas, new[] { (object)overlay, data });
    }

    private static void Rebuild(Overlay overlay)
    {
        var canvas = GetCanvas(overlay);
        ((ISerializationCallbackReceiver)canvas).OnBeforeSerialize();
        canvas.GetType().GetMethod("RestoreOverlays", InstanceMembers).Invoke(canvas, null);
    }

    private static void CheckAfterReload()
    {
        Guard(() =>
        {
            SessionState.SetBool(Prefix + "ReloadPending", false);
            var views = Resources.FindObjectsOfTypeAll<SceneView>();
            var first = GetOverlay(views.Single(view => view.name == FirstViewName));
            var second = GetOverlay(views.Single(view => view.name == SecondViewName));
            Check(!first.displayed && first.layout == Layout.Panel &&
                  GetContainer(first) == "overlay-container--right", "Domain reload preserves hidden panel layout");
            Check(second.floating && second.displayed, "Domain reload preserves floating toolbar");
            CheckQualitySettings();
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "scene-msaa-results.txt"),
                SessionState.GetString(Prefix + "Report", "") + "PASS\n");
            DeleteValidationPreference();
            EditorApplication.Exit(0);
        });
    }

    private static void CheckQualitySettings() =>
        Check(QualitySettings.antiAliasing == SessionState.GetInt(Prefix + "QualityAA", -1),
            "Project anti-aliasing setting is unchanged");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        SessionState.SetString(Prefix + "Report", SessionState.GetString(Prefix + "Report", "") + "PASS: " + message + "\n");
    }

    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            SessionState.SetBool(Prefix + "ReloadPending", false);
            Debug.LogException(exception);
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "scene-msaa-results.txt"),
                SessionState.GetString(Prefix + "Report", "") + "FAIL: " + exception + "\n");
            DeleteValidationPreference();
            EditorApplication.Exit(1);
        }
    }

    private static void DeleteValidationPreference()
    {
        var key = (string)typeof(HoBirpSceneMSAA).GetField("PrefKey",
            BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        if (key.Contains(".validation.")) EditorPrefs.DeleteKey(key);
    }
}
#endif
