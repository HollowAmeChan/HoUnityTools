using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

[Serializable] public sealed class LocalRotationPose
{
    public string name; public float[] matrix;
    public Matrix4x4 Matrix { get { var value=new Matrix4x4();for(int row=0;row<4;row++)for(int column=0;column<4;column++)value[row,column]=matrix[row*4+column];return value; } }
}
[Serializable] public sealed class LocalRotationSample { public string name;public LocalRotationPose[] inputs,expected; }
[Serializable] public sealed class LocalRotationFixture { public string variant,blenderVersion;public string[] ownerNames,inputNames;public LocalRotationPose[] bones;public LocalRotationSample[] samples; }
[Serializable] public sealed class LocalRotationMeasurement { public string variant,sample,bone,timing;public float positionError,angleError,scaleError; }
[Serializable] public sealed class LocalRotationResult
{
    public string unityVersion;
    public List<string> failures=new List<string>();
    public List<LocalRotationMeasurement> measurements=new List<LocalRotationMeasurement>();
}

public sealed class LocalRotationProbe : MonoBehaviour
{
    public GameObject[] models;
    public TextAsset[] fixtures,configs;
    public string outputPath;
    private readonly List<Route> routes=new List<Route>();
    private readonly LocalRotationResult result=new LocalRotationResult();
    private sealed class Route
    {
        public string name;public GameObject rig;public Transform outer;
        public HoAuxRig ho;public LocalRotationFixture fixture;
        public Dictionary<string,Transform> bones;
        public Dictionary<string,Matrix4x4> correction=new Dictionary<string,Matrix4x4>();
        public Matrix4x4 conversion;
    }

    private void Start()
    {
        try
        {
            result.unityVersion=Application.unityVersion;
            for(int index=0;index<models.Length;index++)
                foreach(bool merged in new[]{false,true})routes.Add(Create(index,merged));
            // Start with the imported FBX, then deliberately vary each bone's
            // intrinsic frame. This makes K_owner^-1 K_target nonidentity and
            // catches a plain-Local implementation that accidentally reuses the
            // Owner Orientation mapping (or assumes identical FBX bone bases).
            foreach(bool merged in new[]{false,true})routes.Add(Create(0,merged,true));
            int samples=routes[0].fixture.samples.Length;
            for(int index=0;index<samples;index++)
            {
                foreach(var route in routes)ApplyInputs(route,index);
                foreach(var route in routes)route.ho.EvaluateNow();
                Capture(index,"first");
                // Repeated evaluations must restart owner channels from their
                // bound input, not asymptotically consume their previous output.
                for(int pass=0;pass<8;pass++)foreach(var route in routes)route.ho.EvaluateNow();
                Capture(index,"stable");
            }
            CheckInvalidRoots();
            CheckDegenerateInput();
        }
        catch(Exception error){result.failures.Add(error.ToString());Debug.LogException(error);}
        finally
        {
            File.WriteAllText(outputPath,JsonUtility.ToJson(result,true));
            Debug.Log("COPY_R_LOCAL_RUNTIME_DONE "+result.measurements.Count+" measurements; failures="+result.failures.Count);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(result.failures.Count==0?0:1);
#endif
        }
    }

