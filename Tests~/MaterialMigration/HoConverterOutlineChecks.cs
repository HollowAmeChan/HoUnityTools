using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Material mapping checks in a disposable current-lil + specialized-package host.
// No production writes, texture import changes, Apply or scene binding.
public static class HoConverterOutlineChecks
{
    private static int checks;
    private static readonly List<Object> Owned=new();
    private static readonly MethodInfo Outline=AppDomain.CurrentDomain.GetAssemblies()
        .Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter",false)).First(t=>t!=null)
        .GetMethod("MapOutline",BindingFlags.NonPublic|BindingFlags.Static);

    public static void Run()
    {
        Require(Application.isBatchMode,"disposable batch host");
        try
        {
            foreach(var family in new[]{HoMigrationFamily.Cloth,HoMigrationFamily.Hair,HoMigrationFamily.Skin})CheckFamily(family);
            CheckGeometryAndPasses();CheckModelDecisions();
            Debug.Log("HO_CONVERTER_OUTLINE_CHECKS_OK checks="+checks+" GPU="+SystemInfo.graphicsDeviceType);
        }
        finally{foreach(Object value in Owned)if(value!=null)Object.DestroyImmediate(value);}
    }
    private static Material Source(bool transparent=false)
    {
        var s=new Material(Shader.Find(transparent?"Hidden/lilToonTransparentOutline":"Hidden/lilToonOutline"));Owned.Add(s);
        Require(s.shader!=null,"actual outline source shader");
        s.SetFloat("_OutlineWidth",.08f);s.SetFloat("_OutlineZBias",0);s.SetFloat("_OutlineDeleteMesh",0);
        s.SetFloat("_OutlineVertexR2Width",0);s.SetColor("_OutlineColor",Color.black);s.SetColor("_OutlineLitColor",Color.clear);
        s.SetFloat("_OutlineLitApplyTex",0);s.SetFloat("_OutlineLitShadowReceive",0);s.SetFloat("_OutlineLitScale",10);s.SetFloat("_OutlineLitOffset",-8);
        s.SetFloat("_OutlineZTest",4);s.SetFloat("_OutlineZWrite",1);s.SetFloat("_OutlineAlphaToMask",0);
        s.SetFloat("_AlphaMaskMode",0);s.SetVector("_OutlineTexHSVG",new Vector4(0,1,1,1));
        s.SetTexture("_OutlineTex",null);s.SetTexture("_OutlineVectorTex",null);
        return s;
    }
    private static Texture2D Map()
    {
        var texture=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);texture.SetPixels(new[]{Color.red,Color.blue,Color.white,Color.clear});texture.Apply();Owned.Add(texture);return texture;
    }
    private static Material Convert(Material source,HoMigrationFamily family,out List<string> blockers,out List<string> differences)
    {
        var target=new Material(Shader.Find("HoShader/"+family+" URP"));Owned.Add(target);
        if(target.HasProperty("_OutlineNormalSource"))target.SetFloat("_OutlineNormalSource",1);
        blockers=new List<string>();differences=new List<string>();string before=EditorJsonUtility.ToJson(source);
        Outline.Invoke(null,new object[]{source,target,family,source.shader.name.Contains("Transparent")?2:0,differences,blockers});
        Require(before==EditorJsonUtility.ToJson(source),"source remains immutable");return target;
    }
    private static bool Has(List<string> values,string prefix)=>values.Any(v=>v.StartsWith(prefix,StringComparison.Ordinal));
    private static void CheckFamily(HoMigrationFamily family)
    {
        Material source=Source();Texture2D map=Map();source.SetTexture("_OutlineTex",map);
        Material target=Convert(source,family,out var blockers,out var differences);
        Require(!blockers.Any(),"opaque black dead map unblocked "+family+": "+string.Join("|",blockers));
        Require((!target.HasProperty("_OutlineEnabled")||target.GetFloat("_OutlineEnabled")==1)&&target.GetFloat("_OutlineWidth")>0,"black shell retained "+family);
        Require(Has(differences,"OUTLINE_DEAD_TEXTURE"),"dead texture evidence "+family);
        Require(target.GetTexture("_OutlineColorMap")==null,"dead map omitted "+family);
        Require(target.GetFloat("_OutlineWidthMode")== (family==HoMigrationFamily.Skin?2:1),"native object mode "+family);
        if(family==HoMigrationFamily.Skin)Require(target.GetFloat("_OutlineNormalSource")==1,"existing Skin normal source left independent");

        source.SetColor("_OutlineColor",Color.white);source.SetTextureScale("_MainTex",new Vector2(2,3));source.SetTextureOffset("_MainTex",new Vector2(.3f,.2f));
        source.SetTextureScale("_OutlineTex",new Vector2(.7f,1.2f));source.SetTextureOffset("_OutlineTex",new Vector2(.1f,.4f));
        target=Convert(source,family,out blockers,out differences);
        Require(!blockers.Any(),"live neutral RGB map supported "+family+": "+string.Join("|",blockers));
        Require(target.GetTexture("_OutlineColorMap")==map,"live map retained "+family);
        Require(target.GetTextureScale("_OutlineColorMap")==source.GetTextureScale("_OutlineTex"),"independent outline scale "+family);
        Require(target.GetTextureOffset("_OutlineColorMap")==source.GetTextureOffset("_OutlineTex"),"independent outline offset "+family);
        source.SetVector("_OutlineTexHSVG",new Vector4(0,1,1,2));Convert(source,family,out blockers,out differences);
        Require(Has(blockers,"OUTLINE_HSVG"),"live HSVG explicitly blocked "+family);
        source.SetTexture("_OutlineTex",null);Convert(source,family,out blockers,out differences);
        Require(!Has(blockers,"OUTLINE_HSVG"),"gamma on default white is inert "+family);

        source.SetTexture("_OutlineTex",map);source.SetColor("_OutlineColor",Color.black);source.SetVector("_OutlineTexHSVG",new Vector4(0,1,1,1));
        source.SetColor("_OutlineLitColor",Color.white);source.SetFloat("_OutlineLitApplyTex",0);
        Convert(source,family,out blockers,out differences);Require(!Has(blockers,"OUTLINE_TEXTURE"),"untextured lit branch does not resurrect map "+family);
        source.SetFloat("_OutlineLitApplyTex",1);target=Convert(source,family,out blockers,out differences);
        Require(target.GetTexture("_OutlineColorMap")==map,"textured lit branch keeps map despite black base "+family);
        source.SetColor("_OutlineColor",Color.white);source.SetColor("_OutlineLitColor",new Color(0,0,0,1));source.SetFloat("_OutlineLitApplyTex",0);
        source.SetFloat("_OutlineLitScale",0);source.SetFloat("_OutlineLitOffset",1);
        target=Convert(source,family,out blockers,out differences);Require(target.GetTexture("_OutlineColorMap")==null,"constant one factor removes base texture "+family);
        source.SetFloat("_OutlineLitShadowReceive",1);target=Convert(source,family,out blockers,out differences);
        Require(target.GetTexture("_OutlineColorMap")==map,"shadow-reduced lit factor restores map dependency "+family);

        source=Source(true);source.SetTexture("_OutlineTex",map);Convert(source,family,out blockers,out differences);
        Require(Has(blockers,"TRANSPARENT_OUTLINE"),"transparent shell missing capability "+family);
        Require(Has(blockers,"OUTLINE_ALPHA_TEXTURE"),"black transparent map alpha stays live "+family);
        source.SetFloat("_AlphaMaskMode",1);Convert(source,family,out blockers,out differences);
        Require(!Has(blockers,"OUTLINE_ALPHA_TEXTURE"),"replace-alpha removes outline texture alpha dependency "+family);
        Require(Has(blockers,"TRANSPARENT_OUTLINE"),"replace-alpha does not invent transparent shell "+family);
    }
    private static void CheckGeometryAndPasses()
    {
        Material source=Source();source.SetTexture("_OutlineVectorTex",Map());source.SetFloat("_OutlineVectorScale",1);
        Convert(source,HoMigrationFamily.Cloth,out var blockers,out var differences);Require(Has(blockers,"OUTLINE_DIRECTION_TEXTURE"),"ordinary vector texture stays blocker");
        source.SetFloat("_OutlineVertexR2Width",2);Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);
        Require(!Has(blockers,"OUTLINE_DIRECTION_TEXTURE")&&Has(differences,"OUTLINE_DEAD_VECTOR_TEXTURE"),"fixed vertex RGBA overrides vector texture");
        source.SetFloat("_OutlineVertexR2Width",3);Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);
        Require(Has(blockers,"OUTLINE_DIRECTION_TEXTURE"),"GD fallback cannot be claimed dead");
        source=Source();source.SetFloat("_OutlineWidth",0);source.SetFloat("_OutlineZBias",.5f);
        Convert(source,HoMigrationFamily.Environment,out blockers,out differences);Require(Has(blockers,"OUTLINE_ZERO_WIDTH_SHELL"),"real sofa bias-only shell retained as explicit decision");
        source.SetFloat("_OutlineZBias",0);Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);
        Require(Has(blockers,"OUTLINE_ZERO_WIDTH_SHELL"),"zero width DeleteMesh false not proven inert");
        source.SetFloat("_OutlineDeleteMesh",1);Material target=Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);
        Require(!blockers.Any()&&target.GetFloat("_OutlineEnabled")==0,"actual delete-zero-width is disabled without blockers");
        source=Source();source.SetShaderPassEnabled("FORWARD_OUTLINE",false);source.SetShaderPassEnabled("HO_OUTLINE_NORMAL_DEPTH",false);
        target=Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);Require(!blockers.Any()&&target.GetFloat("_OutlineEnabled")==0,"both explicitly disabled passes stay disabled");
        source=Source();source.SetFloat("_OutlineZTest",2);source.SetFloat("_OutlineZWrite",0);source.SetFloat("_OutlineAlphaToMask",1);
        Convert(source,HoMigrationFamily.Cloth,out blockers,out differences);Require(!blockers.Any()&&Has(differences,"OUTLINE_RASTER_POLICY"),"chosen native raster policy is explicit difference");
    }
    private static Material Proposal(Material source,HoMigrationFamily family,string[] decisions,out List<string> blockers,out List<string> differences)
    {
        MethodInfo method=Outline.DeclaringType.GetMethod("Convert",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        blockers=new List<string>();differences=new List<string>();string before=EditorJsonUtility.ToJson(source);
        var target=(Material)method.Invoke(null,new object[]{source,family,differences,blockers,"Assets/HoOutlineDecisionUnused",decisions});
        Require(target!=null,"decision preview exists");Owned.Add(target);
        Require(before==EditorJsonUtility.ToJson(source),"decision preview leaves source immutable");
        Require(!Has(blockers,"CONVERSION_ERROR"),"decision preview implementation succeeds: "+string.Join("|",blockers));return target;
    }
    private static void CheckModelDecisions()
    {
        Material source=Source();source.SetFloat("_OutlineWidth",0);source.SetFloat("_OutlineZBias",.5f);
        Proposal(source,HoMigrationFamily.Environment,null,out var blockers,out var differences);
        Require(Has(blockers,"OUTLINE_ZERO_WIDTH_SHELL"),"zero-width strict by default");
        Material target=Proposal(source,HoMigrationFamily.Environment,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(!Has(blockers,"OUTLINE_ZERO_WIDTH_SHELL")&&!Has(blockers,"OUTLINE_SPACE"),"only explicitly retired zero shell no longer requires geometry");
        Require(target.GetFloat("_OutlineEnabled")==0&&target.GetFloat("_OutlineWidth")==0,"retired shell remains off");
        Require(Has(differences,"MODEL_DECISION_RetireZeroWidthOutline"),"zero shell decision independently recorded");
        source.SetFloat("_OutlineWidth",.08f);Proposal(source,HoMigrationFamily.Cloth,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION")&&!Has(differences,"MODEL_DECISION_RetireZeroWidthOutline"),"zero retirement cannot swallow expanded shell");
        source.SetFloat("_OutlineWidth",0);source.SetFloat("_OutlineDeleteMesh",1);Proposal(source,HoMigrationFamily.Cloth,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION"),"already deleted shell cannot consume retirement decision");
        source.SetFloat("_OutlineDeleteMesh",.25f);Proposal(source,HoMigrationFamily.Cloth,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION"),"zero-shell decision requires exact DeleteMesh zero");

        source=Source();Proposal(source,HoMigrationFamily.Eye,null,out blockers,out differences);Require(Has(blockers,"OUTLINE_EYE"),"Eye shell strict by default");
        target=Proposal(source,HoMigrationFamily.Eye,new[]{"RetireEyeOutline"},out blockers,out differences);
        Require(!Has(blockers,"OUTLINE_EYE")&&Has(differences,"MODEL_DECISION_RetireEyeOutline"),"explicit Eye role retirement is recorded");
        Proposal(source,HoMigrationFamily.Skin,new[]{"RetireEyeOutline"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION")&&!Has(differences,"MODEL_DECISION_RetireEyeOutline"),"Eye retirement rejected on Skin");
        source.SetShaderPassEnabled("FORWARD_OUTLINE",false);source.SetShaderPassEnabled("HO_OUTLINE_NORMAL_DEPTH",false);
        Proposal(source,HoMigrationFamily.Eye,new[]{"RetireEyeOutline"},out blockers,out differences);Require(Has(blockers,"MODEL_DECISION"),"disabled shell cannot consume Eye retirement");

        source=Source(true);source.SetFloat("_ZWrite",1);
        Proposal(source,HoMigrationFamily.Cloth,null,out blockers,out differences);Require(Has(blockers,"TRANSPARENT_DEPTH_WRITE"),"transparent depth strict by default");
        target=Proposal(source,HoMigrationFamily.Cloth,new[]{"UseNativeTransparentDepth"},out blockers,out differences);
        Require(!Has(blockers,"TRANSPARENT_DEPTH_WRITE")&&Has(differences,"MODEL_DECISION_UseNativeTransparentDepth"),"named transparent depth decision accepted");
        Require(target.GetFloat("_ZWrite")==0,"native transparent depth target stays off");
        Require(Has(blockers,"TRANSPARENT_OUTLINE"),"transparent depth decision cannot hide missing shell");
        source.SetFloat("_ZWrite",0);Proposal(source,HoMigrationFamily.Cloth,new[]{"UseNativeTransparentDepth"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION"),"already native-off depth cannot consume decision");
        source=Source();Proposal(source,HoMigrationFamily.Cloth,new[]{"UseNativeTransparentDepth"},out blockers,out differences);Require(Has(blockers,"MODEL_DECISION"),"opaque cannot use transparent depth decision");
        source=Source(true);source.SetFloat("_ZWrite",1);Proposal(source,HoMigrationFamily.Environment,new[]{"UseNativeTransparentDepth"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION"),"custom-depth-capable Environment cannot use Cloth/Hair fixed-off decision");

        source=new Material(Shader.Find("Hidden/lilToonTwoPassTransparent"));Owned.Add(source);source.SetFloat("_ZWrite",1);source.SetFloat("_Cull",2);
        Proposal(source,HoMigrationFamily.Cloth,null,out blockers,out differences);Require(Has(blockers,"TWO_PASS_TRANSPARENCY"),"TwoPass strict by default");
        target=Proposal(source,HoMigrationFamily.Cloth,new[]{"ReplaceTwoPassTransparency"},out blockers,out differences);
        Require(!Has(blockers,"TWO_PASS_TRANSPARENCY")&&Has(differences,"MODEL_DECISION_ReplaceTwoPassTransparency"),"named single pass policy accepted");
        Require(target.GetFloat("_Cull")==0,"single native pass explicitly double sided");
        Require(Has(blockers,"TRANSPARENT_DEPTH_WRITE"),"single pass decision cannot hide transparent depth mismatch");
        Proposal(source,HoMigrationFamily.Skin,new[]{"ReplaceTwoPassTransparency"},out blockers,out differences);
        Require(Has(blockers,"MODEL_DECISION")&&Has(blockers,"TWO_PASS_TRANSPARENCY")&&Has(blockers,"SURFACE_TRANSPARENT_SKIN"),"no invented Skin transparency");
        source=Source(true);Proposal(source,HoMigrationFamily.Cloth,new[]{"ReplaceTwoPassTransparency"},out blockers,out differences);Require(Has(blockers,"MODEL_DECISION"),"single pass source cannot consume TwoPass decision");

        source=Source();Proposal(source,HoMigrationFamily.Cloth,new[]{"IgnoreEverything",null,"retireeyeoutline"},out blockers,out differences);
        Require(blockers.Count(v=>v.StartsWith("MODEL_DECISION:"))==3,"unknown, null and incorrectly cased codes blocked separately");
        source.SetFloat("_OutlineWidth",0);source.SetFloat("_UseSSS",1);
        Proposal(source,HoMigrationFamily.Cloth,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(Has(blockers,"SSS_SURFACE_UNSUPPORTED"),"shell retirement never hides SSS limitation");
        source.SetFloat("_Metallic",1);source.SetFloat("_Reflectance",.3f);
        Proposal(source,HoMigrationFamily.Skin,new[]{"RetireZeroWidthOutline"},out blockers,out differences);
        Require(Has(blockers,"NONMETAL_FAMILY")&&Has(blockers,"SKIN_F0_RANGE"),"shell retirement never hides metal or F0 limitation");

        source=Source();source.SetFloat("_Invisible",1);source.SetFloat("_UseParallax",1);source.SetFloat("_UseSSS",1);source.SetFloat("_UseEmission",1);
        source.SetColor("_Color",new Color(.7f,.5f,.3f,.8f));source.SetTexture("_MainTex",Map());source.SetTextureScale("_MainTex",new Vector2(2,3));source.SetTextureOffset("_MainTex",new Vector2(.2f,.4f));
        source.renderQueue=2023;source.SetOverrideTag("HO_MaterialSource","PMX");source.SetOverrideTag("HO_PMX_MaterialIndex","8");
        Proposal(source,HoMigrationFamily.Cloth,null,out blockers,out differences);Require(Has(blockers,"INVISIBLE_SOURCE"),"hidden source strict by default");
        target=Proposal(source,HoMigrationFamily.Cloth,new[]{"PreserveHiddenCoverage"},out blockers,out differences);
        Require(!blockers.Any()&&Has(differences,"MODEL_DECISION_PreserveHiddenCoverage"),"explicit hidden coverage produces clean bounded proposal");
        Require(target.GetFloat("_SurfaceMode")==1&&target.GetFloat("_Cutoff")==.5f&&target.GetFloat("_AlphaMaskMode")==0,"hidden native cutout fields");
        Require(target.GetColor("_BaseColor").a==0&&target.GetFloat("_UseEmission")==0&&target.GetFloat("_OutlineEnabled")==0,"hidden alpha, emission and outline");
        Require(target.GetTexture("_BaseMap")==source.GetTexture("_MainTex")&&target.GetTextureScale("_BaseMap")==source.GetTextureScale("_MainTex")&&target.GetTextureOffset("_BaseMap")==source.GetTextureOffset("_MainTex"),"hidden main texture and raw ST preserved");
        Require(target.IsKeywordEnabled("_ALPHATEST_ON")&&target.GetTag("RenderType",false)=="TransparentCutout","hidden native RT any-hit classification");
        Require(target.renderQueue==2023&&target.GetTag("HO_MaterialSource",false)=="PMX"&&target.GetTag("HO_PMX_MaterialIndex",false)=="8","hidden sorting and consumed metadata");
        Proposal(source,HoMigrationFamily.Skin,new[]{"PreserveHiddenCoverage"},out blockers,out differences);Require(Has(blockers,"MODEL_DECISION")&&Has(blockers,"INVISIBLE_SOURCE"),"hidden policy cannot change another family");
        source.SetFloat("_Invisible",0);Proposal(source,HoMigrationFamily.Cloth,new[]{"PreserveHiddenCoverage"},out blockers,out differences);Require(Has(blockers,"MODEL_DECISION"),"visible source cannot request hidden policy");
    }
    private static void Require(bool condition,string message){checks++;if(!condition)throw new Exception("Outline check failed: "+message);}
}
