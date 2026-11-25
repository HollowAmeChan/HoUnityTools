using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
public static class HoConverterChecks
{
    private static string folder;
    private static int checks;
    public static void Run()
    {
        using var exact = HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.ExactPreview);
        Require(Application.isBatchMode&&Environment.GetEnvironmentVariable("HO_MIGRATION_CONVERTER_FIXTURE")=="1","explicit disposable conversion fixture");
        Require(QualitySettings.activeColorSpace==ColorSpace.Linear,"Linear host");
        Require(Shader.Find("lilToon")!=null,"actual current legacy shader installed");
        folder="Assets/ConverterB1_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        CheckMetadataAndColor();CheckPbrAndUv();CheckCoverage();CheckStencilAndOutline();CheckSss();CheckEmission();CheckBlockers();CheckVariants();
        Debug.Log("HO_LEGACY_CONVERTER_B1_OK checks="+checks+" outputs="+folder);
    }
    private static Material Source(string name,string shader="lilToon")
    {
        var asset=new Material(Shader.Find(shader)){name=name};
        AssetDatabase.CreateAsset(asset,folder+"/"+name+".mat");asset.SetTexture("_MainTex",Map(name+"_WhiteMain",Color.white));return asset;
    }
    private static Texture2D Map(string name,Color color)
    {
        var value=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
        value.SetPixels(Enumerable.Repeat(color,4).ToArray());value.Apply(false);AssetDatabase.CreateAsset(value,folder+"/"+name+".asset");return value;
    }
    private static Material Convert(Material source,HoMigrationFamily family,out List<string> blockers,out List<string> differences)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);
        blockers=new List<string>();differences=new List<string>();
        Material result=HoLegacyMaterialConverter.Convert(source,family,differences,blockers,folder+"/Derived");
        Require(result!=null,"preview exists "+family);Require(before==EditorJsonUtility.ToJson(source),"source immutable "+family);
        Require(!blockers.Any(x=>x.StartsWith("CONVERSION_ERROR")||x.StartsWith("TARGET_SYNC")),"no implementation/sync errors: "+string.Join("|",blockers));
        return result;
    }
    private static Color RuntimeColor(Material material,string property)
    {
        int index=material.shader.FindPropertyIndex(property);Color color=material.GetColor(property);
        return (material.shader.GetPropertyFlags(index)&ShaderPropertyFlags.HDR)!=0?color:color.linear;
    }
    private static void CheckMetadataAndColor()
    {
        var s=Source("MetadataColor");s.SetColor("_Color",new Color(.65f,.3f,.15f,.4f));s.enableInstancing=true;s.doubleSidedGI=true;s.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
        s.SetOverrideTag("HO_MaterialSource","PMX");s.SetOverrideTag("HO_PMX_MaterialIndex","7");s.renderQueue=2477;
        s.SetShaderPassEnabled("MOTIONVECTORS",false);s.SetShaderPassEnabled("DistortionVectors",false);
        foreach(HoMigrationFamily family in Enum.GetValues(typeof(HoMigrationFamily)))
        {
            var t=Convert(s,family,out var b,out var d);Require(b.Count==0,"base material unblocked "+family+": "+string.Join("|",b));
            Near(RuntimeColor(t,"_BaseColor"),RuntimeColor(s,"_Color"),"runtime color transfer "+family);
            Require(t.enableInstancing&&t.doubleSidedGI&&t.globalIlluminationFlags==s.globalIlluminationFlags,"metadata "+family);
            Require(t.GetTag("HO_MaterialSource",false)=="PMX"&&t.GetTag("HO_PMX_MaterialIndex",false)=="7"&&t.GetTag("HO_PMX_MaterialFamily",false)==family.ToString(),"PMX tags "+family);
            Require(t.renderQueue==2477&&t.GetTag("RenderType",false)!="Transparent","target sorting/render tag "+family);
            Require(t.GetShaderPassEnabled("DistortionVectors"),"no retired HDRP disabled-pass copy");
            if(family==HoMigrationFamily.Skin)Require(t.GetFloat("_HoSSSProfileId")==0,"disabled SSS ignores saved profile1");
            Require(!EditorJsonUtility.ToJson(t).Contains("_MainTex\""),"no copied legacy material properties");Object.DestroyImmediate(t);
        }
    }
    private static void CheckPbrAndUv()
    {
        var s=Source("PbrUv");s.SetTexture("_MainTex",Map("MainUv",Color.white));s.SetTextureScale("_MainTex",new Vector2(2,3));s.SetTextureOffset("_MainTex",new Vector2(.1f,.2f));
        var pbr=Map("PbrInput",new Color(.3f,.8f,.5f,.7f));s.SetTexture("_MetallicGlossMap",pbr);s.SetFloat("_Metallic",0);s.SetTexture("_SmoothnessTex",pbr);s.SetFloat("_Smoothness",.61f);
        s.SetTextureScale("_SmoothnessTex",new Vector2(3,4));s.SetTextureOffset("_SmoothnessTex",new Vector2(.5f,.6f));
        s.SetTexture("_BumpMap",pbr);s.SetTextureScale("_BumpMap",new Vector2(.5f,.25f));s.SetTextureOffset("_BumpMap",new Vector2(.4f,.6f));
        var t=Convert(s,HoMigrationFamily.Cloth,out var b,out var d);Require(b.Count==0,"Cloth chained PBR supported");
        Near(t.GetFloat("_MetallicMapScale"),0,"old zero metallic multiplier");Near(t.GetFloat("_SmoothnessMapScale"),.61f,"old smoothness multiplier");
        Near(t.GetTextureScale("_SmoothnessTex"),new Vector2(6,12),"smooth scale chain");Near(t.GetTextureOffset("_SmoothnessTex"),new Vector2(.8f,1.4f),"smooth offset chain");
        Near(t.GetTextureScale("_BumpMap"),new Vector2(1,.75f),"normal scale chain");Near(t.GetTextureOffset("_BumpMap"),new Vector2(.45f,.65f),"normal offset chain");
        Require(t.GetVector("_PBRMapFlags").y==1&&t.GetVector("_PBRMapFlags").w==1,"public sync derives assigned map flags");Object.DestroyImmediate(t);
        s.SetTextureScale("_BumpMap",Vector2.one);s.SetTextureOffset("_BumpMap",Vector2.zero);
        t=Convert(s,HoMigrationFamily.Skin,out b,out d);Require(b.Count==0,"Skin real smoothness map supported");Near(t.GetFloat("_SmoothnessMapScale"),.61f,"Skin map multiplier");Near(t.GetTextureScale("_SmoothnessMap"),new Vector2(6,12),"Skin own smooth ST");Object.DestroyImmediate(t);
    }
    private static void CheckCoverage()
    {
        var s=Source("CutoutCoverage","Hidden/lilToonCutout");s.SetTexture("_MainTex",Map("CutoutMain",new Color(.3f,.5f,.7f,.3f)));
        s.SetTexture("_AlphaMask",Map("CoverageMask",new Color(.8f,0,0,1)));s.SetFloat("_AlphaMaskMode",1);s.SetColor("_Color",new Color(1,1,1,.2f));
        foreach(var family in new[]{HoMigrationFamily.Skin,HoMigrationFamily.Eye,HoMigrationFamily.Cloth,HoMigrationFamily.Hair,HoMigrationFamily.Environment})
        {
            var t=Convert(s,family,out var b,out var d);Require(b.Count==0,"cutout conversion "+family+": "+string.Join("|",b));
            string input=family==HoMigrationFamily.Skin||family==HoMigrationFamily.Eye?"_CoverageMap":family==HoMigrationFamily.Cloth?"_AlphaMask":"_BaseMap";
            Color pixel=((Texture2D)t.GetTexture(input)).GetPixel(0,0);Near(input=="_BaseMap"?pixel.a:pixel.r,.8f,"Replace ignores legacy tint alpha "+family);
            Near(t.GetColor("_BaseColor").a,1,"no double alpha tint "+family);
            Require(t.GetTag("RenderType",false)=="TransparentCutout","cutout synchronizer "+family);Object.DestroyImmediate(t);
        }
    }
    private static void CheckStencilAndOutline()
    {
        var s=Source("StencilOutline","Hidden/lilToonOutline");s.SetFloat("_StencilRef",42);s.SetFloat("_StencilPass",2);s.SetFloat("_OutlineWidth",.08f);s.SetFloat("_OutlineVertexR2Width",3);s.SetFloat("_OutlineZBias",.002f);
        var t=Convert(s,HoMigrationFamily.Cloth,out var b,out var d);Require(b.Count==0,"native Cloth object outline supported");
        Require(t.GetFloat("_StencilRef")==42&&t.GetFloat("_StencilPass")==2&&t.GetFloat("_OutlineStencilPass")==0,"independent stencil");
        Require(t.GetFloat("_OutlineWidthMode")==1&&t.GetFloat("_OutlineVertexR2Width")==3,"object/GD outline modes");Near(t.GetFloat("_OutlineWidth"),.08f,"no pixel/unit guess");Object.DestroyImmediate(t);
        t=Convert(s,HoMigrationFamily.Skin,out b,out d);Require(b.Any(x=>x.StartsWith("OUTLINE_SPACE")),"Skin object geometry blocked");Object.DestroyImmediate(t);
    }
    private static void CheckSss()
    {
        var s=Source("SssNumeric");s.SetFloat("_UseSSS",1);s.SetFloat("_HoSSSProfileId",17);s.SetFloat("_HoSurfaceThickness",.2f);s.SetFloat("_HoSSSThicknessScale",2);s.SetFloat("_SSSStrength",.35f);
        var t=Convert(s,HoMigrationFamily.Skin,out var b,out var d);Require(b.Count==0,"Skin SSS constants supported");Near(t.GetFloat("_HoSurfaceThickness"),.7f,"derived thickness constant");Near(t.GetFloat("_HoSurfaceCurvature"),.8f,"derived curvature");Near(t.GetFloat("_HoSurfaceTransmittanceHint"),.5f,"derived transmittance");Require(t.GetFloat("_HoSSSProfileId")==17,"enabled profile");Object.DestroyImmediate(t);
        s.SetTexture("_SSSThicknessMap",Map("SssThickness",new Color(.5f,0,0,1)));s.SetFloat("_SSSPower",2);
        t=Convert(s,HoMigrationFamily.Skin,out b,out d);Require(b.Count==0,"Skin SSS static map supported: "+string.Join("|",b));Near(((Texture2D)t.GetTexture("_SSSThicknessMap")).GetPixel(0,0).r,.2f,"source max and nonlinear power thinness");Near(t.GetFloat("_HoSurfaceThickness"),1,"baked final thinness weight");Object.DestroyImmediate(t);
    }
    private static void CheckEmission()
    {
        var s=Source("EmissionRgba");s.SetFloat("_UseEmission",1);s.SetFloat("_EmissionBlend",.6f);s.SetFloat("_EmissionMainStrength",.25f);s.SetColor("_EmissionColor",new Color(2,1,.5f,.4f));
        s.SetTexture("_EmissionMap",Map("EmitMap",new Color(.4f,.6f,.8f,.5f)));s.SetTexture("_EmissionBlendMask",Map("EmitMask",new Color(.5f,.25f,1,.75f)));
        var t=Convert(s,HoMigrationFamily.Skin,out var b,out var d);Require(b.Count==0,"Skin RGBA emission supported: "+string.Join("|",b));
        Near(((Texture2D)t.GetTexture("_EmissionMap")).GetPixel(0,0),new Color(.2f,.15f,.8f,.225f),"RGBA mask and blend baked");Near(t.GetColor("_EmissionColor"),new Color(2,1,.5f,.4f),"HDR color alpha remains live");Near(t.GetFloat("_EmissionMainStrength"),.25f,"albedo influence retained");Object.DestroyImmediate(t);
        s.SetFloat("_EmissionMainStrength",0);
        t=Convert(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Count==0,"Cloth premultiplied emission supported: "+string.Join("|",b));
        Near(((Texture2D)t.GetTexture("_EmissionMap")).GetPixel(0,0),new Color(.045f,.03375f,.18f,1),"Cloth map alpha folded once");Near(t.GetColor("_EmissionColor"),new Color(.8f,.4f,.2f,1),"constant alpha folded once");Object.DestroyImmediate(t);
    }
    private static void CheckBlockers()
    {
        var s=Source("F0Range");s.SetFloat("_Reflectance",.5f);var t=Convert(s,HoMigrationFamily.Skin,out var b,out var d);
        Require(b.Any(x=>x.StartsWith("SKIN_F0_RANGE")),"Skin F0 is not silently clamped equivalent");Object.DestroyImmediate(t);
        s=Source("TransparentFace","Hidden/lilToonTransparent");t=Convert(s,HoMigrationFamily.Skin,out b,out d);Require(b.Any(x=>x.StartsWith("SURFACE_TRANSPARENT_SKIN")),"transparent skin role blocked");Object.DestroyImmediate(t);
        s=Source("TwoPass","Hidden/lilToonTwoPassTransparent");t=Convert(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Any(x=>x.StartsWith("TWO_PASS_TRANSPARENCY")),"two-pass ordering blocked");Object.DestroyImmediate(t);
        s=Source("AoMask");s.SetTexture("_AOMask",Map("AOWeightMask",Color.gray));t=Convert(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Any(x=>x.StartsWith("AO_MASK")),"dynamic AO masking is not baked away");Object.DestroyImmediate(t);
        s=Source("WeightedOit","Hidden/lilToonTransparent");s.SetFloat("_lilOITEnabled",1);s.SetFloat("_ZWrite",0);
        t=Convert(s,HoMigrationFamily.Cloth,out b,out d);Require(t.GetFloat("_UseHoOIT")==1&&!b.Any(x=>x.StartsWith("OIT_UNSUPPORTED")),"live old OIT field reaches Cloth");Object.DestroyImmediate(t);
        t=Convert(s,HoMigrationFamily.Eye,out b,out d);Require(b.Any(x=>x.StartsWith("OIT_UNSUPPORTED")),"Eye cannot silently drop weighted OIT");Object.DestroyImmediate(t);
        s=Source("IndependentProbePlanar");s.SetFloat("_UsePlanarReflection",1);s.SetFloat("_PlanarReflectionStrength",.8f);s.SetFloat("_EnvironmentReflectionStrength",.5f);s.SetFloat("_ApplyReflection",0);
        t=Convert(s,HoMigrationFamily.Environment,out b,out d);Require(b.Count==0,"independent environment/planar supported");Near(t.GetFloat("_ProbeReflections"),0,"probe gate independent");Near(t.GetFloat("_EnvironmentReflectionStrength"),.5f,"shared environment gain once");Near(t.GetFloat("_PlanarReflectionStrength"),.8f,"planar strength not double-multiplied");Object.DestroyImmediate(t);
        s=Source("RetiredSavedValues");var serialized=new SerializedObject(s);var floats=serialized.FindProperty("m_SavedProperties.m_Floats");int index=floats.arraySize;floats.InsertArrayElementAtIndex(index);floats.GetArrayElementAtIndex(index).FindPropertyRelative("first").stringValue="_UseSilk1";floats.GetArrayElementAtIndex(index).FindPropertyRelative("second").floatValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();
        Require(!s.HasProperty("_UseSilk1"),"retired flag absent from active shader");t=Convert(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Count==0&&t.GetFloat("_UseSilk")==0&&!EditorJsonUtility.ToJson(t).Contains("_UseSilk1"),"retired saved fields never resurrected");Object.DestroyImmediate(t);
    }
    private static void CheckVariants()
    {
        var s=Source("VariantMismatch");s.SetTexture("_HoSemanticWeightTex",Map("SemanticInput",Color.gray));s.EnableKeyword("_HO_SEMANTIC_MASK");Require(s.IsKeywordEnabled("_HO_SEMANTIC_MASK"),"forced keyword state exists before converter");
        var t=ConvertUnsaved(s,HoMigrationFamily.Cloth,out var b,out var d);Require(b.Any(x=>x.StartsWith("SOURCE_VARIANT_STATE")),"semantic keyword-on/control-off blocked");Require(t.GetTexture("_HoSemanticMask")==s.GetTexture("_HoSemanticWeightTex"),"preview follows actual semantic variant");Object.DestroyImmediate(t);
        s.DisableKeyword("_HO_SEMANTIC_MASK");s.SetFloat("_HoSemanticMaskOn",1);
        t=ConvertUnsaved(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Any(x=>x.StartsWith("SOURCE_VARIANT_STATE"))&&t.GetTexture("_HoSemanticMask")==null,"semantic control-on/keyword-off not auto activated");Object.DestroyImmediate(t);
        s.SetFloat("_HoSemanticMaskOn",0);s.SetFloat("_HoTesionEnabled",1);
        t=ConvertUnsaved(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Any(x=>x.StartsWith("SOURCE_VARIANT_STATE"))&&t.GetFloat("_HoTesionEnabled")==0,"Tension control-on/keyword-off not auto activated");Object.DestroyImmediate(t);
        s.EnableKeyword("_HO_GD_TENSION");t=ConvertUnsaved(s,HoMigrationFamily.Cloth,out b,out d);Require(b.Count==0&&t.GetFloat("_HoTesionEnabled")==1&&t.IsKeywordEnabled("_HO_GD_TENSION"),"consistent Tension maps through public sync");Object.DestroyImmediate(t);
    }
    private static Material ConvertUnsaved(Material source,HoMigrationFamily family,out List<string> blockers,out List<string> differences)
    {
        string before=EditorJsonUtility.ToJson(source);blockers=new List<string>();differences=new List<string>();
        var result=HoLegacyMaterialConverter.Convert(source,family,differences,blockers,folder+"/Derived");
        Require(before==EditorJsonUtility.ToJson(source),"converter does not repair inconsistent source variants");return result;
    }
    private static void Near(float actual,float expected,string label)=>Require(Mathf.Abs(actual-expected)<.005f,label+" actual="+actual+" expected="+expected);
    private static void Near(Vector2 actual,Vector2 expected,string label){Near(actual.x,expected.x,label+" X");Near(actual.y,expected.y,label+" Y");}
    private static void Near(Color actual,Color expected,string label){Near(actual.r,expected.r,label+" R");Near(actual.g,expected.g,label+" G");Near(actual.b,expected.b,label+" B");Near(actual.a,expected.a,label+" A");}
    private static void Require(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException("Converter B1: "+label);}
}
