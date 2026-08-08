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
        public interface IRuntimePanel
        {
            string DisplayName { get; }
            int Order { get; }
            void DrawRuntimeGUI();
        }

        [Header("Startup")]
        [Tooltip("Show the small draggable launcher button when the component starts.")]
        public bool showLauncher = true;

        [Tooltip("Open the runtime hub window when the component starts.")]
        public bool showWindowOnStart;

        private static HoWarudoRuntimeHub s_Current;
        private readonly List<IRuntimePanel> m_Panels = new List<IRuntimePanel>();

        private Rect m_LauncherRect = new Rect(24f, 24f, 140f, 52f);
        private Rect m_WindowRect = new Rect(180f, 80f, 360f, 300f);
        private bool m_ShowWindow;
        private bool m_ConsumePointerEvents;
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

        public void Register(IRuntimePanel panel)
        {
            if (panel != null && !m_Panels.Contains(panel))
                m_Panels.Add(panel);
        }

        public void Unregister(IRuntimePanel panel)
        {
            if (panel != null)
                m_Panels.Remove(panel);
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
            GUILayout.Label("Registered modules: " + m_Panels.Count);

            if (GUILayout.Button("Test button"))
            {
                m_TestClickCount++;
                Debug.Log("[HoWarudoRuntimeHub] Test button clicked: " + m_TestClickCount);
            }

            GUILayout.Label("Clicks: " + m_TestClickCount);

            if (m_Panels.Count > 0)
            {
                GUILayout.Space(8f);
                for (int i = 0; i < m_Panels.Count; i++)
                {
                    IRuntimePanel panel = m_Panels[i];
                    if (panel == null)
                        continue;

                    GUILayout.Label(panel.DisplayName);
                    panel.DrawRuntimeGUI();
                }
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
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
