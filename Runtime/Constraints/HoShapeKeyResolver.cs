using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 形态键名字解析。只做字符串容错，不做语义推断：
    /// 精确名 → 忽略大小写（含 NFKC 归一）→ 剥 Blender 重复后缀（`Blink.001`）。
    /// </summary>
    public static class HoShapeKeyResolver
    {
        private static readonly Regex BlenderSuffix = new Regex(@"\.\d{3,}$", RegexOptions.Compiled);

        /// <summary>归一化：NFKC → 去首尾空白 → 去开头 @/+ → 剥 Blender 后缀 → 小写。</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string result = name;
            try
            {
                result = result.Normalize(NormalizationForm.FormKC);
            }
            catch (System.ArgumentException)
            {
                // 非法 Unicode 序列时退回原串
            }

            result = result.Trim();
            result = result.TrimStart('@', '+').Trim();
            result = BlenderSuffix.Replace(result, string.Empty);
            return result.ToLowerInvariant();
        }

        /// <summary>给一个 mesh 建"归一化键名 → 索引"表；重名时保留索引最小的那个。</summary>
        public static Dictionary<string, int> BuildLookup(Mesh mesh)
        {
            Dictionary<string, int> lookup = new Dictionary<string, int>();
            if (mesh == null)
            {
                return lookup;
            }

            int count = mesh.blendShapeCount;
            for (int i = 0; i < count; i++)
            {
                string key = Normalize(mesh.GetBlendShapeName(i));
                if (!string.IsNullOrEmpty(key) && !lookup.ContainsKey(key))
                {
                    lookup.Add(key, i);
                }
            }

            return lookup;
        }

        /// <summary>在已建好的表里查一个键名。</summary>
        public static bool TryResolve(Dictionary<string, int> lookup, string keyName, out int index)
        {
            index = -1;
            if (lookup == null || string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            return lookup.TryGetValue(Normalize(keyName), out index);
        }

        /// <summary>在 mesh 上直接找（没有缓存时的兜底）。</summary>
        public static bool TryResolve(Mesh mesh, string keyName, out int index)
        {
            index = -1;
            if (mesh == null || string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            string normalized = Normalize(keyName);
            int count = mesh.blendShapeCount;
            for (int i = 0; i < count; i++)
            {
                if (Normalize(mesh.GetBlendShapeName(i)) == normalized)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }
    }
}
