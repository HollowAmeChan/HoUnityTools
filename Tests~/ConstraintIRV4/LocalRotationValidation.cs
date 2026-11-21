using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Editor.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.Import;

public static class LocalRotationValidation
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var runner=new GameObject("CopyR Blender FBX local parity").AddComponent<LocalRotationProbe>();
            runner.models=new GameObject[2];runner.fixtures=new TextAsset[2];runner.configs=new TextAsset[2];
            int index=0;
            foreach(string variant in new[]{"Raw","Mch"})
            {
                string model="Assets/Fixture/"+variant+".fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(model);
                importer.animationType=ModelImporterAnimationType.Generic;importer.optimizeGameObjects=false;
                importer.importAnimation=false;importer.SaveAndReimport();
                runner.models[index]=AssetDatabase.LoadAssetAtPath<GameObject>(model);
                runner.fixtures[index]=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/"+variant+"_samples.json");
                runner.configs[index]=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture/"+variant+"_runtime_ir.json");
                var config=JsonUtility.FromJson<ConstraintConfig>(runner.configs[index].text);
                var local=HoDirectConstraintImporter.Build(config).Where(e=>e.Type=="COPY_ROTATION"&&e.raw.constraint.parameters.owner_space=="LOCAL").ToArray();
                if(local.Length==0)throw new Exception("Fixture has no local operations");
                foreach(var entry in local)
                {
                    var status=HoDirectConstraintImporter.Status(entry,HoDirectConstraintImporter.Backend.HoAux);
                    if(status!=(entry.Muted?HoDirectConstraintImporter.Capability.Skipped:HoDirectConstraintImporter.Capability.Exact))throw new Exception("HoAux local domain rejected: "+entry.Owner+" "+status+" "+HoDirectConstraintImporter.Issue(entry,HoDirectConstraintImporter.Backend.HoAux));
                    if(HoDirectConstraintImporter.Status(entry,HoDirectConstraintImporter.Backend.Standard)!=HoDirectConstraintImporter.Capability.Unsupported||HoDirectConstraintImporter.Status(entry,HoDirectConstraintImporter.Backend.Vrc)!=HoDirectConstraintImporter.Capability.Unsupported)throw new Exception("Native backend incorrectly accepts new local space");
                }
                ++index;
            }
            runner.outputPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../local-rotation-results.json"));
            EditorSceneManager.SaveScene(runner.gameObject.scene,"Assets/Fixture/LocalRotationParity.unity");
            Debug.Log("COPY_R_LOCAL_EDITOR_READY");EditorApplication.EnterPlaymode();
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
