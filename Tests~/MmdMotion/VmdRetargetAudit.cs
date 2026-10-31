#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.MmdMotion;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Explicit audit of the saved film setup. Loads temporary preview scenes, never
/// saves them or captures a new bind pose. No PlayerLoop or simulation is advanced.
/// Copy into an Editor test host and call Run; nothing runs on import.
/// </summary>
public static class VmdRetargetAudit
{
    [Serializable] public sealed class Report
    {
        public string createdUtc, sourceRevision, method;
        public bool loadedScenesPreserved;
        public Film[] films;
        public SolverProbe[] solverProbes;
    }
    [Serializable] public sealed class SolverProbe
    {
        public string axis;
        public float intendedBend, residual;
        public Vector3 kneeEuler;
    }
    [Serializable] public sealed class Film
    {
        public string scene, motion, face, camera, error, seekErrorBone;
        public int sampledFrames, referenceBones, mappedBones, ikMappings;
        public float duration, audioDuration, translationScale, maxSeekPositionError, maxSeekRotationError;
        public float maxSourceReachResidual, maxTargetReachResidual, maxKneeDifference;
        public float maxStaleToeTargetError, maxFootAxisRestDifference;
        public int seekErrorFrame, sourceReachFrame, kneeDifferenceFrame, staleToeFrame, targetToeDisabledSamples;
        public string[] missingBoneTracks, missingNonzeroMorphTracks, missingZeroMorphTracks;
        public HoVmdMorphAuditResult morphAudit;
        public FootRow[] feet;
        public BoneRow[] restBones;
        public SeekRow[] seekErrors;
    }
    [Serializable] public sealed class SeekRow
    {
        public string bone;
        public float frame, positionError, rotationError;
    }
    [Serializable] public sealed class FootRow
    {
        public string name;
        public float frame;
        public bool sourceEnabled, targetEnabled, toe;
        public float sourceResidual, targetResidual, sourceKnee, targetKnee;
        public float sourceReachRatio, targetReachRatio;
        public Vector3 sourcePosition, targetPosition, sourceGoal, targetGoal, sourceHip, targetHip;
        public Quaternion sourceAnkleRotation, targetAnkleRotation;
        public Vector3 sourceKneeEuler, targetKneeEuler, sourceFootAxis, targetFootAxis;
    }
    [Serializable] public sealed class BoneRow
    {
        public string source, target, targetParent;
        public Vector3 sourcePosition, targetPosition;
        public Quaternion sourceRotation, targetRotation, offset;
    }
    [Serializable] public sealed class SourReport { public string prefab, motion; public SourFrame[] frames; }
    [Serializable] public sealed class SourFrame { public float vmdFrame; public SourBone[] bones; }
    [Serializable] public sealed class SourBone { public string name; public Vector3 position; }
    private sealed class Pose
    {
        internal Vector3[] positions;
        internal Quaternion[] rotations;
    }

