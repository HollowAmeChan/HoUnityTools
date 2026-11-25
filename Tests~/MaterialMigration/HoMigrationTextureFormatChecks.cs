using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy with Baker/Formats into a disposable Linear host. Inputs under B2TextureSamples
// are optional copies of actual B1 float outputs; never operate on the production project.
public static class HoMigrationTextureFormatChecks
{
    [Serializable] public sealed class Entry { public string label, path; public Color firstPixel; public HoMigrationTextureFormats.Measurement measurement; }
    [Serializable] public sealed class Results { public int checks; public List<Entry> entries = new(); public HoMigrationTextureFormats.Measurement bc7Diagnostic; }
    static int checks;
    static readonly Results results = new();
    static string folder;
    public static void Run()
    {
        try
        {
            string suffix = Environment.GetEnvironmentVariable("HO_FORMAT_FIXTURE_SUFFIX") ?? "1";
            folder = "Assets/HoTextureFormatFixture" + suffix;
            Need(Application.isBatchMode && Environment.GetEnvironmentVariable("HO_FORMAT_FIXTURE") == "1" && !Directory.Exists(folder), "fresh private format fixture");
            Need(QualitySettings.activeColorSpace == ColorSpace.Linear, "Linear host");
            AssetDatabase.CreateFolder("Assets", "HoTextureFormatFixture" + suffix);
            using var scope = HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.Production);
            Color[] dark = Enumerable.Range(0,256).Select(i => new Color(.00001f + i*.00009f, .1f+i*.002f, .9f-i*.001f, 1)).ToArray();
            var color = Store("dark-color",dark,16,16,false,false,false,Array.Empty<float>());
            Need(color.format == TextureFormat.RGBA32 && color.isDataSRGB, "LDR color uses sRGB RGBA32 rather than linear8");
            var colorReport = results.entries.Last().measurement;
            Need(colorReport.maxEncodedRgbError < 1f/255f+.00002f && colorReport.maxLinearRgbError < .009f, "recorded encoded one-code and linear color error bounds");
            float srgbDarkError=Read(color)[0].r-dark[0].r;
            float linear8DarkError=Mathf.Round(dark[0].r*255)/255-dark[0].r;
            Need(Mathf.Abs(srgbDarkError)<=Mathf.Abs(linear8DarkError)+.000001f,"sRGB storage improves low-light quantization");
            Color[] binary=Enumerable.Range(0,256).Select(i=>new Color((i+i/16)%2,0,0,1)).ToArray();
            var coverage=Store("binary-coverage",binary,16,16,true,false,true,new[]{.5f,.001f});
            Need(coverage.format==TextureFormat.R8,"binary coverage accepts compact R8 after all-mip clipping checks");
            Need(results.entries.Last().measurement.cutoffMismatches==0,"R8 preserves tested cutout/floor classifications across mips");
            float[] adversarial={.4999f,.5f,.0013f,.003f};
            var critical=Store("critical-coverage",Enumerable.Range(0,256).Select(i=>new Color(adversarial[i%4],0,0,1)).ToArray(),16,16,true,false,true,new[]{.5f,.001f});
            Need(critical.format==TextureFormat.RFloat,"coverage promotes past R8 and Half when either changes a cutoff result");
            var thin=Store("thinness",Enumerable.Repeat(new Color(.0001f,0,0,1),256).ToArray(),16,16,true,false,false,Array.Empty<float>());
            Need(thin.format==TextureFormat.RHalf,"small nonzero scalar is not rounded to zero by R8");
            var hdr=Store("hdr-emission",Enumerable.Repeat(new Color(32.13f,1024.3f,60000,.34f),256).ToArray(),16,16,false,true,false,Array.Empty<float>());
            Need(hdr.format==TextureFormat.RGBAHalf&&!hdr.isDataSRGB,"HDR emission keeps Half numeric range and linear interpretation");
            Need(Read(hdr)[0].b>59000,"HDR emission is not clamped to one");
            var overflow=Store("hdr-overflow",Enumerable.Repeat(new Color(70000,1,1,.6f),256).ToArray(),16,16,false,true,false,Array.Empty<float>());
            Need(overflow.format==TextureFormat.RGBAFloat,"values outside finite Half range retain Float");
            using(HoMigrationTextureFormats.UseProfile(HoMigrationTextureFormats.OutputProfile.ExactPreview))
            {
                var exact=Store("exact-preview",dark,16,16,false,false,false,Array.Empty<float>());
                Need(exact.format==TextureFormat.RGBAFloat&&exact.isReadable,"explicit reference mode remains readable Float");
            }
            var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/HoMigrationBakerFixture2/Sources/Source.mat");
            if(original!=null)
            {
                var errors=new List<string>();
                var baked=HoMigrationTextureBaker.HsvgBase(original,folder+"/Baker",errors);
                Need(baked!=null&&errors.Count==0,"existing Baker entry uses Production policy: "+string.Join(";",errors));
                var recipe=HoMigrationTextureBaker.ReadRecipe(baked);
                Need(recipe.version==2&&recipe.storageProfile=="Production"&&recipe.storage!=null&&!baked.isReadable,"recipe captures production storage and disabled CPU copy");
                Need(recipe.storage.storedPayloadBytes<recipe.storage.referencePayloadBytes,"actual Baker payload is reduced");
                Need(HoMigrationTextureBaker.HsvgBase(original,folder+"/Baker",errors)==baked,"production output remains deterministic/reusable");
            }
            foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/B2TextureSamples"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x))
            {
                var input=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                bool scalar=path.Contains("coverage"),emission=path.Contains("emission");
                string hash=Hash(path),meta=Hash(path+".meta");
                var output=Store("real-"+Path.GetFileNameWithoutExtension(path),input.GetPixels(),input.width,input.height,scalar,emission,scalar,new[]{.5f,.001f});
                Need(results.entries.Last().measurement.storedPayloadBytes<=results.entries.Last().measurement.referencePayloadBytes,"real source format never grows");
                Need(Hash(path)==hash&&Hash(path+".meta")==meta,"copied real reference asset/meta unchanged");
            }
            Color[] stress=Enumerable.Range(0,256).Select(i=>new Color((i*73%255)/255f,(i*151%255)/255f,(i*47%255)/255f,adversarial[i%4])).ToArray();
            results.bc7Diagnostic=HoMigrationTextureFormats.EvaluateBc7(stress,16,16,true,new[]{.5f,.001f});
            Need(results.bc7Diagnostic.blockCompressed&&results.bc7Diagnostic.cutoffMismatches>0,"BC7 diagnostic exposes cutout/floor loss; production never silently enables it");
            Need(results.entries.All(e=>e.measurement.profile=="ExactPreview"||!AssetDatabase.LoadAssetAtPath<Texture2D>(e.path).isReadable),"production native assets keep no Readable copy");
            results.checks=checks;File.WriteAllText(Output(),JsonUtility.ToJson(results,true));
            Debug.Log("HO_TEXTURE_PRODUCTION_FORMATS_PASSED "+checks);EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void Reload()
    {
        try
        {
            var report=JsonUtility.FromJson<Results>(File.ReadAllText(Output()));
            foreach(var entry in report.entries)
            {
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.path);
                Need(texture!=null&&texture.format.ToString()==entry.measurement.format,"cross-process native format "+entry.label);
                Need(texture.isReadable==(entry.measurement.profile=="ExactPreview"),"cross-process readability "+entry.label);
                Color actual=Read(texture)[0];
                Need(Mathf.Abs(actual.r-entry.firstPixel.r)<=Mathf.Max(.0001f,Mathf.Abs(entry.firstPixel.r)*.0001f)
                    &&Mathf.Abs(actual.a-entry.firstPixel.a)<.0001f,"cross-process GPU payload "+entry.label);
            }
            Debug.Log("HO_TEXTURE_PRODUCTION_RELOAD_PASSED "+checks);EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static Texture2D Store(string label,Color[] pixels,int width,int height,bool scalar,bool emission,bool coverage,float[] cutoffs)
    {
        var texture=HoMigrationTextureFormats.Create(pixels,width,height,scalar,emission,coverage,cutoffs,FilterMode.Bilinear,TextureWrapMode.Repeat,TextureWrapMode.Clamp,2,out var measurement);
        Debug.Log("HO_FORMAT_MEASUREMENT "+label+" "+JsonUtility.ToJson(measurement));
        string path=folder+"/"+label+".asset";AssetDatabase.CreateAsset(texture,path);AssetDatabase.SaveAssetIfDirty(texture);
        HoMigrationTextureFormats.FinalizeAsset(texture,measurement);
        Need(texture.mipmapCount==measurement.mipCount&&texture.filterMode==FilterMode.Bilinear&&texture.wrapModeU==TextureWrapMode.Repeat&&texture.wrapModeV==TextureWrapMode.Clamp&&texture.anisoLevel==2,
            "mips/filter/wrap/aniso retained "+label);
        Need(measurement.cutoffMismatches==0&&measurement.nonzeroMismatches==0,"selected format passes all-mip semantic checks "+label);
        results.entries.Add(new Entry{label=label,path=path,measurement=measurement,firstPixel=Read(texture)[0]});return texture;
    }
    static Color[] Read(Texture2D texture)
    {
        var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;bool write=GL.sRGBWrite;
        try{GL.sRGBWrite=false;Graphics.Blit(texture,target);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply(false,false);return read.GetPixels();}
        finally{RenderTexture.active=previous;GL.sRGBWrite=write;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(read);}
    }
    static string Output()=>Environment.GetEnvironmentVariable("HO_FORMAT_TEST_OUTPUT")??throw new InvalidOperationException("Set external HO_FORMAT_TEST_OUTPUT");
    static string Hash(string path){using var hash=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));}
    static void Need(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
}
