using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Disposable Linear GPU host with current legacy lil, all five specialized
// shaders, Ho pipeline and the migration editor subset installed. No Apply.
public static class HoConverterSurfaceChecks
{
    private static string folder;
    private static int checks;
    private static readonly List<Object> transient=new();
    private static readonly List<string> measuredChanges=new();
    private static readonly HoMigrationFamily[] Families=(HoMigrationFamily[])Enum.GetValues(typeof(HoMigrationFamily));
    private static readonly MethodInfo ConvertMethod=AppDomain.CurrentDomain.GetAssemblies()
        .Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter",false)).First(t=>t!=null)
        .GetMethod("Convert",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static);

    public static void Run()
    {
        Require(Application.isBatchMode&&QualitySettings.activeColorSpace==ColorSpace.Linear,"disposable Linear batch host");
        Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"active GPU");
        ShaderUtil.allowAsyncCompilation=false;
        folder="Assets/HoConverterSurface_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        try
        {
            using var fixture=new Fixture();
            Material source=Source();
            Snapshot neutral=fixture.Capture(source);
            foreach(HoMigrationFamily family in Families)
            {
                Material target=Convert(source,family);
                Snapshot actual=fixture.Capture(target);
                CompareRgb(actual.Color,neutral.Color,"neutral four-color albedo "+family);
                CompareRgb(actual.Emission,neutral.Emission,"disabled emission "+family);
                Debug.Log("HO_CONVERTER_GPU_COLOR "+family+" sourceFlags="+Flags(source,"_Color")+" targetFlags="+Flags(target,"_BaseColor")
                    +" sourceGPU="+neutral.Color[0]+" targetGPU="+actual.Color[0]);
            }
            CheckNeutralEmission(fixture,source);
            CheckHsvgAndEmission(fixture,source);
            foreach(string change in measuredChanges)Debug.Log(change);
            Debug.Log("HO_CONVERTER_SURFACE_GPU_OK "+SystemInfo.graphicsDeviceType+" checks="+checks+" measuredChanges="+measuredChanges.Count+" output="+folder);
        }
        finally{foreach(Object obj in transient)if(obj!=null)Object.DestroyImmediate(obj);transient.Clear();}
    }

    private static Material Source()
    {
        var s=new Material(Shader.Find("lilToon")){name="Actual current lil numeric source"};
        s.SetTexture("_MainTex",Map("MainFour",new[]{new Color(.2f,.4f,.6f,.7f),new Color(.7f,.3f,.1f,.8f),new Color(.1f,.8f,.2f,.9f),new Color(.6f,.2f,.8f,1)}));
        s.SetColor("_Color",new Color(.8f,.6f,.4f,1));s.SetFloat("_Cull",0);s.SetFloat("_Metallic",0);s.SetFloat("_Reflectance",.04f);s.SetFloat("_Smoothness",.5f);
        foreach(string toggle in new[]{"_UseEnvironmentLighting","_UseReflection","_UseShadow","_UseMatCap","_UseBacklight","_UseRim","_UseLightRim","_UseRimShade","_UseSSS","_UseAODarkening","_UseEmission"})s.SetFloat(toggle,0);
        s.SetFloat("_AOSource",0);s.SetFloat("_LightMinLimit",0);s.SetFloat("_LightMaxLimit",10);s.SetFloat("_MonochromeLighting",0);
        s.SetFloat("_HoAdditionalLightIntensity",0);s.SetFloat("_GSAAStrength",0);
        AssetDatabase.CreateAsset(s,folder+"/Source.mat");AssetDatabase.SaveAssetIfDirty(s);return s;
    }
    private static Texture2D Map(string name,Color[] colors)
    {
        int side=colors.Length==4?2:1;
        var t=new Texture2D(side,side,TextureFormat.RGBAFloat,false,true){name=name,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Repeat};
        t.SetPixels(colors);t.Apply(false);AssetDatabase.CreateAsset(t,folder+"/"+name+".asset");AssetDatabase.SaveAssetIfDirty(t);return t;
    }
    private static Material Convert(Material source,HoMigrationFamily family)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);
        var differences=new List<string>();var blockers=new List<string>();
        var t=(Material)ConvertMethod.Invoke(null,new object[]{source,family,differences,blockers,folder+"/Derived"});
        Require(t!=null&&blockers.Count==0,"unblocked fixture conversion "+family+": "+string.Join("|",blockers));
        Require(before==EditorJsonUtility.ToJson(source),"source immutable "+family);transient.Add(t);return t;
    }
    private static void Emission(Material source,float influence)
    {
        source.SetFloat("_UseEmission",1);source.SetFloat("_EmissionMainStrength",influence);source.SetFloat("_EmissionBlend",.6f);
        source.SetColor("_EmissionColor",new Color(2,1,.5f,.4f));
        if(source.GetTexture("_EmissionMap")==null)source.SetTexture("_EmissionMap",Map("Emission",new[]{new Color(.4f,.6f,.8f,.5f)}));
        if(source.GetTexture("_EmissionBlendMask")==null)source.SetTexture("_EmissionBlendMask",Map("EmissionMask",new[]{new Color(.5f,.25f,1,.75f)}));
    }
    private static void CheckNeutralEmission(Fixture fixture,Material source)
    {
        Emission(source,0);Snapshot old=fixture.Capture(source);
        foreach(var family in new[]{HoMigrationFamily.Skin,HoMigrationFamily.Eye,HoMigrationFamily.Cloth,HoMigrationFamily.Environment})
        {
            Snapshot actual=fixture.Capture(Convert(source,family));
            CompareRgb(actual.Color,old.Color,"neutral emitting albedo "+family);
            CompareRgb(actual.Emission,old.Emission,"neutral RGBA/mask/Blend/Color alpha radiance "+family,.00075f);
        }
        Emission(source,.25f);old=fixture.Capture(source);
        foreach(var family in new[]{HoMigrationFamily.Skin,HoMigrationFamily.Eye})
        {
            Snapshot actual=fixture.Capture(Convert(source,family));
            CompareRgb(actual.Emission,old.Emission,"neutral live MainStrength radiance "+family,.00075f);
        }
    }
    private static void CheckHsvgAndEmission(Fixture fixture,Material source)
    {
        source.SetFloat("_UseEmission",0);source.SetVector("_MainTexHSVG",new Vector4(0,1,1,1));
        Snapshot oldNeutral=fixture.Capture(source);Snapshot forwardNeutral=fixture.Capture(source,true);
        source.SetVector("_MainTexHSVG",new Vector4(.08f,.7f,1.2f,.8f));
        Snapshot oldHsvg=fixture.Capture(source);Snapshot forwardHsvg=fixture.Capture(source,true);
        // Reference only from actual old raster captures. The legacy controlled
        // diffuse gain cancels; no CPU HSV or Color.gamma/.linear expectation.
        var visibleAlbedo=new Color[4];
        for(int i=0;i<4;i++)
        {
            Require(Mathf.Min(forwardNeutral.Color[i].r,Mathf.Min(forwardNeutral.Color[i].g,forwardNeutral.Color[i].b))>.002f,"positive GPU forward calibration");
            visibleAlbedo[i]=new Color(oldNeutral.Color[i].r*forwardHsvg.Color[i].r/forwardNeutral.Color[i].r,
                oldNeutral.Color[i].g*forwardHsvg.Color[i].g/forwardNeutral.Color[i].g,
                oldNeutral.Color[i].b*forwardHsvg.Color[i].b/forwardNeutral.Color[i].b,1);
        }
        CompareRgb(oldHsvg.Color,oldNeutral.Color,"legacy numeric producer omits Forward HSVG");
        foreach(HoMigrationFamily family in Families)
        {
            Snapshot actual=fixture.Capture(Convert(source,family));
            CompareRgb(actual.Color,visibleAlbedo,"HSVG bake matches GPU visible Forward main chain "+family);
            float delta=MaximumDelta(actual.Color,oldHsvg.Color);
            Require(delta>.01f,"known legacy/new numeric HSVG delta is observed "+family);
            measuredChanges.Add("HO_CONVERTER_HSVG_NUMERIC_CHANGE "+family+" maxDelta="+delta+" oldSB="+oldHsvg.Color[0]+" newSB="+actual.Color[0]+" oldForwardMainGPU="+visibleAlbedo[0]);
        }
        Emission(source,.25f);Snapshot oldEmission=fixture.Capture(source);Snapshot forwardWithEmission=fixture.Capture(source,true);
        var forwardRadiance=new Color[4];for(int i=0;i<4;i++)forwardRadiance[i]=forwardWithEmission.Color[i]-forwardHsvg.Color[i];
        foreach(var family in new[]{HoMigrationFamily.Skin,HoMigrationFamily.Eye})
        {
            Snapshot actual=fixture.Capture(Convert(source,family));
            CompareRgb(actual.Emission,forwardRadiance,"HSVG MainStrength matches actual Forward emission delta "+family,.00075f);
            float delta=MaximumDelta(actual.Emission,oldEmission.Emission);
            Require(delta>.0005f,"known HSVG/MainStrength numeric delta observed "+family);
            measuredChanges.Add("HO_CONVERTER_EMISSION_NUMERIC_CHANGE "+family+" maxDelta="+delta+" oldSB="+oldEmission.Emission[0]+" newSB="+actual.Emission[0]+" oldForwardEmissionGPU="+forwardRadiance[0]);
        }
    }
    private static ShaderPropertyFlags Flags(Material material,string property)=>material.shader.GetPropertyFlags(material.shader.FindPropertyIndex(property));
    private static float MaximumDelta(Color[] a,Color[] b){float result=0;for(int i=0;i<a.Length;i++)result=Mathf.Max(result,Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b));return result;}
    private static void CompareRgb(Color[] actual,Color[] expected,string label,float tolerance=.0025f)
    {
        for(int i=0;i<4;i++)
        {
            float delta=Mathf.Max(Mathf.Abs(actual[i].r-expected[i].r),Mathf.Abs(actual[i].g-expected[i].g),Mathf.Abs(actual[i].b-expected[i].b));
            Require(delta<tolerance,label+" texel="+i+" actual="+actual[i]+" expected="+expected[i]+" maxDelta="+delta);
        }
    }
    private static void Require(bool value,string message){checks++;if(!value)throw new InvalidOperationException("Converter GPU surface: "+message);}
    private readonly struct Snapshot
    {
        internal readonly Color[] Color,Emission;
        internal Snapshot(Color[] color,Color[] emission){Color=color;Emission=emission;}
    }
    private sealed class Fixture:IDisposable
    {
        private const int Size=66;
        private readonly Camera camera;
        private readonly MeshRenderer renderer;
        private readonly Mesh mesh;
        private readonly GameObject lightObject;
        private readonly UniversalRendererData data;
        private readonly UniversalRenderPipelineAsset pipeline;
        private readonly HoConverterSurfaceCaptureFeature feature;
        private readonly RenderPipelineAsset priorDefault=GraphicsSettings.defaultRenderPipeline,priorQuality=QualitySettings.renderPipeline;
        private readonly Light priorSun=RenderSettings.sun;
        private readonly bool priorFog=RenderSettings.fog;
        private readonly RenderTexture[] colors=new RenderTexture[7];
        private readonly RenderTexture depth,destination;
        private readonly Texture2D readback;
        internal Fixture()
        {
            mesh=new Mesh{vertices=new[]{new Vector3(-1,-1),new Vector3(-1,1),new Vector3(1,1),new Vector3(1,-1)},
                normals=Enumerable.Repeat(Vector3.back,4).ToArray(),tangents=Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray(),
                uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right},triangles=new[]{0,1,2,0,2,3}};
            var quad=new GameObject("Converter numeric surface");quad.layer=31;quad.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=quad.AddComponent<MeshRenderer>();renderer.SetShaderUserValue(0x101);
            camera=new GameObject("Converter numeric camera").AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,0,-3);
            camera.orthographic=true;camera.orthographicSize=1;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=10;
            camera.cullingMask=1<<31;camera.allowHDR=true;camera.allowMSAA=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            lightObject=new GameObject("Controlled legacy Forward calibration light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.color=Color.white;light.intensity=1;light.shadows=LightShadows.None;RenderSettings.sun=light;RenderSettings.fog=false;
            data=ScriptableObject.CreateInstance<UniversalRendererData>();feature=ScriptableObject.CreateInstance<HoConverterSurfaceCaptureFeature>();data.rendererFeatures.Add(feature);
            pipeline=UniversalRenderPipelineAsset.Create(data);pipeline.supportsHDR=true;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            for(int i=0;i<7;i++){colors[i]=new RenderTexture(Size,Size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);Require(colors[i].Create(),"create MRT"+i);}
            depth=new RenderTexture(Size,Size,24,RenderTextureFormat.Depth);Require(depth.Create(),"create native shared depth");
            destination=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);Require(destination.Create(),"create private camera target");
            readback=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            // Select the active SRP before inspecting ShaderData/pass metadata.
            renderer.enabled=false;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});
            renderer.enabled=true;
        }
        internal Snapshot Capture(Material material,bool visibleForward=false)
        {
            renderer.sharedMaterial=material;
            string mode="HoSurfaceBuffer";
            if(visibleForward)
            {
                int subshader=ShaderUtil.GetShaderData(material.shader).ActiveSubshaderIndex;
                int forward=material.FindPass("FORWARD");
                Require(forward>=0,"actual legacy Forward pass");
                mode=material.shader.FindPassTagValue(subshader,forward,new ShaderTagId("LightMode")).name;
                Debug.Log("HO_CONVERTER_FORWARD_PASS "+AssetDatabase.GetAssetPath(material.shader)+" name="+material.GetPassName(forward)+" tag="+mode);
                if(string.IsNullOrEmpty(mode))mode="SRPDefaultUnlit";
            }
            Compile(material,"DepthOnly");Compile(material,mode);
            using var capture=new HoConverterSurfaceCaptureFeature.CapturePass(colors,depth,mode);feature.Active=capture;
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});}
            finally{feature.Active=null;}
            Color[] color=Read(colors[0]);Color[] emission=visibleForward?new Color[4]:Read(colors[6]);
            if(!visibleForward)foreach(Color value in emission)Require(Mathf.Abs(value.a-1)<.002f,"native surface writer marker "+material.shader.name);
            return new Snapshot(color,emission);
        }
        private static void Compile(Material material,string mode)
        {
            int subshader=ShaderUtil.GetShaderData(material.shader).ActiveSubshaderIndex;
            int index=Enumerable.Range(0,material.passCount).Where(i=>material.shader.FindPassTagValue(subshader,i,new ShaderTagId("LightMode")).name==mode).DefaultIfEmpty(-1).First();
            Require(index>=0,"native pass "+material.shader.name+" "+mode+" active="+subshader+" count="+material.passCount);ShaderUtil.CompilePass(material,index,true);
            Require(ShaderUtil.IsPassCompiled(material,index)&&!ShaderUtil.ShaderHasError(material.shader),"exact native variant compiled "+mode);
        }
        private Color[] Read(RenderTexture target)
        {
            var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,Size,Size),0,0,false);
                // Pixel centers are exactly .25/.75 in both axes for Size66.
                return new[]{readback.GetPixel(16,16),readback.GetPixel(49,16),readback.GetPixel(16,49),readback.GetPixel(49,49)};
            }finally{RenderTexture.active=previous;}
        }
        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline=priorDefault;QualitySettings.renderPipeline=priorQuality;RenderSettings.sun=priorSun;RenderSettings.fog=priorFog;
            foreach(var target in colors)Object.DestroyImmediate(target);Object.DestroyImmediate(depth);Object.DestroyImmediate(destination);Object.DestroyImmediate(readback);
            Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(renderer.gameObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(pipeline);Object.DestroyImmediate(feature);Object.DestroyImmediate(data);
        }
    }
}
public sealed class HoConverterSurfaceCaptureFeature:ScriptableRendererFeature
{
    public CapturePass Active;
    public override void Create(){}
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){if(Active!=null)renderer.EnqueuePass(Active);}
    public sealed class CapturePass:ScriptableRenderPass,IDisposable
    {
        private readonly RTHandle[] colors;
        private readonly RTHandle depth;
        private readonly ShaderTagId mode;
        public CapturePass(RenderTexture[] colors,RenderTexture depth,string mode)
        {this.colors=colors.Select(t=>RTHandles.Alloc(t)).ToArray();this.depth=RTHandles.Alloc(depth);this.mode=new ShaderTagId(mode);renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
        {
            var rendering=frame.Get<UniversalRenderingData>();var camera=frame.Get<UniversalCameraData>();var light=frame.Get<UniversalLightData>();
            var filter=new FilteringSettings(RenderQueueRange.opaque,1<<31);
            var depthDrawing=RenderingUtils.CreateDrawingSettings(new ShaderTagId("DepthOnly"),rendering,camera,light,SortingCriteria.CommonOpaque);
            var surfaceDrawing=RenderingUtils.CreateDrawingSettings(mode,rendering,camera,light,SortingCriteria.CommonOpaque);
            using var builder=graph.AddRasterRenderPass<Data>("Source/converted numeric "+mode.name,out var d);
            d.depth=graph.CreateRendererList(new RendererListParams(rendering.cullResults,depthDrawing,filter));
            d.surface=graph.CreateRendererList(new RendererListParams(rendering.cullResults,surfaceDrawing,filter));
            builder.UseRendererList(d.depth);builder.UseRendererList(d.surface);
            for(int i=0;i<colors.Length;i++)builder.SetRenderAttachment(graph.ImportTexture(colors[i]),i,AccessFlags.WriteAll);
            builder.SetRenderAttachmentDepth(graph.ImportTexture(depth),AccessFlags.WriteAll);builder.AllowPassCulling(false);
            builder.SetRenderFunc(static(Data d,RasterGraphContext ctx)=>
            {ctx.cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);ctx.cmd.DrawRendererList(d.depth);ctx.cmd.DrawRendererList(d.surface);});
        }
        public void Dispose(){foreach(var target in colors)RTHandles.Release(target);RTHandles.Release(depth);}
        private sealed class Data{public RendererListHandle depth,surface;}
    }
}
