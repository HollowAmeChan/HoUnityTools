// Run in a disposable Unity project: HoFaceTraceLayoutValidation.RunBatch.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class HoFaceTraceLayoutHost : EditorWindow
{
    public HoFaceTrackingWindow target;
    public int draws;
    private IMGUIContainer container;
    public void RenderEvent(EventType type)
    {
        if(container==null)
        {
            container=new IMGUIContainer(DrawTrace);
            rootVisualElement.Add(container);
        }
        // Headless EditorWindow.SendEvent skips repaint. Enter the real IMGUI container explicitly.
        var method=typeof(IMGUIContainer).GetMethod("DoOnGUI",BindingFlags.Instance|BindingFlags.NonPublic,
            null,new[]{typeof(Event),typeof(Matrix4x4),typeof(Rect),typeof(bool),typeof(Rect),typeof(Action),typeof(bool)},null);
        if(method==null) throw new Exception("Unity IMGUI test entry point unavailable");
        method.Invoke(container,new object[]{new Event {type=type},Matrix4x4.identity,new Rect(0,0,700,300),false,new Rect(0,0,700,300),(Action)DrawTrace,true});
    }
    private void DrawTrace()
    {
        if (target == null) return;
        using (new EditorGUILayout.ScrollViewScope(Vector2.zero))
        {
            typeof(HoFaceTrackingWindow).GetMethod("DrawTraceSection",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
            draws++;
        }
    }
}

[InitializeOnLoad]
public static class HoFaceTraceLayoutValidation
{
    private const string Key="Ho.Trace.LayoutTest";
    private const string Errors="Ho.Trace.LayoutErrors";
    private static double deadline;
    static HoFaceTraceLayoutValidation()
    {
        if(SessionState.GetInt(Key,0)!=0) Install();
    }
    private static void Install()
    {
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        deadline=EditorApplication.timeSinceStartup+45;
    }
    private static void OnLog(string text,string stack,LogType type)
    {
        if(text.Contains("LayoutGroup") || text.Contains("GUIClips") || text.Contains("Getting control") || text.Contains("GUILayout: Mismatched"))
            SessionState.SetInt(Errors,SessionState.GetInt(Errors,0)+1);
    }
    public static void RunBatch()
    {
        if(!File.Exists(".ho-face-validation")) throw new Exception("Disposable project marker required");
        SessionState.SetInt(Errors,0);
        SessionState.SetInt(Key,1);
        var host=ScriptableObject.CreateInstance<HoFaceTraceLayoutHost>();
        host.target=ScriptableObject.CreateInstance<HoFaceTrackingWindow>();
        host.position=new Rect(0,0,700,300);
        host.ShowUtility();
        Install();
    }
    private static void Update()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Layout validation timeout");
            var hosts=Resources.FindObjectsOfTypeAll<HoFaceTraceLayoutHost>();
            if(hosts.Length!=1) throw new Exception("Expected one trace layout host");
            var host=hosts[0];
            int phase=SessionState.GetInt(Key,0);
            if(phase==1 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Probe(host);
                SessionState.SetInt(Key,2);
                EditorApplication.isPlaying=true;
            }
            else if(phase==2 && EditorApplication.isPlaying)
            {
                Probe(host);
                SessionState.SetInt(Key,3);
                EditorApplication.isPlaying=false;
            }
            else if(phase==3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Probe(host);
                if(SessionState.GetInt(Errors,0)!=0) throw new Exception("Layout errors: "+SessionState.GetInt(Errors,0));
                SessionState.SetInt(Key,0);
                EditorApplication.update-=Update;
                var target=host.target;
                host.Close();
                UnityEngine.Object.DestroyImmediate(target);
                File.WriteAllText("face-trace-layout-validation.txt","PASS: null/completed recorder transitions between Layout/Repaint, real enter/exit Play Mode; no layout errors.");
                Debug.Log("HO_TRACE_LAYOUT_PASS");
                EditorApplication.Exit(0);
            }
        }
        catch(Exception e)
        {
            SessionState.SetInt(Key,0);
            EditorApplication.update-=Update;
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
    private static void Probe(HoFaceTraceLayoutHost host)
    {
        if(host.target==null) throw new Exception("Trace window lost after reload");
        var field=typeof(HoFaceTrackingWindow).GetField("traceRecorder",BindingFlags.Instance|BindingFlags.NonPublic);
        // A completed recorder has no active writer or subscriptions. Only its saved path/count are read by the UI.
        var completed=(HoFaceTraceRecorder)FormatterServices.GetUninitializedObject(typeof(HoFaceTraceRecorder));
        typeof(HoFaceTraceRecorder).GetField("<FilePath>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(completed,Path.GetFullPath(".ho-face-validation"));
        field.SetValue(host.target,null);
        int before=host.draws;
        host.RenderEvent(EventType.Layout);
        field.SetValue(host.target,completed);
        host.RenderEvent(EventType.Repaint);
        host.RenderEvent(EventType.Layout);
        field.SetValue(host.target,null);
        host.RenderEvent(EventType.Repaint);
        if(host.draws-before<4) throw new Exception("Probe did not execute four real GUI events");
    }
}
