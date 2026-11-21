using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DirectValidation
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Fixture/Direct.fbx");
            importer.animationType = ModelImporterAnimationType.Generic; importer.optimizeGameObjects = false; importer.importAnimation = false; importer.SaveAndReimport();
            var runner = new GameObject("IR v4 direct numerical parity").AddComponent<DirectRuntimeProbe>();
            runner.model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fixture/Direct.fbx"); runner.fixture = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/Direct_samples.json"); runner.ir = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/Direct_v4.json");
            runner.outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../direct-results.json"));
            EditorSceneManager.SaveScene(runner.gameObject.scene, "Assets/Fixture/DirectParity.unity"); EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
