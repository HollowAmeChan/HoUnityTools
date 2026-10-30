using System;
using System.Collections.Generic;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>Plays imported VMD bone/morph/show-IK tracks on an existing Unity hierarchy.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32050)]
    [AddComponentMenu("HoUnityTools/MMD/VMD Motion Player")]
    public sealed class HoVmdMotionPlayer : MonoBehaviour
    {
        [Serializable] public sealed class BoneBinding { public string vmdName; public Transform target; }
        [Serializable] public sealed class MorphBinding { public string vmdName; public SkinnedMeshRenderer renderer; public int blendShapeIndex = -1; }

        [SerializeField] private HoVmdAnimation animation;
        [SerializeField] private HoVmdBindingProfile bindingProfile;
        [SerializeField] private Transform modelRoot;
        [SerializeField, Min(1.0f)] private float frameRate = 30.0f;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool applyBones = true;
        [SerializeField] private bool applyMorphs = true;
        [SerializeField] private bool applyIkToggles = true;
        [SerializeField] private List<BoneBinding> boneBindings = new List<BoneBinding>();
        [SerializeField] private List<MorphBinding> morphBindings = new List<MorphBinding>();
        [SerializeField, Min(0.0f)] private float currentFrame;
        [SerializeField] private bool playing;

        private readonly Dictionary<string, List<HoVmdBoneFrame>> boneTracks = new Dictionary<string, List<HoVmdBoneFrame>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<HoVmdMorphFrame>> morphTracks = new Dictionary<string, List<HoVmdMorphFrame>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> autoBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MorphBinding>> autoMorphs = new Dictionary<string, List<MorphBinding>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HoIkConstraint> ikControls = new Dictionary<string, HoIkConstraint>(StringComparer.Ordinal);
        private readonly Dictionary<Transform, Vector3> bindPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> bindRotations = new Dictionary<Transform, Quaternion>();
        private bool bindingsDirty = true;

        public HoVmdAnimation Animation { get => animation; set { animation = value; bindingsDirty = true; } }
        public HoVmdBindingProfile BindingProfile { get => bindingProfile; set { bindingProfile = value; bindingsDirty = true; } }
        public Transform ModelRoot { get => modelRoot != null ? modelRoot : transform; set { modelRoot = value; bindingsDirty = true; } }
        public float FrameRate { get => frameRate; set => frameRate = Mathf.Max(1.0f, value); }
        public float CurrentFrame { get => currentFrame; set => currentFrame = Mathf.Max(0.0f, value); }
        public bool Playing { get => playing; set => playing = value; }
        public IList<BoneBinding> BoneBindings => boneBindings;
        public IList<MorphBinding> MorphBindings => morphBindings;

        private void OnEnable()
        {
            playing = playOnEnable;
            bindingsDirty = true;
        }

        private void OnValidate()
        {
            frameRate = Mathf.Max(1.0f, frameRate);
            currentFrame = Mathf.Max(0.0f, currentFrame);
            bindingsDirty = true;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !playing) return;
            if (animation == null) return;
            if (bindingsDirty) RebuildBindings();
            ApplyAtFrame(currentFrame);
            currentFrame += Time.deltaTime * frameRate;
            uint end = animation.LastBoneFrame;
            if (animation.LastMorphFrame > end) end = animation.LastMorphFrame;
            if (animation.LastShowIkFrame > end) end = animation.LastShowIkFrame;
            if (end > 0 && currentFrame > end)
            {
                if (loop) currentFrame %= end;
                else { currentFrame = end; playing = false; }
            }
        }

        public void RebuildBindings()
        {
            bindingsDirty = false;
            boneTracks.Clear(); morphTracks.Clear(); autoBones.Clear(); autoMorphs.Clear(); ikControls.Clear();
            bindPositions.Clear(); bindRotations.Clear();
            if (animation == null) return;
            foreach (var frame in animation.boneFrames) Track(boneTracks, frame.boneName, frame);
            foreach (var frame in animation.morphFrames) Track(morphTracks, frame.morphName, frame);
            Transform root = ModelRoot;
            if (root == null) return;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (!autoBones.ContainsKey(child.name)) autoBones.Add(child.name, child);
            foreach (Transform child in autoBones.Values)
            {
                if (!bindPositions.ContainsKey(child)) bindPositions.Add(child, child.localPosition);
                if (!bindRotations.ContainsKey(child)) bindRotations.Add(child, child.localRotation);
            }
            foreach (var binding in boneBindings)
                if (binding != null && binding.target != null) { bindPositions[binding.target] = binding.target.localPosition; bindRotations[binding.target] = binding.target.localRotation; }
            if (bindingProfile != null)
                foreach (var map in bindingProfile.bones)
                {
                    if (map == null || string.IsNullOrEmpty(map.sourceName) || string.IsNullOrEmpty(map.targetPath)) continue;
                    Transform mapped = map.targetPath == "." ? root : root.Find(map.targetPath);
                    if (mapped == null) continue;
                    autoBones[map.sourceName] = mapped;
                    if (!bindPositions.ContainsKey(mapped)) bindPositions.Add(mapped, mapped.localPosition);
                    if (!bindRotations.ContainsKey(mapped)) bindRotations.Add(mapped, mapped.localRotation);
                }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null) for (int i=0;i<renderer.sharedMesh.blendShapeCount;i++)
                {
                    string name=renderer.sharedMesh.GetBlendShapeName(i);
                    if (!autoMorphs.TryGetValue(name,out var list))autoMorphs.Add(name,list=new List<MorphBinding>());
                    list.Add(new MorphBinding{vmdName=name,renderer=renderer,blendShapeIndex=i});
                }
            foreach (var binding in morphBindings)
                if (binding != null && binding.renderer != null && binding.blendShapeIndex < 0 && binding.renderer.sharedMesh != null)
                    binding.blendShapeIndex=binding.renderer.sharedMesh.GetBlendShapeIndex(binding.vmdName);
            if (bindingProfile != null)
                foreach (var map in bindingProfile.morphs)
                {
                    if (map == null || string.IsNullOrEmpty(map.sourceName) || string.IsNullOrEmpty(map.rendererPath)) continue;
                    var renderer = (map.rendererPath == "." ? root : root.Find(map.rendererPath))?.GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null) continue;
                    int index = renderer.sharedMesh.GetBlendShapeIndex(map.blendShapeName);
                    if (index < 0) continue;
                    if (!autoMorphs.TryGetValue(map.sourceName, out var list)) autoMorphs.Add(map.sourceName, list = new List<MorphBinding>());
                    list.Add(new MorphBinding { vmdName = map.sourceName, renderer = renderer, blendShapeIndex = index });
                }
            foreach (HoIkConstraint control in root.GetComponentsInChildren<HoIkConstraint>(true))
                if (control != null) ikControls[control.IkName]=control;
            if (bindingProfile != null)
                foreach (var map in bindingProfile.ik)
                {
                    if (map == null || string.IsNullOrEmpty(map.sourceName) || string.IsNullOrEmpty(map.constraintPath)) continue;
                    var constraint = (map.constraintPath == "." ? root : root.Find(map.constraintPath))?.GetComponent<HoIkConstraint>();
                    if (constraint != null) ikControls[map.sourceName] = constraint;
                }
        }

        public void ApplyAtFrame(float frame)
        {
            if (animation == null) return;
            if (bindingsDirty) RebuildBindings();
            if (applyBones)
                foreach (var pair in boneTracks)
                {
                    Transform target=FindBone(pair.Key); if (target==null)continue;
                    HoVmdBoneFrame value=SampleBone(pair.Value,frame);
                    if (!bindPositions.TryGetValue(target,out var position))position=target.localPosition;
                    if (!bindRotations.TryGetValue(target,out var rotation))rotation=target.localRotation;
                    target.localPosition=position+ToUnityPosition(value.position);
                    target.localRotation=ToUnityRotation(value.rotation)*rotation;
                }
            if (applyMorphs)
                foreach (var pair in morphTracks)
                {
                    float value=SampleMorph(pair.Value,frame)*100.0f;
                    if (autoMorphs.TryGetValue(pair.Key,out var list))foreach(var binding in list)binding.renderer.SetBlendShapeWeight(binding.blendShapeIndex,value);
                    foreach(var binding in morphBindings)if(binding!=null&&binding.vmdName==pair.Key&&binding.renderer!=null&&binding.blendShapeIndex>=0)binding.renderer.SetBlendShapeWeight(binding.blendShapeIndex,value);
                }
            if (applyIkToggles && animation.showIkFrames.Count>0)
                foreach (var control in ikControls)control.Value.SolveEnabled=SampleIk(control.Key,frame);
            // StandardRequest review runs in editor mode, so there is no
            // LateUpdate pass to solve the newly applied pose. Runtime playback
            // keeps the normal HoIkConstraint LateUpdate order.
            if (!Application.isPlaying)
                foreach (var control in ikControls.Values) control.SolveNow();
        }

        private Transform FindBone(string name)
        {
            foreach(var binding in boneBindings)if(binding!=null&&binding.vmdName==name&&binding.target!=null)return binding.target;
            autoBones.TryGetValue(name,out var target);return target;
        }

        private bool SampleIk(string name,float frame)
        {
            bool value=true;
            for(int i=0;i<animation.showIkFrames.Count;i++)if(animation.showIkFrames[i].frame<=frame)foreach(var toggle in animation.showIkFrames[i].toggles)if(toggle.boneName==name)value=toggle.enabled;
            return value;
        }

        private static Vector3 ToUnityPosition(Vector3 value)=>new Vector3(-value.x,value.y,-value.z)*.08f;
        private static Quaternion ToUnityRotation(Quaternion value)=>new Quaternion(-value.x,value.y,-value.z,value.w).normalized;

        private static void Track<T>(Dictionary<string,List<T>> tracks,string name,T frame)
        { if(!tracks.TryGetValue(name,out var list))tracks.Add(name,list=new List<T>());list.Add(frame); }

        private static HoVmdBoneFrame SampleBone(List<HoVmdBoneFrame> track,float frame)
        {
            if(track.Count==0)return default;if(frame<=track[0].frame)return track[0];if(frame>=track[track.Count-1].frame)return track[track.Count-1];
            int next=1;while(next<track.Count&&track[next].frame<frame)next++;var a=track[next-1];var b=track[next];float u=(frame-a.frame)/Mathf.Max(1,b.frame-a.frame);float t=Bezier(b.interpolation.rotation,u);
            var value=b;value.position=Vector3.Lerp(a.position,b.position,Bezier(b.interpolation.positionX,u));value.position=new Vector3(value.position.x,Mathf.Lerp(a.position.y,b.position.y,Bezier(b.interpolation.positionY,u)),Mathf.Lerp(a.position.z,b.position.z,Bezier(b.interpolation.positionZ,u)));value.rotation=Quaternion.Slerp(a.rotation,b.rotation,t);return value;
        }

        private static float SampleMorph(List<HoVmdMorphFrame> track,float frame)
        { if(track.Count==0)return 0;if(frame<=track[0].frame)return track[0].weight;if(frame>=track[track.Count-1].frame)return track[track.Count-1].weight;int next=1;while(next<track.Count&&track[next].frame<frame)next++;var a=track[next-1];var b=track[next];return Mathf.Lerp(a.weight,b.weight,(frame-a.frame)/Mathf.Max(1,b.frame-a.frame)); }
        private static float Bezier(HoVmdBezier curve,float x)
        { float t=x;for(int i=0;i<6;i++){float omt=1-t;float f=3*omt*omt*t*curve.x1+3*omt*t*t*curve.x2+t*t*t-x;float d=3*omt*omt*curve.x1+6*omt*t*(curve.x2-curve.x1)+3*t*t*(1-curve.x2);if(Mathf.Abs(d)<1e-5f)break;t=Mathf.Clamp01(t-f/d);}float omt2=1-t;return 3*omt2*omt2*t*curve.y1+3*omt2*t*t*curve.y2+t*t*t; }
    }

    /// <summary>Applies a VMD camera track through a target plus camera child rig.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32060)]
    [AddComponentMenu("HoUnityTools/MMD/VMD Camera Player")]
    public sealed class HoVmdCameraPlayer : MonoBehaviour
    {
        [SerializeField] public new HoVmdAnimation animation;
        [SerializeField] public Camera targetCamera;
        [SerializeField] public Transform cameraTarget;
        [SerializeField, Min(1.0f)] public float frameRate=30;
        [SerializeField] public bool playOnEnable=true, loop=true;
        [SerializeField, Min(0.0f)] public float currentFrame;
        [SerializeField, Min(0.0001f)] public float mmdToUnityScale=.08f;
        [SerializeField] public bool snapAdjacentFrames=true;
        [SerializeField, Min(0.0001f)] public float orthographicSizeMultiplier=1.0f;
        private bool playing;
        private readonly List<HoVmdCameraFrame> cameraTrack = new List<HoVmdCameraFrame>();
        private bool trackDirty=true;

        public HoVmdAnimation Animation { get => animation; set { animation=value; trackDirty=true; } }
        public Camera TargetCamera { get => targetCamera; set => targetCamera=value; }
        public Transform CameraTarget { get => cameraTarget; set => cameraTarget=value; }
        public float CurrentFrame { get => currentFrame; set => currentFrame=Mathf.Max(0, value); }
        public bool Playing { get => playing; set => playing=value; }

        private void OnEnable(){playing=playOnEnable;trackDirty=true;}
        private void OnValidate(){frameRate=Mathf.Max(1,frameRate);currentFrame=Mathf.Max(0,currentFrame);mmdToUnityScale=Mathf.Max(.0001f,mmdToUnityScale);orthographicSizeMultiplier=Mathf.Max(.0001f,orthographicSizeMultiplier);trackDirty=true;}
        private void LateUpdate(){if(!Application.isPlaying||!playing||animation==null||targetCamera==null||cameraTarget==null)return;if(trackDirty)RebuildTrack();if(cameraTrack.Count==0)return;ApplyAtFrame(currentFrame);currentFrame+=Time.deltaTime*frameRate;if(currentFrame>LastFrame){if(loop)currentFrame%=Mathf.Max(1,LastFrame);else{currentFrame=LastFrame;playing=false;}}}

        public uint LastFrame => cameraTrack.Count>0 ? cameraTrack[cameraTrack.Count-1].frame : animation!=null ? animation.LastCameraFrame : 0;

        public void RebuildTrack()
        {
            cameraTrack.Clear();
            if(animation==null||animation.cameraFrames==null){trackDirty=false;return;}
            cameraTrack.AddRange(animation.cameraFrames);
            cameraTrack.Sort((a,b)=>a.frame.CompareTo(b.frame));
            trackDirty=false;
        }

        public void ApplyAtFrame(float frame)
        {
            if(animation==null||targetCamera==null||cameraTarget==null)return;
            if(trackDirty)RebuildTrack();
            if(cameraTrack.Count==0)return;
            HoVmdCameraFrame value=Sample(frame);
            cameraTarget.position=new Vector3(-value.targetPosition.x,value.targetPosition.y,-value.targetPosition.z)*mmdToUnityScale;
            Vector3 euler=new Vector3(-value.rotation.x*Mathf.Rad2Deg,value.rotation.y*Mathf.Rad2Deg+180,value.rotation.z*Mathf.Rad2Deg);
            Quaternion rotation=Quaternion.Euler(euler);
            cameraTarget.rotation=rotation;
            targetCamera.transform.rotation=rotation;
            // MMD's signed distance is meaningful: negative values pull the camera
            // through the orbit center instead of being clamped to the other side.
            targetCamera.transform.position=cameraTarget.position-rotation*Vector3.forward*(value.distance*mmdToUnityScale);
            targetCamera.fieldOfView=Mathf.Clamp(value.viewAngle,1,179);
            targetCamera.orthographic=value.perspectiveOff;
            if(value.perspectiveOff)
                targetCamera.orthographicSize=Mathf.Max(.0001f,Mathf.Abs(value.distance*mmdToUnityScale)*Mathf.Tan(targetCamera.fieldOfView*Mathf.Deg2Rad*.5f)*orthographicSizeMultiplier);
        }

        private HoVmdCameraFrame Sample(float frame)
        {
            if(frame<=cameraTrack[0].frame)return cameraTrack[0];
            int next=1;while(next<cameraTrack.Count&&cameraTrack[next].frame<frame)next++;
            HoVmdCameraFrame value=cameraTrack[Mathf.Clamp(next-1,0,cameraTrack.Count-1)];
            if(next>=cameraTrack.Count)return value;
            HoVmdCameraFrame b=cameraTrack[next];
            if(frame>=b.frame)return b;
            uint gap=b.frame-value.frame;
            // Adjacent camera keys are commonly authored as hard cuts.
            if(snapAdjacentFrames&&gap<=1)return value;
            float u=(frame-value.frame)/Mathf.Max(1,gap);float movement=Bezier(b.interpolation.movement,u);
            value.targetPosition=Vector3.Lerp(value.targetPosition,b.targetPosition,movement);
            value.distance=Mathf.Lerp(value.distance,b.distance,Bezier(b.interpolation.distance,u));
            value.rotation=Vector3.Lerp(value.rotation,b.rotation,Bezier(b.interpolation.rotation,u));
            value.viewAngle=(uint)Mathf.Round(Mathf.Lerp(value.viewAngle,b.viewAngle,Bezier(b.interpolation.viewAngle,u)));
            return value;
        }

        private static float Bezier(HoVmdBezier c,float x){float t=x;for(int i=0;i<6;i++){float o=1-t;float f=3*o*o*t*c.x1+3*o*t*t*c.x2+t*t*t-x;float d=3*o*o*c.x1+6*o*t*(c.x2-c.x1)+3*t*t*(1-c.x2);if(Mathf.Abs(d)<1e-5f)break;t=Mathf.Clamp01(t-f/d);}float o2=1-t;return 3*o2*o2*t*c.y1+3*o2*t*t*c.y2+t*t*t;}
    }
}
