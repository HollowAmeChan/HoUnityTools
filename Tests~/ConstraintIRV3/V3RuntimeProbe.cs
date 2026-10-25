using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

[Serializable] public class V3Pose
{
    public string name; public float[] matrix;
    public Matrix4x4 Matrix { get {var m=new Matrix4x4();for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=matrix[r*4+c];return m;} }
}
[Serializable] public class V3Sample {public string name;public V3Pose[] expected,inputs;public Vector3[] meshProbes;}
[Serializable] public class V3Fixture {public string root,meshName;public V3Pose[] bones;public V3Sample[] authoredSamples;public Vector3[] meshRestProbes;}
[Serializable] public class V3Measurement {public string backend,sample,bone,timing;public float positionError,angleError;}
[Serializable] public class V3Result {public List<string> failures=new List<string>();public List<V3Measurement> measurements=new List<V3Measurement>();}

public class V3RuntimeProbe : MonoBehaviour
{
    public GameObject model;public TextAsset fixture,ir;public string outputPath;
    private V3Fixture data;private readonly List<Route> routes=new List<Route>();
    private readonly V3Result result=new V3Result();private int index,frame;private bool ready,started;
    private readonly List<Toy> toys=new List<Toy>();
    private class Toy {public string backend;public bool sparse,positionOnly;public Transform root,parent,owner,target,a,b;public Vector3 bind,preservedScale;public Quaternion preservedRotation;}
    private class Route
    {
        public string name;public GameObject rig;public Transform outer;
        public Dictionary<string,Transform> bones;
        public Matrix4x4 conversion;
        public Dictionary<string,Matrix4x4> correction=new Dictionary<string,Matrix4x4>();
        public SkinnedMeshRenderer skin;public int[] probeMap;public Mesh baked;
    }
    private void Start(){try{Setup();ready=true;}catch(Exception e){Fail(e);}}
    private void Setup()
    {
        data=JsonUtility.FromJson<V3Fixture>(fixture.text);
        foreach(string backend in new[]{"Standard","HoAux"})
        {
            var r=new Route {name=backend,rig=Instantiate(model),outer=new GameObject(backend+" World").transform};
            r.rig.name=backend;r.rig.transform.SetParent(r.outer,true);
            var animator=r.rig.GetComponent<Animator>();if(animator)animator.enabled=false;
            r.bones=r.rig.GetComponentsInChildren<Transform>(true).Where(t=>data.bones.Any(b=>b.name==t.name)).ToDictionary(t=>t.name);
            r.conversion=Fit(r);
            foreach(var b in data.bones)r.correction[b.name]=b.Matrix.inverse*r.conversion.inverse*r.bones[b.name].localToWorldMatrix;
            r.skin=r.rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(x=>x.name==data.meshName);r.skin.updateWhenOffscreen=true;
            r.baked=new Mesh();r.probeMap=new int[data.meshRestProbes.Length];var verts=r.skin.sharedMesh.vertices;
            for(int i=0;i<r.probeMap.Length;i++)
            {
                Vector3 want=r.conversion.MultiplyPoint3x4(data.meshRestProbes[i]);
                int best=Enumerable.Range(0,verts.Length).OrderBy(j=>Vector3.SqrMagnitude(r.skin.transform.TransformPoint(verts[j])-want)).First();
                if(Vector3.Distance(r.skin.transform.TransformPoint(verts[best]),want)>0.00001f)throw new Exception("Mesh probe bind mismatch");
                r.probeMap[i]=best;
            }
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow")).First(t=>t!=null);
            var mode=type.GetNestedType("ConstraintMode",BindingFlags.NonPublic);
            int count=(int)type.GetMethod("ApplyConstraints",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{ir,r.rig,Enum.Parse(mode,backend),r.rig.transform});
            if(count!=11)throw new Exception("Expected 11 semantic operations, got "+count+" for "+backend);
            routes.Add(r);
        }
        Apply(0);
        foreach(string backend in new[]{"Standard","HoAux"})foreach(bool sparse in new[]{false,true})toys.Add(CreateToy(backend,sparse));
        foreach(string backend in new[]{"Standard","HoAux"})toys.Add(CreateToy(backend,false,true));
        ApplyToys(0);
    }
    private Matrix4x4 Fit(Route r)
    {
        float best=float.PositiveInfinity;Matrix4x4 answer=Matrix4x4.identity;
        float scale=Vector3.Distance(r.bones["hips"].position,r.bones["spine"].position)/Vector3.Distance(data.bones.First(b=>b.name=="hips").Matrix.GetColumn(3),data.bones.First(b=>b.name=="spine").Matrix.GetColumn(3));
        foreach(var p in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
        for(int mask=0;mask<8;mask++)
        {
            var m=Matrix4x4.zero;m[3,3]=1;for(int a=0;a<3;a++)m[a,p[a]]=scale*((mask&(1<<a))==0?1:-1);
            float err=data.bones.Sum(b=>Vector3.Distance(m.MultiplyPoint3x4(b.Matrix.GetColumn(3)),r.bones[b.name].position));
            if(err<best){best=err;answer=m;}
        }
        if(best>0.0001f)throw new Exception("Coordinate fit failed: "+best);return answer;
    }
    private void Apply(int sample)
    {
        ApplyToys(sample);
        foreach(var r in routes)
        {
            bool external=data.authoredSamples[sample].name=="root_motion";
            r.outer.position=external?new Vector3(.3f,-.1f,.2f):Vector3.zero;
            r.outer.rotation=external?Quaternion.Euler(12,27,-8):Quaternion.identity;
            r.outer.localScale=external?Vector3.one*1.1f:Vector3.one;
            foreach(var b in data.authoredSamples[sample].inputs)
            {
                var expected=data.authoredSamples[sample].expected.First(x=>x.name==b.name);
                Matrix4x4 world=r.outer.localToWorldMatrix*r.conversion*expected.Matrix*r.correction[b.name];
                Transform t=r.bones[b.name];Matrix4x4 local=t.parent?t.parent.worldToLocalMatrix*world:world;
                t.localPosition=local.GetColumn(3);t.localRotation=local.rotation;t.localScale=Scale(local);
            }
        }
    }
    private void Update()
    {
        if(!ready)return;if(!started){started=true;return;}
        try
        {
            frame++;if(frame==1)Capture("first");if(frame<6)return;Capture("settled");frame=0;index++;
            if(index==data.authoredSamples.Length){ready=false;Finish();return;}Apply(index);
        }
        catch(Exception e){ready=false;Fail(e);}
    }
    private void Capture(string timing)
    {
        var sample=data.authoredSamples[index];
        foreach(var t in toys)
        {
            Vector3 want=Vector3.Lerp(t.parent.TransformPoint(t.bind),t.target.position,.3f);
            float p=Vector3.Distance(t.owner.position,want),a=t.positionOnly?Angle(t.owner.localRotation,t.preservedRotation):Angle(t.owner.rotation,Quaternion.Slerp(t.a.rotation,t.b.rotation,.3f));
            result.measurements.Add(new V3Measurement {backend=t.backend,sample="toy_"+sample.name,bone=t.positionOnly?"PositionKeepsRotationScale":t.sparse?"CopyStackSparse":"CopyStack",timing=timing,positionError=p,angleError=a});
            float contractAngle=t.backend=="Standard"&&!t.positionOnly?Angle(t.owner.rotation,Quaternion.Lerp(t.a.rotation,t.b.rotation,.3f)):a;
            if(p>.00003f||contractAngle>.03f)throw new Exception(t.backend+" copy stack drift: "+sample.name+" p="+p+" contractAngle="+contractAngle);
            if(t.positionOnly&&Vector3.Distance(t.owner.localScale,t.preservedScale)>1e-6f)throw new Exception("Position-only import overwrote scale");
        }
        foreach(var r in routes)
        {
            foreach(var b in sample.expected.Where(b=>b.name.StartsWith("REF_")||b.name.StartsWith("DIR_")||b.name==data.root))
            {
                Matrix4x4 want=r.outer.localToWorldMatrix*r.conversion*b.Matrix*r.correction[b.name];Transform t=r.bones[b.name];
                Add(r,sample,b.name,timing,Vector3.Distance(t.position,want.GetColumn(3)),b.name.StartsWith("REF_")?0:Angle(t.rotation,want.rotation));
            }
            r.skin.BakeMesh(r.baked,true);var verts=r.baked.vertices;
            for(int i=0;i<r.probeMap.Length;i++)Add(r,sample,"@mesh_"+i,timing,Vector3.Distance(r.skin.transform.TransformPoint(verts[r.probeMap[i]]),(r.outer.localToWorldMatrix*r.conversion).MultiplyPoint3x4(sample.meshProbes[i])),0);
            var ho=r.rig.GetComponent<HoAuxRig>();
            if(ho!=null&&sample.name!="nonuniform_scale"&&!string.IsNullOrEmpty(ho.LastDirectError))throw new Exception(ho.LastDirectError);
        }
    }
    private Toy CreateToy(string backend,bool sparse,bool positionOnly=false)
    {
        var t=new Toy {backend=backend,sparse=sparse,positionOnly=positionOnly,root=new GameObject("Toy "+backend+(sparse?" sparse":"")+(positionOnly?" partial":"")).transform};
        t.parent=Child(t.root,"Parent",sparse?Vector3.zero:new Vector3(.1f,.2f,.3f));t.owner=Child(t.parent,"Owner",sparse?Vector3.right*.2f:new Vector3(.2f,.3f,.4f));
        t.owner.localRotation=Quaternion.Euler(15,8,60);t.bind=t.owner.localPosition;
        t.target=Child(t.root,"Target",Vector3.right);t.a=Child(t.root,"A",sparse?Vector3.right*2:Vector3.up);t.b=Child(t.root,"B",sparse?Vector3.right*3:Vector3.forward);
        var config=new ConstraintConfig {schema="hotools.rig-constraint-ir",schemaVersion=3,armatureName="Toy",exportTime="test",mchEnabledBones=new List<string>(),mchBindings=new List<MchBindingInfo>(),auxBones=new List<AuxBoneInfo>(),knownConstraints=new List<KnownConstraintInfo>(),unknownConstraints=new List<UnknownConstraintInfo>(),aimBindings=new List<AimBindingInfo>(),bindingContext=new ConstraintBindingContext {space="BLENDER_ARMATURE",matrixLayout="ROW_MAJOR",objectMatrix=Flat(Matrix4x4.identity),bones=new List<ConstraintBoneBinding>()}};
        config.bindingContext.fbxBoneAxes="Y_X";
        var flip=Matrix4x4.Scale(new Vector3(-1,1,1));
        foreach(var tr in new[]{t.parent,t.owner,t.target,t.a,t.b})config.bindingContext.bones.Add(new ConstraintBoneBinding {boneName=tr.name,parentBone=tr.parent==t.root?"":tr.parent.name,restMatrix=Flat(flip*tr.localToWorldMatrix*flip),poseBasis=Flat(Matrix4x4.identity),inheritScale="FULL"});
        foreach(var spec in new[]{Tuple.Create("COPY_LOCATION","Target",.3f),Tuple.Create("COPY_ROTATION","A",1f),Tuple.Create("COPY_ROTATION","B",.3f)})
            config.knownConstraints.Add(new KnownConstraintInfo {ownerBone="Owner",relationType="DIRECT_CONSTRAINT",constraint=new BlenderConstraintInfo {name=spec.Item1+config.knownConstraints.Count,stackIndex=config.knownConstraints.Count,constraintType=spec.Item1,targetObjectName="Toy",targetBoneName=spec.Item2,parameters=new BlenderConstraintParameters {owner_space="WORLD",target_space="WORLD",influence=spec.Item3,use_x=true,use_y=true,use_z=true,mix_mode="REPLACE"},references=new BlenderConstraintReferences()}});
        if(positionOnly)
        {
            config.knownConstraints.RemoveRange(1,2);
            for(int i=0;i<2;i++)config.unknownConstraints.Add(new UnknownConstraintInfo {ownerBone="Owner",reason="unmarked tracking",constraint=new BlenderConstraintInfo {name="Track"+i,stackIndex=i+1,constraintType=i==0?"DAMPED_TRACK":"LOCKED_TRACK",targetObjectName="Toy",targetBoneName=i==0?"A":"B",parameters=new BlenderConstraintParameters {influence=1,owner_space="WORLD",target_space="WORLD"}}});
        }
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow")).First(x=>x!=null);
        var mode=type.GetNestedType("ConstraintMode",BindingFlags.NonPublic);
        var method=type.GetMethod("ApplyConstraints",BindingFlags.NonPublic|BindingFlags.Static);
        int first=(int)method.Invoke(null,new object[]{new TextAsset(JsonUtility.ToJson(config)),t.root.gameObject,Enum.Parse(mode,backend),t.root});
        int again=(int)method.Invoke(null,new object[]{new TextAsset(JsonUtility.ToJson(config)),t.root.gameObject,Enum.Parse(mode,backend),t.root});
        if(first!=(positionOnly?1:3)||again!=first)throw new Exception("Reimport count mismatch");
        return t;
    }
    private static Transform Child(Transform parent,string name,Vector3 position)
    {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
    private static float[] Flat(Matrix4x4 m)
    {var a=new float[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)a[r*4+c]=m[r,c];return a;}
    private void ApplyToys(int sample)
    {
        foreach(var t in toys)
        {
            t.root.position=new Vector3(.1f*sample,.02f,0);t.root.rotation=Quaternion.Euler(0,sample*2,0);t.root.localScale=Vector3.one*1.1f;
            t.parent.localPosition=new Vector3(.1f+.003f*sample,.2f,.3f);t.parent.localRotation=Quaternion.Euler(sample,-sample*.4f,sample*.2f);
            t.target.localPosition=new Vector3(1,.002f*sample,-.001f*sample);
            t.a.localRotation=Quaternion.Euler(sample*.7f,20+sample,-15);
            t.b.localRotation=Quaternion.Euler(-sample*.3f,-40+sample*.2f,25);
            if(t.positionOnly)
            {
                t.preservedRotation=Quaternion.Euler(7+sample,15-sample*.3f,20);
                t.preservedScale=new Vector3(1.3f,.9f,1.1f);
                t.owner.localRotation=t.preservedRotation;t.owner.localScale=t.preservedScale;
            }
        }
    }
    private void Add(Route r,V3Sample sample,string bone,string timing,float pos,float angle)
    {
        result.measurements.Add(new V3Measurement {backend=r.name,sample=sample.name,bone=bone,timing=timing,positionError=pos,angleError=angle});
        if(r.name=="HoAux"&&sample.name!="nonuniform_scale"&&(pos>0.00003f||angle>0.06f||float.IsNaN(pos)||float.IsNaN(angle)))
            throw new Exception("Ho v3 parity failed: "+sample.name+"/"+bone+" pos="+pos+" angle="+angle);
    }
    private static Vector3 Scale(Matrix4x4 m){return new Vector3(m.GetColumn(0).magnitude,m.GetColumn(1).magnitude,m.GetColumn(2).magnitude);}
    private static float Angle(Quaternion a,Quaternion b)
    {
        double x=(double)a.w*b.x-(double)a.x*b.w-(double)a.y*b.z+(double)a.z*b.y;
        double y=(double)a.w*b.y+(double)a.x*b.z-(double)a.y*b.w-(double)a.z*b.x;
        double z=(double)a.w*b.z-(double)a.x*b.y+(double)a.y*b.x-(double)a.z*b.w;
        double w=(double)a.w*b.w+(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;
        return (float)(2*Math.Atan2(Math.Sqrt(x*x+y*y+z*z),Math.Abs(w))*180/Math.PI);
    }
    private void Fail(Exception e){result.failures.Add(e.ToString());Debug.LogException(e);Finish();}
    private void Finish()
    {
        foreach(var merge in FindObjectsOfType<V3HoMergeProbe>())
            if(!merge.Completed||merge.Failure!=null)result.failures.Add(merge.name+": "+(merge.Failure??"probe did not finish"));
        File.WriteAllText(outputPath,JsonUtility.ToJson(result,true));Debug.Log("IR_V3_RUNTIME_DONE "+result.measurements.Count+" failures="+result.failures.Count);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(result.failures.Count==0?0:1);
#endif
    }
}
