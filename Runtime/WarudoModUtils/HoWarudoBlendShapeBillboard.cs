using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Hollow.HoUnityTools.WarudoModUtils
{
    /// <summary>
    /// Displays the live blend-shape weights of one SkinnedMeshRenderer in world space.
    /// This component is deliberately independent from HoWarudoRuntimeHub.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Warudo Mod Utils/HoWarudo 形态键状态广告牌")]
    public sealed class HoWarudoBlendShapeBillboard : MonoBehaviour
    {
        private const int RowsPerColumnGroup = 10;
        private const float NameValueGap = 0.75f;
        private const float ColumnGroupGap = 1.5f;
        private static readonly Quaternion TextFacingCorrection = Quaternion.Euler(0f, 180f, 0f);

        [Header("数据来源")]
        [InspectorName("目标蒙皮网格")]
        [Tooltip("要读取形态键真实权重的 SkinnedMeshRenderer。")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("世界屏幕")]
        [InspectorName("面向相机")]
        [Tooltip("关闭时直接使用本组件 Transform 的位置和朝向。")]
        public bool faceCamera;

        [InspectorName("观察相机")]
        [Tooltip("仅在启用面向相机时使用。留空则使用 Camera.main。")]
        public Camera viewCamera;

        [Header("显示")]
        [InspectorName("显示标题")]
        public bool showTitle = true;

        [InspectorName("仅显示非零")]
        [Tooltip("只显示绝对值大于阈值的形态键。")]
        public bool onlyNonZero;

        [InspectorName("非零阈值")]
        [Min(0f)]
        public float nonZeroThreshold = 0.01f;

        [InspectorName("显示小数位")]
        [Range(0, 4)]
        public int decimalPlaces = 2;

        [InspectorName("最大条目数")]
        [Min(0)]
        [Tooltip("0 表示显示全部；大于 0 时限制显示数量。每组固定最多 10 行，超出后向右扩展。")]
        public int maxEntries;

        [InspectorName("字体大小")]
        [Min(1)]
        public int fontSize = 26;

        [InspectorName("行间距")]
        [Min(0.1f)]
        [Tooltip("字体行高的倍数。标题与正文间距也会按此值自动计算。")]
        public float lineSpacing = 1f;

        [InspectorName("文字颜色")]
        public Color textColor = Color.white;

        [InspectorName("整体缩放")]
        [Min(0.001f)]
        public float worldScale = 0.01f;

        private readonly List<BlendShapeEntry> m_Entries = new List<BlendShapeEntry>();
        private readonly List<ColumnGroup> m_ColumnGroups = new List<ColumnGroup>();
        private readonly StringBuilder m_NameBuilder = new StringBuilder(512);
        private readonly StringBuilder m_ValueBuilder = new StringBuilder(256);

        private Transform m_DisplayRoot;
        private TextMesh m_TitleText;
        private SkinnedMeshRenderer m_LastRenderer;
        private Mesh m_LastMesh;
        private float[] m_LastWeights;
        private int m_LastVisibleCount = -1;
        private Camera m_CachedCamera;
        private bool m_LastShowTitle;
        private bool m_LastOnlyNonZero;
        private float m_LastNonZeroThreshold;
        private int m_LastDecimalPlaces;
        private int m_LastMaxEntries;
        private int m_LastFontSize;
        private float m_LastLineSpacing;

        private struct BlendShapeEntry
        {
            public float weight;
            public string name;
        }

        private sealed class ColumnGroup
        {
            public GameObject root;
            public TextMesh names;
            public TextMesh values;
        }

        private void OnEnable()
        {
            EnsureDisplayObjects();
            RefreshImmediately();
        }

        private void LateUpdate()
        {
            EnsureDisplayObjects();
            ApplyVisualSettings();
            RefreshTextIfNeeded();
            UpdateDisplayTransform();
        }

        private void OnDisable()
        {
            if (m_DisplayRoot != null)
                m_DisplayRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (m_DisplayRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(m_DisplayRoot.gameObject);
            else
                DestroyImmediate(m_DisplayRoot.gameObject);
        }

        /// <summary>Forces the display to reread mesh metadata and current blend-shape weights.</summary>
        public void RefreshImmediately()
        {
            m_LastMesh = null;
            m_LastRenderer = null;
            m_LastVisibleCount = -1;
            m_LastShowTitle = !showTitle;
            RefreshTextIfNeeded();
        }

        private void EnsureDisplayObjects()
        {
            if (m_DisplayRoot != null)
            {
                if (!m_DisplayRoot.gameObject.activeSelf)
                    m_DisplayRoot.gameObject.SetActive(true);
                return;
            }

            var displayObject = new GameObject("HoWarudoBlendShapeBillboard");
            displayObject.hideFlags = HideFlags.DontSave;
            m_DisplayRoot = displayObject.transform;
            m_DisplayRoot.SetParent(transform, false);

            m_TitleText = CreateTextMesh("Title", m_DisplayRoot);
            ApplyVisualSettings();
        }

        private TextMesh CreateTextMesh(string objectName, Transform parent)
        {
            var textObject = new GameObject(objectName);
            textObject.hideFlags = HideFlags.DontSave;
            textObject.transform.SetParent(parent, false);
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.richText = false;
            textMesh.alignment = TextAlignment.Left;
            textMesh.anchor = TextAnchor.UpperLeft;
            return textMesh;
        }

        private void RefreshTextIfNeeded()
        {
            if (m_DisplayRoot == null)
                return;

            Mesh mesh = targetRenderer == null ? null : targetRenderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                m_LastRenderer = targetRenderer;
                m_LastMesh = null;
                m_LastVisibleCount = 0;
                CacheTextSettings();
                SetStatusText("No blend shapes");
                return;
            }

            int blendShapeCount = mesh.blendShapeCount;
            EnsureWeightCache(blendShapeCount);
            bool valuesChanged = targetRenderer != m_LastRenderer
                || mesh != m_LastMesh
                || HaveTextSettingsChanged();
            for (int i = 0; i < blendShapeCount; i++)
            {
                float weight = targetRenderer.GetBlendShapeWeight(i);
                if (!Mathf.Approximately(weight, m_LastWeights[i]))
                {
                    valuesChanged = true;
                    m_LastWeights[i] = weight;
                }
            }

            if (!valuesChanged && m_LastVisibleCount >= 0)
                return;

            m_LastRenderer = targetRenderer;
            m_LastMesh = mesh;
            CacheTextSettings();
            CollectVisibleEntries(mesh);

            int count = maxEntries <= 0
                ? m_Entries.Count
                : Mathf.Min(maxEntries, m_Entries.Count);
            int groupCount = Mathf.Max(1, (count + RowsPerColumnGroup - 1) / RowsPerColumnGroup);
            EnsureColumnGroupCount(groupCount);
            UpdateTitle(count);

            if (count == 0)
            {
                SetGroupText(m_ColumnGroups[0], "No active blend shapes", string.Empty);
            }
            else
            {
                string valueFormat = "F" + Mathf.Clamp(decimalPlaces, 0, 4);
                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    int start = groupIndex * RowsPerColumnGroup;
                    int end = Mathf.Min(start + RowsPerColumnGroup, count);
                    BuildColumnText(start, end, valueFormat);
                    SetGroupText(m_ColumnGroups[groupIndex], m_NameBuilder.ToString(), m_ValueBuilder.ToString());
                }
            }

            LayoutColumnGroups(groupCount);
            m_LastVisibleCount = m_Entries.Count;
        }

        private void CollectVisibleEntries(Mesh mesh)
        {
            m_Entries.Clear();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = m_LastWeights[i];
                if (onlyNonZero && Mathf.Abs(weight) <= nonZeroThreshold)
                    continue;

                m_Entries.Add(new BlendShapeEntry
                {
                    weight = weight,
                    name = mesh.GetBlendShapeName(i)
                });
            }
        }

        private void BuildColumnText(int start, int end, string valueFormat)
        {
            m_NameBuilder.Length = 0;
            m_ValueBuilder.Length = 0;
            for (int i = start; i < end; i++)
            {
                BlendShapeEntry entry = m_Entries[i];
                m_NameBuilder.Append(entry.name);
                m_ValueBuilder.Append(entry.weight.ToString(valueFormat));
                if (i + 1 < end)
                {
                    m_NameBuilder.Append('\n');
                    m_ValueBuilder.Append('\n');
                }
            }
        }

        private void UpdateTitle(int shownCount)
        {
            m_TitleText.gameObject.SetActive(showTitle);
            if (!showTitle)
                return;

            m_NameBuilder.Length = 0;
            m_NameBuilder.Append(targetRenderer.name);
            m_NameBuilder.Append(" | Blend Shapes");
            if (m_Entries.Count > shownCount)
            {
                m_NameBuilder.Append(" (+");
                m_NameBuilder.Append(m_Entries.Count - shownCount);
                m_NameBuilder.Append(" more)");
            }

            m_TitleText.text = m_NameBuilder.ToString();
            m_TitleText.transform.localPosition = Vector3.zero;
        }

        private void SetStatusText(string status)
        {
            EnsureColumnGroupCount(1);
            m_TitleText.gameObject.SetActive(false);
            SetGroupText(m_ColumnGroups[0], status, string.Empty);
            LayoutColumnGroups(1);
        }

        private void EnsureColumnGroupCount(int requiredCount)
        {
            while (m_ColumnGroups.Count < requiredCount)
            {
                var rootObject = new GameObject("ColumnGroup" + m_ColumnGroups.Count);
                rootObject.hideFlags = HideFlags.DontSave;
                rootObject.transform.SetParent(m_DisplayRoot, false);
                m_ColumnGroups.Add(new ColumnGroup
                {
                    root = rootObject,
                    names = CreateTextMesh("Names", rootObject.transform),
                    values = CreateTextMesh("Values", rootObject.transform)
                });
            }

            for (int i = 0; i < m_ColumnGroups.Count; i++)
                m_ColumnGroups[i].root.SetActive(i < requiredCount);
            ApplyVisualSettings();
        }

        private static void SetGroupText(ColumnGroup group, string names, string values)
        {
            group.names.text = names;
            group.values.text = values;
        }

        private void LayoutColumnGroups(int groupCount)
        {
            float x = 0f;
            float y = showTitle && m_TitleText.gameObject.activeSelf
                ? -MeasureLineHeight(m_TitleText)
                : 0f;
            for (int i = 0; i < groupCount; i++)
            {
                ColumnGroup group = m_ColumnGroups[i];
                group.root.transform.localPosition = new Vector3(x, y, 0f);

                float nameWidth = MeasureTextWidth(group.names, group.names.text);
                float valueWidth = MeasureTextWidth(group.values, group.values.text);
                group.names.transform.localPosition = Vector3.zero;
                group.values.transform.localPosition = new Vector3(nameWidth + NameValueGap, 0f, 0f);
                x += nameWidth + NameValueGap + valueWidth + ColumnGroupGap;
            }
        }

        private static float MeasureTextWidth(TextMesh textMesh, string text)
        {
            if (textMesh == null || string.IsNullOrEmpty(text))
                return 0f;

            Font font = textMesh.font;
            if (font == null)
                return text.Length;

            int size = Mathf.Max(1, textMesh.fontSize);
            font.RequestCharactersInTexture(text, size, textMesh.fontStyle);
            float currentLine = 0f;
            float widestLine = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\n')
                {
                    widestLine = Mathf.Max(widestLine, currentLine);
                    currentLine = 0f;
                    continue;
                }

                CharacterInfo info;
                if (font.GetCharacterInfo(character, out info, size, textMesh.fontStyle))
                    currentLine += info.advance * textMesh.characterSize / size;
                else
                    currentLine += textMesh.characterSize * 0.5f;
            }

            return Mathf.Max(widestLine, currentLine);
        }

        private static float MeasureLineHeight(TextMesh textMesh)
        {
            if (textMesh == null)
                return 1f;

            int size = Mathf.Max(1, textMesh.fontSize);
            float fontLineHeight = textMesh.font != null
                ? textMesh.font.lineHeight * textMesh.characterSize / size
                : textMesh.characterSize;
            return Mathf.Max(0.01f, fontLineHeight * Mathf.Max(0.1f, textMesh.lineSpacing));
        }

        private void ApplyVisualSettings()
        {
            ApplyTextStyle(m_TitleText);
            for (int i = 0; i < m_ColumnGroups.Count; i++)
            {
                ApplyTextStyle(m_ColumnGroups[i].names);
                ApplyTextStyle(m_ColumnGroups[i].values);
            }
        }

        private void ApplyTextStyle(TextMesh textMesh)
        {
            if (textMesh == null)
                return;

            textMesh.fontSize = Mathf.Max(1, fontSize);
            textMesh.lineSpacing = Mathf.Max(0.1f, lineSpacing);
            textMesh.color = textColor;
        }

        private bool HaveTextSettingsChanged()
        {
            return m_LastShowTitle != showTitle
                || m_LastOnlyNonZero != onlyNonZero
                || !Mathf.Approximately(m_LastNonZeroThreshold, nonZeroThreshold)
                || m_LastDecimalPlaces != decimalPlaces
                || m_LastMaxEntries != maxEntries
                || m_LastFontSize != fontSize
                || !Mathf.Approximately(m_LastLineSpacing, lineSpacing);
        }

        private void CacheTextSettings()
        {
            m_LastShowTitle = showTitle;
            m_LastOnlyNonZero = onlyNonZero;
            m_LastNonZeroThreshold = nonZeroThreshold;
            m_LastDecimalPlaces = decimalPlaces;
            m_LastMaxEntries = maxEntries;
            m_LastFontSize = fontSize;
            m_LastLineSpacing = lineSpacing;
        }

        private void EnsureWeightCache(int blendShapeCount)
        {
            if (m_LastWeights != null && m_LastWeights.Length == blendShapeCount)
                return;

            m_LastWeights = new float[blendShapeCount];
            for (int i = 0; i < m_LastWeights.Length; i++)
                m_LastWeights[i] = float.NaN;
            m_LastVisibleCount = -1;
        }

        private void UpdateDisplayTransform()
        {
            if (m_DisplayRoot == null)
                return;

            m_DisplayRoot.localPosition = Vector3.zero;
            m_DisplayRoot.localScale = Vector3.one * Mathf.Max(0.001f, worldScale);
            if (!faceCamera)
            {
                m_DisplayRoot.localRotation = TextFacingCorrection;
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                m_DisplayRoot.localRotation = TextFacingCorrection;
                return;
            }

            Vector3 direction = camera.transform.position - m_DisplayRoot.position;
            if (direction.sqrMagnitude > 0.000001f)
            {
                m_DisplayRoot.rotation = Quaternion.LookRotation(direction, camera.transform.up)
                    * TextFacingCorrection;
            }
        }

        private Camera ResolveCamera()
        {
            if (viewCamera != null)
                return viewCamera;

            if (m_CachedCamera == null || !m_CachedCamera.isActiveAndEnabled)
                m_CachedCamera = Camera.main;
            return m_CachedCamera;
        }
    }
}
