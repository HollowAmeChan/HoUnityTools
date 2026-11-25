using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HoMigrationBakerChecks
{
    static int checks;
    public static void Configure() { PlayerSettings.colorSpace = ColorSpace.Linear; Debug.Log("HO_BAKER_CONFIG_LINEAR"); }
    public static void Edges()
    {
        try
        {
            Need(Environment.GetEnvironmentVariable("HO_BAKER_FIXTURE")=="1", "private edge fixture");
            var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/HoMigrationBakerFixture2/Sources/Source.mat");
            var source=new Material(original);var errors=new List<string>();
            try
            {
                source.SetTexture("_MainTex",null);source.SetFloat("_AlphaMaskMode",0);
                var constant=HoMigrationTextureBaker.Coverage(source,"Assets/HoMigrationBakerFixture2/EdgeDerived",errors);
                Need(constant!=null && constant.width==1 && errors.Count==0,"constant alpha without a source texture");
                Near(constant.GetPixel(0,0).r,.4f,.0002f,"constant tint alpha retained");
                Need(HoMigrationTextureBaker.Coverage(source,"Assets/HoMigrationBakerFixture2/EdgeDerived",errors)==constant && errors.Count==0,
                    "unchanged persisted recipe can be reused after reload");
                string recipePath=AssetDatabase.GetAssetPath(constant)+".recipe.json";string record=File.ReadAllText(recipePath);
                try
                {
                    File.WriteAllText(recipePath,record+"\n");errors.Clear();
                    Need(HoMigrationTextureBaker.Coverage(source,"Assets/HoMigrationBakerFixture2/EdgeDerived",errors)==null && errors.Count>0 && File.ReadAllText(recipePath)==record+"\n",
                        "edited recipe metadata is not silently reused or overwritten");
                }
                finally{File.WriteAllText(recipePath,record);}
                source.SetFloat("_AlphaMaskMode",1);errors.Clear();
                Need(HoMigrationTextureBaker.Coverage(source,"Assets/HoMigrationBakerFixture2/RejectedSampler",errors)==null && errors.Count>0,
                    "unbound Main sampler is not invented for an assigned mask");
                source.SetTexture("_MainTex",original.GetTexture("_MainTex"));source.SetTextureScale("_AlphaMask",new Vector2(1e30f,1));errors.Clear();
                Need(HoMigrationTextureBaker.Coverage(source,"Assets/HoMigrationBakerFixture2/RejectedHuge",errors)==null && errors.Count>0 && !Directory.Exists("Assets/HoMigrationBakerFixture2/RejectedHuge"),
                    "huge finite ST cannot overflow into a downscaled output");
                Debug.Log("HO_MIGRATION_TEXTURE_BAKER_EDGES_PASSED "+checks);
                File.WriteAllText("Results/migration-baker-edges.json","{\"status\":\"passed\",\"checks\":"+checks+"}");
            }
            finally{Object.DestroyImmediate(source);}
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void Run()
    {
        try
        {
            string suffix = Environment.GetEnvironmentVariable("HO_BAKER_FIXTURE_SUFFIX") ?? "1";
            string root = "Assets/HoMigrationBakerFixture" + suffix;
            Need(Application.isBatchMode && Environment.GetEnvironmentVariable("HO_BAKER_FIXTURE") == "1" && !Directory.Exists(root), "fresh private fixture");
            Need(QualitySettings.activeColorSpace == ColorSpace.Linear, "linear shader working space");
            AssetDatabase.CreateFolder("Assets", "HoMigrationBakerFixture" + suffix);
            AssetDatabase.CreateFolder(root, "Sources");
            string sources = root + "/Sources", derived = root + "/Derived";
            File.WriteAllText(sources + "/Source.shader", SourceShader);
            AssetDatabase.ImportAsset(sources + "/Source.shader", ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(sources + "/Source.shader");
            Need(shader != null, "source input fixture shader");
            var mainPixels = new Color[16]; var maskPixels = new Color[16]; var hdrPixels = new Color[16];
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                mainPixels[x + 4*y] = new Color(.2f + .05f*x, .4f, .8f - .08f*y, .3f);
                maskPixels[x + 4*y] = new Color(.2f + .2f*x, .6f, .8f, .25f + .15f*y);
                hdrPixels[x + 4*y] = new Color(8, 12, 25, .6f);
            }
            var main = Image(sources + "/Main.png", mainPixels, true, false, true);
            var mask = Image(sources + "/Mask.tga", maskPixels, false, false, false);
            var hdr = Image(sources + "/Hdr.exr", hdrPixels, false, true, false);
            Need(!main.isReadable && !mask.isReadable && !hdr.isReadable, "all source formats remain unreadable");
            Need(main.format == TextureFormat.BC7, "compressed BC7 source is sampled through GPU");
            Color[] mainRead = Read(main), maskRead = Read(mask), hdrRead = Read(hdr);
            var source = new Material(shader) { name = "BakerSource" };
            source.SetTexture("_MainTex", main); source.SetTexture("_AlphaMask", mask);
            source.SetTexture("_MainColorAdjustMask", mask); source.SetColor("_Color", new Color(1, 1, 1, .4f));
            source.SetFloat("_AlphaMaskScale", .5f); source.SetFloat("_AlphaMaskValue", .2f);
            Vector4 tone = new Vector4(.13f, .7f, 1.3f, .8f); source.SetVector("_MainTexHSVG", tone);
            source.SetTexture("_EmissionMap", hdr); source.SetTexture("_EmissionBlendMask", mask);
            source.SetTexture("_PBRMap", mask); source.SetTexture("_SSSThicknessMap", mask);
            source.SetColor("_EmissionColor", new Color(3, 5, 7, .34f));
            AssetDatabase.CreateAsset(source, sources + "/Source.mat"); AssetDatabase.SaveAssets();
            string[] sourceFiles = Directory.GetFiles(sources).OrderBy(x => x).ToArray();
            string[] sourceHashes = sourceFiles.Select(Hash).ToArray();
            Texture2D originalCoverage = null;
            for (int mode = 0; mode <= 4; mode++)
            {
                source.SetFloat("_AlphaMaskMode", mode);
                var blockers = new List<string>(); string before = EditorJsonUtility.ToJson(source);
                var output = HoMigrationTextureBaker.Coverage(source, derived, blockers);
                Need(output != null && blockers.Count == 0, "coverage mode " + mode + " " + string.Join(";", blockers));
                Need(before == EditorJsonUtility.ToJson(source), "source material untouched by coverage " + mode);
                for (int i = 0; i < 16; i++)
                {
                    float a = mainRead[i].a * .4f, m = Mathf.Clamp01(maskRead[i].r * .5f + .2f);
                    float expected = mode == 0 ? a : mode == 1 ? m : mode == 2 ? a*m : mode == 3 ? Mathf.Clamp01(a+m) : Mathf.Clamp01(a-m);
                    Near(output.GetPixel(i%4, i/4).r, expected, .0004f, "legacy alpha equation " + mode);
                }
                if (mode == 0) originalCoverage = output;
            }
            source.SetFloat("_AlphaMaskMode", 3);
            var errors = new List<string>();
            var hair = HoMigrationTextureBaker.HairBase(source, derived, errors);
            var hsvg = HoMigrationTextureBaker.HsvgBase(source, derived, errors);
            Need(hair != null && hsvg != null && errors.Count == 0, "Hair/HSVG bake " + string.Join(";", errors));
            for (int i = 0; i < 16; i++)
            {
                Color corrected = Color.LerpUnclamped(mainRead[i], Tone(mainRead[i], tone), maskRead[i].r);
                Color hp = hair.GetPixel(i%4,i/4), vp = hsvg.GetPixel(i%4,i/4);
                Near(hp.r, corrected.r, .0005f, "old pow-before-HSV R"); Near(hp.b, corrected.b, .0005f, "old pow-before-HSV B");
                Near(vp.a, mainRead[i].a, .0004f, "HSVG leaves alpha unchanged");
                Near(hp.a, Mathf.Clamp01(mainRead[i].a*.4f + Mathf.Clamp01(maskRead[i].r*.5f+.2f)), .0004f, "Hair final coverage A");
            }
            Need(!hair.isDataSRGB && hair.format == TextureFormat.RGBAFloat && originalCoverage.format == TextureFormat.RFloat,
                "float color/data outputs avoid another sRGB decode");
            source.SetTextureScale("_MainTex", new Vector2(2,1)); source.SetTextureOffset("_MainTex", new Vector2(.25f,0));
            source.SetTextureScale("_AlphaMask", new Vector2(2,1)); source.SetTextureOffset("_AlphaMask", new Vector2(.25f,0));
            source.SetFloat("_AlphaMaskMode", 1);
            var stCoverage = HoMigrationTextureBaker.Coverage(source, derived, errors);
            Need(stCoverage != null && stCoverage.width == 8 && errors.Count == 0, "integer tiled mask is baked in Main sampling domain");
            var stRecipe = HoMigrationTextureBaker.ReadRecipe(stCoverage);
            Need(stRecipe.destinationST == new Vector4(2,1,.25f,0), "destination BaseMap retains old Main ST");
            for (int x = 0; x < 8; x++)
            {
                int sx = Mathf.FloorToInt(Mathf.Repeat((x+.5f)/8*2+.25f,1)*4);
                Near(stCoverage.GetPixel(x,0).r, Mathf.Clamp01(maskRead[sx].r*.5f+.2f), .0004f, "mask ST applied after Main domain");
            }
            source.SetTextureScale("_AlphaMask", new Vector2(1.5f,1));
            var blocked = new List<string>();
            Need(HoMigrationTextureBaker.Coverage(source, root + "/RejectedUV", blocked) == null && blocked.Count > 0 && !Directory.Exists(root + "/RejectedUV"),
                "unrepresentable periodic UV refused before writes");
            source.SetTextureScale("_MainTex", Vector2.one); source.SetTextureOffset("_MainTex", Vector2.zero);
            source.SetTextureScale("_AlphaMask", Vector2.one); source.SetTextureOffset("_AlphaMask", Vector2.zero);
            source.SetFloat("_UseEmission", 1); source.SetFloat("_EmissionBlend", .7f); source.SetFloat("_EmissionMainStrength", .25f);
            var rgba = HoMigrationTextureBaker.EmissionRgba(source, derived, false, errors);
            Need(rgba != null && errors.Count == 0, "Skin/Eye emission RGBA bake");
            for (int i = 0; i < 16; i++)
            {
                Color p = rgba.GetPixel(i%4,i/4);
                Near(p.b, hdrRead[i].b*maskRead[i].b, .003f, "HDR emission RGB retained without constant tint");
                Near(p.a, hdrRead[i].a*maskRead[i].a*.7f, .0005f, "emission RGBA mask alpha and blend");
            }
            blocked.Clear();
            Need(HoMigrationTextureBaker.EmissionRgba(source, root + "/RejectedAlbedo", true, blocked) == null && blocked.Count > 0,
                "Cloth cannot statically erase live emission/albedo influence");
            source.SetFloat("_EmissionMainStrength", 0);
            var premult = HoMigrationTextureBaker.EmissionRgba(source, derived, true, errors);
            Need(premult != null && errors.Count == 0, "Cloth emission folds map alpha and blend");
            Near(premult.GetPixel(0,0).b, hdrRead[0].b*maskRead[0].b*hdrRead[0].a*maskRead[0].a*.7f, .003f, "Cloth premultiplied map RGB");
            Near(premult.GetPixel(0,0).a, 1, .0001f, "Cloth output alpha neutral");
            source.SetVector("_EmissionMap_ScrollRotate", new Vector4(1,0,0,0)); blocked.Clear();
            Need(HoMigrationTextureBaker.EmissionRgba(source, root + "/RejectedAnimated", false, blocked) == null && blocked.Count > 0,
                "active emission animation blocked");
            source.SetFloat("_UseEmission", 0); blocked.Clear();
            Need(HoMigrationTextureBaker.EmissionRgba(source, derived, false, blocked) != null && blocked.Count == 0,
                "disabled emission does not revive dormant animation");
            source.SetFloat("_UseEmission", 1); source.SetVector("_EmissionMap_ScrollRotate", Vector4.zero);
            source.SetTextureScale("_MainTex", new Vector2(3,1)); source.SetTextureOffset("_MainTex", new Vector2(.1f,0));
            source.SetTextureScale("_EmissionMap", new Vector2(2,1)); source.SetTextureOffset("_EmissionMap", new Vector2(.25f,0));
            source.SetTextureScale("_EmissionBlendMask", new Vector2(2,2)); source.SetTextureOffset("_EmissionBlendMask", new Vector2(.25f,.25f));
            var emissionUV = HoMigrationTextureBaker.EmissionRgba(source, derived, false, errors);
            Need(emissionUV != null && emissionUV.width == 8 && emissionUV.height == 8 && errors.Count == 0 &&
                HoMigrationTextureBaker.ReadRecipe(emissionUV).destinationST == new Vector4(1,1,0,0), "emission output is raw UV with identity target ST");
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int sx = Mathf.FloorToInt(Mathf.Repeat((x+.5f)/8*2+.25f,1)*4), sy = Mathf.FloorToInt(Mathf.Repeat((y+.5f)/8*2+.25f,1)*4);
                Near(emissionUV.GetPixel(x,y).a, hdrRead[0].a*maskRead[sx+4*sy].a*.7f, .0005f,
                    "emission independent raw mask ST ignores nonidentity Main ST");
            }
            source.SetFloat("_EmissionMap_UVMode",1); blocked.Clear();
            Need(HoMigrationTextureBaker.EmissionRgba(source, root + "/RejectedUV1", false, blocked)==null && blocked.Count>0,
                "emission on alternate UV set is blocked");
            source.SetFloat("_EmissionMap_UVMode",0);
            source.SetTextureScale("_MainTex",Vector2.one);source.SetTextureOffset("_MainTex",Vector2.zero);
            source.SetTextureScale("_PBRMap", new Vector2(1.5f,.5f));
            var channel = HoMigrationTextureBaker.Channel(source, "_PBRMap", 3, derived, errors);
            Need(channel != null && errors.Count == 0, "packed channel extraction keeps original texture domain");
            for (int i = 0; i < 16; i++) Near(channel.GetPixel(i%4,i/4).r, maskRead[i].a, .0004f, "packed A to linear R");
            source.SetFloat("_UseSSS", 1); source.SetFloat("_HoSurfaceThickness", .15f); source.SetFloat("_SSSThicknessInvert", 1);
            source.SetFloat("_SSSPower", 2); source.SetFloat("_SSSStrength", .8f); source.SetFloat("_HoSSSThicknessScale", 1.5f);
            source.SetTextureScale("_SSSThicknessMap", new Vector2(1.5f,.5f));
            var thinness = HoMigrationTextureBaker.SssThinness(source, derived, errors);
            Need(thinness != null && errors.Count == 0, "HoSurface numeric thinness bake");
            for (int i = 0; i < 16; i++) Near(thinness.GetPixel(i%4,i/4).r,
                Mathf.Max(.15f, Mathf.Clamp01(Mathf.Pow(1-maskRead[i].r,2)*.8f*1.5f)), .0005f, "SSS max/invert/power/scale; ignores unused map ST");
            source.SetTextureScale("_MainTex",new Vector2(2,1));source.SetTextureOffset("_MainTex",new Vector2(.25f,0));
            var thinnessUV=HoMigrationTextureBaker.SssThinness(source,derived,errors);
            Need(thinnessUV!=null && thinnessUV.width==8 && errors.Count==0,"thinness bakes Main ST into raw UV domain");
            for(int x=0;x<8;x++){
                int sx=Mathf.FloorToInt(Mathf.Repeat((x+.5f)/8*2+.25f,1)*4);
                Near(thinnessUV.GetPixel(x,0).r,Mathf.Max(.15f,Mathf.Clamp01(Mathf.Pow(1-maskRead[sx].r,2)*.8f*1.5f)),.0005f,"thinness source Main sampling transform");
            }
            source.SetTextureScale("_MainTex",Vector2.one);source.SetTextureOffset("_MainTex",Vector2.zero);
            source.SetFloat("_AlphaMaskMode", 0);
            var repeat = HoMigrationTextureBaker.Coverage(source, derived, errors);
            Need(repeat == originalCoverage && errors.Count == 0, "same recipe reuses owned unchanged texture");
            var recipe = HoMigrationTextureBaker.ReadRecipe(repeat);
            Need(recipe != null && recipe.inputs.All(i => i.path == "" || i.fileSha256.Length == 64) && recipe.outputGuid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(repeat)),
                "recipe records source hashes and output identity");
            repeat.filterMode = FilterMode.Trilinear; EditorUtility.SetDirty(repeat); AssetDatabase.SaveAssetIfDirty(repeat);
            string modified = Hash(AssetDatabase.GetAssetPath(repeat)); blocked.Clear();
            Need(HoMigrationTextureBaker.Coverage(source, derived, blocked) == null && blocked.Count > 0 && modified == Hash(AssetDatabase.GetAssetPath(repeat)),
                "modified user resource is not overwritten");
            Need(sourceHashes.SequenceEqual(sourceFiles.Select(Hash)), "all source asset/meta bytes unchanged");
            Need(!main.isReadable && !mask.isReadable && !hdr.isReadable, "source importer Readable flags unchanged");
            File.WriteAllText("Results/migration-baker-" + suffix + ".json", "{\"status\":\"passed\",\"checks\":" + checks + "}");
            Debug.Log("HO_MIGRATION_TEXTURE_BAKER_PASSED " + checks); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Need(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks++; }
    static void Near(float actual, float expected, float tolerance, string label) => Need(Mathf.Abs(actual-expected)<=tolerance, label+" expected="+expected+" actual="+actual);
    static string Hash(string path) { using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    static Texture2D Image(string path, Color[] values, bool srgb, bool exr, bool bc7)
    {
        var texture = new Texture2D(4,4,TextureFormat.RGBAFloat,false,true); texture.SetPixels(values);texture.Apply();
        byte[] bytes=exr?texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat):path.EndsWith(".tga")?texture.EncodeToTGA():texture.EncodeToPNG();
        File.WriteAllBytes(path,bytes);Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=srgb;importer.isReadable=false;importer.mipmapEnabled=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Point;importer.wrapMode=path.EndsWith(".tga")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
        if(bc7){var settings=importer.GetPlatformTextureSettings("Standalone");settings.overridden=true;settings.format=TextureImporterFormat.BC7;settings.maxTextureSize=64;importer.SetPlatformTextureSettings(settings);}
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Color[] Read(Texture2D source)
    {
        var rt=RenderTexture.GetTemporary(4,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var old=RenderTexture.active;bool write=GL.sRGBWrite;
        var copy=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        try{GL.sRGBWrite=false;Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,4,4),0,0);copy.Apply();return copy.GetPixels();}
        finally{RenderTexture.active=old;GL.sRGBWrite=write;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(copy);}
    }
    static Color Tone(Color source,Vector4 tone)
    {
        Color p=new Color(Mathf.Pow(Mathf.Abs(source.r),tone.w),Mathf.Pow(Mathf.Abs(source.g),tone.w),Mathf.Pow(Mathf.Abs(source.b),tone.w),source.a);
        Color.RGBToHSV(p,out float h,out float s,out float v);return Color.HSVToRGB(Mathf.Repeat(h+tone.x,1),Mathf.Clamp01(s*tone.y),Mathf.Clamp01(v*tone.z),true);
    }
    const string SourceShader=@"Shader ""Hidden/HoMigrationTests/InputContract""{Properties{
_MainTex(""Main"",2D)=""white""{} _AlphaMask(""Mask"",2D)=""white""{} _MainColorAdjustMask(""Adjust"",2D)=""white""{}
_Color(""Tint"",Color)=(1,1,1,1) _AlphaMaskMode(""Mode"",Float)=0 _AlphaMaskScale(""Scale"",Float)=1 _AlphaMaskValue(""Offset"",Float)=0
_MainTexHSVG(""Tone"",Vector)=(0,1,1,1) _MainGradationStrength(""Grad"",Float)=0 _UseParallax(""Parallax"",Float)=0 _UseDither(""Dither"",Float)=0
_DissolveParams(""Dissolve"",Vector)=(0,0,0,0) _DistanceFade(""Fade"",Vector)=(0,0,0,0)
_UseEmission(""Emit"",Float)=0 _EmissionMap(""EmitMap"",2D)=""white""{} _EmissionBlendMask(""EmitMask"",2D)=""white""{}
_EmissionColor(""EmitColor"",Color)=(1,1,1,1) _EmissionBlend(""Blend"",Float)=1 _EmissionMainStrength(""Albedo"",Float)=0
_EmissionMap_UVMode(""UV"",Float)=0 _EmissionParallaxDepth(""Depth"",Float)=0 _EmissionFluorescence(""Fluo"",Float)=0
_EmissionMap_ScrollRotate(""Scroll"",Vector)=(0,0,0,0) _EmissionBlendMask_ScrollRotate(""MaskScroll"",Vector)=(0,0,0,0) _EmissionBlink(""Blink"",Vector)=(0,0,0,0)
_EmissionUseGrad(""Grad"",Float)=0 _EmissionBlendMode(""Mode"",Float)=1
_PBRMap(""Packed"",2D)=""white""{} _UseSSS(""SSS"",Float)=0 _HoSurfaceThickness(""Thin"",Float)=0 _SSSThicknessMap(""ThinMap"",2D)=""white""{}
_SSSThicknessInvert(""Invert"",Float)=0 _SSSPower(""Power"",Float)=1 _SSSStrength(""Strength"",Float)=1 _HoSSSThicknessScale(""Scale"",Float)=1
}SubShader{Pass{HLSLPROGRAM
#pragma vertex V
#pragma fragment F
float4 V(float3 p:POSITION):SV_Position{return float4(p,1);}float4 F():SV_Target{return 0;}
ENDHLSL}}}";
}
