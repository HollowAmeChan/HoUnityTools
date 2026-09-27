// HoSwitchAnimationWindow.cs -- 开关动画生成器（2026-09-28 用户定的形态键工具升级版）
//
// 干什么：给一个**预制件**，按三族成对生成「开关动画」——
//   BS  形态键      ：<预制件>__BS__On|Off__<键名>        值 100 / 0（全 prefab 上这根键的所有网格一起）
//   GB  物体整体开关：<预制件>__GB__On|Off__<层级…>        m_IsActive 1 / 0（层级用预制件名起头）
//   GBC 组件开关    ：<预制件>__GBC__On|Off__<层级…>__<组件>#<i>  m_Enabled 1 / 0
//
// 三条硬规矩（都是实测定下来的，别改）：
//   ① **一个片段只写一个属性**（BS 只写那一根键；GB 只写 m_IsActive；GBC 只写 m_Enabled）
//      ⇒ 这些片段塞进同一棵 Direct 树时不会互相污染（Direct 是加法语义）。
//   ② **覆盖 = 就地写曲线**（AnimationUtility.SetEditorCurve + SetDirty + SaveAssets），
//      **绝不 delete + create** —— 删了资产引用就断了。名字/路径也不动。
//   ③ 状态词固定 `On`/`Off`，只出现在第 3 段；`__` 是层级分隔（键名里别出现双下划线）。
//
// ⚠️ 已知会对不上直觉的地方：
//   · GB/GBC 是**离散量**：曲线是浮点，但被 Direct 用中间权重混合时行为不定 ⇒ 只挂 0/1 权重。
//   · 根上如果挂了 `Animator`，根的 `__GB__Off` = 把播动画的物体自己关掉 ⇒ 那是**单向锁**（本工具会提示）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    internal sealed class HoSwitchAnimationWindow : EditorWindow
    {
        private sealed class Entry
        {
            public GameObject Go;
            public bool On = true;
            public int ShapeKeys;
            public int Components;
        }

        private GameObject _prefab;
        private string _folder = "Assets/HoSwitchClips";
        private bool _genBs = true;
        private bool _genGb = true;
        private bool _genGbc;
        private bool _skipEmpty = true;
        private bool _overwrite = true;
        private readonly List<Entry> _list = new List<Entry>();
        private Vector2 _scroll;
        private string _status = "";
        private string _report = "";

        [MenuItem("HoUnityTools/开关动画生成器", false, 6)]
        private static void Open()
        {
            var w = GetWindow<HoSwitchAnimationWindow>(false, "开关动画生成器", true);
            w.minSize = new Vector2(560f, 400f);
            w.Show();
        }

        private void OnGUI()
        {
            // ── 第一排：预制件 + 输出文件夹 ────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                _prefab = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("预制件", "要生成开关动画的预制件（资产或场景物体都行）。"),
                    _prefab, typeof(GameObject), true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField(
                    new GUIContent("文件夹", "片段放哪（不存在会建）。"), _folder);
                if (GUILayout.Button("选…", GUILayout.Width(42f)))
                {
                    string picked = EditorUtility.OpenFolderPanel("开关动画放哪", _folder, "");
                    if (!string.IsNullOrEmpty(picked)) _folder = ToAssetPath(picked);
                }
            }

            // ── 第二排：三族开关（永远成对生成，所以没有 0/1 子开关）────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                _genBs = EditorGUILayout.ToggleLeft(new GUIContent("形态键动画", "BS：每根键一对，值 100 / 0（全 prefab 一起）。"), _genBs, GUILayout.Width(120f));
                _genGb = EditorGUILayout.ToggleLeft(new GUIContent("物体动画", "GB：每个物体的 m_IsActive 1 / 0。"), _genGb, GUILayout.Width(120f));
                _genGbc = EditorGUILayout.ToggleLeft(new GUIContent("组件动画", "GBC：每个组件的 m_Enabled 1 / 0（默认关，量很大）。"), _genGbc, GUILayout.Width(120f));
            }

            // ── 第三排：功能开关 ──────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                _skipEmpty = EditorGUILayout.ToggleLeft(
                    new GUIContent("跳过空物体", "没挂任何组件的物体（只有 Transform）不生成 GB/GBC。"), _skipEmpty, GUILayout.Width(120f));
                _overwrite = EditorGUILayout.ToggleLeft(
                    new GUIContent("覆盖动画", "就地重写已有片段（不删资产、引用不丢）；关掉则已存在的跳过。"), _overwrite, GUILayout.Width(120f));
            }

            EditorGUILayout.Space();

            // ── 功能按钮 ─────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("添加物体", "把当前选中的场景物体加进列表。")))
                {
                    foreach (GameObject go in Selection.gameObjects)
                        if (go != null && _list.All(e => e.Go != go)) { var e = Make(go); if (e != null) _list.Add(e); }
                }
                if (GUILayout.Button(new GUIContent("清空", "清空物体列表。"))) _list.Clear();
                if (GUILayout.Button(new GUIContent("从根填充", "以预制件为根，把它下面所有物体填进列表。"))) FillFromRoot();
            }

            EditorGUILayout.Space();

            // ── 第四排：生成 ─────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.75f, 1f, 0.75f);
                if (GUILayout.Button(new GUIContent("生 成", "生成选中的三族开关动画；已存在片段按「覆盖动画」决定重写还是跳过。"), GUILayout.Height(30f)))
                    Generate();
                GUI.backgroundColor = Color.white;
                GUI.backgroundColor = new Color(0.86f, 0.92f, 1f);
                if (GUILayout.Button(new GUIContent("生成静置姿态动画",
                        "弹保存窗口，把当前姿势 k 成一份单帧片段：根下每个 Transform 的本地 position/rotation/scale 都写一遍。\n" +
                        "根物体自己不写（角色位置交给别处）；空节点也写（骨骼多半是空节点）。\n" +
                        "⚠️ 要「场景里摆好的姿势」就拖场景物体；拖预制件资产则用它存储的姿势。"),
                        GUILayout.Height(30f)))
                    GeneratePose();
                GUI.backgroundColor = Color.white;
            }

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
            if (!string.IsNullOrEmpty(_report))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    EditorGUILayout.LabelField(_report, EditorStyles.miniLabel);

            // ── 物体列表 ─────────────────────────────────────────────────────
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("物体列表（" + _list.Count + "）", EditorStyles.boldLabel);
                float h = Mathf.Max(80f, position.height - 300f);
                using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.Height(h)))
                {
                    _scroll = sv.scrollPosition;
                    for (int i = 0; i < _list.Count; i++)
                    {
                        Entry e = _list[i];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            e.On = EditorGUILayout.Toggle(e.On, GUILayout.Width(18f));
                            EditorGUILayout.ObjectField(e.Go, typeof(GameObject), true, GUILayout.Width(150f));
                            EditorGUILayout.LabelField(PathOf(e.Go.transform, Root()), EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            EditorGUILayout.LabelField("B " + e.ShapeKeys + " · C " + e.Components, EditorStyles.miniLabel, GUILayout.Width(90f));
                            if (GUILayout.Button("×", GUILayout.Width(20f))) { _list.RemoveAt(i); break; }
                        }
                    }
                }
            }
        }

        // ── 列表维护 ─────────────────────────────────────────────────────────
        private Transform Root() { return _prefab != null ? _prefab.transform : null; }

        private Entry Make(GameObject go)
        {
            if (go == null) return null;
            if (Root() != null && !go.transform.IsChildOf(Root()) && go.transform != Root())
            {
                _status = "「" + go.name + "」不在预制件下面 —— 路径会算不出来，先跳过。";
                return null;
            }
            return new Entry
            {
                Go = go,
                ShapeKeys = go.GetComponent<SkinnedMeshRenderer>() != null
                    ? go.GetComponent<SkinnedMeshRenderer>().sharedMesh != null
                        ? go.GetComponent<SkinnedMeshRenderer>().sharedMesh.blendShapeCount : 0
                    : 0,
                Components = go.GetComponents<Component>().Count(c => c != null && !(c is Transform)),
            };
        }

        private void FillFromRoot()
        {
            if (_prefab == null) { _status = "先填预制件。"; return; }
            _list.Clear();
            foreach (Transform t in _prefab.GetComponentsInChildren<Transform>(true))
            {
                Entry e = Make(t.gameObject);
                if (e != null) _list.Add(e);
            }
            _status = "从根填充：" + _list.Count + " 个物体。";
        }

        // ── 生成 ─────────────────────────────────────────────────────────────
        private void Generate()
        {
            if (_prefab == null) { _status = "先填预制件。"; return; }
            if (string.IsNullOrEmpty(_folder)) { _status = "先填输出文件夹。"; return; }
            if (!_genBs && !_genGb && !_genGbc) { _status = "三族全关着，没东西可生成。"; return; }

            bool isAsset = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_prefab));
            GameObject root = isAsset ? PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(_prefab)) : _prefab;
            try
            {
                EnsureFolder(_folder);
                string pf = root.name;
                int created = 0, updated = 0, skipped = 0, warnAnimatorRoot = 0;
                var expected = new HashSet<string>(StringComparer.Ordinal);
                var log = new StringBuilder();

                if (root.GetComponent<Animator>() != null && _genGb)
                {
                    warnAnimatorRoot = 1;
                    log.AppendLine("⚠️ 根上挂着 Animator：根的 __GB__Off 会把播动画的物体自己关掉（单向锁，关了回不来）——只能当一次性关用。");
                }

                // ① BS：全 prefab 的形态键，一根一对
                if (_genBs)
                {
                    var keys = new SortedDictionary<string, List<EditorCurveBinding>>(StringComparer.Ordinal);
                    foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Mesh mesh = smr.sharedMesh;
                        if (mesh == null) continue;
                        for (int k = 0; k < mesh.blendShapeCount; k++)
                        {
                            string key = mesh.GetBlendShapeName(k);
                            List<EditorCurveBinding> list;
                            if (!keys.TryGetValue(key, out list)) keys[key] = list = new List<EditorCurveBinding>();
                            list.Add(EditorCurveBinding.FloatCurve(PathOf(smr.transform, root.transform), typeof(SkinnedMeshRenderer), "blendShape." + key));
                        }
                    }
                    foreach (var kv in keys)
                    {
                        Count(Write(root.name, "BS", "On", new[] { kv.Key }, kv.Value, 100f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                        Count(Write(root.name, "BS", "Off", new[] { kv.Key }, kv.Value, 0f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                    }
                    log.AppendLine("BS：键 " + keys.Count + " 根 ⇒ " + (keys.Count * 2) + " 个片段。");
                }

                // ② GB：列表里每个物体的 m_IsActive
                if (_genGb)
                {
                    int n = 0;
                    foreach (Entry e in _list.Where(x => x.On && x.Go != null))
                    {
                        if (_skipEmpty && e.Components == 0) { skipped += 2; continue; }
                        var b = new List<EditorCurveBinding> { EditorCurveBinding.FloatCurve(PathOf(e.Go.transform, root.transform), typeof(GameObject), "m_IsActive") };
                        string[] path = NamePath(root.name, e.Go.transform, root.transform);
                        Count(Write(root.name, "GB", "On", path, b, 1f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                        Count(Write(root.name, "GB", "Off", path, b, 0f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                        n++;
                    }
                    log.AppendLine("GB：物体 " + n + " 个 ⇒ " + (n * 2) + " 个片段。");
                }

                // ③ GBC：列表里每个物体、每个组件的 m_Enabled
                if (_genGbc)
                {
                    int n = 0;
                    foreach (Entry e in _list.Where(x => x.On && x.Go != null))
                    {
                        Component[] comps = e.Go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToArray();
                        if (_skipEmpty && comps.Length == 0) continue;
                        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (Component c in comps)
                        {
                            if (c is Animator) { log.AppendLine("⚠️ 跳过 " + PathOf(c.transform, root.transform) + " 上的 Animator（关它 = 停掉动画器本体）。"); continue; }
                            if (!(c is Behaviour) && !(c is Renderer)) continue;
                            string type = c.GetType().Name;
                            int idx; byType.TryGetValue(type, out idx); byType[type] = idx + 1;
                            string token = type + (idx > 0 ? "#" + idx : "");
                            var b = new List<EditorCurveBinding> { EditorCurveBinding.FloatCurve(PathOf(c.transform, root.transform), c.GetType(), "m_Enabled") };
                            string[] path = NamePath(root.name, c.transform, root.transform).Concat(new[] { token }).ToArray();
                            Count(Write(root.name, "GBC", "On", path, b, 1f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                            Count(Write(root.name, "GBC", "Off", path, b, 0f, _folder, _overwrite, expected), ref created, ref updated, ref skipped);
                            n++;
                        }
                    }
                    log.AppendLine("GBC：组件 " + n + " 个 ⇒ " + (n * 2) + " 个片段。");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var orphans = new List<string>();
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { _folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    string nm = Path.GetFileNameWithoutExtension(p);
                    if (!nm.StartsWith(root.name + "__", StringComparison.Ordinal)) continue;
                    if (!expected.Contains(nm)) orphans.Add(nm);
                }
                _report = log.ToString()
                    + "新建 " + created + " · 覆盖 " + updated + " · 跳过 " + skipped
                    + (warnAnimatorRoot > 0 ? " · ⚠️ 根有 Animator" : "") + "\n"
                    + "孤儿（这次没生成、但文件夹里还在，只报不删）：" + (orphans.Count == 0 ? "无" : orphans.Count + " 个\n  " + string.Join("\n  ", orphans.Take(12)));
                _status = "生成完成。";
            }
            finally
            {
                if (isAsset) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 生成"静置姿态"片段：**先弹保存窗口**（默认名 `&lt;预制件&gt;__POSE__静置.anim`、默认目录 = 第一排那个文件夹），
        /// 然后把根下每个 Transform 的本地 TRS 按**当前值**写成单帧常量，就地覆盖（引用不丢）。
        /// 用途：叶子槽里那些"什么都不该动"的状态 —— 播它，角色就定在当前姿势上，
        /// 不会被 Write Defaults 的默认值（模型自带的浮空姿势）拽走。
        /// ⚠️ 根物体自己的 Transform **不写**（角色位置/朝向交给别处）；空节点**要写**（骨骼多半没组件）。
        /// ⚠️ 姿势来源：拖场景里的物体 ⇒ 用场景里摆好的；拖预制件资产 ⇒ 用它存储的。
        /// </summary>
        private void GeneratePose()
        {
            if (_prefab == null) { _status = "先填预制件（要姿势片段就拖场景里那个摆好姿势的物体）。"; return; }

            bool isAsset = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(_prefab));

            // 先弹保存窗口（还没加载任何东西，取消也不脏）
            string dir = string.IsNullOrEmpty(_folder) ? "Assets" : _folder;
            string picked = EditorUtility.SaveFilePanel("静置姿态片段保存到哪", dir,
                                                        _prefab.name + "__POSE__静置", "anim");
            if (string.IsNullOrEmpty(picked)) { _status = "已取消（没生成）。"; return; }
            string assetPath = ToAssetPath(picked);
            int slash = assetPath.LastIndexOf('/');
            if (slash > 0) EnsureFolder(assetPath.Substring(0, slash));

            GameObject root = isAsset ? PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(_prefab)) : _prefab;
            try
            {
                var bindings = new List<EditorCurveBinding>();
                var values = new List<float>();
                foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr == root.transform) continue;
                    string path = PathOf(tr, root.transform);
                    Vector3 p = tr.localPosition;
                    Quaternion q = tr.localRotation;
                    Vector3 s = tr.localScale;
                    var one = new (string, float)[]
                    {
                        ("m_LocalPosition.x", p.x), ("m_LocalPosition.y", p.y), ("m_LocalPosition.z", p.z),
                        ("m_LocalRotation.x", q.x), ("m_LocalRotation.y", q.y),
                        ("m_LocalRotation.z", q.z), ("m_LocalRotation.w", q.w),
                        ("m_LocalScale.x", s.x), ("m_LocalScale.y", s.y), ("m_LocalScale.z", s.z),
                    };
                    foreach (var item in one)
                    {
                        bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), item.Item1));
                        values.Add(item.Item2);
                    }
                }

                WriteCurves(assetPath, bindings, values, _overwrite);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _report = "静置姿态：" + assetPath + "\n" + bindings.Count + " 条曲线 / " + (bindings.Count / 10) + " 个物体"
                          + (isAsset ? "\n⚠️ 用的是预制件里存储的姿势（想用场景里摆好的，请拖场景物体）" : "");
                _status = "静置姿态生成完成。";
            }
            finally
            {
                if (isAsset) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>和 <see cref="Write"/> 同一套"就地改"规则，只是每条曲线的值各自给、路径由调用方给全。</summary>
        private static void WriteCurves(string assetPath, List<EditorCurveBinding> bindings, List<float> values,
                                        bool overwrite)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(assetPath) };
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            else if (!overwrite)
            {
                return;   // 已存在且不覆盖 ⇒ 跳过（引用保持不动）
            }

            var wanted = new HashSet<string>(bindings.Select(BindingKey), StringComparer.Ordinal);
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                if (!wanted.Contains(BindingKey(b))) AnimationUtility.SetEditorCurve(clip, b, null);
            for (int i = 0; i < bindings.Count; i++)
                AnimationUtility.SetEditorCurve(clip, bindings[i], new AnimationCurve(new Keyframe(0f, values[i])));
            clip.frameRate = 60f;
            EditorUtility.SetDirty(clip);
        }

        private static void Count(int status, ref int created, ref int updated, ref int skipped)
        {
            if (status == 0) skipped++;
            else if (status == 1) created++;
            else updated++;
        }

        // ── 命名与写盘 ───────────────────────────────────────────────────────
        private static string[] NamePath(string prefabName, Transform t, Transform root)
        {
            var levels = new List<string> { prefabName };
            string rel = PathOf(t, root);
            if (!string.IsNullOrEmpty(rel)) levels.AddRange(rel.Split('/'));
            return levels.ToArray();
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == null || root == null || t == root) return "";
            var parts = new List<string>();
            Transform cur = t;
            while (cur != null && cur != root) { parts.Insert(0, cur.name); cur = cur.parent; }
            return string.Join("/", parts);
        }

        private static int Write(string prefabName, string cat, string state, string[] path,
                                 List<EditorCurveBinding> bindings, float value, string folder, bool overwrite, HashSet<string> expected)
        {
            string name = string.Join("__", new[] { prefabName, cat, state }.Concat(path).ToArray());
            expected.Add(name);
            string assetPath = folder + "/" + name + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            bool isNew = clip == null;
            if (isNew)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            else if (!overwrite)
            {
                return 0;   // 已存在且不覆盖 ⇒ 跳过（引用保持不动）
            }

            // 就地改：只保留本片段的绑定，其余删掉（不是删资产）
            var wanted = new HashSet<string>(bindings.Select(BindingKey), StringComparer.Ordinal);
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                if (!wanted.Contains(BindingKey(b))) AnimationUtility.SetEditorCurve(clip, b, null);
            foreach (EditorCurveBinding b in bindings)
                AnimationUtility.SetEditorCurve(clip, b, new AnimationCurve(new Keyframe(0f, value)));
            clip.frameRate = 60f;
            EditorUtility.SetDirty(clip);
            return isNew ? 1 : 2;
        }

        private static string BindingKey(EditorCurveBinding b)
        {
            return b.path + "|" + b.type.Name + "|" + b.propertyName;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
        }

        private static string ToAssetPath(string absolute)
        {
            string data = Application.dataPath.Replace('\\', '/');
            absolute = absolute.Replace('\\', '/');
            int at = absolute.IndexOf(data, StringComparison.Ordinal);
            return at == 0 ? "Assets" + absolute.Substring(data.Length) : absolute;
        }
    }
}
