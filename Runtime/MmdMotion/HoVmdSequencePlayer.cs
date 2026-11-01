using System.Collections.Generic;
using UnityEngine;
using Hollow.HoUnityTools.PlaySpeed;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>Scaled game time drives body, expressions, camera and audio. Supports deterministic offline sampling.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(29000)]
    [AddComponentMenu("HoUnityTools/MMD/VMD Sequence Player")]
    public sealed class HoVmdSequencePlayer : MonoBehaviour
    {
        [SerializeField, InspectorName("角色（主 Humanoid）")] private Animator targetAnimator;
        [SerializeField, InspectorName("参考 PMX（尺寸与静止姿态）")] private Object referenceRigAsset;
        [SerializeField, InspectorName("动作 VMD")] private HoVmdAnimation motionClip;
        [SerializeField, InspectorName("表情 VMD")] private HoVmdAnimation expressionClip;
        [SerializeField, InspectorName("镜头 VMD")] private HoVmdAnimation cameraClip;
        [SerializeField, InspectorName("BGM")] private AudioClip soundtrack;
        [SerializeField, InspectorName("BGM 时间偏移（秒）")]
        [Tooltip("正值延后音乐，负值提前音乐。动作、表情和运镜的时间不变。")]
        private float soundtrackTimeOffset;
        [SerializeField, InspectorName("动作播放器")] private HoVmdMotionPlayer body;
        [SerializeField, InspectorName("表情播放器")] private HoVmdMotionPlayer face;
        [SerializeField, InspectorName("角色适配")] private HoVmdHumanoidRetargeter retargeter;
        [SerializeField, InspectorName("镜头播放器")] private HoVmdCameraPlayer cameraPlayer;
        [SerializeField, InspectorName("音乐")] private AudioSource music;
        [SerializeField, Min(1), InspectorName("VMD 帧率")] private float frameRate = 30;
        [SerializeField, Min(0), InspectorName("时长（秒，0 为自动）")] private float duration;
        [SerializeField, InspectorName("运行时播放")] private bool playOnEnable = true;
        [SerializeField, InspectorName("循环")] private bool loop;
        [SerializeField, Min(0), InspectorName("首帧姿态过渡（秒）")]
        [Tooltip("从开始播放时的角色骨骼姿态平滑进入动作首帧，让衣物物理逐步适应。过渡不占用动作和 BGM 时间；0 保留原行为。")]
        private float initialPoseTransitionSeconds;
        [SerializeField, Min(0), InspectorName("预热（秒）")] private float preroll = 1;
        [SerializeField, Min(0), InspectorName("当前时间（秒）")] private float currentTime;
        [SerializeField, InspectorName("正在播放")] private bool playing;
        private float preparationRemaining;
        private bool audioStarted;
        private bool audioPaused;
        private float appliedSoundtrackTimeOffset;
        private AudioSource controlledMusic;
        private float originalMusicPitch;
        // AudioSource cannot follow the panel's full 0..100 range. Outside its
        // positive pitch range, keep sampling the animation and pause the audio.
        private const float MaxAudioPitch = 3f;
        private const float AudioSyncTolerance = .1f;
        private float lastSampleTime = -1;
        private struct PreparationBone
        {
            public Transform target;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
        private readonly List<PreparationBone> preparationPose = new List<PreparationBone>();
        private bool preparationPoseCaptured;
        private bool externallyDriven;
        public bool ExternallyDriven
        {
            get => externallyDriven;
            set
            {
                if (externallyDriven == value) return;
                externallyDriven = value;
                ResetPreparationPose();
                if (value) HoPlaySpeedControl.Release(this);
                else RequestPlaybackSpeed();
            }
        }
        public float ExternalTime { get; set; }

        public Animator TargetAnimator { get => targetAnimator; set => targetAnimator = value; }
        public Object ReferenceRigAsset { get => referenceRigAsset; set => referenceRigAsset = value; }
        public HoVmdAnimation MotionClip { get => motionClip; set => motionClip = value; }
        public HoVmdAnimation ExpressionClip { get => expressionClip; set => expressionClip = value; }
        public HoVmdAnimation CameraClip { get => cameraClip; set => cameraClip = value; }
        public AudioClip Soundtrack { get => soundtrack; set => soundtrack = value; }
        public float SoundtrackTimeOffset { get => soundtrackTimeOffset; set => soundtrackTimeOffset = value; }
        public HoVmdMotionPlayer Body { get => body; set => body = value; }
        public HoVmdMotionPlayer Face { get => face; set => face = value; }
        public HoVmdHumanoidRetargeter Retargeter { get => retargeter; set => retargeter = value; }
        public HoVmdCameraPlayer CameraPlayer { get => cameraPlayer; set => cameraPlayer = value; }
        public AudioSource Music
        {
            get => music;
            set
            {
                if (music == value) return;
                ReleaseMusic();
                music = value;
            }
        }
        public float FrameRate { get => frameRate; set => frameRate = Mathf.Max(1, value); }
        public float Preroll { get => preroll; set => preroll = Mathf.Max(0, value); }
        public float InitialPoseTransitionSeconds
        {
            get => initialPoseTransitionSeconds;
            set => initialPoseTransitionSeconds = Mathf.Max(0, value);
        }
        public float PreparationDuration => InitialPoseTransitionSeconds + Preroll;
        public float CurrentTime => currentTime;
        public bool Playing
        {
            get => playing;
            set
            {
                bool wasPlaying = playing;
                playing = value;
                if (!value)
                {
                    PauseMusic();
                    HoPlaySpeedControl.Release(this);
                }
                else if (!wasPlaying) RequestPlaybackSpeed();
            }
        }
        public float Duration
        {
            get
            {
                if (duration > 0) return duration;
                if (music != null && music.clip != null) return music.clip.length + Mathf.Max(0, soundtrackTimeOffset);
                return body != null && body.Animation != null ? (body.Animation.LastBoneFrame + 1) / frameRate : 0;
            }
            set => duration = Mathf.Max(0, value);
        }

        private void OnEnable()
        {
            playing = playOnEnable;
            preparationRemaining = PreparationDuration;
            ResetPreparationPose();
            audioStarted = false;
            audioPaused = false;
            if (body != null) body.Playing = false;
            if (face != null) face.Playing = false;
            if (cameraPlayer != null) cameraPlayer.Playing = false;
            RequestPlaybackSpeed();
        }

        private void RequestPlaybackSpeed()
        {
            if (Application.isPlaying && isActiveAndEnabled && playing && !ExternallyDriven)
                HoPlaySpeedControl.SetOverride(this, 1f, lockPanel: true);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (ExternallyDriven)
            {
                ReleaseMusic();
                if (body != null) body.Playing = false;
                if (face != null) face.Playing = false;
                if (cameraPlayer != null) cameraPlayer.Playing = false;
                if (ExternalTime < 0) SamplePreparation(PreparationDuration + ExternalTime);
                else Sample(ExternalTime);
                return;
            }
            if (!playing)
            {
                PauseMusic();
                return;
            }
            // Component OnEnable order is not guaranteed; subordinate players
            // must remain externally clocked even after a later enable.
            if (body != null) body.Playing = false;
            if (face != null) face.Playing = false;
            if (cameraPlayer != null) cameraPlayer.Playing = false;
            if (preparationRemaining > 0)
            {
                SamplePreparation(PreparationDuration - preparationRemaining);
                preparationRemaining -= Time.deltaTime;
                return;
            }
            // AudioSource.time advances independently of Time.timeScale. Using it
            // as the master clock cancels slow motion (and even timeScale = 0)
            // whenever a soundtrack is present. Audio follows the scaled clock.
            float nextTime = currentTime + Time.deltaTime;
            float end = Duration;
            if (end > 0 && nextTime >= end)
            {
                if (loop)
                {
                    nextTime %= end;
                }
                else { nextTime = end; Playing = false; }
            }
            Sample(nextTime);
            if (playing) UpdateMusic();
            else PauseMusic();
        }

        private void UpdateMusic()
        {
            if (controlledMusic != music)
            {
                ReleaseMusic();
                if (music != null)
                {
                    controlledMusic = music;
                    originalMusicPitch = music.pitch;
                }
            }
            if (music == null || music.clip == null) return;

            float speed = Time.timeScale;
            float audioTime = currentTime - soundtrackTimeOffset;
            if (speed <= 0 || speed > MaxAudioPitch || audioTime < 0 || audioTime >= music.clip.length)
            {
                PauseMusic();
                return;
            }

            music.pitch = speed;
            if (!audioStarted)
            {
                music.time = audioTime;
                music.Play();
                audioStarted = true;
            }
            else if (audioPaused)
            {
                music.time = audioTime;
                music.UnPause();
                if (!music.isPlaying) music.Play();
            }
            else if (!music.isPlaying)
            {
                // A loop can wrap after the AudioSource has reached its end.
                music.time = audioTime;
                music.Play();
            }
            else if (!Mathf.Approximately(appliedSoundtrackTimeOffset, soundtrackTimeOffset) ||
                     Mathf.Abs(music.time - audioTime) > AudioSyncTolerance)
            {
                music.time = audioTime;
            }
            appliedSoundtrackTimeOffset = soundtrackTimeOffset;
            audioPaused = false;
        }

        private void PauseMusic()
        {
            if (controlledMusic == null || !audioStarted || audioPaused) return;
            controlledMusic.Pause();
            audioPaused = true;
        }

        private void ReleaseMusic()
        {
            if (controlledMusic != null)
            {
                controlledMusic.Stop();
                controlledMusic.pitch = originalMusicPitch;
                controlledMusic = null;
            }
            audioStarted = false;
            audioPaused = false;
        }

        public void Sample(float seconds)
        {
            if (lastSampleTime >= 0 && (seconds < lastSampleTime || Mathf.Abs(seconds - lastSampleTime) > .1f))
            {
                if (body != null) body.ResetIkHistory();
                if (retargeter != null)
                    foreach (var foot in retargeter.Feet)
                        if (foot.targetIk != null) foot.targetIk.ResetTemporalState();
            }
            lastSampleTime = seconds;
            currentTime = Mathf.Max(0, seconds);
            float frame = currentTime * frameRate;
            if (body != null)
            {
                body.ApplyAtFrame(frame);
                if (Application.isPlaying) body.SolveIkNow();
            }
            if (face != null) face.ApplyAtFrame(frame);
            if (retargeter != null) retargeter.ApplyPose();
            if (cameraPlayer != null) cameraPlayer.ApplyAtFrame(frame);
        }

        /// <summary>
        /// Advance preparation while the clip clock stays at zero. Physics and
        /// HoAux continue on their normal updates after the blended main pose.
        /// </summary>
        public void SamplePreparation(float elapsedSeconds)
        {
            if (InitialPoseTransitionSeconds <= 0)
            {
                Sample(0);
                return;
            }
            if (!preparationPoseCaptured) CapturePreparationPose();
            Sample(0);
            float progress = Mathf.Clamp01(elapsedSeconds / InitialPoseTransitionSeconds);
            if (progress >= 1) return;
            float weight = progress * progress * (3 - 2 * progress);
            // Blend the solved target pose in local space. Blending IK controls
            // alone would still teleport the Humanoid, or trigger another solve.
            foreach (PreparationBone pose in preparationPose)
            {
                if (pose.target == null) continue;
                pose.target.localPosition = Vector3.Lerp(pose.localPosition, pose.target.localPosition, weight);
                pose.target.localRotation = Quaternion.Slerp(pose.localRotation, pose.target.localRotation, weight);
            }
        }

        private void CapturePreparationPose()
        {
            preparationPose.Clear();
            var seen = new HashSet<Transform>();
            if (retargeter != null)
            {
                foreach (var bone in retargeter.Bones) Add(bone.target);
                Add(retargeter.TargetHips);
            }
            else if (targetAnimator != null && targetAnimator.isHuman)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; ++i)
                    Add(targetAnimator.GetBoneTransform((HumanBodyBones)i));
            }
            preparationPoseCaptured = true;

            void Add(Transform target)
            {
                if (target == null || !seen.Add(target)) return;
                preparationPose.Add(new PreparationBone
                {
                    target = target, localPosition = target.localPosition, localRotation = target.localRotation
                });
            }
        }

        private void ResetPreparationPose()
        {
            preparationPose.Clear();
            preparationPoseCaptured = false;
        }

        public void Restart()
        {
            currentTime = 0;
            preparationRemaining = PreparationDuration;
            ResetPreparationPose();
            audioStarted = false;
            audioPaused = false;
            playing = true;
            RequestPlaybackSpeed();
            if (music != null) music.Stop();
        }

        public void ApplySources()
        {
            if (body != null) body.Animation = motionClip;
            if (face != null) face.Animation = expressionClip;
            if (cameraPlayer != null) cameraPlayer.Animation = cameraClip;
            if (music != null) music.clip = soundtrack;
        }

        private void OnDisable()
        {
            ResetPreparationPose();
            HoPlaySpeedControl.Release(this);
            ReleaseMusic();
            if (music != null) music.Stop();
        }
    }
}
