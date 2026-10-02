#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// ─────────────────────────────────────────────────────────────────────────────
// BirpSceneMSAA —— Scene 视图的多重采样开关，专门给 Built-in 管线的工程用。
//
// 为什么是「BIRP 专属」：
//   它靠直接改 Scene 视图相机 targetTexture.antiAliasing 实现（见下面原理）。
//   SRP（URP / HDRP）自己管 Scene 视图的渲染目标，硬改会打架 ——
//   所以检测到 SRP 时按钮和菜单项一律置灰。目标场景就是 Warudo / VRChat 这类
//   基本都用 Built-in 的工程。
//
// 为什么要单独做这个：
//   Scene 视图渲染到它自己的 RenderTexture，那个 RT 的多重采样是照抄
//   QualitySettings.antiAliasing 的 —— 而 Quality 是**项目级**设置。
//   为了看清锯齿去改 Quality，会连 Game 视图和构建一起改掉，代价太大。
//   这个开关只动 Scene 视图那个 RT，一根手指都不碰 QualitySettings。
//
// 原理（同 jp.lilxyzw.editortoolbox 的 SceneToolbar）：
//   Scene 视图每帧渲染前，直接改它相机 targetTexture.antiAliasing。
//   关键是必须 Release → 改 → Create —— RenderTexture 创建之后这个值就定死了，改不动。
// ─────────────────────────────────────────────────────────────────────────────

namespace Hollow.HoUnityTools.Editor.BirpSceneMSAA
{
    /// <summary>
    /// BirpSceneMSAA 的开关状态与实现。
    /// </summary>
    internal static class HoBirpSceneMSAA
    {
        private const string PrefKey = "com.hollow.hounitytools.birp-scene-msaa";

        /// <summary>已经被我们改过 antiAliasing 的 Scene 视图 RT，关掉时要改回去。</summary>
        private static readonly Dictionary<SceneView, RenderTexture> Modified =
            new Dictionary<SceneView, RenderTexture>();

        /// <summary>SRP 在管 Scene 视图时不能碰它的 RT，此时整个功能不可用。</summary>
        internal static bool Supported => GraphicsSettings.currentRenderPipeline == null;

        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set
            {
                if (Enabled == value) return;

                EditorPrefs.SetBool(PrefKey, value);
                HoBirpSceneMSAAButton.SyncAll(value);
                SceneView.RepaintAll(); // 下一帧 duringSceneGui 就会应用，顺手催一下重绘
            }
        }

        [InitializeOnLoadMethod]
        private static void Init()
        {
            SceneView.duringSceneGui -= OnDuringSceneGui;
            SceneView.duringSceneGui += OnDuringSceneGui;
        }

        private static void OnDuringSceneGui(SceneView sceneView)
        {
            if (sceneView == null) return;

            var target = sceneView.camera != null ? sceneView.camera.targetTexture : null;

            if (Enabled && Supported)
            {
                if (target == null) return;

                // RT 换了（换了视图 / 改了分辨率）才需要重设，否则每帧 Release/Create 会卡死。
                if (Modified.TryGetValue(sceneView, out var previous) && previous == target) return;

                target.Release();
                // QualitySettings.antiAliasing 为 0 表示"不开"，而 RT 只认 1/2/4/8，
                // 所以夹到至少 1（= 不开多重采样），效果一样但不会塞个非法值进去。
                target.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
                target.Create();
                Modified[sceneView] = target;
                return;
            }

            // 关掉：把之前改过的那个 RT 还回去。没改过就什么都不做。
            if (target == null) return;
            if (!Modified.TryGetValue(sceneView, out var modified) || modified != target) return;

            target.Release();
            target.antiAliasing = 1;
            target.Create();
            Modified.Remove(sceneView);
        }
    }

    /// <summary>
    /// Scene 视图工具栏上的那个 MSAA 按钮。
    /// </summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HoBirpSceneMSAAButton : ToolbarToggle
    {
        internal const string Id = "hollow.hounitytools.BirpSceneMSAAButton";

        private static readonly List<HoBirpSceneMSAAButton> Instances = new List<HoBirpSceneMSAAButton>();
        private static bool syncing;

        public HoBirpSceneMSAAButton()
        {
            text = "MSAA";
            tooltip = "BirpSceneMSAA：Scene 视图的多重采样。\n" +
                      "只影响 Scene 视图，不动 Quality Settings（也就不会影响 Game 视图与构建）。\n" +
                      "只在 Built-in 管线下有效 —— SRP（URP / HDRP）自己管 Scene 视图的渲染目标。";
            style.fontSize = 10;
            style.unityFontStyleAndWeight = FontStyle.Bold;

            SetValueWithoutNotify(HoBirpSceneMSAA.Enabled);
            SetEnabled(HoBirpSceneMSAA.Supported);

            // 注意 this. —— RegisterValueChangedCallback 是 UnityEngine.UIElements 里的
            // 扩展方法，裸写方法名解析不到（会报 CS0103）。
            this.RegisterValueChangedCallback(evt =>
            {
                if (syncing) return;
                HoBirpSceneMSAA.Enabled = evt.newValue;
            });

            Instances.Add(this);
        }

        /// <summary>把别处（菜单项、另一个 Scene 视图）改的状态同步过来。</summary>
        internal static void SyncAll(bool value)
        {
            syncing = true;
            try
            {
                foreach (var button in Instances)
                {
                    if (button != null) button.SetValueWithoutNotify(value);
                }
            }
            finally
            {
                syncing = false;
            }
        }
    }

    /// <summary>
    /// 把按钮挂到 Scene 视图的顶部工具栏。没有它，按钮只是个没人实例化的类。
    ///
    /// DockZone / Layout 这套「默认停靠」参数是较新版本才有的（2021.3 上没有，实测
    /// Editor Toolbox v2.0.2 的写法在 2021.3 编译不过）。所以按版本分两支：
    /// 新版直接落在顶部工具栏；老版先以浮层出现，手动拖到工具栏一次即可，之后会被记住。
    /// </summary>
#if UNITY_6000_0_OR_NEWER
    [Overlay(typeof(SceneView), Id, "HoUnityTools", true,
        defaultDisplay = true,
        defaultDockZone = DockZone.TopToolbar,
        defaultLayout = Layout.HorizontalToolbar)]
#else
    [Overlay(typeof(SceneView), Id, "HoUnityTools", true)]
#endif
    internal class HoBirpSceneMSAAOverlay : ToolbarOverlay
    {
        internal const string Id = "hollow.hounitytools.BirpSceneMSAA";

        private HoBirpSceneMSAAOverlay() : base(HoBirpSceneMSAAButton.Id)
        {
        }
    }

    /// <summary>
    /// 菜单入口：工具栏上找不到按钮、或者懒得找的时候用。
    /// </summary>
    internal static class HoBirpSceneMSAAMenu
    {
        private const string Path = "HoUnityTools/BIRP Scene MSAA";

        [MenuItem(Path, false, 200)]
        private static void Toggle()
        {
            HoBirpSceneMSAA.Enabled = !HoBirpSceneMSAA.Enabled;
        }

        [MenuItem(Path, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(Path, HoBirpSceneMSAA.Enabled);
            return HoBirpSceneMSAA.Supported;
        }
    }
}
#endif