    private Route Create(int index,bool merged,bool rebaseAxes=false)
    {
        var route=new Route {fixture=JsonUtility.FromJson<LocalRotationFixture>(fixtures[index].text)};
        route.name=route.fixture.variant+(rebaseAxes?"_different_bases":"")+(merged?"_merged":"_serialized");
        route.outer=new GameObject(route.name+" outer").transform;
        route.rig=Instantiate(models[index]);route.rig.transform.SetParent(route.outer,false);
        foreach(var animator in route.rig.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        if(rebaseAxes)RebaseAxes(route);
        Map(route);route.conversion=Fit(route);
        foreach(var bone in route.fixture.bones)
            route.correction[bone.name]=bone.Matrix.inverse*route.conversion.inverse*route.bones[bone.name].localToWorldMatrix;
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow")).First(t=>t!=null);
        var mode=type.GetNestedType("ConstraintMode",BindingFlags.NonPublic);
        var apply=type.GetMethod("ApplyConstraints",BindingFlags.NonPublic|BindingFlags.Static);
        int imported=(int)apply.Invoke(null,new object[]{configs[index],route.rig,Enum.Parse(mode,"HoAux"),route.outer});
        if(imported<30)throw new Exception("Fixture direct stack was not imported: "+imported);
        if(File.Exists(Path.Combine(Application.dataPath,"Fixture/use-v4.txt")))
        {
            var reportType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR.HoConstraintIrImportReports")).First(t=>t!=null);
            object reports=reportType.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            object report=reportType.GetMethod("Find").Invoke(reports,new object[]{route.rig});
            if(report==null||(int)report.GetType().GetField("sourceSchemaVersion").GetValue(report)!=4||
                string.IsNullOrEmpty((string)report.GetType().GetField("selectedImplementationsJson").GetValue(report)))
                throw new Exception("Production v4 import window did not preserve selected implementation evidence");
        }
        route.ho=route.outer.GetComponent<HoAuxRig>();
        if(route.ho==null)throw new Exception("External HoAux host missing");
        route.ho.Mode=HoAuxRig.UpdateMode.Manual;
        if(!route.ho.DirectOperations.Any(op=>op.rotationSpace==HoAuxRig.DirectRotationSpace.Local)||
            !route.ho.DirectOperations.Any(op=>op.rotationSpace==HoAuxRig.DirectRotationSpace.LocalOwnerOrient))
            throw new Exception("Both local contracts must produce operations");
        if(merged)Merge(route);
        else RoundTrip(route);
        return route;
    }

    private void RoundTrip(Route route)
    {
#if UNITY_EDITOR
        string path="Assets/Fixture/"+route.name+".prefab";
        var saved=UnityEditor.PrefabUtility.SaveAsPrefabAsset(route.outer.gameObject,path);
        if(saved==null)throw new Exception("Could not serialize local-rotation bindings");
        var restored=(GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(saved);
        DestroyImmediate(route.outer.gameObject);
        route.outer=restored.transform;
        route.ho=restored.GetComponent<HoAuxRig>();
        route.rig=restored.transform.GetChild(0).gameObject;
        Map(route);
        route.ho.Mode=HoAuxRig.UpdateMode.Manual;
        // Erase the direct serialized references so path recovery is tested too.
        foreach(var operation in route.ho.DirectOperations){operation.owner=null;operation.target=null;operation.up=null;}
#endif
    }

    private void Merge(Route route)
    {
        var replacements=new Dictionary<Transform,Transform>();
        Transform destination=Clone(route.rig.transform,route.outer,replacements);
        destination.name=route.rig.name+" merged";
        var frame=new GameObject("Different target-parent bind frame").transform;
        frame.SetParent(destination,false);frame.localRotation=Quaternion.Euler(17,-23,11);frame.localPosition=new Vector3(.12f,-.04f,.09f);frame.localScale=Vector3.one*1.17f;
        var boneNames=new HashSet<string>(route.fixture.bones.Select(b=>b.name));
        foreach(var bone in route.fixture.bones)
        {
            Transform source=route.bones[bone.name];
            if(source.parent==null||!boneNames.Contains(source.parent.name))replacements[source].SetParent(frame,true);
        }
        Action finish=route.ho.PrepareArmatureMerge(replacements);
        if(finish==null)throw new Exception("Local operation merge did not participate");
        finish();
        DestroyImmediate(route.rig);
        route.rig=destination.gameObject;
        Map(route);
        foreach(var op in route.ho.DirectOperations)
            if(op.owner==null||op.target==null)throw new Exception("Merge dropped local owner/target reference");
    }

    private static Transform Clone(Transform original,Transform parent,Dictionary<Transform,Transform> replacements)
    {
        var clone=new GameObject(original.name).transform;clone.SetParent(parent,false);
        clone.localPosition=original.localPosition;clone.localRotation=original.localRotation;clone.localScale=original.localScale;
        replacements.Add(original,clone);
        foreach(Transform child in original)Clone(child,clone,replacements);
        return clone;
    }

    private static void RebaseAxes(Route route)
    {
        var replacements=new Dictionary<Transform,Transform>();
        var copy=Clone(route.rig.transform,route.outer,replacements);
        DestroyImmediate(route.rig);route.rig=copy.gameObject;
        var names=new HashSet<string>(route.fixture.bones.Select(b=>b.name));
        var bones=copy.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)).ToArray();
        var poses=bones.ToDictionary(t=>t,t=>t.localToWorldMatrix);
        for(int index=0;index<bones.Length;index++)
        {
            Transform bone=bones[index];Matrix4x4 before=poses[bone];
            Quaternion rotation=before.rotation*Quaternion.Euler(index%5*9-13,index%7*7-11,index%3*17-9);
            Matrix4x4 world=Matrix4x4.TRS(before.GetColumn(3),rotation,Scale(before));
            Matrix4x4 local=bone.parent?bone.parent.worldToLocalMatrix*world:world;
            bone.localPosition=local.GetColumn(3);bone.localRotation=local.rotation;bone.localScale=Scale(local);
        }
    }

