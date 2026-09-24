using System;
using System.Collections.Generic;
using System.Text;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **控制器编辑**：把一份**已经在工程里的控制器**就地装配成能跑的样子 ——
    /// 按槽位名把「动画文件夹」里的片段填进树与状态，再把形态键曲线的路径重绑到「调试对象」身上。
    ///
    /// 【为什么不在这里新建资产】
    /// 我们不知道你想把它放哪、叫什么，也不该替你决定。所以流程故意是：
    /// 菜单上的「打开目录」按钮打开**预置控制器目录** → 你自己复制一份到工程里自己管理 →
    /// 拖进这一页的「控制器」栏 → 原地装配。装配**不新建资产、不改名、不移动、不动 GUID**，
    /// 所以已经指向它的引用不会断。
    ///
    /// 【为什么不碰层与参数】
    /// 控制器里有几层、多少参数，是**作者的事**（你可以在 Unity 的 Animator 窗口里随便加）。
    /// Unity 的参数是**控制器级**的，所有层都吃得到，所以我们不需要为"给某一层传参"做任何事。
    /// 这一页只做两件事：填片段、重写曲线路径。
    ///
    /// 【重绑到什么程度】
    /// 目标网格上**没有**的形态键：曲线原样留着（作者的格子数据不丢），但落不到任何网格上 ——
    /// 详情里会点名，那是"这份控制器跟这个角色不匹配"的可见证据。
    /// </summary>
    public sealed class HoFaceControllerToolWindow : EditorWindow
    {
        /// <summary>预置控制器目录：跟本脚本同级的一个子目录（包内 `Editor/FaceTracking/Controllers`）。</summary>
        private const string StockFolderName = "Controllers";

        private Vector2 scroll;
        private string status = "";
        private bool statusIsError;
        private bool animationExpanded = true;

        [MenuItem("HoUnityTools/面捕/控制器编辑", false, 20)]
        public static void ShowWindow() => GetWindow<HoFaceControllerToolWindow>("控制器编辑");

        private static HoFaceDebugSettings Settings { get { return HoFaceDebugHost.Settings; } }

        private void OnEnable() { minSize = new Vector2(430, 340); }

        private void OnGUI()
        {
            var settings = Settings;
            var controller = settings == null ? null : settings.FaceController() as AnimatorController;
            var meshes = settings == null ? null : settings.Meshes();
            var root = settings == null ? null : settings.TargetAnimator();

            DrawTitle(controller);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (HoConstraintEditorControls.Card())
            {
                DrawStockRow();
                DrawControllerRow(settings, controller);
                DrawFolderRow(settings);
                DrawTargetRow(settings, root, meshes);
                DrawActions(settings, controller, meshes, root);

                if (!string.IsNullOrEmpty(status))
                {
                    EditorGUILayout.Space(2.0f);
                    EditorGUILayout.HelpBox(status, statusIsError ? MessageType.Error : MessageType.Info);
                }
            }

            DrawAnimationSection(controller);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTitle(AnimatorController controller)
        {
            string right = controller == null ? "未指定控制器"
                : controller.layers.Length + " 层 · " + controller.parameters.Length + " 参数 · "
                  + controller.animationClips.Length + " 片段";
            HoConstraintEditorControls.Title("控制器编辑", right);
        }

        // ══════════════════════════════════════════════════════════════
        // 一、去哪拿控制器（我们只给目录，复制归你）
        // ══════════════════════════════════════════════════════════════
        private void DrawStockRow()
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("预置控制器", HoConstraintEditorTheme.LabelWidth,
                    "我们做好的控制器放在这里。**复制一份到你的工程**，然后自己管理它放哪、叫什么。");
                if (HoConstraintEditorControls.Button("打开目录",
                    "在系统文件管理器里打开包内的预置控制器目录。", false, 64.0f))
                    RevealStockFolder();

                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption(StockFolderStatus());
            }
        }

        /// <summary>预置目录的绝对路径（包是 `file:` 引用的，所以它的路径就是磁盘路径）。</summary>
        private static string StockFolderPath()
        {
            // 自己定位自己：从本脚本的资产路径推出同级 Controllers/。
            // 这样换机器、换包名（manifest 里换个名字）都不用改常量。
            string scriptPath = null;
            foreach (var guid in AssetDatabase.FindAssets("HoFaceControllerToolWindow t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.EndsWith("HoFaceControllerToolWindow.cs", StringComparison.Ordinal))
                {
                    scriptPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(scriptPath)) return null;

            string project = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string relative = System.IO.Path.GetDirectoryName(scriptPath) ?? "";
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(project, relative, StockFolderName))
                .Replace('\\', '/');
        }

        private static string StockFolderStatus()
        {
            string path = StockFolderPath();
            if (string.IsNullOrEmpty(path)) return "找不到目录";
            return System.IO.Directory.Exists(path) ? "复制到工程里自己管理" : "目录还没建（点「打开目录」会建）";
        }

        private static void RevealStockFolder()
        {
            string path = StockFolderPath();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("Ho 面捕：定位不到预置控制器目录。");
                return;
            }

            if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(path);
        }

        // ══════════════════════════════════════════════════════════════
        // 二、装配用到的三样（控制器 / 动画文件夹 / 目标）
        // ══════════════════════════════════════════════════════════════
        private void DrawControllerRow(HoFaceDebugSettings settings, AnimatorController controller)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("控制器", HoConstraintEditorTheme.LabelWidth,
                    "你复制进工程的那一份。这一页**原地**装配它：不新建资产、不改名、不动位置。");
                var picked = (RuntimeAnimatorController)EditorGUI.ObjectField(
                    HoConstraintEditorControls.NextFlexible(90.0f), controller, typeof(RuntimeAnimatorController), false);
                if (picked != controller)
                {
                    settings.SetFaceController(picked, picked != null ? AssetDatabase.GetAssetPath(picked) : "");
                    HoFaceDebugHost.Save();
                }

                if (controller != null && HoConstraintEditorControls.Button("选中", "在工程窗口里选中这份控制器。", false, 44.0f))
                    EditorGUIUtility.PingObject(controller);

                HoConstraintEditorControls.Flex();
                if (controller == null)
                {
                    var assigned = settings.FaceController();
                    HoConstraintEditorControls.Caption(assigned == null
                        ? "未指定 —— 先「打开目录」复制一份到工程"
                        : "这不是 AnimatorController：" + assigned.GetType().Name);
                }
            }
        }

        private void DrawFolderRow(HoFaceDebugSettings settings)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("动画文件夹", HoConstraintEditorTheme.LabelWidth,
                    "放现成动画的文件夹。按**片段名 = 槽位名**把它们填进控制器；文件夹里没有的名字保留控制器自带的。");
                string edited = EditorGUI.TextField(
                    HoConstraintEditorControls.NextFlexible(80.0f), settings.animationFolder, HoConstraintEditorTheme.Field);
                if (edited != settings.animationFolder) { settings.animationFolder = edited; HoFaceDebugHost.Save(); }

                HoConstraintEditorControls.Gap();
                if (HoConstraintEditorControls.Button("选…", "选一个工程里的文件夹。", false, 34.0f))
                {
                    string picked = EditorUtility.OpenFolderPanel("选放现成动画的文件夹", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        settings.animationFolder = ToProjectRelative(picked);
                        HoFaceDebugHost.Save();
                    }
                }

                HoConstraintEditorControls.Flex();
                if (string.IsNullOrEmpty(settings.animationFolder))
                    HoConstraintEditorControls.Caption("空 = 只重绑网格");
            }
        }

        private void DrawTargetRow(HoFaceDebugSettings settings, Animator root, List<SkinnedMeshRenderer> meshes)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("目标对象", HoConstraintEditorTheme.LabelWidth,
                    "重绑需要一个目标：曲线的路径要指向它的网格。**角色上不需要挂任何组件**，跟主面板那一栏是同一个。");
                var character = settings.Character();
                var picked = (GameObject)EditorGUI.ObjectField(
                    HoConstraintEditorControls.NextFlexible(90.0f), character, typeof(GameObject), true);
                if (picked != character) { settings.SetCharacter(picked); HoFaceDebugHost.Save(); }

                HoConstraintEditorControls.Gap();
                if (HoConstraintEditorControls.Button("当前选择", "用当前选中的物体当目标。", false, 62.0f))
                {
                    if (Selection.activeGameObject != null)
                    {
                        settings.SetCharacter(Selection.activeGameObject);
                        HoFaceDebugHost.Save();
                    }
                }

                HoConstraintEditorControls.Flex();
                if (character == null) HoConstraintEditorControls.Caption("未指定");
                else if (root == null) HoConstraintEditorControls.Caption("找不到 Animator");
                else HoConstraintEditorControls.Caption(meshes.Count + " 个网格 · " + ShapeCount(meshes) + " 个形态键");
            }
        }

        private static int ShapeCount(List<SkinnedMeshRenderer> meshes)
        {
            int total = 0;
            if (meshes == null) return 0;
            foreach (var mesh in meshes)
                if (mesh != null && mesh.sharedMesh != null) total += mesh.sharedMesh.blendShapeCount;
            return total;
        }

        private void DrawActions(HoFaceDebugSettings settings, AnimatorController controller,
            List<SkinnedMeshRenderer> meshes, Animator root)
        {
            bool hasTarget = root != null && meshes != null && meshes.Count > 0;
            bool ready = controller != null && hasTarget;

            HoConstraintEditorControls.Separator(3.0f, 3.0f);

            using (HoConstraintEditorControls.Row())
            {
                using (new EditorGUI.DisabledScope(!ready))
                {
                    if (HoConstraintEditorControls.Button("原地装配",
                        "填动画 + 重绑形态键曲线。就地改这份控制器，不新建资产。", true, 76.0f))
                        Assemble(settings, controller, meshes, root);

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("只填动画",
                        "只按槽位名把动画文件夹里的片段填进树与状态。", false, 68.0f))
                        FillOnly(controller, settings.animationFolder);

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("只重绑网格",
                        "只把形态键曲线的路径重绑到目标网格上。", false, 76.0f))
                        RetargetOnly(controller, meshes, root);
                }

                HoConstraintEditorControls.Flex();
                if (!ready)
                {
                    HoConstraintEditorControls.Caption(controller == null
                        ? "缺控制器"
                        : "缺目标对象（要有 Animator 与 SkinnedMeshRenderer）");
                }
            }
        }

        /// <summary>原地装配 = 填动画 + 重绑网格 + 清掉没人用的临时片段。</summary>
        private void Assemble(HoFaceDebugSettings settings, AnimatorController controller,
            List<SkinnedMeshRenderer> meshes, Animator root)
        {
            try
            {
                string path = AssetDatabase.GetAssetPath(controller);
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("这份控制器不是工程里的资产。");

                // 传"模板 = 自己 + 目标 = 自己"：Adopt 会跳过复制那一段，只做填 / 重绑 / 清理。
                var result = HoFaceAnimationAssets.Adopt(
                    controller, path, meshes, root, settings.animationFolder, true);

                statusIsError = false;
                status = "装配完成（就地）：" + path + "\n"
                    + "层 " + result.layers.Length + " · 参数 " + result.parameters.Length
                    + " · 片段 " + result.animationClips.Length + "\n"
                    + "之后要加层、改动画、改参数，去 Unity 的 Animator 窗口 —— 这一页不会覆盖它。";
            }
            catch (Exception e)
            {
                statusIsError = true;
                status = e.Message;
            }
        }

        private void FillOnly(AnimatorController controller, string animationFolder)
        {
            try
            {
                int filled = HoFaceAnimationAssets.ResolveClips(controller, animationFolder);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                statusIsError = false;
                status = filled == 0
                    ? "没填任何片段：动画文件夹是空的，或者里面的片段名跟槽位名对不上。"
                    : "已按槽位名填进 " + filled + " 个片段。";
            }
            catch (Exception e)
            {
                statusIsError = true;
                status = e.Message;
            }
        }

        private void RetargetOnly(AnimatorController controller, List<SkinnedMeshRenderer> meshes, Animator root)
        {
            try
            {
                HoFaceAnimationAssets.Retarget(controller, meshes, root);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                statusIsError = false;
                status = "已把形态键曲线重绑到目标网格上。";
            }
            catch (Exception e)
            {
                statusIsError = true;
                status = e.Message;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 三、详情：槽位填得怎么样 + 这份控制器认不认这个角色
        // ══════════════════════════════════════════════════════════════
        private void DrawAnimationSection(AnimatorController controller)
        {
            if (controller == null) return;

            var settings = Settings;
            var meshes = settings.Meshes();
            var slots = HoFaceAnimationAssets.Slots(controller, settings.animationFolder);
            int filled = 0;
            foreach (var slot in slots)
                if (slot.fromFolder != null) filled++;
            int placeholder = slots.Count - filled;

            string summary = slots.Count + " 个槽位 · " + filled + " 个有现成动画";
            if (placeholder > 0) summary += " · " + placeholder + " 个还是控制器自带的";

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                    ref animationExpanded, "详情", summary, HoConstraintEditorTheme.AccentOutput))
                return;

            using (HoConstraintEditorControls.Card(true))
            {
                var info = HoFaceAnimationAssets.Inspect(controller, meshes);
                if (info != null)
                {
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("形态键", HoConstraintEditorTheme.LabelWidth,
                            "控制器里曲线用到的形态键名，以及目标网格上认不认。");
                        HoConstraintEditorControls.Caption(info.shapes.Count + " 个 · 目标网格缺 " + info.missing.Count + " 个");
                        HoConstraintEditorControls.Flex();
                    }

                    if (info.missing.Count > 0)
                    {
                        var text = new StringBuilder("目标网格上没有这些键（曲线留着，但落不到网格上）：");
                        int shown = Math.Min(8, info.missing.Count);
                        for (int i = 0; i < shown; i++) text.Append("\n  ").Append(info.missing[i]);
                        if (info.missing.Count > shown) text.Append("\n  …还有 ").Append(info.missing.Count - shown).Append(" 个");
                        EditorGUILayout.HelpBox(text.ToString(), MessageType.Warning);
                    }

                    if (info.otherKinds.Count > 0)
                    {
                        var text = new StringBuilder("还有这些非形态键曲线（骨骼缩放之类，本工具不管）：");
                        int shown = Math.Min(6, info.otherKinds.Count);
                        for (int i = 0; i < shown; i++) text.Append("\n  ").Append(info.otherKinds[i]);
                        EditorGUILayout.HelpBox(text.ToString(), MessageType.None);
                    }
                }

                if (placeholder > 0)
                {
                    var text = new StringBuilder("这些槽位在动画文件夹里没有同名片段，还是控制器自带的：");
                    int listed = 0;
                    for (int i = 0; i < slots.Count && listed < 8; i++)
                    {
                        if (slots[i].fromFolder != null) continue;
                        text.Append("\n  ").Append(slots[i].name);
                        listed++;
                    }

                    if (placeholder > listed) text.Append("\n  …还有 ").Append(placeholder - listed).Append(" 个");
                    EditorGUILayout.HelpBox(text.ToString(), MessageType.None);
                }
            }
        }

        /// <summary>绝对路径 → `Assets/...`（设置文件里存可移植路径）。</summary>
        private static string ToProjectRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;
            return path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
    }
}
