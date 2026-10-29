// Real FastBuild -> official UMod build. Only run in a marked disposable SDK project.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.FaceTracking;
using HoFaceTracking.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HoVtsPackagingValidation
{
    private const string Phase = "HoVtsPackagingValidation.Phase";
    private const string Folder = "Assets/VtsFixture";
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static Type WindowType => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildWarudoModWindow"))
        .First(type => type != null);
    private static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string StatePath => Path.Combine(Project, "vts-packaging-state.json");
    private static string ReportPath => Path.Combine(Project, "vts-packaging-result.txt");
    private static string RuntimeReportPath => Path.Combine(Project, "vts-mod-runtime-result.txt");

    [InitializeOnLoadMethod]
    private static void Resume() { EditorApplication.update += Tick; }

    public static void RunBatch()
    {
        try
        {
            Require(File.Exists(Path.Combine(Project, ".ho-vts-packaging-validation")), "disposable project marker");
            Require(string.IsNullOrEmpty(SessionState.GetString(Phase, "")), "no pending test run");
            if (File.Exists(StatePath))
            {
                Type previousType = WindowType.GetNestedType("BuildState", BindingFlags.NonPublic);
                object previous = JsonUtility.FromJson(File.ReadAllText(StatePath), previousType);
                Call("FinishBuildState", previous, true);
            }
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_4_6);
            CreateFixture();
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/ExportSettings.asset");
            Require(settings != null, "SDK export settings loaded");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("activeProfile").intValue = 0;
            var profiles = serialized.FindProperty("exportProfiles");
            profiles.arraySize = 1;
            var profile = profiles.GetArrayElementAtIndex(0);
            profile.FindPropertyRelative("modName").stringValue = "HoVtsComponentsValidation";
            profile.FindPropertyRelative("modAuthor").stringValue = "Hollow";
            profile.FindPropertyRelative("modAssetPath").stringValue = Path.Combine(Project, Folder).Replace('\\', '/');
            string output = Path.Combine(Project, "Artifacts");
            Directory.CreateDirectory(output);
            profile.FindPropertyRelative("modExportPath").stringValue = output.Replace('\\', '/');
            serialized.FindProperty("showOutputDirectory").boolValue = false;
            serialized.FindProperty("clearConsoleOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            var window = ScriptableObject.CreateInstance(WindowType);
            try
            {
                WindowType.GetField("sourcePrefabPath", Instance).SetValue(window, Folder + "/Character.prefab");
                WindowType.GetField("exportSettingsPath", Instance).SetValue(window, "Assets/ExportSettings.asset");
                WindowType.GetField("cleanupTemporaryAssets", Instance).SetValue(window, false);
                WindowType.GetMethod("RefreshDependencyPreview", Instance).Invoke(window, new object[] { false });
                object state = WindowType.GetMethod("CreateStagedBuildState", Instance).Invoke(window, null);
                File.WriteAllText(StatePath, JsonUtility.ToJson(state, true));
                Call("ApplyTemporaryPlayerSettings", state);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            SessionState.SetString(Phase, "awaiting-compile");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("HO_VTS_PACKAGING staged source; waiting for compilation");
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Tick()
    {
        if (SessionState.GetString(Phase, "") == "awaiting-play" && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            SessionState.EraseString(Phase);
            RunLoadedMod();
            return;
        }
        if (SessionState.GetString(Phase, "") != "awaiting-compile" || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetString(Phase, "building");
        try
        {
            Require(!EditorUtility.scriptCompilationFailed, "staged source compiles");
            var stateType = WindowType.GetNestedType("BuildState", BindingFlags.NonPublic);
            var state = JsonUtility.FromJson(File.ReadAllText(StatePath), stateType);
            Call("PrepareStagedPrefab", state);
            string prefabPath = (string)stateType.GetField("temporaryPrefabPath").GetValue(state);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Require(prefab != null, "staged prefab saved");
            var components = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            Require(components.Length == 3 && components.All(component => component != null), "all three components remain attached");
            foreach (var component in components)
            {
                Require(component.GetType().Assembly.GetName().Name == "Assembly-CSharp", "component rebound to staged source");
                var script = MonoScript.FromMonoBehaviour(component);
                Require(script != null && script.GetClass() == component.GetType(), "staged MonoScript is valid");
            }
            var receiver = components.Single(component => component.GetType().Name == "HoVtsParameterReceiver");
            var controller = components.Single(component => component.GetType().Name == "HoVtsController");
            var controllerFields = new SerializedObject(controller);
            Require(controllerFields.FindProperty("parameterReceiver").objectReferenceValue == receiver, "receiver reference survives assembly rebind");
            Require(controllerFields.FindProperty("outputRoot").objectReferenceValue == prefab.transform, "output root survives assembly rebind");
            Require(controllerFields.FindProperty("controller").objectReferenceValue != null, "controller asset remains referenced");
            Require(controllerFields.FindProperty("bindings").arraySize == 1, "animation ownership is compiled in staged copy");
            var pose = components.Single(component => component.GetType().Name == "HoVtsLookAt");
            var poseFields = new SerializedObject(pose);
            Require(poseFields.FindProperty("parameterReceiver").objectReferenceValue == receiver, "pose receiver reference survives assembly rebind");
            Require(poseFields.FindProperty("referenceFrame").objectReferenceValue == prefab.transform, "pose reference frame survives assembly rebind");
            Require(poseFields.FindProperty("head").objectReferenceValue != null && poseFields.FindProperty("leftEye").objectReferenceValue != null
                && poseFields.FindProperty("rightEye").objectReferenceValue != null, "pose bone references survive assembly rebind");
            var receiverFields = new SerializedObject(receiver);
            Require(receiverFields.FindProperty("middleware").FindPropertyRelative("outputs").arraySize == 1, "character-owned rules survive rebind");
            Require((bool)Call("TrySyncEditorProjects"), "Unity project generation requested");
            Call("ValidateStagedScriptsAreInProjects", state);
            Call("ValidateTemporaryPlayerSettings", state);
            Call("ApplyTemporaryExportSettings", state);
            object build = Call("InvokeOfficialBuild", "Assets/ExportSettings.asset");
            string result = (string)Call("ValidateBuildResult", build);
            object verification = Call("RunArtifactVerification", state, build);
            Require(verification != null, "artifact verification available");
            string report = (string)verification.GetType().GetField("report").GetValue(verification);
            Require(!(bool)verification.GetType().GetProperty("HasProblems").GetValue(verification), "artifact has no missing or unconfirmed components");
            Require(report.Contains("HoVtsParameterReceiver") && report.Contains("HoVtsController") && report.Contains("HoVtsLookAt"), "artifact report covers all three VTS types");
            File.WriteAllText(ReportPath, "HO_VTS_PACKAGING PASS\n" + result + "\n" + report);
            Debug.Log("HO_VTS_PACKAGING PASS\n" + report);
            Call("FinishBuildState", state, true);
            SessionState.EraseString(Phase);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Fail(error); }
    }

    public static void RunLoadBatch()
    {
        Require(File.Exists(Path.Combine(Project, ".ho-vts-packaging-validation")), "disposable project marker");
        SessionState.SetString(Phase, "awaiting-play");
        EditorApplication.isPlaying = true;
    }

    private static void RunLoadedMod()
    {
        UMod.ModHost host = null;
        GameObject instance = null;
        var delivery = new HoVtsFrameDelivery();
        try
        {
            string artifact = Path.Combine(Project, "Artifacts/HoVtsComponentsValidation.warudo");
            Require(File.Exists(artifact), "built artifact exists");
            host = UMod.Mod.Load(new Uri(artifact), true);
            Require(host != null && host.IsModLoaded, "UMod actually loads the produced artifact");
            var prefab = host.SharedAssets.Load<GameObject>("Character");
            Require(prefab != null, "UMod resolves the canonical Character prefab");
            instance = UnityEngine.Object.Instantiate(prefab);
            var components = instance.GetComponentsInChildren<MonoBehaviour>(true);
            var receiver = components.Single(component => component != null && component.GetType().FullName == typeof(HoVtsParameterReceiver).FullName);
            var controller = components.Single(component => component != null && component.GetType().FullName == typeof(HoVtsController).FullName);
            var pose = components.Single(component => component != null && component.GetType().FullName == typeof(HoVtsLookAt).FullName);
            Require(receiver.GetType() != typeof(HoVtsParameterReceiver) && controller.GetType() != typeof(HoVtsController)
                && pose.GetType() != typeof(HoVtsLookAt), "all three instances use Mod types, not package types");
            Require(receiver.GetType().Assembly == controller.GetType().Assembly && controller.GetType().Assembly == pose.GetType().Assembly,
                "all three component types share the character Mod assembly");
            Require(components.All(component => component != null), "loaded character has no missing script");
            var raw = new System.Collections.Generic.Dictionary<string, float> {
                { "FaceFound", 1 }, { "JawOpen", .6f }, { "Rotation_x", 0 }, { "Rotation_y", 20 }, { "Rotation_z", 0 },
                { "EyeLeft_x", 0 }, { "EyeLeft_y", 0 }, { "EyeLeft_z", 0 },
                { "EyeRight_x", 0 }, { "EyeRight_y", 0 }, { "EyeRight_z", 0 }, { "Custom", -123 } };
            Require(delivery.Deliver(instance, raw, true, Time.realtimeSinceStartupAsDouble, "validation"),
                "production bridge delivers across the actual Mod assembly boundary: " + delivery.Status);
            float value = (float)receiver.GetType().GetMethod("Get").Invoke(receiver, new object[] { "Drive", -1f });
            Require(Mathf.Abs(value - .6f) < .0001f, "bundled character rules evaluate the input");
            raw["JawOpen"] = .2f;
            value = (float)receiver.GetType().GetMethod("GetRaw").Invoke(receiver, new object[] { "JawOpen", -1f });
            Require(Mathf.Abs(value - .6f) < .0001f, "Mod receiver owns a copied raw frame");
            Require((bool)controller.GetType().GetMethod("EvaluateNow", new[] { typeof(float) }).Invoke(controller, new object[] { 0f }),
                "Mod controller graph evaluates its bundled controller");
            var mesh = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            Require(Mathf.Abs(mesh.GetBlendShapeWeight(0) - 50) < .01f, "bundled animation writes the owned shape");
            Require(!instance.GetComponent<Animator>().enabled, "disabled character Animator stays disabled in loaded Mod");
            Require((bool)pose.GetType().GetMethod("EvaluateNow", new[] { typeof(float) }).Invoke(pose, new object[] { 0f }),
                "Mod pose component reads the same receiver");
            Require(Quaternion.Angle(instance.transform.Find("Head").localRotation, Quaternion.Euler(0, 20, 0)) < .1f,
                "loaded Mod pose is written to its own head bone");
            delivery.Release();
            Require(!(bool)controller.GetType().GetMethod("EvaluateNow", new[] { typeof(float) }).Invoke(controller, new object[] { 0f }),
                "released Mod input stops controller output");
            Require(!(bool)pose.GetType().GetMethod("EvaluateNow", new[] { typeof(float) }).Invoke(pose, new object[] { 0f }),
                "released Mod input stops pose output");
            Require(Mathf.Abs(mesh.GetBlendShapeWeight(0)) < .01f && Quaternion.Angle(instance.transform.Find("Head").localRotation, Quaternion.identity) < .1f,
                "actual Mod components restore their owned properties");
            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            host.UnloadMod();
            host = null;
            File.WriteAllText(RuntimeReportPath, "HO_VTS_MOD_RUNTIME PASS\nActual UMod artifact loaded in Unity 2021.3.45f2 play mode; cross-assembly input, bundled rules/controller, three runtime types, pose, and release verified.\n");
            Debug.Log("HO_VTS_MOD_RUNTIME PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            delivery.Release();
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (host != null) host.UnloadMod();
            File.WriteAllText(RuntimeReportPath, "HO_VTS_MOD_RUNTIME FAIL\n" + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void CreateFixture()
    {
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets", "VtsFixture");
        var root = new GameObject("Character");
        try
        {
            root.AddComponent<Animator>().enabled = false;
            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Driven", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            AssetDatabase.CreateAsset(mesh, Folder + "/Face.asset");
            face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            var host = new GameObject("VTS");
            host.transform.SetParent(root.transform, false);
            var receiver = host.AddComponent<HoVtsParameterReceiver>();
            var rules = new HoFaceMiddleware();
            rules.outputs.Add(new HoFaceOutput { parameter = "Drive", expression = "JawOpen" });
            receiver.SetMiddleware(rules);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Face.controller");
            controller.AddParameter("Drive", AnimatorControllerParameterType.Float);
            var clip = new AnimationClip { name = "Face" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Driven"), AnimationCurve.Constant(0, 1, 50));
            AssetDatabase.CreateAsset(clip, Folder + "/Face.anim");
            var state = controller.layers[0].stateMachine.AddState("Face");
            state.motion = clip;
            var slot = host.AddComponent<HoVtsController>();
            slot.Controller = controller;
            slot.OutputRoot = root.transform;
            slot.ParameterReceiver = receiver;
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            var left = new GameObject("LeftEye").transform;
            left.SetParent(head, false);
            var right = new GameObject("RightEye").transform;
            right.SetParent(head, false);
            var pose = host.AddComponent<HoVtsLookAt>();
            pose.ParameterReceiver = receiver;
            pose.ReferenceFrame = root.transform;
            pose.Head = head;
            pose.LeftEye = left;
            pose.RightEye = right;
            // Intentionally do not bake here: exercise the real FastBuild preparation hook.
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Character.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static object Call(string name, params object[] arguments)
    {
        try { return WindowType.GetMethod(name, Static).Invoke(null, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("HO_VTS_PACKAGING ok " + message);
    }
    private static void Fail(Exception error)
    {
        SessionState.EraseString(Phase);
        File.WriteAllText(ReportPath, "HO_VTS_PACKAGING FAIL\n" + error);
        Debug.LogException(error);
        EditorApplication.Exit(1);
    }
}
