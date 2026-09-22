using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 混合树工具：把"一族形态键 × 一个二维参数"快速落成一棵 2D FreeformCartesian 树。
    ///
    /// **它是通用的**，只是默认值填的是果冻那对参数。果冻眼是它的第一个用法 ——
    /// 物理（我们的 C#，每帧写两个 Float）与映射（这棵树，用户自己选键摆位置）在这里正式分开。
    ///
    /// 版面照抄 <see cref="HoFaceTrackingWindow"/>：标题行（胶囊只放"当前挡路的状态"）+
    /// <see cref="HoConstraintEditorSectionGui.DrawSectionHeader"/> 折叠分区 + <c>Card()</c> 包内容。
    /// 四个分区按操作顺序编号，**默认全展开**；折叠起来时摘要仍要说出这一步缺什么 ——
    /// 第一版把入口按钮放进默认折叠的分区里，用户打开面板只看到一片分区头，找不到下一步。
    /// </summary>
    public sealed class HoFaceBlendTreeWindow : EditorWindow
    {
        private const string WindowTitle = "混合树工具";

        [MenuItem("HoUnityTools/混合树工具", false, 41)]
        public static void Open() => GetWindow<HoFaceBlendTreeWindow>(WindowTitle);

        [SerializeField] private Animator animator;
        [SerializeField] private AnimatorController controller;
        [SerializeField] private HoBlendTreePlan plan = new HoBlendTreePlan();
        [SerializeField] private List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private List<HoBlendTreeKey> candidates = new List<HoBlendTreeKey>();
        [SerializeField] private HoFaceTrackingDebugger previewRig;
        [SerializeField] private string search = "";
        [SerializeField] private bool onlySelected;
        [SerializeField] private bool previewOn;
        [SerializeField] private float previewX;
        [SerializeField] private float previewY;
        [SerializeField] private bool previewSession;
        [SerializeField] private bool targetExpanded = true;
        [SerializeField] private bool keysExpanded = true;
        [SerializeField] private bool directionsExpanded = true;
        [SerializeField] private bool writeExpanded = true;

        private Vector2 body, keyScroll, directionScroll;
        private string message = "";
        private MessageType messageType = MessageType.None;
        private static GUIStyle hint;
        private static GUIStyle warning;

        private void OnEnable() => minSize = new Vector2(520f, 460f);

        // ── 数据维护 ──────────────────────────────────────────────────────────

        private void PickAnimator(Animator next)
        {
            bool changed = animator != next;
            animator = next;
            if (animator == null) return;
            // 面捕的控制器挂在组件上、不一定挂在 Animator 上，先问组件。
            var rig = animator.GetComponent<HoFaceTrackingDebugger>();
            if (controller == null && rig != null && rig.faceController is AnimatorController fromRig) controller = fromRig;
            if (controller == null && animator.runtimeAnimatorController is AnimatorController current) controller = current;
            // 换角色就顺手把键收一遍：面板打开来就该是有东西可选的样子。
            if (changed && meshes.Count == 0) ScanMeshes();
        }

        private void ScanMeshes()
        {
            meshes.Clear();
            if (animator != null)
                foreach (var mesh in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (mesh.sharedMesh != null && mesh.GetComponentInParent<Animator>() == animator) meshes.Add(mesh);
            ScanKeys();
        }

        private void ScanKeys()
        {
            candidates.Clear();
            candidates.AddRange(HoFaceBlendTreeTool.ScanKeys(meshes));
            SortCandidates();
            // 用**新扫出来的实例**替换旧引用，但顺序严格沿用 plan.keys ——
            // 方向的权重是按位置对齐的，重扫一次把顺序洗了，等于把用户填的权重全打乱。
            var byId = new Dictionary<(SkinnedMeshRenderer, int), HoBlendTreeKey>();
            foreach (var candidate in candidates) byId[(candidate.renderer, candidate.index)] = candidate;
            var next = new List<HoBlendTreeKey>();
            foreach (var old in plan.keys)
                if (byId.TryGetValue((old.renderer, old.index), out var candidate))
                {
                    candidate.selected = true;
                    next.Add(candidate);
                }

            plan.keys.Clear();
            plan.keys.AddRange(next);
            plan.FitKeys();
        }

        private void SortCandidates()
        {
            candidates.Sort((a, b) =>
            {
                int byMesh = string.CompareOrdinal(a.renderer != null ? a.renderer.name : "", b.renderer != null ? b.renderer.name : "");
                return byMesh != 0 ? byMesh : a.index.CompareTo(b.index);
            });
        }

        /// <summary>加键一律**追加**：权重是按位置对齐的，插在中间会让已有的权重整体错位。</summary>
        private void AddKey(HoBlendTreeKey key)
        {
            if (plan.keys.Contains(key)) return;
            plan.keys.Add(key);
            plan.FitKeys();
        }

        private void RemoveKeyAt(int index)
        {
            if (index < 0 || index >= plan.keys.Count) return;
            plan.keys[index].selected = false;
            plan.keys.RemoveAt(index);
            foreach (var direction in plan.directions)
            {
                var weights = new List<float>(direction.weights ?? new float[0]);
                if (index < weights.Count) weights.RemoveAt(index);
                direction.weights = weights.ToArray();
            }

            plan.FitKeys();
        }

        private void ResetCorners()
        {
            plan.directions.Clear();
            for (int i = 0; i < HoFaceBlendTreeTool.Corners.Length; i++)
            {
                var direction = new HoBlendTreeDirection
                {
                    name = HoFaceBlendTreeTool.CornerNames[i],
                    position = HoFaceBlendTreeTool.Corners[i]
                };
                direction.Fit(plan.keys.Count);
                plan.directions.Add(direction);
            }
        }

        private void Say(string text, MessageType type = MessageType.Info)
        {
            message = text;
            messageType = type;
            Repaint();
        }

        // ── 版面零件 ──────────────────────────────────────────────────────────

        /// <summary>灰字提示：会换行，所以可以写完整句子。只用在"这一步缺什么"上。</summary>
        private static void Hint(string text)
        {
            if (hint == null)
            {
                hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, richText = false, wordWrap = true };
                hint.normal.textColor = HoConstraintEditorTheme.TextFaintColor;
            }

            GUILayout.Label(text, hint);
        }

        /// <summary>真问题：折叠起来也要说（照抄 siblings 的规矩，别把问题藏进折叠里）。</summary>
        private static void Warn(string text)
        {
            if (warning == null)
            {
                warning = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, richText = false, wordWrap = true };
                warning.normal.textColor = HoConstraintEditorTheme.WarningColor;
            }

            GUILayout.Label(text, warning);
        }

        private static string TextField(string value, float width = 0f, string tooltip = null)
        {
            Rect rect = width > 0f ? HoConstraintEditorControls.Next(width) : HoConstraintEditorControls.NextFlexible(90f);
            if (!string.IsNullOrEmpty(tooltip)) GUI.Label(rect, new GUIContent(string.Empty, tooltip));
            return EditorGUI.TextField(rect, value, HoConstraintEditorTheme.Field);
        }

        private int ConflictCount()
        {
            int conflicts = 0;
            foreach (var key in plan.keys)
                if (HoFaceTrackingChannels.IndexOf(key.shape) >= 0) conflicts++;
            return conflicts;
        }

        // ── 主界面 ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawTitle();
            Hint("① 指定角色和控制器　→　② 勾出要驱动的形态键　→　③ 填每个方向的权重　→　④ 写入控制器");

            body = EditorGUILayout.BeginScrollView(body);
            DrawTarget();
            DrawKeys();
            DrawDirections();
            DrawWrite();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTitle()
        {
            string pill;
            bool ready;
            if (animator == null) { pill = "先指定角色"; ready = false; }
            else if (controller == null) { pill = "先指定控制器"; ready = false; }
            else if (plan.keys.Count == 0) { pill = "还没选键"; ready = false; }
            else if (plan.directions.Count == 0) { pill = "还没摆方向"; ready = false; }
            else { pill = "可以写入"; ready = true; }

            HoConstraintEditorControls.Title(WindowTitle, plan.layerName, (pill, ready));
        }

        // ① 目标 ────────────────────────────────────────────────────────────────
        private void DrawTarget()
        {
            string summary = animator == null ? "未指定角色"
                : controller == null ? "缺控制器"
                : controller.name;
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref targetExpanded, "① 目标", summary, HoConstraintEditorTheme.AccentDriver))
            {
                if (animator == null) Warn("还没指定角色 —— 曲线的绑定路径按它算，先把它拖上。");
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("角色", HoConstraintEditorTheme.LabelWidth, "曲线的绑定路径相对于这个 Animator 的根节点算。");
                    PickAnimator((Animator)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(120f), animator, typeof(Animator), true));
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("控制器", HoConstraintEditorTheme.LabelWidth, "树写进这个控制器资产。通常就是「面捕」组件上的面部控制器。");
                    controller = (AnimatorController)EditorGUI.ObjectField(HoConstraintEditorControls.NextFlexible(120f), controller, typeof(AnimatorController), false);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("图层", HoConstraintEditorTheme.LabelWidth, "同名整层重建，位置不变。别用 Ho/00 Drive 或 Ho/99 (EDIT THIS)。");
                    plan.layerName = TextField(plan.layerName);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("参数", HoConstraintEditorTheme.LabelWidth, "横纵两个 Float 参数。缺了自动建，类型不对会报错。");
                    plan.parameterX = TextField(plan.parameterX, HoConstraintEditorTheme.FieldWidthWide + 34f);
                    HoConstraintEditorControls.Gap();
                    plan.parameterY = TextField(plan.parameterY, HoConstraintEditorTheme.FieldWidthWide + 34f);
                }

                HoConstraintEditorControls.Caption("参数空间是 [-1, 1]：0 = 静止，正 = 一个方向，负 = 反方向。");
            }
        }

        // ② 选键 ────────────────────────────────────────────────────────────────
        private void DrawKeys()
        {
            string summary = plan.keys.Count + " / " + candidates.Count;
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref keysExpanded, "② 选键", summary, HoConstraintEditorTheme.AccentOutput))
            {
                // 收起也要说：ARKit 键被驱动树占着会互相掺和，这是真会挡路的问题。
                int conflicts = ConflictCount();
                if (conflicts > 0)
                    Warn("有 " + conflicts + " 个键是 ARKit 键，驱动树也在写它们 —— 两棵树会互相掺和（不是相加）。");
                else if (plan.keys.Count == 0) Warn("还没选键。");
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("收网格 + 扫形态键",
                            "把「角色」下所有 SkinnedMeshRenderer 及其形态键收进来。", true, 132f))
                        ScanMeshes();
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("＋ 网格", "手工加一个网格到列表里。", false, 66f)) meshes.Add(null);
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(meshes.Count + " 个网格");
                }

                for (int i = 0; i < meshes.Count; i++)
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        meshes[i] = (SkinnedMeshRenderer)EditorGUI.ObjectField(
                            HoConstraintEditorControls.NextFlexible(120f), meshes[i], typeof(SkinnedMeshRenderer), true);
                        if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18f))) meshes.RemoveAt(i--);
                    }
                }

                if (candidates.Count == 0)
                {
                    HoConstraintEditorControls.Gap(2f);
                    Hint(animator == null ? "先在①里指定角色。" : "点上面的「收网格 + 扫形态键」。");
                    return;
                }

                HoConstraintEditorControls.Separator(3f, 2f);
                using (HoConstraintEditorControls.Row())
                {
                    search = TextField(search);
                    HoConstraintEditorControls.Gap();
                    onlySelected = HoConstraintEditorControls.Toggle("只看已选", onlySelected, null, 76f);
                }

                keyScroll = EditorGUILayout.BeginScrollView(keyScroll, GUILayout.Height(126f));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    if (!Matches(candidate)) continue;
                    using (HoConstraintEditorControls.Row(true))
                    {
                        bool was = candidate.selected;
                        bool now = EditorGUILayout.Toggle(was, GUILayout.Width(14f));
                        if (now != was)
                        {
                            candidate.selected = now;
                            if (now) AddKey(candidate);
                            else RemoveKeyAt(plan.keys.IndexOf(candidate));
                        }

                        string meshName = candidate.renderer != null ? candidate.renderer.name : "?";
                        GUILayout.Label(meshName + " / " + candidate.shape, HoConstraintEditorTheme.Label);
                        HoConstraintEditorControls.Flex();
                        if (HoFaceTrackingChannels.IndexOf(candidate.shape) >= 0) HoConstraintEditorControls.Pill("ARKit", false);
                    }
                }

                EditorGUILayout.EndScrollView();

                int conflicts = ConflictCount();
                if (conflicts > 0)
                    Warn("勾中的键里有 " + conflicts + " 个是 ARKit 键，驱动树也在写它们 —— 两棵树会互相掺和（不是相加）。"
                        + "果冻这类东西该驱动自己的键。");
                else if (plan.keys.Count > 0)
                    HoConstraintEditorControls.Caption("已选 " + plan.keys.Count + " 个：" + KeyList());
            }
        }

        // ③ 摆方向 ──────────────────────────────────────────────────────────────
        private void DrawDirections()
        {
            string summary = plan.directions.Count + " 个方向";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref directionsExpanded, "③ 摆方向", summary, HoConstraintEditorTheme.AccentRules))
            {
                if (plan.keys.Count > 0 && plan.directions.Count == 0) Warn("还没摆方向。");
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("四角布局", "在 (±1, ±1) 摆好四个方向，权重清零。", false, 70f)) ResetCorners();
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("＋ 方向", "手工加一个方向。", false, 62f))
                    {
                        var direction = new HoBlendTreeDirection { name = "方向 " + (plan.directions.Count + 1), position = Vector2.zero };
                        direction.Fit(plan.keys.Count);
                        plan.directions.Add(direction);
                    }

                    HoConstraintEditorControls.Flex();
                    plan.originChild = HoConstraintEditorControls.Toggle("补原点", plan.originChild,
                        "在 (0,0) 自动补一个全写 0 的子节点。2D 树对权重归一化 —— 没有它，参数归零时脸上挂着各方向的加权平均。", 0f);
                }

                if (plan.directions.Count == 0)
                {
                    Hint("点「四角布局」一次摆好四个，或者用「＋ 方向」加一个。");
                    return;
                }

                if (plan.keys.Count == 0)
                {
                    Hint("先去②里选键，这里才会出现可填的权重。");
                    return;
                }

                HoConstraintEditorControls.Caption("每个方向 = 参数空间里的一个位置。参数走到那里，就按这列权重给姿势。");
                directionScroll = EditorGUILayout.BeginScrollView(directionScroll, GUILayout.Height(178f));
                for (int d = 0; d < plan.directions.Count; d++)
                {
                    var direction = plan.directions[d];
                    using (HoConstraintEditorControls.Row())
                    {
                        direction.name = TextField(direction.name, 72f);
                        HoConstraintEditorControls.Gap();
                        direction.position.x = HoConstraintEditorControls.NumberField(direction.position.x, null, "x", 40f);
                        direction.position.y = HoConstraintEditorControls.NumberField(direction.position.y, null, "y", 40f);
                        HoConstraintEditorControls.Flex();
                        if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18f)))
                        {
                            plan.directions.RemoveAt(d--);
                            break;
                        }
                    }

                    using (HoConstraintEditorControls.Indent())
                    {
                        for (int k = 0; k < plan.keys.Count; k++)
                        {
                            using (HoConstraintEditorControls.Row(true))
                            {
                                GUILayout.Label(plan.keys[k].shape, HoConstraintEditorTheme.LabelDim);
                                HoConstraintEditorControls.Flex();
                                direction.SetWeight(k, HoConstraintEditorControls.NumberField(direction.Weight(k), null, "0–100", 46f));
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // ④ 写入 ────────────────────────────────────────────────────────────────
        private void DrawWrite()
        {
            string summary = !previewOn ? "未预览" : previewSession ? "预览中" : "预览未接会话";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref writeExpanded, "④ 写入", summary, HoConstraintEditorTheme.AccentBlink))
            {
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    bool was = previewOn;
                    previewOn = HoConstraintEditorControls.Toggle("预览", previewOn,
                        "把滑条的值喂给正在跑的面捕会话，走完整条管线再抄到真模型上。", 50f);
                    if (was && !previewOn) HoFaceInputHub.Session(previewRig)?.ClearPreviews();
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Caption(plan.parameterX);
                    previewX = EditorGUI.Slider(HoConstraintEditorControls.NextFlexible(80f), previewX, -1f, 1f);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(plan.parameterY);
                    previewY = EditorGUI.Slider(HoConstraintEditorControls.NextFlexible(80f), previewY, -1f, 1f);
                }

                if (previewOn && !previewSession)
                    Hint("没找到这个 Animator 上正在跑的会话 —— 进播放模式、启动面捕，预览才会走到真模型上。");

                HoConstraintEditorControls.Separator(3f, 2f);
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("写入控制器",
                            "参数 + 图层 + 树 + 每个方向的片段，全部落盘。这是本工具唯一的写盘点。", true, 84f))
                        Write();
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("看看会写什么", null, false, 92f)) Say(Describe());
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption(plan.keys.Count + " 键 × " + plan.directions.Count + " 方向");
                }

                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
            }
        }

        private void Update()
        {
            if (!previewOn) return;
            var rig = animator != null ? animator.GetComponent<HoFaceTrackingDebugger>() : null;
            previewRig = rig;
            var session = HoFaceInputHub.Session(rig);
            previewSession = session != null;
            if (session == null) return;
            session.SetPreview(plan.parameterX, previewX);
            session.SetPreview(plan.parameterY, previewY);
            Repaint();
        }

        private void OnDisable()
        {
            // 窗口关了、或者脚本重编译：把预览值撤掉，免得滑条上的残留值一直压着真参数。
            HoFaceInputHub.Session(previewRig)?.ClearPreviews();
            previewOn = false;
        }

        // ── 小工具 ────────────────────────────────────────────────────────────

        private bool Matches(HoBlendTreeKey candidate)
        {
            if (onlySelected && !candidate.selected) return false;
            if (string.IsNullOrEmpty(search)) return true;
            return candidate.shape != null
                && candidate.shape.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string KeyList()
        {
            var text = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var key in plan.keys)
            {
                if (shown++ == 6)
                {
                    text.Append("…");
                    break;
                }

                if (text.Length > 0) text.Append("、");
                text.Append(key.shape);
            }

            return text.ToString();
        }

        private string Describe()
        {
            var text = new System.Text.StringBuilder();
            text.Append("写进 ").Append(plan.layerName)
                .Append("：参数 ").Append(plan.parameterX).Append(" / ").Append(plan.parameterY)
                .Append("；键 ").Append(plan.keys.Count).Append(" 个；方向 ").Append(plan.directions.Count).Append(" 个");
            if (plan.keys.Count > 0) text.Append("（").Append(KeyList()).Append("）");
            if (plan.originChild && !plan.HasOrigin) text.Append("；自动补一个原点子节点");
            int conflicts = ConflictCount();
            if (conflicts > 0)
                text.Append("。注意：有 ").Append(conflicts)
                    .Append(" 个键是 ARKit 键，驱动树也在写它们 —— 两棵树会互相掺和（不是相加）。");
            return text.ToString();
        }

        private void Write()
        {
            try
            {
                HoFaceBlendTreeTool.Write(animator, controller, plan);
                Say("已写入 " + plan.layerName + "（" + plan.keys.Count + " 键 × " + plan.directions.Count + " 方向）。");
            }
            catch (System.Exception exception)
            {
                Say(exception.Message, MessageType.Warning);
            }
        }
    }
}
