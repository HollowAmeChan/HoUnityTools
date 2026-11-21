using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;
using Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR;

internal static class ReaderValidation
{
    private static int checks;
    private static HoConstraintIrV4Registry registry;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (FormatException) { rejected = true; } Check(rejected, message); }
    private static string Hash = new string('0', 64);
    private const string Identity = "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]";
    private static HoConstraintJsonValue Json(string value) { return HoConstraintJson.Parse(value); }
    private static HoConstraintJsonValue BaseDocument()
    {
        var root = Json("{\"schema\":\"hotools.rig-constraint-ir\",\"schemaVersion\":4,\"armatureName\":\"Rig\",\"exportTime\":\"2026-11-21T00:00:00Z\",\"blenderVersion\":\"4.5.8\",\"captureStage\":\"AUTHORING\",\"registry\":{\"id\":\"hotools.rig-constraints.registry\",\"version\":1,\"sha256\":\"" + registry.Sha256 + "\"},\"sources\":[],\"semantics\":[],\"implementationBindings\":[],\"inputFingerprint\":\"" + Hash + "\",\"bindings\":{\"captureStage\":\"POSTPROCESS\",\"boneMap\":[],\"constraintMap\":[],\"bindingContext\":{\"space\":\"BLENDER_ARMATURE\",\"matrixLayout\":\"ROW_MAJOR\",\"fbxBoneAxes\":\"Y_X\",\"objectMatrix\":" + Identity + ",\"bones\":[]}}}");
        foreach (string name in new[] { "Owner", "Target", "Other", "MCH_Target" })
            root.Get("bindings").Get("bindingContext").Get("bones").Items.Add(Json("{\"boneName\":\"" + name + "\",\"parentBone\":\"\",\"inheritScale\":\"FULL\",\"restMatrix\":" + Identity + ",\"poseBasis\":" + Identity + ",\"connected\":false,\"positionOnly\":false,\"hasPoseAnimation\":false}"));
        return root;
    }
    private static HoConstraintJsonValue Source(string id, string type, int index, string target = "Target", string extra = "")
    { return Json("{\"sourceId\":\"" + id + "\",\"ownerBone\":\"Owner\",\"ownerKind\":\"BONE\",\"captureStage\":\"AUTHORING\",\"constraint\":{\"stackIndex\":" + index + ",\"name\":\"" + id + "\",\"constraintType\":\"" + type + "\",\"targetObjectName\":\"Rig\",\"targetBoneName\":\"" + target + "\",\"parameters\":{\"influence\":1.0,\"mute\":false,\"owner_space\":\"WORLD\",\"target_space\":\"WORLD\",\"mix_mode\":\"REPLACE\",\"use_x\":true,\"use_y\":true,\"use_z\":true,\"invert_x\":false,\"invert_y\":false,\"invert_z\":false,\"use_offset\":false,\"head_tail\":0.0,\"use_bbone_shape\":false" + extra + "},\"references\":{}}}"); }
    private static void Semantic(HoConstraintJsonValue root, string id, string type, string[] sourceIds, string parameters = "{}", string evidence = "GENERATED")
    {
        string rule = type == "COPY_ROTATION" ? "group.copy_r" : type == "COPY_LOCATION" ? "direct.copy_location" : type == "ARMATURE" ? "direct.armature" : "group." + type.ToLowerInvariant();
        string ids = string.Join(",", sourceIds.Select(x => "\"" + x + "\"").ToArray());
        root.Get("semantics").Items.Add(Json("{\"semanticId\":\"" + id + "\",\"semanticType\":\"" + type + "\",\"semanticVersion\":1,\"ownerBone\":\"Owner\",\"sourceIds\":[" + ids + "],\"ruleId\":\"" + rule + "\",\"ruleVersion\":1,\"evidence\":\"" + evidence + "\",\"parameters\":" + parameters + "}"));
    }
    private static void Bind(HoConstraintJsonValue root, string semantic, string implementation, string backend = "HoAux", string policy = "EXACT_ONLY", string selection = "CANDIDATE")
    { root.Get("implementationBindings").Items.Add(Json("{\"semanticId\":\"" + semantic + "\",\"backendId\":\"" + backend + "\",\"implementationId\":\"" + implementation + "\",\"contractVersion\":1,\"selection\":\"" + selection + "\",\"fallbackPolicy\":\"" + policy + "\"}")); }
    private static HoConstraintIrV4Document Read(HoConstraintJsonValue root)
    { var unverified = HoConstraintIrV4Reader.Read(HoConstraintJson.Serialize(root)); root.Properties["inputFingerprint"] = HoConstraintJsonValue.String(unverified.ComputeInputFingerprint()); var document = HoConstraintIrV4Reader.Read(HoConstraintJson.Serialize(root)); document.ValidateInputFingerprint(); registry.Validate(document); return document; }
    private static HoConstraintIrV4LegacyProjection Project(HoConstraintJsonValue root, string backend = "HoAux")
    { return HoConstraintIrV4LegacyAdapter.Project(Read(root), registry, backend); }
    private static HoConstraintJsonValue CopyFixture()
    { var root = BaseDocument(); root.Get("sources").Items.Add(Source("c0", "COPY_ROTATION", 0)); Semantic(root, "s0", "COPY_ROTATION", new[] { "c0" }, "{\"auxType\":\"COPY_R\"}"); Bind(root, "s0", "hoaux.copy_rotation.world"); return root; }
    public static int Main(string[] args)
    {
        try
        {
            registry = HoConstraintIrV4Registry.Load(File.ReadAllBytes(args[0]));
            if (args.Length > 2 && args[1] == "--emit") { File.WriteAllText(args[2], Read(CopyFixture()).ToJson(), new System.Text.UTF8Encoding(false)); Console.WriteLine("EMITTED " + args[2]); return 0; }
            TestJson(); TestReaderAndRegistry(); TestProjection(); TestComposites(); TestLocalAuthoringDomain(); TestRigSpaceContract();
            if (args.Length > 1)
            {
                var imported = HoConstraintIrV4Import.Read(File.ReadAllText(args[1]), File.ReadAllText(args[0]));
                Check(imported.ComputeInputFingerprint() == imported.Root.StringAt("inputFingerprint"), "Python/C# input fingerprint differs");
                var projected = HoConstraintIrV4LegacyAdapter.Project(imported, registry, "HoAux");
                Check(projected.Document.Sources.Count == imported.Sources.Count, "Projection lost authoritative source facts");
            }
            Console.WriteLine("CONSTRAINT_IR_V4_READER_PASS " + checks + " checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void TestJson()
    {
        string raw = "{\"future\":{\"number\":123456789012345678901234567890,\"tiny\":1.25e-300,\"array\":[null,true,\"裙子😀\\n\"]},\"𐀀\":1,\"\":2}";
        string roundtrip = HoConstraintJson.Serialize(Json(raw)); Check(roundtrip == raw, "DOM lost unknown fields or number tokens");
        string sorted = HoConstraintJson.Serialize(Json(raw), true); Check(sorted.IndexOf("", StringComparison.Ordinal) < sorted.IndexOf("𐀀", StringComparison.Ordinal), "Key sort must use Unicode code points");
        foreach (string invalid in new[] { "{\"a\":1,\"a\":2}", "[1,]", "01", "-", "1e", "{\"x\":NaN}", "\"\\ud800\"", "true false", "\"a\nb\"" }) Reject(() => Json(invalid), "Accepted invalid JSON " + invalid);
    }
    private static void TestReaderAndRegistry()
    {
        var root = CopyFixture(); root.Properties["future"] = Json("{\"unseen\":[1.234567890123456789,\"😀\",null]}");
        var document = Read(root); Check(document.ToJson().Contains("1.234567890123456789"), "Lost future numeric precision");
        document.Root.Get("sources").Items[0].Get("constraint").Get("parameters").Properties["influence"] = Json("0.3"); Reject(() => document.ValidateInputFingerprint(), "Accepted stale fingerprint");
        var duplicate = CopyFixture(); duplicate.Get("sources").Items.Add(duplicate.Get("sources").Items[0].Clone()); Reject(() => Read(duplicate), "Accepted duplicate source ID");
        var missing = CopyFixture(); missing.Get("semantics").Items[0].Get("sourceIds").Items[0] = HoConstraintJsonValue.String("missing"); Reject(() => Read(missing), "Accepted missing source reference");
        var contract = CopyFixture(); contract.Get("implementationBindings").Items[0].Properties["contractVersion"] = Json("2"); Reject(() => Read(contract), "Accepted unknown contract version");
        var unknown = CopyFixture(); unknown.Get("implementationBindings").Items[0].Properties["implementationId"] = HoConstraintJsonValue.String("hoaux.arbitrary.command"); Reject(() => Read(unknown), "Accepted unknown implementation");
        var staleRegistry = CopyFixture(); staleRegistry.Get("registry").Properties["sha256"] = HoConstraintJsonValue.String(Hash); Reject(() => Read(staleRegistry), "Accepted registry mismatch");
        var wrongType = CopyFixture(); wrongType.Get("sources").Items[0].Get("constraint").Properties["constraintType"] = HoConstraintJsonValue.String("COPY_SCALE"); Reject(() => Read(wrongType), "Accepted semantic/source type mismatch");
        var rawCopy = CopyFixture(); rawCopy.Get("semantics").Items[0].Properties["evidence"] = HoConstraintJsonValue.String("RAW_TYPE"); Reject(() => Read(rawCopy), "Unregistered raw copyR accepted as semantic");
        var wrongStage = CopyFixture(); wrongStage.Get("bindings").Properties["captureStage"] = HoConstraintJsonValue.String("AUTHORING"); Reject(() => Read(wrongStage), "Authoring binding overlay accepted");
        var lockOverlap = CopyFixture(); Semantic(lockOverlap, "duplicate", "COPY_ROTATION", new[] { "c0" }, "{\"auxType\":\"COPY_R\"}"); Bind(lockOverlap, "duplicate", "hoaux.copy_rotation.world", "HoAux", "EXACT_ONLY", "LOCKED"); lockOverlap.Get("implementationBindings").Items[0].Properties["selection"] = HoConstraintJsonValue.String("LOCKED"); Reject(() => Read(lockOverlap), "Locked source overlap accepted by shape reader");
        var old = CopyFixture(); old.Properties["schemaVersion"] = Json("3"); Reject(() => Read(old), "V4 reader reinterpreted v3");
    }
    private static void TestProjection()
    {
        var root = CopyFixture(); var p = Project(root); Check(p.Config.schemaVersion == 3 && p.Config.knownConstraints.Count == 1, "Missing legacy execution view"); Check(p.Document.Root.Get("schemaVersion").Int32() == 4, "Projection replaced source document");
        var unknown = BaseDocument(); unknown.Get("sources").Items.Add(Source("u", "COPY_ROTATION", 0)); var u = Project(unknown); Check(u.Config.knownConstraints.Count == 0 && u.Config.unknownConstraints.Count == 1, "Unregistered copy rotation became executable");
        var mapped = CopyFixture(); mapped.Get("bindings").Get("constraintMap").Items.Add(Json("{\"sourceId\":\"c0\",\"targetBoneName\":\"MCH_Target\"}")); var m = Project(mapped); Check(m.Config.knownConstraints[0].constraint.targetBoneName == "MCH_Target", "Overlay target not projected"); Check(m.Document.Sources["c0"].Get("constraint").StringAt("targetBoneName") == "Target", "Overlay overwrote author source");
        var absent = CopyFixture(); absent.Properties.Remove("bindings"); Reject(() => Project(absent), "Authoring-only snapshot assembled");
        var savedRegistry = registry;
        var futureRegistry = registry.Root.Clone(); futureRegistry.Get("implementations").Items.First(x => x.StringAt("id") == "hoaux.copy_rotation.world").Properties["availability"] = HoConstraintJsonValue.String("PLANNED");
        registry = HoConstraintIrV4Registry.Load(HoConstraintJson.Serialize(futureRegistry));
        try
        {
            var planned = CopyFixture(); var l = Project(planned); Check(l.SelectedBindings.Count == 0 && l.Config.unknownConstraints.Count == 1, "PLANNED contract executed");
            planned.Get("implementationBindings").Items[0].Properties["selection"] = HoConstraintJsonValue.String("LOCKED"); Reject(() => Project(planned), "Locked unsupported implementation silently changed");
        }
        finally { registry = savedRegistry; }
        var owner = CopyFixture(); owner.Get("sources").Items[0].Properties["ownerKind"] = HoConstraintJsonValue.String("OBJECT"); var o = Project(owner); Check(o.Config.knownConstraints.Count == 0 && o.Config.unknownConstraints.Count == 0 && o.Diagnostics.Any(x => x.Code == "UnsupportedOwner"), "Object owner projected onto bone");
        var overlap = CopyFixture(); Semantic(overlap, "s1", "COPY_ROTATION", new[] { "c0" }, "{\"auxType\":\"COPY_R\"}"); Bind(overlap, "s1", "hoaux.copy_rotation.world"); Reject(() => Project(overlap), "Double execution accepted");
        var standard = CopyFixture(); standard.Get("implementationBindings").Items.Clear(); Bind(standard, "s0", "standard.copy_rotation.world", "Standard"); standard.Get("sources").Items[0].Get("constraint").Get("parameters").Properties["influence"] = Json("0.3"); Check(Project(standard, "Standard").SelectedBindings.Count == 0, "Exact-only native non-half rotation executed");
        var missingTarget = CopyFixture(); missingTarget.Get("sources").Items[0].Get("constraint").Properties["targetBoneName"] = HoConstraintJsonValue.String("Missing"); Reject(() => Project(missingTarget), "Missing target binding was accepted");
        var applied = CopyFixture(); applied.Properties["exportOptions"] = Json("{\"appliedConstraints\":true}"); Check(Project(applied).SelectedBindings.Count == 0, "Already-applied constraints were replayed");
        var posed = CopyFixture(); posed.Properties["sourceContext"] = Json("{\"bones\":[{\"boneName\":\"Owner\",\"poseBasis\":[1,0,0,0.1,0,1,0,0,0,0,1,0,0,0,0,1]}]}"); Check(Project(posed).SelectedBindings.Count == 0, "Author nonidentity owner pose became Exact after preprocessing");
        var cyclic = CopyFixture(); cyclic.Get("sources").Items[0].Get("constraint").Properties["targetBoneName"] = HoConstraintJsonValue.String("Owner"); Reject(() => Project(cyclic), "Self-dependent execution graph was accepted");
        var cyclicParent = CopyFixture(); cyclicParent.Get("bindings").Get("bindingContext").Get("bones").Items[0].Properties["parentBone"] = HoConstraintJsonValue.String("Owner"); Reject(() => Project(cyclicParent), "Cyclic parent binding was accepted");
    }
    private static void TestComposites()
    {
        var aim = BaseDocument(); aim.Get("sources").Items.Add(Source("a", "DAMPED_TRACK", 0, "Target", ",\"track_axis\":\"TRACK_X\"")); aim.Get("sources").Items.Add(Source("b", "LOCKED_TRACK", 1, "Other", ",\"track_axis\":\"TRACK_Z\",\"lock_axis\":\"LOCK_X\"")); Semantic(aim, "s", "AIM", new[] { "a", "b" }, "{\"auxType\":\"AIM\",\"sourceBones\":[\"Target\",\"Other\"],\"targetBone\":\"Target\",\"upBone\":\"Other\",\"aimAxis\":\"TRACK_X\",\"upAxis\":\"TRACK_Z\",\"upMode\":\"OBJECT_POSITION\",\"muted\":false}"); Bind(aim, "s", "hoaux.aim.object_position"); var a = Project(aim); Check(a.Config.aimBindings.Count == 1 && a.Config.knownConstraints.Count == 2 && a.Config.auxBones[0].constraints.Count == 0, "Aim composite view not coherent");
        var badAim = aim.Clone(); badAim.Get("semantics").Items[0].Get("parameters").Properties["targetBone"] = HoConstraintJsonValue.String("Other"); Reject(() => Project(badAim), "Aim semantic/source target disagreement accepted");
        var fan = BaseDocument(); fan.Get("sources").Items.Add(Source("f", "COPY_ROTATION", 0)); Semantic(fan, "sf", "FAN", new[] { "f" }, "{\"auxType\":\"FAN_SINGLE\",\"sourceBones\":[\"Target\",\"Other\"]}"); Bind(fan, "sf", "hoaux.fan.legacy"); Check(Project(fan).SelectedBindings.Count == 0, "Legacy behavior executed under EXACT_ONLY"); fan.Get("implementationBindings").Items[0].Properties["fallbackPolicy"] = HoConstraintJsonValue.String("ALLOW_LEGACY"); Check(Project(fan).Config.auxBones[0].auxType == "FAN_SINGLE", "Fan legacy group projection failed");
        var twist = BaseDocument(); twist.Get("sources").Items.Add(Source("r", "COPY_ROTATION", 0)); twist.Get("sources").Items.Add(Source("t", "STRETCH_TO", 1, "Target", ",\"volume\":\"NO_VOLUME\",\"keep_axis\":\"SWING_Y\"")); Semantic(twist, "st", "TWIST", new[] { "r", "t" }, "{\"auxType\":\"TWIST\",\"sourceBones\":[\"Target\"]}"); Bind(twist, "st", "hoaux.twist.legacy", "HoAux", "ALLOW_LEGACY"); Check(Project(twist).Config.auxBones[0].constraints.Count == 2, "Twist did not preserve both constraints");
        var mch = BaseDocument(); mch.Get("sources").Items.Add(Source("m", "CHILD_OF", 0, "Target", ",\"use_location_x\":true,\"use_location_y\":true,\"use_location_z\":true,\"use_rotation_x\":true,\"use_rotation_y\":true,\"use_rotation_z\":true,\"use_scale_x\":false,\"use_scale_y\":false,\"use_scale_z\":false")); Semantic(mch, "sm", "MCH", new[] { "m" }, "{\"auxType\":\"MCH\",\"sourceBones\":[\"Target\"]}"); Bind(mch, "sm", "hoaux.mch.parent"); var m = Project(mch); Check(m.Config.mchBindings.Count == 1 && m.Config.mchEnabledBones.Contains("Target") && m.Config.auxBones[0].constraints.Count == 0, "MCH compatibility projection failed");
        var armature = BaseDocument(); var arm = Source("ar", "ARMATURE", 0); arm.Get("constraint").Get("references").Properties["targets"] = Json("[{\"rnaType\":\"ArmatureConstraintTarget\",\"properties\":{\"target\":{\"name\":\"Rig\",\"rnaType\":\"Object\"},\"subtarget\":\"Target\",\"weight\":1}}]"); armature.Get("sources").Items.Add(arm); Semantic(armature, "sa", "ARMATURE", new[] { "ar" }, "{}", "RAW_TYPE"); Bind(armature, "sa", "standard.armature.linear", "Standard"); Check(Project(armature, "Standard").SelectedBindings.Count == 0, "Exact-only native Armature approximation executed"); armature.Get("implementationBindings").Items[0].Properties["fallbackPolicy"] = HoConstraintJsonValue.String("ALLOW_APPROXIMATE"); Check(Project(armature, "Standard").Config.knownConstraints.Count == 1, "Explicit native Armature approximation was not projected");
    }
    private static void TestLocalAuthoringDomain()
    {
        var saved = registry; var testRegistry = registry.Root.Clone();
        var localContract = testRegistry.Get("implementations").Items.First(x => x.StringAt("id") == "hoaux.copy_rotation.local");
        localContract.Properties["availability"] = HoConstraintJsonValue.String("IMPLEMENTED"); localContract.Properties["fidelity"] = HoConstraintJsonValue.String("EXACT");
        registry = HoConstraintIrV4Registry.Load(HoConstraintJson.Serialize(testRegistry));
        try
        {
            var root = CopyFixture(); var p = root.Get("sources").Items[0].Get("constraint").Get("parameters");
            p.Properties["owner_space"] = HoConstraintJsonValue.String("LOCAL"); p.Properties["target_space"] = HoConstraintJsonValue.String("LOCAL");
            root.Get("implementationBindings").Items[0].Properties["implementationId"] = HoConstraintJsonValue.String("hoaux.copy_rotation.local");
            var context = root.Get("bindings").Get("bindingContext");
            foreach (var bone in context.Get("bones").Items) bone.Properties["inheritRotation"] = HoConstraintJsonValue.Bool(true);
            root.Properties["sourceContext"] = context.Clone();
            Check(Project(root).SelectedBindings.Count == 1, "Local exact authoring domain was rejected");
            var missing = root.Clone(); missing.Get("sourceContext").Get("bones").Items[0].Properties.Remove("inheritRotation"); Check(Project(missing).SelectedBindings.Count == 0, "Missing inheritRotation admitted to Exact");
            var disabled = root.Clone(); disabled.Get("bindings").Get("bindingContext").Get("bones").Items[1].Properties["inheritRotation"] = HoConstraintJsonValue.Bool(false); Check(Project(disabled).SelectedBindings.Count == 0, "Disabled target rotation inheritance admitted");
            var scale = root.Clone(); scale.Get("sourceContext").Get("bones").Items[1].Properties["inheritScale"] = HoConstraintJsonValue.String("NONE"); Check(Project(scale).SelectedBindings.Count == 0, "Target nonFULL inheritance admitted");
            var parent = root.Clone(); parent.Get("sourceContext").Get("bones").Items[0].Properties["parentBone"] = HoConstraintJsonValue.String("Other"); parent.Get("bindings").Get("bindingContext").Get("bones").Items[0].Properties["parentBone"] = HoConstraintJsonValue.String("Other"); parent.Get("bindings").Get("bindingContext").Get("bones").Items[2].Get("restMatrix").Items[3] = Json("0.3"); Check(Project(parent).SelectedBindings.Count == 0, "Parent rest reference change admitted");
            var converted = root.Clone(); var final = converted.Get("bindings").Get("bindingContext");
            final.Properties["objectMatrix"] = Json("[0,-1,0,0,1,0,0,0,0,0,1,0,0,0,0,1]");
            foreach (var bone in final.Get("bones").Items) bone.Properties["restMatrix"] = Json("[0,1,0,0,-1,0,0,0,0,0,1,0,0,0,0,1]");
            Check(Project(converted).SelectedBindings.Count == 1, "Pure armature object/bone basis conversion was misclassified as rest change");
            var changed = root.Clone(); changed.Get("bindings").Get("bindingContext").Get("bones").Items[1].Get("restMatrix").Items[3] = Json("0.4"); Check(Project(changed).SelectedBindings.Count == 0, "Target author/final rest change admitted");
        }
        finally { registry = saved; }
    }
    private static void TestRigSpaceContract()
    {
        var saved = registry;
        try
        {
            var copy = saved.Root.Clone();
            var contract = copy.Get("implementations").Items.First(x => x.StringAt("id") == "hoaux.copy_rotation.world");
            contract.Get("conditions").Properties["rigSpace"] = HoConstraintJsonValue.String("FINITE_POSITIVE_UNIFORM_TRS");
            registry = HoConstraintIrV4Registry.Load(HoConstraintJson.Serialize(copy));
            Check(Project(CopyFixture()).SelectedBindings.Count == 1, "Known HoAux runtime rig-space contract was rejected");
            contract.Get("conditions").Properties["rigSpace"] = HoConstraintJsonValue.String("FUTURE_UNIMPLEMENTED_SPACE");
            registry = HoConstraintIrV4Registry.Load(HoConstraintJson.Serialize(copy));
            var unsupported = Project(CopyFixture());
            Check(unsupported.SelectedBindings.Count == 0 && unsupported.Diagnostics.Any(x => x.Code == "Unsupported" && x.Message.Contains("rigSpace")), "Unknown rigSpace enum executed");
            var locked = CopyFixture(); locked.Get("implementationBindings").Items[0].Properties["selection"] = HoConstraintJsonValue.String("LOCKED");
            Reject(() => Project(locked), "Unknown locked rigSpace condition silently changed implementation");
            contract.Get("conditions").Properties["rigSpace"] = HoConstraintJsonValue.Bool(true);
            registry = HoConstraintIrV4Registry.Load(HoConstraintJson.Serialize(copy));
            Check(Project(CopyFixture()).SelectedBindings.Count == 0, "Non-string rigSpace condition executed");
        }
        finally { registry = saved; }
    }
}
