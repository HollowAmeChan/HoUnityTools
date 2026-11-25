using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Isolated real-lil + all five family host. Only creates private test assets and
// calls the public conversion boundary by reflection; no private mapping API.
public static class HoConverterNativePolicyChecks
{
    private static int checks;
    private static string folder;
    private static readonly List<Object> Transient=new();
    private static readonly Type Converter=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter",false)).First(t=>t!=null);
    private static readonly Type Family=Converter.Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoMigrationFamily");
    private static readonly MethodInfo ConvertMethod=Converter.GetMethod("Convert",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);

    public static void Run()
    {
        Require(Application.isBatchMode&&QualitySettings.activeColorSpace==ColorSpace.Linear,"private Linear batch host");
        Require(ConvertMethod.GetParameters().Length==6,"six-argument material boundary");
        folder="Assets/HoNativePolicy_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        try
        {
            foreach(string family in new[]{"Cloth","Hair","Skin","Eye","Environment"})
            {var target=Convert(Source("default_"+family),family,null,out _,out _);Require(target.shader.name=="HoShader/"+family+" URP","actual family shader "+family);}
            CheckEyeMap();CheckRealtimeAO();CheckNonSss();CheckSkinF0();
            string snapshot="Assets/Editor/Migration/HoLegacyMaterialConverter.cs";
            string hash=File.Exists(snapshot)?string.Concat(SHA256.Create().ComputeHash(File.ReadAllBytes(snapshot)).Select(b=>b.ToString("x2"))):"package source; see host snapshot record";
            Debug.Log("HO_CONVERTER_NATIVE_POLICY_OK checks="+checks+" converterSHA256="+hash+" GPU="+SystemInfo.graphicsDeviceType+" outputs="+folder);
        }
        finally{foreach(Object value in Transient)if(value!=null)Object.DestroyImmediate(value);}
    }
    private static Material Source(string name)
    {
        var m=new Material(Shader.Find("lilToon")){name=name};Require(m.shader!=null,"actual current legacy shader");AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");
        m.SetTexture("_MainTex",Map(name+"_main",Color.white,Color.white));m.SetFloat("_PBRInputMode",0);m.SetFloat("_Metallic",0);m.SetFloat("_Reflectance",.04f);
        m.SetFloat("_UseSSS",0);m.SetFloat("_HoSurfaceThickness",0);m.SetFloat("_HoSurfaceCurvature",0);m.SetFloat("_HoSurfaceTransmittanceHint",0);
        m.SetFloat("_UseEmission",0);m.SetFloat("_UseReflection",0);m.SetFloat("_ApplySpecular",1);m.SetFloat("_ApplyReflection",0);
        m.SetFloat("_AOSource",0);m.SetFloat("_UseAODarkening",0);m.SetFloat("_UseShadow",1);m.SetFloat("_AOStrength",1);return m;
    }
    private static Texture2D Map(string name,Color left,Color right)
    {
        var value=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};value.SetPixels(new[]{left,right});value.Apply(false);AssetDatabase.CreateAsset(value,folder+"/"+name+".asset");return value;
    }
    private static Material Convert(Material source,string family,string[] decisions,out List<string> blockers,out List<string> differences)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);Hash128 inputs=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source));
        blockers=new List<string>();differences=new List<string>();
        var result=(Material)ConvertMethod.Invoke(null,new object[]{source,Enum.Parse(Family,family),differences,blockers,folder+"/Derived",decisions});
        Require(result!=null,"proposal exists "+family);Transient.Add(result);
        Require(before==EditorJsonUtility.ToJson(source),"source serialization remains unchanged "+source.name);
        Require(inputs==AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)),"source dependencies unchanged "+source.name);
        Require(!Has(blockers,"CONVERSION_ERROR")&&!Has(blockers,"TARGET_SYNC"),"converter/sync implementation succeeds: "+string.Join("|",blockers));return result;
    }
    private static bool Has(List<string> values,string code)=>values.Any(v=>v.StartsWith(code+":",StringComparison.Ordinal));
    private static void CheckEyeMap()
    {
        var source=Source("eye_map");var map=Map("eye_smoothness",new Color(.2f,1,.9f,1),new Color(.8f,0,.1f,0));
        source.SetFloat("_Smoothness",.37f);source.SetTexture("_SmoothnessTex",map);
        source.SetTextureScale("_MainTex",new Vector2(2,3));source.SetTextureOffset("_MainTex",new Vector2(.1f,.2f));
        source.SetTextureScale("_SmoothnessTex",new Vector2(.5f,.25f));source.SetTextureOffset("_SmoothnessTex",new Vector2(.3f,.4f));
        var target=Convert(source,"Eye",null,out var blockers,out _);
        Require(!Has(blockers,"EYE_SMOOTHNESS_MAP"),"Eye actual R-map capability resolves old scalar-only blocker");
        Require(target.GetTexture("_SmoothnessMap")==map,"Eye retains actual texture/R source");
        Near(target.GetFloat("_SmoothnessMapScale"),.37f,"old scalar becomes map multiplier");Near(target.GetFloat("_Smoothness"),.37f,"fallback scalar retained independently");
        Vector(target.GetTextureScale("_SmoothnessMap"),new Vector2(1,.75f),"MainST composed map scale");Vector(target.GetTextureOffset("_SmoothnessMap"),new Vector2(.35f,.45f),"MainST composed map offset");
        Near(target.GetFloat("_UseSmoothnessMap"),1,"public sync derives assigned map flag");
        target.SetTexture("_SmoothnessMap",null);Sync(target,"Eye");Near(target.GetFloat("_UseSmoothnessMap"),0,"map removal clears derived flag");Near(target.GetFloat("_Smoothness"),.37f,"map removal restores source scalar");
        source.SetFloat("_Smoothness",0);target=Convert(source,"Eye",null,out _,out _);Near(target.GetFloat("_SmoothnessMapScale"),0,"zero source map multiplier never becomes default1");
    }
    private static void CheckRealtimeAO()
    {
        var source=Source("ao_mask");var mask=Map("ao_weight",new Color(0,1,1,1),new Color(1,0,0,0));
        source.SetFloat("_AOSource",3);source.SetFloat("_AORealtimeWeight",.37f);source.SetFloat("_AOContrast",1.7f);source.SetFloat("_AOLevel",.23f);source.SetTexture("_AOMask",mask);
        source.SetTextureScale("_MainTex",new Vector2(2,3));source.SetTextureOffset("_MainTex",new Vector2(.1f,.2f));
        // Old NoScaleOffset AOMask ignores this saved transform. Its native
        // target contract also samples already transformed BaseMap UV.
        source.SetTextureScale("_AOMask",new Vector2(.2f,.4f));source.SetTextureOffset("_AOMask",new Vector2(.7f,.8f));
        foreach(string family in new[]{"Skin","Eye","Cloth"})
        {
            var target=Convert(source,family,null,out var blockers,out var differences);
            Require(!Has(blockers,"AO_MASK")&&!Has(blockers,"AO_MASK_BAKED"),"white unbound baked AO permits native realtime mask "+family);
            Require(target.GetTexture("_RealtimeAOMask")==mask&&Has(differences,"AO_REALTIME_MASK"),"actual mask resource and model difference "+family);
            Vector(target.GetTextureScale("_BaseMap"),new Vector2(2,3),"mask BaseUV scale "+family);Vector(target.GetTextureOffset("_BaseMap"),new Vector2(.1f,.2f),"mask BaseUV offset "+family);
            Near(target.GetFloat("_RealtimeAOStrength"),.37f,"mixed source realtime weight "+family);
            if(family=="Cloth")Require(Has(differences,"AO_REALTIME_REMAP_MODEL"),"Cloth missing remap is explicit native policy difference");
            else{Near(target.GetFloat("_RealtimeAOContrast"),1.7f,"center contrast "+family);Near(target.GetFloat("_RealtimeAOLevel"),.23f,"level "+family);}
        }
        source.SetFloat("_AOSource",1);
        foreach(string family in new[]{"Skin","Eye","Cloth"})
        {
            var target=Convert(source,family,null,out var blockers,out var differences);
            Require(!Has(blockers,"AO_MASK")&&!Has(blockers,"AO_MASK_BAKED")&&Has(differences,"AO_MASK_INERT_BAKED_WHITE"),"baked-only unbound white makes mask inert "+family);
            Near(target.GetFloat("_RealtimeAOStrength"),0,"baked-only disables realtime "+family);Require(target.GetTexture("_RealtimeAOMask")==null,"inert mask not wired into realtime "+family);
        }
        source.SetTexture("_AOMap",Map("real_baked_ao",new Color(.4f,.3f,.2f,1),new Color(.8f,.7f,.6f,1)));
        foreach(int mode in new[]{1,3})
        foreach(string family in new[]{"Skin","Eye","Cloth"})
        {
            source.SetFloat("_AOSource",mode);Convert(source,family,null,out var blockers,out _);Require(Has(blockers,"AO_MASK_BAKED"),"bound baked AO plus mask remains blocked "+family+" source"+mode);
        }
        source.SetTexture("_AOMap",null);source.SetFloat("_AOSource",2);source.SetFloat("_AORealtimeWeight",.2f);
        Near(Convert(source,"Eye",null,out _,out _).GetFloat("_RealtimeAOStrength"),1,"realtime-only does not apply Mixed-only weight");
    }
    private static void CheckNonSss()
    {
        var source=Source("sss_roles");source.SetFloat("_UseSSS",1);source.SetFloat("_HoSSSProfileId",17);source.SetFloat("_HoSurfaceThickness",.2f);source.SetFloat("_HoSurfaceCurvature",.23f);source.SetFloat("_HoSurfaceTransmittanceHint",.4f);
        source.SetFloat("_HoSSSTransmissionStrength",0);source.SetFloat("_HoSSSTransmissionRadius",0);
        foreach(string family in new[]{"Cloth","Hair","Eye","Environment"})
        {
            Convert(source,family,null,out var strict,out _);Require(Has(strict,"SSS_SURFACE_UNSUPPORTED"),"non-Skin SSS remains strict by default "+family);
            var target=Convert(source,family,new[]{"UseNativeNonSssSurface"},out var blockers,out var differences);
            Require(!Has(blockers,"SSS_SURFACE_UNSUPPORTED")&&!Has(blockers,"MODEL_DECISION")&&Has(differences,"MODEL_DECISION_UseNativeNonSssSurface"),"scoped native non-SSS decision "+family);
            foreach(string property in new[]{"_HoSSSProfileId","_HoSurfaceThickness","_HoSurfaceCurvature","_HoSurfaceTransmittanceHint"})if(target.HasProperty(property))Near(target.GetFloat(property),0,"non-SSS surface field "+family+property);
        }
        var skin=Convert(source,"Skin",new[]{"UseNativeNonSssSurface"},out var wrong,out _);Require(Has(wrong,"MODEL_DECISION"),"non-SSS decision cannot affect Skin role");
        Near(skin.GetFloat("_HoSSSProfileId"),17,"Skin role profile retained");Near(skin.GetFloat("_HoSurfaceCurvature"),.23f,"Skin role curvature retained");Near(skin.GetFloat("_HoSurfaceTransmittanceHint"),.4f,"Skin role transmittance retained");
        source.SetFloat("_UseSSS",0);source.SetFloat("_HoSurfaceThickness",0);source.SetFloat("_HoSurfaceCurvature",0);source.SetFloat("_HoSurfaceTransmittanceHint",0);
        Convert(source,"Eye",new[]{"UseNativeNonSssSurface"},out wrong,out _);Require(Has(wrong,"MODEL_DECISION"),"saved inactive profile alone cannot authorize non-SSS redesign");
        source.SetFloat("_HoSurfaceCurvature",-.2f);Convert(source,"Eye",new[]{"UseNativeNonSssSurface"},out var hintOnly,out _);Require(!Has(hintOnly,"MODEL_DECISION")&&!Has(hintOnly,"SSS_SURFACE_UNSUPPORTED"),"effective absolute curvature alone permits bounded hint retirement");
        source.SetFloat("_UseParallax",1);Convert(source,"Eye",new[]{"UseNativeNonSssSurface"},out var other,out _);Require(Has(other,"PARALLAX"),"non-SSS decision never swallows unrelated material limitation");
    }
    private static void CheckSkinF0()
    {
        var source=Source("native_skin_f0");source.SetFloat("_Reflectance",.48f);
        Convert(source,"Skin",null,out var strict,out _);Require(Has(strict,"SKIN_F0_RANGE"),"high F0 remains strict by default");
        var target=Convert(source,"Skin",new[]{"UseNativeSkinF0"},out var blockers,out var differences);
        Require(!Has(blockers,"MODEL_DECISION")&&!Has(blockers,"SKIN_F0_RANGE")&&Has(differences,"MODEL_DECISION_UseNativeSkinF0"),"only named high scalar Skin F0 redesigned");
        Near(target.GetFloat("_SpecularStrength"),1,"native F0 .04 represented as weight1");Near(target.GetFloat("_DirectSpecularStrength"),0,"disabled direct specular preserved");Near(target.GetFloat("_EnvironmentReflectionStrength"),0,"disabled native probe preserved");
        Require(differences.Any(v=>v.StartsWith("MODEL_DECISION_UseNativeSkinF0:")&&v.Contains("0.52")&&v.Contains(".96")),"visible diffuse remaining .52 to .96 explicitly recorded");
        source.SetFloat("_UseReflection",1);source.SetFloat("_SpecularStrength",.73f);source.SetFloat("_ApplyReflection",1);
        target=Convert(source,"Skin",new[]{"UseNativeSkinF0"},out _,out _);Near(target.GetFloat("_DirectSpecularStrength"),.73f,"independent enabled direct strength preserved");Near(target.GetFloat("_EnvironmentReflectionStrength"),1,"independent probe enabled");
        source.SetFloat("_ApplySpecular",0);target=Convert(source,"Skin",new[]{"UseNativeSkinF0"},out _,out _);Near(target.GetFloat("_DirectSpecularStrength"),0,"direct toggle independent from probe");Near(target.GetFloat("_EnvironmentReflectionStrength"),1,"probe remains enabled after direct off");
        foreach(string family in new[]{"Cloth","Hair","Eye","Environment"}){Convert(source,family,new[]{"UseNativeSkinF0"},out var wrong,out _);Require(Has(wrong,"MODEL_DECISION"),"Skin F0 code rejected on "+family);}
        source.SetFloat("_Reflectance",.08f);Convert(source,"Skin",new[]{"UseNativeSkinF0"},out var boundary,out _);Require(Has(boundary,"MODEL_DECISION"),"in-range boundary cannot use high-F0 redesign");
        source.SetFloat("_Reflectance",.48f);source.SetFloat("_PBRInputMode",1);Convert(source,"Skin",new[]{"UseNativeSkinF0"},out var packed,out _);Require(Has(packed,"MODEL_DECISION")&&Has(packed,"SKIN_F0_RANGE"),"packed source cannot bypass scalar-only scope even when unbound");
        source.SetFloat("_PBRInputMode",0);source.SetTexture("_ReflectanceTex",Map("spatial_f0",Color.white,Color.gray));Convert(source,"Skin",new[]{"UseNativeSkinF0"},out var spatial,out _);Require(Has(spatial,"MODEL_DECISION")&&Has(spatial,"DIELECTRIC_F0_MAP"),"spatial F0 remains independently blocked");
        source.SetTexture("_ReflectanceTex",null);source.SetFloat("_Metallic",1);source.SetTexture("_MetallicGlossMap",Map("nonzero_metal",Color.white,Color.white));source.SetFloat("_UseParallax",1);
        Convert(source,"Skin",new[]{"UseNativeSkinF0"},out var other,out _);Require(Has(other,"NONMETAL_FAMILY")&&Has(other,"PARALLAX"),"F0 redesign cannot swallow metallic or parallax limitation");
        Convert(source,"Skin",new[]{"UseNativeSkinF0","UnknownPolicy"},out var unknown,out _);Require(Has(unknown,"MODEL_DECISION"),"known decision never grants generic unknown-policy bypass");
    }
    private static void Sync(Material material,string family)
    {
        string gui=family=="Eye"?"HoShader.SkinStylizedURP.Editor.EyeShaderGUI, HoShader.SkinStylizedURP.Editor":throw new InvalidOperationException();
        Type.GetType(gui,true).GetMethod("SyncMaterial",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{material});
    }
    private static void Near(float actual,float expected,string label)=>Require(Mathf.Abs(actual-expected)<.00001f,label+" actual="+actual+" expected="+expected);
    private static void Vector(Vector2 actual,Vector2 expected,string label){Near(actual.x,expected.x,label+" X");Near(actual.y,expected.y,label+" Y");}
    private static void Require(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException(label);}
}