    public static string Run(string filmRoot, string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Audit requires an idle Editor.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var states = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).ToArray();
        var dirty = states.Select(s => s.isDirty).ToArray();
        var active = SceneManager.GetActiveScene();
        var report = new Report {
            createdUtc = DateTime.UtcNow.ToString("O"),
            sourceRevision = "Set externally alongside the report.",
            method = "Saved scenes in OpenPreviewScene; every 5th frame plus NEMONEMO 7.5-10.5s at 60fps; shuffled seek; no saves or simulation.",
            solverProbes = ProbeHingeLimits()
        };
        var films = new List<Film>();
        foreach (string path in Directory.GetFiles(filmRoot, "*.unity", SearchOption.AllDirectories)
                     .Where(p => p.Replace('\\', '/').Contains("/Scenes/") &&
                         !p.Replace('\\', '/').Contains("/_验证/")).OrderBy(p => p, StringComparer.Ordinal))
        {
            var film = new Film { scene = path.Replace('\\', '/') };
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(film.scene);
                var roots = preview.GetRootGameObjects();
                var sequence = roots.SelectMany(r => r.GetComponentsInChildren<HoVmdSequencePlayer>(true))
                    .First(s => s.Retargeter != null);
                var rig = sequence.Retargeter;
                film.motion = AssetDatabase.GetAssetPath(sequence.Body.Animation);
                film.face = sequence.Face != null ? AssetDatabase.GetAssetPath(sequence.Face.Animation) : null;
                film.camera = sequence.CameraPlayer != null ? AssetDatabase.GetAssetPath(sequence.CameraPlayer.Animation) : null;
                film.duration = sequence.Duration;
                film.audioDuration = sequence.Soundtrack != null ? sequence.Soundtrack.length : 0;
                film.translationScale = rig.TranslationScale;
                film.referenceBones = rig.SourceSpace.GetComponentsInChildren<Transform>(true).Length;
                film.mappedBones = rig.Bones.Count;
                film.ikMappings = rig.Feet.Count;
                film.restBones = rig.Bones.Where(b => b.source != null && b.target != null).Select(b => new BoneRow {
                    source = b.source.name, target = b.target.name, targetParent = b.target.parent?.name,
                    sourcePosition = rig.SourceSpace.InverseTransformPoint(b.source.position),
                    targetPosition = rig.TargetSpace.InverseTransformPoint(b.target.position),
                    sourceRotation = Quaternion.Inverse(rig.SourceSpace.rotation) * b.source.rotation,
                    targetRotation = Quaternion.Inverse(rig.TargetSpace.rotation) * b.target.rotation,
                    offset = b.rotationOffset
                }).ToArray();
                sequence.Body.RebuildBindings();
                sequence.Face?.RebuildBindings();
                var findBone = typeof(HoVmdMotionPlayer).GetMethod("FindBone", BindingFlags.Instance | BindingFlags.NonPublic);
                film.missingBoneTracks = sequence.Body.Animation.boneFrames.Select(k => k.boneName)
                    .Distinct().Where(n => (Transform)findBone.Invoke(sequence.Body, new object[] { n }) == null).ToArray();
                if (sequence.Face != null && sequence.Face.Animation != null)
                {
                    film.morphAudit = HoVmdMorphAudit.Audit(sequence.Face.Animation, rig.TargetSpace.gameObject,
                        sequence.Face.BindingProfile, sequence.Face.MorphBindings);
                    var nonzero = new HashSet<string>(sequence.Face.Animation.morphFrames
                        .Where(k => Mathf.Abs(k.weight) > .00001f).Select(k => k.morphName));
                    film.missingNonzeroMorphTracks = film.morphAudit.missingNames.Where(nonzero.Contains).ToArray();
                    film.missingZeroMorphTracks = film.morphAudit.missingNames.Where(n => !nonzero.Contains(n)).ToArray();
                }
                int last = (int)sequence.Body.Animation.LastBoneFrame;
                IEnumerable<float> frameList = Enumerable.Range(0, last + 1)
                    .Where(f => f % 5 == 0 || f == last).Select(f => (float)f);
                if (film.scene.Contains("NEMONEMO"))
                    frameList = frameList.Concat(Enumerable.Range(450, 181).Select(f => f * .5f));
                var frames = frameList.Distinct().OrderBy(f => f).ToArray();
                var targets = rig.SourceSpace.GetComponentsInChildren<Transform>(true)
                    .Concat(rig.Bones.Select(b => b.target).Where(b => b != null)).Distinct().ToArray();
                var poses = new Dictionary<float, Pose>();
                var rows = new List<FootRow>();
                foreach (var foot in rig.Feet.Where(f => f.sourceIk != null && f.targetIk != null &&
                    f.sourceIk.Links.Count == 1))
                {
                    var a = rig.SourceSpace.InverseTransformDirection(
                        foot.sourceIk.Effector.position - foot.sourceIk.Links[0].bone.position);
                    var b = rig.TargetSpace.InverseTransformDirection(
                        foot.targetIk.Effector.position - foot.targetIk.Links[0].bone.position);
                    film.maxFootAxisRestDifference = Mathf.Max(film.maxFootAxisRestDifference, Vector3.Angle(a, b));
                }
                foreach (float frame in frames)
                {
                    sequence.Sample(frame / sequence.FrameRate);
                    poses[frame] = Snapshot(targets);
                    foreach (var foot in rig.Feet)
                    {
                        if (foot.sourceIk == null || foot.targetIk == null) continue;
                        var a = foot.sourceIk; var b = foot.targetIk;
                        var row = new FootRow {
                            name = a.IkName, frame = frame, toe = a.Links.Count == 1,
                            sourceEnabled = a.SolveEnabled, targetEnabled = b.SolveEnabled,
                            sourcePosition = rig.SourceSpace.InverseTransformPoint(a.Effector.position),
                            targetPosition = rig.TargetSpace.InverseTransformPoint(b.Effector.position),
                            sourceGoal = rig.SourceSpace.InverseTransformPoint(a.Target.position),
                            targetGoal = rig.TargetSpace.InverseTransformPoint(b.Target.position),
                            sourceResidual = Vector3.Distance(a.Effector.position, a.Target.position),
                            targetResidual = Vector3.Distance(b.Effector.position, b.Target.position)
                        };
                        if (!row.toe && a.Links.Count == 2 && b.Links.Count == 2)
                        {
                            var ah = a.Links[1].bone; var ak = a.Links[0].bone;
                            var bh = b.Links[1].bone; var bk = b.Links[0].bone;
                            row.sourceHip = rig.SourceSpace.InverseTransformPoint(ah.position);
                            row.targetHip = rig.TargetSpace.InverseTransformPoint(bh.position);
                            row.sourceKnee = Vector3.Angle(ah.position - ak.position, a.Effector.position - ak.position);
                            row.targetKnee = Vector3.Angle(bh.position - bk.position, b.Effector.position - bk.position);
                            row.sourceKneeEuler = ak.localEulerAngles;
                            row.targetKneeEuler = bk.localEulerAngles;
                            row.sourceReachRatio = Vector3.Distance(ah.position, a.Target.position) /
                                (Vector3.Distance(ah.position, ak.position) + Vector3.Distance(ak.position, a.Effector.position));
                            row.targetReachRatio = Vector3.Distance(bh.position, b.Target.position) /
                                (Vector3.Distance(bh.position, bk.position) + Vector3.Distance(bk.position, b.Effector.position));
                            if (row.sourceResidual > film.maxSourceReachResidual)
                            { film.maxSourceReachResidual = row.sourceResidual; film.sourceReachFrame = Mathf.RoundToInt(frame); }
                            film.maxTargetReachResidual = Mathf.Max(film.maxTargetReachResidual, row.targetResidual);
                            float difference = Mathf.Abs(row.sourceKnee - row.targetKnee);
                            if (difference > film.maxKneeDifference)
                            { film.maxKneeDifference = difference; film.kneeDifferenceFrame = Mathf.RoundToInt(frame); }
                        }
                        if (row.toe)
                        {
                            row.sourceAnkleRotation = a.Links[0].bone.rotation;
                            row.targetAnkleRotation = b.Links[0].bone.rotation;
                            row.sourceFootAxis = rig.SourceSpace.InverseTransformDirection(a.Effector.position - a.Links[0].bone.position);
                            row.targetFootAxis = rig.TargetSpace.InverseTransformDirection(b.Effector.position - b.Links[0].bone.position);
                            if (row.sourceEnabled && !row.targetEnabled) film.targetToeDisabledSamples++;
                            if (row.targetResidual > film.maxStaleToeTargetError)
                            { film.maxStaleToeTargetError = row.targetResidual; film.staleToeFrame = Mathf.RoundToInt(frame); }
                        }
                        rows.Add(row);
                    }
                }
                // Jump between distant points, then revisit each recorded pose.
                // Missing FK tracks must not retain an old IK-solved rotation.
                var seekErrors = new List<SeekRow>();
                foreach (float frame in frames.Where((f, i) => i % 7 == 0).Reverse())
                {
                    sequence.Sample(0);
                    sequence.Sample(last / sequence.FrameRate);
                    sequence.Sample(frame / sequence.FrameRate);
                    var now = Snapshot(targets); var before = poses[frame];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        float position = Vector3.Distance(now.positions[i], before.positions[i]);
                        float angle = Quaternion.Angle(now.rotations[i], before.rotations[i]);
                        if (position > film.maxSeekPositionError || angle > film.maxSeekRotationError)
                        {
                            film.seekErrorFrame = Mathf.RoundToInt(frame);
                            film.seekErrorBone = targets[i].name;
                        }
                        film.maxSeekPositionError = Mathf.Max(film.maxSeekPositionError, position);
                        film.maxSeekRotationError = Mathf.Max(film.maxSeekRotationError, angle);
                        if (position > .001f || angle > .5f)
                            seekErrors.Add(new SeekRow { bone = targets[i].name, frame = frame,
                                positionError = position, rotationError = angle });
                    }
                }
                film.sampledFrames = frames.Length;
                film.feet = rows.ToArray();
                film.seekErrors = seekErrors.ToArray();
            }
            catch (Exception error) { film.error = error.ToString(); }
            finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
            films.Add(film);
        }
        report.films = films.ToArray();
        report.loadedScenesPreserved = active == SceneManager.GetActiveScene() &&
            states.Select((s, i) => s.IsValid() && s.isDirty == dirty[i]).All(s => s) &&
            setup.Select(s => s.path).SequenceEqual(EditorSceneManager.GetSceneManagerSetup().Select(s => s.path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        return output;
    }

    private static Pose Snapshot(Transform[] bones) => new Pose {
        positions = bones.Select(b => b.position).ToArray(),
        rotations = bones.Select(b => b.rotation).ToArray()
    };

    public static string RunSour(string prefab, string motion, string output)
    {
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), preview);
            var player = model.AddComponent<HoVmdMotionPlayer>();
            player.Animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(motion);
            player.ModelRoot = model.transform; player.SkeletonRoot = model.transform.Find("MMD 骨架");
            player.RebuildBindings();
            var map = player.SkeletonRoot.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            var frames = new List<SourFrame>();
            for (int half = 450; half <= 630; half++)
            {
                float frame = half * .5f;
                player.ApplyAtFrame(frame);
                frames.Add(new SourFrame { vmdFrame = frame, bones = new[] { "下半身", "左足", "左ひざ", "左足首",
                    "左つま先", "左足ＩＫ", "左つま先ＩＫ", "右足", "右ひざ", "右足首", "右つま先",
                    "右足ＩＫ", "右つま先ＩＫ" }.Select(n => new SourBone { name = n,
                        position = model.transform.InverseTransformPoint(map[n].position) }).ToArray() });
            }
            File.WriteAllText(output, JsonUtility.ToJson(new SourReport { prefab = prefab, motion = motion, frames = frames.ToArray() }, true));
            return output;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static SolverProbe[] ProbeHingeLimits()
    {
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("single axis hinge regression");
            SceneManager.MoveGameObjectToScene(root, preview);
            var knee = new GameObject("knee").transform; knee.SetParent(root.transform, false);
            var ankle = new GameObject("ankle").transform; ankle.SetParent(knee, false);
            knee.localPosition = ankle.localPosition = Vector3.down;
            var goal = new GameObject("goal").transform; SceneManager.MoveGameObjectToScene(goal.gameObject, preview);
            var ik = goal.gameObject.AddComponent<HoIkConstraint>();
            ik.Effector = ankle; ik.Target = goal; ik.UsePole = false; ik.Mode = HoIkConstraint.UpdateMode.Manual;
            ik.Iterations = 100; ik.Tolerance = .0001f;
            ik.Links.Add(new HoIkConstraint.Link { bone = knee, useAxisMask = true,
                useX = true, useY = false, useZ = false, useLocalEulerLimits = true,
                localEulerMin = new Vector3(.05f, 0, 0), localEulerMax = new Vector3(175, 0, 0) });
            ik.Links.Add(new HoIkConstraint.Link { bone = root.transform });
            var results = new List<SolverProbe>();
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            foreach (int sign in new[] { 1, -1 })
            foreach (float bend in new[] { 45f, 85f, 95f, 120f, 150f })
            {
                Vector3 segment = axis == Vector3.up ? Vector3.forward : Vector3.down;
                knee.localPosition = ankle.localPosition = segment;
                var link = ik.Links[0];
                link.useX = axis == Vector3.right; link.useY = axis == Vector3.up; link.useZ = axis == Vector3.forward;
                link.localEulerMin = axis * (sign > 0 ? .05f : -175f);
                link.localEulerMax = axis * (sign > 0 ? 175f : -.05f);
                root.transform.localRotation = Quaternion.identity;
                knee.localRotation = Quaternion.AngleAxis(5 * sign, axis);
                goal.position = segment + Quaternion.AngleAxis(bend * sign, axis) * segment;
                ik.CaptureBindPose(); ik.SolveNow();
                results.Add(new SolverProbe { axis = axis.ToString(), intendedBend = bend * sign,
                    residual = Vector3.Distance(ankle.position, goal.position), kneeEuler = knee.localEulerAngles });
            }
            return results.ToArray();
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
#endif
