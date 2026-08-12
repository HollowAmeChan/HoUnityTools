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
        [Header("数据来源")]
        [InspectorName("目标蒙皮网格")]
        [Tooltip("要读取形态键真实权重的 SkinnedMeshRenderer。")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("世界屏幕")]
        [InspectorName("位置锚点")]
        [Tooltip("留空时使用本组件的 Transform。可将组件挂在单独空物体上并摆在角色旁边。")]
        public Transform anchor;

        [InspectorName("本地偏移")]
        [Tooltip("相对于锚点的本地位置偏移。")]
        public Vector3 localOffset;

        [InspectorName("面向相机")]
        [Tooltip("关闭时保持位置锚点的世界朝向，像固定在世界中的屏幕。")]
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
        [Tooltip("0 表示显示全部；大于 0 时限制显示数量。")]
        public int maxEntries;

        [InspectorName("字体大小")]
        [Min(1)]
        public int fontSize = 26;

        [InspectorName("文字颜色")]
        public Color textColor = Color.white;

        [InspectorName("文字对齐")]
        public TextAlignment textAlignment = TextAlignment.Left;

        [InspectorName("文字锚点")]
        public TextAnchor textAnchor = TextAnchor.MiddleCenter;

        [InspectorName("整体缩放")]
        [Min(0.001f)]
        public float worldScale = 0.01f;

        private readonly List<BlendShapeEntry> m_Entries = new List<BlendShapeEntry>();
        private readonly StringBuilder m_TextBuilder = new StringBuilder(1024);

        private TextMesh m_TextMesh;
        private SkinnedMeshRenderer m_LastRenderer;
        private Mesh m_LastMesh;
        private float[] m_LastWeights;
        private int m_LastVisibleCount = -1;
        private string m_LastText;
        private Camera m_CachedCamera;
        private bool m_LastShowTitle;
        private bool m_LastOnlyNonZero;
        private float m_LastNonZeroThreshold;
        private int m_LastDecimalPlaces;
        private int m_LastMaxEntries;

        private struct BlendShapeEntry
        {
            public float weight;
            public string name;
        }

        private void OnEnable()
        {
            EnsureTextMesh();
            RefreshImmediately();
        }

        private void LateUpdate()
        {
            EnsureTextMesh();
            ApplyVisualSettings();
            RefreshTextIfNeeded();
            UpdateBillboardTransform();
        }

        private void OnDisable()
        {
            if (m_TextMesh != null)
                m_TextMesh.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (m_TextMesh == null)
                return;

            if (Application.isPlaying)
                Destroy(m_TextMesh.gameObject);
            else
                DestroyImmediate(m_TextMesh.gameObject);
        }

        /// <summary>Forces the billboard to reread mesh metadata and current blend-shape weights.</summary>
        public void RefreshImmediately()
        {
            m_LastMesh = null;
            m_LastRenderer = null;
            m_LastVisibleCount = -1;
            m_LastText = null;
            m_LastShowTitle = !showTitle;
            RefreshTextIfNeeded();
        }

        private void EnsureTextMesh()
        {
            if (m_TextMesh != null)
            {
                if (!m_TextMesh.gameObject.activeSelf)
                    m_TextMesh.gameObject.SetActive(true);
                return;
            }

            var billboardObject = new GameObject("HoWarudoBlendShapeBillboard");
            billboardObject.hideFlags = HideFlags.DontSave;
            m_TextMesh = billboardObject.AddComponent<TextMesh>();
            m_TextMesh.richText = false;
            ApplyVisualSettings();
        }

        private void RefreshTextIfNeeded()
        {
            if (m_TextMesh == null)
                return;

            Mesh mesh = targetRenderer == null ? null : targetRenderer.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0)
            {
                m_LastMesh = null;
                m_LastVisibleCount = -1;
                ApplyText("No blend shapes");
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
            m_Entries.Clear();
            for (int i = 0; i < blendShapeCount; i++)
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

            int count = maxEntries <= 0
                ? m_Entries.Count
                : Mathf.Min(maxEntries, m_Entries.Count);
            m_TextBuilder.Length = 0;
            if (showTitle)
            {
                m_TextBuilder.Append(targetRenderer.name);
                m_TextBuilder.Append(" | Blend Shapes");
                m_TextBuilder.Append('\n');
            }

            if (count == 0)
            {
                m_TextBuilder.Append("No active blend shapes");
            }
            else
            {
                string format = "F" + Mathf.Clamp(decimalPlaces, 0, 4);
                for (int i = 0; i < count; i++)
                {
                    BlendShapeEntry entry = m_Entries[i];
                    m_TextBuilder.Append(entry.name);
                    m_TextBuilder.Append(": ");
                    m_TextBuilder.Append(entry.weight.ToString(format));
                    if (i + 1 < count)
                        m_TextBuilder.Append('\n');
                }
            }

            if (m_Entries.Count > count)
            {
                m_TextBuilder.Append('\n');
                m_TextBuilder.Append("+ ");
                m_TextBuilder.Append(m_Entries.Count - count);
                m_TextBuilder.Append(" more");
            }

            m_LastVisibleCount = m_Entries.Count;
            ApplyText(m_TextBuilder.ToString());
        }

        private void ApplyVisualSettings()
        {
            if (m_TextMesh == null)
                return;

            m_TextMesh.fontSize = Mathf.Max(1, fontSize);
            m_TextMesh.color = textColor;
            m_TextMesh.alignment = textAlignment;
            m_TextMesh.anchor = textAnchor;
        }

        private bool HaveTextSettingsChanged()
        {
            return m_LastShowTitle != showTitle
                || m_LastOnlyNonZero != onlyNonZero
                || !Mathf.Approximately(m_LastNonZeroThreshold, nonZeroThreshold)
                || m_LastDecimalPlaces != decimalPlaces
                || m_LastMaxEntries != maxEntries;
        }

        private void CacheTextSettings()
        {
            m_LastShowTitle = showTitle;
            m_LastOnlyNonZero = onlyNonZero;
            m_LastNonZeroThreshold = nonZeroThreshold;
            m_LastDecimalPlaces = decimalPlaces;
            m_LastMaxEntries = maxEntries;
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

        private void ApplyText(string value)
        {
            if (m_LastText == value)
                return;

            m_LastText = value;
            m_TextMesh.text = value;
        }

        private void UpdateBillboardTransform()
        {
            if (m_TextMesh == null)
                return;

            Transform targetAnchor = anchor != null ? anchor : transform;
            Transform billboardTransform = m_TextMesh.transform;
            billboardTransform.position = targetAnchor.TransformPoint(localOffset);
            billboardTransform.localScale = Vector3.one * Mathf.Max(0.001f, worldScale);

            if (!faceCamera)
            {
                billboardTransform.rotation = targetAnchor.rotation;
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                billboardTransform.rotation = targetAnchor.rotation;
                return;
            }

            Vector3 direction = billboardTransform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.000001f)
                billboardTransform.rotation = Quaternion.LookRotation(direction, camera.transform.up);
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
