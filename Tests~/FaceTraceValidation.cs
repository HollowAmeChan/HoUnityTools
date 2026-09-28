// Copy into a disposable Unity project's Assets/Editor and run HoFaceTraceValidation.RunBatch.
using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.FaceTracking;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class HoFaceTraceValidation
{
    private const string Pending = "Ho.Face.TraceValidation";
    static HoFaceTraceValidation()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += InPlay;
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message)
    { Check(Mathf.Abs(actual-expected)<0.00001f, message+": "+actual+" != "+expected); }

    public static void RunBatch()
    {
        if (!File.Exists(".ho-face-validation")) throw new Exception("Disposable project marker required");
        var schedule = new HoFaceTraceSchedule(.2);
        Check(schedule.Accept(0), "First observed solve");
        Check(!schedule.Accept(.1) && !schedule.Accept(.199), "Do not over-sample");
        Check(schedule.Accept(.2), "Requested 0.2s interval");
        Check(schedule.Accept(.81), "One real sample after stall");
        Check(!schedule.Accept(.81) && !schedule.Accept(.9), "No catch-up duplicates");
        Check(schedule.Accept(1.01), "Resume actual-time spacing");
        var every = new HoFaceTraceSchedule(0);
        Check(every.Accept(.001) && every.Accept(.002) && !every.Accept(.002), "Every solve, no same-time duplicates");
        SessionState.SetBool(Pending, true);
        EditorApplication.update -= InPlay;
        EditorApplication.update += InPlay;
        EditorApplication.isPlaying = true;
    }

    private static void InPlay()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= InPlay;
        SessionState.SetBool(Pending, false);
        try { Validate(); Debug.Log("HO_FACE_TRACE_PASS"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void Validate()
    {
        var go = new GameObject("TraceValidationRig");
        go.AddComponent<Animator>();
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] {0,1,2} };
        mesh.AddBlendShapeFrame("TestJaw",100,new[] {Vector3.up,Vector3.up,Vector3.up},new Vector3[3],new Vector3[3]);
        renderer.sharedMesh = mesh;
        var path = AssetDatabase.GenerateUniqueAssetPath("Assets/TraceValidation.controller");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Jaw",AnimatorControllerParameterType.Float);
        var clip = new AnimationClip();
        clip.SetCurve("",typeof(SkinnedMeshRenderer),"blendShape.TestJaw",AnimationCurve.Constant(0,1,0));
        AssetDatabase.AddObjectToAsset(clip,controller);
        controller.layers[0].stateMachine.AddState("Only").motion=clip;
        AssetDatabase.SaveAssets();
        var middleware = new HoFaceMiddleware();
        middleware.inputs.Add(new HoFaceOutput {parameter="jawOpen",expression="JawOpen"});
        middleware.inputs.Add(new HoFaceOutput {parameter="mouthClose",expression="MouthClose"});
        middleware.outputs.Add(new HoFaceOutput {
            parameter="Jaw", expression="if((jawOpen-mouthClose)<0,jawOpen-mouthClose,jawOpen)",
            curve=new AnimationCurve(new Keyframe(-1,-1,1,1),new Keyframe(-.08f,-.08f,1,8f/3),
                new Keyframe(-.05f,0,8f/3,0),new Keyframe(.05f,0,0,8f/3),new Keyframe(.08f,.08f,8f/3,1),new Keyframe(1,1,1,1))
        });
        middleware.outputs[0].modifiers.Add(new HoFaceModifier {kind=HoFaceModifierKind.Smooth,seconds=.009f});
        middleware.outputs.Add(new HoFaceOutput {parameter="Chain",expression="",defaultValue=1,curve=AnimationCurve.Linear(0,0,1,3)});
        middleware.outputs.Add(new HoFaceOutput {parameter="Chain",expression="out(\"Chain\")+1",curve=AnimationCurve.Linear(0,0,3,3)});
        var profilePath = Path.GetFullPath("Logs/trace-validation-profile.json");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(profilePath,HoFaceProfileJson.Write(middleware));
        var settings = new HoFaceDebugSettings {characterPath=go.name,faceControllerPath=path,profilePath=profilePath,startOnPlay=false};
        using (var session = new HoFaceAnimationSession(settings))
        {
            var recorder = new HoFaceTraceRecorder(session,"test label \"quoted\"",0);
            foreach (float close in new[]{.281f,.279f,.278f})
            {
                session.SetPreview("jawOpen",.28f);
                session.SetPreview("mouthClose",close);
                if(close==.278f) session.SetPreview("Jaw",.9f);
                session.Tick(1f/60);
            }
            recorder.Stop("test-stop");
            recorder.Stop("must-not-append-twice");
            var lines=File.ReadAllLines(recorder.FilePath);
            Check(lines.Length==5 && recorder.Samples==3,"Header + 3 actual samples + footer");
            Check(lines[0].Contains("profileJson") && lines[0].Contains("quoted"),"Metadata and label");
            var frames=lines.Skip(1).Take(3).Select(JsonUtility.FromJson<HoFaceTraceFrame>).ToArray();
            for(int i=0;i<3;i++)
            {
                Check(frames[i].sample==i,"Sequence");
                if(i>0) Check(frames[i].clock>frames[i-1].clock,"Actual monotonic timestamps");
                Near(frames[i].inputs.Single(v=>v.name=="jawOpen").effective,.28f,"Effective input");
                var chain=frames[i].outputs.Where(v=>v.name=="Chain").ToArray();
                Check(chain.Length==2,"Do not collapse duplicate rows");
                Near(chain[0].curve,1,"Constants bypass curve");
                Near(chain[0].modified,1,"Earlier row preserved");
                Near(chain[0].published,2,"Final published duplicate value");
            }
            Near(frames[0].outputs[0].expression,-.001f,"Difference branch");
            Near(frames[0].outputs[0].curve,0,"Dead zone");
            Near(frames[1].outputs[0].expression,.28f,"Positive branch");
            Near(frames[1].outputs[0].curve,.28f,"Identity shoulder");
            Check(frames[1].outputs[0].modified>0 && frames[1].outputs[0].modified<.28f,"Smoothing kept separate");
            Near(frames[2].outputs[0].published,.9f,"Final preview override recorded");
            Check(frames[2].overrides.Any(v=>v.name=="Jaw"),"Override provenance");
            Check(lines.Last().Contains("test-stop"),"Partial recording survives stop");
            var second=new HoFaceTraceRecorder(session,"configuration-change",0);
            settings.ReloadProfile();
            session.Tick(1f/60);
            Check(!second.Recording && File.ReadAllLines(second.FilePath).Last().Contains("configuration-changed"),"Stop before mixing configurations");
            var timed=new HoFaceTraceRecorder(session,"duration",0,.01);
            System.Threading.Thread.Sleep(20);
            session.Tick(1f/60);
            Check(!timed.Recording && timed.Samples==0,"No fabricated sample beyond duration");
            File.WriteAllText("face-trace-validation.txt","PASS: interval, gaps, same-solve stages, duplicate rows, overrides, profile changes, duration, partial save\n"+recorder.FilePath);
        }
        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(mesh);
    }
}
