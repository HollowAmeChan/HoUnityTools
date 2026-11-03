using System;
using System.Collections.Generic;
using System.IO;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using UnityEditor;
using UnityEngine;

public static class HoVtsJellyEyeValidation
{
    private static int assertions;
    private const float Dt = 1f / 60f;
    private const string PlayPending = "HoVtsJellyEyeValidation.PlayPending";
    private static readonly string[] InputKeys = { "BlinkL", "BlinkR", "LookLeft", "LookRight", "LookUp", "LookDown" };
    private static readonly string[] OutputKeys = { "Right", "Left", "Up", "Down", "Large", "Small", "Tall", "Flat" };

    public static void RunBatch()
    {
        try
        {
            RequireDisposableProject();
            ValidateSpring();
            ValidateDirectionsAndRest();
            ValidateOptionalKeysAndSettings();
            ValidateOwnership();
            ValidateLifecycle();
            ValidateMeshChanges();
            ValidateThreeLayers();
            ValidateAuthoring();
            Debug.Log("HO_VTS_JELLY PASS assertions=" + assertions);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunPlayMode()
    {
        RequireDisposableProject();
        SessionState.SetBool(PlayPending, true);
        EditorApplication.isPlaying = true;
    }

    private static void RequireDisposableProject()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!File.Exists(Path.Combine(project, ".ho-vts-jelly-validation"))
            && !File.Exists(Path.Combine(project, ".ho-vts-ir-validation")))
            throw new InvalidOperationException("Disposable jelly project marker missing.");
    }

    [InitializeOnLoadMethod]
    private static void ResumePlayMode() => EditorApplication.update += StartPlayModeProbe;

