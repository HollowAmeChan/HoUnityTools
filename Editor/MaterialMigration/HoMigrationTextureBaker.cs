using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    /// <summary>
    /// Static LOD0 material-input baking. The caller must separately prove that clips,
    /// scripts and MPBs do not animate these inputs. No source importer is modified.
    /// Base/Coverage output uses the old MainTex sampling domain: retain MainTex ST on
    /// destination BaseMap. Emission/SSS uses raw UV0 and identity destination ST.
    /// Constant base tint and emission HDR color remain the converter's responsibility.
    /// </summary>
    public static class HoMigrationTextureBaker
    {
        private const string Owner = "HoUnityTools.HoMigrationTextureBaker";
        private static readonly Vector4 Identity = new(1, 1, 0, 0);
        private enum Operation { Coverage, HairBase, HsvgBase, EmissionRgba, SssThinness, Channel }

        [Serializable] public sealed class Input
        {
            public string property, path, guid, dependencyHash, fileSha256, metaSha256;
            public long localId;
            public int width, height, filter, wrapU, wrapV, anisotropy;
            public bool srgb;
            public Vector4 st;
        }
        [Serializable] public sealed class Recipe
        {
            public string owner = Owner;
            public int version = 2;
            public string operation, definitionHash, sourceMaterialPath, sourceMaterialGuid, sourceMaterialSha256,
                sourceMaterialMetaSha256, sourceShaderPath, sourceShaderSha256, unity, graphicsApi, colorSpace,
                domain, outputFormat, requestedOutputFormat, storageProfile, shaderSha256, outputGuid, outputSha256, outputMetaSha256;
            public int storagePolicyVersion;
            public HoMigrationTextureFormats.Measurement storage;
            public long sourceMaterialLocalId;
            public int width, height, filter, wrapU, wrapV, anisotropy;
            public Vector4 destinationST;
            public List<string> parameters = new();
            public List<Input> inputs = new();
            public string limitations = "Static UV0, LOD0 sampled at output texel centers; baked nonlinear filtering/mips approximate source runtime filtering. External animation/script/MPB proof is required from the converter. Production color may use sRGB-encoded RGBA32; float/half and scalar outputs store working-space data. Format and all-mip numeric/coverage evidence are recorded. Normal inputs are not unpacked or altered.";
        }

        public static Texture2D Coverage(Material source, string derivedAssetFolder, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.Coverage);
        public static Texture2D HairBase(Material source, string derivedAssetFolder, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.HairBase);
        public static Texture2D HsvgBase(Material source, string derivedAssetFolder, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.HsvgBase);
        public static Texture2D EmissionRgba(Material source, string derivedAssetFolder, bool premultiplyAlphaForCloth, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.EmissionRgba, premultiply: premultiplyAlphaForCloth);
        public static Texture2D SssThinness(Material source, string derivedAssetFolder, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.SssThinness);
        public static Texture2D Channel(Material source, string property, int channel, string derivedAssetFolder, List<string> blockers) =>
            Bake(source, derivedAssetFolder, blockers, Operation.Channel, property: property, channel: channel);

        public static Recipe ReadRecipe(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            return string.IsNullOrEmpty(path) ? null : ReadRecipeFile(Absolute(path + ".recipe.json"));
        }

        private sealed class Map
        {
            internal string Property;
            internal Texture2D Texture, Sampler;
            internal Vector4 ST = Identity;
        }

        private static Texture2D Bake(Material source, string folder, List<string> blockers, Operation operation,
            bool premultiply = false, string property = null, int channel = 0)
        {
            if (blockers == null) throw new ArgumentNullException(nameof(blockers));
            int initial = blockers.Count;
            void Block(string message) => blockers.Add(operation + ": " + message);
            if (source == null || source.shader == null) { Block("Missing source material/shader."); return null; }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Block("A graphics device is required; no Readable/importer fallback is performed."); return null; }
            string assetFolder;
            HoMigrationTextureFormats.OutputProfile storageProfile;
            try { assetFolder = ValidateFolder(folder); storageProfile = HoMigrationTextureFormats.Profile; }
            catch (Exception error) { Block(error.Message); return null; }
            var recipe = new Recipe { operation = operation.ToString(), unity = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString(),
                sourceMaterialPath = AssetDatabase.GetAssetPath(source), sourceShaderPath = AssetDatabase.GetAssetPath(source.shader),
                shaderSha256 = HashText(BakeShader), destinationST = Identity,
                storageProfile = storageProfile.ToString(), storagePolicyVersion = HoMigrationTextureFormats.PolicyVersion };
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out recipe.sourceMaterialGuid, out recipe.sourceMaterialLocalId);
            recipe.sourceMaterialSha256 = FileHashOrEmpty(recipe.sourceMaterialPath);
            recipe.sourceMaterialMetaSha256 = FileHashOrEmpty(recipe.sourceMaterialPath + ".meta");
            recipe.sourceShaderSha256 = FileHashOrEmpty(recipe.sourceShaderPath);

            float Float(string name, float fallback = 0)
            {
                float value = source.HasProperty(name) ? source.GetFloat(name) : fallback;
                if (!Finite(value)) Block("Nonfinite " + name);
                recipe.parameters.Add(name + "=" + value.ToString("R", CultureInfo.InvariantCulture));
                return value;
            }
            Vector4 Vector(string name, Vector4 fallback)
            {
                Vector4 value = source.HasProperty(name) ? source.GetVector(name) : fallback;
                if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w)) Block("Nonfinite " + name);
                recipe.parameters.Add(name + "=" + Components(value));
                return value;
            }
            Map Texture(string name, bool ownSampler = false)
            {
                UnityEngine.Texture value = source.HasProperty(name) ? source.GetTexture(name) : null;
                if (value != null && !(value is Texture2D)) Block(name + " must be a static Texture2D.");
                var map = new Map { Property = name, Texture = value as Texture2D };
                if (map.Texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(map.Texture)))
                    Block(name + " is a nonpersistent texture; an immutable source hash is required.");
                if (source.HasProperty(name))
                {
                    Vector2 scale = source.GetTextureScale(name), offset = source.GetTextureOffset(name);
                    map.ST = new Vector4(scale.x, scale.y, offset.x, offset.y);
                }
                if (!Finite(map.ST.x) || !Finite(map.ST.y) || !Finite(map.ST.z) || !Finite(map.ST.w)) Block("Nonfinite " + name + " ST.");
                map.Sampler = ownSampler ? map.Texture : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") as Texture2D : null;
                string path = map.Texture == null ? "" : AssetDatabase.GetAssetPath(map.Texture);
                string guid = ""; long localId = 0;
                if (map.Texture != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(map.Texture, out guid, out localId);
                recipe.inputs.Add(new Input { property = name, path = path, guid = guid, localId = localId,
                    dependencyHash = string.IsNullOrEmpty(path) ? "constant-white" : AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    fileSha256 = FileHashOrEmpty(path), metaSha256 = FileHashOrEmpty(path + ".meta"),
                    width = map.Texture == null ? 1 : map.Texture.width, height = map.Texture == null ? 1 : map.Texture.height,
                    srgb = map.Texture != null && map.Texture.isDataSRGB, st = map.ST,
                    filter = (int)Filter(map.Sampler), wrapU = (int)Wrap(map.Sampler, true), wrapV = (int)Wrap(map.Sampler, false),
                    anisotropy = map.Sampler == null ? 1 : map.Sampler.anisoLevel });
                return map;
            }

            Map main = Texture("_MainTex", true), mask = null, adjust = null, emission = null, sss = null, selected = null;
            Vector4 tone = new(0, 1, 1, 1), maskST = Identity, emissionST = Identity, emissionMaskST = Identity;
            int alphaMode = 0;
            float alpha = 1, maskScale = 1, maskValue = 0, emissionEnabled = 0, blend = 1,
                useSSS = 0, thickness = 0, invert = 0, power = 1, strength = 1, thicknessScale = 1;
            var domainInputs = new List<Map>();
            bool baseDomain = operation == Operation.Coverage || operation == Operation.HairBase || operation == Operation.HsvgBase;
            if (baseDomain)
            {
                recipe.domain = "q=rawUV0*oldMainScale+oldMainOffset; retain old Main ST on destination BaseMap";
                recipe.destinationST = main.ST;
                domainInputs.Add(new Map { Texture = main.Texture, Sampler = main.Sampler, ST = Identity, Property = "_MainTex" });
                if (Float("_UseParallax") != 0) Block("Parallax/POM depends on the view and cannot be baked into this domain.");
                if (operation != Operation.HsvgBase)
                {
                    if (Float("_UseDither") != 0 || Vector("_DissolveParams", Vector4.zero).x != 0 || Vector("_DistanceFade", Vector4.zero).w != 0)
                        Block("Dither/dissolve/distance coverage requires its live evaluator.");
                    alpha = source.HasProperty("_Color") ? source.GetColor("_Color").a : 1;
                    recipe.parameters.Add("_Color.a=" + alpha.ToString("R", CultureInfo.InvariantCulture));
                    if (!Finite(alpha) || alpha < 0 || alpha > 1) Block("Tint alpha outside [0,1] requires an explicit conversion decision.");
                    alphaMode = (int)Float("_AlphaMaskMode");
                    if (alphaMode < 0 || alphaMode > 4) Block("Unknown alpha-mask mode.");
                    if (alphaMode != 0)
                    {
                        mask = Texture("_AlphaMask"); maskST = mask.ST;
                        maskScale = Float("_AlphaMaskScale", 1); maskValue = Float("_AlphaMaskValue"); domainInputs.Add(mask);
                    }
                }
                if (operation != Operation.Coverage)
                {
                    tone = Vector("_MainTexHSVG", new Vector4(0, 1, 1, 1));
                    if (Float("_MainGradationStrength") != 0) Block("Main gradation is not part of the static HSVG subset.");
                    adjust = Texture("_MainColorAdjustMask");
                    // Source adjust mask uses Main UV directly; its own saved ST is not consumed.
                    domainInputs.Add(new Map { Texture = adjust.Texture, Sampler = main.Sampler, ST = Identity, Property = adjust.Property });
                }
            }
            else if (operation == Operation.EmissionRgba)
            {
                recipe.domain = "rawUV0; destination EmissionMap ST=identity; constant HDR color remains external";
                emissionEnabled = Float("_UseEmission");
                if (emissionEnabled != 0)
                {
                    emission = Texture("_EmissionMap", true); mask = Texture("_EmissionBlendMask");
                    emissionST = emission.ST; emissionMaskST = mask.ST; domainInputs.Add(emission); domainInputs.Add(mask);
                    if (Float("_EmissionMap_UVMode") != 0 || Float("_EmissionParallaxDepth") != 0 || Float("_EmissionFluorescence") != 0)
                        Block("Emission requires static UV0 without parallax/lighting-dependent fluorescence.");
                    CheckScroll(Vector("_EmissionMap_ScrollRotate", Vector4.zero), "emission", Block);
                    CheckScroll(Vector("_EmissionBlendMask_ScrollRotate", Vector4.zero), "emission mask", Block);
                    Vector4 blink = Vector("_EmissionBlink", Vector4.zero);
                    float blinkWeight = 1;
                    if (blink.x != 0)
                    {
                        if (blink.z != 0) Block("Emission blink is animated.");
                        else
                        {
                            float sample = Mathf.Sin(blink.w) * .5f + .5f;
                            if (blink.y > .5f) sample = Mathf.Round(sample);
                            blinkWeight = Mathf.LerpUnclamped(1, sample, blink.x);
                        }
                    }
                    if (Float("_EmissionUseGrad") != 0 || Float("_EmissionBlendMode", 1) != 1) Block("Emission gradation/non-add compositing is unsupported.");
                    blend = Float("_EmissionBlend", 1) * blinkWeight;
                    float mainStrength = Float("_EmissionMainStrength");
                    if (premultiply && mainStrength != 0) Block("Destination has no live emission-albedo influence; it cannot be folded into a static emission map.");
                }
                recipe.parameters.Add("premultiplyMapAlpha=" + premultiply);
                recipe.parameters.Add("constantColorContract=" + (premultiply ? "destination HDR RGB=oldRGB*oldA; A=1" : "retain old HDR RGBA and MainStrength"));
            }
            else if (operation == Operation.SssThinness)
            {
                recipe.domain = "rawUV0; destination thinness-map ST=identity";
                useSSS = Float("_UseSSS"); thickness = Float("_HoSurfaceThickness");
                if (useSSS != 0)
                {
                    sss = Texture("_SSSThicknessMap");
                    domainInputs.Add(new Map { Texture = sss.Texture, Sampler = main.Sampler, ST = main.ST, Property = sss.Property });
                    invert = Float("_SSSThicknessInvert"); power = Float("_SSSPower", 1);
                    strength = Float("_SSSStrength", 1); thicknessScale = Float("_HoSSSThicknessScale", 1);
                    if (Float("_UseParallax") != 0) Block("SSS Main UV depends on parallax.");
                }
            }
            else
            {
                recipe.domain = "original selected texture UV; ST not baked; converter composes source Main/Map ST";
                if (string.IsNullOrEmpty(property) || !source.HasProperty(property) || channel < 0 || channel > 3)
                    Block("Channel requires a declared texture property and RGBA channel0..3.");
                else
                {
                    selected = Texture(property, property == "_EmissionMap");
                    if (selected.Texture == null) Block("Channel source has no assigned texture.");
                    domainInputs.Add(new Map { Texture = selected.Texture, Sampler = selected.Sampler, ST = Identity, Property = property });
                }
                recipe.parameters.Add("channel=" + channel);
            }

            TextureWrapMode wrapU = Wrap(main.Sampler, true), wrapV = Wrap(main.Sampler, false);
            foreach (bool horizontal in new[] { true, false })
            {
                var active = domainInputs.Where(m => m.Texture != null).ToArray();
                TextureWrapMode wrap = active.Length == 0 ? TextureWrapMode.Repeat : Wrap(active[0].Sampler, horizontal);
                foreach (var map in active)
                {
                    if (map.Sampler == null) Block(map.Property + " depends on an unbound Main sampler; its sampling state is not guessed.");
                    if (Wrap(map.Sampler, horizontal) != wrap) Block("Mixed effective wrap domains cannot be represented by one output texture.");
                    if (!Periodic(map.ST, wrap, horizontal)) Block(map.Property + " ST cannot be represented in this output's wrap domain; retain a live evaluator.");
                }
                if (horizontal) wrapU = wrap; else wrapV = wrap;
            }
            int width = 1, height = 1;
            foreach (var map in domainInputs.Where(m => m.Texture != null))
            {
                double neededWidth = map.Texture.width * Math.Max(Math.Abs((double)map.ST.x), 1);
                double neededHeight = map.Texture.height * Math.Max(Math.Abs((double)map.ST.y), 1);
                if (neededWidth > SystemInfo.maxTextureSize || neededHeight > SystemInfo.maxTextureSize)
                    Block("Required bake resolution exceeds this graphics device; no silent downscale is allowed.");
                else
                {
                    width = Math.Max(width, (int)Math.Ceiling(neededWidth));
                    height = Math.Max(height, (int)Math.Ceiling(neededHeight));
                }
            }
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize) Block("Required bake resolution exceeds this graphics device; no silent downscale is allowed.");
            if (blockers.Count != initial) return null;
            Map reference = domainInputs.FirstOrDefault(m => m.Texture != null);
            Texture2D sampler = reference?.Sampler;
            bool scalar = operation == Operation.Coverage || operation == Operation.SssThinness || operation == Operation.Channel;
            bool coverage = operation == Operation.Coverage || operation == Operation.HairBase || operation == Operation.HsvgBase;
            float[] cutoffs = new[] { source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f,
                source.HasProperty("_SubpassCutoff") ? source.GetFloat("_SubpassCutoff") : .5f, .001f }
                .Where(v => Finite(v) && v >= 0 && v <= 1).Distinct().OrderBy(v => v).ToArray();
            recipe.parameters.Add("storageCutoffs=" + string.Join(",", cutoffs.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            recipe.width = width; recipe.height = height;
            recipe.requestedOutputFormat = storageProfile == HoMigrationTextureFormats.OutputProfile.ExactPreview
                ? scalar ? "RFloat linear" : "RGBAFloat linear-data"
                : scalar ? "Production scalar" : operation == Operation.EmissionRgba ? "Production HDR emission" : "Production color";
            recipe.outputFormat = recipe.requestedOutputFormat;
            recipe.filter = (int)Filter(sampler); recipe.wrapU = (int)wrapU; recipe.wrapV = (int)wrapV; recipe.anisotropy = sampler == null ? 1 : sampler.anisoLevel;
            recipe.parameters.Sort(StringComparer.Ordinal);
            recipe.definitionHash = DefinitionHash(recipe);
            string slug = new string(source.name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').Take(40).ToArray());
            string assetPath = assetFolder + "/" + slug + "_" + operation + "_" + recipe.definitionHash.Substring(0, 20) + ".asset";
            string recordPath = Absolute(assetPath + ".recipe.json");
            if (File.Exists(Absolute(assetPath)) || File.Exists(Absolute(assetPath + ".meta")) || File.Exists(recordPath) || File.Exists(recordPath + ".meta"))
            {
                var old = ReadRecipeFile(recordPath);
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (old != null && old.owner == Owner && old.definitionHash == recipe.definitionHash && DefinitionHash(old) == old.definitionHash &&
                    File.ReadAllText(recordPath) == JsonUtility.ToJson(old, true) && existing != null && !EditorUtility.IsDirty(existing) &&
                    old.outputGuid == AssetDatabase.AssetPathToGUID(assetPath) && old.outputSha256 == FileHashOrEmpty(assetPath) && old.outputMetaSha256 == FileHashOrEmpty(assetPath + ".meta")) return existing;
                Block("Refusing to overwrite an existing or modified resource: " + assetPath); return null;
            }

            Shader shader = null; Material material = null; Texture2D readback = null, result = null;
            RenderTexture target = null; RenderTexture previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite, previousAsync = ShaderUtil.allowAsyncCompilation;
            string createdGuid = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                shader = ShaderUtil.CreateShaderAsset(BakeShader, false); shader.hideFlags = HideFlags.HideAndDontSave;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_Operation", (int)operation); material.SetInt("_Channel", channel);
                material.SetTexture("_MainTex", main.Texture == null ? Texture2D.whiteTexture : main.Texture);
                material.SetTexture("_MaskTex", mask?.Texture == null ? Texture2D.whiteTexture : mask.Texture);
                material.SetTexture("_AdjustTex", adjust?.Texture == null ? Texture2D.whiteTexture : adjust.Texture);
                material.SetTexture("_EmissionTex", emission?.Texture == null ? Texture2D.whiteTexture : emission.Texture);
                material.SetTexture("_SssTex", sss?.Texture == null ? Texture2D.whiteTexture : sss.Texture);
                material.SetTexture("_ChannelTex", selected?.Texture == null ? Texture2D.whiteTexture : selected.Texture);
                material.SetVector("_MainST", main.ST); material.SetVector("_MaskST", maskST);
                material.SetVector("_EmissionST", emissionST); material.SetVector("_EmissionMaskST", emissionMaskST);
                material.SetVector("_Tone", tone); material.SetFloat("_TintAlpha", alpha); material.SetInt("_MaskMode", alphaMode);
                material.SetFloat("_MaskScale", maskScale); material.SetFloat("_MaskValue", maskValue);
                material.SetFloat("_EmissionEnabled", emissionEnabled); material.SetFloat("_EmissionBlend", blend);
                material.SetFloat("_Premultiply", premultiply ? 1 : 0);
                material.SetVector("_Sss", new Vector4(useSSS, thickness, invert, power));
                material.SetVector("_SssStrength", new Vector4(strength, thicknessScale, 0, 0));
                ShaderUtil.CompilePass(material, 0, true);
                if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Transient bake shader did not compile.");
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                GL.sRGBWrite = false;
                Graphics.Blit(main.Texture == null ? Texture2D.whiteTexture : main.Texture, target, material, 0);
                RenderTexture.active = target;
                readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply(false, false);
                Color[] pixels = readback.GetPixels();
                if (pixels.Any(p => !Finite(p.r) || !Finite(p.g) || !Finite(p.b) || !Finite(p.a))) throw new InvalidOperationException("Bake produced nonfinite pixels.");
                result = HoMigrationTextureFormats.Create(pixels, width, height, scalar, operation == Operation.EmissionRgba, coverage, cutoffs,
                    (FilterMode)recipe.filter, wrapU, wrapV, recipe.anisotropy, out recipe.storage);
                result.name = slug + "_" + operation;
                recipe.outputFormat = recipe.storage.format + " / " + recipe.storage.encoding;
                CreateFolders(assetFolder);
                AssetDatabase.CreateAsset(result, assetPath); createdGuid = AssetDatabase.AssetPathToGUID(assetPath);
                AssetDatabase.SaveAssetIfDirty(result);
                HoMigrationTextureFormats.FinalizeAsset(result, recipe.storage);
                recipe.outputGuid = createdGuid; recipe.outputSha256 = FileHashOrEmpty(assetPath); recipe.outputMetaSha256 = FileHashOrEmpty(assetPath + ".meta");
                using (var stream = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(JsonUtility.ToJson(recipe, true));
                AssetDatabase.ImportAsset(assetPath + ".recipe.json", ImportAssetOptions.ForceSynchronousImport);
                return result;
            }
            catch (Exception error)
            {
                if (!string.IsNullOrEmpty(createdGuid) && AssetDatabase.AssetPathToGUID(assetPath) == createdGuid) AssetDatabase.DeleteAsset(assetPath);
                Block(error.Message); return null;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousWrite; ShaderUtil.allowAsyncCompilation = previousAsync;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readback != null) Object.DestroyImmediate(readback);
                if (result != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(result))) Object.DestroyImmediate(result);
                if (material != null) Object.DestroyImmediate(material);
                if (shader != null) Object.DestroyImmediate(shader);
            }
        }

        private static void CheckScroll(Vector4 value, string role, Action<string> block)
        {
            if (value.x != 0 || value.y != 0 || value.w != 0) block(role + " scroll/rotation is animated.");
            if (value.z != 0) block(role + " fixed rotation is outside this static UV0 subset.");
        }
        private static bool Periodic(Vector4 st, TextureWrapMode wrap, bool horizontal)
        {
            float scale = horizontal ? st.x : st.y, offset = horizontal ? st.z : st.w;
            if (Mathf.Abs(scale) < 1e-7f) return true;
            if (wrap == TextureWrapMode.Repeat) return Mathf.Abs(scale - Mathf.Round(scale)) < 1e-6f;
            if (wrap == TextureWrapMode.Mirror) return Mathf.Abs(scale - Mathf.Round(scale)) < 1e-6f && Mathf.Abs(offset - Mathf.Round(offset)) < 1e-6f;
            if (wrap == TextureWrapMode.Clamp) return scale > 0 ? offset <= 0 && scale + offset >= 1 : offset >= 1 && scale + offset <= 0;
            return st == Identity;
        }
        private static TextureWrapMode Wrap(Texture2D texture, bool horizontal) => texture == null ? TextureWrapMode.Repeat : horizontal ? texture.wrapModeU : texture.wrapModeV;
        private static FilterMode Filter(Texture2D texture) => texture == null ? FilterMode.Bilinear : texture.filterMode;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string Components(Vector4 value) => string.Join(",", new[] { value.x, value.y, value.z, value.w }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        private static string ValidateFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("A dedicated Assets subfolder is required.");
            string normalized = folder.Replace('\\', '/').TrimEnd('/');
            string absolute = Absolute(normalized), assets = Path.GetFullPath(Application.dataPath);
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !absolute.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Derived assets must be in an Assets subfolder of the current project.");
            return "Assets/" + absolute.Substring(assets.Length + 1).Replace('\\', '/');
        }
        private static void CreateFolders(string folder)
        {
            string current = "Assets";
            foreach (string part in folder.Substring(7).Split('/'))
            { string next = current + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part); current = next; }
        }
        private static Recipe ReadRecipeFile(string path)
        {
            try { return File.Exists(path) ? JsonUtility.FromJson<Recipe>(File.ReadAllText(path)) : null; }
            catch { return null; }
        }
        private static string DefinitionHash(Recipe recipe)
        {
            string definition = recipe.definitionHash, guid = recipe.outputGuid, content = recipe.outputSha256, meta = recipe.outputMetaSha256;
            string format = recipe.outputFormat;
            var storage = recipe.storage;
            try
            {
                recipe.definitionHash = recipe.outputGuid = recipe.outputSha256 = recipe.outputMetaSha256 = null;
                recipe.outputFormat = string.IsNullOrEmpty(recipe.requestedOutputFormat) ? format : recipe.requestedOutputFormat;
                recipe.storage = null;
                return HashText(JsonUtility.ToJson(recipe));
            }
            finally { recipe.definitionHash = definition; recipe.outputGuid = guid; recipe.outputSha256 = content; recipe.outputMetaSha256 = meta; recipe.outputFormat = format; recipe.storage = storage; }
        }
        private static string Absolute(string path)
        {
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssetPath(path);
                if (package != null) return Path.GetFullPath(Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length).TrimStart('/', '\\')));
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
        private static string FileHashOrEmpty(string path)
        {
            if (string.IsNullOrEmpty(path) || path == ".meta") return "";
            string absolute = Absolute(path);
            if (!File.Exists(absolute)) return "";
            using var sha = SHA256.Create(); using var stream = File.OpenRead(absolute);
            return Hex(sha.ComputeHash(stream));
        }
        private static string HashText(string value) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        private const string BakeShader = @"Shader ""Hidden/HoMigration/StaticTextureBake"" {
