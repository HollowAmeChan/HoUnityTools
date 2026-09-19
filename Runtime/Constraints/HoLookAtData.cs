using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoLookAtMode
    {
        [InspectorName("跟随物体")]
        Transform,

        [InspectorName("跟随鼠标")]
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
        /// <summary>
        /// 鼠标屏幕位置 → 角度偏移（把鼠标当摇杆）。跟相机无关，但**不是**"看向鼠标指的那个点"：
        /// 灵敏度是人为定的线性映射，跟相机 FOV 无关，角色不在画面中心时还有视差。
        /// </summary>
        [InspectorName("角度摇杆")]
        AngleMap,

        /// <summary>
        /// 准星：取鼠标射线上"角色所在深度"的那个点当目标点 —— 眼睛正好落在鼠标指的位置。
        /// 想要"看着鼠标"的精确感就用这个（默认）。
        /// </summary>
        [InspectorName("准星")]
        CursorPoint,

        /// <summary>相机 → 鼠标射线打到指定层（没打中时取射线上固定距离的点）。</summary>
        [InspectorName("射线命中")]
        Raycast
    }

    public enum HoLookAtLostBehavior
    {
        [InspectorName("停在最后方向")]
        Hold,

        [InspectorName("回中立")]
        Return,

        [InspectorName("立刻松开")]
        Disable
    }

    /// <summary>
    /// 眼睛用哪条路驱动。两套不混用（面板按模式切换）。
    /// </summary>
    public enum HoLookAtEyeDriver
    {
        /// <summary>
        /// 直接转 humanoid 的 LeftEye/RightEye 骨骼（世界空间叠加）。**默认**：
        /// 眼睛残余角是多少就转多少，能精确指向目标，不需要标定。
        /// </summary>
        [InspectorName("眼球骨骼")]
        EyeBones,

        /// <summary>
        /// 写凝视形态键（四条通道 → 键）。没有眼球骨骼的模型用这条，
        /// 需要按模型标定四个角度上限。
        /// </summary>
        [InspectorName("形态键")]
        ShapeKeys
    }

    /// <summary>
    /// 鼠标角度映射的坐标系。
    /// `ScreenRelative`：鼠标右 = 看向**画面**右侧（相机在角色正面时，等于看向角色的左边 —— 观众视角的直觉）。
    /// `CharacterRelative`：鼠标右 = 看向**角色**右侧（把鼠标当成角色自己的注视摇杆，正面机位下会看着相反）。
    /// </summary>
    public enum HoLookAtMouseSpace
    {
        [InspectorName("屏幕相对")]
        ScreenRelative,

        [InspectorName("角色相对")]
        CharacterRelative
    }

    /// <summary>
    /// 眼睛形态键的通道。**只有四条**：
    /// 横向只留「看左 / 看右」一套 —— 往里/往外（In/Out）与看左/看右本来就是同一件事的两种键名约定：
    /// 往右看 = 左眼的 In 键 + 右眼的 Out 键，往左看 = 左眼的 Out 键 + 右眼的 In 键。
    /// 所以每条通道的左右眼各填一个键就能表达两族；两只眼填同一个键时（VRM/Meta 那种双眼共用的 LookLeft）
    /// 写入器只注册一次，不会把同一个键写两遍。
    /// </summary>
    public enum HoLookAtEyeChannel
    {
        [InspectorName("看左")]
        LookLeft,

        [InspectorName("看右")]
        LookRight,

        [InspectorName("看上")]
        Up,

        [InspectorName("看下")]
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

        /// <summary>
        /// 两个槽是不是同一个键（含同名同增益）。双眼共用键名的模型（VRM/Meta 的 LookLeft）用它来去重，
        /// 免得同一个键被写两遍。
        /// </summary>
        public static bool SameKeyName(HoLookAtEyeKey a, HoLookAtEyeKey b)
        {
            if (a == null || b == null || !a.HasKey || !b.HasKey)
            {
                return false;
            }

            return string.Equals(a.KeyName.Trim(), b.KeyName.Trim(), StringComparison.OrdinalIgnoreCase)
                   && Mathf.Abs(a.Gain - b.Gain) < 0.0001f;
        }

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
        private HoLookAtEyeChannel channel = HoLookAtEyeChannel.LookLeft;

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

        /// <summary>残余超过眼球范围、已经还给头部的那部分（度）。非零 = "眼睛转不过来，头多担了"。</summary>
        public float eyeOverflowYaw;
        public float eyeOverflowPitch;

        /// <summary>眼睛平滑后的角度（LateUpdate 里的状态）。</summary>
        public float smoothedEyeYaw;
        public float smoothedEyePitch;

        /// <summary>头部**估计**朝向（我们让 Unity 看哪 × 权重）：眼睛残余与调试都按它算。</summary>
        public float headEstimateYaw;
        public float headEstimatePitch;

        /// <summary>本帧头部实际转过的量（IK 前→后的旋转增量），只作信息用。</summary>
        public float headDeltaYaw;
        public float headDeltaPitch;

        /// <summary>本帧是否把头部交给了 Unity 的 IK（量增量的前提）。</summary>
        public bool headCommanded;

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

        /// <summary>这一次采样实际用的相机（可能是 Scene 视图相机 —— 编辑器里鼠标在 Scene 视图上时）。</summary>
        public Camera camera;

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

        /// <summary>
        /// 头部**估计**朝向（我们让 Unity 看哪、它就朝哪，再按权重打折）—— 眼睛残余与调试都按它算。
        /// 头骨骼的绝对朝向在骨骼坐标系里无法唯一确定（含一个模型常数），所以不能用"旋转增量"当绝对朝向。
        /// </summary>
        public float headEstimateYaw;
        public float headEstimatePitch;

        /// <summary>本帧头部**实际转过的量**（IK 前→后的旋转增量）。只是信息：看出 IK 有没有偷懒。</summary>
        public float headDeltaYaw;
        public float headDeltaPitch;

        /// <summary>眼睛要补的残余角。</summary>
        public float eyeYaw;
        public float eyePitch;

        /// <summary>残余超过眼球范围、已经还给头部的那部分（度）。</summary>
        public float eyeOverflowYaw;
        public float eyeOverflowPitch;

        /// <summary>四条通道各自的量（0..1，未乘增益）。</summary>
        public float lookLeft;
        public float lookRight;
        public float up;
        public float down;

        public bool hasTarget;
        public bool targetValid;
        public Vector3 targetPoint;
        public bool hasTargetPoint;

        /// <summary>实际目光方向（头实际 + 眼睛残余）。</summary>
        public float gazeYaw;
        public float gazePitch;

        /// <summary>目光落点：从支点沿目光方向、走到和目标同样的距离（给屏幕叠加画点用）。</summary>
        public Vector3 gazePoint;
        public bool hasGazePoint;
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

        /// <summary>准星模式用：角色的注视支点（取"鼠标射线上这个深度"的那个点）。</summary>
        public Vector3 pivot;

        /// <summary>射线没打中任何东西时，取射线上的这个距离（米）。</summary>
        public float distance;

        public LayerMask raycastMask;
        public bool holdOffscreen;

        /// <summary>
        /// 编辑器专用：鼠标在 Scene 视图上时，改用 Scene 视图的相机与坐标当"观众视角"。
        /// 这样就能在 Scene 视图里一边看 Gizmo 一边用鼠标调试（构建里这个开关不起作用）。
        /// </summary>
        public bool useSceneViewMouse;
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
