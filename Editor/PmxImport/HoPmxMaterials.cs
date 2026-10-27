using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    internal static class HoPmxMaterials
    {
        internal static Material Build(HoPmxMaterial source, HoPmxMaterialInfo info, HoPmxRayMaterial ray,
            HoPmxTextures textures, float scale, bool generateOutline, HoPmxImportReport report)
        {
            bool outline = generateOutline && (source.Flags & 16) != 0 && source.EdgeSize > 0;
            bool transparent = info.alphaMode == HoPmxAlphaMode.Transparent;
            bool cutout = info.alphaMode == HoPmxAlphaMode.Cutout;
            string shaderName = transparent ? (outline ? "Hidden/lilToonTransparentOutline" : "Hidden/lilToonTransparent") :
                cutout ? (outline ? "Hidden/lilToonCutoutOutline" : "Hidden/lilToonCutout") :
                outline ? "Hidden/lilToonOutline" : "lilToon";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("缺少 Ho lilToon shader：" + shaderName);
            var material = new Material(shader) { name = source.Name };
            material.SetOverrideTag("HO_MaterialSource", "PMX");
            material.SetOverrideTag("HO_PMX_MaterialIndex", info.index.ToString());
            Color color = HoPmxImporter.ColorOf(source.Diffuse);
            string basePath = info.baseTexturePath;
            if (ray != null)
            {
                if (!string.IsNullOrEmpty(ray.BaseTexturePath)) basePath = ray.BaseTexturePath;
                Color rayColor = new Color(ray.Albedo[0], ray.Albedo[1], ray.Albedo[2], 1);
                if (ray.AlbedoMapFrom == 0)
                {
                    basePath = null;
                    float alpha = color.a;
                    color = rayColor * (ray.AlbedoApplyDiffuse ? color : Color.white);
                    color.a = alpha;
                }
                else if (!ray.AlbedoApplyDiffuse) color = Color.white;
                // Alpha source constants are applied only when the shader explicitly selects them.
                if (ray.AlphaMapFrom == 0) color.a *= ray.Alpha;
            }
            ColorValue(material, "_Color", color);
            Texture2D baseTexture = textures.ColorTexture(basePath);
            TextureValue(material, "_MainTex", baseTexture);
            Float(material, "_Cull", (source.Flags & 1) != 0 ? 0 : 2);
            Float(material, "_FlipNormal", (source.Flags & 1) != 0 ? 1 : 0);
            Float(material, "_UseShadow", 1);
            ColorValue(material, "_ShadowColor", HoPmxImporter.ColorOf(source.Ambient));
            Float(material, "_ShadowReceive", (source.Flags & 8) != 0 ? 1 : 0);
            Float(material, "_ShadowBorder", 0.5f);
            Float(material, "_ShadowBlur", 0.1f);
            Float(material, "_ShadowStrength", 1);
            Float(material, "_UseOutline", outline ? 1 : 0);
            ColorValue(material, "_OutlineColor", HoPmxImporter.ColorOf(source.EdgeColor));
            Float(material, "_OutlineWidth", outline ? source.EdgeSize * 3 * scale : 0);
            Float(material, "_OutlineVertexR2Width", 2);
            Float(material, "_OutlineFixWidth", 0);
            Float(material, "_OutlineEnableLighting", 0);
            Float(material, "_Metallic", 0);
            Float(material, "_Reflectance", 0.04f);
            Float(material, "_Smoothness", HoPmxRayMaterial.ShininessToSmoothness(source.Shininess));
            Float(material, "_UseReflection", ray != null || source.Specular.X + source.Specular.Y + source.Specular.Z > 0 ? 1 : 0);
            Float(material, "_ApplySpecular", 1);
            Float(material, "_ApplySpecularFA", 1);
            Float(material, "_ApplyReflection", ray != null ? 1 : 0);
            Float(material, "_UseEmission", 0);
            if (ray != null) ApplyRay(material, source, info, ray, textures, report);
            Float(material, "_Cutoff", info.alphaCutoff);
            Float(material, "_ZWrite", transparent ? 0 : 1);
            Float(material, "_SrcBlend", (float)BlendMode.One);
            Float(material, "_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            Float(material, "_SrcBlendAlpha", (float)BlendMode.One);
            Float(material, "_DstBlendAlpha", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            Float(material, "_AlphaToMask", cutout ? 1 : 0);
            Float(material, "_OutlineZWrite", transparent ? 0 : 1);
            Float(material, "_OutlineSrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            Float(material, "_OutlineDstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            Float(material, "_OutlineAlphaToMask", cutout ? 1 : 0);
            material.renderQueue = transparent ? 3000 : cutout ? 2450 : 2000;
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            material.doubleSidedGI = (source.Flags & 1) != 0;
            return material;
        }

        private static void ApplyRay(Material target, HoPmxMaterial source, HoPmxMaterialInfo info,
            HoPmxRayMaterial ray, HoPmxTextures textures, HoPmxImportReport report)
        {
            string normalPath = SourcePath(ray.NormalMapFrom, ray.NormalTexturePath, info, "Normal", report);
            if (normalPath != null && ray.NormalMapType <= 1)
            {
                Texture2D normal = textures.DataTexture(normalPath, "normal", type: ray.NormalMapType);
                TextureValue(target, "_BumpMap", normal);
                Float(target, "_BumpScale", ray.NormalScale);
                Float(target, "_UseBumpMap", normal == null ? 0 : 1);
                Sampling(target, "_BumpMap", ray.NormalLoop, ray.NormalUvFlip);
            }
            string metallicPath = SourcePath(ray.MetallicMapFrom, ray.MetallicTexturePath, info, "Metalness", report);
            if (metallicPath != null)
            {
                Texture2D texture = textures.DataTexture(metallicPath, "metallic", ray.MetallicChannel,
                    scalar: ray.Metallic, applyScale: ray.MetallicApplyScale);
                TextureValue(target, "_MetallicGlossMap", texture);
                Float(target, "_Metallic", texture == null ? 0 : 1);
                Sampling(target, "_MetallicGlossMap", ray.MetallicLoop, ray.MetallicUvFlip);
            }
            else Float(target, "_Metallic", ray.EvaluateMetallic(0));

            float smoothness = HoPmxRayMaterial.ShininessToSmoothness(source.Shininess);
            if (ray.SmoothnessFromMaterialPower) smoothness = ray.EvaluateSmoothness(smoothness);
            else if (ray.SmoothnessMapFrom == 0) smoothness = ray.Smoothness;
            string smoothnessPath = SourcePath(ray.SmoothnessMapFrom, ray.SmoothnessTexturePath, info, "Smoothness", report);
            if (smoothnessPath != null)
            {
                Texture2D texture = textures.DataTexture(smoothnessPath, "smoothness", ray.SmoothnessChannel,
                    ray.SmoothnessMapType, scalar: ray.Smoothness, applyScale: ray.SmoothnessApplyScale);
                TextureValue(target, "_SmoothnessTex", texture);
                if (texture != null) smoothness = 1;
                Sampling(target, "_SmoothnessTex", ray.SmoothnessLoop, ray.SmoothnessUvFlip);
            }
            Float(target, "_Smoothness", Mathf.Clamp01(smoothness));
            if (ray.EmissionEnabled)
            {
                string emissionPath = SourcePath(ray.EmissionMapFrom, ray.EmissionTexturePath, info, "Emissive", report);
                TextureValue(target, "_EmissionMap", textures.ColorTexture(emissionPath));
                ColorValue(target, "_EmissionColor", new Color(ray.EmissionColor[0], ray.EmissionColor[1], ray.EmissionColor[2], 1) * ray.EmissionIntensity);
                Float(target, "_UseEmission", 1);
            }
            if (!string.IsNullOrEmpty(ray.AlphaTexturePath) || (ray.AlphaMapFrom != 0 && ray.AlphaMapFrom != 3))
                report.warnings.Add("独立 Ray Alpha 来源需手动检查：" + source.Name);
        }

        private static string SourcePath(int from, string explicitPath, HoPmxMaterialInfo info, string role, HoPmxImportReport report)
        {
            if (from == 1) return explicitPath;
            if (from == 3) return info.baseTexturePath;
            if (from != 0 && !(role == "Smoothness" && from == 9))
                report.warnings.Add("未自动映射 Ray " + role + " 来源 " + from + "：" + info.sourceName);
            return null;
        }
        private static void Sampling(Material material, string key, float loop, int flip)
        {
            if (!material.HasProperty(key)) return;
            // Ray's bit 0 flips U; bit 1 flips V.
            float x = (flip & 1) != 0 ? -loop : loop;
            float y = (flip & 2) != 0 ? -loop : loop;
            material.SetTextureScale(key, new Vector2(x, y));
            material.SetTextureOffset(key, new Vector2(x < 0 ? loop : 0, y < 0 ? 1 : 1 - loop));
        }
        private static void Float(Material m, string key, float value) { if (m.HasProperty(key)) m.SetFloat(key, value); }
        private static void ColorValue(Material m, string key, Color value) { if (m.HasProperty(key)) m.SetColor(key, value); }
        private static void TextureValue(Material m, string key, Texture value) { if (m.HasProperty(key)) m.SetTexture(key, value); }
    }
}
