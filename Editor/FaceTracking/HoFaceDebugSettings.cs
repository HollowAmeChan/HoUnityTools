using System;
using System.Collections.Generic;
using System.IO;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **面板的全局调试设置。**
    ///
    /// 【为什么需要它、而且必须落盘】
    /// 目标态是"角色预制件上零组件"—— 调试状态不再有 MonoBehaviour 兜着，
    /// 而编译、进出播放都会清掉编辑器里的静态状态。所以这一坨必须自己存成文件，
    /// 否则每次重编译面板就空了。按约定放在工程 `Assets/` 下，跟着工程走（团队共享同一套调试设置）。
    ///
    /// 【为什么存的是"路径/GUID"而不是对象引用】
    /// 调试对象是**场景里的实例**，场景对象引用进不了 `Assets/` 下的资产；控制器是资产但也有被移动的可能。
    /// 所以：角色存**层级路径**（加载时按路径找回）、控制器存 **GUID + 路径兜底**。
    /// 找不回来时面板照常打开、只是把这些栏标红，让用户重填 —— 不静默退回"随便找一个"。
    ///
    /// 【和 Warudo 侧的关系】
    /// `profilePath` 指向的就是那份 `*.hoface.json` —— **Unity 侧和 Warudo 侧读的是同一个文件**，
    /// 这是"面板里看到什么、Warudo 里就是什么"的物理保证（另一重保证是两边共用同一份求值代码）。
    /// </summary>
    [Serializable]
    public sealed class HoFaceDebugSettings
    {
        /// <summary>配置文件格式标识（和 profile 一样，自己的文件自己认）。</summary>
        public const string Format = "ho-face-debug";

        public const int Version = 1;

        /// <summary>默认落点：工程的 `Assets/` 下，跟着工程走。</summary>
        public const string DefaultAssetPath = "Assets/HoFaceDebugSettings.json";

        // ── 对象栏：三样，全部按字符串引用存（理由见类注释）────────────────────

        /// <summary>调试对象：**场景里的角色实例**（预制件实例）。空 = 没选。层级路径。</summary>
        public string characterPath = "";

        /// <summary>
        /// 面捕混合树控制器（`.controller` 资产）—— **会话真正跑的那一份**。
        /// 由「控制器编辑」页**就地**装配（先在预置目录里复制一份到工程、拖进来）；你也可以自己改完指到这儿。
        /// 首选按 GUID 引用，抗移动。
        /// </summary>
        public string faceControllerGuid = "";

        /// <summary>控制器路径，GUID 失效时的兜底。</summary>
        public string faceControllerPath = "";

        /// <summary>
        /// **预置/模板控制器**：`Adopt` 那种"从模板复制一份新控制器"的用法需要一个源资产。
        /// 工具页已经不需要它了（复制与装配都在工程里手动做、原地改），保留给脚本与验收用例。
        /// </summary>
        public string treeTemplateGuid = "";

        /// <summary>模板路径兜底。</summary>
        public string treeTemplatePath = "";

        /// <summary>动画文件夹：装现成片段的目录，装配时按槽位名找同名 `.anim` 填进去。</summary>
        public string animationFolder = "";

        /// <summary>中间层配置（`*.hoface.json`）。**必须**；空的时候面板下面全部锁住不让改。</summary>
        public string profilePath = "";

        /// <summary>
        /// 52 个输入通道（模式 / 手动值 / 中性 / 输入曲线）。
        /// ⚠️ **过渡期字段**：按已定的方向，输入曲线归 profile 的输入行，通道层只剩排练用途，
        /// 面板上不再画它（第三栏只显示裸输入值）。等会话那边摘掉通道层之后这个字段也该删。
        /// </summary>
        public List<HoFaceChannel> channels = HoFaceTrackingChannels.CreateDefaults();

        // ── 时间常数 ────────────────────────────────────────────────────────────

        /// <summary>多久没收到包算断流（秒）。</summary>
        public float staleSeconds = 1f;

        /// <summary>断流后回中性的淡出时长（秒）。</summary>
        public float neutralFadeSeconds = 0.2f;

        /// <summary>进入播放后自动开会话（**不会**自动连手机）。</summary>
        public bool startOnPlay;

        /// <summary>配置文件填了没有 —— 这是"下面能不能改"的总闸。</summary>
        public bool HasProfile { get { return !string.IsNullOrEmpty(profilePath); } }

        // ── 解析：对象栏那三样 ──────────────────────────────────────────────────

        private GameObject cachedCharacter;
        private string cachedCharacterPath;

        /// <summary>调试对象。按层级路径在**当前场景**里找；找不到返回 null（面板会把那一栏标红）。</summary>
        public GameObject Character()
        {
            if (string.IsNullOrEmpty(characterPath)) return null;
            if (cachedCharacter != null && cachedCharacterPath == characterPath) return cachedCharacter;

            cachedCharacterPath = characterPath;
            cachedCharacter = FindByPath(characterPath);
            return cachedCharacter;
        }

        /// <summary>调试对象上的 Animator（角色的骨骼动画器）。</summary>
        public Animator TargetAnimator()
        {
            var character = Character();
            if (character == null) return null;
            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.GetComponentInChildren<Animator>();
            return animator;
        }

        /// <summary>要驱动的网格：调试对象下**所有** SkinnedMeshRenderer（含未激活的）。</summary>
        public List<SkinnedMeshRenderer> Meshes()
        {
            var character = Character();
            if (character == null) return new List<SkinnedMeshRenderer>();
            return new List<SkinnedMeshRenderer>(character.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        /// <summary>面捕混合树控制器（会话真正跑的那一份）。先按 GUID、再按路径。</summary>
        public RuntimeAnimatorController FaceController()
        {
            return LoadController(faceControllerGuid, faceControllerPath);
        }

        /// <summary>混合树模板（装配源）。</summary>
        public RuntimeAnimatorController TreeTemplate()
        {
            return LoadController(treeTemplateGuid, treeTemplatePath);
        }

        /// <summary>记下一个控制器资产（同时写 GUID 与路径，两条腿走路）。</summary>
        public void SetFaceController(RuntimeAnimatorController controller, string assetPath)
        {
            faceControllerPath = controller != null ? assetPath ?? "" : "";
            faceControllerGuid = controller != null && !string.IsNullOrEmpty(faceControllerPath)
                ? AssetDatabase.AssetPathToGUID(faceControllerPath) : "";
        }

        /// <summary>记下混合树模板。</summary>
        public void SetTreeTemplate(RuntimeAnimatorController controller, string assetPath)
        {
            treeTemplatePath = controller != null ? assetPath ?? "" : "";
            treeTemplateGuid = controller != null && !string.IsNullOrEmpty(treeTemplatePath)
                ? AssetDatabase.AssetPathToGUID(treeTemplatePath) : "";
        }

        private static RuntimeAnimatorController LoadController(string guid, string path)
        {
            if (!string.IsNullOrEmpty(guid))
            {
                string resolved = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(resolved))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(resolved);
                    if (asset != null) return asset;
                }
            }

            if (!string.IsNullOrEmpty(path))
            {
                var asset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (asset != null) return asset;
                // 常见情况：用户手填了绝对路径或带盘符的老路径，退回 GUID 缓存再试一次。
                string byGuid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(byGuid))
                {
                    string resolved = AssetDatabase.GUIDToAssetPath(byGuid);
                    if (!string.IsNullOrEmpty(resolved))
                        return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(resolved);
                }
            }

            return null;
        }

        /// <summary>记下一个场景对象（存层级路径）。</summary>
        public void SetCharacter(GameObject character)
        {
            characterPath = character != null ? PathOf(character.transform) : "";
            cachedCharacter = null;
            cachedCharacterPath = null;
        }

        /// <summary>给对象算一个稳定的层级路径（不含场景名，按 `A/B/C`）。</summary>
        public static string PathOf(Transform transform)
        {
            if (transform == null) return "";
            var parts = new List<string>();
            for (var t = transform; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// 按 `A/B/C` 在当前场景里找对象。找不到就返回 null —— **不猜、不退回"随便找一个"**：
        /// 拿错角色比拿不到更糟（会往别的角色脸上写东西）。
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string[] parts = path.Split('/');
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                Transform node = root.transform;
                bool ok = true;
                for (int i = 1; i < parts.Length; i++)
                {
                    node = node.Find(parts[i]);
                    if (node == null) { ok = false; break; }
                }
                if (ok) return node.gameObject;
            }
            return null;
        }

        // ── 中间层配置：读文件（和 Warudo 侧同一份）──────────────────────────────

        private HoFaceMiddleware loadedProfile;
        private string loadedPath;
        private long loadedStamp = -1;
        private string profileError;

        /// <summary>读进来的配置（null = 没填路径 / 读失败；那时调用方不许往下走）。</summary>
        public HoFaceMiddleware Middleware
        {
            get
            {
                if (!HasProfile)
                {
                    loadedProfile = null;
                    loadedPath = null;
                    profileError = null;
                    return null;
                }

                // 按"路径 + 文件时间戳"缓存：面板里改完文件存盘，时间戳一变就重新解析。
                long stamp = 0L;
                try { stamp = File.GetLastWriteTimeUtc(FullProfilePath()).Ticks; }
                catch (Exception) { stamp = -1L; }

                if (loadedProfile != null && loadedPath == profilePath && stamp == loadedStamp) return loadedProfile;

                loadedPath = profilePath;
                loadedStamp = stamp;
                loadedProfile = null;
                profileError = null;

                string text;
                try
                {
                    if (!File.Exists(FullProfilePath())) { profileError = "文件不存在：" + profilePath; return null; }
                    text = File.ReadAllText(FullProfilePath());
                }
                catch (Exception e)
                {
                    profileError = "读文件失败：" + e.Message;
                    return null;
                }

                HoFaceMiddleware parsed;
                string error;
                if (!HoFaceProfile.TryParse(text, out parsed, out error))
                {
                    profileError = error;
                    return null;
                }

                loadedProfile = parsed;
                profileError = error;   // 非致命提示（比如认不出的修饰符）也照样带出来
                return loadedProfile;
            }
        }

        /// <summary>配置读不进来的原因（面板直接显示）；读得进来时可能是非致命提示。</summary>
        public string ProfileError { get { var _ = Middleware; return profileError; } }

        /// <summary>配置文件在磁盘上被改过之后叫它一次（面板保存后调）。</summary>
        public void ReloadProfile()
        {
            loadedPath = null;
            loadedProfile = null;
            loadedStamp = -1;
        }

        /// <summary>
        /// 这份配置的"版本戳"：路径 + 写盘时间戳。会话用它判断"要不要重新编译映射"。
        /// 换成文件路径之后就靠它 —— 以前是 TextAsset 的 InstanceID + 文本长度。
        /// </summary>
        public string ProfileStamp
        {
            get
            {
                if (!HasProfile) return "-";
                long stamp = 0L;
                try { stamp = File.GetLastWriteTimeUtc(FullProfilePath()).Ticks; }
                catch (Exception) { stamp = -1L; }
                return profilePath + "@" + stamp.ToString();
            }
        }

        /// <summary>配置文件的项目相对路径 → 绝对路径。相对路径按工程根解析。</summary>
        public string FullProfilePath()
        {
            if (string.IsNullOrEmpty(profilePath)) return "";
            if (Path.IsPathRooted(profilePath)) return profilePath;
            return Path.Combine(ProjectRoot(), profilePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary>
        /// 这台角色要跑的**输出行**：指了配置文件就**只用它里面的**，否则用内置默认。
        /// 和输入行一样，不许悄悄拿默认来补（那会变成隐式处理）。
        /// </summary>
        public List<HoFaceOutput> Outputs()
        {
            var loaded = Middleware;
            return loaded != null && loaded.outputs.Count > 0 ? loaded.outputs : HoFaceMiddlewareDefaults.Outputs();
        }

        /// <summary>
        /// 这台角色要跑的**输入行**（线名 → 规范名 + 量纲 + 输入曲线）。指了配置文件就只用它里面的 ——
        /// 一条都没有就是"不做改名"，规范名必须与线名同名。
        /// </summary>
        public List<HoFaceOutput> Inputs()
        {
            var loaded = Middleware;
            return loaded != null ? loaded.inputs : HoFaceMiddlewareDefaults.Inputs();
        }

        // ── 落盘 ────────────────────────────────────────────────────────────────

        /// <summary>写成 JSON（2 空格缩进，可 diff）。</summary>
        public string ToJson()
        {
            var text = new System.Text.StringBuilder(512);
            text.Append("{\n");
            text.Append("  \"format\": ").Append(HoFaceProfileJson.Quote(Format)).Append(",\n");
            text.Append("  \"version\": ").Append(Version).Append(",\n");
            text.Append("  \"characterPath\": ").Append(HoFaceProfileJson.Quote(characterPath)).Append(",\n");
            text.Append("  \"faceControllerGuid\": ").Append(HoFaceProfileJson.Quote(faceControllerGuid)).Append(",\n");
            text.Append("  \"faceControllerPath\": ").Append(HoFaceProfileJson.Quote(faceControllerPath)).Append(",\n");
            text.Append("  \"treeTemplateGuid\": ").Append(HoFaceProfileJson.Quote(treeTemplateGuid)).Append(",\n");
            text.Append("  \"treeTemplatePath\": ").Append(HoFaceProfileJson.Quote(treeTemplatePath)).Append(",\n");
            text.Append("  \"animationFolder\": ").Append(HoFaceProfileJson.Quote(animationFolder)).Append(",\n");
            text.Append("  \"profilePath\": ").Append(HoFaceProfileJson.Quote(profilePath)).Append(",\n");
            text.Append("  \"staleSeconds\": ").Append(HoFaceProfileJson.Num(staleSeconds)).Append(",\n");
            text.Append("  \"neutralFadeSeconds\": ").Append(HoFaceProfileJson.Num(neutralFadeSeconds)).Append(",\n");
            text.Append("  \"startOnPlay\": ").Append(startOnPlay ? "true" : "false").Append('\n');
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>按 JSON 文本填自己（未知字段跳过；缺字段保留当前值）。失败时把原因写进 <paramref name="error"/>。</summary>
        public static bool TryParse(string json, string projectRelativeOrAbsolutePath, out HoFaceDebugSettings settings, out string error)
        {
            settings = null;
            error = null;
            if (string.IsNullOrEmpty(json)) { error = "设置文件是空的。"; return false; }

            var result = new HoFaceDebugSettings();
            if (!string.IsNullOrEmpty(projectRelativeOrAbsolutePath)) result.settingsPath = projectRelativeOrAbsolutePath;

            string format = null;
            try
            {
                var reader = new HoJsonReader(json);
                reader.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "format": format = r.ReadString(); break;
                        case "version": r.ReadNumber(); break;
                        case "characterPath": result.characterPath = r.ReadString(); break;
                        case "faceControllerGuid": result.faceControllerGuid = r.ReadString(); break;
                        case "faceControllerPath": result.faceControllerPath = r.ReadString(); break;
                        case "treeTemplateGuid": result.treeTemplateGuid = r.ReadString(); break;
                        case "treeTemplatePath": result.treeTemplatePath = r.ReadString(); break;
                        case "animationFolder": result.animationFolder = r.ReadString(); break;
                        case "profilePath": result.profilePath = r.ReadString(); break;
                        case "staleSeconds": result.staleSeconds = r.ReadFloat(); break;
                        case "neutralFadeSeconds": result.neutralFadeSeconds = r.ReadFloat(); break;
                        case "startOnPlay": result.startOnPlay = r.ReadBool(); break;
                        default: r.SkipValue(); break;
                    }
                });
            }
            catch (Exception e)
            {
                error = "设置文件解析失败：" + e.Message;
                return false;
            }

            if (!string.IsNullOrEmpty(format) && format != Format)
            {
                error = "这不是 Ho 的调试设置文件（format = " + format + "）。";
                return false;
            }

            settings = result;
            return true;
        }

        /// <summary>自己从哪个文件来的（空 = 还没落过盘）。</summary>
        [NonSerialized] public string settingsPath = "";

        /// <summary>读一份设置；文件不存在时给一份空的（不是错误：第一次打开就是这样）。</summary>
        public static HoFaceDebugSettings LoadOrCreate(string path)
        {
            string full = FullPathOf(path);
            if (!File.Exists(full))
            {
                var fresh = new HoFaceDebugSettings();
                fresh.settingsPath = path;
                return fresh;
            }

            HoFaceDebugSettings parsed;
            string error;
            if (TryParse(File.ReadAllText(full), path, out parsed, out error)) return parsed;

            Debug.LogWarning("[Ho 面捕] 调试设置读不进来，用空的： " + error);
            var fallback = new HoFaceDebugSettings();
            fallback.settingsPath = path;
            return fallback;
        }

        /// <summary>落盘。写完刷新资产库（它落在 Assets/ 下，Unity 会当成 TextAsset 导入）。</summary>
        public void Save()
        {
            string path = string.IsNullOrEmpty(settingsPath) ? DefaultAssetPath : settingsPath;
            string full = FullPathOf(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(full, ToJson());
            settingsPath = path;
            AssetDatabase.Refresh();
        }

        /// <summary>项目相对路径 → 绝对路径。</summary>
        public static string FullPathOf(string path)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultAssetPath;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(ProjectRoot(), path.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// 绝对路径尽量转成工程相对路径（`Assets/...`）—— 设置文件里存可移植路径，
        /// 换台机器/换个盘符打开工程也能找回来。不在工程内的路径原样返回。
        /// </summary>
        public static string MakeProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string root = ProjectRoot();
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;
            return path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
    }
}
