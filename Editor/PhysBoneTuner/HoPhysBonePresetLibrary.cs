#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PhysBoneTuner
{
    /// <summary>
    /// 内置预设库的读取器。预设是包内的只读 json，不扫工程、不可在面板里改。
    ///
    /// 只读带来一个好处：预设文件与代码同源，改了就是改包，
    /// 不会出现"某天被谁在面板里改乱了、行为对不上文档"。
    /// 用户自己的预设走「导入 json」文件对话框，见 <see cref="ImportFromDisk"/>。
    /// </summary>
    internal static class HoPhysBonePresetLibrary
    {
        /// <summary>内置预设库文件名（含扩展名，用于 AssetDatabase 查找）。</summary>
        private const string BuiltInFileName = "HoPhysBonePresets.json";

        private static List<HoPhysBonePreset> cachedPresets;
        private static double nextRetryTime = -1.0;
        private const double RetryIntervalSeconds = 5.0;

        /// <summary>内置预设列表（按文件里的顺序）。读不到返回空表。</summary>
        internal static IList<HoPhysBonePreset> BuiltIn
        {
            get
            {
                if (cachedPresets != null)
                    return cachedPresets;
                if (EditorApplication.timeSinceStartup < nextRetryTime)
                    return Array.Empty<HoPhysBonePreset>();

                List<HoPhysBonePreset> loaded = LoadBuiltIn();
                if (loaded.Count == 0)
                {
                    // 脚本重载 / 包刚装好之后要能自动恢复，所以失败要允许重试。
                    nextRetryTime = EditorApplication.timeSinceStartup + RetryIntervalSeconds;
                    return Array.Empty<HoPhysBonePreset>();
                }

                cachedPresets = loaded;
                return cachedPresets;
            }
        }

        /// <summary>强制重读（文件被改过之后用）。</summary>
        internal static void Invalidate()
        {
            cachedPresets = null;
            nextRetryTime = -1.0;
        }

        private static List<HoPhysBonePreset> LoadBuiltIn()
        {
            var result = new List<HoPhysBonePreset>();

            string path = FindBuiltInPath();
            if (string.IsNullOrEmpty(path))
                return result;

            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
                return result;

            HoPhysBonePresetLibraryFile library;
            try
            {
                library = JsonUtility.FromJson<HoPhysBonePresetLibraryFile>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("HoPhysBoneTuner: 内置预设库解析失败（" + path + "）：" + exception.Message);
                return result;
            }

            if (library == null || library.presets == null)
                return result;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < library.presets.Length; index++)
            {
                HoPhysBonePreset preset = library.presets[index];
                if (preset == null)
                    continue;
                preset.Clamp();
                if (!preset.IsUsable)
                {
                    Debug.LogWarning(
                        "HoPhysBoneTuner: 内置预设 #" + index + " 缺 id 或 name，已跳过（" + path + "）。");
                    continue;
                }
                if (!seenIds.Add(preset.id))
                {
                    Debug.LogWarning("HoPhysBoneTuner: 内置预设 id 重复「" + preset.id + "」，保留第一个。");
                    continue;
                }
                // 内置库里没有"曲线键是否出现"这一说：写进 json 的就是本意。
                preset.HasPullCurveField = true;
                preset.HasSpringCurveField = true;
                preset.HasStiffnessCurveField = true;
                preset.HasGravityCurveField = true;
                preset.HasRadiusCurveField = true;
                result.Add(preset);
            }
            return result;
        }

        /// <summary>
        /// 按文件名在工程里找内置预设库。用文件名而不是写死路径，
        /// 这样包内嵌、VPM 安装、被拷进 Assets 三种情况都能找到。
        /// </summary>
        private static string FindBuiltInPath()
        {
            string[] guids = AssetDatabase.FindAssets(
                Path.GetFileNameWithoutExtension(BuiltInFileName) + " t:TextAsset");
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileName(path), BuiltInFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            return null;
        }

        /// <summary>内置库所在路径，用于在面板上显示来源；找不到返回 null。</summary>
        internal static string BuiltInPath
        {
            get { return FindBuiltInPath(); }
        }

        // ---- 用户预设：显式导入 / 显式保存，不扫目录 ----

        /// <summary>
        /// 从磁盘读一个预设 json（文件对话框，默认开在上次用过的目录）。
        /// 失败时把原因写进 error。
        /// </summary>
        internal static HoPhysBonePreset ImportFromDisk(out string error)
        {
            error = null;
            string folder = EditorUserSettings.GetConfigValue(FolderConfigKey);
            string path = EditorUtility.OpenFilePanel("导入 PhysBone 预设 json", folder ?? string.Empty, "json");
            if (string.IsNullOrEmpty(path))
                return null;

            EditorUserSettings.SetConfigValue(FolderConfigKey, Path.GetDirectoryName(path) ?? string.Empty);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                error = "读不了这个文件：" + exception.Message;
                return null;
            }

            HoPhysBonePreset preset = HoPhysBonePreset.Parse(text);
            if (preset == null)
            {
                error = "不是有效的预设 json（缺 id / name，或 json 结构不对）。";
                return null;
            }
            return preset;
        }

        /// <summary>把预设写到磁盘（文件对话框）。成功返回写入路径，取消返回 null。</summary>
        internal static string ExportToDisk(HoPhysBonePreset preset, out string error)
        {
            error = null;
            if (preset == null)
            {
                error = "没有可保存的预设。";
                return null;
            }

            string folder = EditorUserSettings.GetConfigValue(FolderConfigKey);
            string suggested = string.IsNullOrEmpty(preset.id) ? "HoTools_PhysBone" : preset.id;
            string path = EditorUtility.SaveFilePanel(
                "保存 PhysBone 预设 json", folder ?? string.Empty, suggested, "json");
            if (string.IsNullOrEmpty(path))
                return null;

            EditorUserSettings.SetConfigValue(FolderConfigKey, Path.GetDirectoryName(path) ?? string.Empty);

            try
            {
                File.WriteAllText(path, preset.ToJson());
            }
            catch (Exception exception)
            {
                error = "写不了这个文件：" + exception.Message;
                return null;
            }
            return path;
        }

        private const string FolderConfigKey = "HoPhysBoneTuner preset folder";
    }
}
#endif
