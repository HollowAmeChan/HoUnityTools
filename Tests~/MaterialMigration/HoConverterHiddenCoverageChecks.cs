using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Uses the same disposable RendererList/MRT fixture as the numeric color test.
// Tests compiled native passes, with a visible witness and an empty-renderer
// baseline; no production material or Renderer is touched.
public static class HoConverterHiddenCoverageChecks
{
    private static int checks;
    public static void Run()
    {
        Require(Application.isBatchMode&&QualitySettings.activeColorSpace==ColorSpace.Linear,"private Linear GPU host");
        ShaderUtil.allowAsyncCompilation=false;
        var source=new Material(Shader.Find("lilToon"));source.SetFloat("_Invisible",1);
        var blockers=new List<string>();var differences=new List<string>();
        Type converter=typeof(HoConverterSurfaceChecks).Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter");
        var hidden=(Material)converter.GetMethod("Convert",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source,HoMigrationFamily.Cloth,differences,blockers,"Assets/NoHiddenBake",new[]{"PreserveHiddenCoverage"}});
        Require(!blockers.Any(),"bounded hidden proposal: "+string.Join("|",blockers));
        var visible=new Material(hidden);Color tint=visible.GetColor("_BaseColor");tint.a=1;visible.SetColor("_BaseColor",tint);
        Type fixtureType=typeof(HoConverterSurfaceChecks).GetNestedType("Fixture",BindingFlags.NonPublic);
        var fixture=(IDisposable)Activator.CreateInstance(fixtureType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,Array.Empty<object>(),null);
        T Field<T>(string name)=>(T)fixtureType.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
        var renderer=Field<MeshRenderer>("renderer");var camera=Field<Camera>("camera");var colors=Field<RenderTexture[]>("colors");var depth=Field<RenderTexture>("depth");var destination=Field<RenderTexture>("destination");var feature=Field<HoConverterSurfaceCaptureFeature>("feature");
        var readback=new Texture2D(colors[0].width,colors[0].height,TextureFormat.RGBAFloat,false,true);
        Shader depthShader=ShaderUtil.CreateShaderAsset(DepthShader,false);var depthMaterial=new Material(depthShader);
        RenderTexture depthColor=RenderTexture.GetTemporary(depth.width,depth.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        float[] ReadDepth()
        {
            depthMaterial.SetTexture("_DepthTex",depth,RenderTextureSubElement.Depth);Graphics.Blit(Texture2D.whiteTexture,depthColor,depthMaterial);RenderTexture.active=depthColor;readback.ReadPixels(new Rect(0,0,depth.width,depth.height),0,0,false);readback.Apply(false,false);return readback.GetPixels().Select(v=>v.r).ToArray();
        }
        float[] Capture(Material material,string mode,bool enabled,out Color[][] mrt)
        {
            renderer.sharedMaterial=material;renderer.enabled=enabled;
            using(var pass=new HoConverterSurfaceCaptureFeature.CapturePass(colors,depth,mode))
            {feature.Active=pass;try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});}finally{feature.Active=null;}}
            mrt=colors.Select(rt=>{RenderTexture.active=rt;readback.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);readback.Apply(false,false);return readback.GetPixels();}).ToArray();return ReadDepth();
        }
        RenderTexture previous=RenderTexture.active;
        Vector4 priorMeta=Shader.GetGlobalVector("unity_MetaFragmentControl");float priorBoost=Shader.GetGlobalFloat("unity_OneOverOutputBoost"),priorMaximum=Shader.GetGlobalFloat("unity_MaxOutputValue");
        try
        {
            Shader.SetGlobalVector("unity_MetaFragmentControl",new Vector4(1,0,0,0));Shader.SetGlobalFloat("unity_OneOverOutputBoost",1);Shader.SetGlobalFloat("unity_MaxOutputValue",65504);
            foreach(string passName in new[]{"Forward","DepthOnly","HO_SURFACE_BUFFER","HO_OBJECT_BUFFER","HO_GEOMETRY_BUFFER","ShadowCaster","Meta"})
            {
                int pass=hidden.FindPass(passName);Require(pass>=0,"actual target pass "+passName);ShaderUtil.CompilePass(hidden,pass,true);ShaderUtil.CompilePass(visible,pass,true);
                int subshader=ShaderUtil.GetShaderData(hidden.shader).ActiveSubshaderIndex;string tag=hidden.shader.FindPassTagValue(subshader,pass,new ShaderTagId("LightMode")).name;
                float[] empty=Capture(hidden,tag,false,out _);float[] actual=Capture(hidden,tag,true,out Color[][] mrt);
                Require(actual.SequenceEqual(empty),"hidden writes no depth in "+passName);
                foreach(Color[] values in mrt)Require(values.All(c=>c.Equals(Color.clear)),"hidden writes no MRT color/owner/emission in "+passName);
                float[] witness=Capture(visible,tag,true,out Color[][] witnessMrt);
                Require(!witness.SequenceEqual(empty)||witnessMrt.Any(v=>v.Any(c=>!c.Equals(Color.clear))),"visible control proves submitted geometry "+passName);
            }
            Require(!ShaderUtil.ShaderHasError(hidden.shader),"all exact native variants compile");
            Debug.Log("HO_HIDDEN_COVERAGE_GPU_OK checks="+checks+" GPU="+SystemInfo.graphicsDeviceType);
        }
        finally
        {
            Shader.SetGlobalVector("unity_MetaFragmentControl",priorMeta);Shader.SetGlobalFloat("unity_OneOverOutputBoost",priorBoost);Shader.SetGlobalFloat("unity_MaxOutputValue",priorMaximum);
            RenderTexture.active=previous;fixture.Dispose();RenderTexture.ReleaseTemporary(depthColor);Object.DestroyImmediate(readback);Object.DestroyImmediate(depthMaterial);Object.DestroyImmediate(depthShader);Object.DestroyImmediate(source);Object.DestroyImmediate(hidden);Object.DestroyImmediate(visible);
        }
    }
    private static void Require(bool value,string text){checks++;if(!value)throw new Exception(text);}
    private const string DepthShader=@"Shader ""Hidden/HoMigration/HiddenDepthReadback"" { Properties { _DepthTex(""Depth"",2D)=""black""{} } SubShader { Pass { Cull Off ZWrite Off ZTest Always
HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D<float> _DepthTex;
float4 frag(v2f_img input):SV_Target{return _DepthTex.Load(int3(int2(input.pos.xy),0)).xxxx;}
ENDHLSL
} } }";
}