    private static void Map(Route route)
    {
        var names=new HashSet<string>(route.fixture.bones.Select(b=>b.name));
        route.bones=route.rig.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)).ToDictionary(t=>t.name);
        if(route.bones.Count!=names.Count)throw new Exception("FBX dropped fixture bones");
    }

    private static Matrix4x4 Fit(Route route)
    {
        var rest=route.fixture.bones.ToDictionary(b=>b.name,b=>b.Matrix);
        float scale=Vector3.Distance(route.bones["Root"].position,route.bones["TargetA_L"].position)/Vector3.Distance(rest["Root"].GetColumn(3),rest["TargetA_L"].GetColumn(3));
        float best=float.PositiveInfinity;Matrix4x4 answer=Matrix4x4.identity;
        foreach(var permutation in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
        for(int mask=0;mask<8;mask++)
        {
            var conversion=Matrix4x4.zero;conversion[3,3]=1;
            for(int axis=0;axis<3;axis++)conversion[axis,permutation[axis]]=scale*((mask&(1<<axis))==0?1:-1);
            Vector3 offset=route.bones["Root"].position-conversion.MultiplyPoint3x4(rest["Root"].GetColumn(3));
            conversion.SetColumn(3,new Vector4(offset.x,offset.y,offset.z,1));
            float error=rest.Max(b=>Vector3.Distance(conversion.MultiplyPoint3x4(b.Value.GetColumn(3)),route.bones[b.Key].position));
            if(error<best){best=error;answer=conversion;}
        }
        if(best>1e-4f)throw new Exception("Fixture FBX coordinate fit failed: "+best);
        return answer;
    }

    private static void ApplyInputs(Route route,int index)
    {
        route.ho.RestoreOwnersToBindPose();
        route.outer.localPosition=new Vector3(.03f*index,.04f,-.02f);
        route.outer.localRotation=Quaternion.Euler(index*.3f,-index*.5f,7);
        route.outer.localScale=Vector3.one*(1+.003f*index);
        // Fixture inputs are ancestor ordered. Their expected matrices were read
        // from Blender, including constraints on any input's parent.
        var expected=route.fixture.samples[index].expected.ToDictionary(b=>b.name,b=>b.Matrix);
        foreach(var input in route.fixture.samples[index].inputs)
        {
            Transform target=route.bones[input.name];
            Matrix4x4 world=route.outer.localToWorldMatrix*route.conversion*input.Matrix*route.correction[input.name];
            Matrix4x4 parentWorld=target.parent?target.parent.localToWorldMatrix:Matrix4x4.identity;
            if(target.parent!=null&&expected.TryGetValue(target.parent.name,out Matrix4x4 parentPose))
                parentWorld=route.outer.localToWorldMatrix*route.conversion*parentPose*route.correction[target.parent.name];
            Matrix4x4 local=parentWorld.inverse*world;
            target.localPosition=local.GetColumn(3);target.localRotation=local.rotation;target.localScale=Scale(local);
        }
    }

    private void Capture(int index,string timing)
    {
        foreach(var route in routes)Capture(route,index,timing);
    }

    private void Capture(Route route,int index,string timing)
    {
        if(!string.IsNullOrEmpty(route.ho.LastDirectError))throw new Exception(route.name+": "+route.ho.LastDirectError);
        var expected=route.fixture.samples[index].expected.ToDictionary(b=>b.name,b=>b.Matrix);
        foreach(string name in route.fixture.ownerNames)
        {
            Transform actual=route.bones[name];
            Matrix4x4 world=route.outer.localToWorldMatrix*route.conversion*expected[name]*route.correction[name];
            float position=Vector3.Distance(actual.position,world.GetColumn(3));
            float angle=Angle(actual.rotation,world.rotation);
            float scale=Vector3.Distance(actual.lossyScale,Scale(world));
            result.measurements.Add(new LocalRotationMeasurement{variant=route.name,sample=route.fixture.samples[index].name,bone=name,timing=timing,positionError=position,angleError=angle,scaleError=scale});
            if(float.IsNaN(position)||float.IsNaN(angle)||float.IsNaN(scale)||position>1e-4f||angle>.1f||scale>1e-4f)
                throw new Exception("CopyR parity "+route.name+"/"+route.fixture.samples[index].name+"/"+name+" "+timing+": p="+position+" a="+angle+" s="+scale);
        }
    }

    private void CheckInvalidRoots()
    {
        foreach(var route in routes)
        foreach(string mode in new[]{"singular","negative","even_negative","nonuniform","sheared"})
        {
            ApplyInputs(route,1);route.ho.EvaluateNow();
            var owners=route.ho.DirectOperations.Select(op=>op.owner).Distinct().ToDictionary(t=>t,t=>Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale));
            Vector3 beforeScale=route.outer.localScale;
            Quaternion beforeRotation=route.rig.transform.localRotation;
            try
            {
                route.outer.localScale=mode=="singular"?Vector3.zero:
                    mode=="negative"?new Vector3(-1,1,1):
                    mode=="even_negative"?new Vector3(-1,-1,1):new Vector3(2,1,1);
                if(mode=="sheared")
                {
                    route.rig.transform.localRotation=Quaternion.Euler(23,41,-17);
                    Matrix4x4 root=route.rig.transform.localToWorldMatrix;
                    Vector3 x=((Vector3)root.GetColumn(0)).normalized,y=((Vector3)root.GetColumn(1)).normalized,z=((Vector3)root.GetColumn(2)).normalized;
                    if(Mathf.Max(Mathf.Abs(Vector3.Dot(x,y)),Mathf.Max(Mathf.Abs(Vector3.Dot(x,z)),Mathf.Abs(Vector3.Dot(y,z))))<.001f)
                        throw new Exception("Root shear fixture did not create shear");
                }
                for(int pass=0;pass<2;pass++)
                {
                    route.ho.EvaluateNow();
                    if(string.IsNullOrEmpty(route.ho.LastDirectError)||!route.ho.LastDirectError.StartsWith("Rig-space root needs ",StringComparison.Ordinal))
                        throw new Exception(route.name+" accepted invalid root "+mode+": "+route.ho.LastDirectError);
                    foreach(var owner in owners)
                    {
                        Matrix4x4 current=Matrix4x4.TRS(owner.Key.localPosition,owner.Key.localRotation,owner.Key.localScale);
                        for(int cell=0;cell<16;cell++)if(current[cell]!=owner.Value[cell])
                            throw new Exception(route.name+" changed owner before rejecting root "+mode+": "+owner.Key.name);
                    }
                    foreach(var bone in route.outer.GetComponentsInChildren<Transform>(true))
                    {
                        Matrix4x4 world=bone.localToWorldMatrix;
                        for(int cell=0;cell<16;cell++)if(float.IsNaN(world[cell])||float.IsInfinity(world[cell]))
                            throw new Exception(route.name+" produced nonfinite bone under root "+mode+": "+bone.name);
                    }
                }
            }
            finally
            {
                route.outer.localScale=beforeScale;
                route.rig.transform.localRotation=beforeRotation;
            }
            ApplyInputs(route,1);route.ho.EvaluateNow();
            Capture(route,1,"root_recovered_"+mode);
        }
    }

    private void CheckDegenerateInput()
    {
        var route=routes[0];
        route.bones["TargetA_L"].localScale=Vector3.zero;
        route.ho.EvaluateNow();
        if(string.IsNullOrEmpty(route.ho.LastDirectError))throw new Exception("Degenerate local input was accepted");
        ApplyInputs(route,1);
        route.bones["TargetParent_L"].localScale=new Vector3(1.3f,.8f,1.1f);
        route.ho.EvaluateNow();
        if(string.IsNullOrEmpty(route.ho.LastDirectError))throw new Exception("Sheared target hierarchy was accepted as exact local rotation");
    }

    private static Vector3 Scale(Matrix4x4 value){return new Vector3(value.GetColumn(0).magnitude,value.GetColumn(1).magnitude,value.GetColumn(2).magnitude);}
    private static float Angle(Quaternion a,Quaternion b)
    {
        double x=(double)a.w*b.x-(double)a.x*b.w-(double)a.y*b.z+(double)a.z*b.y;
        double y=(double)a.w*b.y+(double)a.x*b.z-(double)a.y*b.w-(double)a.z*b.x;
        double z=(double)a.w*b.z-(double)a.x*b.y+(double)a.y*b.x-(double)a.z*b.w;
        double w=(double)a.w*b.w+(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;
        return (float)(2*Math.Atan2(Math.Sqrt(x*x+y*y+z*z),Math.Abs(w))*180/Math.PI);
    }
}