Properties { _MainTex(""Main"",2D)=""white""{} _MaskTex(""Mask"",2D)=""white""{} _AdjustTex(""Adjust"",2D)=""white""{} _EmissionTex(""Emission"",2D)=""white""{} _SssTex(""SSS"",2D)=""white""{} _ChannelTex(""Channel"",2D)=""white""{} }
SubShader { Pass { Cull Off ZWrite Off ZTest Always Blend Off
HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D _MainTex,_MaskTex,_AdjustTex,_EmissionTex,_SssTex,_ChannelTex;
SamplerState sampler_MainTex,sampler_EmissionTex,sampler_ChannelTex;
int _Operation,_Channel,_MaskMode; float _TintAlpha,_MaskScale,_MaskValue,_EmissionEnabled,_EmissionBlend,_Premultiply;
float4 _MainST,_MaskST,_EmissionST,_EmissionMaskST,_Tone,_Sss,_SssStrength;
float3 Tone(float3 c) {
 c=pow(abs(c),_Tone.w);
 float4 p=(c.b>c.g)?float4(c.bg,-1.0,2.0/3.0):float4(c.gb,0.0,-1.0/3.0);
 float4 q=(p.x>c.r)?float4(p.xyw,c.r):float4(c.r,p.yzx);
 float d=q.x-min(q.w,q.y); float e=1.0e-10;
 float3 hsv=float3(abs(q.z+(q.w-q.y)/(6.0*d+e)),d/(q.x+e),q.x);
 hsv=float3(hsv.x+_Tone.x,saturate(hsv.y*_Tone.y),saturate(hsv.z*_Tone.z));
 return hsv.z-hsv.z*hsv.y+hsv.z*hsv.y*saturate(abs(frac(hsv.x+float3(1.0,2.0/3.0,1.0/3.0))*6.0-3.0)-1.0);
}
float4 frag(v2f_img input):SV_Target {
 float2 uv=input.uv;
 if(_Operation<=2) {
  float4 main=_MainTex.SampleLevel(sampler_MainTex,uv,0);
  float a=main.a*_TintAlpha;
  if(_MaskMode!=0) {
   float m=saturate(_MaskTex.SampleLevel(sampler_MainTex,uv*_MaskST.xy+_MaskST.zw,0).r*_MaskScale+_MaskValue);
   if(_MaskMode==1)a=m;else if(_MaskMode==2)a*=m;else if(_MaskMode==3)a=saturate(a+m);else a=saturate(a-m);
  }
  if(_Operation==0)return float4(saturate(a),0,0,1);
  float adjust=_AdjustTex.SampleLevel(sampler_MainTex,uv,0).r;
  float3 rgb=lerp(main.rgb,Tone(main.rgb),adjust);
  return float4(rgb,_Operation==1?saturate(a):main.a);
 }
 if(_Operation==3) {
  if(_EmissionEnabled==0)return float4(0,0,0,_Premultiply>.5?1:0);
  float4 value=_EmissionTex.SampleLevel(sampler_EmissionTex,uv*_EmissionST.xy+_EmissionST.zw,0)
   *_MaskTex.SampleLevel(sampler_MainTex,uv*_EmissionMaskST.xy+_EmissionMaskST.zw,0);
  value.a*=_EmissionBlend;
  return _Premultiply>.5?float4(value.rgb*value.a,1):value;
 }
 if(_Operation==4) {
  float thickness=saturate(_Sss.y);
  if(_Sss.x!=0) {
   float value=_SssTex.SampleLevel(sampler_MainTex,uv*_MainST.xy+_MainST.zw,0).r;
   if(_Sss.z!=0)value=1-value;
   thickness=max(thickness,saturate(pow(saturate(value),max(_Sss.w,.001))*_SssStrength.x*max(_SssStrength.y,0)));
  }
  return float4(thickness,0,0,1);
 }
 float4 value=_ChannelTex.SampleLevel(sampler_ChannelTex,uv,0);
 return float4(value[_Channel],0,0,1);
}
ENDHLSL
} } }";
    }
}