    private static void StartPlayModeProbe()
    {
        if (!SessionState.GetBool(PlayPending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        SessionState.EraseBool(PlayPending);
        try
        {
            var f = new Fixture();
            f.ir.AutoRun = true;
            f.receiver.InputTimeoutSeconds = 0.08f;
            var pupil = f.AddLayer("");
            var h1 = f.AddLayer("H1");
            var h2 = f.AddLayer("H2");
            pupil.AutoRun = h1.AutoRun = h2.AutoRun = true;
            var probe = f.root.AddComponent<HoVtsJellyEyeFrameProbe>();
            probe.receiver = f.receiver; probe.ir = f.ir; probe.layers = new[] { pupil, h1, h2 }; probe.face = f.face;
            probe.Assert = Check;
            probe.Failed = error => { Debug.LogException(error); EditorApplication.Exit(1); };
            probe.Completed = () =>
            {
                Debug.Log("HO_VTS_JELLY_PLAYMODE PASS assertions=" + assertions);
                EditorApplication.Exit(0);
            };
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ValidateAuthoring()
    {
        string folder = "Assets/JellyAuthoring_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        try
        {
            using (var f = new Fixture())
            {
                AssetDatabase.CreateAsset(f.face.sharedMesh, folder + "/Face.asset");
                AssetDatabase.CreateAsset(f.secondary.sharedMesh, folder + "/Secondary.asset");
                AssetDatabase.CreateAsset(f.unlisted.sharedMesh, folder + "/Unlisted.asset");
                string irPath = folder + "/Role.hoface.ir.json";
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", irPath)), f.ir.IrAsset.text);
                AssetDatabase.ImportAsset(irPath, ImportAssetOptions.ForceSynchronousImport);
                f.ir.IrAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(irPath);
                var pupil = f.AddLayer("");
                var h1 = f.AddLayer("H1");
                var h2 = f.AddLayer("H2");
                pupil.Scale.response.strength = 0.42f;
                pupil.Scale.response.damping = 0.6f;
                pupil.Scale.enlarge = "";
                var editor = UnityEditor.Editor.CreateEditor(pupil);
                Check(editor.GetType().Name == "HoVtsJellyEyeEditor", "Unity selects the native Chinese jelly Inspector");
                UnityEngine.Object.DestroyImmediate(editor);
                var serialized = new SerializedObject(pupil);
                var strength = serialized.FindProperty("scale.response.strength");
                Check(strength != null && serialized.FindProperty("movement.response.damping") != null,
                    "Inspector can address independent serialized response settings");
                Undo.IncrementCurrentGroup();
                strength.floatValue = 0.9f;
                serialized.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Near(pupil.Scale.response.strength, 0.9f, "Inspector property changes apply natively");
                Undo.PerformUndo();
                Near(pupil.Scale.response.strength, 0.42f, "Inspector edits support Unity Undo");
                Undo.PerformRedo();
                Near(pupil.Scale.response.strength, 0.9f, "Inspector edits support Unity Redo");
                string prefabPath = folder + "/Character.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(f.root, prefabPath);
                Check(prefab != null, "three-layer authoring fixture saves as a native prefab");
                var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var layers = clone.GetComponents<HoVtsJellyEye>();
                    var cloneIr = clone.GetComponent<HoVtsIrController>();
                    Check(layers.Length == 3, "prefab retains three separate instances of the same component type");
                    Check(layers[0].IrController == cloneIr && layers[1].IrController == cloneIr && layers[2].IrController == cloneIr,
                        "prefab retains internal references to its own IR instance");
                    Check(layers[0].TargetMeshes.Count == 2 && layers[0].TargetMeshes[0].transform.IsChildOf(clone.transform),
                        "prefab renderer lists refer to the cloned role");
                    Check(layers[0].Scale.enlarge == "" && layers[0].Inputs.blinkKeys.Count == 2,
                        "prefab preserves optional directions and explicit input names");
                    Near(layers[0].Scale.response.strength, 0.9f, "prefab preserves per-effect intensity");
                    Near(layers[0].Scale.response.damping, 0.6f, "prefab preserves per-effect damping");
                    Check(!layers[0].AutoRun && !cloneIr.AutoRun, "AutoRun remains serialized independently of enabled state");
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
        }
        finally { AssetDatabase.DeleteAsset(folder); }
    }

    private static void ValidateSpring()
    {
        var response = new HoVtsJellyResponse { inputSmoothing = 0 };
        var spring = new HoVtsJellySpring();
        spring.Step(0.8f, Dt, response);
        Near(spring.Offset, 0, "nonzero first input does not inject an activation impulse");
        spring.Reset(0);
        spring.Step(1, Dt, response);
        Check(spring.Offset < 0, "positive input creates initial inertial opposition");
        bool rebounded = false;
        for (int i = 0; i < 300; i++) { spring.Step(1, Dt, response); rebounded |= spring.Offset > 0.01f; }
        Check(rebounded && spring.IsSettled, "signed spring rebounds and settles under a held input");
        Near(spring.Offset, 0, "held nonzero input has exact zero residual");

        foreach (float frequency in new[] { 2f, 6f, 30f })
        foreach (float damping in new[] { 0.05f, 0.35f, 1f, 2f })
        foreach (float fps in new[] { 15f, 30f, 60f, 144f })
        {
            response.frequency = frequency; response.damping = damping;
            spring.Reset(0);
            float peak = 0;
            for (int i = 0; i < fps * 25; i++)
            {
                spring.Step(1, 1f / fps, response);
                peak = Mathf.Max(peak, Mathf.Abs(spring.Offset));
            }
            Check(!float.IsNaN(peak) && !float.IsInfinity(peak) && peak < 2,
                "bounded spring at " + frequency + " Hz / damping " + damping + " / " + fps + " fps");
            Near(spring.Offset, 0, "no accumulated rest drift across solver parameter ranges");
        }
        response.frequency = 6; response.damping = 0.35f;
        spring.Reset(0); spring.Step(1, Dt, response);
        float before = spring.Offset;
        spring.Step(0, 0, response); spring.Step(0, float.NaN, response);
        Near(spring.Offset, before, "invalid or zero elapsed time never advances the spring");
        spring.Step(0.7f, 2, response);
        Near(spring.Offset, 0, "long pause reinitializes at current input");
        spring.Reset(0); spring.Step(1, Dt, response);
        response.strength = 0;
        Near(spring.WeightOffset(response), 0, "zero strength only suppresses output");
        Check(Mathf.Abs(spring.Offset) > 0.01f, "strength does not erase signed simulation state");
        response.strength = 2; response.maxAmplitude = 7;
        Check(Mathf.Abs(spring.WeightOffset(response)) <= 7, "maximum amplitude bounds the output independently");
    }

    private static void ValidateDirectionsAndRest()
    {
        using (var f = new Fixture())
        {
            var jelly = f.AddLayer("");
            f.Apply(); Check(jelly.Step(Dt), "component initializes from a completed IR output");
            Near(f.Weight("Left"), 7, "nonzero output neutral is retained");
            f.Apply(blink: 1); Check(jelly.Step(Dt), "blink excites all enabled effect groups");
            Check(f.Weight("Small") > 0 && f.Weight("Flat") > 0 && f.Weight("Down") > 0,
                "closing blink initially shrinks, squashes and displaces the layer");
            Check(HoFaceOutputOwnership.IsReservedBy(f.face, f.Index("Small"), jelly),
                "jelly owns only its explicit output keys");
            Check(HoFaceOutputOwnership.IsReservedBy(f.face, f.Index("BlinkL"), f.ir),
                "IR retains its input shape ownership");
            Near(f.Weight("Unrelated"), 51, "unrelated shape keys remain untouched");
            Near(f.unlisted.GetBlendShapeWeight(f.Index("Small")), 0, "unlisted renderer is never scanned or written");
            Near(f.secondary.GetBlendShapeWeight(f.Index("Small")), f.Weight("Small"), "same-name output fans out across explicit mesh list");
            bool large = false, tall = false;
            for (int i = 0; i < 300; i++)
            {
                f.Apply(blink: 1); jelly.Step(Dt);
                large |= f.Weight("Large") > 0.1f; tall |= f.Weight("Tall") > 0.1f;
            }
            Check(large && tall, "opposite output keys receive the rebound half-cycle");
            f.CheckNeutral("holding eyes closed does not leave persistent jelly deformation");
            f.Apply(blink: 1, left: 1); jelly.Step(Dt);
            Check(f.Weight("Right") > 0, "horizontal direction uses final opposing IR shape keys");
            for (int i = 0; i < 300; i++) { f.Apply(blink: 1, left: 1); jelly.Step(Dt); }
            f.CheckNeutral("held sideways gaze also returns to the neutral overlay");
            ulong revision = f.ir.OutputRevision;
            f.face.SetBlendShapeWeight(f.Index("LookLeft"), 0);
            jelly.Step(Dt);
            Near(jelly.MovementOffset.x, 0, "downstream physical mutations are never fed back as source motion");
            Check(f.ir.OutputRevision == revision, "a jelly consumer never evaluates IR");
        }
    }

    private static void ValidateOptionalKeysAndSettings()
    {
        using (var f = new Fixture())
        {
            var jelly = f.AddLayer("");
            jelly.Movement.left = jelly.Movement.right = jelly.Movement.up = jelly.Movement.down = "";
            jelly.Scale.enlarge = "";
            jelly.Squash.squash = jelly.Squash.stretch = "";
            f.Apply(); jelly.Step(Dt);
            Check(jelly.BindingCount == 2, "empty directions are skipped on each mesh");
            f.Apply(blink: 1); jelly.Step(Dt);
            Check(f.Weight("Small") > 0, "a one-sided mapping still receives its half-cycle");
            for (int i = 0; i < 300; i++) { f.Apply(blink: 1); jelly.Step(Dt); }
            Near(f.Weight("Small"), 0, "missing opposite direction does not prevent recentering");
            jelly.Scale.enlarge = "TypoOutput";
            f.Apply(); jelly.Step(Dt);
            Check(jelly.MissingOutputKeys.Count == 1, "named missing output is reported while existing directions still work");
            f.Apply(blink: 1); jelly.Step(Dt);
            jelly.Scale.response.strength = 0; jelly.Step(Dt);
            Near(f.Weight("Small"), 0, "strength zero clears a previously written output");
            jelly.Scale.response.strength = 2; jelly.Scale.response.maxAmplitude = 4;
            f.Apply(); jelly.Step(Dt);
            f.Apply(blink: 1); jelly.Step(Dt);
            Check(f.Weight("Small") <= 4, "per-effect amplitude limit applies to actual output");
            jelly.Inputs.blinkKeys.Add("TypoInput");
            f.Apply(); jelly.Step(Dt);
            Check(jelly.MissingInputKeys.Count == 1 && jelly.Status.Contains("TypoInput"),
                "missing configured input is diagnosed instead of silently inventing a signal");
        }
    }

    private static void ValidateOwnership()
    {
        using (var f = new Fixture())
        {
            var jelly = f.AddLayer("");
            f.Apply(); jelly.Step(Dt); f.Apply(blink: 1); jelly.Step(Dt);
            var duplicate = f.AddLayer("");
            Check(!duplicate.Step(Dt) && duplicate.Status.Contains("另一个"),
                "a second component cannot overwrite or release another layer");
            Check(f.Weight("Small") > 0 && HoFaceOutputOwnership.IsReservedBy(f.face, f.Index("Small"), jelly),
                "failed competitor cleanup leaves the owner and pose intact");
            duplicate.Stop();
            jelly.Stop();
            Check(duplicate.Step(Dt), "a blocked layer can acquire keys after the owner releases them");
            duplicate.Stop();
            jelly.Scale.enlarge = "BlinkL";
            Check(!jelly.Step(Dt) && jelly.Status.Contains("IR"),
                "IR targets are rejected as jelly outputs even at zero weight");
            Near(f.Weight("BlinkL"), 100, "IR conflict leaves actual face input unchanged");
            Check(HoFaceOutputOwnership.IsReservedBy(f.face, f.Index("BlinkL"), f.ir), "jelly cleanup preserves IR reservations");
            jelly.Scale.enlarge = "Large";
            jelly.Scale.shrink = "Large";
            Check(!jelly.Step(Dt) && jelly.Status.Contains("同一输出键"), "duplicate directions fail before writing a partial overlay");
            jelly.Scale.shrink = "Small";
            var outsideOwner = new object();
            HoFaceOutputOwnership.Reserve(f.face, f.Index("Flat"), outsideOwner);
            Check(!jelly.Step(Dt), "existing Ho writer reservations are also respected");
            Check(!HoFaceOutputOwnership.IsReserved(f.face, f.Index("Large")), "failed reservation releases only its own partial acquisition");
            HoFaceOutputOwnership.Release(outsideOwner);
            Check(jelly.Step(Dt), "layer recovers after an external reservation is removed");
        }
    }

    private static void ValidateLifecycle()
    {
        using (var f = new Fixture())
        {
            var jelly = f.AddLayer("");
            f.Apply(); jelly.Step(Dt); f.Apply(blink: 1); jelly.Step(Dt);
            f.receiver.ClearFrame();
            Check(!f.ir.HasOutputFrame, "receiver release immediately invalidates source snapshot");
            for (int i = 0; i < 300; i++) jelly.Step(Dt);
            f.CheckNeutral("tracking loss settles the existing overlay to neutral");
            Check(!HoFaceOutputOwnership.IsReserved(f.face, f.Index("Small")), "settled lost input hands back output ownership");
            f.Apply(blink: 1); jelly.Step(Dt);
            f.CheckNeutral("reacquiring nonzero input does not inject a reconnect impulse");
            f.Apply(); jelly.Step(Dt); f.Apply(blink: 1); jelly.Step(Dt);
            f.face.SetBlendShapeWeight(f.Index("Small"), 23);
            jelly.enabled = false;
            Near(f.Weight("Small"), 23, "disable preserves a newer external output write");
            Near(f.Weight("Left"), 7, "disable restores untouched captured neutrals");
            Check(!jelly.Step(Dt), "disabled component cannot manually write");
            jelly.enabled = true;
            f.Apply(blink: 1); jelly.Step(Dt);
            Near(f.Weight("Small"), 23, "reenabling captures the new external neutral");
            f.Apply(); jelly.Step(Dt);
            jelly.ResetSprings();
            Near(f.Weight("Small"), 23, "manual spring reset immediately restores neutral");
            f.ir.IrAsset = new TextAsset(f.ir.IrAsset.text);
            f.Apply(blink: 1); jelly.Step(Dt);
            Near(f.Weight("Small"), 23, "IR binding revision resets spring history");
            f.ir.enabled = false;
            for (int i = 0; i < 300; i++) jelly.Step(Dt);
            Near(f.Weight("Small"), 23, "disabled source does not leave an overlay stuck");
        }
    }

    private static void ValidateMeshChanges()
    {
        using (var f = new Fixture())
        {
            var jelly = f.AddLayer("");
            f.Apply(); jelly.Step(Dt); f.Apply(blink: 1); jelly.Step(Dt);
            jelly.TargetMeshes.Remove(f.secondary);
            jelly.Step(Dt);
            Near(f.secondary.GetBlendShapeWeight(f.Index("Small")), 0, "removing a renderer releases its previous output");
            var replacement = f.NewMesh("Replacement");
            int small = replacement.GetBlendShapeIndex("Small");
            f.face.sharedMesh = replacement;
            f.face.SetBlendShapeWeight(small, 19);
            f.Apply(blink: 1); jelly.Step(Dt);
            Near(f.face.GetBlendShapeWeight(small), 19, "mesh replacement rebinds without restoring old indices onto the new mesh");
            jelly.TargetMeshes.Clear();
            Check(!jelly.Step(Dt) && jelly.Status.Contains("网格"), "empty mesh list never falls back to scene scanning");
            Near(f.face.GetBlendShapeWeight(small), 19, "clearing mesh list releases the new mesh baseline");
        }
    }

    private static void ValidateThreeLayers()
    {
        using (var f = new Fixture())
        {
            var pupil = f.AddLayer("");
            var highlight1 = f.AddLayer("H1");
            var highlight2 = f.AddLayer("H2");
            highlight1.Scale.response.frequency = 4;
            highlight2.Scale.response.frequency = 10;
            highlight1.Scale.response.maxAmplitude = highlight2.Scale.response.maxAmplitude = 100;
            f.Apply();
            pupil.Step(Dt); highlight1.Step(Dt); highlight2.Step(Dt);
            f.Apply(blink: 1);
            ulong revision = f.ir.OutputRevision;
            Check(pupil.Step(Dt) && highlight1.Step(Dt) && highlight2.Step(Dt), "three independently configured components can coexist on one object");
            Check(f.ir.OutputRevision == revision, "three consumers share one completed upstream evaluation");
            Check(Mathf.Abs(f.Weight("H1Small") - f.Weight("H2Small")) > 0.1f, "independent response settings produce different highlight motion");
            pupil.Scale.response.strength = 0;
            pupil.Step(Dt);
            Near(f.Weight("Small"), 0, "one effect strength can be disabled independently");
            Check(f.Weight("Flat") > 0 && f.Weight("H1Small") > 0, "other effects and other layers remain active");
            for (int i = 0; i < 300; i++)
            {
                f.Apply(blink: 1);
                pupil.Step(Dt); highlight1.Step(Dt); highlight2.Step(Dt);
            }
            f.CheckNeutral("three layers all return to their own neutral");
            Near(f.Weight("H1Small"), 0, "first highlight returns to zero");
            Near(f.Weight("H2Small"), 0, "second highlight returns to zero");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject root;
        public readonly HoVtsParameterReceiver receiver;
        public readonly HoVtsIrController ir;
        public readonly SkinnedMeshRenderer face, secondary, unlisted;
        private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        private double time;

        public Fixture()
        {
            root = new GameObject("JellyCharacter");
            receiver = root.AddComponent<HoVtsParameterReceiver>();
            var middleware = new HoFaceMiddleware();
            foreach (string key in InputKeys) middleware.outputs.Add(new HoFaceOutput { parameter = key, expression = key });
            receiver.SetMiddleware(middleware);
            face = AddMesh("Face");
            secondary = AddMesh("FaceSecondary");
            unlisted = AddMesh("UnlistedFace");
            face.SetBlendShapeWeight(Index("Left"), 7);
            face.SetBlendShapeWeight(Index("Unrelated"), 51);
            secondary.SetBlendShapeWeight(Index("Left"), 7);
            var doc = new HoFaceIrDocument { id = "jelly-test", root = "root" };
            var direct = new HoFaceIrNode { id = "root", type = "direct" };
            doc.nodes.Add(direct);
            foreach (string key in InputKeys)
            {
                doc.parameters.Add(new HoFaceIrParameter { name = key, min = 0, max = 1 });
                doc.targets.Add(new HoFaceIrTarget { id = key, min = 0, max = 1 });
                var pose = new HoFaceIrNode { id = key, type = "pose" };
                pose.weights.Add(new HoFaceIrWeight { target = key, value = 1 });
                doc.nodes.Add(pose);
                direct.children.Add(new HoFaceIrChild { node = key, weightParameter = key });
            }
            ir = root.AddComponent<HoVtsIrController>();
            ir.AutoRun = false; ir.ParameterReceiver = receiver;
            ir.TargetMeshes.Add(face);
            ir.IrAsset = new TextAsset(doc.Write());
            resources.Add(ir.IrAsset);
        }

        public Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (string key in InputKeys) mesh.AddBlendShapeFrame(key, 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            foreach (string prefix in new[] { "", "H1", "H2" })
                foreach (string key in OutputKeys) mesh.AddBlendShapeFrame(prefix + key, 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            mesh.AddBlendShapeFrame("Unrelated", 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            resources.Add(mesh);
            return mesh;
        }

        private SkinnedMeshRenderer AddMesh(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = NewMesh(name);
            return renderer;
        }

        public HoVtsJellyEye AddLayer(string prefix)
        {
            var jelly = root.AddComponent<HoVtsJellyEye>();
            jelly.AutoRun = false; jelly.IrController = ir;
            jelly.TargetMeshes.Add(face); jelly.TargetMeshes.Add(secondary);
            jelly.Inputs.blinkKeys.Add("BlinkL"); jelly.Inputs.blinkKeys.Add("BlinkR");
            jelly.Inputs.lookLeft = "LookLeft"; jelly.Inputs.lookRight = "LookRight";
            jelly.Inputs.lookUp = "LookUp"; jelly.Inputs.lookDown = "LookDown";
            jelly.Movement.left = prefix + "Left"; jelly.Movement.right = prefix + "Right";
            jelly.Movement.up = prefix + "Up"; jelly.Movement.down = prefix + "Down";
            jelly.Scale.enlarge = prefix + "Large"; jelly.Scale.shrink = prefix + "Small";
            jelly.Squash.squash = prefix + "Flat"; jelly.Squash.stretch = prefix + "Tall";
            jelly.Movement.response.inputSmoothing = jelly.Scale.response.inputSmoothing = jelly.Squash.response.inputSmoothing = 0;
            return jelly;
        }

        public void Apply(float blink = 0, float left = 0, float right = 0)
        {
            time += Dt;
            receiver.ApplyRawFrame(new Dictionary<string, float> { { "BlinkL", blink }, { "BlinkR", blink },
                { "LookLeft", left }, { "LookRight", right }, { "LookUp", 0 }, { "LookDown", 0 } }, true, time);
            Check(ir.EvaluateNow(), "fixture IR writes the current frame");
        }

        public int Index(string key) => face.sharedMesh.GetBlendShapeIndex(key);
        public float Weight(string key) => face.GetBlendShapeWeight(Index(key));
        public void CheckNeutral(string label)
        {
            foreach (string key in OutputKeys) Near(Weight(key), key == "Left" ? 7 : 0, label + " / " + key);
        }
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            foreach (var resource in resources)
                if (resource != null && !EditorUtility.IsPersistent(resource)) UnityEngine.Object.DestroyImmediate(resource);
        }
    }

    private static void Near(float actual, float expected, string label) =>
        Check(Mathf.Abs(actual - expected) < 0.01f, label + ": " + actual + " vs " + expected);
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
}
