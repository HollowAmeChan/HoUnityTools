using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.WarudoModUtils
{
    /// <summary>
    /// Central runtime entry point for Warudo tools.
    /// Components can register runtime panels here without creating their own windows.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Hub")]
    public sealed class HoWarudoRuntimeHub : MonoBehaviour
    {
        [Header("启动设置")]
        [InspectorName("显示浮动入口")]
        [Tooltip("组件启动时显示可拖动的 Hub 浮动入口。")]
        public bool showLauncher = true;

        [InspectorName("启动时打开窗口")]
        [Tooltip("组件启动时直接打开运行时中控窗口。")]
        public bool showWindowOnStart;

        private static HoWarudoRuntimeHub s_Current;
        private readonly List<IHoWarudoRuntimeModule> m_Modules = new List<IHoWarudoRuntimeModule>();
        private readonly Dictionary<string, bool> m_ModuleFoldouts = new Dictionary<string, bool>();
        private HoWarudoRuntimeGUIContext m_GuiContext;

        private Rect m_LauncherRect = new Rect(24f, 24f, 140f, 52f);
        private Rect m_WindowRect = new Rect(180f, 80f, 360f, 300f);
        private bool m_ShowWindow;
        private bool m_ConsumePointerEvents;
        private Vector2 m_ModuleScroll;
        private int m_TestClickCount;
        private int m_LauncherWindowId;
        private int m_HubWindowId;

        public static HoWarudoRuntimeHub Current
        {
            get { return s_Current; }
        }

        private void Awake()
        {
            if (s_Current != null && s_Current != this)
            {
                enabled = false;
                return;
            }

            s_Current = this;
            m_LauncherWindowId = GetInstanceID() ^ 0x4F485542;
            m_HubWindowId = m_LauncherWindowId + 1;
            m_ShowWindow = showWindowOnStart;
            m_GuiContext = new HoWarudoRuntimeGUIContext(this);
        }

        private void OnEnable()
        {
            if (s_Current == null)
                s_Current = this;
        }

        private void OnDisable()
        {
            if (s_Current == this)
                s_Current = null;
        }

        private void OnDestroy()
        {
            if (s_Current == this)
                s_Current = null;
        }

        private void Start()
        {
            RefreshModules();
        }

        public void Register(IHoWarudoRuntimeModule module)
        {
            if (module == null || m_Modules.Contains(module))
                return;

            m_Modules.Add(module);
            m_Modules.Sort(CompareModules);
            if (!m_ModuleFoldouts.ContainsKey(module.Id))
                m_ModuleFoldouts[module.Id] = true;
        }

        public void Unregister(IHoWarudoRuntimeModule module)
        {
            if (module != null)
                m_Modules.Remove(module);
        }

        public void RefreshModules()
        {
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                IHoWarudoRuntimeModule module = behaviours[i] as IHoWarudoRuntimeModule;
                if (module != null)
                    Register(module);
            }
        }

        private static int CompareModules(IHoWarudoRuntimeModule left, IHoWarudoRuntimeModule right)
        {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : string.Compare(left.DisplayName, right.DisplayName, System.StringComparison.Ordinal);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || s_Current != this)
                return;

            if (m_ShowWindow)
                m_WindowRect = GUI.Window(m_HubWindowId, m_WindowRect, DrawHubWindow, "HoWarudoRuntimeHub");

            if (showLauncher)
                m_LauncherRect = GUI.Window(m_LauncherWindowId, m_LauncherRect, DrawLauncher, "HoHub");

            ConsumeHubPointerEvents();
            ClampWindow(ref m_LauncherRect);
            ClampWindow(ref m_WindowRect);
        }

        private void DrawLauncher(int windowId)
        {
            if (GUILayout.Button(m_ShowWindow ? "Close Hub" : "Open Hub", GUILayout.Height(24f)))
                m_ShowWindow = !m_ShowWindow;

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void DrawHubWindow(int windowId)
        {
            GUILayout.Label("Runtime hub connected");
            GUILayout.Label("Registered modules: " + m_Modules.Count);

            if (GUILayout.Button("Refresh modules"))
                RefreshModules();

            if (GUILayout.Button("Test button"))
            {
                m_TestClickCount++;
                Debug.Log("[HoWarudoRuntimeHub] Test button clicked: " + m_TestClickCount);
            }

            GUILayout.Label("Clicks: " + m_TestClickCount);

            if (m_Modules.Count > 0)
            {
                GUILayout.Space(8f);
                m_ModuleScroll = m_GuiContext.BeginScrollView(m_ModuleScroll, GUILayout.ExpandHeight(true));
                for (int i = 0; i < m_Modules.Count; i++)
                {
                    IHoWarudoRuntimeModule module = m_Modules[i];
                    if (!IsModuleAlive(module))
                    {
                        m_Modules.RemoveAt(i--);
                        continue;
                    }

                    bool expanded = IsModuleExpanded(module);
                    if (GUILayout.Button((expanded ? "- " : "+ ") + module.DisplayName, GUI.skin.button))
                    {
                        expanded = !expanded;
                        m_ModuleFoldouts[module.Id] = expanded;
                    }

                    if (!expanded)
                        continue;

                    GUILayout.BeginVertical(GUI.skin.box);
                    try
                    {
                        module.DrawRuntimeGUI(m_GuiContext);
                    }
                    catch (System.Exception exception)
                    {
                        GUILayout.Label("Module UI error: " + exception.GetType().Name);
                        Debug.LogException(exception);
                    }
                    GUILayout.EndVertical();
                }
                m_GuiContext.EndScrollView();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private bool IsModuleExpanded(IHoWarudoRuntimeModule module)
        {
            bool expanded;
            if (!m_ModuleFoldouts.TryGetValue(module.Id, out expanded))
            {
                expanded = true;
                m_ModuleFoldouts[module.Id] = expanded;
            }

            return expanded;
        }

        private static bool IsModuleAlive(IHoWarudoRuntimeModule module)
        {
            if (module == null)
                return false;

            MonoBehaviour behaviour = module as MonoBehaviour;
            if (behaviour != null)
                return true;

            return !(module is MonoBehaviour);
        }

        private void ConsumeHubPointerEvents()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null)
                return;

            bool overLauncher = showLauncher && m_LauncherRect.Contains(currentEvent.mousePosition);
            bool overHub = m_ShowWindow && m_WindowRect.Contains(currentEvent.mousePosition);
            bool overHubUI = overLauncher || overHub;

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (overHubUI)
                    {
                        m_ConsumePointerEvents = true;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                    if (m_ConsumePointerEvents || overHubUI)
                        currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (m_ConsumePointerEvents || overHubUI)
                    {
                        currentEvent.Use();
                        m_ConsumePointerEvents = false;
                    }
                    break;
            }
        }

        private static void ClampWindow(ref Rect rect)
        {
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, screenWidth - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, screenHeight - rect.height));
        }
    }
}
