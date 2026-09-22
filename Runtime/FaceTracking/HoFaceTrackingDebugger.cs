using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Serializable]
    public sealed class HoFacePathRemap
    {
        public string sourcePath = "Body";
        public SkinnedMeshRenderer target;
    }

    /// <summary>Editor-only sessions subscribe to these callbacks. Exported players never open a socket.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Tracking Debugger")]
    public sealed class HoFaceTrackingDebugger : MonoBehaviour
    {
        public Animator targetAnimator;
        public RuntimeAnimatorController faceController;
        [Tooltip("面捕驱动哪些区域。凝视键默认也开 —— LookAt 不是一定存在的，"
            + "要不要把凝视让给 LookAt 由两边的开关各自决定。")]
        public HoFaceRegion outputRegions = HoFaceRegion.All;
        [Tooltip("仅启用面部形态键；手机头姿与眼骨旋转不会写入角色。")]
        public List<HoFaceChannel> channels = HoFaceTrackingChannels.CreateDefaults();
        public List<HoFacePathRemap> pathRemaps = new List<HoFacePathRemap>();
        [Min(0.1f)] public float staleSeconds = 1f;
        [Min(0.01f)] public float neutralFadeSeconds = 0.2f;
        [Tooltip("分组指数平滑的时长（秒）。0 = 不过滤、直接透传 —— 默认就是 0，避免和手机侧自带的处理叠出额外延迟。"
            + "眼球要跟得紧、眼睑要稳、嘴更黏，所以分三组而不是一个系数。")]
        public float smoothEyelids;
        public float smoothGaze;
        public float smoothMouth;
        [Tooltip("眉、脸颊、鼻子等剩下的键。")]
        public float smoothOther;
        [Tooltip("分组死区：低于它的（实时）输入按 0 处理，以上的部分重新铺满 0..1。用来压住静止时的抖动。0 = 关。\n"
            + "位置对齐参考实现的 OSCm/Sensitivity 分组，但具体曲线没有逐位复刻 —— 这一版先做死区这个最有用的整形。")]
        public float deadZoneEyelids;
        public float deadZoneGaze;
        public float deadZoneMouth;
        public float deadZoneOther;
        [Tooltip("双眼同步：0 = 左右独立（允许 wink），1 = 强制两侧同值。\n"
            + "有些模型的左右眨眼键**各自都能闭双眼**（美术为了不让两只眼睛闭合程度不一致），"
            + "左右一起触发就会过眨眼 —— 这时把它调到 1。\n"
            + "作用范围照参考实现：眼睑 + 眼球横向，眼球纵向不进去。")]
        [Range(0f, 1f)] public float eyeSync;
        [Tooltip("同步到哪个值：0 = 全用左眼，0.5 = 平均，1 = 全用右眼。")]
        [Range(0f, 1f)] public float eyeSyncMix = 0.5f;
        [Tooltip("进入播放后自动启动角色动画会话，不会自动连接手机。")]
        public bool startOnPlay;

        [Header("果冻眼")]
        [Tooltip("把眼睑信号过一遍一维弹簧，产出带物理的参数供果冻动画消费。\n"
            + "参数负责「每次都不一样」，动画负责「每次都好看」—— 单用任一边都缺一半。\n"
            + "物理由中间层自己实现（Warudo 摆锤进不了蓝图，VRC 侧没有曲线驱动参数）。")]
        public bool jellyEnabled;
        [Min(0.1f)] public float jellyFrequencyX = 6f;
        [Tooltip("竖向分量的频率。**和横向取不同的值**，两个分量才会走成 Lissajous 曲线 —— "
            + "这是「灵动」的来源；取一样就退化成一维直线来回。")]
        [Min(0.1f)] public float jellyFrequencyY = 8.5f;
        [Range(0.02f, 1f)]
        [Tooltip("阻尼比，两个方向共用（果冻感应该是一致的）。越小越「果冻」；1 就完全不过冲了。")]
        public float jellyDamping = 0.25f;
        [Tooltip("横向（左右）分量产出的参数名。")]
        public string jellyParameterX = "Ho/JellyX";
        [Tooltip("竖向（上下）分量产出的参数名。")]
        public string jellyParameterY = "Ho/JellyY";

        /// <summary>按形态键名取分组平滑时长（秒）；0 = 直通。</summary>
        public float SmoothSeconds(string shape) => SmoothSeconds(HoFaceTrackingChannels.SmoothGroup(shape));

        /// <summary>某一组的死区。只作用在**实时输入**上；手动滑杆是调试用的，不该被它吃掉。</summary>
        public float DeadZone(HoFaceSmoothGroup group)
        {
            switch (group)
            {
                case HoFaceSmoothGroup.Eyelids: return deadZoneEyelids;
                case HoFaceSmoothGroup.Gaze: return deadZoneGaze;
                case HoFaceSmoothGroup.Mouth: return deadZoneMouth;
                default: return deadZoneOther;
            }
        }

        /// <summary>
        /// 响应整形（灵敏度）：死区以下归 0，以上重新铺满 0..1。
        /// 静止时面捕总有几十分之一的抖动，死区是压住它最直接的手段。
        /// </summary>
        public float ApplySensitivity(string shape, float value)
        {
            float dead = Mathf.Clamp(DeadZone(HoFaceTrackingChannels.SmoothGroup(shape)), 0f, 0.95f);
            if (dead <= 0f) return value;
            return value <= dead ? 0f : Mathf.Clamp01((value - dead) / (1f - dead));
        }

        /// <summary>某一组的分组平滑时长（秒）；0 = 直通。</summary>
        public float SmoothSeconds(HoFaceSmoothGroup group)        {
            switch (group)
            {
                case HoFaceSmoothGroup.Eyelids: return smoothEyelids;
                case HoFaceSmoothGroup.Gaze: return smoothGaze;
                case HoFaceSmoothGroup.Mouth: return smoothMouth;
                default: return smoothOther;
            }
        }

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
        }
    }
}
