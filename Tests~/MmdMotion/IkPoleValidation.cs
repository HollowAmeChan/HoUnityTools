#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class IkPoleValidation
{
    [Serializable] public sealed class Case { public string name; public float initialError, finalError, maxError, targetDrift, lengthDrift, maxKneeStep, maxKneeRotationStep, maxRootRotationStep, minPoleAlignment, minPoleProjection; public string kneeJumpAt, rootJumpAt, error; }
    [Serializable] public sealed class Report { public List<Case> cases = new List<Case>(); public List<string> failures = new List<string>(); public List<string> configuration = new List<string>(); }
    public static void Run()
    {
        var report = new Report();
        Scene active = SceneManager.GetActiveScene();
        bool wasDirty = active.isDirty;
        var scene = EditorSceneManager.NewPreviewScene();
        const string path = "Assets/Hollow/WTD-summer/WTD-BUILD.prefab";
        string prefabHash = File.Exists(path) ? Hash(path) : null;
        GameObject rig = null, prefab = null;
        try
        {
            rig = new GameObject("IK Pole regression");
            SceneManager.MoveGameObjectToScene(rig, scene);
            Transform root = Child(rig.transform, "Hip", Vector3.zero);
            Transform knee = Child(root, "Knee", new Vector3(0, -1, .15f));
            Transform ankle = Child(knee, "Ankle", new Vector3(0, -1, -.15f));
            Transform goal = Child(rig.transform, "Goal", new Vector3(.3f, -1.65f, .1f));
            Transform pole = Child(rig.transform, "Pole", new Vector3(1, -.8f, 2));
            var solver = rig.AddComponent<HoIkConstraint>();
            solver.Mode = HoIkConstraint.UpdateMode.Manual; solver.Iterations = 80; solver.Tolerance = .0001f;
            solver.Effector = ankle; solver.Target = goal; solver.Pole = pole;
            solver.Links.Add(new HoIkConstraint.Link { bone = knee });
            solver.Links.Add(new HoIkConstraint.Link { bone = root });
            solver.CaptureBindPose();
            Measure(report, "Two-link Pole preserves reachable goal", solver, 120);
            Vector3 direction = Vector3.ProjectOnPlane(knee.position - root.position, ankle.position - root.position).normalized;
            Vector3 expected = Vector3.ProjectOnPlane(pole.position - root.position, ankle.position - root.position).normalized;
            if (Vector3.Dot(direction, expected) < .999f) report.failures.Add("Pole must actually orient the bend plane");
            root.localRotation = Quaternion.identity; knee.localRotation = Quaternion.identity;
            solver.Links[0].useAxisMask = true; solver.Links[0].useX = true; solver.Links[0].useY = solver.Links[0].useZ = false;
            Measure(report, "Hinge knee with Pole stays on goal", solver, 120);
            Quaternion[] poleAxisPose = solver.Links.Select(x => x.bone.localRotation).ToArray();
            SweepPoleAxis(report, solver, root, knee, ankle, goal, pole, null, false);
            SweepPoleAxis(report, solver, root, knee, ankle, goal, pole, poleAxisPose, true);
            Transform originalParent = goal.parent;
            goal.SetParent(knee, true);
            Quaternion rootBefore = root.rotation, kneeBefore = knee.rotation;
            solver.SolveNow();
            if (string.IsNullOrEmpty(solver.LastError) || Quaternion.Angle(rootBefore, root.rotation) > .001f || Quaternion.Angle(kneeBefore, knee.rotation) > .001f)
                report.failures.Add("A goal inside its solved chain must be refused before writing bones");
            goal.SetParent(originalParent, true);
            rig.transform.localRotation = Quaternion.Euler(17, 58, -6);
            rig.transform.localScale = Vector3.one * 1.7f;
            Measure(report, "Rotated and uniformly scaled rig", solver, 120);

            rig.transform.localRotation = Quaternion.identity;
            rig.transform.localScale = Vector3.one;
            root.localRotation = knee.localRotation = Quaternion.identity;
            knee.localPosition = ankle.localPosition = Vector3.down;
            goal.localPosition = new Vector3(0, -1.6f, 0);
            pole.localPosition = new Vector3(0, -1, 2);
            Measure(report, "Straight leg shortens toward Pole", solver, 120);
            root.localRotation = knee.localRotation = Quaternion.identity;
            goal.localPosition = new Vector3(0, -3, 0);
            Measure(report, "Unreachable goal remains stable at full extension", solver, 120, 1.0001f);
            root.localRotation = knee.localRotation = Quaternion.identity;
            goal.localPosition = new Vector3(.3f, -1.6f, .2f);
            pole.localPosition = new Vector3(0, -1, 2);
            solver.Links[0].useLocalEulerLimits = true;
            solver.Links[0].localEulerMin = Vector3.zero;
            solver.Links[0].localEulerMax = new Vector3(150, 0, 0);
            solver.CaptureBindPose();
            Measure(report, "Hinge uses positive-only knee limits", solver, 120);
            root.localRotation = knee.localRotation = Quaternion.identity;
            solver.Links[0].localEulerMin = new Vector3(-150, 0, 0);
            solver.Links[0].localEulerMax = Vector3.zero;
            solver.CaptureBindPose();
            Measure(report, "Hinge uses negative-only knee limits", solver, 120);
            solver.Weight = .5f;
            root.localRotation = knee.localRotation = Quaternion.identity;
            solver.CaptureBindPose();
            for (int warmup = 0; warmup < 40; warmup++) solver.SolveNow();
            Measure(report, "Partial-weight hinge converges without branch flip", solver, 120);
            solver.Weight = 1;

            if (File.Exists(path))
            {
                prefab = PrefabUtility.LoadPrefabContents(path);
                var main = prefab.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Armature-Swimsuit");
                foreach (var foot in prefab.GetComponentsInChildren<HoIkConstraint>(true).Where(x => x.Effector != null && x.Effector.IsChildOf(main) && x.Links.Count == 2))
                {
                    foot.Mode = HoIkConstraint.UpdateMode.Manual;
                    var rotations = foot.Links.Select(x => x.bone.localRotation).ToArray();
                    var originalGoal = foot.Target.position;
                    report.configuration.Add(Describe(foot));
                    foot.Target.position = foot.Effector.position + new Vector3(.04f, .12f, .07f);
                    Measure(report, "WTD " + foot.IkName + " moving foot", foot, 120);
                    for (int i = 0; i < rotations.Length; i++) foot.Links[i].bone.localRotation = rotations[i];
                    foot.Target.position = originalGoal;
                    Trajectory(report, foot, rotations, 240, false);
                    Trajectory(report, foot, rotations, 240, true);
                    for (int i = 0; i < rotations.Length; i++) foot.Links[i].bone.localRotation = rotations[i];
                    foot.Target.position = originalGoal;
                    SweepWtd(report, foot, rotations);
                    EnvelopeWtd(report, foot, rotations, false);
                    EnvelopeWtd(report, foot, rotations, true);
                    for (int i = 0; i < rotations.Length; i++) foot.Links[i].bone.localRotation = rotations[i];
                    foot.Target.position = originalGoal;
                }
                foreach (GameObject sceneRoot in active.GetRootGameObjects())
                    foreach (var constraint in sceneRoot.GetComponentsInChildren<HoIkConstraint>(true))
                        report.configuration.Add("Scene: " + Describe(constraint));
                foreach (var toe in prefab.GetComponentsInChildren<HoIkConstraint>(true).Where(x => x.Effector != null && x.Effector.IsChildOf(main) && x.Links.Count == 1))
                {
                    toe.Mode = HoIkConstraint.UpdateMode.Manual;
                    Transform bone = toe.Links[0].bone;
                    Quaternion originalRotation = bone.localRotation;
                    Vector3 originalGoal = toe.Target.position;
                    toe.Target.position = bone.position + Quaternion.AngleAxis(15, bone.right) * (toe.Effector.position - bone.position);
                    Measure(report, "WTD " + toe.IkName + " one-link toe", toe, 120);
                    bone.localRotation = originalRotation;
                    toe.Target.position = originalGoal;
                }
            }
        }
        catch (Exception error) { report.failures.Add(error.ToString()); Debug.LogException(error); }
        finally
        {
            if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab);
            if (rig != null) Object.DestroyImmediate(rig);
            EditorSceneManager.ClosePreviewScene(scene);
            if (SceneManager.GetActiveScene() != active || active.isDirty != wasDirty)
                report.failures.Add("Existing scene and dirty state must be preserved");
            if (prefabHash != null && Hash(path) != prefabHash)
                report.failures.Add("WTD prefab bytes must remain unchanged");
            Directory.CreateDirectory("Library/HoMmdStages");
            File.WriteAllText("Library/HoMmdStages/ik-pole-validation.json", JsonUtility.ToJson(report, true));
        }
        Debug.Log("HO_IK_POLE_VALIDATION cases=" + report.cases.Count + " failures=" + report.failures.Count);
    }
    private static void Measure(Report report, string name, HoIkConstraint c, int count, float maximumError = .003f)
    {
        var result = new Case { name = name, initialError = Vector3.Distance(c.Effector.position, c.Target.position) };
        Vector3 target = c.Target.position;
        float[] lengths = c.Links.Select((x, i) => Vector3.Distance(x.bone.position, i == 0 ? c.Effector.position : c.Links[i - 1].bone.position)).ToArray();
        for (int j = 0; j < count; j++)
        {
            c.SolveNow();
            CheckFinite(report, c, name);
            result.finalError = Vector3.Distance(c.Effector.position, c.Target.position);
            result.maxError = Mathf.Max(result.maxError, result.finalError);
            result.targetDrift = Mathf.Max(result.targetDrift, Vector3.Distance(target, c.Target.position));
            for (int i = 0; i < lengths.Length; i++) result.lengthDrift = Mathf.Max(result.lengthDrift, Mathf.Abs(lengths[i] - Vector3.Distance(c.Links[i].bone.position, i == 0 ? c.Effector.position : c.Links[i - 1].bone.position)));
            foreach (var link in c.Links.Where(x => x.useLocalEulerLimits))
                if (!Within(link, link.bone.localEulerAngles))
                    report.failures.Add(name + ": local Euler limit exceeded");
        }
        result.error = c.LastError; report.cases.Add(result);
        if (result.maxError > maximumError || result.targetDrift > .00001f || result.lengthDrift > .0001f || !string.IsNullOrEmpty(result.error)) report.failures.Add(name);
    }
    private static void Trajectory(Report report, HoIkConstraint c, Quaternion[] pose, int count, bool restoreInput)
    {
        var result = new Case { name = "WTD " + c.IkName + (restoreInput ? " with upstream animation pose" : " continuous moving targets") };
        Vector3 start = c.Effector.position;
        float[] lengths = c.Links.Select((x, i) => Vector3.Distance(x.bone.position, i == 0 ? c.Effector.position : c.Links[i - 1].bone.position)).ToArray();
        for (int j = 0; j < count; j++)
        {
            if (restoreInput) for (int i = 0; i < pose.Length; i++) c.Links[i].bone.localRotation = pose[i];
            float angle = j * Mathf.PI * 2 / count;
            Vector3 target = start + new Vector3(.035f * Mathf.Sin(angle), .12f + .025f * Mathf.Cos(angle), .07f + .02f * Mathf.Sin(angle * 2));
            c.Target.position = target;
            c.SolveNow();
            CheckFinite(report, c, result.name);
            result.finalError = Vector3.Distance(c.Effector.position, target);
            result.maxError = Mathf.Max(result.maxError, result.finalError);
            result.targetDrift = Mathf.Max(result.targetDrift, Vector3.Distance(target, c.Target.position));
            for (int i = 0; i < lengths.Length; i++) result.lengthDrift = Mathf.Max(result.lengthDrift, Mathf.Abs(lengths[i] - Vector3.Distance(c.Links[i].bone.position, i == 0 ? c.Effector.position : c.Links[i - 1].bone.position)));
        }
        result.error = c.LastError; report.cases.Add(result);
        if (result.maxError > .003f || result.targetDrift > .00001f || result.lengthDrift > .0001f || !string.IsNullOrEmpty(result.error)) report.failures.Add(result.name);
    }
    private static void CheckFinite(Report report, HoIkConstraint c, string name)
    {
        foreach (Transform t in c.Links.Select(x => x.bone).Concat(new[] { c.Effector, c.Target }))
        {
            Vector3 p = t.position;
            Quaternion q = t.rotation;
            if (new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w }.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new InvalidOperationException(name + ": non-finite transform");
        }
    }
    private static bool Within(HoIkConstraint.Link link, Vector3 euler)
    {
        euler = new Vector3(Normalize(euler.x), Normalize(euler.y), Normalize(euler.z));
        return euler.x >= link.localEulerMin.x - .001f && euler.x <= link.localEulerMax.x + .001f &&
            euler.y >= link.localEulerMin.y - .001f && euler.y <= link.localEulerMax.y + .001f &&
            euler.z >= link.localEulerMin.z - .001f && euler.z <= link.localEulerMax.z + .001f;
    }
    private static float Normalize(float angle)
    {
        angle %= 360.0f;
        return angle > 180.0f ? angle - 360.0f : angle;
    }
    private static void SweepWtd(Report report, HoIkConstraint c, Quaternion[] pose)
    {
        var result = new Case { name = "WTD " + c.IkName + " backward/outward sweep", minPoleAlignment = 1.0f, minPoleProjection = 1.0f };
        Transform joint = c.Links[0].bone;
        Transform root = c.Links[1].bone;
        for (int i = 0; i < pose.Length; i++) c.Links[i].bone.localRotation = pose[i];
        Vector3 start = c.Effector.position + Vector3.up * .2f;
        float upper = Vector3.Distance(joint.position, root.position);
        float lower = Vector3.Distance(c.Effector.position, joint.position);
        Vector3 outward = root.parent.right * Mathf.Sign(Vector3.Dot(root.position - root.parent.position, root.parent.right));
        Vector3 backward = -root.parent.forward;
        Vector3[,] previousRows = new Vector3[2, 17];
        Vector3[,] previousGoals = new Vector3[2, 17];
        bool[,] previousValid = new bool[2, 17];
        for (int row = 0; row < 17; row++)
        {
            for (int column = 0; column < 17; column++)
            {
                for (int i = 0; i < pose.Length; i++) c.Links[i].bone.localRotation = pose[i];
                Vector3 goal = start + backward * (row * 0.02f) + outward * (column * 0.02f);
                float distance = Vector3.Distance(root.position, goal);
                if (distance < Mathf.Abs(upper - lower) + 0.005f || distance > upper + lower - 0.005f)
                    continue;
                c.Target.position = goal;
                c.SolveNow();
                CheckFinite(report, c, result.name);
                result.finalError = Vector3.Distance(c.Effector.position, goal);
                result.maxError = Mathf.Max(result.maxError, result.finalError);
                Vector3 knee = joint.position;
                if (column > 0 && previousValid[0, column - 1])
                    RecordKneeStep(result, knee, previousRows[0, column - 1], goal, previousGoals[0, column - 1], row, column, "outward");
                if (row > 0 && previousValid[1, column])
                    RecordKneeStep(result, knee, previousRows[1, column], goal, previousGoals[1, column], row, column, "backward");
                previousRows[0, column] = knee;
                previousGoals[0, column] = goal;
                previousValid[0, column] = true;
                Vector3 axis = c.Effector.position - root.position;
                Vector3 bend = Vector3.ProjectOnPlane(knee - root.position, axis);
                Vector3 poleOffset = c.Pole.position - root.position;
                Vector3 pole = Vector3.ProjectOnPlane(poleOffset, axis);
                if (poleOffset.sqrMagnitude > 1e-8f)
                    result.minPoleProjection = Mathf.Min(result.minPoleProjection, pole.magnitude / poleOffset.magnitude);
                if (bend.sqrMagnitude > 1e-8f && pole.sqrMagnitude > 1e-8f)
                    result.minPoleAlignment = Mathf.Min(result.minPoleAlignment, Vector3.Dot(bend.normalized, pole.normalized));
            }
            for (int column = 0; column < 17; column++)
            {
                previousRows[1, column] = previousRows[0, column];
                previousGoals[1, column] = previousGoals[0, column];
                previousValid[1, column] = previousValid[0, column];
                previousValid[0, column] = false;
            }
        }
        result.error = c.LastError;
        report.cases.Add(result);
        if (result.maxError > .003f || result.maxKneeStep > .15f || result.minPoleAlignment < .98f || !string.IsNullOrEmpty(result.error))
            report.failures.Add(result.name);
    }
    private static void RecordKneeStep(Case result, Vector3 currentKnee, Vector3 previousKnee, Vector3 goal, Vector3 previousGoal, int row, int column, string axis)
    {
        float distance = Vector3.Distance(currentKnee, previousKnee);
        if (distance <= result.maxKneeStep) return;
        result.maxKneeStep = distance;
        result.kneeJumpAt = axis + " row=" + row + " column=" + column + " goal=" + goal.ToString("F3") +
            " previous=" + previousGoal.ToString("F3") + " kneeDelta=" + (currentKnee - previousKnee).ToString("F3");
    }
    private static void SweepPoleAxis(Report report, HoIkConstraint c, Transform root, Transform joint, Transform effector, Transform goal, Transform pole, Quaternion[] inputPose, bool restoreInput)
    {
        root.localRotation = joint.localRotation = Quaternion.identity;
        pole.position = root.position + new Vector3(.3f, -.8f, 1.8f);
        c.CaptureBindPose();
        Vector3 poleDirection = (pole.position - root.position).normalized;
        Vector3 axis = Vector3.Cross(poleDirection, Vector3.right).normalized;
        var result = new Case { name = "Two-link target crosses the Pole axis" + (restoreInput ? " with FK reset" : string.Empty) };
        goal.position = root.position + Quaternion.AngleAxis(-18, axis) * poleDirection * 1.65f;
        for (int warmup = 0; warmup < 30; warmup++) c.SolveNow();
        Quaternion previousRoot = root.rotation, previousKnee = joint.localRotation;
        for (int frame = 1; frame <= 120; frame++)
        {
            if (restoreInput)
                for (int i = 0; i < inputPose.Length; i++) c.Links[i].bone.localRotation = inputPose[i];
            float angle = Mathf.Lerp(-18, 18, frame / 120f);
            Vector3 direction = Quaternion.AngleAxis(angle, axis) * poleDirection;
            goal.position = root.position + direction * 1.65f;
            c.SolveNow();
            CheckFinite(report, c, result.name);
            float error = Vector3.Distance(effector.position, goal.position);
            if (error > result.maxError)
            {
                result.maxError = error;
                result.kneeJumpAt = "frame=" + frame + " angle=" + angle.ToString("F2") +
                    " rootEuler=" + root.eulerAngles.ToString("F2") + " kneeEuler=" + joint.eulerAngles.ToString("F2");
            }
            result.finalError = error;
            float rootStep = Quaternion.Angle(previousRoot, root.rotation);
            float kneeStep = Quaternion.Angle(previousKnee, joint.localRotation);
            if (rootStep > result.maxRootRotationStep)
            {
                result.maxRootRotationStep = rootStep;
                result.rootJumpAt = "frame=" + frame + " angle=" + angle.ToString("F2") + " goal=" + goal.position.ToString("F3") +
                    " foot=" + effector.position.ToString("F3") + " root=" + root.eulerAngles.ToString("F2") + " knee=" + joint.eulerAngles.ToString("F2");
            }
            result.maxKneeRotationStep = Mathf.Max(result.maxKneeRotationStep, kneeStep);
            previousRoot = root.rotation;
            previousKnee = joint.localRotation;
        }
        result.error = c.LastError;
        report.cases.Add(result);
        if (result.maxError > .003f || result.maxRootRotationStep > c.PoleMaxStepAngle + 1.0f || result.maxKneeRotationStep > 15.0f ||
            !string.IsNullOrEmpty(result.error))
            report.failures.Add(result.name);
    }
    private static void EnvelopeWtd(Report report, HoIkConstraint c, Quaternion[] pose, bool restoreInput)
    {
        for (int i = 0; i < pose.Length; i++) c.Links[i].bone.localRotation = pose[i];
        Transform joint = c.Links[0].bone, root = c.Links[1].bone;
        Vector3 start = c.Effector.position;
        float upper = Vector3.Distance(root.position, joint.position);
        float lower = Vector3.Distance(joint.position, start);
        float length = upper + lower;
        Vector3 up = root.parent.up, back = -root.parent.forward;
        Vector3 outward = root.parent.right * Mathf.Sign(Vector3.Dot(root.position - root.parent.position, root.parent.right));
        Vector3[] offsets = {
            Vector3.zero, up * .2f + back * .25f,
            up * .4f + back * .7f + outward * .35f,
            up * .1f + outward * 1.2f,
            up * -.3f + back * .8f + outward * .4f,
            up * .85f + back * .1f,
            Vector3.zero
        };
        var result = new Case { name = "WTD " + c.IkName + (restoreInput ? " full reach with FK reset" : " full reach continuous") };
        Vector3 previousKnee = joint.position;
        Quaternion previousKneeRotation = joint.localRotation, previousRootRotation = root.rotation;
        for (int segment = 0; segment < offsets.Length - 1; segment++)
        {
            for (int frame = 0; frame <= 120; frame++)
            {
                if (restoreInput) for (int i = 0; i < pose.Length; i++) c.Links[i].bone.localRotation = pose[i];
                Vector3 goal = start + Vector3.Lerp(offsets[segment], offsets[segment + 1], frame / 120f) * length;
                c.Target.position = goal;
                c.SolveNow();
                CheckFinite(report, c, result.name);
                Vector3 toGoal = goal - root.position;
                float reach = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(upper - lower), length);
                Vector3 reachable = root.position + toGoal.normalized * reach;
                float error = Vector3.Distance(c.Effector.position, reachable);
                if (error > result.maxError)
                {
                    result.maxError = error;
                    result.kneeJumpAt = "segment=" + segment + " frame=" + frame + " goal=" + goal.ToString("F3") + " kneeEuler=" + joint.localEulerAngles.ToString("F2");
                }
                result.finalError = error;
                result.maxKneeStep = Mathf.Max(result.maxKneeStep, Vector3.Distance(joint.position, previousKnee));
                result.maxKneeRotationStep = Mathf.Max(result.maxKneeRotationStep, Quaternion.Angle(joint.localRotation, previousKneeRotation));
                result.maxRootRotationStep = Mathf.Max(result.maxRootRotationStep, Quaternion.Angle(root.rotation, previousRootRotation));
                result.targetDrift = Mathf.Max(result.targetDrift, Vector3.Distance(c.Target.position, goal));
                result.lengthDrift = Mathf.Max(result.lengthDrift, Mathf.Abs(Vector3.Distance(joint.position, root.position) - upper),
                    Mathf.Abs(Vector3.Distance(c.Effector.position, joint.position) - lower));
                previousKnee = joint.position;
                previousKneeRotation = joint.localRotation;
                previousRootRotation = root.rotation;
            }
        }
        result.error = c.LastError;
        report.cases.Add(result);
        if (result.maxError > .003f || result.maxKneeStep > .05f || result.maxKneeRotationStep > 35f || result.maxRootRotationStep > 35f ||
            result.targetDrift > .00001f || result.lengthDrift > .0001f || !string.IsNullOrEmpty(result.error))
            report.failures.Add(result.name);
    }
    private static string Describe(HoIkConstraint c)
    {
        return c.IkName + " effector=" + (c.Effector != null ? c.Effector.name + " pos=" + c.Effector.position.ToString("F3") : "none") +
            " target=" + (c.Target != null ? c.Target.position.ToString("F3") : "none") + " pole=" + (c.Pole != null ? c.Pole.position.ToString("F3") : "none") +
            " root=" + c.Links.LastOrDefault()?.bone?.position.ToString("F3") +
            " links=" + string.Join("; ", c.Links.Select(x => x.bone.name + " rot=" + x.bone.localEulerAngles.ToString("F2") +
                " mask=" + x.useAxisMask + "/" + x.useX + x.useY + x.useZ + " limits=" + x.useLocalEulerLimits)) +
            " iter=" + c.Iterations + " mode=" + c.Mode + " weight=" + c.Weight;
    }
    private static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    private static Transform Child(Transform parent, string name, Vector3 position) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
}
#endif
