// Copy into a disposable Unity 6 project's Assets/Editor; run HoFaceProfileSplitterValidation.RunBatch.
using System;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class HoFaceProfileSplitterTestHost : EditorWindow
{
    public HoFaceProfileWindow target;
    private IMGUIContainer container;
    private Event input;
    public void Render(Event evt)
    {
        input=new Event(evt);
        if(container==null){container=new IMGUIContainer(Draw);rootVisualElement.Add(container);}
        foreach(var field in typeof(IMGUIContainer).GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
        {
            var cache=field.GetValue(container);
            if(cache!=null && cache.GetType().Name=="LayoutCache")
                cache.GetType().GetMethod("ResetCursor",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(cache,null);
        }
        var method=typeof(IMGUIContainer).GetMethod("DoOnGUI",BindingFlags.Instance|BindingFlags.NonPublic,null,
            new[]{typeof(Event),typeof(Matrix4x4),typeof(Rect),typeof(bool),typeof(Rect),typeof(Action),typeof(bool)},null);
        if(method==null)throw new Exception("Unity IMGUI test entry point unavailable");
        method.Invoke(container,new object[]{evt,Matrix4x4.identity,new Rect(0,0,900,700),false,new Rect(0,0,900,700),(Action)Draw,true});
    }
    private void Draw()
    {
        // Headless containers have no native pointer position; inject the test pointer in their GUI context.
        if(Event.current.isMouse)
        {
            Event.current.mousePosition=input.mousePosition;Event.current.delta=input.delta;
            Event.current.button=input.button;Event.current.clickCount=input.clickCount;
        }
        typeof(HoFaceProfileWindow).GetMethod("DrawColumnSplit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    }
}

public static class HoFaceProfileSplitterValidation
{
    private const string Pref="HoUnityTools.FaceProfile.LeftWidth";
    private static FieldInfo Field(string name)=>typeof(HoFaceProfileWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Paint(HoFaceProfileSplitterTestHost h)
    {h.Render(new Event{type=EventType.Layout});h.Render(new Event{type=EventType.Repaint});}
    private static Rect Divider(HoFaceProfileSplitterTestHost h)=>(Rect)Field("splitterRect").GetValue(h.target);
    private static float Width(HoFaceProfileSplitterTestHost h)=>(float)Field("leftWidth").GetValue(h.target);
    private static void Pointer(HoFaceProfileSplitterTestHost h,EventType type,Vector2 point,int clicks=1,int button=0)
    {h.Render(new Event{type=type,mousePosition=point,button=button,clickCount=clicks});}
    public static void RunBatch()
    {
        bool had=EditorPrefs.HasKey(Pref);float saved=EditorPrefs.GetFloat(Pref);
        HoFaceProfileSplitterTestHost host=null;
        int exit=1;
        try
        {
            Check(File.Exists(".ho-face-validation"),"Disposable project marker required");
            host=ScriptableObject.CreateInstance<HoFaceProfileSplitterTestHost>();
            host.target=ScriptableObject.CreateInstance<HoFaceProfileWindow>();
            host.target.position=new Rect(0,0,900,700);
            var profile=new HoFaceMiddleware();
            for(int i=0;i<80;i++)profile.outputs.Add(new HoFaceOutput{parameter="Some/Long/Parameter/"+i,expression="jawOpen"});
            string original=HoFaceProfileJson.Write(profile);
            Field("middleware").SetValue(host.target,profile);Field("selected").SetValue(host.target,0);
            Field("leftWidth").SetValue(host.target,300f);
            host.position=new Rect(0,0,900,700);host.ShowUtility();Paint(host);
            float oldX=Divider(host).x;
            var start=Divider(host).center;
            Pointer(host,EventType.MouseDown,start);
            Check((bool)Field("draggingSplitter").GetValue(host.target),"Divider must capture press");
            Pointer(host,EventType.MouseDrag,start+new Vector2(80,0));Paint(host);
            Check(Mathf.Abs(Width(host)-380)<.01f && Divider(host).x>oldX+40,"Right drag moves the visible divider: "+Width(host)+" / "+Divider(host).x);
            Pointer(host,EventType.MouseDrag,start-new Vector2(60,0));Paint(host);
            Check(Mathf.Abs(Width(host)-240)<.01f && Divider(host).x<oldX-40,"Left drag moves the visible divider after repaint: width="+Width(host)+" x="+Divider(host).x+" start="+oldX+" hot="+GUIUtility.hotControl+" id="+Field("splitterControl").GetValue(host.target));
            Pointer(host,EventType.MouseUp,start-new Vector2(60,0));
            Check(GUIUtility.hotControl==0 && !(bool)Field("draggingSplitter").GetValue(host.target),"Release capture");
            Paint(host);start=Divider(host).center;
            Pointer(host,EventType.MouseDown,start,1,1);
            Check(!(bool)Field("draggingSplitter").GetValue(host.target),"Right click must not resize");
            Pointer(host,EventType.MouseDown,start,2);Paint(host);
            Check(Mathf.Abs(Width(host)-300)<.01f,"Double-click reset");
            start=Divider(host).center;Pointer(host,EventType.MouseDown,start);
            Pointer(host,EventType.MouseDrag,start+new Vector2(5000,0));Paint(host);
            Check(Mathf.Abs(Width(host)-450)<.01f,"Upper limit");
            Pointer(host,EventType.MouseDrag,start-new Vector2(5000,0));Paint(host);
            Check(Mathf.Abs(Width(host)-180)<.01f,"Lower limit");
            typeof(HoFaceProfileWindow).GetMethod("OnLostFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host.target,null);
            Check(GUIUtility.hotControl==0 && !(bool)Field("draggingSplitter").GetValue(host.target),"Lost focus releases capture");
            Check(HoFaceProfileJson.Write(profile)==original,"Dragging must not edit profile data");
            File.WriteAllText("profile-splitter-validation.txt","PASS: press, right/left visible movement with repaint, release, right-click ignored, reset, bounds, focus loss, profile unchanged.");
            Debug.Log("HO_PROFILE_SPLITTER_PASS");exit=0;
        }
        catch(Exception e){Debug.LogException(e);}
        finally
        {
            if(host!=null){var target=host.target;host.Close();if(target!=null)UnityEngine.Object.DestroyImmediate(target);}
            if(had)EditorPrefs.SetFloat(Pref,saved);else EditorPrefs.DeleteKey(Pref);
            EditorApplication.Exit(exit);
        }
    }
}
