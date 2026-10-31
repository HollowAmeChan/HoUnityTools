// Copy to an isolated Unity project's Assets/Editor together with the component and its dependencies.
// Unity -batchmode -nographics -projectPath <project> -executeMethod HoBlendShapeOverwriteValidation.Run
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HoBlendShapeOverwriteValidation
{
    [Serializable] public sealed class Report
    {
        public string status;
        public string error;
        public List<string> passed = new List<string>();
        public List<ReferenceShape> referenceShapes = new List<ReferenceShape>();
    }
    [Serializable] public sealed class ReferenceShape
    {
        public string asset, path, guid, shape;
        public long rendererFileId;
        public int index;
        public float weight;
    }
    private static Report report;
    private static string Output => Path.GetFullPath("validation.json");
    private const string Pending = "HoBlendShapeOverwriteValidation.Pending";
    private static GameObject lateRoot;
    private static Mesh lateMesh;
    private static SkinnedMeshRenderer lateRenderer;
    private static int lateFrame;

    static HoBlendShapeOverwriteValidation()
    {
        EditorApplication.update += () =>
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            RunPlaying();
        };
    }

    public static void Run()
    {
        report = new Report { status = "running" };
        try
        {
            Cases(true);
            InspectReferences();
            InspectConfiguredPrefab();
            Save();
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void RunPlaying()
    {
        report = JsonUtility.FromJson<Report>(File.ReadAllText(Output));
        try
        {
            Cases(false);
            lateRoot = new GameObject("LateUpdate overwrite test");
            lateMesh = MakeMesh("HIDE_Elbow", "Other");
            lateRenderer = Renderer(lateRoot, "Body", lateMesh);
            var outfit = Child(lateRoot, "Miko");
            Configure(outfit, new[] { lateRenderer }, new[] { "HIDE_Elbow" }, false, false);
            lateRenderer.SetBlendShapeWeight(0, 17);
            lateFrame = Time.frameCount;
            EditorApplication.update += WaitForLateUpdate;
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void WaitForLateUpdate()
    {
        if (Time.frameCount < lateFrame + 3) return;
        EditorApplication.update -= WaitForLateUpdate;
        try
        {
            Equal(lateRenderer, 0, 100, "play: actual LateUpdate overwrites external value");
            Object.DestroyImmediate(lateRoot);
            Object.DestroyImmediate(lateMesh);
            report.status = "passed";
            Save();
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void Cases(bool edit)
    {
        string p = edit ? "edit: " : "play: ";
        var root = new GameObject("Overwrite tests");
        var mesh = MakeMesh("HIDE_Elbow", "Smile");
        var replacement = MakeMesh("Unrelated", "HIDE_Elbow");
        try
        {
            var a = Renderer(root, "Body A", mesh);
            var b = Renderer(root, "Body B", mesh);
            var unrelated = Renderer(root, "No matching shape", replacement);
            a.SetBlendShapeWeight(0, 23); b.SetBlendShapeWeight(0, 41); a.SetBlendShapeWeight(1, 7);
            var outfit = Child(root, "Miko");
            var component = Configure(outfit, new[] { a, b, a }, new[] { "HIDE_Elbow", "Smile" }, edit, false);
            Equal(a, 0, 100, p + "first mesh"); Equal(b, 0, 100, p + "second mesh");
            Equal(a, 1, 100, p + "multiple rules");
            outfit.SetActive(false);
            Equal(a, 0, 0, p + "outfit off restores zero"); Equal(b, 0, 0, p + "all meshes restored");
            outfit.SetActive(true); Equal(a, 0, 100, p + "outfit on reapplies");
            component.enabled = false; Equal(a, 0, 0, p + "component off restores");
            component.enabled = true; Equal(a, 0, 100, p + "component on reapplies");
            root.SetActive(false); Equal(a, 0, 0, p + "parent off restores");
            root.SetActive(true); Equal(a, 0, 100, p + "parent on reapplies");
            Object.DestroyImmediate(component); Equal(a, 0, 0, p + "component deletion restores");

            a.SetBlendShapeWeight(0, 23); b.SetBlendShapeWeight(0, 41); a.SetBlendShapeWeight(1, 7);
            component = Configure(outfit, new[] { a, b }, new[] { "HIDE_Elbow" }, edit, true);
            Equal(a, 1, 7, p + "unlisted key untouched");
            component.enabled = false;
            Equal(a, 0, 23, p + "previous value A"); Equal(b, 0, 41, p + "previous value B");
            component.enabled = true;
            component.EvaluateOverwrite(); component.EvaluateOverwrite();
            component.enabled = false; Equal(a, 0, 23, p + "repeat cycle does not capture own output");
            Object.DestroyImmediate(component);

            component = Configure(outfit, new[] { a }, new[] { "HIDE_Elbow" }, edit, false);
            a.sharedMesh = replacement; a.SetBlendShapeWeight(0, 63);
            component.EvaluateOverwrite();
            Equal(a, 0, 63, p + "replacement mesh unrelated index untouched");
            Equal(a, 1, 100, p + "replacement mesh resolves name");
            component.enabled = false; Equal(a, 1, 0, p + "replacement mesh cleanup");
            Object.DestroyImmediate(component);
            a.sharedMesh = mesh;

            component = Configure(outfit, new[] { a }, new[] { "HIDE_Elbow" }, edit, false);
            var secondOutfit = Child(root, "Second outfit");
            var second = Configure(secondOutfit, new[] { a }, new[] { "HIDE_Elbow" }, edit, true);
            component.enabled = false;
            second.EvaluateOverwrite(); Equal(a, 0, 100, p + "conflicting owner acquires after release");
            second.enabled = false; Equal(a, 0, 0, p + "conflict never captures old overwrite as baseline");
            Object.DestroyImmediate(second); Object.DestroyImmediate(component);

            component = Configure(outfit, new[] { a, unrelated }, new[] { "Smile" }, edit, false);
            Equal(unrelated, 0, 0, p + "missing shape on one mesh skipped");
            Object.DestroyImmediate(a.gameObject);
            component.EvaluateOverwrite(); component.enabled = false;
            report.passed.Add(p + "destroyed renderer cleanup safe");
            Object.DestroyImmediate(component);

            if (edit)
            {
                component = Configure(outfit, new[] { b }, new[] { "HIDE_Elbow" }, true, false);
                // Register exactly as the Inspector does, then verify a real scene save stops preview.
                var preview = typeof(Hollow.HoUnityTools.Editor.Constraints.HoBlendShapeOverwritePreview);
                preview.GetMethod("Set", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { component, true });
                EditorSceneManager.SaveScene(root.scene, "Assets/PreviewSave.unity");
                Equal(b, 0, 0, "edit: scene save restores preview");
                if (component.EditorPreviewActive) throw new Exception("Preview survived save");
                report.passed.Add("edit: preview flag stopped before serialization");
            }
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(replacement); }
    }

    private static HoBlendShapeOverwrite Configure(GameObject outfit, SkinnedMeshRenderer[] meshes, string[] keys, bool edit, bool previous)
    {
        var component = outfit.AddComponent<HoBlendShapeOverwrite>();
        var so = new SerializedObject(component);
        var list = so.FindProperty("meshes"); list.arraySize = meshes.Length;
        for (int i = 0; i < meshes.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = meshes[i];
        list = so.FindProperty("rules"); list.arraySize = keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            list.GetArrayElementAtIndex(i).FindPropertyRelative("shapeName").stringValue = keys[i];
            list.GetArrayElementAtIndex(i).FindPropertyRelative("value").floatValue = 100;
        }
        so.FindProperty("restoreMode").enumValueIndex = previous ? 1 : 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (edit) component.SetEditorPreview(true); else component.EvaluateOverwrite();
        return component;
    }

    private static Mesh MakeMesh(params string[] names)
    {
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        foreach (string name in names) mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.one, Vector3.zero, Vector3.zero }, null, null);
        return mesh;
    }
    private static GameObject Child(GameObject root, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform); return go;
    }
    private static SkinnedMeshRenderer Renderer(GameObject root, string name, Mesh mesh)
    {
        var renderer = Child(root, name).AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; return renderer;
    }
    private static void Equal(SkinnedMeshRenderer renderer, int index, float expected, string test)
    {
        float actual = renderer.GetBlendShapeWeight(index);
        if (!Mathf.Approximately(actual, expected)) throw new Exception(test + $": expected {expected}, got {actual}");
        report.passed.Add(test);
    }
    private static void InspectReferences()
    {
        if (!Directory.Exists("Assets/Reference")) return;
        foreach (string path in Directory.GetFiles("Assets/Reference", "*.fbx"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int index = renderer.sharedMesh.GetBlendShapeIndex("HIDE_Elbow");
                if (index < 0) continue;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer, out string guid, out long id);
                report.referenceShapes.Add(new ReferenceShape
                {
                    asset = path, path = AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform),
                    guid = guid, rendererFileId = id, shape = renderer.sharedMesh.GetBlendShapeName(index), index = index,
                    weight = renderer.GetBlendShapeWeight(index)
                });
            }
        }
    }
    private static void InspectConfiguredPrefab()
    {
        const string path = "Assets/Reference/WTD-BUILD.prefab";
        if (!File.Exists(path)) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var components = prefab.GetComponentsInChildren<HoBlendShapeOverwrite>(true);
        if (components.Length != 1) throw new Exception("Expected exactly one configured overwrite component");
        var component = components[0];
        if (component.name != "巫女服" || component.Meshes.Count != 1 || component.Meshes[0] == null ||
            component.Rules.Count != 1 || component.Rules[0].shapeName != "HIDE_Elbow" || component.Rules[0].value != 100)
            throw new Exception("Configured prefab binding mismatch");
        var body = component.Meshes[0];
        int index = body.sharedMesh.GetBlendShapeIndex("HIDE_Elbow");
        if (index < 0) throw new Exception("Configured renderer does not contain HIDE_Elbow");
        Equal(body, index, 0, "prefab: serialized HIDE_Elbow baseline stays zero");
        report.passed.Add("prefab: Miko root references actual Body1 renderer and HIDE_Elbow=100");
        var so = new SerializedObject(component);
        if (so.FindProperty("restoreMode").enumValueIndex != 0 || so.FindProperty("restoreValue").floatValue != 0)
            throw new Exception("Prefab restore policy mismatch");
        report.passed.Add("prefab: disable restores fixed zero");
    }
    private static void Save() => File.WriteAllText(Output, JsonUtility.ToJson(report, true));
    private static void Fail(Exception ex)
    {
        if (report == null) report = new Report();
        report.status = "failed"; report.error = ex.ToString(); Save();
        Debug.LogException(ex); EditorApplication.Exit(1);
    }
}
