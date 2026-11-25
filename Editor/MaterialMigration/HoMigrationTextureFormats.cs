using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    /// <summary>Versioned native-asset storage policy. Selection compares every generated
    /// mip on the GPU; it does not compress source textures or change source importers.</summary>
    public static class HoMigrationTextureFormats
    {
        public enum OutputProfile { Production, ExactPreview }
        public const int PolicyVersion = 1;
        private const float UnormBudget = 1f / 510f + .00002f;
        // One encoded 8-bit step includes GPU sRGB decode precision. Alpha/scalar
        // keep the stricter half-step budget and separate semantic checks.
        private const float ColorCodeBudget = 1f / 255f + .00002f;
        private const float HalfRelativeBudget = 1f / 1024f;
        [ThreadStatic] private static OutputProfile? scopedProfile;

        public static OutputProfile Profile
        {
            get
            {
                if (scopedProfile.HasValue) return scopedProfile.Value;
                string value = Environment.GetEnvironmentVariable("HO_MIGRATION_TEXTURE_FORMAT");
                if (string.IsNullOrEmpty(value)) return OutputProfile.Production;
                if (Enum.TryParse(value, out OutputProfile result) && Enum.IsDefined(typeof(OutputProfile), result)) return result;
                throw new InvalidOperationException("HO_MIGRATION_TEXTURE_FORMAT must be Production or ExactPreview.");
            }
        }
        public static IDisposable UseProfile(OutputProfile profile) => new ProfileScope(profile);
        private sealed class ProfileScope : IDisposable
        {
            private readonly OutputProfile? previous;
            private bool disposed;
            internal ProfileScope(OutputProfile value) { previous = scopedProfile; scopedProfile = value; }
            public void Dispose() { if (disposed) return; scopedProfile = previous; disposed = true; }
        }

        [Serializable] public sealed class Measurement
        {
            public string profile, format, encoding, reason, worstEncodedSample;
            public int policyVersion = PolicyVersion, width, height, mipCount, cutoffMismatches, nonzeroMismatches;
            public long referencePayloadBytes, storedPayloadBytes;
            public float maxLinearRgbError, maxEncodedRgbError, rmsEncodedRgbError, maxRelativeRgbError, maxScalarOrAlphaError;
            public bool readable, blockCompressed;
            public float[] testedCutoffs;
            public string[] rejectedCandidates;
        }

        public static Texture2D Create(Color[] pixels, int width, int height, bool scalar, bool emission,
            bool coverage, float[] cutoffs, FilterMode filter, TextureWrapMode wrapU, TextureWrapMode wrapV,
            int anisotropy, out Measurement result)
        {
            if (width <= 0 || height <= 0 || pixels == null || (long)width * height != pixels.LongLength)
                throw new ArgumentException("Texture dimensions and pixel count do not agree.");
            if (pixels.Any(p => !Finite(p.r) || !Finite(p.g) || !Finite(p.b) || !Finite(p.a)))
                throw new ArgumentException("Production texture storage requires finite working-space pixels.");
            OutputProfile profile = Profile;
            cutoffs = (cutoffs ?? Array.Empty<float>()).Where(v => v >= 0 && v <= 1 && !float.IsNaN(v)).Distinct().OrderBy(v => v).ToArray();
            var exact = Make(pixels, width, height, scalar ? TextureFormat.RFloat : TextureFormat.RGBAFloat, false, filter, wrapU, wrapV, anisotropy);
            Texture2D selected = null;
            var rejected = new List<string>();
            try
            {
                if (profile == OutputProfile.ExactPreview)
                {
                    selected = exact; result = Description(exact, exact, "ExactPreview", false, cutoffs);
                    result.reason = "Unquantized reference output."; result.rejectedCandidates = Array.Empty<string>(); return selected;
                }
                bool unitRange = pixels.All(p => scalar ? p.r >= 0 && p.r <= 1 : p.r >= 0 && p.r <= 1 && p.g >= 0 && p.g <= 1 && p.b >= 0 && p.b <= 1 && p.a >= 0 && p.a <= 1);
                bool halfRange = pixels.All(p => scalar ? Mathf.Abs(p.r) <= 65504 : Mathf.Abs(p.r) <= 65504 && Mathf.Abs(p.g) <= 65504 && Mathf.Abs(p.b) <= 65504 && Mathf.Abs(p.a) <= 65504);
                var candidates = new List<(TextureFormat format, bool srgb)>();
                if (unitRange && !emission) candidates.Add((scalar ? TextureFormat.R8 : TextureFormat.RGBA32, !scalar && QualitySettings.activeColorSpace == ColorSpace.Linear));
                if (halfRange) candidates.Add((scalar ? TextureFormat.RHalf : TextureFormat.RGBAHalf, false));
                foreach (var candidate in candidates)
                {
                    if (!SystemInfo.SupportsTextureFormat(candidate.format)) { rejected.Add(candidate.format + ": unsupported by graphics device"); continue; }
                    var texture = Make(pixels, width, height, candidate.format, candidate.srgb, filter, wrapU, wrapV, anisotropy, exact);
                    Measurement measure;
                    try { measure = Measure(exact, texture, scalar, coverage, cutoffs); }
                    catch { Object.DestroyImmediate(texture); throw; }
                    bool numeric = candidate.format == TextureFormat.RGBA32
                        ? (candidate.srgb ? measure.maxEncodedRgbError <= ColorCodeBudget : measure.maxLinearRgbError <= UnormBudget)
                        : scalar ? measure.maxScalarOrAlphaError <= UnormBudget : measure.maxRelativeRgbError <= HalfRelativeBudget;
                    bool valid = numeric && measure.maxScalarOrAlphaError <= UnormBudget && measure.cutoffMismatches == 0 && measure.nonzeroMismatches == 0;
                    if (valid)
                    {
                        selected = texture; result = measure; result.reason = "Smallest accepted native format; all generated mip texels satisfy the numeric and coverage budgets.";
                        result.rejectedCandidates = rejected.ToArray(); return selected;
                    }
                    rejected.Add(candidate.format + ": maxLinearRGB=" + measure.maxLinearRgbError.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        + " maxEncodedRGB=" + measure.maxEncodedRgbError.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        + " maxScalarAlpha=" + measure.maxScalarOrAlphaError.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " worst=" + measure.worstEncodedSample
                        + " cutoff=" + measure.cutoffMismatches + " nonzero=" + measure.nonzeroMismatches);
                    Object.DestroyImmediate(texture);
                }
                selected = exact; result = Description(exact, exact, "Production", false, cutoffs);
                result.reason = "Retained float after range/precision/coverage checks; no clamping or forced quantization.";
                result.rejectedCandidates = rejected.ToArray(); return selected;
            }
            finally { if (selected != exact) Object.DestroyImmediate(exact); }
        }

        // Diagnostic only: production never automatically chooses block compression.
        public static Measurement EvaluateBc7(Color[] pixels, int width, int height, bool coverage, float[] cutoffs)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7)) throw new InvalidOperationException("BC7 is not supported by this graphics device.");
            var exact = Make(pixels, width, height, TextureFormat.RGBAFloat, false, FilterMode.Bilinear, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1);
            var encoded = Make(pixels, width, height, TextureFormat.RGBA32, QualitySettings.activeColorSpace == ColorSpace.Linear, FilterMode.Bilinear, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1, exact);
            try
            {
                EditorUtility.CompressTexture(encoded, TextureFormat.BC7, TextureCompressionQuality.Best);
                encoded.Apply(false, false);
                var result = Measure(exact, encoded, false, coverage, cutoffs ?? Array.Empty<float>());
                result.profile = "DiagnosticBC7"; result.reason = "Measured only; block compression is not a default production choice.";
                return result;
            }
            finally { Object.DestroyImmediate(exact); Object.DestroyImmediate(encoded); }
        }

        /// <summary>Called only after the new native asset has been serialized once.
        /// The second save retains its pixel payload while dropping the runtime CPU copy.</summary>
        public static void FinalizeAsset(Texture2D texture, Measurement report)
        {
            if (Profile == OutputProfile.Production)
            {
                texture.Apply(false, true); EditorUtility.SetDirty(texture); AssetDatabase.SaveAssetIfDirty(texture);
            }
            report.readable = texture.isReadable;
        }

        public static long PayloadBytes(int width, int height, int mips, TextureFormat format)
        {
            long bytes = 0;
            for (int mip = 0; mip < mips; mip++)
            {
                int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
                bytes += format == TextureFormat.BC7 ? (long)((w + 3) / 4) * ((h + 3) / 4) * 16
                    : (long)w * h * (format == TextureFormat.R8 ? 1 : format == TextureFormat.RHalf ? 2 : format == TextureFormat.RFloat || format == TextureFormat.RGBA32 ? 4 : format == TextureFormat.RGBAHalf ? 8 : 16);
            }
            return bytes;
        }
        private static Texture2D Make(Color[] pixels, int width, int height, TextureFormat format, bool srgb,
            FilterMode filter, TextureWrapMode wrapU, TextureWrapMode wrapV, int anisotropy, Texture2D referenceMips = null)
        {
            var texture = new Texture2D(width, height, format, true, !srgb)
            { filterMode = filter, wrapModeU = wrapU, wrapModeV = wrapV, anisoLevel = anisotropy };
            // Unity's automatic sRGB native mip generation need not equal the float
            // working-space reference. Quantize each reference mip independently.
            int levels = referenceMips == null ? 1 : texture.mipmapCount;
            for (int mip = 0; mip < levels; mip++)
            {
                Color[] valuesAtMip = referenceMips == null ? pixels : referenceMips.GetPixels(mip);
                if (format == TextureFormat.R8) texture.SetPixelData(valuesAtMip.Select(p => (byte)Mathf.RoundToInt(Mathf.Clamp01(p.r) * 255)).ToArray(), mip);
                else if (format == TextureFormat.RHalf) texture.SetPixelData(valuesAtMip.Select(p => Mathf.FloatToHalf(p.r)).ToArray(), mip);
                else if (format == TextureFormat.RFloat) texture.SetPixelData(valuesAtMip.Select(p => p.r).ToArray(), mip);
                else if (format == TextureFormat.RGBA32)
                {
                    var values = new Color32[valuesAtMip.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        Color p = valuesAtMip[i];
                        values[i] = new Color32(Byte(srgb ? EncodeSrgb(p.r) : p.r), Byte(srgb ? EncodeSrgb(p.g) : p.g),
                            Byte(srgb ? EncodeSrgb(p.b) : p.b), Byte(p.a));
                    }
                    texture.SetPixels32(values, mip);
                }
                else texture.SetPixels(valuesAtMip, mip);
            }
            texture.Apply(referenceMips == null, false); return texture;
        }
        private static byte Byte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float EncodeSrgb(float linear) => linear <= .0031308f ? 12.92f * linear : (float)(1.055 * Math.Pow(linear, 1.0 / 2.4) - .055);
        private static Measurement Description(Texture2D exact, Texture2D stored, string profile, bool srgb, float[] cutoffs) => new()
        {
            profile = profile, format = stored.format.ToString(), encoding = srgb ? "sRGB RGB / linear alpha" : "shader working-space data; no sRGB decode",
            width = stored.width, height = stored.height, mipCount = stored.mipmapCount, readable = stored.isReadable, testedCutoffs = cutoffs,
            referencePayloadBytes = PayloadBytes(exact.width, exact.height, exact.mipmapCount, exact.format), storedPayloadBytes = PayloadBytes(stored.width, stored.height, stored.mipmapCount, stored.format),
            blockCompressed = stored.format == TextureFormat.BC7
        };

        private static Measurement Measure(Texture2D exact, Texture2D stored, bool scalar, bool coverage, float[] cutoffs)
        {
            var result = Description(exact, stored, "Production", stored.isDataSRGB, cutoffs);
            Shader shader = null; Material material = null; bool async = ShaderUtil.allowAsyncCompilation;
            double squared = 0; long components = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; shader = ShaderUtil.CreateShaderAsset(ReadShader, false); shader.hideFlags = HideFlags.HideAndDontSave;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; ShaderUtil.CompilePass(material, 0, true);
                if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Storage comparison shader did not compile.");
                for (int mip = 0; mip < exact.mipmapCount; mip++)
                {
                    Color[] a = Read(exact, material, mip), b = Read(stored, material, mip);
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (!scalar)
                        {
                            for (int channel = 0; channel < 3; channel++)
                            {
                                float error = Mathf.Abs(a[i][channel] - b[i][channel]);
                                float encoded = Mathf.Abs(EncodeSrgb(Mathf.Max(a[i][channel], 0)) - EncodeSrgb(Mathf.Max(b[i][channel], 0)));
                                result.maxLinearRgbError = Mathf.Max(result.maxLinearRgbError, error);
                                if (encoded > result.maxEncodedRgbError)
                                {
                                    result.maxEncodedRgbError = encoded;
                                    result.worstEncodedSample = "mip=" + mip + " texel=" + i + " channel=" + channel
                                        + " reference=" + a[i][channel].ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                                        + " stored=" + b[i][channel].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                                }
                                result.maxRelativeRgbError = Mathf.Max(result.maxRelativeRgbError, error / Mathf.Max(Mathf.Abs(a[i][channel]), .001f));
                                squared += encoded * encoded; components++;
                            }
                        }
                        float reference = scalar ? a[i].r : a[i].a, candidate = scalar ? b[i].r : b[i].a;
                        if (float.IsNaN(candidate) || float.IsInfinity(candidate)) throw new InvalidOperationException("Storage format produced nonfinite values.");
                        result.maxScalarOrAlphaError = Mathf.Max(result.maxScalarOrAlphaError, Mathf.Abs(reference - candidate));
                        if ((reference > 0) != (candidate > 0)) result.nonzeroMismatches++;
                        if (coverage) foreach (float cutoff in cutoffs) if ((reference >= cutoff) != (candidate >= cutoff)) result.cutoffMismatches++;
                    }
                }
                result.rmsEncodedRgbError = components == 0 ? 0 : (float)Math.Sqrt(squared / components);
                return result;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (material != null) Object.DestroyImmediate(material); if (shader != null) Object.DestroyImmediate(shader);
            }
        }
        private static Color[] Read(Texture2D texture, Material material, int mip)
        {
            int width = Math.Max(1, texture.width >> mip), height = Math.Max(1, texture.height >> mip);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active; bool write = GL.sRGBWrite;
            try
            {
                material.SetFloat("_Mip", mip); GL.sRGBWrite = false; Graphics.Blit(texture, target, material);
                RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, width, height), 0, 0); read.Apply(false, false); return read.GetPixels();
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = write; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(read); }
        }
        private const string ReadShader = @"Shader ""Hidden/HoMigration/StorageComparison"" { Properties { _MainTex(""Source"",2D)=""white""{} }
SubShader { Pass { Cull Off ZWrite Off ZTest Always Blend Off HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D _MainTex; SamplerState sampler_point_clamp; float _Mip;
float4 frag(v2f_img input):SV_Target { return _MainTex.SampleLevel(sampler_point_clamp,input.uv,_Mip); }
ENDHLSL } } }";
    }
}
