using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.ShadowResponse
{
    // Explicit one-time authoring conversion. Never called by imports or inspector repaint.
    public static class HoEffectShadowLutMigration
    {
        static readonly string[] Changed = { "_ShadowResponseLUT", "_HasShadowResponseLUT", "_ShadowResponseVersion", "_ShadowStrength", "_AODarken" };
        [Serializable] public sealed class Input
        {
            public Color bright, first, second, third;
            public float border1, border2, border3, blur1, blur2, blur3, receive1, receive2, receive3, use, strength, aoDark;
        }
        [Serializable] public sealed class Row
        {
            public string path, guid, beforeSha, metaSha, shaderGuid, shaderHash, unchangedState;
            public string curvePath, curveSha, curveMetaSha, curveGuid;
            public long localId, lutLocalId;
            public Input input;
            public float maximumCurveError;
            public string[] blockers;
        }
        [Serializable] public sealed class Report
        {
            public int version = 1;
            public string projectRoot, curveRoot;
            public bool reviewed;
            public Row[] rows;
            public string[] createdPaths;
            public string[] differences = {
                "The material keeps its GUID, all non-shadow settings and outer blend/mask/AO. Old band controls become editor-only migration inputs; one linear RGBA curve LUT supplies RGB shade and A lit visibility.",
                "Offline curves preserve fixed band blur. The old screen-derivative fwidth antialiasing is replaced by linear LUT filtering; pixel-equivalence at screen-dependent transition edges is not claimed.",
                "One shared cast reception coordinate replaces per-band reception. Spatial band colors or different effective reception weights are blockers, not silently flattened.",
                "Disabled old shadows become native strength 0 and AO darkening 0. New native AO darkening is independently evaluated; new shadow strength 0 also makes the MatCap/Rim visibility mask neutral." };
        }
        [Serializable] public sealed class AppliedRow { public Row source; public string afterSha, afterMetaSha; }
        [Serializable] public sealed class Journal
        {
            public int version = 1;
            public string projectRoot, state, reportSha;
            public AppliedRow[] rows;
            public FileStamp[] resources;
            public string[] createdPaths;
        }
        [Serializable] public sealed class FileStamp { public string path, sha; }
        [Serializable] sealed class Field { public string path, type, value; }
        [Serializable] sealed class Fields { public Field[] fields; }
        [Serializable] sealed class Session { public string project, report, backup, curves; }
        const string SessionKey = "HoShader.EffectShadowLut.CurrentSession";

        [MenuItem("Tools/HoShader/Effect Shadow LUT/1 Preview")]
        public static void MenuPreview()
        {
            string token=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+System.Guid.NewGuid().ToString("N").Substring(0,8);
            string folder=Path.Combine(Path.GetTempPath(),"HoShader","EffectShadowLut",token);Directory.CreateDirectory(folder);
            var s=new Session {project=ProjectRoot,report=Path.Combine(folder,"preview.json"),backup=Path.Combine(folder,"backup"),curves="Assets/HoShadowCurves/Effect-"+token};
            WriteNew(Path.Combine(folder,"session.json"),s);SessionState.SetString(SessionKey,Path.Combine(folder,"session.json"));Configure(s);RunPreview();
            Debug.Log("Review preview.json and set reviewed=true before the explicit Apply menu: "+s.report);
        }
        [MenuItem("Tools/HoShader/Effect Shadow LUT/2 Apply Reviewed Preview")]
        public static void MenuApply(){Configure(MenuSession());RunApply();}
        [MenuItem("Tools/HoShader/Effect Shadow LUT/3 Verify")]
        public static void MenuVerify(){Configure(MenuSession());RunVerify();}
        [MenuItem("Tools/HoShader/Effect Shadow LUT/4 Restore")]
        public static void MenuRestore(){Configure(MenuSession());RunRestore();}
        static Session MenuSession(){string path=SessionState.GetString(SessionKey,"");if(!File.Exists(path))throw new InvalidOperationException("Run Preview in this Editor session first.");return JsonUtility.FromJson<Session>(File.ReadAllText(path));}
        static void Configure(Session s){if(s.project!=ProjectRoot)throw new InvalidOperationException("Preview belongs to another project.");Environment.SetEnvironmentVariable("HO_EFFECT_SHADOW_PROJECT",s.project);Environment.SetEnvironmentVariable("HO_EFFECT_SHADOW_REPORT",s.report);Environment.SetEnvironmentVariable("HO_EFFECT_SHADOW_BACKUP",s.backup);Environment.SetEnvironmentVariable("HO_EFFECT_SHADOW_CURVE_ROOT",s.curves);}

        public static void RunPreview()
        {
            RequireProject(); string output = External("HO_EFFECT_SHADOW_REPORT", true), root = Required("HO_EFFECT_SHADOW_CURVE_ROOT");
            AssetPath(root); if (File.Exists(Absolute(root)) || Directory.Exists(Absolute(root)) || File.Exists(Absolute(root + ".meta"))) throw new InvalidOperationException("Use a new curve directory.");
            var created = new List<string>(); var rows = new List<Row>();
            EnsureFolder(root, created);
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material?.shader == null || !EffectShader(material.shader.name) || StoredVersion(material) >= 1) continue;
                Clean(path);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long id);
                var input = ReadInput(material); var blockers = Blockers(material, input);
                var row = new Row { path = path, guid = guid, localId = id, beforeSha = Hash(Absolute(path)), metaSha = Hash(Absolute(path + ".meta")),
                    shaderGuid = Guid(material.shader), shaderHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(material.shader)).ToString(),
                    unchangedState = State(material), input = input, blockers = blockers, curvePath = root + "/" + guid + ".asset" };
                if (blockers.Length == 0)
                {
                    var profile = ScriptableObject.CreateInstance<HoShadowResponseAsset>(); profile.name = guid;
                    profile.SetCurves(Curve(t => Shade(input, t).r), Curve(t => Shade(input, t).g), Curve(t => Shade(input, t).b), Curve(t => Band(t, input.border1, input.blur1)));
                    AssetDatabase.CreateAsset(profile, row.curvePath); created.Add(row.curvePath); HoShadowResponseBaker.Bake(profile, false);
                    AssetDatabase.ImportAsset(row.curvePath, ImportAssetOptions.ForceSynchronousImport);
                    profile = AssetDatabase.LoadAssetAtPath<HoShadowResponseAsset>(row.curvePath);
                    row.curveGuid = Guid(profile); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(profile.LookupTexture, out string _, out row.lutLocalId);
                    for (int n = 0; n <= 4096; ++n)
                    {
                        float t = n / 4096f; Color expected = Shade(input, t), actual = profile.Evaluate(t);
                        row.maximumCurveError = Mathf.Max(row.maximumCurveError, Mathf.Abs(expected.r - actual.r), Mathf.Abs(expected.g - actual.g), Mathf.Abs(expected.b - actual.b), Mathf.Abs(Band(t,input.border1,input.blur1)-actual.a));
                    }
                    if (row.maximumCurveError > .002f) row.blockers = new[] { "CURVE_ERROR: adaptive authoring curve exceeds .002 absolute error." };
                    row.curveSha = Hash(Absolute(row.curvePath)); row.curveMetaSha = Hash(Absolute(row.curvePath + ".meta"));
                }
                rows.Add(row);
            }
            var report = new Report { projectRoot = ProjectRoot, curveRoot = root, rows = rows.ToArray(), createdPaths = created.ToArray() };
            WriteNew(output, report); Debug.Log($"HO_EFFECT_SHADOW_PREVIEW rows={rows.Count} blocked={rows.Count(r=>r.blockers.Length!=0)} report={output}");
        }

        public static void RunApply()
        {
            RequireProject(); string reportPath = External("HO_EFFECT_SHADOW_REPORT", false), backup = External("HO_EFFECT_SHADOW_BACKUP", true);
            var report = ReadReport(reportPath);
            if (!report.reviewed || report.rows.Length == 0 || report.rows.Any(r => r.blockers.Length != 0)) throw new InvalidOperationException("Review all concrete LUT differences and resolve blockers before Apply.");
            foreach (var row in report.rows) Before(row);
            Directory.CreateDirectory(backup);
            var journal = new Journal { projectRoot = ProjectRoot, state = "Prepared", reportSha = Hash(reportPath),
                rows = report.rows.Select(r => new AppliedRow { source = r }).ToArray(), createdPaths = report.createdPaths,
                resources = report.createdPaths.SelectMany(p => new[] { p, p + ".meta" }).Where(p => File.Exists(Absolute(p))).Select(p => new FileStamp { path = p, sha = Hash(Absolute(p)) }).ToArray() };
            foreach (var row in report.rows) { File.Copy(Absolute(row.path), Path.Combine(backup, row.guid + ".mat")); File.Copy(Absolute(row.path + ".meta"), Path.Combine(backup, row.guid + ".meta")); }
            WriteNew(Path.Combine(backup, "preview.json"), report); SaveJournal(backup, journal);
            try
            {
                foreach (var row in journal.rows)
                {
                    Before(row.source); var material = MaterialAt(row.source);
                    var profile = AssetDatabase.LoadAssetAtPath<HoShadowResponseAsset>(row.source.curvePath);
                    material.SetTexture("_ShadowResponseLUT", profile.LookupTexture); material.SetFloat("_HasShadowResponseLUT", 1); material.SetFloat("_ShadowResponseVersion", 1);
                    material.SetFloat("_ShadowStrength", row.source.input.use < .5f ? 0 : Mathf.Clamp01(row.source.input.use * row.source.input.strength));
                    material.SetFloat("_AODarken", row.source.input.use < .5f ? 0 : Mathf.Clamp01(row.source.input.use * row.source.input.aoDark));
                    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); AssetDatabase.ImportAsset(row.source.path, ImportAssetOptions.ForceSynchronousImport);
                    CheckTarget(row.source); row.afterSha = Hash(Absolute(row.source.path)); row.afterMetaSha = Hash(Absolute(row.source.path + ".meta")); journal.state = "Applied"; SaveJournal(backup, journal);
                }
                journal.state = "Verified"; SaveJournal(backup, journal); Debug.Log("HO_EFFECT_SHADOW_APPLY_OK rows=" + journal.rows.Length);
            }
            catch
            {
                foreach (var row in journal.rows) { File.Copy(Path.Combine(backup, row.source.guid + ".mat"), Absolute(row.source.path), true); File.Copy(Path.Combine(backup, row.source.guid + ".meta"), Absolute(row.source.path + ".meta"), true); }
                foreach (var row in journal.rows) AssetDatabase.ImportAsset(row.source.path, ImportAssetOptions.ForceSynchronousImport);
                journal.state = "RolledBack"; SaveJournal(backup, journal); throw;
            }
        }
        public static void RunCleanupUnusedPreview() => CleanupUnusedPreview(External("HO_EFFECT_SHADOW_REPORT", false));

        public static void CleanupUnusedPreview(string reportPath)
        {
            RequireProject(); var report = ReadReport(Path.GetFullPath(reportPath));
            if (!report.curveRoot.StartsWith("Assets/HoShadowCurves/",StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup only accepts a dedicated shadow-curve preview directory.");
            AssetPath(report.curveRoot); Clean(report.curveRoot);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in report.rows.Where(r=>!string.IsNullOrEmpty(r.curveSha)))
            {
                if (Path.GetDirectoryName(row.curvePath).Replace('\\','/') != report.curveRoot) throw new InvalidOperationException("Preview curve escaped its dedicated directory.");
                Clean(row.curvePath);
                if (Hash(Absolute(row.curvePath)) != row.curveSha || Hash(Absolute(row.curvePath+".meta")) != row.curveMetaSha) throw new InvalidOperationException("Preview resource changed; preserve it: "+row.curvePath);
                var profile = AssetDatabase.LoadAssetAtPath<HoShadowResponseAsset>(row.curvePath);
                if (profile == null || Guid(profile) != row.curveGuid || LocalId(profile.LookupTexture) != row.lutLocalId) throw new InvalidOperationException("Preview resource identity changed.");
                expected.Add(row.curvePath); expected.Add(row.curvePath+".meta");
            }
            foreach (string file in Directory.GetFiles(Absolute(report.curveRoot),"*",SearchOption.AllDirectories))
                if (!expected.Contains(Path.GetRelativePath(ProjectRoot,file).Replace('\\','/'))) throw new InvalidOperationException("Unknown file in preview directory; preserve it: "+file);
            var consumers = AssetDatabase.GetAllAssetPaths().Where(p=>(p.StartsWith("Assets/",StringComparison.Ordinal)||p.StartsWith("Packages/",StringComparison.Ordinal)) && !AssetDatabase.IsValidFolder(p) && !p.StartsWith(report.curveRoot+"/",StringComparison.Ordinal)).ToArray();
            if (AssetDatabase.GetDependencies(consumers,true).Any(p=>p==report.curveRoot||p.StartsWith(report.curveRoot+"/",StringComparison.Ordinal))) throw new InvalidOperationException("An imported asset still references this preview; cleanup refused.");
            if (!AssetDatabase.DeleteAsset(report.curveRoot)) throw new IOException("Could not delete unused preview directory.");
            Debug.Log("HO_EFFECT_SHADOW_UNUSED_PREVIEW_REMOVED "+report.curveRoot);
        }

        public static void RunVerify()
        {
            RequireProject(); string backup = External("HO_EFFECT_SHADOW_BACKUP", false); var j = ReadJournal(backup); CheckJournal(backup,j);
            Debug.Log("HO_EFFECT_SHADOW_RELOAD_OK rows=" + j.rows.Length);
        }
        public static void RunRestore()
        {
            RequireProject(); string backup = External("HO_EFFECT_SHADOW_BACKUP", false); var j = ReadJournal(backup); CheckJournal(backup,j);
            // All current byte, identity, resource and importer checks precede writes.
            foreach (var row in j.rows) { File.Copy(Path.Combine(backup, row.source.guid + ".mat"), Absolute(row.source.path), true); File.Copy(Path.Combine(backup, row.source.guid + ".meta"), Absolute(row.source.path + ".meta"), true); }
            foreach (var row in j.rows) AssetDatabase.ImportAsset(row.source.path, ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in j.createdPaths.Reverse())
            {
                if (Directory.Exists(Absolute(path)) && Directory.EnumerateFileSystemEntries(Absolute(path)).Any()) throw new InvalidOperationException("Preserve unexpected files in curve directory: " + path);
                if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Cannot remove owned curve output: " + path);
            }
            foreach (var row in j.rows) if (Hash(Absolute(row.source.path)) != row.source.beforeSha || Hash(Absolute(row.source.path + ".meta")) != row.source.metaSha) throw new IOException("Original material did not restore byte-exact.");
            j.state = "Restored"; SaveJournal(backup,j); Debug.Log("HO_EFFECT_SHADOW_RESTORED rows=" + j.rows.Length);
        }
        static void CheckJournal(string backup, Journal j)
        {
            if (j.state != "Verified") throw new InvalidOperationException("A verified independent LUT transaction is required.");
            var expectedFiles=new HashSet<string>(j.resources.Select(f=>f.path),StringComparer.Ordinal);
            foreach(string path in j.createdPaths)
            {
                Clean(path);
                if(Directory.Exists(Absolute(path)))foreach(string file in Directory.GetFiles(Absolute(path),"*",SearchOption.AllDirectories))
                    if(!expectedFiles.Contains(Path.GetRelativePath(ProjectRoot,file).Replace('\\','/')))throw new InvalidOperationException("Preserve extra files in curve output before Restore: "+file);
            }
            foreach (var file in j.resources) if (Hash(Absolute(file.path)) != file.sha) throw new InvalidOperationException("Authored curve resources changed; merge before Restore: " + file.path);
            foreach (var row in j.rows)
            {
                if (Hash(Path.Combine(backup,row.source.guid+".mat")) != row.source.beforeSha || Hash(Path.Combine(backup,row.source.guid+".meta")) != row.source.metaSha) throw new InvalidOperationException("Material backup changed.");
                if (Hash(Absolute(row.source.path)) != row.afterSha || Hash(Absolute(row.source.path+".meta")) != row.afterMetaSha) throw new InvalidOperationException("Material changed after LUT migration: " + row.source.path);
                Clean(row.source.path); CheckTarget(row.source);
            }
        }
        static void Before(Row row)
        {
            AssetPath(row.path); Clean(row.path);
            if (Hash(Absolute(row.path)) != row.beforeSha || Hash(Absolute(row.path + ".meta")) != row.metaSha) throw new InvalidOperationException("Source material changed: " + row.path);
            var material = MaterialAt(row);
            string shaderGuid=Guid(material.shader),shaderHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(material.shader)).ToString();
            if(shaderGuid!=row.shaderGuid)throw new InvalidOperationException("Source shader GUID changed: "+row.path+" expected="+row.shaderGuid+" actual="+shaderGuid);
            if(shaderHash!=row.shaderHash)throw new InvalidOperationException("Source shader dependency hash changed: "+row.path+" expected="+row.shaderHash+" actual="+shaderHash+". Freeze shader and its Editor dependencies, then regenerate Preview.");
            string state=State(material);
            if(state!=row.unchangedState)
            {
                var expected=JsonUtility.FromJson<Fields>(row.unchangedState).fields.ToDictionary(f=>f.path,f=>f.type+":"+f.value);
                var actual=JsonUtility.FromJson<Fields>(state).fields.ToDictionary(f=>f.path,f=>f.type+":"+f.value);
                string first=expected.Keys.Concat(actual.Keys).Distinct().OrderBy(p=>p,StringComparer.Ordinal).First(p=>!expected.TryGetValue(p,out string a)||!actual.TryGetValue(p,out string b)||a!=b);
                throw new InvalidOperationException("Source material state changed: "+row.path+" field="+first+" expected="+(expected.TryGetValue(first,out string e)?e:"<missing>")+" actual="+(actual.TryGetValue(first,out string v)?v:"<missing>"));
            }
            AssetPath(row.curvePath); Clean(row.curvePath);
            if (Hash(Absolute(row.curvePath)) != row.curveSha || Hash(Absolute(row.curvePath + ".meta")) != row.curveMetaSha) throw new InvalidOperationException("Preview curve changed.");
            var profile = AssetDatabase.LoadAssetAtPath<HoShadowResponseAsset>(row.curvePath);
            if (profile == null || Guid(profile) != row.curveGuid || LocalId(profile.LookupTexture) != row.lutLocalId || profile.LookupTexture.name != HoShadowResponseAsset.TextureName) throw new InvalidOperationException("Preview LUT identity changed.");
        }
        static void CheckTarget(Row row)
        {
            var m = MaterialAt(row); var texture = m.GetTexture("_ShadowResponseLUT");
            if (State(m) != row.unchangedState || texture == null || Guid(texture) != row.curveGuid || LocalId(texture) != row.lutLocalId
                || m.GetFloat("_HasShadowResponseLUT") != 1 || m.GetFloat("_ShadowResponseVersion") != 1
                || m.GetFloat("_ShadowStrength") != (row.input.use < .5f ? 0 : Mathf.Clamp01(row.input.use*row.input.strength))
                || m.GetFloat("_AODarken") != (row.input.use < .5f ? 0 : Mathf.Clamp01(row.input.use*row.input.aoDark))) throw new InvalidOperationException("Saved LUT material differs from reviewed proposal: " + row.path);
        }
        static string[] Blockers(Material m, Input i)
        {
            var errors = new List<string>();
            if (!m.HasProperty("_ShadowResponseLUT")) errors.Add("TARGET_SHADER: update the Effect package to the native LUT model first.");
            using (var so = new SerializedObject(m)) if (so.FindProperty("m_Parent")?.objectReferenceValue != null) errors.Add("MATERIAL_VARIANT: migrate its parent explicitly.");
            foreach (string p in new[] { "_BrightColorTex", "_ShadowColorTex", "_Shadow2ColorTex", "_Shadow3ColorTex" })
                if (m.GetTexture(p) != null) errors.Add("SPATIAL_SHADE: a source color texture cannot be represented by one 1D response: " + p);
            if (i.use >= .5f && ((i.second.a > 0 && i.receive2 != i.receive1) || (i.third.a > 0 && i.receive3 != i.receive1))) errors.Add("CAST_COORDINATE: active band reception weights differ.");
            if (i.strength < 0 || i.strength > 1 || i.use < 0 || i.use > 1) errors.Add("SOURCE_RANGE: shadow controls exceed native strength range.");
            return errors.ToArray();
        }
        static Input ReadInput(Material m) => new() { bright=m.GetColor("_BrightColor"),first=m.GetColor("_ShadowColor"),second=m.GetColor("_Shadow2Color"),third=m.GetColor("_Shadow3Color"),
            border1=m.GetFloat("_ShadowBorder"),border2=m.GetFloat("_Shadow2Border"),border3=m.GetFloat("_Shadow3Border"),blur1=m.GetFloat("_ShadowBlur"),blur2=m.GetFloat("_Shadow2Blur"),blur3=m.GetFloat("_Shadow3Blur"),
            receive1=m.GetFloat("_ReceiveShadow"),receive2=m.GetFloat("_Shadow2Receive"),receive3=m.GetFloat("_Shadow3Receive"),use=m.GetFloat("_UseShadow"),strength=m.GetFloat("_ShadowStrength"),aoDark=m.GetFloat("_AODarken") };
        static Color Shade(Input i,float t)
        {
            Color c=Color.LerpUnclamped(i.bright,i.first,(1-Band(t,i.border1,i.blur1))*Mathf.Clamp01(i.first.a));
            c=Color.LerpUnclamped(c,i.second,(1-Band(t,Mathf.Min(i.border2,i.border1),i.blur2))*Mathf.Clamp01(i.second.a));
            c=Color.LerpUnclamped(c,i.third,(1-Band(t,Mathf.Min(i.border3,Mathf.Min(i.border2,i.border1)),i.blur3))*Mathf.Clamp01(i.third.a));return c;
        }
        static float Band(float x,float b,float blur) { float s=Mathf.Max(blur*.5f,.0001f),t=Mathf.Clamp01((x-b+s)/(2*s));return t*t*(3-2*t); }
        static AnimationCurve Curve(Func<float,float> f)
        {
            var times=new SortedSet<float>(); for(int i=0;i<=32;i++)times.Add(i/32f);
            float D(float t){float a=Mathf.Max(0,t-.00001f),b=Mathf.Min(1,t+.00001f);return (f(b)-f(a))/(b-a);}
            void Refine(float a,float b,int depth)
            {
                float v0=f(a),v1=f(b),m0=D(a)*(b-a),m1=D(b)*(b-a);bool add=false;
                foreach(float u in new[]{.25f,.5f,.75f}){float v=(2*u*u*u-3*u*u+1)*v0+(u*u*u-2*u*u+u)*m0+(-2*u*u*u+3*u*u)*v1+(u*u*u-u*u)*m1;if(Mathf.Abs(v-f(Mathf.Lerp(a,b,u)))>.0001f)add=true;}
                if(add&&depth<12){float c=(a+b)*.5f;times.Add(c);Refine(a,c,depth+1);Refine(c,b,depth+1);}
            }
            float[] initial=times.ToArray();for(int i=1;i<initial.Length;i++)Refine(initial[i-1],initial[i],0);
            return new AnimationCurve(times.Select(t=>new Keyframe(t,f(t),D(t),D(t))).ToArray()){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        static string State(Material material)
        {
            using var so=new SerializedObject(material);var list=new List<Field>();var p=so.GetIterator();bool enter=true;
            while(p.Next(enter))
            {
                enter=true;if(p.propertyPath=="m_SavedProperties"){enter=false;continue;} if(p.propertyType==SerializedPropertyType.Generic)continue;
                list.Add(new Field {path=p.propertyPath,type=p.propertyType.ToString(),value=Value(p)});if(p.propertyType==SerializedPropertyType.ObjectReference)enter=false;
            }
            // Unity may materialize a shader default in the in-memory saved table
            // without rewriting the asset. Compare declared EFFECTIVE inputs, while
            // preserving unknown serialized legacy fields separately below.
            var declared=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<material.shader.GetPropertyCount();i++)
            {
                string name=material.shader.GetPropertyName(i);declared.Add(name);if(Changed.Contains(name))continue;
                var type=material.shader.GetPropertyType(i);string value;
                switch(type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:value=JsonUtility.ToJson(material.GetColor(name));break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:value=JsonUtility.ToJson(material.GetVector(name));break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        var texture=material.GetTexture(name);value=(texture==null?"null":Guid(texture)+":"+LocalId(texture))
                            +";scale="+JsonUtility.ToJson(material.GetTextureScale(name))+";offset="+JsonUtility.ToJson(material.GetTextureOffset(name));break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int:value=material.GetInteger(name).ToString(CultureInfo.InvariantCulture);break;
                    default:value=material.GetFloat(name).ToString("R",CultureInfo.InvariantCulture);break;
                }
                list.Add(new Field {path="declared/"+name,type=type.ToString(),value=value});
            }
            foreach(string group in new[]{"m_TexEnvs","m_Floats","m_Ints","m_Colors"})
            {
                var array=so.FindProperty("m_SavedProperties."+group);if(array==null)continue;
                for(int n=0;n<array.arraySize;n++)
                {
                    var entry=array.GetArrayElementAtIndex(n);string name=entry.FindPropertyRelative("first").stringValue;if(Changed.Contains(name)||declared.Contains(name))continue;
                    var value=entry.FindPropertyRelative("second");var iterator=value.Copy();var end=value.GetEndProperty();bool children=true;
                    do {if(iterator.propertyType!=SerializedPropertyType.Generic)list.Add(new Field {path=group+"/"+name+"/"+iterator.propertyPath.Substring(value.propertyPath.Length),type=iterator.propertyType.ToString(),value=Value(iterator)});children=iterator.propertyType!=SerializedPropertyType.ObjectReference;}
                    while(iterator.Next(children)&&!SerializedProperty.EqualContents(iterator,end));
                }
            }
            return JsonUtility.ToJson(new Fields {fields=list.OrderBy(f=>f.path,StringComparer.Ordinal).ToArray()});
        }
        static string Value(SerializedProperty p)
        {
            switch(p.propertyType)
            {
                case SerializedPropertyType.ObjectReference:return p.objectReferenceValue==null?"null":Guid(p.objectReferenceValue)+":"+LocalId(p.objectReferenceValue);
                case SerializedPropertyType.String:return p.stringValue;
                case SerializedPropertyType.Boolean:return p.boolValue?"1":"0";
                case SerializedPropertyType.Integer:return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float:return p.doubleValue.ToString("R",CultureInfo.InvariantCulture);
                case SerializedPropertyType.Color:return JsonUtility.ToJson(p.colorValue);
                case SerializedPropertyType.Vector2:return JsonUtility.ToJson(p.vector2Value);
                case SerializedPropertyType.Vector3:return JsonUtility.ToJson(p.vector3Value);
                case SerializedPropertyType.Vector4:return JsonUtility.ToJson(p.vector4Value);
                case SerializedPropertyType.Enum:case SerializedPropertyType.ArraySize:case SerializedPropertyType.LayerMask:return p.intValue.ToString(CultureInfo.InvariantCulture);
                default:throw new InvalidOperationException("Unsupported saved material field: "+p.propertyPath+" "+p.propertyType);
            }
        }
        static float StoredVersion(Material m)
        {string text=File.ReadAllText(Absolute(AssetDatabase.GetAssetPath(m)));var match=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^\s*- _ShadowResponseVersion:\s*([^\r\n]+)");return match.Success?float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):0;}
        static Material MaterialAt(Row row){var m=AssetDatabase.LoadAssetAtPath<Material>(row.path);if(m==null||Guid(m)!=row.guid||LocalId(m)!=row.localId||!EffectShader(m.shader.name))throw new InvalidOperationException("Material identity changed: "+row.path);return m;}
        static bool EffectShader(string name)=>name is "HoShader/Effect/URP/Layered Toon" or "HoShader/Effect/BIRP/Layered Toon";
        static Report ReadReport(string path){var r=JsonUtility.FromJson<Report>(File.ReadAllText(path));if(r?.version!=1||r.projectRoot!=ProjectRoot)throw new InvalidOperationException("Wrong LUT preview project.");foreach(var x in r.rows){AssetPath(x.path);AssetPath(x.curvePath);}return r;}
        static Journal ReadJournal(string backup){var j=JsonUtility.FromJson<Journal>(File.ReadAllText(Path.Combine(backup,"journal.json")));if(j?.version!=1||j.projectRoot!=ProjectRoot)throw new InvalidOperationException("Wrong LUT journal project.");foreach(var p in j.createdPaths)AssetPath(p);return j;}
        static void SaveJournal(string backup,Journal journal){string path=Path.Combine(backup,"journal.json"),tmp=path+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(journal,true),new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
        static void EnsureFolder(string path,List<string> created){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');if(parent!="Assets")EnsureFolder(parent,created);if(File.Exists(Absolute(path))||File.Exists(Absolute(path+".meta")))throw new InvalidOperationException("Output folder is not new: "+path);if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))))throw new IOException("Cannot create "+path);created.Add(path);}
        static void Clean(string path){var importer=AssetImporter.GetAtPath(path);if(importer!=null&&EditorUtility.IsDirty(importer))throw new InvalidOperationException("Unsaved importer: "+path);foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))if(EditorUtility.IsDirty(asset))throw new InvalidOperationException("Unsaved asset: "+path);}
        static void RequireProject(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||Path.GetFullPath(Required("HO_EFFECT_SHADOW_PROJECT"))!=ProjectRoot)throw new InvalidOperationException("Select the stable target project explicitly.");}
        static string External(string key,bool fresh){string path=Path.GetFullPath(Required(key));if(path.StartsWith(ProjectRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||fresh&&(File.Exists(path)||Directory.Exists(path)))throw new InvalidOperationException("Use a new external output/backup: "+path);return path;}
        static void WriteNew(string path,object obj){using var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write);using var w=new StreamWriter(f,new UTF8Encoding(false));w.Write(JsonUtility.ToJson(obj,true));}
        static string Required(string key)=>Environment.GetEnvironmentVariable(key)??throw new InvalidOperationException("Set "+key);
        static void AssetPath(string p){if(!p.StartsWith("Assets/",StringComparison.Ordinal)||p.Contains('\\')||p.Split('/').Any(v=>v is ".." or "." or ""))throw new InvalidOperationException("Invalid Assets path: "+p);}
        static string ProjectRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        static string Absolute(string p)=>Path.GetFullPath(Path.Combine(ProjectRoot,p));
        static string Guid(Object obj){if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string g,out long _))throw new InvalidOperationException("Persistent object required: "+obj);return g;}
        static long LocalId(Object obj){if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string _,out long id))throw new InvalidOperationException("Persistent object required.");return id;}
        static string Hash(string p){using var sha=SHA256.Create();using var f=File.OpenRead(p);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    }
}
