using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.RigConstraints;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Samples the imported avatar in Play Mode, after applying HoFBX MCH bindings.</summary>
[DefaultExecutionOrder(32002)]
public sealed class HoBsCharacterParityPlayer : MonoBehaviour
{
    [Serializable] public sealed class Rotation { public float x, y, z, w; }
    [Serializable] public sealed class Frame { public int frame; public float time; public Rotation[] rotations; public float[] weights; }
    [Serializable] public sealed class Output { public string mesh, shape, ruleId, pointId; public bool mute; }
    [Serializable] public sealed class Samples { public string armature, clipId; public int fps; public float duration; public string[] bones; public Output[] outputs; public Frame[] frames; }
    [Serializable] public sealed class Result
    {
        public string unity, clip, status;
        public bool avatarValid, avatarHuman, clipHuman;
        public bool tPoseApplied;
        public float tPoseErrorBefore, tPoseErrorAfter;
        public int tPoseChangedBones;
        public int tPoseMappedBones;
        public bool tPoseSolverVerified;
        public int frames, outputs, importedConstraints, nonemptyOutputs;
        public float replayMaxWeightError, animationMaxWeightError, animationMeanWeightError;
        public float animationMaxBoneAngle, mchMaxAngle, sourceAnimationMaxBoneAngle, mchSourceMaxAngle;
        public List<string> issues = new List<string>();
        public List<Frame> unityFrames = new List<Frame>();
    }

    public Animator animator;
    public HoBsCorrective corrective;
    public TextAsset samples;
    public AnimationClip clip;
    public int importedConstraints, nonemptyOutputs;
    public float replayMaxWeightError;
    public bool tPoseApplied;
    public float tPoseErrorBefore, tPoseErrorAfter;
    public int tPoseChangedBones;
    public int tPoseMappedBones;
    public bool tPoseSolverVerified;
    public Transform[] sourceBones;
    public Quaternion[] sourceBindRotations;
    private PlayableGraph graph;
    private AnimationClipPlayable playable;
    private Samples data;
    private HoBsCorrective.Rule[] rules;
    private readonly Result result = new Result();
    private int next;
    private double totalError;

