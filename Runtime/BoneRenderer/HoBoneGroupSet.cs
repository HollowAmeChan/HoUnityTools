using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.BoneRendering
{
    /// <summary>
    /// 单个骨骼集合(对应 Blender 的 Bone Collection)。
    /// bones 仅包含该集合"直接持有"的骨骼名(不含子集合递归),与 Blender 界面一致。
    /// </summary>
    [System.Serializable]
    public sealed class HoBoneCollection
    {
        /// <summary>集合名称。</summary>
        public string name;

        /// <summary>父集合名称,顶层集合为空字符串。</summary>
        public string parent;

        /// <summary>该集合直接持有的骨骼名称列表。</summary>
        public List<string> bones = new List<string>();

        /// <summary>Unity 侧附加:该集合骨骼的显示颜色(Blender JSON 不含此字段)。</summary>
        public Color color = new Color(0.35f, 0.55f, 1.0f, 0.5f);

        /// <summary>
        /// Unity 侧附加:是否为自动生成的兜底组(收纳未归任何显式集合的骨骼)。
        /// 用标记而非名字识别,避免与 Blender 中同名集合冲突。
        /// </summary>
        public bool isOther;
    }

    /// <summary>
    /// 骨骼分组文件 — 从 Blender 的 HoTools 骨骼集合导出器(.json)导入而来。
    /// 记录骨架的所有骨骼集合、层级关系(parent)与各集合直接持有的骨骼名称。
    ///
    /// 可见性语义(完全分组绘制):
    /// - 集合可见 = 自身未被关闭 且 所有祖先集合均未被关闭;
    /// - 骨骼可见 = 它所属集合中至少有一个可见;
    /// - 不属于任何集合的骨骼一律不绘制(一旦启用分组即完全由分组控制)。
    /// </summary>
    public sealed class HoBoneGroupSet : ScriptableObject
    {
        [Tooltip("导出规范版本(来自 Blender JSON)。")]
        public string version = "1.0";

        [Tooltip("导出时间(来自 Blender JSON)。")]
        public string exportTime;

        [Tooltip("源骨架名称(来自 Blender JSON)。")]
        public string armatureName;

        [Tooltip("骨骼集合列表。")]
        public List<HoBoneCollection> collections = new List<HoBoneCollection>();

        /// <summary>兜底组的默认名字:收纳所有未被显式集合收录的骨骼(如 Blender 自动生成的 end 骨)。
        /// 若与 Blender 已有集合同名,导入时会自动加后缀去重;识别兜底组请用 <see cref="HoBoneCollection.isOther"/>。</summary>
        public const string OtherCollectionName = "Other";

        /// <summary>Other 兜底组的默认颜色(中性灰)。</summary>
        public static readonly Color OtherCollectionColor = new Color(0.6f, 0.6f, 0.65f, 0.5f);

        /// <summary>
        /// 判断某集合当前是否可见:自身未被关闭,且所有祖先集合均未被关闭。
        /// </summary>
        /// <param name="collectionName">集合名。</param>
        /// <param name="hiddenCollections">被关闭的集合名集合。</param>
        public bool IsCollectionVisible(string collectionName, ICollection<string> hiddenCollections)
        {
            if (string.IsNullOrEmpty(collectionName))
            {
                return true;
            }

            // 沿 parent 链向上逐级检查,任一级被关闭则不可见。
            // guard 防止数据里出现的父级环形引用导致死循环。
            string current = collectionName;
            int guard = 0;
            int max = collections.Count + 1;
            while (!string.IsNullOrEmpty(current) && guard <= max)
            {
                if (hiddenCollections != null && hiddenCollections.Contains(current))
                {
                    return false;
                }

                HoBoneCollection collection = FindCollection(current);
                if (collection == null)
                {
                    break;
                }

                current = collection.parent;
                guard++;
            }

            return true;
        }

        /// <summary>
        /// 判断某集合当前是否允许 Scene 选择:自身未关闭选择,且所有祖先集合均未关闭选择。
        /// </summary>
        /// <param name="collectionName">集合名。</param>
        /// <param name="unselectableCollections">被关闭选择的集合名集合。</param>
        public bool IsCollectionSelectable(string collectionName, ICollection<string> unselectableCollections)
        {
            if (string.IsNullOrEmpty(collectionName))
            {
                return true;
            }

            string current = collectionName;
            int guard = 0;
            int max = collections.Count + 1;
            while (!string.IsNullOrEmpty(current) && guard <= max)
            {
                if (unselectableCollections != null && unselectableCollections.Contains(current))
                {
                    return false;
                }

                HoBoneCollection collection = FindCollection(current);
                if (collection == null)
                {
                    break;
                }

                current = collection.parent;
                guard++;
            }

            return true;
        }

        /// <summary>
        /// 判断某骨骼是否应显示(完全分组绘制):
        /// 命中某个可见的显式集合即显示,取其颜色;
        /// 不属于任何显式集合的骨骼归入 Other 兜底组,按 Other 组的可见性与颜色处理。
        /// </summary>
        /// <param name="boneName">骨骼名。</param>
        /// <param name="hiddenCollections">被关闭的集合名集合。</param>
        /// <param name="visibleColor">输出:该骨骼采用的显示颜色(取首个命中的可见集合颜色)。</param>
        public bool IsBoneVisible(string boneName, ICollection<string> hiddenCollections, out Color visibleColor)
        {
            visibleColor = default;
            bool belongsToExplicit = false;

            for (int i = 0; i < collections.Count; i++)
            {
                HoBoneCollection collection = collections[i];
                if (collection == null || collection.bones == null || !collection.bones.Contains(boneName))
                {
                    continue;
                }

                // 跳过 Other 兜底组:它不显式持有骨骼,仅作为未归组骨骼的兜底。
                // 用 isOther 标记识别,避免与用户自建的同名集合冲突。
                if (collection.isOther)
                {
                    continue;
                }

                belongsToExplicit = true;
                if (IsCollectionVisible(collection.name, hiddenCollections))
                {
                    // 命中首个可见集合即可显示,取其颜色。
                    visibleColor = collection.color;
                    return true;
                }
            }

            // 属于显式集合但集合被关闭的骨骼保持隐藏,不掉入 Other。
            if (belongsToExplicit)
            {
                return false;
            }

            // 不属于任何显式集合的骨骼(如自动生成的 end 骨)归入 Other 组。
            HoBoneCollection other = FindOtherCollection();
            if (other != null && IsCollectionVisible(other.name, hiddenCollections))
            {
                visibleColor = other.color;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断某骨骼是否允许 Scene 选择。
        /// 命中至少一个可选的显式集合即允许选择;不属于任何显式集合时按 Other 兜底组处理。
        /// </summary>
        /// <param name="boneName">骨骼名。</param>
        /// <param name="unselectableCollections">被关闭选择的集合名集合。</param>
        public bool IsBoneSelectable(string boneName, ICollection<string> unselectableCollections)
        {
            bool belongsToExplicit = false;

            for (int i = 0; i < collections.Count; i++)
            {
                HoBoneCollection collection = collections[i];
                if (collection == null || collection.bones == null || !collection.bones.Contains(boneName))
                {
                    continue;
                }

                if (collection.isOther)
                {
                    continue;
                }

                belongsToExplicit = true;
                if (IsCollectionSelectable(collection.name, unselectableCollections))
                {
                    return true;
                }
            }

            if (belongsToExplicit)
            {
                return false;
            }

            HoBoneCollection other = FindOtherCollection();
            return other != null && IsCollectionSelectable(other.name, unselectableCollections);
        }

        /// <summary>某骨骼是否被任一显式集合(Other 兜底组除外)收录。用于编辑器统计 Other 组实际骨骼数。</summary>
        public bool BelongsToAnyExplicitCollection(string boneName)
        {
            for (int i = 0; i < collections.Count; i++)
            {
                HoBoneCollection collection = collections[i];
                if (collection == null || collection.isOther || collection.bones == null)
                {
                    continue;
                }

                if (collection.bones.Contains(boneName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>查找 Other 兜底组(按 isOther 标记),未找到返回 null。</summary>
        public HoBoneCollection FindOtherCollection()
        {
            for (int i = 0; i < collections.Count; i++)
            {
                if (collections[i] != null && collections[i].isOther)
                {
                    return collections[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 生成一个与当前显式集合都不冲突的兜底组名字。
        /// 默认 "Other";若已被占用,依次尝试 "Other (1)"、"Other (2)"…直到唯一。
        /// 名字仅用于开关状态(hiddenCollections)作 key 与界面显示,识别兜底组仍靠 isOther。
        /// </summary>
        private string MakeUniqueOtherName()
        {
            if (FindCollection(OtherCollectionName) == null)
            {
                return OtherCollectionName;
            }

            for (int suffix = 1; ; suffix++)
            {
                string candidate = OtherCollectionName + " (" + suffix + ")";
                if (FindCollection(candidate) == null)
                {
                    return candidate;
                }
            }
        }

        /// <summary>按名称查找集合,未找到返回 null。</summary>
        public HoBoneCollection FindCollection(string collectionName)
        {
            for (int i = 0; i < collections.Count; i++)
            {
                if (collections[i] != null && collections[i].name == collectionName)
                {
                    return collections[i];
                }
            }

            return null;
        }

        // 用于解析 Blender JSON 的临时结构(JsonUtility 需与 JSON 字段一一对应)。
        [System.Serializable]
        private sealed class BlenderCollectionDto
        {
            public string name;
            public string parent;
            public List<string> bones;
        }

        [System.Serializable]
        private sealed class BlenderExportDto
        {
            public string version;
            public string exportTime;
            public string armatureName;
            public List<BlenderCollectionDto> collections;
        }

        /// <summary>
        /// 解析 Blender HoTools 导出的骨骼集合 JSON,填充本资产的集合数据。
        /// 会保留已有同名集合的颜色,避免重复解析时颜色被重置;新集合按色相环自动取色。
        /// </summary>
        /// <param name="json">JSON 文本内容。</param>
        /// <param name="message">输出:结果说明(成功或失败原因)。</param>
        /// <returns>成功返回 true。</returns>
        public bool LoadFromJson(string json, out string message)
        {
            message = string.Empty;

            BlenderExportDto dto;
            try
            {
                dto = JsonUtility.FromJson<BlenderExportDto>(json);
            }
            catch (System.Exception e)
            {
                message = "解析 JSON 失败: " + e.Message;
                return false;
            }

            if (dto == null || dto.collections == null)
            {
                message = "JSON 内容为空或缺少 collections 字段。";
                return false;
            }

            // 保留已有集合颜色。Other 兜底组按标记单独记录,不按名字(名字可能因冲突检测而变)。
            var existingColors = new Dictionary<string, Color>();
            bool hasPrevOtherColor = false;
            Color prevOtherColor = OtherCollectionColor;
            if (collections != null)
            {
                foreach (HoBoneCollection existing in collections)
                {
                    if (existing == null)
                    {
                        continue;
                    }

                    if (existing.isOther)
                    {
                        hasPrevOtherColor = true;
                        prevOtherColor = existing.color;
                    }
                    else if (!string.IsNullOrEmpty(existing.name))
                    {
                        existingColors[existing.name] = existing.color;
                    }
                }
            }

            version = dto.version;
            exportTime = dto.exportTime;
            armatureName = dto.armatureName;
            collections = new List<HoBoneCollection>(dto.collections.Count);

            int totalBones = 0;
            for (int i = 0; i < dto.collections.Count; i++)
            {
                BlenderCollectionDto source = dto.collections[i];
                if (source == null)
                {
                    continue;
                }

                Color color = existingColors.TryGetValue(source.name, out Color kept)
                    ? kept
                    : AutoColor(i, dto.collections.Count);

                var bones = source.bones ?? new List<string>();
                totalBones += bones.Count;

                collections.Add(new HoBoneCollection
                {
                    name = source.name,
                    parent = source.parent ?? string.Empty,
                    bones = bones,
                    color = color
                });
            }

            // 追加 Other 兜底组:收纳所有未被显式集合收录的骨骼(如自动生成的 end 骨)。
            // 它不显式持有骨骼,可见性判定在 IsBoneVisible 里作为兜底分支处理。
            // 用 isOther 标记识别(而非名字),并生成与现有集合不冲突的名字供开关状态作 key。
            Color otherColor = hasPrevOtherColor ? prevOtherColor : OtherCollectionColor;
            collections.Add(new HoBoneCollection
            {
                name = MakeUniqueOtherName(),
                parent = string.Empty,
                bones = new List<string>(),
                color = otherColor,
                isOther = true
            });

            message = "已导入 " + (collections.Count - 1) + " 个骨骼集合(共 " + totalBones + " 条骨骼引用),另含 Other 兜底组。";
            return true;
        }

        /// <summary>
        /// 从 JSON 文本创建一个内存态分组模型(不落盘)。解析失败返回一个空模型,不会抛异常。
        /// 供 HoBoneRenderer 直接链接项目内 JSON 时现场解析使用。
        /// </summary>
        /// <param name="json">JSON 文本内容。</param>
        public static HoBoneGroupSet CreateFromJson(string json)
        {
            var instance = CreateInstance<HoBoneGroupSet>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            if (!string.IsNullOrEmpty(json))
            {
                instance.LoadFromJson(json, out _);
            }

            return instance;
        }

        /// <summary>按索引在色相环上均匀取色,保证各集合颜色区分度。</summary>
        public static Color AutoColor(int index, int total)
        {
            float hue = total > 0 ? (index / (float)total) : 0f;
            Color color = Color.HSVToRGB(Mathf.Repeat(hue + 0.08f, 1.0f), 0.6f, 1.0f);
            color.a = 0.5f;
            return color;
        }
    }
}
