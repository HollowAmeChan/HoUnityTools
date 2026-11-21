#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Editor.RigConstraints;
using Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR;
using UnityEditor;
using UnityEngine;

/// <summary>Exercise a real HoFBX asset, its IR/MCH bindings and original Humanoid clip.</summary>
public static class HoBsCharacterParityChecks
{
    [Serializable] public sealed class Rotation { public float x, y, z, w; }
    [Serializable] public sealed class Frame { public int frame; public float time; public Rotation[] rotations; public float[] weights; }
    [Serializable] public sealed class Output { public string mesh, shape, ruleId, pointId; public bool mute; }
    [Serializable] public sealed class Samples { public string armature, clipId; public int fps; public float duration; public string[] bones; public Output[] outputs; public Frame[] frames; }
    [Serializable] private sealed class Manifest { public Entry[] files; }
    [Serializable] private sealed class Entry { public string kind, file; }
    [Serializable] private sealed class Mapping { public HumanArmature[] armatures; }
    [Serializable] private sealed class HumanArmature { public string armatureName; public HumanMapping[] bones; }
    [Serializable] private sealed class HumanMapping { public string boneName, humanName; }

    public static void Run()
    {
        const string folder = "Assets/Character/";
        const string model = folder + "Yuqiu.fbx";
        var result = new HoBsCharacterParityPlayer.Result { unity = Application.unityVersion };
        GameObject instance = null;
        bool playRequested = false;
        try
        {
            Samples samples = JsonUtility.FromJson<Samples>(File.ReadAllText(folder + "blender_samples.json"));
            result.clip = samples.clipId;
            result.frames = samples.frames.Length;
            result.outputs = samples.outputs.Length;
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(folder + "HoFBX/Yuqiu_unity.json"));
            Func<string, TextAsset> metadata = kind => AssetDatabase.LoadAssetAtPath<TextAsset>(
                folder + "HoFBX/" + manifest.files.First(e => e.kind == kind).file);

            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            bool importerNeedsUpdate = importer.animationType != ModelImporterAnimationType.Human ||
                !importer.importBlendShapes;
            importer.importBlendShapes = true;
            importer.animationType = ModelImporterAnimationType.Human;
            Mapping mapping = JsonUtility.FromJson<Mapping>(metadata("humanoid").text);
            HumanDescription description = importer.humanDescription;
            HumanBone[] mappedBones = mapping.armatures.Where(a => a.armatureName == samples.armature)
                .SelectMany(a => a.bones)
                .Select(b => new HumanBone
                {
                    boneName = b.boneName,
                    humanName = b.humanName,
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray();
            bool mappingMatches = description.human != null && description.human.Length == mappedBones.Length &&
                description.human.Zip(mappedBones, (current, expected) =>
                    current.boneName == expected.boneName && current.humanName == expected.humanName).All(equal => equal);
            if (importerNeedsUpdate || !mappingMatches)
            {
                description.human = mappedBones;
                importer.humanDescription = description;
                importer.SaveAndReimport();
            }

            // Avatar.isValid checks the mapping, not whether Configure > Pose >
            // Enforce T-Pose has been applied. Run the same Unity editor solver
            // and persist its reference skeleton before importing constraints.
            EnforceTPose(importer, model, mappedBones, result);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
                throw new InvalidOperationException("HoFBX Humanoid avatar invalid");
            ApplyConstraints(manifest, instance, samples.armature, result);
            TextAsset bs = metadata("bsCorrective");
            if (HoFbxBsCorrectiveImportUtility.Apply(bs, instance) != result.outputs)
                throw new InvalidOperationException("BS output count mismatch");
            HoBsCorrective component = instance.GetComponent<HoBsCorrective>();
            if (component == null) throw new InvalidOperationException("Missing BS component");
            HoBsCorrective.Rule[] rules = FindRules(samples, component);
            foreach (HoBsCorrective.Rule rule in rules)
            {
                int index = rule.target.sharedMesh.GetBlendShapeIndex(rule.shapeName);
                var vertices = new Vector3[rule.target.sharedMesh.vertexCount];
                rule.target.sharedMesh.GetBlendShapeFrameVertices(index, 0, vertices, null, null);
                if (vertices.Any(v => v.sqrMagnitude > 1e-14f)) result.nonemptyOutputs++;
            }

            component.SetEditorPreview(true);
            // Replay Blender's sampled local inputs to isolate BS math and the
            // imported bind / MCH convention from Humanoid retargeting.
            foreach (Frame frame in samples.frames)
            {
                foreach (var group in rules.GroupBy(r => r.bone))
                {
                    HoBsCorrective.Rule rule = group.First();
                    Quaternion q = Mirrored(frame.rotations[Array.IndexOf(samples.bones, rule.sourceBone)]);
                    rule.bone.localRotation = rule.referenceRotation * q;
                }
                component.EvaluateNow();
                for (int i = 0; i < rules.Length; i++)
                    result.replayMaxWeightError = Mathf.Max(
                        result.replayMaxWeightError, Mathf.Abs(rules[i].lastOutput - frame.weights[i]));
            }
            if (result.replayMaxWeightError >= 0.0001f)
                throw new InvalidOperationException("Blender/Unity BS replay error: " + result.replayMaxWeightError);

            UnityEngine.Object.DestroyImmediate(instance);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            ApplyConstraints(manifest, instance, samples.armature, result);
            if (HoFbxBsCorrectiveImportUtility.Apply(bs, instance) != result.outputs)
                throw new InvalidOperationException("BS reimport output count mismatch");
            component = instance.GetComponent<HoBsCorrective>();
            animator = instance.GetComponent<Animator>();
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "Preview.anim");
            if (clip == null || !clip.isHumanMotion)
                throw new InvalidOperationException("VRMA_12 did not import as a Humanoid animation clip");

            HoBsCharacterParityPlayer player = instance.AddComponent<HoBsCharacterParityPlayer>();
            player.animator = animator;
            player.corrective = component;
            player.samples = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "blender_samples.json");
            player.clip = clip;
            player.importedConstraints = result.importedConstraints;
            player.nonemptyOutputs = result.nonemptyOutputs;
            player.replayMaxWeightError = result.replayMaxWeightError;
            player.tPoseApplied = result.tPoseApplied;
            player.tPoseErrorBefore = result.tPoseErrorBefore;
            player.tPoseErrorAfter = result.tPoseErrorAfter;
            player.tPoseChangedBones = result.tPoseChangedBones;
            player.tPoseMappedBones = result.tPoseMappedBones;
            player.tPoseSolverVerified = result.tPoseSolverVerified;
            player.sourceBones = samples.bones.Select(name =>
                instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == name)).ToArray();
            player.sourceBindRotations = player.sourceBones.Select(source =>
            {
                Transform original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(source);
                return original != null ? original.localRotation : source.localRotation;
            }).ToArray();
            playRequested = true;
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
            return;
        }
        catch (Exception exception)
        {
            result.status = "ERROR";
            result.issues.Add(exception.ToString());
            Debug.LogException(exception);
        }
        finally
        {
            if (instance != null && !playRequested)
                UnityEngine.Object.DestroyImmediate(instance);
            if (!playRequested)
            {
                string resultPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "character_results.json");
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
                EditorApplication.Exit(result.status == "ERROR" ? 1 : 0);
            }
        }
    }

    private static Quaternion Mirrored(Rotation q) => new Quaternion(q.x, -q.y, -q.z, q.w);

    private static void EnforceTPose(
        ModelImporter importer, string modelPath, HumanBone[] mapping,
        HoBsCharacterParityPlayer.Result result)
    {
        Type setup = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.AvatarSetupTool"))
            .FirstOrDefault(type => type != null);
        if (setup == null) throw new MissingMemberException("Unity AvatarSetupTool is unavailable.");
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Func<string, int, MethodInfo> method = (name, count) => setup.GetMethods(flags)
            .Single(m => m.Name == name && m.GetParameters().Length == count);
        GameObject reference = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        reference.name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
        try
        {
            var serializedImporter = new SerializedObject(importer);
            method("TransferDescriptionToPose", 2).Invoke(null, new object[] { serializedImporter, reference.transform });
            object modelBones = method("GetModelBones", 3).Invoke(null, new object[] { reference.transform, true, null });
            MethodInfo getHumanBones = setup.GetMethods(flags).Single(m => m.Name == "GetHumanBones" &&
                m.GetParameters()[0].ParameterType == typeof(System.Collections.Generic.Dictionary<string, string>));
            object humanBones = getHumanBones.Invoke(null, new object[] {
                mapping.ToDictionary(bone => bone.humanName, bone => bone.boneName), modelBones });
            var wrappers = (Array)humanBones;
            FieldInfo boneField = wrappers.GetType().GetElementType().GetField("bone");
            result.tPoseMappedBones = wrappers.Cast<object>()
                .Count(wrapper => boneField.GetValue(wrapper) as Transform != null);
            if (result.tPoseMappedBones != mapping.Length)
                throw new InvalidOperationException("Enforce T-Pose did not resolve every mapped bone: " +
                    result.tPoseMappedBones + "/" + mapping.Length);
            Transform[] transforms = reference.GetComponentsInChildren<Transform>(true);
            Quaternion[] before = transforms.Select(t => t.localRotation).ToArray();
            result.tPoseErrorBefore = (float)method("GetPoseError", 1).Invoke(null, new[] { humanBones });
            method("MakePoseValid", 1).Invoke(null, new[] { humanBones });
            result.tPoseErrorAfter = (float)method("GetPoseError", 1).Invoke(null, new[] { humanBones });
            result.tPoseChangedBones = transforms.Where((t, i) => Quaternion.Angle(before[i], t.localRotation) > 0.01f).Count();
            // Prove that a zero-error result is not caused by empty mappings or
            // a no-op reflection call. Perturb only this disposable reference,
            // then restore every transform before persisting the real T-pose.
            Quaternion[] solved = transforms.Select(t => t.localRotation).ToArray();
            Vector3[] solvedPositions = transforms.Select(t => t.localPosition).ToArray();
            Transform upperArm = (Transform)boneField.GetValue(wrappers.GetValue((int)HumanBodyBones.LeftUpperArm));
            upperArm.rotation = Quaternion.AngleAxis(75f, Vector3.forward) * upperArm.rotation;
            float perturbedError = (float)method("GetPoseError", 1).Invoke(null, new[] { humanBones });
            method("MakePoseValid", 1).Invoke(null, new[] { humanBones });
            float repairedError = (float)method("GetPoseError", 1).Invoke(null, new[] { humanBones });
            result.tPoseSolverVerified = perturbedError > 1f && repairedError < perturbedError;
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localRotation = solved[i];
                transforms[i].localPosition = solvedPositions[i];
            }
            if (!result.tPoseSolverVerified)
                throw new InvalidOperationException("Enforce T-Pose solver verification failed: " +
                    perturbedError + " -> " + repairedError);
            // Persist through the same serialized skeleton property as Avatar
            // Configure's Enforce T-Pose + Apply, without changing mesh bind data.
            method("TransferPoseToDescription", 2).Invoke(null, new object[] {
                serializedImporter.FindProperty("m_HumanDescription.m_Skeleton"), reference.transform });
            serializedImporter.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            result.tPoseApplied = true;
            Debug.Log("HO_BS_ENFORCE_TPOSE before=" + result.tPoseErrorBefore +
                " after=" + result.tPoseErrorAfter + " changedBones=" + result.tPoseChangedBones +
                " mapped=" + result.tPoseMappedBones + " solverPerturbation=" + perturbedError + "->" + repairedError);
        }
        finally { UnityEngine.Object.DestroyImmediate(reference); }
    }

    private static HoBsCorrective.Rule[] FindRules(Samples samples, HoBsCorrective component) =>
        samples.outputs.Select(output => component.Rules.Single(rule =>
            rule.target.name == output.mesh && rule.shapeName == output.shape)).ToArray();

    private static void ApplyConstraints(
        Manifest manifest, GameObject instance, string armatureName,
        HoBsCharacterParityPlayer.Result result)
    {
        Transform skeleton = instance.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == armatureName);
        foreach (Entry entry in manifest.files.Where(e => e.kind == "rigConstraintIR"))
        {
            TextAsset ir = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Character/HoFBX/" + entry.file);
            var config = HoConstraintIrAssetLoader.Load(ir, "HoAux");
            if (config.armatureName != armatureName) continue;
            Transform host = new GameObject("HoAux parity").transform;
            host.SetParent(instance.transform, false);
            result.importedConstraints = HoFbxConstraintImportUtility.ApplyHoAux(ir, skeleton.gameObject, host);
        }
    }
}
#endif
