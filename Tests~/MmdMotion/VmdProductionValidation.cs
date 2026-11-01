#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.MmdMotion;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VmdProductionValidation
{
    [Serializable] public sealed class SamplingReport
    {
        public string asset;
        public int checkedKeys, unsortedBoneTracks, unsortedMorphTracks;
        public float maxAngularError, maxPositionError;
        public float rebuildAngularError, rebuildPositionError;
        public bool syntheticMorphOrderAndDuplicates;
    }

    public static string RunSampling(string path, string output)
    {
        var report = new SamplingReport { asset = path };
        var scene = EditorSceneManager.NewPreviewScene();
        Mesh mesh = null;
        HoVmdAnimation synthetic = null;
        try
        {
            var animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(path);
            var root = new GameObject("unordered VMD validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var player = root.AddComponent<HoVmdMotionPlayer>();
            player.Animation = animation; player.ModelRoot = root.transform;
            var tracks = animation.boneFrames.Where(x => !string.IsNullOrEmpty(x.boneName)).GroupBy(x => x.boneName).ToArray();
            var targets = new Dictionary<string, Transform>();
            foreach (var track in tracks)
            {
                var bone = new GameObject(track.Key).transform; bone.SetParent(root.transform, false);
                targets.Add(track.Key, bone);
                player.BoneBindings.Add(new HoVmdMotionPlayer.BoneBinding { vmdName = track.Key, target = bone });
                if (!track.Select(x => x.frame).SequenceEqual(track.Select(x => x.frame).OrderBy(x => x))) ++report.unsortedBoneTracks;
            }
            foreach (var track in animation.morphFrames.GroupBy(x => x.morphName))
                if (!track.Select(x => x.frame).SequenceEqual(track.Select(x => x.frame).OrderBy(x => x))) ++report.unsortedMorphTracks;
            player.RebuildBindings();
            foreach (var track in tracks.Where(x => x.Count() > 1))
            {
                var keys = track.GroupBy(x => x.frame).Select(x => x.Last()).OrderBy(x => x.frame).ToArray();
                foreach (int index in Enumerable.Range(0, 5).Select(x => x * (keys.Length - 1) / 4).Distinct())
                {
                    var key = keys[index];
                    player.ApplyAtFrame(key.frame);
                    Transform target = targets[key.boneName];
                    float angle = Quaternion.Angle(target.localRotation, key.rotation);
                    float position = Vector3.Distance(target.localPosition, new Vector3(-key.position.x, key.position.y, -key.position.z) * .08f);
                    report.maxAngularError = Mathf.Max(report.maxAngularError, angle);
                    report.maxPositionError = Mathf.Max(report.maxPositionError, position);
                    ++report.checkedKeys;
                    if (angle > .1f || position > .0001f)
                        throw new InvalidOperationException("Exact source key mismatch: " + path + " / " + key.boneName + " / " + key.frame +
                            " angular=" + angle + " position=" + position);
                }
            }
            // The recorder and inspector can rebuild mappings after the first
            // sample. That must never redefine the animated pose as bind pose.
            player.ApplyAtFrame(0);
            var sampledRotations = targets.ToDictionary(x => x.Key, x => x.Value.localRotation);
            var sampledPositions = targets.ToDictionary(x => x.Key, x => x.Value.localPosition);
            player.RebuildBindings();
            player.ApplyAtFrame(0);
            foreach (var target in targets)
            {
                report.rebuildAngularError = Mathf.Max(report.rebuildAngularError,
                    Quaternion.Angle(sampledRotations[target.Key], target.Value.localRotation));
                report.rebuildPositionError = Mathf.Max(report.rebuildPositionError,
                    Vector3.Distance(sampledPositions[target.Key], target.Value.localPosition));
            }
            if (report.rebuildAngularError > .1f || report.rebuildPositionError > .0001f)
                throw new InvalidOperationException("Rebuilding bindings recaptured an animated rest pose: angular=" +
                    report.rebuildAngularError + ", position=" + report.rebuildPositionError);
            synthetic = ScriptableObject.CreateInstance<HoVmdAnimation>();
            synthetic.morphFrames.AddRange(new[] {
                new HoVmdMorphFrame { morphName = "test", frame = 30, weight = .3f },
                new HoVmdMorphFrame { morphName = "test", frame = 0, weight = 0 },
                new HoVmdMorphFrame { morphName = "test", frame = 20, weight = .2f },
                new HoVmdMorphFrame { morphName = "test", frame = 10, weight = .1f },
                new HoVmdMorphFrame { morphName = "test", frame = 20, weight = .8f } });
            var morphObject = new GameObject("morph validation");
            morphObject.transform.SetParent(root.transform, false);
            var renderer = morphObject.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero };
            mesh.AddBlendShapeFrame("test", 100, new[] { Vector3.up }, null, null); renderer.sharedMesh = mesh;
            var morphPlayer = morphObject.AddComponent<HoVmdMotionPlayer>();
            morphPlayer.Animation = synthetic; morphPlayer.ModelRoot = morphObject.transform;
            morphPlayer.RebuildBindings(); morphPlayer.ApplyAtFrame(15);
            if (Mathf.Abs(renderer.GetBlendShapeWeight(0) - 45) > .001f) throw new InvalidOperationException("Unordered morph/duplicate frame interpolation failed.");
            report.syntheticMorphOrderAndDuplicates = true;
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            return output;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (mesh != null) Object.DestroyImmediate(mesh);
            if (synthetic != null) Object.DestroyImmediate(synthetic);
        }
    }

    [Serializable] public sealed class Report
    {
        public List<string> passed = new List<string>();
        public int referenceTransforms, mappedBones;
        public float seekPositionError, seekRotationError;
    }

    public static string Run(string motionPath, string referencePath, string prefabPath, string output)
    {
        var report = new Report();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject cameraObject = null;
        HoVmdAnimation data = null;
        try
        {
            cameraObject = new GameObject("Camera validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            var player = cameraObject.AddComponent<HoVmdCameraPlayer>();
            data = ScriptableObject.CreateInstance<HoVmdAnimation>();
            var linear = new HoVmdBezier(.25f, .25f, .75f, .75f);
            var interpolation = new HoVmdCameraInterpolation { movement = linear, rotation = linear, distance = linear, viewAngle = linear };
            data.cameraFrames.Add(new HoVmdCameraFrame { frame = 0, distance = -10, targetPosition = new Vector3(0, 12, 0),
                viewAngle = 30, interpolation = interpolation });
            player.Animation = data; player.ApplyAtFrame(0);
            Vector3 center = new Vector3(0, .96f, 0);
            Check(Vector3.Dot(camera.transform.forward, (center - camera.transform.position).normalized) > .99999f, "normal signed distance looks at center", report);

            var first = data.cameraFrames[0];
            first.rotation = new Vector3(.2f, .3f, .1f);
            data.cameraFrames[0] = first; player.RebuildTrack(); player.ApplyAtFrame(0);
            Vector3 basis = CameraBasis(Vector3.forward, first.rotation);
            Vector3 expectedForward = new Vector3(basis.x, -basis.y, basis.z);
            basis = CameraBasis(Vector3.up, first.rotation);
            Vector3 expectedUp = new Vector3(-basis.x, basis.y, -basis.z);
            Check(Vector3.Dot(camera.transform.forward, expectedForward) > .99999f &&
                Vector3.Dot(camera.transform.up, expectedUp) > .99999f, "pitch/yaw/roll match MMD view basis", report);

            first.rotation = Vector3.zero; first.targetPosition = Vector3.zero;
            data.cameraFrames[0] = first;
            data.cameraFrames.Add(new HoVmdCameraFrame { frame = 10, distance = -10, targetPosition = Vector3.one * 10,
                viewAngle = 31, interpolation = new HoVmdCameraInterpolation { separatePosition = true,
                    positionX = linear, positionY = new HoVmdBezier(.5f, 0, .5f, 0),
                    positionZ = new HoVmdBezier(.5f, 1, .5f, 1), distance = linear, rotation = linear, viewAngle = linear } });
            var debugCenter = new GameObject("center").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(debugCenter.gameObject, scene);
            player.CameraTarget = debugCenter; player.RebuildTrack(); player.ApplyAtFrame(5);
            Check(Mathf.Abs(camera.fieldOfView - 30.5f) < .0001f, "FOV interpolates without integer stepping", report);
            Check(Vector3.Distance(debugCenter.position, new Vector3(-.4f, .1f, -.7f)) < .0001f, "independent camera XYZ Beziers", report);
            var origin = new GameObject("origin").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(origin.gameObject, scene);
            origin.position = new Vector3(3, 2, -1); origin.rotation = Quaternion.Euler(0, 45, 0); origin.localScale = Vector3.one * 2;
            player.motionOrigin = origin; player.ApplyAtFrame(0);
            Check(Vector3.Distance(camera.transform.position, origin.TransformPoint(new Vector3(0, 0, .8f))) < .0001f,
                "motion origin scales camera radius and center together", report);
            player.motionOrigin = null;
            var cut = data.cameraFrames[1]; cut.frame = 1; data.cameraFrames[1] = cut;
            player.RebuildTrack(); player.ApplyAtFrame(.5f);
            Check(debugCenter.position.sqrMagnitude < .00001f, "adjacent camera keys stay hard cuts at 60fps", report);

            first.distance = -80; first.viewAngle = 20;
            data.cameraFrames[0] = first;
            player.portraitFraming = true; player.RebuildTrack(); player.ApplyAtFrame(0);
            float authoredRadius = Vector3.Distance(camera.transform.position, debugCenter.position);
            Vector3 authoredForward = camera.transform.forward;
            Check(Mathf.Abs(camera.fieldOfView - 20) < .0001f, "zero indoor FOV floor preserves authored lens", report);
            player.minimumPerspectiveFov = 45; player.ApplyAtFrame(0);
            float indoorRadius = Vector3.Distance(camera.transform.position, debugCenter.position);
            float indoorSpan = 2 * indoorRadius * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            Check(indoorRadius < authoredRadius && Mathf.Abs(indoorSpan - player.maximumVerticalSpan) < .0001f,
                "indoor lens floor shortens radius without changing central framing", report);
            Check(Vector3.Dot(authoredForward, camera.transform.forward) > .99999f && debugCenter.position.sqrMagnitude < .00001f,
                "indoor lens floor preserves orbit center and direction", report);
            first.perspectiveOff = true; data.cameraFrames[0] = first;
            player.minimumPerspectiveFov = 0; player.RebuildTrack(); player.ApplyAtFrame(0);
            float orthographicSize = camera.orthographicSize;
            player.minimumPerspectiveFov = 60; player.ApplyAtFrame(0);
            Check(camera.orthographic && Mathf.Abs(camera.orthographicSize - orthographicSize) < .0001f &&
                Mathf.Abs(camera.fieldOfView - 20) < .0001f, "indoor lens floor leaves orthographic shots unchanged", report);

            first.perspectiveOff = false;
            first.rotation = new Vector3(.2f, .3f, .1f);
            data.cameraFrames[0] = first;
            player.portraitFraming = false; player.minimumPerspectiveFov = 0;
            player.motionOrigin = origin; player.RebuildTrack(); player.ApplyAtFrame(0);
            Vector3 authoredCenter = debugCenter.position;
            Vector3 originalCameraPosition = camera.transform.position;
            Quaternion originalCameraRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            var follow = new GameObject("horizontal framing target").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(follow.gameObject, scene);
            follow.position = authoredCenter + camera.transform.right * .45f + camera.transform.up * .2f;
            player.framingFollowTarget = follow; player.framingFollowWeight = Vector2.zero; player.ApplyAtFrame(0);
            Check(Vector3.Distance(camera.transform.position, originalCameraPosition) < .0001f,
                "zero horizontal framing weight preserves authored camera", report);
            player.framingFollowWeight = Vector2.right; player.ApplyAtFrame(0);
            Vector3 adjustment = debugCenter.position - authoredCenter;
            Check(Mathf.Abs(camera.WorldToViewportPoint(follow.position).x - .5f) < .0001f &&
                Mathf.Abs(Vector3.Dot(adjustment, camera.transform.up)) < .0001f &&
                Mathf.Abs(Vector3.Dot(adjustment, camera.transform.forward)) < .0001f &&
                Quaternion.Angle(camera.transform.rotation, originalCameraRotation) < .01f &&
                Mathf.Abs(camera.fieldOfView - originalFov) < .0001f,
                "horizontal framing centers target in rotated scaled origin without vertical or lens changes", report);
            player.framingFollowWeight = new Vector2(.5f, 0); player.ApplyAtFrame(0);
            Check(Mathf.Abs(Vector3.Dot(debugCenter.position - authoredCenter, camera.transform.right) - .225f) < .0001f,
                "horizontal framing supports partial composition weight", report);
            Vector3 expectedPosition = camera.transform.position;
            player.ApplyAtFrame(10); player.ApplyAtFrame(0);
            Check(Vector3.Distance(camera.transform.position, expectedPosition) < .0001f,
                "horizontal framing is deterministic after seeking", report);
            player.framingFollowWeight = Vector2.one;
            player.framingFollowOffset = new Vector2(0, -.1f);
            player.ApplyAtFrame(0);
            Check(Mathf.Abs(Vector3.Dot(debugCenter.position - authoredCenter, camera.transform.right) - .45f) < .0001f &&
                Mathf.Abs(Vector3.Dot(debugCenter.position - authoredCenter, camera.transform.up) - .1f) < .0001f &&
                Quaternion.Angle(camera.transform.rotation, originalCameraRotation) < .01f &&
                Mathf.Abs(camera.fieldOfView - originalFov) < .0001f,
                "vertical framing opt-in applies camera-space offset without changing lens or rotation", report);
            player.framingFollowTarget = null; player.ApplyAtFrame(0);
            Check(Vector3.Distance(camera.transform.position, originalCameraPosition) < .0001f,
                "clearing horizontal target restores original authored camera", report);
            player.motionOrigin = null;

            var character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            character.transform.SetPositionAndRotation(new Vector3(2, 0, 3), Quaternion.Euler(0, 33, 0));
            character.transform.localScale = Vector3.one * 1.25f;
            // Exercise an arbitrary Humanoid without pre-existing Ho IK.
            foreach (var ik in character.GetComponentsInChildren<HoIkConstraint>(true)) Object.DestroyImmediate(ik);
            var root = new GameObject("generic human sequence");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var sequence = root.AddComponent<HoVmdSequencePlayer>();
            sequence.TargetAnimator = character.GetComponent<Animator>();
            sequence.ReferenceRigAsset = AssetDatabase.LoadMainAssetAtPath(referencePath);
            sequence.MotionClip = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(motionPath);
            HoVmdSequenceBuilder.Configure(sequence);
            report.referenceTransforms = sequence.Body.ModelRoot.GetComponentsInChildren<Transform>(true).Length;
            report.mappedBones = sequence.Retargeter.Bones.Count;
            Check(report.referenceTransforms < 80 && report.mappedBones >= 45, "bounded reference rig maps body and fingers", report);
            Check(sequence.Retargeter.Feet.Count == 2 && sequence.Retargeter.Feet.All(x => x.targetIk != null),
                "Humanoid feet receive automatic Ho IK when absent", report);
            var driven = sequence.Retargeter.Bones.Select(x => x.target).Distinct().ToArray();
            var initialPositions = driven.Select(x => x.localPosition).ToArray();
            var initialRotations = driven.Select(x => x.localRotation).ToArray();
            float clipDuration = sequence.Duration;
            sequence.InitialPoseTransitionSeconds = 1;
            sequence.Preroll = 1;
            sequence.SamplePreparation(0);
            Check(driven.Select((x, i) => Vector3.Distance(x.localPosition, initialPositions[i])).Max() < .0001f &&
                driven.Select((x, i) => Quaternion.Angle(x.localRotation, initialRotations[i])).Max() < .05f,
                "preparation starts at the original target pose after solving source IK", report);
            sequence.SamplePreparation(.5f);
            var halfwayPositions = driven.Select(x => x.localPosition).ToArray();
            var halfwayRotations = driven.Select(x => x.localRotation).ToArray();
            sequence.SamplePreparation(1);
            var firstPositions = driven.Select(x => x.localPosition).ToArray();
            var firstRotations = driven.Select(x => x.localRotation).ToArray();
            Check(driven.Select((x, i) => Quaternion.Angle(firstRotations[i], initialRotations[i])).Max() > 1,
                "preparation fixture exercises a nontrivial first pose", report);
            Check(driven.Select((x, i) => Vector3.Distance(halfwayPositions[i],
                    Vector3.Lerp(initialPositions[i], firstPositions[i], .5f))).Max() < .0001f &&
                driven.Select((x, i) => Quaternion.Angle(halfwayRotations[i],
                    Quaternion.Slerp(initialRotations[i], firstRotations[i], .5f))).Max() < .05f,
                "preparation interpolates the solved target hierarchy without a first-frame jump", report);
            sequence.SamplePreparation(2);
            Check(driven.Select((x, i) => Quaternion.Angle(x.localRotation, firstRotations[i])).Max() < .05f &&
                sequence.CurrentTime == 0 && sequence.Duration == clipDuration && sequence.PreparationDuration == 2,
                "settling holds frame zero without extending the motion clock", report);
            sequence.Sample(4);
            var restartRotations = driven.Select(x => x.localRotation).ToArray();
            sequence.Restart();
            sequence.SamplePreparation(0);
            Check(driven.Select((x, i) => Quaternion.Angle(x.localRotation, restartRotations[i])).Max() < .05f,
                "restart transitions from the current pose without recapturing source bind data", report);
            sequence.InitialPoseTransitionSeconds = 0;
            sequence.SamplePreparation(0);
            Check(driven.Select((x, i) => Quaternion.Angle(x.localRotation, firstRotations[i])).Max() < .05f,
                "zero transition preserves legacy immediate frame-zero sampling", report);
            sequence.Sample(0);
            Vector3 position = sequence.Retargeter.TargetHips.position;
            Quaternion rotation = sequence.TargetAnimator.GetBoneTransform(HumanBodyBones.LeftHand).rotation;
            sequence.Sample(4); sequence.Sample(0);
            report.seekPositionError = Vector3.Distance(position, sequence.Retargeter.TargetHips.position);
            report.seekRotationError = Quaternion.Angle(rotation, sequence.TargetAnimator.GetBoneTransform(HumanBodyBones.LeftHand).rotation);
            Check(report.seekPositionError < .0001f && report.seekRotationError < .01f, "rewind does not recapture animated rest pose", report);
            Check(sequence.Body.BindingProfile == null && sequence.Face.BindingProfile == null, "no external mapping profile assets", report);
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            return output;
        }
        finally
        {
            if (data != null) Object.DestroyImmediate(data);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Check(bool condition, string name, Report report)
    {
        if (!condition) throw new InvalidOperationException("VMD production validation failed: " + name);
        report.passed.Add(name);
    }
    static Vector3 CameraBasis(Vector3 value, Vector3 euler)
    {
        float z = euler.z, x = euler.x, y = -(euler.y + Mathf.PI);
        value = new Vector3(value.x * Mathf.Cos(z) - value.y * Mathf.Sin(z), value.x * Mathf.Sin(z) + value.y * Mathf.Cos(z), value.z);
        value = new Vector3(value.x, value.y * Mathf.Cos(x) - value.z * Mathf.Sin(x), value.y * Mathf.Sin(x) + value.z * Mathf.Cos(x));
        return new Vector3(value.x * Mathf.Cos(y) + value.z * Mathf.Sin(y), value.y, -value.x * Mathf.Sin(y) + value.z * Mathf.Cos(y));
    }
}
#endif
