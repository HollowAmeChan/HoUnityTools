// Runs the shipping parser, receiver preprocessing/calibration, JSON evaluator,
// range mapper and LookAt against real Unity quaternion/Transform implementations.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.FaceTracking;
using HoFaceTracking.Core;
using UnityEditor;
using UnityEngine;
using Packet = Hollow.HoUnityTools.FaceTracking.HoVtsPacket;

public static class HoVtsMirrorValidation
{
    private static int assertions;
    private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo Apply = typeof(HoFaceInputState).GetMethod("Apply", PrivateStatic);
    private static Dictionary<string, float> Offsets =>
        (Dictionary<string, float>)typeof(HoFaceInputState).GetField("CalibrationOffsets", PrivateStatic).GetValue(null);

    public static void RunBatch()
    {
        try
        {
            string[] files = Directory.GetFiles(Path.Combine(Application.dataPath, "Profiles"), "*.json");
            Check(files.Length == 2, "template and actual character profiles are present");
            foreach (string file in files)
            {
                var profile = new TextAsset(File.ReadAllText(file));
                profile.name = Path.GetFileName(file);
                foreach (bool mirror in new[] { false, true })
                {
                    CheckPose(profile, new Vector3(30, 0, 0), mirror, false);
                    CheckPose(profile, new Vector3(-30, 0, 0), mirror, false);
                    CheckPose(profile, new Vector3(0, 20, 0), mirror, false);
                    CheckPose(profile, new Vector3(0, -20, 0), mirror, false);
                    CheckPose(profile, new Vector3(0, 0, 15), mirror, false);
                    CheckPose(profile, new Vector3(0, 0, -15), mirror, false);
                    CheckPose(profile, new Vector3(27, -18, 9), mirror, false);
                    CheckPose(profile, new Vector3(30, 0, 0), mirror, true);
                    CheckPose(profile, new Vector3(0, -20, 0), mirror, true);
                    CheckPose(profile, new Vector3(0, 0, 15), mirror, true);
                    CheckPose(profile, new Vector3(0, 0, -15), mirror, true);
                    CheckPose(profile, new Vector3(27, -18, 9), mirror, true);
                }
                UnityEngine.Object.DestroyImmediate(profile);
            }
            CheckZeroNeutralAxes();
            CheckLiveCalibrationToggle();
            CheckPreprocessing();
            Debug.Log("HO_VTS_MIRROR PASS assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static Dictionary<string, float> Frame(Vector3 head)
    {
        string json = "{\"FaceFound\":true,\"Rotation\":{\"x\":" + N(head.x)
            + ",\"y\":" + N(head.y) + ",\"z\":" + N(head.z)
            + "},\"Position\":{\"x\":2,\"y\":3,\"z\":4},"
            + "\"EyeLeft\":{\"x\":-11,\"y\":7,\"z\":2},"
            + "\"EyeRight\":{\"x\":5,\"y\":-3,\"z\":-1},"
            + "\"BlendShapes\":[{\"k\":\"EyeBlinkLeft\",\"v\":0.2},{\"k\":\"EyeBlinkRight\",\"v\":0.8}]}";
        Check(Packet.TryParse(json, out var frame, out bool tracked, out string error) && tracked, "package parser: " + error);
        Check(HoFaceTracking.Core.HoVtsPacket.TryParse(json, out var modFrame, out tracked, out error) && tracked, "mod parser: " + error);
        foreach (var pair in frame) Near(modFrame[pair.Key], pair.Value, "same parser field " + pair.Key);
        Near(frame["Rotation_x"], head.x, "packet preserves x");
        Near(frame["Rotation_y"], head.y, "packet preserves y");
        return frame;
    }

    private static string N(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Reset()
    {
        HoFaceInputState.ResetInputOptions();
        Offsets.Clear();
        typeof(HoFaceInputState).GetField("calibrationActive", PrivateStatic).SetValue(null, false);
    }

    private static Dictionary<string, float> Receive(Dictionary<string, float> frame)
    {
        Apply.Invoke(null, new object[] { frame, true });
        return HoFaceInputState.Snapshot();
    }

    private static void CaptureNeutral(Dictionary<string, float> frame)
    {
        HoFaceInputState.BeginCalibration(.001f);
        typeof(HoFaceInputState).GetField("calibrationStartedAt", PrivateStatic)
            .SetValue(null, (double)Time.realtimeSinceStartup - 1);
        Receive(frame);
        Check(!HoFaceInputState.CalibrationActive && Offsets.Count == 12, "neutral captured by production receiver");
    }

    private static void CheckPose(TextAsset profile, Vector3 head, bool mirror, bool calibrated)
    {
        Reset();
        HoFaceInputState.SetInputOptions(mirror, false);
        var neutral = Frame(calibrated ? new Vector3(8, -6, 3) : Vector3.zero);
        if (calibrated) CaptureNeutral(neutral);
        var processedInput = Receive(Frame(head));
        var editorInput = new Dictionary<string, float>(processedInput);
        HoFaceInputState.ApplyPoseCalibration(processedInput);
        HoEditorCalibrationProbe.SetOffsets(Offsets);
        HoEditorCalibrationProbe.Apply(editorInput);
        foreach (string key in new[] { "Rotation_x", "Rotation_y", "Rotation_z", "EyeLeft_x", "EyeLeft_y", "EyeLeft_z" })
            Near(editorInput[key], processedInput[key], "Unity/Mod calibration parity " + key);
        foreach (string key in new[] { "Rotation_x", "Rotation_y", "Rotation_z" })
            processedInput[key] = HoFaceVtsRamp.Map(processedInput[key], 0, 1, 0, 1, 1, false, false);

        var root = new GameObject("MirrorProbe");
        try
        {
            root.transform.rotation = Quaternion.Euler(9, 37, -11);
            var receiver = root.AddComponent<HoVtsParameterReceiver>();
            receiver.SetProfile(profile);
            receiver.ApplyRawFrame(processedInput, true, 1, "mirror-validation");
            Check(string.IsNullOrEmpty(receiver.RuleError), "profile parses: " + receiver.RuleError);
            foreach (string key in new[] { "Rotation_x", "Rotation_y", "Rotation_z", "EyeLeft_x", "EyeLeft_y", "EyeLeft_z" })
            {
                float expected = key == "Rotation_x" || key == "Rotation_z" ? -processedInput[key] : processedInput[key];
                Near(receiver.Get(key), expected, "profile aligns head with ARKit while preserving other pose axes " + key);
            }
            var bone = new GameObject("Head").transform;
            bone.SetParent(root.transform, false);
            Quaternion rest = Quaternion.Euler(5, 3, 2);
            bone.localRotation = rest;
            var eye = new GameObject("Eye").transform;
            eye.SetParent(bone, false);
            Quaternion eyeRest = Quaternion.Euler(-2, 4, 1);
            eye.localRotation = eyeRest;
            var look = root.AddComponent<HoVtsLookAt>();
            look.AutoRun = false;
            look.ParameterReceiver = receiver;
            look.ReferenceFrame = root.transform;
            look.Head = bone;
            look.LeftEye = eye;
            Check(look.EvaluateNow(0), "LookAt consumes processed pose: " + look.Status);

            // The profile aligns the device's horizontal head convention with
            // ARKit by reversing yaw/roll. The user mirror toggles that whole
            // convention again before the profile; pitch and eyes keep theirs.
            float yaw = mirror ? head.x : -head.x;
            float roll = mirror ? head.z : -head.z;
            Quaternion target = Quaternion.Euler(head.y, yaw, roll);
            if (calibrated)
                target *= Quaternion.Inverse(Quaternion.Euler(-6, mirror ? 8 : -8, mirror ? 3 : -3));
            RotationNear(bone.rotation, root.transform.rotation * target * rest,
                profile.name + " head mirror=" + mirror + " calibrated=" + calibrated + " input=" + head);
            float ex = mirror ? -5 : -11;
            float ey = mirror ? -3 : 7;
            float ez = mirror ? -1 : 2;
            Quaternion eyeTarget = calibrated ? Quaternion.identity : Quaternion.Euler(-ey, ex, ez);
            RotationNear(eye.rotation,
                root.transform.rotation * eyeTarget * Quaternion.Inverse(root.transform.rotation) * bone.rotation * eyeRest,
                "mirrored eye fields keep horizontal/vertical axes after calibration");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckZeroNeutralAxes()
    {
        Reset();
        CaptureNeutral(Frame(Vector3.zero));
        var yaw = Receive(Frame(new Vector3(30, 0, 0)));
        HoFaceInputState.ApplyPoseCalibration(yaw);
        Near(yaw["Rotation_x"], 30, "calibration keeps horizontal turn in x");
        Near(yaw["Rotation_y"], 0, "calibrated turn does not become a nod");
        var pitch = Receive(Frame(new Vector3(0, -20, 0)));
        HoFaceInputState.ApplyPoseCalibration(pitch);
        Near(pitch["Rotation_x"], 0, "calibrated nod does not become a turn");
        Near(pitch["Rotation_y"], -20, "calibration keeps vertical nod in y");
    }

    private static void CheckLiveCalibrationToggle()
    {
        Reset();
        var neutral = Frame(new Vector3(8, -6, 3));
        CaptureNeutral(neutral);
        foreach (bool mirror in new[] { true, false, true })
        {
            HoFaceInputState.SetInputOptions(mirror, false);
            var input = Receive(neutral);
            HoFaceInputState.ApplyPoseCalibration(input);
            foreach (string key in new[] { "Rotation_x", "Rotation_y", "Rotation_z", "EyeLeft_x", "EyeLeft_y", "EyeLeft_z" })
                Near(input[key], 0, "live mirror keeps calibrated neutral " + key);
        }
        Reset();
        HoFaceInputState.BeginCalibration(3);
        Receive(neutral); // First sample in the unmirrored space.
        HoFaceInputState.SetInputOptions(true, false);
        typeof(HoFaceInputState).GetField("calibrationStartedAt", PrivateStatic)
            .SetValue(null, (double)Time.realtimeSinceStartup - 4);
        var final = Receive(neutral);
        HoFaceInputState.ApplyPoseCalibration(final);
        Near(final["Rotation_x"], 0, "mirror during calibration transforms accumulated samples");
    }

    private static void CheckPreprocessing()
    {
        Reset();
        HoFaceInputState.SetInputOptions(true, true, 2);
        var raw = Frame(new Vector3(30, -20, 7));
        raw["FaceAngleX"] = 30;
        raw["FaceAngleY"] = -20;
        raw["FaceAngleZ"] = 7;
        var frame = Receive(raw);
        Near(frame["Rotation_x"], -60, "mirror reverses horizontal turn before sensitivity");
        Near(frame["Rotation_y"], -40, "mirror preserves nod direction");
        Near(frame["Rotation_z"], -14, "mirror reverses head roll before sensitivity");
        Near(frame["FaceAngleX"], -60, "API yaw mirrors like phone yaw");
        Near(frame["FaceAngleY"], -40, "API pitch keeps vertical direction");
        Near(frame["FaceAngleZ"], -14, "API roll mirrors like phone roll");
        Near(frame["Position_x"], -4, "horizontal position mirrored");
        Near(frame["Position_y"], 6, "vertical position preserved");
        Near(frame["EyeLeft_x"], -10, "eyes swap and horizontal gaze reverses");
        Near(frame["EyeLeft_y"], -6, "eye vertical direction preserved");
        Near(frame["EyeBlinkLeft"], .8f, "linked blink still takes maximum");
        Near(frame["EyeBlinkRight"], .8f, "linked blink still takes maximum on both eyes");
        HoFaceInputState.SetInputOptions(true, false);
        frame = Receive(Frame(Vector3.zero));
        Near(frame["EyeBlinkLeft"], .8f, "ARKit sides still swap");
        Near(frame["EyeBlinkRight"], .2f, "ARKit sides still swap independently");
    }

    private static void RotationNear(Quaternion actual, Quaternion expected, string name) =>
        Check(Quaternion.Angle(actual, expected) < .08f, name + " angular error=" + Quaternion.Angle(actual, expected));

    private static void Near(float actual, float expected, string name) =>
        Check(Mathf.Abs(actual - expected) < .001f, name + ": actual=" + actual + " expected=" + expected);

    private static void Check(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception(name);
    }
}
