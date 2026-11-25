using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    // No assembly dependency on a material package: require only the family actually generated.
    internal static class HoPmxSpecializedMaterials
    {
        private static readonly Dictionary<HoPmxMaterialFamily, Action<Material>> Synchronizers = new();
        private static readonly Regex Hint = new(@"\[ho:(Cloth|Hair|Skin|Eye|Environment)\]", RegexOptions.IgnoreCase);

        internal static void ResolveFamily(HoPmxMaterialInfo info, HoPmxMaterialSetting setting)
        {
            info.familyNeedsReview = false;
            if (setting != null && setting.targetFamily != HoPmxMaterialFamily.Auto)
            {
                info.targetFamily = setting.targetFamily;
                info.familyReason = "Inspector / migration plan override";
                return;
            }
            var choices = new HashSet<HoPmxMaterialFamily>();
            foreach (Match match in Hint.Matches((info.sourceName ?? "") + " " + (info.memo ?? "")))
                choices.Add((HoPmxMaterialFamily)Enum.Parse(typeof(HoPmxMaterialFamily), match.Groups[1].Value, true));
            if (choices.Count == 1)
            {
                foreach (var choice in choices) info.targetFamily = choice;
                info.familyReason = "authored [ho:Family] hint";
                return;
            }
            bool conflictingHints = choices.Count > 1;
            if (!conflictingHints)
            {
                string name = (info.sourceName ?? "").ToLowerInvariant();
                // These are source-role suggestions, never texture/model filename classifiers.
                if (Role(name, "hair") || Has(name, "髪", "头发", "頭髮", "前髪", "後髪")) choices.Add(HoPmxMaterialFamily.Hair);
                if (Role(name, "skin") || Has(name, "肌", "皮肤", "皮膚")) choices.Add(HoPmxMaterialFamily.Skin);
                if (Role(name, "iris") || Role(name, "eye") || Has(name, "瞳", "虹彩", "眼球", "白目")) choices.Add(HoPmxMaterialFamily.Eye);
                if (Role(name, "cloth") || Has(name, "服", "衣", "スカート", "シャツ")) choices.Add(HoPmxMaterialFamily.Cloth);
            }
            info.targetFamily = HoPmxMaterialFamily.Cloth;
            if (choices.Count == 1)
                foreach (var choice in choices) info.targetFamily = choice;
            info.familyNeedsReview = true;
            info.familyReason = choices.Count == 1 && !conflictingHints
                ? "source material role suggestion; review slot override"
                : "ambiguous source role; provisional Cloth, choose slot override";
        }

        private static bool Role(string name, string role) => Regex.IsMatch(name, @"(^|[^a-z])" + role + @"([^a-z]|$)");
        private static bool Has(string name, params string[] parts)
        {
            foreach (string part in parts) if (name.Contains(part)) return true;
            return false;
        }

        internal static void PrepareRemappedTextures(HoPmxMaterial source, HoPmxMaterialInfo info, HoPmxRayMaterial ray, HoPmxTextures textures)
        {
            // Extracted materials can still reference this deterministic importer subasset.
            if ((info.targetFamily == HoPmxMaterialFamily.Skin || info.targetFamily == HoPmxMaterialFamily.Eye) &&
                info.alphaMode != HoPmxAlphaMode.Opaque)
                Coverage(info, ray, textures);
            if (info.targetFamily == HoPmxMaterialFamily.Hair && info.alphaMode == HoPmxAlphaMode.Cutout)
            {
                var input = HoPmxMaterials.ReadBase(source, info, ray, textures, preserveDiffuseAlpha: true);
                if (!Mathf.Approximately(input.Color.a, 1))
                    textures.AlphaScaledColorTexture(BasePath(info, ray), input.Color.a);
            }
        }

        private static string BasePath(HoPmxMaterialInfo info, HoPmxRayMaterial ray) =>
            ray != null && ray.AlbedoMapFrom == 0 ? null :
            ray != null && !string.IsNullOrEmpty(ray.BaseTexturePath) ? ray.BaseTexturePath : info.baseTexturePath;

        private static Texture2D Coverage(HoPmxMaterialInfo info, HoPmxRayMaterial ray, HoPmxTextures textures)
        {
            string path = BasePath(info, ray);
            Texture2D coverage = textures.DataTexture(path, "coverage", channel: 3);
            if (path != null && coverage == null)
                throw new InvalidOperationException("无法为 Skin/Eye 转换 Alpha→Coverage：" + info.sourceName);
            return coverage;
        }

        internal static Material Build(AssetImportContext context, HoPmxMaterial source, HoPmxMaterialInfo info,
            HoPmxRayMaterial ray, HoPmxTextures textures, float scale, bool generateOutline, HoPmxImportReport report)
        {
            if (!Enum.IsDefined(typeof(HoPmxMaterialFamily), info.targetFamily) || info.targetFamily == HoPmxMaterialFamily.Auto)
                throw new InvalidOperationException("无效的专职材质家族：" + info.sourceName);
            if (info.familyNeedsReview) report.warnings.Add("材质槽 " + info.index + "「" + info.sourceName + "」：" + info.familyReason);
            if (info.targetFamily == HoPmxMaterialFamily.Environment)
                return HoPmxMaterials.Build(source, info, ray, textures, scale, generateOutline,
                    HoPmxMaterials.RequireEnvironmentShader(context), report);
            bool skin = info.targetFamily == HoPmxMaterialFamily.Skin;
            bool eye = info.targetFamily == HoPmxMaterialFamily.Eye;
            bool hair = info.targetFamily == HoPmxMaterialFamily.Hair;
            bool transparent = info.alphaMode == HoPmxAlphaMode.Transparent;
            if (skin && transparent)
                throw new InvalidOperationException("Skin URP 不支持透明混合：槽 " + info.index + "「" + info.sourceName +
                    "」。请明确改为 Cloth 家族，或将源表面设为不透明/裁切。");
            Shader shader = RequireShader(context, info.targetFamily);
            var target = new Material(shader) { name = source.Name };
            target.SetOverrideTag("HO_MaterialSource", "PMX");
            target.SetOverrideTag("HO_PMX_MaterialIndex", info.index.ToString());
            target.SetOverrideTag("HO_PMX_MaterialFamily", info.targetFamily.ToString());
            var (color, baseTexture) = HoPmxMaterials.ReadBase(source, info, ray, textures, preserveDiffuseAlpha: true);
            if (hair)
            {
                // Hair's transparent branch uses _Opacity; solid/cutout passes clip the
                // texture alpha directly. BaseColor.a has no coverage meaning in either.
                target.SetFloat("_Opacity", transparent ? color.a : 1);
                if (info.alphaMode == HoPmxAlphaMode.Cutout && !Mathf.Approximately(color.a, 1))
                {
                    baseTexture = textures.AlphaScaledColorTexture(BasePath(info, ray), color.a);
                    report.warnings.Add("Hair 裁切的漫反射 Alpha 已乘入派生底图 A（8-bit）；实体、深度和 Ho Pass 共用该覆盖：" + info.sourceName);
                }
                color.a = 1;
            }
            target.SetColor("_BaseColor", color);
            target.SetTexture("_BaseMap", baseTexture);
            Set(target, "_Cull", (source.Flags & 1) != 0 ? 0 : 2);
            Set(target, "_FlipNormal", (source.Flags & 1) != 0 ? 1 : 0);
            Set(target, hair ? "_ReceiveShadowStrength" : "_ReceiveShadows", (source.Flags & 8) != 0 ? 1 : 0);
            Set(target, "_CastShadows", (source.Flags & 4) != 0 ? 1 : 0);
            Set(target, "_SurfaceMode", transparent ? 2 : info.alphaMode == HoPmxAlphaMode.Cutout ? 1 : 0);
            if (skin) Set(target, "_AlphaClip", info.alphaMode == HoPmxAlphaMode.Cutout ? 1 : 0);
            Set(target, "_Cutoff", info.alphaCutoff);
            // Eye/Skin reserve base alpha for AO; coverage is a separate linear R texture.
            if ((skin || eye) && info.alphaMode != HoPmxAlphaMode.Opaque)
            {
                target.SetTexture("_CoverageMap", Coverage(info, ray, textures));
            }
            bool outline = generateOutline && (source.Flags & 16) != 0 && source.EdgeSize > 0 && !eye;
            Set(target, "_OutlineEnabled", outline ? 1 : 0);
            if (!eye)
            {
                float width = outline ? Mathf.Clamp(source.EdgeSize * 3 * scale / .08f, 0, 8) : 0;
                Set(target, "_OutlineWidth", width);
                target.SetColor("_OutlineColor", HoPmxImporter.ColorOf(source.EdgeColor));
                if (outline) report.warnings.Add("描边宽度仅作像素种子，需按镜头校准：" + info.sourceName);
            }
            else if (generateOutline && (source.Flags & 16) != 0 && source.EdgeSize > 0)
                report.warnings.Add("Eye URP 无外壳描边；眼线请使用独立 Cloth 槽：" + info.sourceName);
            if (transparent && outline) report.warnings.Add("透明表面不绘制外壳描边，请使用裁切或独立轮廓：" + info.sourceName);
            if (hair && (source.Flags & 4) == 0)
            {
                target.SetShaderPassEnabled("ShadowCaster", false);
                report.warnings.Add("Hair URP 未提供不透明投影开关；PMX 不投影标志仅写入当前材质 Pass，编辑表面模式后请复核：" + info.sourceName);
            }
            Set(target, "_Smoothness", HoPmxRayMaterial.ShininessToSmoothness(source.Shininess));
            if (ray != null) ApplyRay(target, source, info, ray, textures, report);
            target.doubleSidedGI = (source.Flags & 1) != 0;
            Synchronizers[info.targetFamily](target);
            if (hair && (source.Flags & 4) == 0) target.SetShaderPassEnabled("ShadowCaster", false);
            return target;
        }

        private static Shader RequireShader(AssetImportContext context, HoPmxMaterialFamily family)
        {
            bool skin = family == HoPmxMaterialFamily.Skin || family == HoPmxMaterialFamily.Eye;
            string package = skin ? "com.hoshader.skin-urp-stylized" : family == HoPmxMaterialFamily.Hair ? "com.hoshader.hair-urp" : "com.hoshader.cloth-urp";
            string path = "Packages/" + package + "/Runtime/Shaders/" + family + ".shader";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || shader.name != "HoShader/" + family + " URP")
                throw new InvalidOperationException("生成 " + family + " 材质需要 " + package + "，安装后重新导入 PMX。");
            context.DependsOnArtifact(path);
            if (!Synchronizers.ContainsKey(family))
            {
                string ns = skin ? "HoShader.SkinStylizedURP.Editor" : "HoShader." + family + "URP.Editor";
                var type = Type.GetType(ns + "." + family + "ShaderGUI, " + ns, false);
                var method = type?.GetMethod(family == HoPmxMaterialFamily.Hair ? "SyncSurfaceMode" : "SyncMaterial",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
                if (method == null || method.ReturnType != typeof(void))
                    throw new InvalidOperationException(package + " 缺少公开材质同步 API，请更新包。");
                Synchronizers.Add(family, (Action<Material>)Delegate.CreateDelegate(typeof(Action<Material>), method));
            }
            return shader;
        }

        private static void ApplyRay(Material target, HoPmxMaterial source, HoPmxMaterialInfo info,
            HoPmxRayMaterial ray, HoPmxTextures textures, HoPmxImportReport report)
        {
            bool cloth = info.targetFamily == HoPmxMaterialFamily.Cloth;
            bool skin = info.targetFamily == HoPmxMaterialFamily.Skin;
            string normal = HoPmxMaterials.SourcePath(ray.NormalMapFrom, ray.NormalTexturePath, info, "Normal", report);
            if (normal != null && ray.NormalMapType <= 1)
            {
                target.SetTexture("_BumpMap", textures.DataTexture(normal, "normal", type: ray.NormalMapType));
                Set(target, "_BumpScale", ray.NormalScale);
                if (cloth || info.targetFamily == HoPmxMaterialFamily.Hair)
                    HoPmxMaterials.Sampling(target, "_BumpMap", ray.NormalLoop, ray.NormalUvFlip);
                else if (ray.NormalLoop != 1 || ray.NormalUvFlip != 0)
                    report.warnings.Add("Skin/Eye 法线没有独立 ST，未转换 Ray 法线平铺/翻转，请烘焙贴图：" + info.sourceName);
            }
            else if (normal != null) report.warnings.Add("未支持 Ray 法线编码 " + ray.NormalMapType + "：" + info.sourceName);
            string metal = HoPmxMaterials.SourcePath(ray.MetallicMapFrom, ray.MetallicTexturePath, info, "Metalness", report);
            if (cloth)
            {
                Texture2D map = textures.DataTexture(metal, "metallic", ray.MetallicChannel, scalar: ray.Metallic, applyScale: ray.MetallicApplyScale);
                target.SetTexture("_MetallicGlossMap", map);
                target.SetFloat("_Metallic", ray.EvaluateMetallic(0));
                target.SetFloat("_MetallicMapScale", 1);
                HoPmxMaterials.Sampling(target, "_MetallicGlossMap", ray.MetallicLoop, ray.MetallicUvFlip);
            }
            else if (metal != null || ray.EvaluateMetallic(0) > 0)
                report.warnings.Add("" + info.targetFamily + " 不接受通用金属度；请检查家族选择或手工重建高光：" + info.sourceName);
            string smooth = HoPmxMaterials.SourcePath(ray.SmoothnessMapFrom, ray.SmoothnessTexturePath, info, "Smoothness", report);
            float value = HoPmxRayMaterial.ShininessToSmoothness(source.Shininess);
            value = ray.SmoothnessFromMaterialPower ? ray.EvaluateSmoothness(value) : ray.SmoothnessMapFrom == 0 ? ray.Smoothness : value;
            if (cloth || skin)
            {
                string property = skin ? "_SmoothnessMap" : "_SmoothnessTex";
                target.SetTexture(property, textures.DataTexture(smooth, "smoothness", ray.SmoothnessChannel,
                    ray.SmoothnessMapType, scalar: ray.Smoothness, applyScale: ray.SmoothnessApplyScale));
                Set(target, "_SmoothnessMapScale", 1);
                HoPmxMaterials.Sampling(target, property, ray.SmoothnessLoop, ray.SmoothnessUvFlip);
            }
            else if (smooth != null)
                report.warnings.Add("" + info.targetFamily + " 无通用光滑度贴图入口，需手动重建家族高光：" + info.sourceName);
            Set(target, "_Smoothness", Mathf.Clamp01(value));
            if (ray.EmissionEnabled && target.HasProperty("_EmissionMap"))
            {
                string emission = HoPmxMaterials.SourcePath(ray.EmissionMapFrom, ray.EmissionTexturePath, info, "Emissive", report);
                target.SetTexture("_EmissionMap", textures.ColorTexture(emission));
                Color color = new Color(ray.EmissionColor[0], ray.EmissionColor[1], ray.EmissionColor[2], 1) * ray.EmissionIntensity;
                color.a = 1;
                target.SetColor("_EmissionColor", color);
                Set(target, "_UseEmission", 1);
            }
            else if (ray.EmissionEnabled) report.warnings.Add("Hair URP 无自发光入口，请改用 Cloth 或单独发光件：" + info.sourceName);
            if (!string.IsNullOrEmpty(ray.AlphaTexturePath) || (ray.AlphaMapFrom != 0 && ray.AlphaMapFrom != 3))
                report.warnings.Add("未转换独立 Ray Alpha 来源，请显式重建覆盖贴图：" + info.sourceName);
        }

        private static void Set(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
