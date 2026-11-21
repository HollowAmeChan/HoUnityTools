#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.ReleaseInfo
{
    /// <summary>Shows the release identity embedded in installed Ho packages.</summary>
    public sealed class HoReleaseVersionWindow : EditorWindow
    {
        [Serializable]
        private sealed class VersionInfo
        {
            public int schema = 0;
            public string repository = string.Empty;
            public string package_name = string.Empty;
            public string package_version = string.Empty;
            public string version = string.Empty;
            public string commit = string.Empty;
            public string content_sha256 = string.Empty;
            public bool worktree_dirty = false;
        }

        private sealed class InstalledVersion
        {
            public VersionInfo Info;
            public string Location;
            public string GitRevision;
        }

        private readonly List<InstalledVersion> versions = new List<InstalledVersion>();
        private Vector2 scroll;

        [MenuItem("HoUnityTools/Ho 渲版本信息", false, 1000)]
        private static void Open()
        {
            GetWindow<HoReleaseVersionWindow>("Ho 渲版本信息").Show();
        }

        private void OnEnable()
        {
            RefreshVersions();
        }

        private void RefreshVersions()
        {
            versions.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in PackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageInfo>())
            {
                if (string.IsNullOrEmpty(package.resolvedPath))
                    continue;
                string revision = package.git != null ? package.git.hash : string.Empty;
                AddVersion(Path.Combine(package.resolvedPath, "version_info.json"), revision, seen);
            }

            // Also supports the lilToon .unitypackage installer under Assets.
            foreach (string guid in AssetDatabase.FindAssets("version_info t:TextAsset", new[] { "Assets" }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileName(assetPath), "version_info.json", StringComparison.Ordinal))
                    AddVersion(assetPath, string.Empty, seen);
            }

            versions.Sort((a, b) => string.Compare(a.Info.repository + "/" + a.Info.package_name,
                b.Info.repository + "/" + b.Info.package_name, StringComparison.Ordinal));
            Repaint();
        }

        private void AddVersion(string path, string revision, HashSet<string> seen)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 65536)
                    return;
                string fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath))
                    return;
                var info = JsonUtility.FromJson<VersionInfo>(File.ReadAllText(fullPath));
                if (info == null || info.schema != 1 || string.IsNullOrEmpty(info.repository)
                    || !info.repository.StartsWith("HollowAmeChan/", StringComparison.Ordinal))
                    return;
                versions.Add(new InstalledVersion { Info = info, Location = fullPath, GitRevision = revision });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException)
            {
                Debug.LogWarning("无法读取 Ho 发布版本：" + path + "\n" + exception.Message);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
                    RefreshVersions();
                if (GUILayout.Button("复制版本清单", EditorStyles.toolbarButton))
                    EditorGUIUtility.systemCopyBuffer = GetVersionList();
            }

            EditorGUILayout.HelpBox("日期号是 Ho 发布版本；原始包版本用于 Unity 依赖。配布时保存并核对每个包的日期号与提交号。"
                + "dev 表示源码安装；Git 安装请同时核对 Git 修订值。", MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (versions.Count == 0)
                EditorGUILayout.LabelField("未找到带 version_info.json 的 Ho 包。");

            foreach (InstalledVersion installed in versions)
            {
                VersionInfo info = installed.Info;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(info.package_name ?? info.repository, EditorStyles.boldLabel);
                    Value("仓库", info.repository);
                    Value("日期发布号", info.version);
                    Value("原始包版本", info.package_version);
                    Value("提交号（工作区构建为基准）", info.commit);
                    if (!string.IsNullOrEmpty(installed.GitRevision))
                        Value("Git 安装修订值", installed.GitRevision);
                    if (!string.IsNullOrEmpty(info.content_sha256))
                        Value("内容 SHA-256", info.content_sha256);
                    if (info.worktree_dirty)
                        EditorGUILayout.LabelField("包含打包时的本地工作区修改");
                    if (GUILayout.Button("定位版本文件", GUILayout.Width(120)))
                        EditorUtility.RevealInFinder(installed.Location);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void Value(string label, string value)
        {
            EditorGUILayout.LabelField(label);
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "—" : value,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private string GetVersionList()
        {
            var text = new StringBuilder("Ho 渲安装版本清单\n");
            foreach (InstalledVersion installed in versions)
            {
                VersionInfo info = installed.Info;
                text.Append(info.repository).Append(" | ").Append(info.package_name)
                    .Append(" | Ho ").Append(info.version).Append(" | package ").Append(info.package_version)
                    .Append(" | commit ").Append(string.IsNullOrEmpty(info.commit) ? "dev" : info.commit);
                if (!string.IsNullOrEmpty(installed.GitRevision))
                    text.Append(" | git ").Append(installed.GitRevision);
                if (!string.IsNullOrEmpty(info.content_sha256))
                    text.Append(" | content ").Append(info.content_sha256);
                if (info.worktree_dirty)
                    text.Append(" | worktree modified");
                text.AppendLine();
            }
            return text.ToString();
        }
    }
}
#endif
