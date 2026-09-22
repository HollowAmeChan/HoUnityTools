using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 混合树小工具：把"一族形态键 × 一个二维参数"快速落成一棵 2D FreeformCartesian 树。
    ///
    /// **它是通用的**，只是默认值填的是果冻那对参数。果冻眼是它的第一个用法 ——
    /// 物理（我们的 C#，每帧写两个 Float）与映射（这棵树，用户自己选键摆位置）在这里正式分开。
    ///
    /// 三件事这个工具替用户兜住，因为它们都是"手搓树时一定会踩、踩了还看不出来"的：
    ///   1. **原点子节点**：没有它，参数在 (0,0) 时脸上挂着四个方向的加权平均；
    ///   2. **每个方向写全部键**：不留"某个键在这个方向没人管"的空洞；
    ///   3. **键冲突提示**：ARKit 的键已经被驱动树占着，两棵树会互相掺和（具体混法未实测）。
    /// </summary>
    public sealed class HoFaceBlendTreeWindow : EditorWindow
    {
        [MenuItem("HoUnityTools/面捕混合树", false, 41)]
        private static void Open()
        {
            var window = GetWindow<HoFaceBlendTreeWindow>("面捕混合树");
            window.minSize = new Vector2(520f, 460f);
        }

        [SerializeField] private Animator animator;
        [SerializeField] private AnimatorController controller;
        [SerializeField] private HoBlendTreePlan plan = new HoBlendTreePlan();
        [SerializeField] private List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private List<HoBlendTreeKey> candidates = new List<HoBlendTreeKey>();
        [SerializeField] private HoFaceTrackingDebugger previewRig;

        [SerializeField] private bool showTarget = true;
        [SerializeField] private bool showKeys = true;
        [SerializeField] private bool showDirections = true;
        [SerializeField] private bool showPreview = true;
        [SerializeField] private string search = "";
        [SerializeField] private bool onlySelected;
        [SerializeField] private bool previewOn;
        [SerializeField] private float previewX;
        [SerializeField] private float previewY;
        [SerializeField] private bool previewSession;

        private Vector2 body, keyScroll, directionScroll;
        private string message = "";
        private MessageType messageType = MessageType.None;

        // ── 数据维护 ──────────────────────────────────────────────────────────

        private void PickAnimator(Animator next)
        {
            animator = next;
            if (animator == null) return;
            // 面捕的控制器挂在组件上、不一定挂在 Animator 上，先问组件。
            var rig = animator.GetComponent<HoFaceTrackingDebugger>();
            if (controller == null && rig != null && rig.faceController is AnimatorController fromRig) controller = fromRig;
            if (controller == null && animator.runtimeAnimatorController is AnimatorController current) controller = current;
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

        // ── 主界面 ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            HoConstraintEditorControls.Title(
                "面捕混合树",
                plan.layerName,
                (previewOn ? "预览中" : "未预览", previewOn),
                (plan.keys.Count + " 键", plan.keys.Count > 0),
                (plan.directions.Count + " 方向", plan.directions.Count > 0));

            body = EditorGUILayout.BeginScrollView(body);
            DrawTarget();
            DrawKeys();
            DrawDirections();
            DrawPreview();
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
        }

        private void DrawTarget()
        {
            if (!(showTarget = HoConstraintEditorControls.Section(ref showTarget, "目标", controller != null ? controller.name : "未指定", HoConstraintEditorTheme.AccentMesh)))
                return;
            using (HoConstraintEditorControls.Indent())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("角色", 40f, "曲线的绑定路径相对于这个 Animator 的根节点算。");
                    PickAnimator((Animator)EditorGUILayout.ObjectField(animator, typeof(Animator), true));
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("控制器", 40f, "树写进这个控制器资产。通常就是组件上的「面部控制器」。");
                    controller = (AnimatorController)EditorGUILayout.ObjectField(controller, typeof(AnimatorController), false);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("图层", 40f, "写进这个图层（同名整层重建，位置不变）。别用 Ho/00 Drive 或 Ho/99 (EDIT THIS)。");
                    plan.layerName = EditorGUILayout.TextField(plan.layerName);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("参数", 40f, "横纵两个 Float 参数。缺了会自动建，类型不对会报错。");
                    plan.parameterX = EditorGUILayout.TextField(plan.parameterX);
                    HoConstraintEditorControls.Gap(4f);
                    plan.parameterY = EditorGUILayout.TextField(plan.parameterY);
                }

                if (GUILayout.Button("收网格 + 扫形态键", HoConstraintEditorTheme.Button))
                    ScanMeshes();
                HoConstraintEditorControls.Caption(
                    "收的是「角色」下所有网格的形态键。手工加网格也可以，直接往下面的列表里拖。");
            }
        }

        private void DrawKeys()
        {
            string summary = plan.keys.Count + " / " + candidates.Count;
            showKeys = HoConstraintEditorControls.Section(ref showKeys, "键", summary, HoConstraintEditorTheme.AccentOutput);
            if (!showKeys) return;

            using (HoConstraintEditorControls.Indent())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("网格", 40f, "参与扫描的网格列表。");
                    if (GUILayout.Button("＋", HoConstraintEditorTheme.IconButton, GUILayout.Width(18f))) meshes.Add(null);
                    HoConstraintEditorControls.Caption(meshes.Count + " 个");
                }

                for (int i = 0; i < meshes.Count; i++)
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        meshes[i] = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(meshes[i], typeof(SkinnedMeshRenderer), true);
                        if (GUILayout.Button("✕", HoConstraintEditorTheme.IconButton, GUILayout.Width(18f))) meshes.RemoveAt(i--);
                    }
                }

                HoConstraintEditorControls.Separator(3f, 3f);
                using (HoConstraintEditorControls.Row())
                {
                    search = EditorGUILayout.TextField(search);
                    onlySelected = HoConstraintEditorControls.Toggle("只看已选", onlySelected, null, 76f);
                }

                if (candidates.Count == 0)
                {
                    HoConstraintEditorControls.Caption("还没有候选键 —— 先点上面的「收网格 + 扫形态键」。");
                }
                else
                {
                    keyScroll = EditorGUILayout.BeginScrollView(keyScroll, GUILayout.Height(140f));
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
                            if (Conflict(candidate))
                            {
                                HoConstraintEditorControls.Pill("ARKit", false);
                            }
                        }
                    }

                    EditorGUILayout.EndScrollView();
                }

                if (plan.keys.Count > 0)
                {
                    HoConstraintEditorControls.Caption("已选：" + KeyList());
                }
            }
        }

        private void DrawDirections()
        {
            showDirections = HoConstraintEditorControls.Section(ref showDirections, "方向", plan.directions.Count + " 个", HoConstraintEditorTheme.AccentRules);
            if (!showDirections) return;

            using (HoConstraintEditorControls.Indent())
            {
                using (HoConstraintEditorControls.Row())
                {
                    if (GUILayout.Button("四角布局", HoConstraintEditorTheme.Button)) ResetCorners();
                    if (GUILayout.Button("＋ 方向", HoConstraintEditorTheme.Button))
                    {
                        var direction = new HoBlendTreeDirection { name = "方向 " + (plan.directions.Count + 1), position = Vector2.zero };
                        direction.Fit(plan.keys.Count);
                        plan.directions.Add(direction);
                    }

                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Label("原点", 30f, "自动补一个位于 (0,0)、全写 0 的子节点。");
                    plan.originChild = HoConstraintEditorControls.Toggle("", plan.originChild, null, 0f);
                }

                HoConstraintEditorControls.Caption(plan.originChild && plan.HasOrigin
                    ? "参数空间按 [-1, 1] 摆位置；原点已经有子节点了，不会重复补。"
                    : "参数空间按 [-1, 1] 摆位置。原点子节点保证参数归零时姿势也归零。");

                if (plan.keys.Count == 0)
                {
                    HoConstraintEditorControls.Caption("先在上面选键，每个方向才会出现可填的权重。");
                    return;
                }

                directionScroll = EditorGUILayout.BeginScrollView(directionScroll, GUILayout.Height(190f));
                for (int d = 0; d < plan.directions.Count; d++)
                {
                    var direction = plan.directions[d];
                    using (HoConstraintEditorControls.Row())
                    {
                        direction.name = EditorGUILayout.TextField(direction.name, GUILayout.Width(72f));
                        direction.position.x = EditorGUILayout.FloatField(direction.position.x, GUILayout.Width(44f));
                        direction.position.y = EditorGUILayout.FloatField(direction.position.y, GUILayout.Width(44f));
                        HoConstraintEditorControls.Caption("x / y");
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
                                direction.SetWeight(k, EditorGUILayout.FloatField(direction.Weight(k), GUILayout.Width(48f)));
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPreview()
        {
            showPreview = HoConstraintEditorControls.Section(ref showPreview, "预览", previewSession ? "走会话" : "未接会话", HoConstraintEditorTheme.AccentDriver);
            if (!showPreview) return;

            using (HoConstraintEditorControls.Indent())
            {
                using (HoConstraintEditorControls.Row())
                {
                    bool was = previewOn;
                    previewOn = HoConstraintEditorControls.Toggle("预览", previewOn, "把滑条的值喂给正在跑的面捕会话，走完整条管线再抄到真模型上。");
                    if (was && !previewOn) HoFaceInputHub.Session(previewRig)?.ClearPreviews();
                    HoConstraintEditorControls.Label(plan.parameterX, 60f, null);
                    previewX = EditorGUILayout.Slider(previewX, -1f, 1f, GUILayout.Width(150f));
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Label(plan.parameterY, 60f, null);
                    previewY = EditorGUILayout.Slider(previewY, -1f, 1f, GUILayout.Width(150f));
                }

                if (previewOn && !previewSession)
                    HoConstraintEditorControls.Caption("没找到这个 Animator 上正在跑的会话 —— 进播放模式、启动面捕，预览才会走到真模型上。");

                HoConstraintEditorControls.Separator(4f, 4f);
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("写入控制器", "参数 + 图层 + 树 + 每个方向的片段，全部落盘。这是本工具唯一的写盘点。", true))
                        Write();
                    if (HoConstraintEditorControls.Button("看看会写什么"))
                        Say(Describe(), MessageType.Info);
                }
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

        /// <summary>
        /// 这个键是不是已经在驱动树手里。驱动树驱动的是 52 个标准 ARKit 键里、模型上真实存在的那些，
        /// 所以"名字是 ARKit 的"就是冲突的充分征兆。
        /// </summary>
        private static bool Conflict(HoBlendTreeKey candidate) =>
            HoFaceTrackingChannels.IndexOf(candidate.shape) >= 0;

        private string KeyList()
        {
            var text = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var key in plan.keys)
            {
                if (shown++ == 8)
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
            text.Append("图层 ").Append(plan.layerName)
                .Append("；参数 ").Append(plan.parameterX).Append(" / ").Append(plan.parameterY)
                .Append("；键 ").Append(plan.keys.Count).Append(" 个；方向 ").Append(plan.directions.Count).Append(" 个");
            if (plan.originChild && !plan.HasOrigin) text.Append("；自动补原点子节点");
            int conflicts = 0;
            foreach (var key in plan.keys)
                if (Conflict(key)) conflicts++;
            if (conflicts > 0)
                text.Append("。注意：有 ").Append(conflicts)
                    .Append(" 个键是 ARKit 键，驱动树也在写它们 —— 两棵树会互相掺和（不是相加），"
                        + "果冻应该驱动自己的键（例如一个专用的 JellyEye）。");
            return text.ToString();
        }

        private void Write()
        {
            try
            {
                HoFaceBlendTreeTool.Write(animator, controller, plan);
                Say("已写入 " + plan.layerName + "（" + plan.keys.Count + " 键 × " + plan.directions.Count + " 方向）。", MessageType.Info);
            }
            catch (System.Exception exception)
            {
                Say(exception.Message, MessageType.Warning);
            }
        }
    }
}
