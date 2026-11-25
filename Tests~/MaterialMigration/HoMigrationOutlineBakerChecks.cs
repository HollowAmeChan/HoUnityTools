using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Disposable current-lil GPU host. Copy HoConverterSurfaceChecks.cs beside this
// test for its real RendererList fixture, used as an independent old-outline
// visible HSVG witness. Formula expectations sample source GPU textures first;
// no source importer/readability flag is changed by a bake.
public static class HoMigrationOutlineBakerChecks
{
    private static int checks;
    private static string folder;
    private static readonly List<Object> Owned=new();
    private static Shader probeShader;
    private static Material probe;
    public static void Run()
    {
        Require(Application.isBatchMode&&QualitySettings.activeColorSpace==ColorSpace.Linear,"private Linear GPU host");
        ShaderUtil.allowAsyncCompilation=false;
        folder="Assets/HoOutlineBake_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        probeShader=ShaderUtil.CreateShaderAsset(ProbeShader,false);Owned.Add(probeShader);probe=new Material(probeShader);Owned.Add(probe);ShaderUtil.CompilePass(probe,0,true);Require(!ShaderUtil.ShaderHasError(probeShader),"independent GPU sampler probe compiled");
        using(var profile=HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.ExactPreview))
        {
            CheckCoverage();CheckOutlineTone();CheckEmission();CheckDecisions();
        }
        CheckProductionReload();
        Require(!ShaderUtil.ShaderHasError(probeShader),"GPU source/readback probe compiled");
        Debug.Log("HO_OUTLINE_BAKER_GPU_OK checks="+checks+" GPU="+SystemInfo.graphicsDeviceType+" outputs="+folder);
        foreach(Object value in Owned)if(value!=null)Object.DestroyImmediate(value);
    }
    private static Material Source(string name,string shader="Hidden/lilToonTransparentOutline")
    {
        var m=new Material(Shader.Find(shader)){name=name};Require(m.shader!=null,"actual legacy source shader");AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");
        m.SetTexture("_MainTex",Map(name+"_main",new Color(.5f,.6f,.7f,.03f),new Color(.2f,.3f,.4f,.97f)));
        m.SetColor("_Color",new Color(1,1,1,.07f));m.SetColor("_OutlineColor",new Color(1,1,1,.6f));m.SetFloat("_OutlineWidth",.08f);m.SetFloat("_OutlineZBias",0);m.SetFloat("_OutlineDeleteMesh",0);
        m.SetFloat("_UseReflection",0);m.SetFloat("_UseSSS",0);m.SetFloat("_UseEmission",0);m.SetFloat("_Invisible",0);m.SetFloat("_AOSource",0);m.SetFloat("_Metallic",0);m.SetFloat("_Reflectance",.04f);
        m.SetFloat("_AlphaMaskMode",0);m.SetFloat("_AlphaMaskScale",.7f);m.SetFloat("_AlphaMaskValue",.1f);m.SetFloat("_Cutoff",0);m.SetFloat("_SubpassCutoff",0);m.SetFloat("_ZWrite",0);m.SetFloat("_lilOITEnabled",0);
        m.SetFloat("_UseParallax",0);m.SetFloat("_UseDither",0);m.SetVector("_DissolveParams",Vector4.zero);m.SetVector("_DistanceFade",Vector4.zero);m.SetVector("_MainTexHSVG",new Vector4(0,1,1,1));m.SetFloat("_MainGradationStrength",0);m.SetFloat("_HoTesionEnabled",0);
        m.SetVector("_OutlineTexHSVG",new Vector4(0,1,1,1));m.SetFloat("_OutlineEnableLighting",0);m.SetColor("_OutlineLitColor",Color.clear);m.SetFloat("_OutlineShadowStrength",0);
        return m;
    }
    private static Texture2D Map(string name,params Color[] pixels)
    {
        var t=new Texture2D(pixels.Length,1,TextureFormat.RGBAFloat,false,true){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};t.SetPixels(pixels);t.Apply(false);AssetDatabase.CreateAsset(t,folder+"/"+name+".asset");return t;
    }
    private static Texture2D Imported(string name)
    {
        var t=new Texture2D(4,4,TextureFormat.RGBA32,false,true);Owned.Add(t);t.SetPixels(Enumerable.Range(0,16).Select(i=>new Color((i%4+.5f)/4,.25f,(i/4+.5f)/4,(i+1f)/17)).ToArray());t.Apply(false);
        string path=folder+"/"+name+".png";File.WriteAllBytes(path,t.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.isReadable=false;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Compressed;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
        var result=AssetDatabase.LoadAssetAtPath<Texture2D>(path);Require(!result.isReadable&&result.isDataSRGB,"actual unreadable sRGB import");Debug.Log("HO_OUTLINE_SOURCE_FORMAT "+result.format);return result;
    }
    private static Texture2D Bake(Material source,string method,bool premultiply=false,bool native=false)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);Hash128 dep=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source));var blockers=new List<string>();
        var result=method=="Coverage"?HoMigrationTextureBaker.OutlineCoverage(source,folder+"/Derived",blockers):method=="Color"?HoMigrationTextureBaker.OutlineColor(source,folder+"/Derived",blockers):HoMigrationTextureBaker.EmissionRgba(source,folder+"/Derived",premultiply,blockers,native);
        Require(result!=null&&!blockers.Any(),method+" bake accepted: "+string.Join("|",blockers));Require(before==EditorJsonUtility.ToJson(source)&&dep==AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)),"bake source and dependency hash unchanged");
        var recipe=HoMigrationTextureBaker.ReadRecipe(result);Require(recipe!=null&&recipe.outputGuid==AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(result)),"owned output recipe/GUID");return result;
    }
    private static void Rejected(Material source,string method,bool premultiply=false,bool native=false)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);var blockers=new List<string>();var result=method=="Coverage"?HoMigrationTextureBaker.OutlineCoverage(source,folder+"/Derived",blockers):HoMigrationTextureBaker.EmissionRgba(source,folder+"/Derived",premultiply,blockers,native);
        Require(result==null&&blockers.Any(),method+" unsupported case stays blocked");Require(before==EditorJsonUtility.ToJson(source),"rejected bake leaves source unchanged");
    }
    private static Color[] Sample(Texture texture,int width,int height,Vector4 st,Texture sampler=null,bool own=true,int mip=0)
    {
        var previous=RenderTexture.active;bool priorWrite=GL.sRGBWrite;var rt=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var rb=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
        try{probe.SetTexture("_ProbeTex",texture);probe.SetTexture("_SamplerTex",sampler??texture);probe.SetVector("_ProbeST",st);probe.SetFloat("_Own",own?1:0);probe.SetFloat("_Mip",mip);GL.sRGBWrite=false;Graphics.Blit(Texture2D.whiteTexture,rt,probe);RenderTexture.active=rt;rb.ReadPixels(new Rect(0,0,width,height),0,0,false);rb.Apply(false,false);return rb.GetPixels();}
        finally{RenderTexture.active=previous;GL.sRGBWrite=priorWrite;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(rb);}
    }
    private static readonly Vector4 Identity=new Vector4(1,1,0,0);
    private static Vector4 ST(Material m,string property){Vector2 s=m.GetTextureScale(property),o=m.GetTextureOffset(property);return new Vector4(s.x,s.y,o.x,o.y);}
    private static Vector4 Chain(Vector4 a,Vector4 b)=>new Vector4(a.x*b.x,a.y*b.y,a.z*b.x+b.z,a.w*b.y+b.w);
    private static void CheckCoverage()
    {
        var source=Source("coverage");var outline=Map("outline_alpha",new Color(.1f,.8f,.3f,.1f),new Color(.8f,.2f,.4f,.3f),new Color(.4f,.6f,.8f,.6f),new Color(.2f,.4f,.6f,.9f));outline.filterMode=FilterMode.Bilinear;AssetDatabase.SaveAssetIfDirty(outline);
        var mask=Map("mask_r",new Color(.1f,.9f,.9f,1),new Color(.8f,.2f,.4f,0),new Color(.3f,.8f,.2f,1),new Color(.9f,0,0,0));source.SetTexture("_OutlineTex",outline);source.SetTexture("_AlphaMask",mask);
        source.SetTextureScale("_MainTex",new Vector2(3,2));source.SetTextureOffset("_MainTex",new Vector2(.4f,.5f));source.SetTextureScale("_OutlineTex",new Vector2(2,1));source.SetTextureOffset("_OutlineTex",new Vector2(.25f,0));source.SetTextureOffset("_AlphaMask",new Vector2(.25f,0));
        foreach(int mode in new[]{0,1,2,3,4})
        {
            source.SetFloat("_AlphaMaskMode",mode);var result=Bake(source,"Coverage");var actual=Sample(result,result.width,result.height,Identity);
            var a=Sample(outline,result.width,result.height,ST(source,"_OutlineTex"));var b=Sample(mask,result.width,result.height,Chain(ST(source,"_OutlineTex"),ST(source,"_AlphaMask")),source.GetTexture("_MainTex"),false);
            for(int i=0;i<actual.Length;i++)
            {
                float initial=a[i].a*.6f,weight=Mathf.Clamp01(b[i].r*.7f+.1f);float expected=mode switch{0=>initial,1=>weight,2=>initial*weight,3=>Mathf.Clamp01(initial+weight),_=>Mathf.Clamp01(initial-weight)};
                Near(actual[i].r,Mathf.Clamp01(expected),.00002f,"coverage mode"+mode+" texel"+i);
            }
            var recipe=HoMigrationTextureBaker.ReadRecipe(result);Require(recipe.destinationST==Identity&&recipe.operation=="OutlineCoverage","coverage is independent rawUV0/identity ST");
            source.SetColor("_Color",new Color(1,1,1,.92f));source.SetTexture("_MainTex",Map("different_main_alpha_"+mode,new Color(1,1,1,1),new Color(1,1,1,0)));
            var changed=Bake(source,"Coverage");Same(Sample(changed,changed.width,changed.height,Identity),actual,.00002f,"MainTex/Color alpha cannot affect outline coverage");
            source.SetColor("_Color",new Color(1,1,1,.07f));
        }
        source.SetFloat("_UseDither",1);Rejected(source,"Coverage");source.SetFloat("_UseDither",0);source.SetVector("_DissolveParams",new Vector4(1,0,0,0));Rejected(source,"Coverage");source.SetVector("_DissolveParams",Vector4.zero);
        source.SetTexture("_MainTex",null);source.SetFloat("_AlphaMaskMode",2);Rejected(source,"Coverage");
        source.SetFloat("_AlphaMaskMode",0);Bake(source,"Coverage"); // no mask sample, so no Main sampler to guess
    }
    private static void CheckOutlineTone()
    {
        var source=Source("outline_tone");var texture=Map("tone_four",new Color(.2f,.4f,.7f,1),new Color(.8f,.2f,.4f,1),new Color(.4f,.7f,.2f,1),new Color(1.5f,.3f,.5f,1));
        // 2x2 texture makes the old camera fixture's four UV probes exact texel centers.
        var square=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};square.SetPixels(texture.GetPixels());square.Apply(false);AssetDatabase.CreateAsset(square,folder+"/tone_square.asset");source.SetTexture("_OutlineTex",square);source.SetColor("_OutlineColor",Color.white);source.SetVector("_OutlineTexHSVG",new Vector4(.13f,.65f,1.2f,.8f));
        source.SetFloat("_OutlineWidth",0);source.SetFloat("_OutlineCull",0);source.SetFloat("_OutlineZTest",8);source.SetFloat("_OutlineZWrite",0);source.SetFloat("_OutlineSrcBlend",1);source.SetFloat("_OutlineDstBlend",0);source.SetFloat("_OutlineSrcBlendAlpha",1);source.SetFloat("_OutlineDstBlendAlpha",0);source.SetFloat("_UseShadow",0);
        var baked=Bake(source,"Color");Color[] actual=Sample(baked,2,2,Identity);Color[] witness=VisibleOutline(source);
        Same(actual,witness,.0025f,"HSVG matches actual legacy visible outline (no CPU HSV oracle)");foreach(Color c in actual)Near(c.a,1,.00001f,"color bake alpha always1");
        source.SetTextureScale("_OutlineTex",new Vector2(2,1));source.SetTextureOffset("_OutlineTex",new Vector2(.25f,0));baked=Bake(source,"Color");Require(HoMigrationTextureBaker.ReadRecipe(baked).destinationST==ST(source,"_OutlineTex"),"color remains outline texture UV; destination preserves OutlineST");
        var compressed=Imported("outline_srgb_compressed");source.SetTexture("_OutlineTex",compressed);source.SetVector("_OutlineTexHSVG",new Vector4(0,1,1,1));var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(compressed));string before=EditorJsonUtility.ToJson(importer);
        baked=Bake(source,"Color");var src=Sample(compressed,baked.width,baked.height,Identity);actual=Sample(baked,baked.width,baked.height,Identity);
        for(int i=0;i<actual.Length;i++){Near(actual[i].r,src[i].r,.00005f,"sRGB decoded R");Near(actual[i].g,src[i].g,.00005f,"sRGB decoded G");Near(actual[i].b,src[i].b,.00005f,"sRGB decoded B");Near(actual[i].a,1,.00001f,"compressed source alpha not copied into color");}
        Require(!compressed.isReadable&&before==EditorJsonUtility.ToJson(importer),"compressed source importer/readability untouched");
    }
    private static Color[] VisibleOutline(Material source)
    {
        Type fixtureType=typeof(HoConverterSurfaceChecks).GetNestedType("Fixture",BindingFlags.NonPublic);var fixture=(IDisposable)Activator.CreateInstance(fixtureType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,Array.Empty<object>(),null);
        T Field<T>(string name)=>(T)fixtureType.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(fixture);
        try
        {
            var renderer=Field<MeshRenderer>("renderer");renderer.sharedMaterial=source;source.renderQueue=2500;
            int pass=source.FindPass("FORWARD_OUTLINE");Require(pass>=0,"old visible outline pass exists");ShaderUtil.CompilePass(source,pass,true);int subshader=ShaderUtil.GetShaderData(source.shader).ActiveSubshaderIndex;string mode=source.shader.FindPassTagValue(subshader,pass,new ShaderTagId("LightMode")).name;
            var feature=Field<HoConverterSurfaceCaptureFeature>("feature");var colors=Field<RenderTexture[]>("colors");using(var capture=new HoConverterSurfaceCaptureFeature.CapturePass(colors,Field<RenderTexture>("depth"),mode))
            {feature.Active=capture;try{RenderPipeline.SubmitRenderRequest(Field<Camera>("camera"),new UniversalRenderPipeline.SingleCameraRequest{destination=Field<RenderTexture>("destination")});}finally{feature.Active=null;}}
            var old=RenderTexture.active;var read=new Texture2D(66,66,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=colors[0];read.ReadPixels(new Rect(0,0,66,66),0,0,false);return new[]{read.GetPixel(16,16),read.GetPixel(49,16),read.GetPixel(16,49),read.GetPixel(49,49)};}
            finally{RenderTexture.active=old;Object.DestroyImmediate(read);}
        }
        finally{fixture.Dispose();}
    }
    private static Material EmissionSource(string name)
    {
        var source=Source(name,"lilToon");source.SetTexture("_MainTex",null);source.SetColor("_Color",Color.white);source.SetFloat("_UseEmission",1);source.SetFloat("_EmissionBlendMode",1);source.SetFloat("_EmissionBlend",.7f);source.SetFloat("_EmissionMainStrength",.25f);
        source.SetTexture("_EmissionMap",Map(name+"_hdr",new Color(8,1,.5f,.9f),new Color(.2f,3,12,.7f)));source.SetTexture("_EmissionBlendMask",Map(name+"_weight",new Color(1,.5f,.2f,1),new Color(.3f,1,.8f,.4f)));
        source.SetVector("_EmissionMap_ScrollRotate",Vector4.zero);source.SetVector("_EmissionBlendMask_ScrollRotate",Vector4.zero);source.SetFloat("_EmissionMap_UVMode",0);source.SetFloat("_EmissionUseGrad",0);source.SetFloat("_EmissionFluorescence",0);source.SetFloat("_EmissionParallaxDepth",0);source.SetVector("_EmissionBlink",Vector4.zero);return source;
    }
    private static void CheckEmission()
    {
        var source=EmissionSource("unbound_emission");source.SetTextureScale("_EmissionMap",new Vector2(2,1));source.SetTextureOffset("_EmissionMap",new Vector2(.5f,0));source.SetTextureOffset("_EmissionBlendMask",new Vector2(.5f,0));Rejected(source,"Emission",true);
        foreach(bool premultiply in new[]{false,true})
        {
            var result=Bake(source,"Emission",premultiply,true);var actual=Sample(result,result.width,result.height,Identity);var a=Sample(source.GetTexture("_EmissionMap"),result.width,result.height,ST(source,"_EmissionMap"));var b=Sample(source.GetTexture("_EmissionBlendMask"),result.width,result.height,ST(source,"_EmissionBlendMask"));
            for(int i=0;i<actual.Length;i++){Color expected=a[i]*b[i];expected.a*=.7f;if(premultiply)expected=new Color(expected.r*expected.a,expected.g*expected.a,expected.b*expected.a,1);Same(new[]{actual[i]},new[]{expected},.00005f,"native emission own samplers/HDR/weight");}
            Require(actual.Any(c=>Mathf.Max(c.r,c.g,c.b)>1),"HDR radiance is not clamped to UNorm");Require(HoMigrationTextureBaker.ReadRecipe(result).parameters.Any(s=>s.Contains("explicit native mask-own sampler")),"explicit native fallback appears in recipe");
        }
        source.SetVector("_MainTexHSVG",new Vector4(0,1,1,2));Rejected(source,"Emission",true,true);source.SetVector("_MainTexHSVG",new Vector4(0,1,1,1));
        source.SetColor("_Color",new Color(.99f,1,1,1));Rejected(source,"Emission",true,true);source.SetColor("_Color",Color.white);
        source.SetFloat("_MainGradationStrength",.1f);Rejected(source,"Emission",true,true);source.SetFloat("_MainGradationStrength",0);
        source.SetFloat("_HoTesionEnabled",1);Rejected(source,"Emission",true,true);source.SetFloat("_HoTesionEnabled",0);
        source.SetFloat("_EmissionMap_UVMode",1);Rejected(source,"Emission",true,true);source.SetFloat("_EmissionMap_UVMode",0);source.SetVector("_EmissionBlendMask_ScrollRotate",new Vector4(.1f,0,0,0));Rejected(source,"Emission",true,true);
    }
    private static Material Convert(Material source,string family,string[] decisions,out List<string> blockers,out List<string> differences)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);blockers=new List<string>();differences=new List<string>();
        Type converter=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter",false)).First(t=>t!=null);Type f=converter.Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoMigrationFamily");
        var target=(Material)converter.GetMethod("Convert",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source,Enum.Parse(f,family),differences,blockers,folder+"/Derived",decisions});Require(target!=null,"converter preview exists");Owned.Add(target);Require(before==EditorJsonUtility.ToJson(source),"converter leaves source unchanged");Require(!Has(blockers,"CONVERSION_ERROR"),"converter implementation succeeds: "+string.Join("|",blockers));return target;
    }
    private static bool Has(List<string> values,string code)=>values.Any(v=>v.StartsWith(code+":",StringComparison.Ordinal));
    private static void CheckDecisions()
    {
        var source=EmissionSource("emission_decision");Convert(source,"Cloth",null,out var blockers,out _);Require(blockers.Any(s=>s.StartsWith("EmissionRgba:")),"default unbound emission sampler remains strict");
        Convert(source,"Cloth",new[]{"UseNativeUnboundEmissionSampler"},out blockers,out var differences);Require(!blockers.Any(s=>s.StartsWith("EmissionRgba:"))&&!Has(blockers,"MODEL_DECISION")&&Has(differences,"MODEL_DECISION_UseNativeUnboundEmissionSampler"),"only named native unbound sampler accepted");
        Convert(source,"Eye",new[]{"UseNativeUnboundEmissionSampler"},out blockers,out _);Require(Has(blockers,"MODEL_DECISION"),"unbound sampler code is Cloth-only");source.SetTexture("_MainTex",Map("bound_main",Color.white));Convert(source,"Cloth",new[]{"UseNativeUnboundEmissionSampler"},out blockers,out _);Require(Has(blockers,"MODEL_DECISION"),"unbound sampler decision cannot apply to bound Main");
        source=Source("transparent_outline_decision");source.SetTexture("_OutlineTex",Map("decision_outline",new Color(.2f,.3f,.4f,.2f),new Color(.5f,.6f,.7f,.8f)));
        foreach(string family in new[]{"Cloth","Hair"})
        {
            Convert(source,family,null,out blockers,out _);Require(Has(blockers,"TRANSPARENT_OUTLINE"),"transparent shell strict by default "+family);
            var target=Convert(source,family,new[]{"UseNativeTransparentOutline"},out blockers,out differences);Require(!Has(blockers,"TRANSPARENT_OUTLINE")&&!Has(blockers,"MODEL_DECISION")&&!Has(blockers,"OUTLINE_NATIVE_CAPABILITY"),"native transparent outline capability "+family+": "+string.Join("|",blockers));
            Require(Has(differences,"MODEL_DECISION_UseNativeTransparentOutline")&&target.GetTexture("_OutlineCoverageMap")!=null,"independent native shell coverage assigned "+family);Near(target.GetColor("_OutlineColor").a,1,.00001f,"baked alpha weight not multiplied again "+family);
            source.SetFloat("_UseDither",1);Convert(source,family,new[]{"UseNativeTransparentOutline"},out blockers,out _);Require(Has(blockers,"DITHER")&&blockers.Any(s=>s.StartsWith("OutlineCoverage:")),"shell decision does not swallow dynamic coverage "+family);source.SetFloat("_UseDither",0);
        }
        Convert(source,"Skin",new[]{"UseNativeTransparentOutline"},out blockers,out _);Require(Has(blockers,"MODEL_DECISION"),"transparent outline decision rejected outside Cloth/Hair");
        source=Source("opaque_outline_decision","Hidden/lilToonOutline");Convert(source,"Cloth",new[]{"UseNativeTransparentOutline"},out blockers,out _);Require(Has(blockers,"MODEL_DECISION"),"opaque shell cannot request transparent policy");
    }
    private static void CheckProductionReload()
    {
        var source=Source("production_outline");source.SetTexture("_OutlineTex",Imported("production_srgb"));source.SetFloat("_AlphaMaskMode",0);
        Texture2D exactColor,exactCoverage,exactEmission;var emission=EmissionSource("production_hdr");
        using(HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.ExactPreview)){exactColor=Bake(source,"Color");exactCoverage=Bake(source,"Coverage");exactEmission=Bake(emission,"Emission",true,true);}
        using(HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.Production))
        foreach(var pair in new[]{(Bake(source,"Color"),exactColor),(Bake(source,"Coverage"),exactCoverage),(Bake(emission,"Emission",true,true),exactEmission)})
        {
            string path=AssetDatabase.GetAssetPath(pair.Item1);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);var actual=AssetDatabase.LoadAssetAtPath<Texture2D>(path);Require(actual!=null&&HoMigrationTextureBaker.ReadRecipe(actual).storageProfile=="Production","native production asset reload and recipe");
            Require(actual.mipmapCount==pair.Item2.mipmapCount,"production mip chain retained");for(int mip=0;mip<actual.mipmapCount;mip++)Same(Sample(actual,Mathf.Max(1,actual.width>>mip),Mathf.Max(1,actual.height>>mip),Identity,mip:mip),Sample(pair.Item2,Mathf.Max(1,actual.width>>mip),Mathf.Max(1,actual.height>>mip),Identity,mip:mip),.012f,"production reload GPU mip"+mip);
        }
    }
    private static void Same(Color[] a,Color[] b,float tolerance,string label){Require(a.Length==b.Length,label+" size");for(int i=0;i<a.Length;i++){Near(a[i].r,b[i].r,tolerance,label+" R"+i);Near(a[i].g,b[i].g,tolerance,label+" G"+i);Near(a[i].b,b[i].b,tolerance,label+" B"+i);Near(a[i].a,b[i].a,tolerance,label+" A"+i);}}
    private static void Near(float a,float b,float tolerance,string label)=>Require(Mathf.Abs(a-b)<=tolerance,label+" actual="+a+" expected="+b);
    private static void Require(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
    private const string ProbeShader=@"Shader ""Hidden/HoMigration/IndependentOutlineTestSample"" {
Properties { _ProbeTex(""Sample"",2D)=""white""{} _SamplerTex(""Sampling state"",2D)=""white""{} }
SubShader { Pass { Cull Off ZWrite Off ZTest Always Blend Off
HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D<float4> _ProbeTex,_SamplerTex; SamplerState sampler_ProbeTex,sampler_SamplerTex; float4 _ProbeST;float _Own,_Mip;
float4 frag(v2f_img input):SV_Target {uint w,h;_SamplerTex.GetDimensions(w,h);if(w==0||h==0)return 1e30;float2 uv=input.uv*_ProbeST.xy+_ProbeST.zw;return _Own>.5?_ProbeTex.SampleLevel(sampler_ProbeTex,uv,_Mip):_ProbeTex.SampleLevel(sampler_SamplerTex,uv,_Mip);}
ENDHLSL
} } }";
}
