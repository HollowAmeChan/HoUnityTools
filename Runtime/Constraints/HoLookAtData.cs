using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoLookAtMode
    {
        Transform,
        Mouse
    }

    public enum HoLookAtInputSource
    {
        Auto,
        InputSystem,
        LegacyInput
    }

    public enum HoLookAtMouseSampleMode
    {
        /// <summary>鼠标屏幕位置 → 角度偏移。跟相机距离无关，最可控。</summary>
        AngleMap,

        /// <summary>相机 → 鼠标射线打到指定层；没打中时取射线上固定距离的点。</summary>
        Raycast
    }

    public enum HoLookAtLostBehavior
    {
        Hold,
        Return,
        Disable
    }

    /// <summary>
    /// 鼠标角度映射的坐标系。
    /// `ScreenRelative`：鼠标右 = 看向**画面**右侧（相机在角色正面时，等于看向角色的左边 —— 观众视角的直觉）。
    /// `CharacterRelative`：鼠标右 = 看向**角色**右侧（把鼠标当成角色自己的注视摇杆，正面机位下会看着相反）。
    /// </summary>
    public enum HoLookAtMouseSpace
    {
        ScreenRelative,
        CharacterRelative
    }

    /// <summary>
    /// 眼睛形态键的通道。横向有两种族，必须分开：
    /// `Inner/Outer` 是**相对眼球**的（UniVRM 的正统表达，ARKit/PICO 用），
    /// `LookLeft/LookRight` 是**相对头**的（VRM/Meta/SRanipal 用）。
    /// </summary>
    public enum HoLookAtEyeChannel
    {
        Inner,
        Outer,
        LookLeft,
        LookRight,
        Up,
        Down
    }

    /// <summary>
    /// 单只眼睛的一个键。刻意做得极简：只存键名和增益。
    /// 眼睛通道的映射规律固定（量 → 曲线 → 增益），不需要每条通道单独配 ramp/范围。
    /// </summary>
    [Serializable]
    public sealed class HoLookAtEyeKey
    {
        [SerializeField]
        private bool enabled = true;

        [SerializeField]
        private string keyName = string.Empty;

        [SerializeField]
        private float gain = 1.0f;

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public string KeyName
        {
            get => keyName ?? string.Empty;
            set => keyName = value ?? string.Empty;
        }

        /// <summary>增益：通道量（0..1）乘上它再写出去，1 = 曲线拉满时输出 100。</summary>
        public float Gain
        {
            get => gain;
            set => gain = value;
        }

        public bool HasKey => enabled && !string.IsNullOrWhiteSpace(keyName);

        public void Sanitize()
        {
            gain = Mathf.Max(0.0f, gain);
            keyName = keyName ?? string.Empty;
        }
    }

    /// <summary>
    /// 一条眼睛通道：左右眼各一个键。通道的"量"由注视解算给出，这里只负责映射。
    /// </summary>
    [Serializable]
    public sealed class HoLookAtEyeEntry
    {
        [SerializeField]
        private bool enabled = true;

        [SerializeField]
        private HoLookAtEyeChannel channel = HoLookAtEyeChannel.Inner;

        [SerializeField]
        private HoLookAtEyeKey leftEye = new HoLookAtEyeKey();

        [SerializeField]
        private HoLookAtEyeKey rightEye = new HoLookAtEyeKey();

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public HoLookAtEyeChannel Channel
        {
            get => channel;
            set => channel = value;
        }

        public HoLookAtEyeKey LeftEye => leftEye;

        public HoLookAtEyeKey RightEye => rightEye;

        public void Sanitize()
        {
            leftEye?.Sanitize();
            rightEye?.Sanitize();
        }
    }

    /// <summary>注视解算结果（都相对参考系，单位为度；正 yaw = 角色右侧，正 pitch = 上方）。</summary>
    public struct HoLookAtAngles
    {
        public float yaw;
        public float pitch;

        /// <summary>总角度（矢量长度，用于死区与"眼睛吃满"的判定）。</summary>
        public float Magnitude => Mathf.Sqrt(yaw * yaw + pitch * pitch);
    }

    /// <summary>注视约束的运行时状态。</summary>
    public struct HoLookAtState
    {
        /// <summary>平滑后的目标方向（角空间）。</summary>
        public float smoothedYaw;
        public float smoothedPitch;

        /// <summary>本帧分给眼睛的残余角（由头部 IK 那一段算出来，LateUpdate 用）。</summary>
        public float eyeYaw;
        public float eyePitch;

        /// <summary>眼睛平滑后的角度（LateUpdate 里的状态）。</summary>
        public float smoothedEyeYaw;
        public float smoothedEyePitch;

        /// <summary>本帧是否要把头部交给 Unity 的 IK（丢失且设为 Disable 时为 false）。</summary>
        public bool applyLookAt;

        public bool hasTarget;
        public float lostTime;
        public double lastIkTime;
    }

    /// <summary>鼠标/指针采样结果。</summary>
    public struct HoPointerSample
    {
        public bool valid;
        public Vector2 screenPosition;
        public bool hasWorldPoint;
        public Vector3 worldPoint;
    }

    /// <summary>
    /// 调试快照：Gizmo 和面板条形读数共用同一份数据，避免两处各算一遍。
    /// 角度单位为度，全部相对参考系（正 yaw = 右侧，正 pitch = 上）。
    /// </summary>
    public struct HoLookAtDebug
    {
        /// <summary>平滑后的目标角（头部会去追的总角）。</summary>
        public float targetYaw;
        public float targetPitch;

        /// <summary>头部限位内的角（限位裁剪后）。</summary>
        public float headYaw;
        public float headPitch;

        /// <summary>眼睛要补的残余角。</summary>
        public float eyeYaw;
        public float eyePitch;

        /// <summary>六条通道各自的量（0..1，未乘增益）。</summary>
        public float inner;
        public float outer;
        public float lookLeft;
        public float lookRight;
        public float up;
        public float down;

        public bool hasTarget;
        public bool targetValid;
        public Vector3 targetPoint;
        public bool hasTargetPoint;
    }

    /// <summary>注视约束的鼠标参数（采样器输入）。</summary>
    public struct HoMouseSettings
    {
        public HoLookAtInputSource inputSource;
        public Camera camera;
        public HoLookAtMouseSampleMode sampleMode;
        public HoLookAtMouseSpace angleSpace;
        public Vector2 sensitivity;
        public float deadZone;

        /// <summary>射线没打中任何东西时，取射线上的这个距离（米）。</summary>
        public float distance;

        public LayerMask raycastMask;
        public bool holdOffscreen;
    }

    /// <summary>注视约束的眼睛参数（求解器输入）。</summary>
    public struct HoEyeSettings
    {
        public float angleLimitInner;
        public float angleLimitOuter;
        public float angleLimitUp;
        public float angleLimitDown;

        public void Sanitize()
        {
            angleLimitInner = Mathf.Max(1.0f, angleLimitInner);
            angleLimitOuter = Mathf.Max(1.0f, angleLimitOuter);
            angleLimitUp = Mathf.Max(1.0f, angleLimitUp);
            angleLimitDown = Mathf.Max(1.0f, angleLimitDown);
        }
    }

    /// <summary>注视约束的头部参数（求解器输入）。俯仰限位左右对称，够用且好调。</summary>
    public struct HoHeadSettings
    {
        public float deadZone;
        public float headShare;
        public float yawLimit;
        public float pitchLimit;

        public void Sanitize()
        {
            deadZone = Mathf.Clamp(deadZone, 0.0f, 89.0f);
            headShare = Mathf.Clamp01(headShare);
            yawLimit = Mathf.Clamp(yawLimit, 0.0f, 179.0f);
            pitchLimit = Mathf.Clamp(pitchLimit, 0.0f, 179.0f);
        }
    }
}
