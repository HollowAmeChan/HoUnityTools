using UnityEngine;

namespace Hollow.HoUnityTools.WarudoModUtils
{
    /// <summary>
    /// Small runtime GUI facade. Keeping controls here lets the Hub skin them later
    /// without requiring every module to create its own UI framework.
    /// </summary>
    public sealed class HoWarudoRuntimeGUIContext
    {
        internal HoWarudoRuntimeGUIContext(HoWarudoRuntimeHub hub)
        {
            Hub = hub;
        }

        public HoWarudoRuntimeHub Hub { get; private set; }

        public void Label(string text)
        {
            GUILayout.Label(text);
        }

        public bool Button(string text, params GUILayoutOption[] options)
        {
            return GUILayout.Button(text, options);
        }

        public bool Toggle(string text, bool value, params GUILayoutOption[] options)
        {
            return GUILayout.Toggle(value, text, options);
        }

        public float Slider(string text, float value, float min, float max, params GUILayoutOption[] options)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(text, GUILayout.Width(120f));
            float next = GUILayout.HorizontalSlider(value, min, max, options);
            GUILayout.Label(next.ToString("0.###"), GUILayout.Width(54f));
            GUILayout.EndHorizontal();
            return next;
        }

        public string TextField(string value, params GUILayoutOption[] options)
        {
            return GUILayout.TextField(value ?? string.Empty, options);
        }

        public void Space(float pixels)
        {
            GUILayout.Space(pixels);
        }

        public Vector2 BeginScrollView(Vector2 scroll, params GUILayoutOption[] options)
        {
            return GUILayout.BeginScrollView(scroll, options);
        }

        public void EndScrollView()
        {
            GUILayout.EndScrollView();
        }
    }
}
