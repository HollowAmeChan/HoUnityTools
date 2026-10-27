using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    /// <summary>Known Ray material settings read as data; this does not execute MME effects.</summary>
    public sealed class HoPmxRayDocument
    {
        public string EmdPath;
        public readonly Dictionary<int, HoPmxRayMaterial> Materials = new Dictionary<int, HoPmxRayMaterial>();
        public readonly List<string> Dependencies = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed class HoPmxRayMaterial
    {
        public string FxPath;
        public bool RecognizedTemplate;
        public readonly Dictionary<string, string> Macros = new Dictionary<string, string>(StringComparer.Ordinal);
        public string BaseTexturePath;
        public int AlbedoMapFrom;
        public float[] Albedo = { 1f, 1f, 1f };
        public bool AlbedoApplyDiffuse = true;

        public string NormalTexturePath;
        public int NormalMapFrom;
        public int NormalMapType;
        public float NormalScale = 1f;
        public int NormalUvFlip;
        public float NormalLoop = 1f;
        // Ray's derivative frame uses -B. This records source semantics, not a request to
        // invert a Unity texture: the latter also depends on the imported UV/tangent basis.
        public bool NormalUsesNegativeBitangent = true;

        public string MetallicTexturePath;
        public int MetallicMapFrom;
        public int MetallicChannel;
        public float Metallic;
        public int MetallicApplyScale;
        public int MetallicUvFlip;
        public float MetallicLoop = 1f;

        public string SmoothnessTexturePath;
        public int SmoothnessMapFrom;
        public int SmoothnessChannel;
        public float Smoothness;
        public int SmoothnessMapType;
        public int SmoothnessApplyScale;
        public int SmoothnessUvFlip;
        public float SmoothnessLoop = 1f;
        public bool SmoothnessFromMaterialPower;

        public string AlphaMode;
        public float Cutoff = 0.5f;
        public int AlphaMapFrom;
        public int AlphaChannel = 3;
        public string AlphaTexturePath;
        public float Alpha = 1f;

        public bool EmissionEnabled;
        public int EmissionMapFrom;
        public string EmissionTexturePath;
        public float[] EmissionColor = { 1f, 1f, 1f };
        public float EmissionIntensity = 1f;

        public float EvaluateMetallic(float sampledOrSourceValue)
        {
            return MetallicMapFrom == 0 ? Clamp01(Metallic) : Clamp01(ApplyScalar(sampledOrSourceValue, Metallic, MetallicApplyScale));
        }

        public float EvaluateSmoothness(float sampledOrSourceValue)
        {
            if (SmoothnessMapFrom == 0) return Clamp01(Smoothness);
            float value = ConvertSmoothness(sampledOrSourceValue, SmoothnessMapType);
            return Clamp01(ApplyScalar(value, Smoothness, SmoothnessApplyScale));
        }

        public static float ShininessToSmoothness(float shininess)
        {
            if (float.IsNaN(shininess) || float.IsInfinity(shininess) || shininess < 0f) return 0f;
            return Clamp01(1f - (float)Math.Pow(2.0 / (shininess + 2.0), 0.125));
        }

        public static float ConvertSmoothness(float value, int mapType)
        {
            if (mapType == 1) return 1f - (float)Math.Sqrt(Math.Max(0f, value));
            if (mapType == 2) return 1f - value;
            return value;
        }

        public static float ApplyScalar(float value, float scalar, int mode)
        {
            if (mode == 1) return value * scalar;
            if (mode == 2) return (float)Math.Pow(Math.Max(0f, value), scalar);
            return value;
        }

        private static float Clamp01(float value) { return float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value)); }
    }
}
