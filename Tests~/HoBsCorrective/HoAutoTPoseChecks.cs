#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.RigConstraints;
using UnityEditor;
using UnityEngine;

public static class HoAutoTPoseChecks
{
    [Serializable] private sealed class Mapping { public Armature[] armatures; }
    [Serializable] private sealed class Armature { public Bone[] bones; }
    [Serializable] private sealed class Bone { public string boneName, humanName; }
    [Serializable] private sealed class Result
    {
        public string name, worstBone;
        public int mappedBones;
        public float maxRotationError, maxPositionError, blenderPoseError, unityPoseError;
    }
    [Serializable] private sealed class Report { public string unity; public List<Result> cases = new List<Result>(); }
    private static readonly Type Setup = typeof(Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static MethodInfo Method(string name, int count) => Setup.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == count);
    private static float Angle(Quaternion a, Quaternion b)
    {
        double la = Math.Sqrt((double)a.x*a.x+(double)a.y*a.y+(double)a.z*a.z+(double)a.w*a.w);
        double lb = Math.Sqrt((double)b.x*b.x+(double)b.y*b.y+(double)b.z*b.z+(double)b.w*b.w);
        double dot = Math.Min(1, Math.Abs(((double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w)/(la*lb)));
        return (float)(2*Math.Atan2(Math.Sqrt(Math.Max(0,1-dot*dot)),dot)*180/Math.PI);
    }
    public static void Run()
    {
        var report = new Report { unity = Application.unityVersion };
        try
        {
            foreach (string path in Directory.GetFiles("Assets/Fixtures", "case*.fbx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixtures/" + name + ".json");
                var mapping = JsonUtility.FromJson<Mapping>(metadata.text).armatures.SelectMany(a => a.bones).ToDictionary(b => b.humanName, b => b.boneName);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var expected = UnityEngine.Object.Instantiate(model);
                var actual = UnityEngine.Object.Instantiate(model);
                try
                {
                    object Wrap(GameObject instance)
                    {
                        object modelBones = Method("GetModelBones", 3).Invoke(null, new object[] { instance.transform, true, null });
                        var get = Setup.GetMethods(Flags).Single(m => m.Name == "GetHumanBones" && m.GetParameters()[0].ParameterType == typeof(Dictionary<string,string>));
                        return get.Invoke(null, new object[] { mapping, modelBones });
                    }
                    object wrappers = Wrap(expected);
                    Method("MakePoseValid", 1).Invoke(null, new[] { wrappers });
                    var expectedBones = expected.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                    var actualBones = actual.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                    var description = HoFbxHumanoidImportUtility.BuildDescription(metadata, (ModelImporter)AssetImporter.GetAtPath(path));
                    foreach (var bone in description.skeleton)
                        if (actualBones.TryGetValue(bone.name, out Transform t))
                        { t.localPosition = bone.position; t.localRotation = bone.rotation; t.localScale = bone.scale; }
                    var result = new Result { name = name, mappedBones = mapping.Count,
                        unityPoseError = (float)Method("GetPoseError", 1).Invoke(null, new[] { wrappers }),
                        blenderPoseError = (float)Method("GetPoseError", 1).Invoke(null, new[] { Wrap(actual) }) };
                    foreach (string bone in mapping.Values)
                    {
                        float error = Angle(expectedBones[bone].localRotation, actualBones[bone].localRotation);
                        if (error > result.maxRotationError) { result.maxRotationError = error; result.worstBone = bone; }
                        result.maxPositionError = Mathf.Max(result.maxPositionError, Vector3.Distance(expectedBones[bone].position, actualBones[bone].position));
                    }
                    report.cases.Add(result);
                    // Validate against the engine, including cases where Unity's
                    // single traversal leaves a small final alignment error.
                    if (result.maxRotationError > .005f || result.maxPositionError > .00001f ||
                        Mathf.Abs(result.blenderPoseError-result.unityPoseError) > .01f)
                        throw new Exception("Automatic T-Pose differs from Unity: " + JsonUtility.ToJson(result));
                }
                finally { UnityEngine.Object.DestroyImmediate(actual); UnityEngine.Object.DestroyImmediate(expected); }
            }
            if (report.cases.Count != 3) throw new Exception("Missing independent automatic T-Pose fixtures");
            File.WriteAllText("auto_tpose_results.json", JsonUtility.ToJson(report, true));
            Debug.Log("AUTO_TPOSE_UNITY_PARITY_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("auto_tpose_results.json", JsonUtility.ToJson(report, true));
            File.WriteAllText("auto_tpose_error.txt", error.ToString());
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
}
#endif
