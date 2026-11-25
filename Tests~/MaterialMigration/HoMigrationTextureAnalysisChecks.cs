using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class HoMigrationTextureAnalysisChecks
{
    private static int checks;
    private static string folder;
    private static readonly List<Object> Owned=new();
    private static readonly Type Analysis=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoMigrationTextureAnalysis",false)).First(t=>t!=null);
    private static readonly MethodInfo Prove=Analysis.GetMethod("TryProveRedConstantZero",BindingFlags.NonPublic|BindingFlags.Static);
    public static void Run()
    {
        Require(Application.isBatchMode,"disposable host");folder="Assets/HoZeroProof_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        try
        {
            var texture=Map();Require(Zero(texture,out string evidence),"all manually authored mips zero: "+evidence);
            Require(evidence.Contains("5 complete imported mips")&&evidence.Contains("341 texels"),"proof covers complete mip chain");
            SetLast(texture,1e-8f);Require(!Zero(texture,out evidence)&&evidence.Contains("mip 4"),"one tiny positive in final mip rejected");
            SetLast(texture,-1e-8f);Require(!Zero(texture,out evidence),"negative is not zero");
            SetLast(texture,float.NaN);Require(!Zero(texture,out evidence),"NaN is not zero");
            SetLast(texture,float.PositiveInfinity);Require(!Zero(texture,out evidence),"infinity is not zero");
            SetLast(texture,0);Require(Zero(texture,out evidence),"fresh GPU content reproved without stale cache");
            Require(!Zero(null,out evidence),"missing input not certified");
            var rt=new RenderTexture(4,4,0);Owned.Add(rt);Require(!Zero(rt,out evidence),"non Texture2D input not certified");
            int priorLimit=QualitySettings.globalTextureMipmapLimit;
            try{QualitySettings.globalTextureMipmapLimit=1;Require(!Zero(texture,out evidence),"global mip exclusion prevents proof");}
            finally{QualitySettings.globalTextureMipmapLimit=priorLimit;}
            Texture2D imported=ImportCompressed();
            Require(!imported.isReadable&&imported.isDataSRGB,"actual unreadable sRGB imported resource");
            Require(Zero(imported,out evidence),"compressed unreadable sRGB zero-red accepted: "+evidence);
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(imported));
            string before=EditorJsonUtility.ToJson(importer);Zero(imported,out evidence);
            Require(before==EditorJsonUtility.ToJson(importer)&&!imported.isReadable,"proof never mutates importer/readability");
            CheckConverter(imported,false);CheckConverter(imported,true);
            foreach(string path in new[]{"Assets/LegacySurfaceProbe/Hiro_T_Body_Metallic.png","Assets/LegacySurfaceProbe/Hiro_T_Body_Glossiness.png"})
            {
                var actual=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(actual==null)continue;
                Require(Zero(actual,out evidence),"real imported Hiro channel is exact zero: "+evidence);
                Debug.Log("HO_REAL_HIRO_GPU_ZERO "+path+" "+evidence);CheckConverter(actual,false);
            }
            Debug.Log("HO_TEXTURE_ZERO_PROOF_OK checks="+checks+" GPU="+SystemInfo.graphicsDeviceType);
        }
        finally{foreach(Object value in Owned)if(value!=null)Object.DestroyImmediate(value);}
    }
    private static Texture2D Map()
    {
        var t=new Texture2D(16,16,TextureFormat.RGBAFloat,true,true);Owned.Add(t);
        for(int mip=0;mip<t.mipmapCount;mip++)t.SetPixels(Enumerable.Repeat(new Color(0,.25f,.75f,.2f),Mathf.Max(1,16>>mip)*Mathf.Max(1,16>>mip)).ToArray(),mip);
        t.Apply(false,false);return t;
    }
    private static void SetLast(Texture2D texture,float value){texture.SetPixels(new[]{new Color(value,.25f,.75f,.2f)},texture.mipmapCount-1);texture.Apply(false,false);}
    private static bool Zero(Texture texture,out string evidence)
    {object[] args={texture,null};bool result=(bool)Prove.Invoke(null,args);evidence=(string)args[1];return result;}
    private static Texture2D ImportCompressed()
    {
        var t=new Texture2D(32,32,TextureFormat.RGBA32,false,true);Owned.Add(t);t.SetPixels(Enumerable.Repeat(new Color(0,.25f,.75f,.7f),1024).ToArray());t.Apply(false,false);
        string path=folder+"/compressed-srgb-zero.png";File.WriteAllBytes(path,t.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.isReadable=false;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        var result=AssetDatabase.LoadAssetAtPath<Texture2D>(path);Debug.Log("HO_ZERO_PROOF_IMPORTED_FORMAT "+result.format);return result;
    }
    private static void CheckConverter(Texture2D input,bool packed)
    {
        Type converter=Analysis.Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoLegacyMaterialConverter");
        Type family=Analysis.Assembly.GetType("Hollow.HoUnityTools.Editor.MaterialMigration.HoMigrationFamily");
        var source=new Material(Shader.Find("lilToon"));Owned.Add(source);source.SetFloat("_Metallic",.8f);source.SetFloat("_PBRInputMode",packed?1:0);source.SetTexture(packed?"_PBRMap":"_MetallicGlossMap",input);
        var blockers=new List<string>();var differences=new List<string>();string before=EditorJsonUtility.ToJson(source);
        var target=(Material)converter.GetMethod("Convert",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source,Enum.Parse(family,"Skin"),differences,blockers,folder+"/Derived",null});Owned.Add(target);
        Require(target!=null&&!blockers.Any(s=>s.StartsWith("CONVERSION_ERROR")),"constant-zero proposal succeeds");
        Require(!blockers.Any(s=>s.StartsWith("NONMETAL_FAMILY"))&&differences.Any(s=>s.StartsWith("CONSTANT_ZERO_METALLIC")),"scalar .8 is resolved by actual map R proof");
        Require(before==EditorJsonUtility.ToJson(source),"proof leaves source material unchanged");
        if(packed)Require(blockers.Any(s=>s.StartsWith("DIELECTRIC_F0_MAP")),"red proof never drops unrelated packed F0 channel limitation");
        source.SetTexture(packed?"_PBRMap":"_MetallicGlossMap",Texture2D.whiteTexture);blockers.Clear();differences.Clear();
        target=(Material)converter.GetMethod("Convert",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{source,Enum.Parse(family,"Skin"),differences,blockers,folder+"/Derived",null});Owned.Add(target);
        Require(blockers.Any(s=>s.StartsWith("NONMETAL_FAMILY"))&&!differences.Any(s=>s.StartsWith("CONSTANT_ZERO_METALLIC")),"nonzero actual metallic input remains blocked");
    }
    private static void Require(bool value,string text){checks++;if(!value)throw new Exception(text);}
}
