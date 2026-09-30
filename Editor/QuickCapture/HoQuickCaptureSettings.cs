// HoQuickCaptureSettings.cs -- 快速渲染面板的配置：存什么、存哪、怎么清洗
//
// 存 **EditorPrefs**（每台机器一份，不进仓库），和播放速度面板同一套做法：
// 输出目录是"我这台机器上顺手往哪写"的东西，塞进仓库只会变成每次拉代码都打架的资产。
// 但**输出目录本身可以指向仓库里的相对路径**（相对工程根目录），所以真正跨机器要共享的是
// "把图放哪"这个位置，而不是这条配置 —— 位置在设置里手填，配置只记字符串。
//
// 序列化用 JsonUtility，所以整个 Data 是 [Serializable] 的公开字段 —— **别改字段名**。
// 读回来一定要过 Sanitize：EditorPrefs 里的字符串可能来自旧版本、也可能被手改过，
// 一个负数帧率或者 0 帧的录制请求不该让面板崩掉，也不该让它跑出一个空目录。
using System;
using System.IO;
using Hollow.HoUnityTools.Runtime.QuickCapture;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>要录多少：按帧数还是按秒数。</summary>
    public enum HoQuickCaptureTargetUnit
    {
        /// <summary>录够 N 帧就停。</summary>
        Frames,

        /// <summary>录够 N 秒（游戏时间，= 帧数 / 帧率）就停。</summary>
        Seconds,
    }

    /// <summary>出图分辨率从哪来。</summary>
    public enum HoQuickCaptureResolutionMode
    {
        /// <summary>跟游戏视图当前渲染分辨率一致（默认）。</summary>
        MatchGameView,

        /// <summary>自己填宽高；游戏视图会被临时改成这个尺寸再拍，拍完还原。</summary>
        Custom,
    }

    /// <summary>面板的全部可存状态。</summary>
    [Serializable]
    public sealed class HoQuickCaptureSettingsData
    {
        /// <summary>
        /// 输出目录。空 = 工程根目录下的 <c>HoQuickCapture</c>。
        /// 相对路径按**工程根目录**（<c>Application.dataPath</c> 的上一级）解析，绝对路径原样用。
        /// </summary>
        public string outputFolder = string.Empty;

        /// <summary>截图与录制的文件名前缀（会被洗掉路径非法字符）。</summary>
        public string filePrefix = "HoCapture";

        /// <summary>录制帧率。这个值同时决定 <c>Time.captureDeltaTime</c>，也决定"几秒"怎么换算成帧。</summary>
        public float frameRate = 30f;

        /// <summary>录制时长按帧数还是按秒数。</summary>
        public HoQuickCaptureTargetUnit targetUnit = HoQuickCaptureTargetUnit.Seconds;

        /// <summary>录多少帧（<see cref="targetUnit"/> = Frames 时用它）。</summary>
        public int targetFrames = 150;

        /// <summary>录多少秒（<see cref="targetUnit"/> = Seconds 时用它）。</summary>
        public float targetSeconds = 5f;

        /// <summary>出图分辨率从哪来。</summary>
        public HoQuickCaptureResolutionMode resolutionMode = HoQuickCaptureResolutionMode.MatchGameView;

        /// <summary>单张截图存成什么格式。</summary>
        public HoQuickCaptureImageFormat imageFormat = HoQuickCaptureImageFormat.Png;

        /// <summary>画面从哪来：游戏视图合成结果（默认）/ 指定相机渲进 RT（可透明背景）。</summary>
        public HoQuickCaptureRenderSource renderSource = HoQuickCaptureRenderSource.GameView;

        /// <summary>
        /// <see cref="HoQuickCaptureRenderSource.Camera"/> 时要渲的相机。
        ///
        /// ⚠️ 这个字段本身**不参与 JSON 序列化**（`[NonSerialized]`）：它是个**场景对象引用**，
        /// 直接塞进 EditorPrefs 只会存下一个在本场景里没有意义的本地 fileID。
        ///
        /// 但"不存"曾经被写成了"不持久化"，于是**每次域重载 / 关窗再开，这个槽就被清空**
        ///（用户报的正是这个：「指定相机这个槽老是被清空」）。正确的做法是
        /// **存一个跨会话稳定的身份**，而不是存引用本身 —— 见下面的
        /// <see cref="CameraPrefsKey"/> 与 <c>SaveCameraRef</c> / <c>LoadCameraRef</c>：
        /// 用 `GlobalObjectId`（GUID + 本地 fileID）把它存成字符串，载入时再解析回对象。
        /// </summary>
        [NonSerialized]
        public Camera sourceCamera;

        /// <summary>
        /// 渲相机时是否临时把背景改成透明（Clear Flags = Solid Color + 背景 alpha = 0），拍完还原。
        /// 只在 <see cref="renderSource"/> = Camera 时有意义。
        /// </summary>
        public bool transparentBackground;

        /// <summary>录制成什么格式。</summary>
        public HoQuickCaptureVideoFormat videoFormat = HoQuickCaptureVideoFormat.Mp4;

        /// <summary>
        /// MP4 的目标码率（Mbps）。0 = 按分辨率自动估一个。
        ///
        /// Unity 的编码器只有"码率 / 恒定质量档位"这一档粒度，**没有 CRF**
        ///（那是 x264 的概念，引擎里的编码器不暴露）。所以这里就是一个 Mbps 数字。
        /// </summary>
        public float videoBitrateMbps = 12f;

        /// <summary>H.264 档次：0 = Baseline，1 = Main，2 = High。</summary>
        public int h264Profile = 2;

        /// <summary>关键帧间隔（秒）。越小拖动定位越灵，文件越大。</summary>
        public float keyframeIntervalSeconds = 2f;

        /// <summary>自定义宽（<see cref="resolutionMode"/> = Custom 时用它）。</summary>
        public int customWidth = 1920;

        /// <summary>自定义高（<see cref="resolutionMode"/> = Custom 时用它）。</summary>
        public int customHeight = 1080;

        /// <summary>
        /// PNG 编码质量：0 = 最快（文件最大），100 = 最小（最慢）。
        /// 直接映射到 <c>ImageConversion.EncodeToPNG</c> 的 quality 参数。
        /// </summary>
        public int pngQuality = 50;

        /// <summary>每录完一段就在输出目录里新建一个带时间戳的子目录（不覆盖上一段）。</summary>
        public bool perTakeSubfolder = true;

        /// <summary>设成自定义分辨率时，是否把游戏视图也切成那个尺寸（拍完还原）。关掉的话出图会被拉伸。</summary>
        public bool lockGameViewResolution = true;

        /// <summary>录完把输出目录在文件管理器里打开。</summary>
        public bool revealWhenDone = true;

        /// <summary>按帧率把 <see cref="targetSeconds"/> 换算成帧数（至少 1 帧，且不超过上限）。</summary>
        public int ResolveTargetFrames()
        {
            if (targetUnit == HoQuickCaptureTargetUnit.Frames)
            {
                return Mathf.Clamp(targetFrames, 1, HoQuickCaptureSettings.MaxFrames);
            }

            // 这里必须**同时**夹上限：秒数本身只夹到 3600、帧率夹到 240，
            // 两个上限相乘是 86 万帧 —— 是 MaxFrames 的 8 倍多。
            // 只夹输入不夹乘积，等于上限形同虚设（"秒数"还是默认单位，所以这条路才是常态）。
            int frames = Mathf.RoundToInt(Mathf.Max(0.01f, targetSeconds) * Mathf.Max(1f, frameRate));
            return Mathf.Clamp(frames, 1, HoQuickCaptureSettings.MaxFrames);
        }

        /// <summary>这次录制按当前设置大概要多久（游戏时间，秒）。</summary>
        public float ResolveTargetDuration()
        {
            return ResolveTargetFrames() / Mathf.Max(1f, frameRate);
        }

        /// <summary>
        /// 出图分辨率（游戏视图模式下返回当前游戏视图的渲染分辨率）。
        /// </summary>
        /// <returns>
        /// 拿到了有效分辨率返回 true。返回 false 时长宽是 1x1 的占位值 ——
        /// **调用方必须判这个返回值**：游戏视图没开着的时候 `GetRenderSize` 就会失败，
        /// 不看的话会一路拍出 1x1 的 PNG 而没有任何报错。
        /// </returns>
        public bool ResolveOutputSize(out int width, out int height)
        {
            if (resolutionMode == HoQuickCaptureResolutionMode.Custom)
            {
                width = Mathf.Clamp(customWidth, 1, 16384);
                height = Mathf.Clamp(customHeight, 1, 16384);
                return true;
            }

            return HoQuickCaptureGameView.GetRenderSize(out width, out height);
        }
    }

    /// <summary>配置的载体：单例 + EditorPrefs 读写 + 变更通知。</summary>
    internal static class HoQuickCaptureSettings
    {
        /// <summary>EditorPrefs 键。带包名，免得和别的工具撞。</summary>
        public const string PrefsKey = "com.hollow.hounitytools.quickcapture";

        /// <summary>输出目录留空时用的目录名（落在工程根目录下）。</summary>
        public const string DefaultFolderName = "HoQuickCapture";

        /// <summary>帧率上下限。上到 240 已经远超"离线渲染"的实用范围；低到 1 是"一秒一帧"的极限。</summary>
        public const float MinFrameRate = 1f;
        public const float MaxFrameRate = 240f;

        /// <summary>单次录制的帧数上限。10 万帧 @30fps 是 55 分钟游戏时间，够用了；
        /// 再往上多半是把"秒"当"帧"填错了，与其等它跑到天亮，不如在这里挡住。</summary>
        public const int MaxFrames = 100000;

        public const int MaxSeconds = 3600;

        private static HoQuickCaptureSettingsData data;
        private static int revision;

        /// <summary>配置变了。面板据此重建按钮状态并重画。</summary>
        public static event Action Changed;

        public static HoQuickCaptureSettingsData Data
        {
            get
            {
                if (data == null)
                {
                    Load();
                }

                return data;
            }
        }

        /// <summary>每变一次加一。</summary>
        public static int Revision => revision;

        /// <summary>改完配置调它：通知 + 存盘。</summary>
        public static void NotifyChanged()
        {
            revision++;
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }

            Save();
        }

        public static void Save()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] 配置存不进 EditorPrefs：" + exception.Message);
            }

            SaveCameraRef();
        }

        public static void Load()
        {
            data = new HoQuickCaptureSettingsData();
            try
            {
                if (EditorPrefs.HasKey(PrefsKey))
                {
                    string json = EditorPrefs.GetString(PrefsKey);
                    if (!string.IsNullOrEmpty(json))
                    {
                        JsonUtility.FromJsonOverwrite(json, data);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] EditorPrefs 里的配置读不出来，用默认值：" + exception.Message);
                data = new HoQuickCaptureSettingsData();
            }

            Sanitize(data);
            LoadCameraRef();
        }

        // ══════════════════════════════════════════════════════════════
        // 指定相机的持久化（单独一个键，不进上面那份 JSON）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 存"指定相机"用的键。**必须和 <see cref="PrefsKey"/> 分开** ——
        /// 上面那份 JSON 是靠 `JsonUtility` 走的，`GlobalObjectId` 那种字符串塞进去
        /// 只会变成一个普通字符串字段，而我们这里要的是"能解析回对象"的身份串。
        /// </summary>
        private const string CameraPrefsKey = "com.hollow.hounitytools.quickcapture.camera";

        /// <summary>
        /// 把当前指定的相机存成 `GlobalObjectId` 字符串。
        ///
        /// 为什么要绕这一圈：`Camera` 是**场景对象引用**，直接序列化只会得到一个本地 fileID ——
        /// 换个场景、或者场景重新导入，那个数字就可能指向别的对象（Unity 的"假 null"）。
        /// 而 `GlobalObjectId` 是 **GUID（资产/场景）+ 本地 fileID** 的组合，跨会话、跨域重载都稳定，
        /// 这正是 Inspector 自己记住拖进去的场景对象的方式。
        ///
        /// 存不上（比如对象还没进场景、或版本没有这个 API）就**清掉这个键**，
        /// 免得留下一个指向旧对象的串，下次载入时解析出一个莫名其妙的东西。
        /// </summary>
        private static void SaveCameraRef()
        {
            try
            {
                if (data == null || data.sourceCamera == null)
                {
                    EditorPrefs.DeleteKey(CameraPrefsKey);
                    return;
                }

                GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(data.sourceCamera);
                if (id.identifierType == 0)
                {
                    // 0 = Null：拿不到稳定身份，宁可不存。
                    EditorPrefs.DeleteKey(CameraPrefsKey);
                    return;
                }

                EditorPrefs.SetString(CameraPrefsKey, id.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] 指定相机存不进 EditorPrefs：" + exception.Message);
            }
        }

        /// <summary>
        /// 载入"指定相机"。解析不回来就留空（**不报错**）——
        /// 场景没开、对象被删了，都是正常情况，报错只会打扰人。
        /// </summary>
        private static void LoadCameraRef()
        {
            if (data == null)
            {
                return;
            }

            data.sourceCamera = null;

            try
            {
                if (!EditorPrefs.HasKey(CameraPrefsKey))
                {
                    return;
                }

                string stored = EditorPrefs.GetString(CameraPrefsKey);
                if (string.IsNullOrEmpty(stored)
                    || !GlobalObjectId.TryParse(stored, out GlobalObjectId id))
                {
                    return;
                }

                UnityEngine.Object resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                data.sourceCamera = resolved as Camera;
            }
            catch (Exception)
            {
                // 解析失败就当没存过。用户重新拖一次即可，不值得打扰。
                data.sourceCamera = null;
            }
        }

        /// <summary>恢复全部默认。</summary>
        public static void ResetToDefault()
        {
            data = new HoQuickCaptureSettingsData();
            Sanitize(data);
            NotifyChanged();
        }

        /// <summary>工程根目录（<c>Assets</c> 的上一级）。</summary>
        public static string ProjectRoot
        {
            get
            {
                try
                {
                    string assets = Application.dataPath;
                    DirectoryInfo parent = Directory.GetParent(assets);
                    return parent != null ? parent.FullName : assets;
                }
                catch (Exception)
                {
                    return Application.dataPath;
                }
            }
        }

        /// <summary>
        /// 把配置里的 <see cref="HoQuickCaptureSettingsData.outputFolder"/> 解析成绝对路径并建好目录。
        /// 空串 / 只有空白 = 工程根目录下的 <see cref="DefaultFolderName"/>；相对路径按工程根解析。
        /// </summary>
        /// <returns>绝对路径；解析不出来时返回 null 并给出 <paramref name="error"/>。</returns>
        public static string ResolveOutputFolder(out string error)
        {
            error = null;
            string raw = Data.outputFolder;

            try
            {
                string path;
                if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0)
                {
                    path = Path.Combine(ProjectRoot, DefaultFolderName);
                }
                else
                {
                    // 用户可能直接粘的是从资源管理器复制的 "D:\a\b" 或者 "/a/b"：
                    // Path.IsPathRooted 在 Windows 上认得这两种，认不出的就当相对路径。
                    path = Path.IsPathRooted(raw.Trim())
                        ? raw.Trim()
                        : Path.Combine(ProjectRoot, raw.Trim());
                }

                path = NormalizeFolderPath(raw);
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception exception)
            {
                error = "输出目录用不了：" + exception.Message;
                return null;
            }
        }

        /// <summary>
        /// 目录路径统一成正斜杠、不留尾斜杠 —— 但**盘符根不能削**。
        ///
        /// 反例：`"D:\"` 去掉尾斜杠就变成 `"D:"`，而 `"D:"` 在 Windows 上是
        /// "D 盘的当前目录"这种相对写法，`Path.Combine("D:", "x.png")` 得到
        /// `"D:x.png"` —— 落在 D 盘当前目录里，不是根目录。所以根路径要保留那个斜杠。
        /// </summary>
        private static string NormalizeFolderPath(string raw)
        {
            string path = raw.Trim().Replace('\\', '/');
            while (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }

            // "D:" / "D:/" 这种只剩盘符的，补回根斜杠。
            if (path.Length == 2 && path[1] == ':')
            {
                path += "/";
            }

            return path;
        }

        /// <summary>界面上显示用的路径（不用真去建目录）。</summary>
        public static string PreviewOutputFolder()
        {
            string raw = Data.outputFolder;
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0)
            {
                return Path.Combine(ProjectRoot, DefaultFolderName);
            }

            return Path.IsPathRooted(raw.Trim()) ? raw.Trim() : Path.Combine(ProjectRoot, raw.Trim());
        }

        /// <summary>把界面上填进来的值收进合法范围。</summary>
        private static void Sanitize(HoQuickCaptureSettingsData target)
        {
            if (target.outputFolder == null)
            {
                target.outputFolder = string.Empty;
            }

            if (target.filePrefix == null)
            {
                target.filePrefix = "HoCapture";
            }

            // ⚠️ 先挡 NaN / 无穷，再夹范围。
            // `Mathf.Clamp` **会把 NaN 原样放过去**（NaN 跟谁比都是 false），
            // 而 `EditorGUI.FloatField` 是允许用户输入 `1/0` 这种表达式的。
            // 漏过去的后果不小：帧率变成 NaN → `Time.captureDeltaTime = NaN`
            // → 整个播放会话的游戏时间坏掉。
            target.frameRate = FiniteOr(target.frameRate, 30f);
            target.targetSeconds = FiniteOr(target.targetSeconds, 5f);

            target.frameRate = Mathf.Clamp(target.frameRate, MinFrameRate, MaxFrameRate);
            target.targetFrames = Mathf.Clamp(target.targetFrames, 1, MaxFrames);
            target.targetSeconds = Mathf.Clamp(target.targetSeconds, 0.01f, MaxSeconds);
            target.customWidth = Mathf.Clamp(target.customWidth, 1, 16384);
            target.customHeight = Mathf.Clamp(target.customHeight, 1, 16384);
            target.pngQuality = Mathf.Clamp(target.pngQuality, 0, 100);
        }

        /// <summary>不是有限数就换成兜底值（`Mathf.Clamp` 挡不住 NaN / ±∞）。</summary>
        private static float FiniteOr(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value;
        }
    }
}
