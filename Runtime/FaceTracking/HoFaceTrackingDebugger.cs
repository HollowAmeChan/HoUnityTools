using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>Editor-only sessions subscribe to these callbacks. Exported players never open a socket.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Tracking Debugger")]
    public sealed class HoFaceTrackingDebugger : MonoBehaviour
    {
        public Animator targetAnimator;
        public RuntimeAnimatorController faceController;
        [Tooltip("仅启用面部形态键；手机头姿与眼骨旋转不会写入角色。")]
        public List<HoFaceChannel> channels = HoFaceTrackingChannels.CreateDefaults();
        [Tooltip("混合树模板：一份完整的 .controller（树形、坐标、门控、参数都在里面）。\n"
            + "装配时整份复制成下面那个面部控制器，只把动画驱动的对象换成「驱动对象」。")]
        public RuntimeAnimatorController treeTemplate;
        [Tooltip("动画文件夹：装现成片段的地方（比如每个形态键一份 `<键名>.anim`）。\n"
            + "装配时按**槽位名**（模板里那个片段的名字）找同名 .anim 填进去；找不到的槽位保留模板自带的那份。\n"
            + "片段是**按形态键名**重绑到驱动对象上的，所以这份文件夹跟模型无关，可以复用。")]
        public string animationFolder;
        [Tooltip("面捕要驱动的网格 —— 初始化时把控制器里的形态键动画重绑到这些网格上。\n"
            + "某个键在这些网格里谁都没有时，那条曲线原样留着不动（作者的格子数据不丢），"
            + "但那些格子落不到任何网格上 —— 面板的结构摘要会列出来。")]
        public List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        [Tooltip("中间层配置（我们自己的 `.hoface.json`）：一列输出行 = 参数名 + 曲线(表达式) + 有序修饰符。\n"
            + "它跟着控制器模板走（vrc-common 控制器配 vrc-common 中间层），在这里**手选**；\n"
            + "面板上的「面捕配置」按钮可以直接打开窗口预览与编辑。留空 = 内置默认。")]
        public TextAsset profile;
        [Min(0.1f)] public float staleSeconds = 1f;
        [Min(0.01f)] public float neutralFadeSeconds = 0.2f;
        [Tooltip("双眼同步（强制同眨）：把左右眼合成一个值再写回去。\n"
            + "有些模型的左右眨眼键**各自都能闭双眼**（美术为了不让两只眼睛闭合程度不一致），"
            + "左右一起触发就会过眨眼 —— 这时打开它。\n"
            + "关 = 左右独立（允许 wink）。作用范围照参考实现：眼睑 + 眼球横向，眼球纵向不进去。")]
        public bool eyeSync;
        [Tooltip("同步到哪个值：0 = 全用左眼，0.5 = 平均，1 = 全用右眼。")]
        [Range(0f, 1f)] public float eyeSyncMix = 0.5f;
        [Tooltip("单键双眼：模型上的左右眨眼键**各自都能闭双眼**时打开它。\n"
            + "那种模型光合并两个值治不了过眨眼 —— 同一个形变还是被写了两遍；\n"
            + "打开这个只留左侧有值、右侧写 0，形变就只被应用一次。\n"
            + "代价：右侧键不再被驱动（要让基础动画拿回它，把那个通道的模式设成「释放」）。")]
        public bool eyeSyncSingleKey;
        [Tooltip("进入播放后自动启动角色动画会话，不会自动连接手机。")]
        public bool startOnPlay;

        /// <summary>这台角色要跑的那套输出行：指了配置文件就用它，否则用内置默认。</summary>
        public List<HoFaceOutput> Outputs()
        {
            var loaded = Middleware;
            return loaded != null && loaded.outputs.Count > 0 ? loaded.outputs : HoFaceMiddlewareDefaults.Outputs();
        }

        /// <summary>
        /// 这台角色要跑的**输入行**（线名 → 规范名 + 量纲）。指了配置文件就**只用它里面的** ——
        /// 一条都没有就是"不做改名"，规范名必须与线名同名；**不会偷偷拿内置默认来补**（那会变成隐式处理）。
        /// 没指配置文件时才用内置默认（它同时带两种内置协议的行）。
        /// </summary>
        public List<HoFaceOutput> Inputs()
        {
            var loaded = Middleware;
            return loaded != null ? loaded.inputs : HoFaceMiddlewareDefaults.Inputs();
        }

        /// <summary>
        /// 读进来的配置文件（<c>null</c> = 没指配置 / 读失败，那时用内置默认）。
        /// 解析按"资产实例 + 文本长度"缓存：窗口写完文件后会调 <see cref="ReloadProfile"/>。
        /// </summary>
        public HoFaceMiddleware Middleware
        {
            get
            {
                if (profile == null)
                {
                    loadedProfile = null;
                    loadedFrom = null;
                    profileError = null;
                    return null;
                }

                if (loadedFrom == profile && loadedLength == profile.text.Length && loadedProfile != null) return loadedProfile;
                loadedFrom = profile;
                loadedLength = profile.text.Length;
                loadedProfile = HoFaceProfile.TryParse(profile.text, out var parsed, out profileError) ? parsed : null;
                return loadedProfile;
            }
        }

        /// <summary>配置读不进来的原因（面板直接显示）；读了没问题就是 <c>null</c>。</summary>
        public string ProfileError => profile == null ? null : profileError;

        /// <summary>配置文件在磁盘上被改过之后叫它一次（窗口保存后调）。</summary>
        public void ReloadProfile()
        {
            loadedFrom = null;
            loadedProfile = null;
        }

        private HoFaceMiddleware loadedProfile;
        private TextAsset loadedFrom;
        private int loadedLength = -1;
        private string profileError;

#if UNITY_EDITOR
        public static event Action<HoFaceTrackingDebugger> EditorTick;
        public static event Action<HoFaceTrackingDebugger> EditorLateTick;
        public static event Action<HoFaceTrackingDebugger> EditorDisabled;
        private void Update() => EditorTick?.Invoke(this);
        /// <summary>动画求值之后才有影子结果可抄，所以写回放在 LateUpdate。</summary>
        private void LateUpdate() => EditorLateTick?.Invoke(this);
        private void OnDisable() => EditorDisabled?.Invoke(this);
#endif

        private void Reset()
        {
            targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null) targetAnimator = GetComponentInParent<Animator>();
            if (targetAnimator == null) targetAnimator = GetComponentInChildren<Animator>();
            if (targetAnimator != null && (meshes == null || meshes.Count == 0))
                meshes = new List<SkinnedMeshRenderer>(targetAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }
    }
}
