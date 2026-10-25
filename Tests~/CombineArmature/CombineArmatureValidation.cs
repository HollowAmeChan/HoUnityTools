using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Armatures;
using Hollow.HoUnityTools.Editor.Armatures;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run in a disposable Unity project with -executeMethod CombineArmatureValidation.RunBatch.
[InitializeOnLoad]
public static class CombineArmatureValidation
{
    private const string StageKey = "HoCombine.Validation.Stage";
    private const string PassedKey = "HoCombine.Validation.Passed";
    private static double deadline;

    private sealed class Fixture : IDisposable
    {
        public GameObject root;
        public Transform rig, hips, chest, outfit, source, sourceHips, sourceChest, skirt, tip;
        public SkinnedMeshRenderer renderer;
        public HoCombineArmature config;
        public CombineArmatureTestReferences references;
        public Mesh mesh;
        public void Dispose()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (mesh != null && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
        }
    }

    static CombineArmatureValidation()
    {
        if (SessionState.GetInt(StageKey, 0) != 0) AttachMonitor();
    }

    public static void RunBatch()
    {
        SessionState.SetBool("HoCombine.Validation.Prebaked", false);
        SessionState.SetInt(PassedKey, 0);
        SessionState.SetInt(StageKey, 0);
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestMergeAndSkin();
            TestOffsetBindFrames();
            TestMatchingAndFailures();
            TestAnimations();
            TestMultipleOutfits();
            TestPrefabCopy();
            TestLegacySerialization();
            TestConstraintStructure();
            PreparePlayScene(true);
            EditorSettings.enterPlayModeOptionsEnabled = false;
            SessionState.SetInt(StageKey, 1);
            AttachMonitor();
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception) { Fail(exception); }
    }

    public static void RunBakedConstraints()
    {
        SessionState.SetBool("HoCombine.Validation.Prebaked", true);
        SessionState.SetInt(PassedKey, 0);
        SessionState.SetInt(StageKey, 0);
        try
        {
            PreparePlayScene(false);
            GameObject root = GameObject.Find("PlayCharacter");
            root.GetComponentInChildren<HoCombineArmature>(true).mode = HoCombineArmatureMode.Constraints;
            HoCombineArmatureProcessor.ProcessHierarchy(root);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            EditorSettings.enterPlayModeOptionsEnabled = false;
            SessionState.SetInt(StageKey, 3);
            AttachMonitor();
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void TestMergeAndSkin()
    {
        using (Fixture f = Create())
        {
            Transform meshParent = f.renderer.transform.parent;
            Matrix4x4 originalBind = f.mesh.bindposes[0];
            using (var before = new MeshScope())
            using (var after = new MeshScope())
            {
                f.renderer.BakeMesh(before.mesh);
                var report = HoCombineArmatureProcessor.ProcessHierarchy(f.root);
                f.renderer.BakeMesh(after.mesh);
                Check(report.IsValid && report.matchedBones == 3, "merge maps root/hips/chest");
                Check(f.sourceHips == null && f.sourceChest == null, "duplicate bones removed");
                Check(f.source != null && f.source.parent == f.outfit, "source root identity/path retained");
                Check(f.skirt.parent == f.hips && f.tip.parent == f.skirt, "unique branch moved intact");
                Check(f.renderer.transform.parent == meshParent, "mesh/outfit hierarchy unchanged");
                Check(f.renderer.bones[0] == f.chest && f.renderer.bones[1] == f.tip && f.renderer.rootBone == f.hips,
                    "skin bones/rootBone remapped without reordering");
                Check(f.renderer.probeAnchor == f.chest, "renderer probeAnchor remapped");
                Check(f.references.bone == f.chest && f.references.boneObject == f.hips.gameObject &&
                    f.references.bones[0] == f.chest && f.references.bones[1] == f.tip &&
                    f.references.sourceRoot == f.source, "serialized Transform/GameObject/array references remapped");
                Check(f.renderer.sharedMesh == f.mesh && f.mesh.bindposes[0] == originalBind,
                    "source mesh and bindposes unchanged");
                Check(VertexError(before.mesh, after.mesh) < 0.0001f, "baked skin vertices unchanged by merge");
                f.chest.localPosition += Vector3.right * 0.3f;
                f.renderer.BakeMesh(after.mesh);
                Check(Vector3.Distance(after.mesh.vertices[0], before.mesh.vertices[0]) > 0.1f,
                    "merged clothing follows animated target bone");
                Check(f.root.GetComponentsInChildren<HoCombineArmature>(true).Length == 0, "authoring marker stripped");
                Check(HoCombineArmatureProcessor.ProcessHierarchy(f.root).matchedBones == 0, "processing is idempotent");
            }
        }
        using (Fixture f = Create())
        {
            f.outfit.gameObject.SetActive(false);
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(!f.outfit.gameObject.activeSelf && f.sourceChest == null, "inactive outfit is processed and remains hidden");
        }
    }

    private static void TestMatchingAndFailures()
    {
        using (Fixture f = Create())
        {
            f.sourceHips.name = "cloth_Hips_end";
            f.sourceChest.name = "cloth_Chest_end";
            f.config.bonePrefix = "cloth_";
            f.config.boneSuffix = "_end";
            // Unmatched names that don't have the prefix are independent unique branches.
            Child(f.sourceHips, "RibbonOne", Vector3.zero);
            Child(f.sourceHips, "RibbonTwo", Vector3.zero);
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.renderer.bones[0] == f.chest, "prefix/suffix matching and unmatched branches");
        }
        using (Fixture f = Create())
        {
            f.sourceChest.localPosition += Vector3.right;
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.sourceChest != null && f.renderer.bones[0] == f.sourceChest,
                "different bind poses retain offset bones instead of rejecting the outfit");
        }
        using (Fixture f = Create())
        {
            Child(f.hips, "Chest", Vector3.zero);
            ExpectFailure(f, "duplicate target siblings rejected");
        }
        using (Fixture f = Create())
        {
            f.sourceChest.gameObject.AddComponent<BoxCollider>();
            ExpectFailure(f, "components on removed bones rejected");
        }
        using (Fixture f = Create())
        {
            f.config.targetArmature = f.sourceHips;
            ExpectFailure(f, "overlapping source/target rejected");
        }
        using (Fixture f = Create())
        {
            f.sourceChest.name = "UnmatchedChest";
            // Matching is relative to the matched parent, not a global name lookup.
            Child(f.rig, "UnmatchedChest", f.sourceChest.position);
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.renderer.bones[0] == f.sourceChest && f.sourceChest != null,
                "same name under wrong parent does not match");
        }
        using (Fixture f = Create())
        {
            GameObject external = new GameObject("ExternalReference");
            external.AddComponent<CombineArmatureTestReferences>().bone = f.sourceChest;
            try { ExpectFailure(f, "out-of-scope scene references rejected"); }
            finally { Object.DestroyImmediate(external); }
        }
        using (Fixture f = Create())
        {
            f.config.enabled = false;
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.sourceChest != null && f.root.GetComponentInChildren<HoCombineArmature>(true) == null,
                "disabled marker stripped without merging");
        }
        using (Fixture f = Create())
        {
            f.config.processAutomatically = false;
            HoCombineArmatureProcessor.ProcessHierarchy(f.root, true);
            Check(f.sourceChest != null && f.config != null, "automatic processing respects manual-only flag");
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.sourceChest == null && f.config == null, "explicit export processes manual-only configuration");
        }
    }

    private static void TestOffsetBindFrames()
    {
        using (Fixture f = Create())
        using (var before = new MeshScope())
        using (var after = new MeshScope())
        {
            f.sourceChest.localPosition += new Vector3(0.12f, 0.03f, -0.02f);
            f.sourceChest.localRotation = Quaternion.Euler(13, -21, 7);
            f.sourceChest.localScale = new Vector3(1.1f, 0.9f, 1.05f);
            f.skirt.SetParent(f.sourceChest, true);
            Vector3 position = f.sourceChest.localPosition, scale = f.sourceChest.localScale;
            Quaternion rotation = f.sourceChest.localRotation;
            Matrix4x4 sourceBind = f.sourceChest.localToWorldMatrix;
            Matrix4x4 targetBind = f.chest.localToWorldMatrix;
            Mesh originalMesh = f.renderer.sharedMesh;
            f.renderer.BakeMesh(before.mesh);
            var report = HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            f.renderer.BakeMesh(after.mesh);
            Check(report.offsetBones == 1, "different source bind frame is retained under the matching body bone");
            Check(VertexError(before.mesh, after.mesh) < 0.0001f, "offset merge preserves baked skin vertices");
            Check(f.renderer.sharedMesh == originalMesh, "offset merge does not modify or clone mesh assets");
            Check(f.sourceChest.localPosition == position && f.sourceChest.localScale == scale &&
                Quaternion.Angle(f.sourceChest.localRotation, rotation) < 0.01f,
                "offset merge preserves original local TRS for local-space rig consumers");
            Check(f.skirt.parent == f.sourceChest, "unique branch retains its offset source parent");
            Check(f.references.bone == f.sourceChest && f.renderer.probeAnchor == f.sourceChest,
                "serialized/probe references retain the offset coordinate frame");
            f.chest.localRotation = Quaternion.Euler(-11, 28, 6);
            f.chest.localPosition += Vector3.right * 0.15f;
            Matrix4x4 expected = f.chest.localToWorldMatrix * targetBind.inverse * sourceBind;
            Check(MatrixError(f.sourceChest.localToWorldMatrix, expected) < 0.0001f,
                "retained bind frame follows body animation with the original offset");
        }
        using (Fixture f = Create())
        using (var before = new MeshScope())
        using (var after = new MeshScope())
        {
            f.rig.localScale = new Vector3(0.8f, 1.3f, 1.1f);
            f.hips.localRotation = Quaternion.Euler(25, -5, 17);
            f.source.localRotation = Quaternion.Euler(17, -8, 3);
            f.source.localScale = new Vector3(-1.1f, 0.8f, 1.3f);
            f.sourceHips.localRotation = Quaternion.Euler(-8, 11, 30);
            Matrix4x4 sourceChest = f.sourceChest.localToWorldMatrix;
            f.renderer.BakeMesh(before.mesh);
            var report = HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            f.renderer.BakeMesh(after.mesh);
            Check(report.offsetBones == 3 && f.source != null && f.sourceChest != null,
                "mirrored and non-uniform bind frames remain supported");
            Check(MatrixError(f.sourceChest.localToWorldMatrix, sourceChest) < 0.0002f &&
                VertexError(before.mesh, after.mesh) < 0.0002f,
                "factored bind frame preserves shear without changing skin shape");
        }
    }

    private static void TestAnimations()
    {
        using (Fixture f = Create())
        {
            Animator animator = f.root.AddComponent<Animator>();
            var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Forbidden.controller");
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Outfits/A/Mesh", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.AddObjectToAsset(clip, controller);
            controller.AddMotion(clip);
            animator.runtimeAnimatorController = controller;
            Check(HoCombineArmatureProcessor.Validate(f.config).IsValid, "mesh toggle animation accepted");
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Outfits/A/Outfits_A/Hips/Chest", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0, 1, 0));
            int bindingCount = AnimationUtility.GetCurveBindings(clip).Length;
            ExpectFailure(f, "animation into skeleton subtree rejected");
            Check(AnimationUtility.GetCurveBindings(clip).Length == bindingCount, "animation asset never rewritten");
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Outfits/A/Outfits_A/Hips/Chest", typeof(Transform), "m_LocalPosition.x"), null);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Rig/Hips/Chest", typeof(Transform), "m_LocalScale.x"),
                AnimationCurve.Linear(0, 1, 1, 1.2f));
            f.config.mode = HoCombineArmatureMode.Constraints;
            ExpectFailure(f, "constraint backend rejects unsupported target bone scale animation");
        }
        using (Fixture f = Create())
        {
            // Real imports often put the body Animator on the body FBX, outside the outfit subtree.
            Animator animator = f.rig.gameObject.AddComponent<Animator>();
            var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/BodyOnly.controller");
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Hips/Chest", typeof(Transform), "m_LocalScale.x"),
                AnimationCurve.Linear(0, 1, 1, 1.2f));
            AssetDatabase.AddObjectToAsset(clip, controller);
            controller.AddMotion(clip);
            animator.runtimeAnimatorController = controller;
            f.config.mode = HoCombineArmatureMode.Constraints;
            ExpectFailure(f, "scale validation includes body-only Animator outside outfit subtree");
        }
    }

    private static void TestMultipleOutfits()
    {
        using (Fixture f = Create())
        using (Fixture second = Create())
        {
            second.outfit.SetParent(f.outfit.parent, true);
            second.config.targetArmature = f.rig;
            second.config.characterRoot = f.root.transform;
            second.source.name = "Outfits_B";
            second.outfit.name = "B";
            // Discard the second body, keeping only its outfit under the common character.
            Object.DestroyImmediate(second.root);
            second.root = null;
            f.outfit.gameObject.SetActive(false);
            HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.skirt != null && second.skirt != null && f.skirt != second.skirt &&
                f.skirt.parent == f.hips && second.skirt.parent == f.hips && f.skirt.name != second.skirt.name,
                "two outfits retain independent namespaced special bones");
            Check(f.renderer.bones[0] == f.chest && second.renderer.bones[0] == f.chest,
                "both outfits share the main skeleton");
            Check(!f.outfit.gameObject.activeSelf && second.outfit.gameObject.activeSelf,
                "outfit switch roots retain independent active states");
        }
        using (Fixture f = Create())
        {
            var second = Child(f.root.transform, "BadOutfit", Vector3.zero).gameObject.AddComponent<HoCombineArmature>();
            second.targetArmature = f.rig;
            second.characterRoot = f.root.transform;
            ExpectFailure(f, "all outfits validated before first mutation");
        }
    }

    private static void TestPrefabCopy()
    {
        using (Fixture f = Create())
        {
            AssetDatabase.CreateAsset(f.mesh, "Assets/PrefabMesh.asset");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(f.root, "Assets/Source.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject baked = null;
            try
            {
                baked = HoCombineArmatureProcessor.BakeCopy(instance);
                Check(instance.transform.Find("Outfits/A/Outfits_A/Hips/Chest") != null &&
                    instance.GetComponentInChildren<HoCombineArmature>(true) != null,
                    "bake copy leaves prefab instance unchanged");
                Check(prefab.transform.Find("Outfits/A/Outfits_A/Hips/Chest") != null,
                    "source prefab hierarchy unchanged");
                Check(baked.transform.Find("Outfits/A/Outfits_A/Hips") == null &&
                    baked.GetComponentInChildren<SkinnedMeshRenderer>(true).bones[0] == baked.transform.Find("Rig/Hips/Chest"),
                    "prefab instance bake remaps to cloned main skeleton");
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(baked, "Assets/Baked.prefab");
                GameObject loaded = PrefabUtility.LoadPrefabContents("Assets/Baked.prefab");
                try
                {
                    Check(loaded.GetComponentInChildren<HoCombineArmature>(true) == null &&
                        loaded.GetComponentInChildren<SkinnedMeshRenderer>(true).bones.All(b => b != null),
                        "processed prefab reload has no dangling skin references or authoring component");
                }
                finally { PrefabUtility.UnloadPrefabContents(loaded); }

                GameObject contents = PrefabUtility.LoadPrefabContents("Assets/Source.prefab");
                try
                {
                    HoCombineArmatureProcessor.ProcessHierarchy(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, "Assets/Exported.prefab");
                    Check(contents.GetComponentInChildren<HoCombineArmature>(true) == null,
                        "exporter can process isolated LoadPrefabContents copy");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Check(prefab.transform.Find("Outfits/A/Outfits_A/Hips/Chest") != null,
                    "processing prefab contents does not overwrite source asset");
            }
            finally
            {
                Object.DestroyImmediate(instance);
                if (baked != null) Object.DestroyImmediate(baked);
            }
        }
    }

    private static void TestConstraintStructure()
    {
        using (Fixture f = Create())
        {
            f.config.mode = HoCombineArmatureMode.Constraints;
            var report = HoCombineArmatureProcessor.ProcessHierarchy(f.root);
            Check(f.sourceChest.parent == f.sourceHips && f.skirt.parent == f.sourceHips &&
                f.renderer.bones[0] == f.sourceChest, "constraint mode preserves hierarchy and skin references");
            Check(report.generatedConstraints == 3 && f.sourceChest.GetComponent<ParentConstraint>().locked &&
                f.sourceChest.GetComponent<ScaleConstraint>() == null,
                "constraint mode creates configured native parent constraints without scale writers");
        }
    }

    private static void TestLegacySerialization()
    {
        using (Fixture f = Create())
        {
            AssetDatabase.CreateAsset(f.mesh, "Assets/LegacyMesh.asset");
            f.config.enabled = false;
            f.config.bonePrefix = "legacy_";
            f.config.boneSuffix = "_suffix";
            f.config.mode = HoCombineArmatureMode.Constraints;
            PrefabUtility.SaveAsPrefabAsset(f.root, "Assets/LegacySource.prefab");
            string yaml = File.ReadAllText("Assets/LegacySource.prefab");
            int script = yaml.IndexOf("guid: e728499dd926419ca259bb6d7519c346", StringComparison.Ordinal);
            int field = yaml.IndexOf("  m_EditorClassIdentifier:", script, StringComparison.Ordinal);
            int end = yaml.IndexOf('\n', field);
            yaml = yaml.Substring(0, field) + "  m_EditorClassIdentifier: HoUnityTools.Armatures.Runtime::Hollow.HoUnityTools.Armatures.CombineArmature\n" + yaml.Substring(end + 1);
            File.WriteAllText("Assets/Legacy.prefab", yaml);
            AssetDatabase.ImportAsset("Assets/Legacy.prefab", ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Legacy.prefab");
            var migrated = prefab.GetComponentInChildren<HoCombineArmature>(true);
            Check(migrated != null && !migrated.enabled && migrated.bonePrefix == "legacy_" &&
                migrated.boneSuffix == "_suffix" && migrated.mode == HoCombineArmatureMode.Constraints &&
                migrated.targetArmature != null && migrated.characterRoot == prefab.transform,
                "legacy component class identifier retains settings and references after Ho rename");
            Check(AssetDatabase.GUIDToAssetPath("e728499dd926419ca259bb6d7519c346").EndsWith("/HoCombineArmature.cs", StringComparison.Ordinal),
                "component rename preserves the existing MonoScript GUID");
        }
    }

    private static void PreparePlayScene(bool animateScale)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Fixture f = Create();
        string prefix = animateScale ? "Assets/MergePlay" : "Assets/ConstraintPlay";
        AssetDatabase.CreateAsset(f.mesh, prefix + "Mesh.asset");
        f.root.name = "PlayCharacter";
        f.root.transform.localScale = Vector3.one * 1.3f;
        if (!animateScale)
        {
            f.sourceChest.localPosition += new Vector3(0.12f, -0.03f, 0.02f);
            f.sourceChest.localRotation = Quaternion.Euler(-3, 14, 5);
            f.sourceChest.localScale = new Vector3(1.1f, 0.9f, 1.05f);
            f.references.useBindOffset = true;
            f.references.bindOffset = f.chest.worldToLocalMatrix * f.sourceChest.localToWorldMatrix;
        }
        var animator = f.root.AddComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var avatar = AvatarBuilder.BuildGenericAvatar(f.root, "Rig");
        AssetDatabase.CreateAsset(avatar, prefix + "Avatar.asset");
        animator.avatar = avatar;
        var controller = AnimatorController.CreateAnimatorControllerAtPath(prefix + ".controller");
        var clip = new AnimationClip { name = "TargetMotion", wrapMode = WrapMode.Loop };
        clip.SetCurve("Rig/Hips/Chest", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 2, 0.8f));
        clip.SetCurve("Rig/Hips/Chest", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 2, 40));
        if (animateScale)
            foreach (string axis in new[] { "x", "y", "z" })
                clip.SetCurve("Rig/Hips/Chest", typeof(Transform), "localScale." + axis, AnimationCurve.Linear(0, 1, 2, 1.4f));
        AssetDatabase.AddObjectToAsset(clip, controller);
        controller.AddMotion(clip);
        animator.runtimeAnimatorController = controller;
        f.references.expectedTarget = f.chest;
        f.references.constrainedBone = f.sourceChest;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Play.unity");
        AssetDatabase.SaveAssets();
    }

    private static void AttachMonitor()
    {
        deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.update -= Monitor;
        EditorApplication.update += Monitor;
    }

    private static void Monitor()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Play-mode validation timed out");
            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 1 && EditorApplication.isPlaying)
            {
                GameObject root = GameObject.Find("PlayCharacter");
                if (root == null || Time.frameCount < 12) return;
                Check(root.GetComponentInChildren<HoCombineArmature>(true) == null &&
                    root.transform.Find("Outfits/A/Outfits_A/Hips") == null,
                    "real Play entry automatically merges scene copy");
                Check(root.GetComponentInChildren<SkinnedMeshRenderer>(true).bones[0] == root.transform.Find("Rig/Hips/Chest"),
                    "real Play skin references target skeleton");
                SessionState.SetInt(StageKey, 2);
                EditorApplication.isPlaying = false;
            }
            else if (stage == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                GameObject root = GameObject.Find("PlayCharacter");
                Check(root.transform.Find("Outfits/A/Outfits_A/Hips/Chest") != null &&
                    root.GetComponentInChildren<HoCombineArmature>(true) != null,
                    "exiting Play restores original hierarchy and marker");
                // Independent fixtures/controllers prevent Write Defaults or native controller
                // caches from carrying animated scale from the previous Play session.
                PreparePlayScene(false);
                root = GameObject.Find("PlayCharacter");
                root.GetComponentInChildren<HoCombineArmature>(true).mode = HoCombineArmatureMode.Constraints;
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                // Also exercise Domain Reload disabled while retaining safe Scene Reload.
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                SessionState.SetInt(StageKey, 3);
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.isPlaying = true;
            }
            else if (stage == 3 && EditorApplication.isPlaying)
            {
                GameObject root = GameObject.Find("PlayCharacter");
                if (root == null) return;
                var probe = root.GetComponent<CombineArmatureTestReferences>();
                if (probe.samples < 30) return;
                Check(root.GetComponentInChildren<HoCombineArmature>(true) == null &&
                    root.transform.Find("Outfits/A/Outfits_A/Hips/Chest") != null,
                    "real Play constraints preserve original skeleton paths");
                Check(probe.maxPositionError < 0.001f && probe.maxRotationError < 0.05f && probe.maxScaleError < 0.001f,
                    "native constraints follow animated translation/rotation with common root scale in same frame: " +
                    probe.maxPositionError + "/" + probe.maxRotationError + "/" + probe.maxScaleError);
                SessionState.SetInt(StageKey, 4);
                EditorApplication.isPlaying = false;
            }
            else if (stage == 4 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                GameObject root = GameObject.Find("PlayCharacter");
                if (SessionState.GetBool("HoCombine.Validation.Prebaked", false))
                    Check(root.GetComponentsInChildren<ParentConstraint>(true).Length == 3,
                        "prebaked constraints remain intact on returning to edit mode");
                else
                    Check(root.GetComponentInChildren<ParentConstraint>(true) == null &&
                        root.GetComponentInChildren<HoCombineArmature>(true) != null,
                        "exiting constraints Play removes generated constraints and restores authoring");
                EditorSettings.enterPlayModeOptionsEnabled = false;
                SessionState.SetInt(StageKey, 0);
                EditorApplication.update -= Monitor;
                int passed = SessionState.GetInt(PassedKey, 0);
                File.WriteAllText("combine-armature-result.txt", "PASS " + passed + " Unity " + Application.unityVersion);
                Debug.Log("HO_COMBINE_ALL_PASSED " + passed + " Unity " + Application.unityVersion);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static Fixture Create()
    {
        var f = new Fixture { root = new GameObject("Character") };
        f.rig = Child(f.root.transform, "Rig", Vector3.zero);
        f.hips = Child(f.rig, "Hips", Vector3.up);
        f.chest = Child(f.hips, "Chest", Vector3.up * 0.5f);
        f.outfit = Child(Child(f.root.transform, "Outfits", Vector3.zero), "A", Vector3.zero);
        f.source = Child(f.outfit, "Outfits_A", Vector3.zero);
        f.sourceHips = Child(f.source, "Hips", Vector3.up);
        f.sourceChest = Child(f.sourceHips, "Chest", Vector3.up * 0.5f);
        f.skirt = Child(f.sourceHips, "Skirt", Vector3.down * 0.1f);
        f.tip = Child(f.skirt, "Tip", Vector3.down * 0.5f);
        f.renderer = Child(f.outfit, "Mesh", Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
        f.mesh = new Mesh { name = "OutfitMesh" };
        f.mesh.vertices = new[] { new Vector3(-0.1f, 1.5f, 0), new Vector3(0.1f, 1.5f, 0),
            new Vector3(-0.1f, 0.4f, 0), new Vector3(0.1f, 0.4f, 0) };
        f.mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
        f.mesh.boneWeights = new[] { Weight(0), Weight(0), Weight(1), Weight(1) };
        f.mesh.bindposes = new[] { f.sourceChest.worldToLocalMatrix * f.renderer.transform.localToWorldMatrix,
            f.tip.worldToLocalMatrix * f.renderer.transform.localToWorldMatrix };
        f.mesh.RecalculateNormals();
        f.mesh.RecalculateBounds();
        f.renderer.sharedMesh = f.mesh;
        f.renderer.bones = new[] { f.sourceChest, f.tip };
        f.renderer.rootBone = f.sourceHips;
        f.renderer.probeAnchor = f.sourceChest;
        f.renderer.updateWhenOffscreen = true;
        f.references = f.root.AddComponent<CombineArmatureTestReferences>();
        f.references.bone = f.sourceChest;
        f.references.boneObject = f.sourceHips.gameObject;
        f.references.bones = new[] { f.sourceChest, f.tip };
        f.references.sourceRoot = f.source;
        f.config = f.source.gameObject.AddComponent<HoCombineArmature>();
        f.config.targetArmature = f.rig;
        f.config.characterRoot = f.root.transform;
        return f;
    }

    private static Transform Child(Transform parent, string name, Vector3 localPosition)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = localPosition;
        return child;
    }

    private static BoneWeight Weight(int index) => new BoneWeight { boneIndex0 = index, weight0 = 1f };

    private static void ExpectFailure(Fixture f, string name)
    {
        bool rejected = false;
        try { HoCombineArmatureProcessor.ProcessHierarchy(f.root); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && f.sourceChest != null && f.sourceChest.parent == f.sourceHips && f.config != null, name);
    }

    private static float VertexError(Mesh a, Mesh b)
    {
        float error = 0;
        Vector3[] av = a.vertices, bv = b.vertices;
        for (int i = 0; i < av.Length; i++) error = Mathf.Max(error, Vector3.Distance(av[i], bv[i]));
        return error;
    }

    private static float MatrixError(Matrix4x4 a, Matrix4x4 b)
    {
        float error = 0;
        for (int i = 0; i < 16; i++) error = Mathf.Max(error, Mathf.Abs(a[i] - b[i]));
        return error;
    }

    private sealed class MeshScope : IDisposable
    {
        public readonly Mesh mesh = new Mesh();
        public void Dispose() => Object.DestroyImmediate(mesh);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("HO_COMBINE_FAILED: " + message);
        SessionState.SetInt(PassedKey, SessionState.GetInt(PassedKey, 0) + 1);
        Debug.Log("HO_COMBINE_PASS " + message);
    }

    private static void Fail(Exception exception)
    {
        SessionState.SetInt(StageKey, 0);
        EditorApplication.update -= Monitor;
        File.WriteAllText("combine-armature-result.txt", exception.ToString());
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }
}
