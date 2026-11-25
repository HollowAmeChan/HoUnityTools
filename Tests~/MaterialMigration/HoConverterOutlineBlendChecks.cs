using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Focused real legacy-template regression, independent of the bake formula suite.
// Runs only Convert(..., decisions) through reflection, with immutable sources.
public static class HoConverterOutlineBlendChecks
{
    private static int checks;
    private static string folder;
    private static Texture2D white;
    private static readonly List<Material> Transient=new();
    private static readonly Type Converter=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter",false)).First(t=>t!=null);
    private static readonly Type Family=Converter.Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoMigrationFamily");
    private static readonly string[] Templates={"lilToon","Hidden/lilToonOutline","Hidden/lilToonCutout","Hidden/lilToonCutoutOutline","Hidden/lilToonTransparent","Hidden/lilToonTransparentOutline","Hidden/lilToonTwoPassTransparent","Hidden/lilToonTwoPassTransparentOutline"};
    private static readonly string[] RetiredAliases={"Hidden/lilToonOnePassTransparent","Hidden/lilToonOnePassTransparentOutline"};
    public static void Run()
    {
        Require(Application.isBatchMode,"isolated legacy batch host");folder="Assets/HoOutlineBlend_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        white=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};white.SetPixels(Enumerable.Repeat(Color.white,4).ToArray());white.Apply(false);AssetDatabase.CreateAsset(white,folder+"/white.asset");
        try
        {
            using(HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.ExactPreview))
            {foreach(string alias in RetiredAliases)Require(Shader.Find(alias)==null,"retired alias is not resurrected as a fake shader "+alias);CheckTemplates();CheckCustomTransparent();CheckSolidAndCutout();}
            string path="Assets/Editor/Migration/HoLegacyMaterialConverter.cs";string hash=File.Exists(path)?string.Concat(SHA256.Create().ComputeHash(File.ReadAllBytes(path)).Select(b=>b.ToString("x2"))):"see snapshot record";
            Debug.Log("HO_OUTLINE_BLEND_BOUNDARY_OK actualTemplates="+Templates.Length+" unavailableRetiredAliases="+RetiredAliases.Length+" checks="+checks+" converterSHA256="+hash+" outputs="+folder);
        }
        finally{foreach(Material material in Transient)if(material!=null)Object.DestroyImmediate(material);}
    }
    private static Material Source(string shaderName,string suffix)
    {
        Shader shader=Shader.Find(shaderName);Require(shader!=null,"actual source template "+shaderName);var source=new Material(shader){name=shaderName.Replace('/','_')+suffix};AssetDatabase.CreateAsset(source,folder+"/"+source.name+".mat");
        bool transparent=shaderName.Contains("Transparent");
        Set(source,"_UseSSS",0);Set(source,"_HoSurfaceThickness",0);Set(source,"_HoSurfaceCurvature",0);Set(source,"_HoSurfaceTransmittanceHint",0);Set(source,"_UseEmission",0);Set(source,"_UseReflection",0);Set(source,"_Metallic",0);Set(source,"_Reflectance",.04f);Set(source,"_PBRInputMode",0);Set(source,"_AOSource",0);Set(source,"_AlphaMaskMode",0);Set(source,"_Invisible",0);Set(source,"_lilOITEnabled",0);
        Set(source,"_ZWrite",transparent?0:1);Set(source,"_SrcBlend",transparent?5:1);Set(source,"_DstBlend",transparent?10:0);Set(source,"_BlendOp",0);Set(source,"_BlendOpAlpha",0);source.SetTexture("_MainTex",white);
        source.SetShaderPassEnabled("HO_GEOMETRY_BUFFER",false);source.SetShaderPassEnabled("HoGeometryBuffer",false);
        Set(source,"_OutlineWidth",.08f);Set(source,"_OutlineZBias",0);Set(source,"_OutlineDeleteMesh",0);Set(source,"_OutlineAlphaToMask",0);if(source.HasProperty("_OutlineColor"))source.SetColor("_OutlineColor",Color.black);
        Standard(source,transparent,10);return source;
    }
    private static void Standard(Material source,bool transparent,float alphaDestination)
    {Set(source,"_OutlineSrcBlend",transparent?5:1);Set(source,"_OutlineDstBlend",transparent?10:0);Set(source,"_OutlineSrcBlendAlpha",1);Set(source,"_OutlineDstBlendAlpha",alphaDestination);Set(source,"_OutlineBlendOp",0);Set(source,"_OutlineBlendOpAlpha",0);}
    private static string[] Decisions(Material source,bool outline)
    {var list=new List<string>();if(source.shader.name.Contains("TwoPass"))list.Add("ReplaceTwoPassTransparency");if(outline)list.Add("UseNativeTransparentOutline");return list.ToArray();}
    private static List<string> Convert(Material source,string family,string[] decisions,out List<string> differences)
    {
        AssetDatabase.SaveAssetIfDirty(source);string before=EditorJsonUtility.ToJson(source);Hash128 hash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source));
        var blockers=new List<string>();differences=new List<string>();var target=(Material)Converter.GetMethod("Convert",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source,Enum.Parse(Family,family),differences,blockers,folder+"/Derived",decisions});
        Require(target!=null,"preview exists");Transient.Add(target);Require(before==EditorJsonUtility.ToJson(source)&&hash==AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)),"source serialization/dependencies unchanged");Require(!Has(blockers,"CONVERSION_ERROR")&&!Has(blockers,"TARGET_SYNC"),"implementation/synchronizer succeeded: "+string.Join("|",blockers));return blockers;
    }
    private static void CheckTemplates()
    {
        foreach(string template in Templates)
        foreach(string family in new[]{"Cloth","Hair"})
        {
            var source=Source(template,"_"+family);bool active=template.Contains("Outline"),transparent=template.Contains("Transparent");
            var strict=Convert(source,family,Decisions(source,false),out _);Require(!Has(strict,"OUTLINE_CUSTOM_BLEND"),"standard blend accepted in actual template "+template+family);
            Require(Has(strict,"TRANSPARENT_OUTLINE")== (active&&transparent),"default transparent-shell policy stays strict "+template+family);
            if(active&&transparent)
            {
                var accepted=Convert(source,family,Decisions(source,true),out var differences);Require(!Has(accepted,"MODEL_DECISION")&&!Has(accepted,"TRANSPARENT_OUTLINE")&&!Has(accepted,"OUTLINE_CUSTOM_BLEND"),"standard transparent decision accepted "+template+family+": "+string.Join("|",accepted));
                Require(Has(differences,"MODEL_DECISION_UseNativeTransparentOutline"),"named standard blend model difference "+template+family);
            }
        }
    }
    private static void CheckCustomTransparent()
    {
        var cases=new[]{("_OutlineBlendOp",2f),("_OutlineSrcBlend",1f),("_OutlineSrcBlendAlpha",5f),("_OutlineDstBlendAlpha",0f),("_OutlineBlendOpAlpha",2f),("_OutlineDstBlend",1f)};
        foreach(string family in new[]{"Cloth","Hair"})
        foreach(var change in cases)
        {
            var source=Source("Hidden/lilToonTransparentOutline","_custom_"+family+change.Item1);Set(source,change.Item1,change.Item2);
            var strict=Convert(source,family,null,out _);Require(Has(strict,"OUTLINE_CUSTOM_BLEND")&&Has(strict,"TRANSPARENT_OUTLINE"),"custom blend blocked by default "+family+change.Item1);
            var requested=Convert(source,family,new[]{"UseNativeTransparentOutline"},out var differences);
            Require(Has(requested,"MODEL_DECISION")&&Has(requested,"OUTLINE_CUSTOM_BLEND"),"native transparent decision cannot swallow custom blend "+family+change.Item1);
            Require(!Has(differences,"MODEL_DECISION_UseNativeTransparentOutline"),"invalid custom-state decision is never recorded as accepted");
        }
    }
    private static void CheckSolidAndCutout()
    {
        foreach(string template in new[]{"Hidden/lilToonOutline","Hidden/lilToonCutoutOutline"})
        foreach(float alphaDestination in new[]{0f,10f})
        {
            var source=Source(template,"_solid_alpha"+alphaDestination);Standard(source,false,alphaDestination);Set(source,"_OutlineZTest",4);Set(source,"_OutlineZWrite",1);Set(source,"_OutlineAlphaToMask",1);
            var blockers=Convert(source,"Cloth",null,out var differences);Require(!Has(blockers,"OUTLINE_CUSTOM_BLEND"),"solid/cutout One/Zero RGB accepts alpha One/Zero or One/OneMinusSrcAlpha");
            Require(differences.Any(v=>v.StartsWith("OUTLINE_RASTER_POLICY:")&&v.Contains("OutlineAlphaToMask=1")),"A2C alone remains its separate explicit raster policy difference");
            Set(source,"_OutlineBlendOp",2);blockers=Convert(source,"Cloth",null,out _);Require(Has(blockers,"OUTLINE_CUSTOM_BLEND"),"solid/cutout ReverseSubtract remains blocked");
        }
        var inactive=Source("lilToon","_no_outline_custom_saved");Set(inactive,"_OutlineBlendOp",2);var noEffect=Convert(inactive,"Cloth",null,out _);Require(!Has(noEffect,"OUTLINE_CUSTOM_BLEND"),"non-outline template does not resurrect saved inactive outline blend");
    }
    private static void Set(Material material,string property,float value){if(material.HasProperty(property))material.SetFloat(property,value);}
    private static bool Has(List<string> values,string prefix)=>values.Any(v=>v.StartsWith(prefix+":",StringComparison.Ordinal));
    private static void Require(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException(label);}
}
