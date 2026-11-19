#if UNITY_EDITOR && !UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
#if !UNITY_2023_1_OR_NEWER
using System;
using System.Collections;
using System.Reflection;
#endif
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// ─────────────────────────────────────────────────────────────────────────────
// BirpSceneMSAA —— Scene 视图的多重采样开关，专门给 Built-in 管线的工程用。
// Unity 6 不注册本功能的按钮、Overlay、菜单或渲染回调。
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
    /// 把按钮挂到 Scene 视图的顶部工具栏。
    /// 2022.1 起支持默认停靠参数；2021.3 需要在 Unity 恢复布局后补默认位置和可见性。
    /// </summary>
#if UNITY_2022_1_OR_NEWER
    [Overlay(typeof(SceneView), Id, "BIRP Scene MSAA", true,
        defaultDockZone = DockZone.TopToolbar,
        defaultLayout = Layout.HorizontalToolbar)]
#else
    [Overlay(typeof(SceneView), Id, "BIRP Scene MSAA", true)]
#endif
    internal class HoBirpSceneMSAAOverlay : ToolbarOverlay
    {
        internal const string Id = "hollow.hounitytools.BirpSceneMSAA";

        private HoBirpSceneMSAAOverlay() : base(HoBirpSceneMSAAButton.Id)
        {
        }

#if !UNITY_2023_1_OR_NEWER
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // 2021.3 的 OverlayCanvas 和停靠 API 不公开。只通过它自己的 RestoreOverlay
        // 补默认值，让 Unity 继续管理容器、拖动、显隐和布局保存，不直接改 UI 层级。
        private static readonly PropertyInfo CanvasProperty =
            typeof(Overlay).GetProperty("canvas", InstanceMembers);
        private static readonly PropertyInfo ContainerProperty =
            typeof(Overlay).GetProperty("container", InstanceMembers);
        private static readonly FieldInfo InitializedCallback =
            CanvasProperty.PropertyType.GetField("afterOverlaysInitialized", InstanceMembers);
        private static readonly MethodInfo RestoreOverlay =
            CanvasProperty.PropertyType.GetMethod("RestoreOverlay", InstanceMembers);
        private static readonly FieldInfo SavedLayouts =
            CanvasProperty.PropertyType.GetField("m_SaveData", InstanceMembers);

        // 和该 SceneView 的布局一起保存迁移标记，避免重新加载时覆盖用户后续的显隐/停靠选择。
        private const string PlacementInitializedId = Id + ".placement-v1";

        private object legacyCanvas;

        public override void OnCreated()
        {
            base.OnCreated();
            legacyCanvas = CanvasProperty.GetValue(this);
            var initialized = (Action)InitializedCallback.GetValue(legacyCanvas);
            InitializedCallback.SetValue(legacyCanvas, initialized + InitializeLegacyPlacement);

            // 动态添加 Overlay 时不会经过整张 Canvas 的 RestoreOverlays。
            EditorApplication.delayCall += InitializeLegacyPlacement;
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.delayCall -= InitializeLegacyPlacement;
            if (legacyCanvas != null)
            {
                var initialized = (Action)InitializedCallback.GetValue(legacyCanvas);
                InitializedCallback.SetValue(legacyCanvas, initialized - InitializeLegacyPlacement);
                legacyCanvas = null;
            }
            base.OnWillBeDestroyed();
        }

        private void InitializeLegacyPlacement()
        {
            if (legacyCanvas == null || containerWindow == null)
                return;

            try
            {
                var saveDataType = RestoreOverlay.GetParameters()[1].ParameterType;
                var idField = saveDataType.GetField("id");
                var savedLayouts = (IList)SavedLayouts.GetValue(legacyCanvas);
                foreach (var saved in savedLayouts)
                    if ((string)idField.GetValue(saved) == PlacementInitializedId)
                        return;

                var currentContainer = ContainerProperty.GetValue(this) as VisualElement;
                if (currentContainer == null)
                    return;
#if UNITY_2022_1_OR_NEWER
                // 旧实现没有给 2022 默认停靠参数，已保存的布局会留在右侧面板。
                var usesLegacyDefault = currentContainer.name == "overlay-container--right";
#else
                // 2021.3 对没有保存记录的 Overlay 使用 displayed=false / Layout.Panel，
                // 不读取 OverlayAttribute.defaultDisplay。只修复这个默认回退状态。
                var defaultContainer = legacyCanvas.GetType()
                    .GetProperty("defaultToolbarContainer", InstanceMembers).GetValue(legacyCanvas);
                var usesLegacyDefault = ReferenceEquals(currentContainer, defaultContainer);
#endif
                if (!floating && layout == Layout.Panel && usesLegacyDefault)
                {
                    var data = Activator.CreateInstance(saveDataType);
                    idField.SetValue(data, Id);
                    saveDataType.GetField("containerId").SetValue(data, "overlay-toolbar__top");
                    saveDataType.GetField("layout").SetValue(data, Layout.HorizontalToolbar);
                    saveDataType.GetField("displayed").SetValue(data, true);
                    saveDataType.GetField("index").SetValue(data, int.MaxValue);
                    RestoreOverlay.Invoke(legacyCanvas, new[] { (object)this, data });
                }

                var marker = Activator.CreateInstance(saveDataType);
                idField.SetValue(marker, PlacementInitializedId);
                savedLayouts.Add(marker);
                ((ISerializationCallbackReceiver)legacyCanvas).OnBeforeSerialize();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("HoUnityTools：无法初始化 BIRP Scene MSAA 工具栏位置。\n" +
                                 exception.GetBaseException().Message);
            }
        }
#endif
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
