#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Hollow.HoUnityTools.Editor.MmdMotion;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VmdAuthoringValidation
{
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> failures = new List<string>();
    public static void Run()
    {
        passed.Clear(); failures.Clear();
        string folder = "Assets/__HoVmdAuthoringValidation_" + Guid.NewGuid().ToString("N");
        GameObject synthetic = null, character = null;
        HoVmdAnimation animation = null;
        var preview = EditorSceneManager.NewPreviewScene();
        var active = SceneManager.GetActiveScene();
        bool wasDirty = active.isDirty;
        try
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.ImportAsset(folder, ImportAssetOptions.ForceSynchronousImport);
            synthetic = new GameObject("Authoring validation");
            SceneManager.MoveGameObjectToScene(synthetic, preview);
            Transform main = Child(synthetic.transform, "Main"), outfit = Child(synthetic.transform, "Outfit");
            Transform hips = Child(main, "hips"), leg = Child(hips, "upper_leg.L"), knee = Child(leg, "lower_leg.L");
            Transform ankle = Child(knee, "foot.L"), toe = Child(ankle, "toes.L");
            Child(outfit, "upper_leg.L");
            Transform controls = Child(synthetic.transform, "Controls"), goal = Child(controls, "左足ＩＫ");
            var ik = Child(controls, "Foot constraint").gameObject.AddComponent<HoIkConstraint>();
            ik.IkName = "左足ＩＫ"; ik.Target = goal; ik.Effector = ankle;
            animation = ScriptableObject.CreateInstance<HoVmdAnimation>();
            animation.name = "Authoring Motion";
            foreach (string name in new[] { "左足", "左ひざ", "左足首", "左つま先", "左足IK", "下半身", "センター", "RootTrack" })
                animation.boneFrames.Add(new HoVmdBoneFrame { boneName = name, rotation = Quaternion.identity });
            animation.showIkFrames.Add(new HoVmdShowIkFrame { toggles = new[] { new HoVmdIkToggle { boneName = "左足IK", enabled = false } } });
            var profile = HoVmdBindingProfileBuilder.Build(animation, synthetic, folder + "/Synthetic.asset", main);
            Check(profile.TryBone("左足", out string legPath) && synthetic.transform.Find(legPath) == leg, "Main skeleton alias mapping excludes duplicate outfit bones");
            Check(profile.TryBone("左ひざ", out string kneePath) && synthetic.transform.Find(kneePath) == knee, "Japanese knee maps to lower_leg.L");
            Check(profile.TryBone("左足首", out string footPath) && synthetic.transform.Find(footPath) == ankle, "Ankle FK maps to foot.L");
            Check(profile.TryBone("左つま先", out string toePath) && synthetic.transform.Find(toePath) == toe, "Toe FK maps to toes.L");
            Check(profile.TryBone("左足IK", out string ikPath) && synthetic.transform.Find(ikPath) == goal, "ASCII IK track maps to full-width Sour controller name");
            Check(profile.ik.Any(x => x.sourceName == "左足IK") && profile.ik.Any(x => x.sourceName == "左足ＩＫ"), "IK switch profile entries preserve actual VMD and Sour names");
            Check(!profile.TryBone("センター", out _), "Unmapped center is not silently collapsed into hips");

            // A configured Profile must override a same-name automatic candidate.
            var alternate = Child(synthetic.transform, "Mapped override");
            Child(synthetic.transform, "OverrideTrack");
            profile.bones.Add(new HoVmdBindingProfile.BoneMap { sourceName = "OverrideTrack", targetPath = "Mapped override" });
            profile.bones.Add(new HoVmdBindingProfile.BoneMap { sourceName = "RootTrack", targetPath = "." });
            animation.boneFrames.Add(new HoVmdBoneFrame { boneName = "OverrideTrack", rotation = Quaternion.identity, position = Vector3.one });
            var player = synthetic.AddComponent<HoVmdMotionPlayer>();
            player.Animation = animation; player.ModelRoot = synthetic.transform; player.BindingProfile = profile;
            player.RebuildBindings();
            var auto = (Dictionary<string, Transform>)typeof(HoVmdMotionPlayer).GetField("autoBones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(player);
            Check(auto["OverrideTrack"] == alternate, "Profile bone override wins over automatic same-name candidate");
            Check(auto["RootTrack"] == synthetic.transform, "Profile dot path resolves model root");
            var invalid = new GameObject("External skeleton");
            SceneManager.MoveGameObjectToScene(invalid, preview);
            bool refused = false;
            try { HoVmdBindingProfileBuilder.Build(animation, synthetic, folder + "/Invalid.asset", invalid.transform); }
            catch (ArgumentException) { refused = true; }
            finally { Object.DestroyImmediate(invalid); }
            Check(refused, "Profile builder refuses a skeleton outside character");

            string vmdPath = AssetDatabase.FindAssets("t:HoVmdAnimation").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(x => AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(x)?.boneFrames.Count > 0);
            if (vmdPath == null) throw new InvalidOperationException("Real imported character VMD required for integration validation.");
            var real = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(vmdPath);
            var importer = AssetImporter.GetAtPath(vmdPath);
            var importerEditor = UnityEditor.Editor.CreateEditor(importer);
            Check(importerEditor is HoVmdScriptedImporterEditor, "Unity selects the interactive VMD importer Inspector");
            Check(importerEditor is AssetImporterEditor assetEditor && !assetEditor.showImportedObject, "Read-only imported-object Inspector suppressed");
            Object.DestroyImmediate(importerEditor);
            var playerEditor = UnityEditor.Editor.CreateEditor(player);
            Check(playerEditor.GetType().Name == "HoVmdMotionPlayerEditor", "Unity selects Chinese playback and Bone Bindings Inspector");
            Object.DestroyImmediate(playerEditor);
            var profileEditor = UnityEditor.Editor.CreateEditor(profile);
            Check(profileEditor.GetType().Name == "HoVmdBindingProfileEditor", "Unity selects Chinese profile Inspector");
            Object.DestroyImmediate(profileEditor);

            const string wtd = "Assets/Hollow/WTD-summer/WTD-BUILD.prefab";
            if (File.Exists(wtd))
            {
                string hash = Hash(wtd);
                character = PrefabUtility.LoadPrefabContents(wtd);
                var skeleton = character.transform.Find("Armature-Swimsuit");
                Check(skeleton != null, "Real WTD main skeleton exists");
                var wtdProfile = HoVmdBindingProfileBuilder.Build(real, character, folder + "/WTD.asset", skeleton);
                var mapped = wtdProfile.bones.Select(x => new { x.sourceName, transform = character.transform.Find(x.targetPath) }).ToArray();
                Check(mapped.Any(x => x.transform != null && x.transform.IsChildOf(skeleton)), "Real VMD generates WTD main-skeleton mappings");
                Check(mapped.All(x => x.transform == null || !PathOf(x.transform, character.transform).Contains("Armature-Miko")), "WTD mappings do not select the merged outfit skeleton");
                var result = HoVmdMorphAudit.Audit(real, character, wtdProfile);
                HoVmdMorphAudit.WriteJson(real, character, wtdProfile, "Library/HoMmdStages/vmd-authoring-real-morphs.json");
                Check(result.required == real.morphFrames.Select(x => x.morphName).Distinct().Count(), "Real VMD morph audit covers all source morph tracks");
                Check(Hash(wtd) == hash, "WTD prefab bytes unchanged by binding and audit");
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); Debug.LogException(error); }
        finally
        {
            if (character != null) PrefabUtility.UnloadPrefabContents(character);
            if (synthetic != null) Object.DestroyImmediate(synthetic);
            if (animation != null) Object.DestroyImmediate(animation);
            EditorSceneManager.ClosePreviewScene(preview);
            AssetDatabase.DeleteAsset(folder);
            Check(SceneManager.GetActiveScene() == active && active.isDirty == wasDirty, "Existing scene and dirty-state preserved");
            Directory.CreateDirectory("Library/HoMmdStages");
            File.WriteAllText("Library/HoMmdStages/vmd-authoring-validation.json", JsonUtility.ToJson(new Report { passed = passed.ToArray(), failures = failures.ToArray() }, true));
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        Debug.Log("HO_VMD_AUTHORING_VALIDATION_PASS=" + passed.Count);
    }
    private static Transform Child(Transform parent, string name) { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
    private static void Check(bool condition, string message) { (condition ? passed : failures).Add(message); }
    private static string Hash(string file) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file))); }
    private static string PathOf(Transform t, Transform root) => AnimationUtility.CalculateTransformPath(t, root);
    [Serializable] private sealed class Report { public string[] passed, failures; }
}
#endif
