using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    // No texture importer settings are changed during PMX import. Data-map conversions are
    // linear Texture2D subassets; color textures remain references to external Unity assets.
    internal sealed class HoPmxTextures : IDisposable
    {
        internal sealed class Pixels
        {
            public int Width, Height;
            public Color32[] Values;
            public bool HasTransparency;
        }

        private readonly AssetImportContext context;
        private readonly HoPmxImportReport report;
        private readonly Dictionary<string, Pixels> pixels = new Dictionary<string, Pixels>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> converted = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly string projectDirectory = Directory.GetParent(Application.dataPath).FullName;

        internal HoPmxTextures(AssetImportContext context, HoPmxImportReport report)
        { this.context = context; this.report = report; }

        internal static string AbsoluteAssetPath(string assetPath)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package != null)
                return Path.GetFullPath(Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/', '\\')));
            return Path.GetFullPath(assetPath);
        }

        internal string Resolve(string baseFile, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            try
            {
                string cleaned = reference.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.IsPathRooted(cleaned) ? cleaned : Path.Combine(Path.GetDirectoryName(baseFile), cleaned));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            { Warn("无法解析贴图路径：" + reference); return null; }
        }

        internal string AssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase))
                return "Assets/" + full.Substring(data.Length + 1);
            // Packages may be local packages outside the project directory.
            string project = projectDirectory.Replace('\\', '/').TrimEnd('/') + "/";
            if (full.StartsWith(project + "Packages/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(project.Length);
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            foreach (var item in package)
            {
                string resolved = item.resolvedPath.Replace('\\', '/').TrimEnd('/') + "/";
                if (full.StartsWith(resolved, StringComparison.OrdinalIgnoreCase))
                    return item.assetPath.TrimEnd('/') + "/" + full.Substring(resolved.Length);
            }
            return null;
        }

        internal void Depend(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return;
            string asset = AssetPath(absolutePath);
            if (!string.IsNullOrEmpty(asset))
            {
                context.DependsOnSourceAsset(asset);
            }
            if (!report.dependencies.Contains(absolutePath)) report.dependencies.Add(absolutePath);
        }

        internal Texture2D ColorTexture(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;
            Depend(absolutePath);
            string asset = AssetPath(absolutePath);
            if (asset == null)
            { Warn("贴图不在 Unity Assets/Packages 内，请连同相对目录复制：" + absolutePath); return null; }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            if (texture != null) context.DependsOnArtifact(asset);
            if (texture == null) Warn("贴图缺失或 Unity 不支持该图片：" + absolutePath);
            return texture;
        }

        internal Pixels ReadPixels(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;
            if (pixels.TryGetValue(absolutePath, out var cached)) return cached;
            Depend(absolutePath);
            Pixels result = null;
            Texture2D decoded = null;
            try
            {
                if (!File.Exists(absolutePath)) { Warn("图片文件不存在：" + absolutePath); return null; }
                byte[] bytes = File.ReadAllBytes(absolutePath);
                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (ImageConversion.LoadImage(decoded, bytes, false))
                    result = new Pixels { Width = decoded.width, Height = decoded.height, Values = decoded.GetPixels32() };
                else if (HoPmxSourceImage.Read(bytes, Path.GetExtension(absolutePath), out int width, out int height, out byte[] rgba))
                {
                    var values = new Color32[width * height];
                    for (int i = 0; i < values.Length; ++i)
                        values[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                    result = new Pixels { Width = width, Height = height, Values = values };
                }
                else
                {
                    // Unity's native importer also handles TGA/BMP. Read back a temporary view
                    // without enabling Read/Write or changing the user's texture import settings.
                    Texture2D imported = ColorTexture(absolutePath);
                    if (imported != null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        RenderTexture previous = RenderTexture.active;
                        RenderTexture rt = RenderTexture.GetTemporary(imported.width, imported.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        try
                        {
                            Graphics.Blit(imported, rt);
                            RenderTexture.active = rt;
                            UnityEngine.Object.DestroyImmediate(decoded);
                            decoded = new Texture2D(imported.width, imported.height, TextureFormat.RGBA32, false, true);
                            decoded.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            decoded.Apply(false, false);
                            var values = decoded.GetPixels32();
                            if (imported.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear)
                            {
                                for (int i = 0; i < values.Length; ++i)
                                {
                                    Color32 c = values[i];
                                    c.r = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.r / 255f) * 255);
                                    c.g = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.g / 255f) * 255);
                                    c.b = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(c.b / 255f) * 255);
                                    values[i] = c;
                                }
                            }
                            result = new Pixels { Width = decoded.width, Height = decoded.height, Values = values };
                            Warn("图片使用 Unity 导入结果读回，数据图建议转为 PNG/TGA/BMP：" + absolutePath);
                        }
                        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
                    }
                    else Warn("无法读取图片像素进行分类/通道转换：" + absolutePath);
                }
            }
            catch (Exception e) when (e is IOException || e is UnityException || e is ArgumentException)
            { Warn("读取图片失败：" + absolutePath + " (" + e.Message + ")"); }
            finally
            {
                if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
                if (result != null)
                    foreach (var value in result.Values)
                        if (value.a < 253) { result.HasTransparency = true; break; }
                pixels[absolutePath] = result;
            }
            return result;
        }

        internal Texture2D DataTexture(string path, string role, int channel = 0, int type = 0,
            bool flipGreen = false, float scalar = 1, int applyScale = 0)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + "|" + role + "|" + channel + "|" + type + "|" + flipGreen + "|" +
                scalar.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + applyScale;
            if (converted.TryGetValue(key, out var texture)) return texture;
            Pixels source = ReadPixels(path);
            if (source == null) return null;
            var values = new Color32[source.Values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                Color32 c = source.Values[i];
                if (role == "normal")
                {
                    // RGB and RG tangent normal maps both work with lilToon's RG/AG unpack.
                    // PMX V is flipped in the mesh, matching Ray's -B tangent basis.
                    if (flipGreen) c.g = (byte)(255 - c.g);
                    c.a = 255;
                    values[i] = c;
                }
                else
                {
                    float value = (channel == 1 ? c.g : channel == 2 ? c.b : channel == 3 ? c.a : c.r) / 255f;
                    if (role == "smoothness") value = HoPmxRayMaterial.ConvertSmoothness(value, type);
                    value = HoPmxRayMaterial.ApplyScalar(value, scalar, applyScale);
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255);
                    values[i] = new Color32(v, v, v, 255);
                }
            }
            texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, true, true);
            texture.name = Path.GetFileNameWithoutExtension(path) + "_Ho_" + role;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.SetPixels32(values);
            texture.Apply(true, false);
            string identity;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                identity = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "");
            context.AddObjectToAsset("dataTexture_" + identity, texture);
            converted.Add(key, texture);
            return texture;
        }

        internal HoPmxAlphaMode Classify(HoPmxDocument doc, HoPmxMaterial material, int indexStart,
            string texturePath, out string reason)
        {
            if (material.Diffuse.W < 0.999f) { reason = "材质 Alpha 小于 1"; return HoPmxAlphaMode.Transparent; }
            if (string.IsNullOrEmpty(texturePath)) { reason = "材质不含透明度"; return HoPmxAlphaMode.Opaque; }
            Pixels image = ReadPixels(texturePath);
            if (image == null) { reason = "图片不可读；保守保留透明，建议检查"; return HoPmxAlphaMode.Transparent; }
            if (!image.HasTransparency) { reason = "贴图完全不透明"; return HoPmxAlphaMode.Opaque; }
            int zero = 0, soft = 0, opaque = 0;
            // Rasterize only this material's UV footprint. The bounded classification grid
            // avoids assigning an entire atlas's transparent areas to every material.
            const int grid = 256;
            var visited = new HashSet<int>();
            for (int p = indexStart; p < indexStart + material.IndexCount; p += 3)
            {
                Vector2 a = Uv(doc.Vertices[doc.Indices[p]].Uv);
                Vector2 b = Uv(doc.Vertices[doc.Indices[p + 1]].Uv);
                Vector2 c = Uv(doc.Vertices[doc.Indices[p + 2]].Uv);
                Sample(a, image, ref zero, ref soft, ref opaque);
                Sample(b, image, ref zero, ref soft, ref opaque);
                Sample(c, image, ref zero, ref soft, ref opaque);
                Sample((a + b + c) / 3, image, ref zero, ref soft, ref opaque);
                Vector2 origin = new Vector2(Mathf.Floor(a.x), Mathf.Floor(a.y));
                a -= origin; b -= origin; c -= origin;
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * grid);
                int x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * grid);
                int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * grid);
                int y1 = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * grid);
                // Very large tiled triangles are sampled over their repeating footprint.
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > grid * grid * 4L)
                {
                    for (int sy = 0; sy < grid; ++sy)
                    for (int sx = 0; sx < grid; ++sx)
                        if (visited.Add(sy * grid + sx))
                            Sample(new Vector2((sx + 0.5f) / grid, (sy + 0.5f) / grid), image, ref zero, ref soft, ref opaque);
                    break;
                }
                for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / grid, (y + 0.5f) / grid);
                    if (!Inside(uv, a, b, c)) continue;
                    int key = ((y % grid + grid) % grid) * grid + ((x % grid + grid) % grid);
                    if (visited.Add(key)) Sample(uv, image, ref zero, ref soft, ref opaque);
                }
            }
            if (zero + soft == 0) { reason = "材质 UV 覆盖区域不透明"; return HoPmxAlphaMode.Opaque; }
            // Antialiased cutout borders contain a small number of intermediate values.
            // Glass/cloth with sustained intermediate alpha must remain blended.
            float softRatio = soft / (float)Math.Max(1, soft + opaque);
            if (soft > 0 && (zero == 0 || softRatio > 0.12f))
            { reason = "材质 UV 区域有连续半透明"; return HoPmxAlphaMode.Transparent; }
            reason = "材质 UV 区域有透明孔洞/边缘";
            return HoPmxAlphaMode.Cutout;
        }

        private static Vector2 Uv(HoPmxVector2 value) { return new Vector2(value.X, 1 - value.Y); }
        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float x = Cross(b - a, p - a), y = Cross(c - b, p - b), z = Cross(a - c, p - c);
            return (x >= -0.000001f && y >= -0.000001f && z >= -0.000001f) ||
                   (x <= 0.000001f && y <= 0.000001f && z <= 0.000001f);
        }
        private static void Sample(Vector2 uv, Pixels image, ref int zero, ref int soft, ref int opaque)
        {
            int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1) * image.Width), 0, image.Width - 1);
            int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1) * image.Height), 0, image.Height - 1);
            byte a = image.Values[y * image.Width + x].a;
            if (a <= 2) ++zero; else if (a < 253) ++soft; else ++opaque;
        }
        private void Warn(string text) { if (!report.warnings.Contains(text)) report.warnings.Add(text); }
        public void Dispose() { pixels.Clear(); converted.Clear(); }
    }
}
