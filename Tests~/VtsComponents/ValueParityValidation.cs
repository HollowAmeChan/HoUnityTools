using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using UnityEditor;
using UnityEngine;

public static class HoFaceValueParityValidation
{
    private static int assertions;
    public static void RunBatch()
    {
        try
        {
            var assembly = Assembly.LoadFile(Path.GetFullPath(Path.Combine(Application.dataPath, "../HoFace.Core.dll")));
            Type portableDocument = assembly.GetType("Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueDocument", true);
            Type portableSession = assembly.GetType("Hollow.HoUnityTools.FaceTracking.Core.HoFaceValueSession", true);
            MethodInfo portableParse = portableDocument.GetMethod("TryParse");
            MethodInfo portableEvaluate = portableSession.GetMethod("Evaluate");
            PropertyInfo portableValues = portableSession.GetProperty("Values");
            foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "Profiles"), "*.json"))
            {
                string json = File.ReadAllText(path);
                Check(HoFaceValueDocument.TryParse(json, out var profile, out string error), error);
                object[] parseArgs = { json, null, null };
                Check((bool)portableParse.Invoke(null, parseArgs), "portable profile parse");
                object portable = Activator.CreateInstance(portableSession, parseArgs[1], 123u);
                var native = new HoFaceValueSession(profile, 123);
                var variables = new List<string>();
                foreach (var input in profile.inputs)
                    if (HoFaceExpression.TryParse(input.expression, out var expression, out _))
                        expression.CollectVariables(variables);
                for (int frame = 0; frame < 180; frame++)
                {
                    var raw = new Dictionary<string, float>(StringComparer.Ordinal);
                    for (int i = 0; i < variables.Count; i++)
                    {
                        string name = variables[i];
                        if (frame % 17 == 0 && i % 7 == 0) continue;
                        float value = (float)(Math.Sin(frame * .07 + i * .31) * .5 + .5);
                        if (name.StartsWith("Rotation_", StringComparison.Ordinal)) value = (value - .5f) * 90;
                        raw[name] = value;
                    }
                    double time = frame / 60.0;
                    native.Evaluate(raw, time);
                    portableEvaluate.Invoke(portable, new object[] { raw, time });
                    var values = (IDictionary)portableValues.GetValue(portable);
                    Check(values.Count == native.Values.Count, "same published parameter count");
                    foreach (var value in native.Values)
                        Near((float)values[value.Key], value.Value, Path.GetFileName(path) + " frame=" + frame + " " + value.Key);
                }
            }
            var nativeCurve = new AnimationCurve(new Keyframe(-2, -.5f, -.2f, .7f), new Keyframe(.3f, 1.3f, -.4f, 2),
                new Keyframe(3, -.8f, .9f, -.1f));
            var portableCurve = new HoFaceCurveData(new HoFaceCurveKey(-2, -.5f, -.2f, .7f),
                new HoFaceCurveKey(.3f, 1.3f, -.4f, 2), new HoFaceCurveKey(3, -.8f, .9f, -.1f));
            for (int i = -200; i <= 300; i++)
                Near(nativeCurve.Evaluate(i / 100f), portableCurve.Evaluate(i / 100f), "Unity/portable Hermite");
            Debug.Log("HO_FACE_VALUE_PARITY PASS assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Near(float actual, float expected, string label) =>
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) < .0002f, label + " actual=" + actual + " expected=" + expected);
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
}