    private void Start()
    {
        try
        {
            data = UnityEngine.JsonUtility.FromJson<Samples>(samples.text);
            result.unity = Application.unityVersion;
            result.clip = data.clipId;
            result.frames = data.frames.Length;
            result.outputs = data.outputs.Length;
            result.importedConstraints = importedConstraints;
            result.nonemptyOutputs = nonemptyOutputs;
            result.replayMaxWeightError = replayMaxWeightError;
            result.tPoseApplied = tPoseApplied;
            result.tPoseErrorBefore = tPoseErrorBefore;
            result.tPoseErrorAfter = tPoseErrorAfter;
            result.tPoseChangedBones = tPoseChangedBones;
            result.tPoseMappedBones = tPoseMappedBones;
            result.tPoseSolverVerified = tPoseSolverVerified;
            result.avatarValid = animator != null && animator.avatar != null && animator.avatar.isValid;
            result.avatarHuman = animator != null && animator.isHuman;
            result.clipHuman = clip != null && clip.isHumanMotion;
            if (!result.avatarValid || !result.avatarHuman || !result.clipHuman)
                throw new InvalidOperationException("Avatar or VRMA_12 clip lost Humanoid state.");
            rules = data.outputs.Select(output => corrective.Rules.Single(rule =>
                rule.target.name == output.mesh && rule.shapeName == output.shape)).ToArray();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            graph = PlayableGraph.Create("HoFBX BS character parity");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph, "Humanoid", animator).SetSourcePlayable(playable);
            graph.Play();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Update()
    {
        if (data == null) return;
        try
        {
            Frame frame = data.frames[next];
            playable.SetTime(frame.time);
            graph.Evaluate(0f);
            foreach (HoAuxRig rig in GetComponentsInChildren<HoAuxRig>(true)) rig.EvaluateNow();
            corrective.EvaluateNow();

            var measured = new Frame
            {
                frame = frame.frame, time = frame.time,
                weights = new float[rules.Length], rotations = new Rotation[data.bones.Length]
            };
            for (int i = 0; i < rules.Length; i++)
            {
                HoBsCorrective.Rule rule = rules[i];
                int shapeIndex = rule.target.sharedMesh.GetBlendShapeIndex(rule.shapeName);
                measured.weights[i] = rule.target.GetBlendShapeWeight(shapeIndex) * 0.01f;
                float error = Mathf.Abs(measured.weights[i] - frame.weights[i]);
                totalError += error;
                result.animationMaxWeightError = Mathf.Max(result.animationMaxWeightError, error);
                if (!string.IsNullOrEmpty(rule.lastError)) throw new InvalidOperationException(rule.lastError);
            }
            for (int i = 0; i < data.bones.Length; i++)
            {
                HoBsCorrective.Rule rule = rules.First(r => r.sourceBone == data.bones[i]);
                Quaternion relative = HoBsCorrective.ReadLocalRotation(rule.bone.localRotation, rule.referenceRotation);
                Quaternion unityRotation = relative;
                measured.rotations[i] = ToData(new Quaternion(relative.x, -relative.y, -relative.z, relative.w));
                Quaternion source = Quaternion.Inverse(sourceBindRotations[i]) * sourceBones[i].localRotation;
                if (rule.measurementIsMch)
                    source = Quaternion.Inverse(rule.referenceRotation) * source * rule.referenceRotation;
                float angle = Quaternion.Angle(unityRotation, ToUnity(frame.rotations[i]));
                result.animationMaxBoneAngle = Mathf.Max(result.animationMaxBoneAngle, angle);
                result.sourceAnimationMaxBoneAngle = Mathf.Max(
                    result.sourceAnimationMaxBoneAngle, Quaternion.Angle(source, ToUnity(frame.rotations[i])));
                if (rule.measurementIsMch)
                {
                    result.mchMaxAngle = Mathf.Max(result.mchMaxAngle, angle);
                    result.mchSourceMaxAngle = Mathf.Max(result.mchSourceMaxAngle, Quaternion.Angle(relative, source));
                }
            }
            result.unityFrames.Add(measured);
            next++;
            if (next >= data.frames.Length) Finish();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Finish()
    {
        if (graph.IsValid()) graph.Destroy();
        result.animationMeanWeightError =
            (float)(totalError / Math.Max(1, rules.Length * data.frames.Length));
        if (result.mchSourceMaxAngle > 0.05f)
            result.issues.Add("HoFBX MCH input does not track the imported source bone.");
        if (result.sourceAnimationMaxBoneAngle > 0.1f)
            result.issues.Add("Unity Humanoid retarget pose differs from the Blender Humanoid Debug samples.");
        if (result.animationMaxWeightError > 0.01f)
            result.issues.Add("BS weights differ when the two Humanoid retargeters produce different bone poses.");
        result.status = result.issues.Count == 0 ? "PASS" : "DIFFERENCES";
        WriteAndExit(0);
    }

    private void Fail(Exception exception)
    {
        result.status = "ERROR";
        result.issues.Add(exception.ToString());
        Debug.LogException(exception, this);
        if (graph.IsValid()) graph.Destroy();
        WriteAndExit(1);
    }

    private void WriteAndExit(int exitCode)
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "character_results.json");
        File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(result, true));
        Debug.Log("HO_BS_CHARACTER_PARITY " + result.status);
        UnityEditor.EditorApplication.Exit(exitCode);
    }

    private static Quaternion ToUnity(Rotation q) => new Quaternion(q.x, -q.y, -q.z, q.w);
    private static Rotation ToData(Quaternion q) => new Rotation { x=q.x, y=q.y, z=q.z, w=q.w };
}
