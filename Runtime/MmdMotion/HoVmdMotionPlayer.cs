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
        [Serializable] public sealed class IkToggleBinding { public string vmdName; public GameObject targetObject; }

        [SerializeField] private new HoVmdAnimation animation;
        [SerializeField, HideInInspector] private HoVmdBindingProfile bindingProfile;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform skeletonRoot;
        [SerializeField, Min(1.0f)] private float frameRate = 30.0f;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool applyBones = true;
        [SerializeField] private bool applyMorphs = true;
        [SerializeField] private bool applyIkToggles = true;
        [SerializeField] private List<BoneBinding> boneBindings = new List<BoneBinding>();
        [SerializeField] private List<MorphBinding> morphBindings = new List<MorphBinding>();
        [SerializeField] private List<IkToggleBinding> ikToggleBindings = new List<IkToggleBinding>();
        [SerializeField, Min(0.0f)] private float currentFrame;
        [SerializeField] private bool playing;

        private readonly Dictionary<string, List<HoVmdBoneFrame>> boneTracks = new Dictionary<string, List<HoVmdBoneFrame>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<HoVmdMorphFrame>> morphTracks = new Dictionary<string, List<HoVmdMorphFrame>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> autoBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MorphBinding>> autoMorphs = new Dictionary<string, List<MorphBinding>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Transform> explicitBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MorphBinding>> explicitMorphs = new Dictionary<string, List<MorphBinding>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HoIkConstraint> ikControls = new Dictionary<string, HoIkConstraint>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> ikObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<Transform, Vector3> bindPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> bindRotations = new Dictionary<Transform, Quaternion>();
        private bool bindingsDirty = true;

        public HoVmdAnimation Animation { get => animation; set { animation = value; bindingsDirty = true; } }
        public HoVmdBindingProfile BindingProfile { get => bindingProfile; set { bindingProfile = value; bindingsDirty = true; } }
        public Transform ModelRoot { get => modelRoot != null ? modelRoot : transform; set { modelRoot = value; bindingsDirty = true; } }
        public Transform SkeletonRoot { get => skeletonRoot != null ? skeletonRoot : ModelRoot; set { skeletonRoot = value; bindingsDirty = true; } }
        public float FrameRate { get => frameRate; set => frameRate = Mathf.Max(1.0f, value); }
        public float CurrentFrame { get => currentFrame; set => currentFrame = Mathf.Max(0.0f, value); }
        public bool Playing { get => playing; set => playing = value; }
        public IList<BoneBinding> BoneBindings => boneBindings;
        public IList<MorphBinding> MorphBindings => morphBindings;
        public IList<IkToggleBinding> IkToggleBindings => ikToggleBindings;

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
            boneTracks.Clear(); morphTracks.Clear(); autoBones.Clear(); autoMorphs.Clear();
            explicitBones.Clear(); explicitMorphs.Clear(); ikControls.Clear(); ikObjects.Clear();
            bindPositions.Clear(); bindRotations.Clear();
            if (animation == null) return;
            foreach (var frame in animation.boneFrames) Track(boneTracks, frame.boneName, frame);
            foreach (var frame in animation.morphFrames) Track(morphTracks, frame.morphName, frame);
            Transform root = ModelRoot;
            if (root == null) return;

            foreach (var binding in boneBindings)
                if (binding != null && !string.IsNullOrEmpty(binding.vmdName) && binding.target != null)
                {
                    if (!explicitBones.ContainsKey(binding.vmdName)) explicitBones.Add(binding.vmdName, binding.target);
                    CacheBindPose(binding.target);
                }

            foreach (HoIkConstraint control in root.GetComponentsInChildren<HoIkConstraint>(true))
                if (control != null) ikControls[control.IkName] = control;

            Transform boneRoot = SkeletonRoot != null ? SkeletonRoot : root;
            var exactNames = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            var normalizedNames = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
            foreach (Transform bone in boneRoot.GetComponentsInChildren<Transform>(true))
            {
                AddCandidate(exactNames, bone.name, bone);
                AddCandidate(normalizedNames, NormalizeBoneName(bone.name), bone);
            }
            Animator animator = FindAnimator(boneRoot, root);
            foreach (string sourceName in UniqueNames(boneTracks))
            {
                Transform mapped = FindExplicitBone(sourceName);
                if (mapped == null && bindingProfile != null && bindingProfile.TryBone(sourceName, out string path))
                    mapped = path == "." ? root : root.Find(path);
                if (mapped == null) mapped = FindIkTarget(sourceName);
                if (mapped == null) mapped = FindHumanoidBone(animator, sourceName);
                if (mapped == null) mapped = FindNamedBone(exactNames, normalizedNames, sourceName);
                if (mapped == null) continue;
                autoBones[sourceName] = mapped;
                CacheBindPose(mapped);
            }

            var activeMorphNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sourceName in morphTracks.Keys)
                activeMorphNames.Add(NormalizeBoneName(sourceName));
            var normalizedMorphs = new Dictionary<string, List<MorphBinding>>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null) for (int i=0;i<renderer.sharedMesh.blendShapeCount;i++)
                {
                    string name=renderer.sharedMesh.GetBlendShapeName(i);
                    var candidate = new MorphBinding { vmdName = name, renderer = renderer, blendShapeIndex = i };
                    if (morphTracks.ContainsKey(name))
                    {
                        if (!autoMorphs.TryGetValue(name, out var list))
                            autoMorphs.Add(name, list = new List<MorphBinding>());
                        list.Add(candidate);
                    }
                    string normalized = NormalizeBoneName(name);
                    if (activeMorphNames.Contains(normalized))
                    {
                        if (!normalizedMorphs.TryGetValue(normalized, out var list))
                            normalizedMorphs.Add(normalized, list = new List<MorphBinding>());
                        list.Add(candidate);
                    }
                }
            foreach (var binding in morphBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.vmdName) || binding.renderer == null ||
                    binding.renderer.sharedMesh == null) continue;
                if (binding.blendShapeIndex < 0)
                    binding.blendShapeIndex = binding.renderer.sharedMesh.GetBlendShapeIndex(binding.vmdName);
                if (binding.blendShapeIndex < 0 || binding.blendShapeIndex >= binding.renderer.sharedMesh.blendShapeCount)
                    continue;
                if (!explicitMorphs.TryGetValue(binding.vmdName, out var list))
                    explicitMorphs.Add(binding.vmdName, list = new List<MorphBinding>());
                list.Add(binding);
            }
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
            foreach (string sourceName in morphTracks.Keys)
            {
                if (explicitMorphs.ContainsKey(sourceName) || autoMorphs.ContainsKey(sourceName)) continue;
                string normalized = NormalizeBoneName(sourceName);
                if (normalizedMorphs.TryGetValue(normalized, out var candidates) && candidates.Count == 1)
                    autoMorphs.Add(sourceName, new List<MorphBinding>
                    {
                        new MorphBinding { vmdName = sourceName, renderer = candidates[0].renderer,
                            blendShapeIndex = candidates[0].blendShapeIndex }
                    });
            }
            if (bindingProfile != null)
                foreach (var map in bindingProfile.ik)
                {
                    if (map == null || string.IsNullOrEmpty(map.sourceName) || string.IsNullOrEmpty(map.constraintPath)) continue;
                    var constraint = (map.constraintPath == "." ? root : root.Find(map.constraintPath))?.GetComponent<HoIkConstraint>();
                    if (constraint != null) ikControls[map.sourceName] = constraint;
                }
            foreach (var binding in ikToggleBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.vmdName) || binding.targetObject == null) continue;
                HoIkConstraint constraint = binding.targetObject.GetComponent<HoIkConstraint>();
                if (constraint != null) ikControls[binding.vmdName] = constraint;
                else ikObjects[binding.vmdName] = binding.targetObject;
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
                    if (explicitMorphs.TryGetValue(pair.Key, out var explicitBindings))
                    {
                        foreach (var binding in explicitBindings)
                            binding.renderer.SetBlendShapeWeight(binding.blendShapeIndex, value);
                    }
                    else if (autoMorphs.TryGetValue(pair.Key,out var list))
                        foreach(var binding in list)binding.renderer.SetBlendShapeWeight(binding.blendShapeIndex,value);
                }
            if (applyIkToggles && animation.showIkFrames.Count>0)
            {
                foreach (var control in ikControls)control.Value.SolveEnabled=SampleIk(control.Key,frame);
                foreach (var item in ikObjects)
                {
                    bool active = SampleIk(item.Key, frame);
                    if (item.Value != null && item.Value.activeSelf != active) item.Value.SetActive(active);
                }
            }
            // StandardRequest review runs in editor mode, so there is no
            // LateUpdate pass to solve the newly applied pose. Runtime playback
            // keeps the normal HoIkConstraint LateUpdate order.
            if (!Application.isPlaying)
                foreach (var control in ikControls.Values) control.SolveNow();
        }

        private Transform FindBone(string name)
        {
            if (explicitBones.TryGetValue(name, out var explicitTarget)) return explicitTarget;
            autoBones.TryGetValue(name,out var target);return target;
        }

        private Transform FindExplicitBone(string sourceName)
        {
            return explicitBones.TryGetValue(sourceName, out Transform target) ? target : null;
        }

        private Transform FindIkTarget(string sourceName)
        {
            if (ikControls.TryGetValue(sourceName, out var exact) && exact.Target != null)
                return exact.Target;
            string normalized = NormalizeBoneName(sourceName);
            foreach (var pair in ikControls)
                if (NormalizeBoneName(pair.Key) == normalized && pair.Value != null && pair.Value.Target != null)
                    return pair.Value.Target;
            return null;
        }

        private static Transform FindHumanoidBone(Animator animator, string sourceName)
        {
            if (animator == null || !animator.isHuman) return null;
            HumanBodyBones bone = HumanBodyBone(sourceName);
            return bone == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(bone);
        }

        private static Transform FindNamedBone(Dictionary<string, List<Transform>> exactNames,
            Dictionary<string, List<Transform>> normalizedNames, string sourceName)
        {
            if (TryUnique(exactNames, sourceName, out Transform match)) return match;
            string alias = BoneAlias(sourceName);
            if (!string.IsNullOrEmpty(alias) && TryUnique(exactNames, alias, out match)) return match;
            return TryUnique(normalizedNames, NormalizeBoneName(sourceName), out match) ? match : null;
        }

        private static Animator FindAnimator(Transform skeleton, Transform root)
        {
            Animator animator = skeleton.GetComponentInParent<Animator>();
            if (animator != null && animator.isHuman &&
                (animator.transform == root || animator.transform.IsChildOf(root))) return animator;
            foreach (Animator candidate in skeleton.GetComponentsInChildren<Animator>(true))
                if (candidate != null && candidate.isHuman) return candidate;
            return null;
        }

        private void CacheBindPose(Transform target)
        {
            if (target == null) return;
            if (!bindPositions.ContainsKey(target)) bindPositions.Add(target, target.localPosition);
            if (!bindRotations.ContainsKey(target)) bindRotations.Add(target, target.localRotation);
        }

        private static IEnumerable<string> UniqueNames<T>(Dictionary<string, List<T>> tracks)
        {
            foreach (var pair in tracks) yield return pair.Key;
        }

        private static void AddCandidate(Dictionary<string, List<Transform>> index, string key, Transform value)
        {
            if (!index.TryGetValue(key, out var list)) index.Add(key, list = new List<Transform>());
            list.Add(value);
        }

        private static bool TryUnique(Dictionary<string, List<Transform>> index, string key, out Transform value)
        {
            if (index.TryGetValue(key, out var list) && list.Count == 1)
            {
                value = list[0];
                return true;
            }
            value = null;
            return false;
        }

        private static HumanBodyBones HumanBodyBone(string name)
        {
            string side = name.StartsWith("左", StringComparison.Ordinal) ? "Left" :
                name.StartsWith("右", StringComparison.Ordinal) ? "Right" : string.Empty;
            if (name == "上半身") return HumanBodyBones.Spine;
            if (name == "上半身2") return HumanBodyBones.Chest;
            if (name == "下半身" || name == "腰") return HumanBodyBones.Hips;
            if (name == "首") return HumanBodyBones.Neck;
            if (name == "頭") return HumanBodyBones.Head;
            if (name == "左肩" || name == "右肩") return side == "Left" ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            if (name == "左腕" || name == "右腕") return side == "Left" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            if (name == "左ひじ" || name == "右ひじ") return side == "Left" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            if (name == "左手首" || name == "右手首") return side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            if (name == "左足" || name == "右足") return side == "Left" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            if (name == "左ひざ" || name == "右ひざ") return side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            if (name == "左足首" || name == "右足首") return side == "Left" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            if (name == "左つま先" || name == "右つま先") return side == "Left" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            return HumanBodyBones.LastBone;
        }

        private static string BoneAlias(string name)
        {
            switch (name)
            {
                case "下半身": return "hips";
                case "上半身": return "spine";
                case "上半身2": return "chest";
                case "首": return "neck";
                case "頭": return "head";
            }
            string suffix = name.StartsWith("左", StringComparison.Ordinal) ? ".L" :
                name.StartsWith("右", StringComparison.Ordinal) ? ".R" : null;
            if (suffix == null) return null;
            switch (name.Substring(1))
            {
                case "肩": return "shoulder" + suffix;
                case "腕": return "upper_arm" + suffix;
                case "ひじ": return "lower_arm" + suffix;
                case "手首": return "hand" + suffix;
                case "足": return "upper_leg" + suffix;
                case "ひざ": return "lower_leg" + suffix;
                case "足首": return "foot" + suffix;
                case "つま先": return "toes" + suffix;
                default: return null;
            }
        }

        private static string NormalizeBoneName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string normalized = value.Normalize(System.Text.NormalizationForm.FormKC);
            var builder = new System.Text.StringBuilder(normalized.Length);
            foreach (char c in normalized)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        private bool SampleIk(string name, float frame)
        {
            bool enabled = true;
            for (int i = 0; i < animation.showIkFrames.Count; i++)
            {
                HoVmdShowIkFrame frameData = animation.showIkFrames[i];
                if (frameData.frame > frame || frameData.toggles == null) continue;
                foreach (var toggle in frameData.toggles)
                    if (toggle.boneName == name) enabled = toggle.enabled;
            }
            return enabled;
        }

        private static Vector3 ToUnityPosition(Vector3 value)=>new Vector3(-value.x,value.y,-value.z)*.08f;
        private static Quaternion ToUnityRotation(Quaternion value)=>new Quaternion(-value.x,value.y,-value.z,value.w).normalized;

        private static void Track<T>(Dictionary<string,List<T>> tracks,string name,T frame)
        { if(string.IsNullOrEmpty(name))return;if(!tracks.TryGetValue(name,out var list))tracks.Add(name,list=new List<T>());list.Add(frame); }

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

}
