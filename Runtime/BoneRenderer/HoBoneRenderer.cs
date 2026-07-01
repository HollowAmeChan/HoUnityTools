using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.BoneRendering
{
    /// <summary>
    /// 骨骼渲染组件 — 把骨架根拖进来即可在 Scene 视图显示全部骨骼,支持分组文件逐组开关显示。
    /// 参考 Unity Animation Rigging 的 BoneRenderer 实现,仅在编辑器场景视图工作,运行时不做任何事。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Ho Bone Renderer")]
    public sealed class HoBoneRenderer : MonoBehaviour
    {
        /// <summary>骨骼形状。</summary>
        public enum BoneShape
        {
            /// <summary>单线条。</summary>
            Line,

            /// <summary>金字塔。</summary>
            Pyramid,

            /// <summary>盒子。</summary>
            Box
        }

        [Header("Skeleton")]
        [Tooltip("骨架根节点。指定后自动递归采集其下所有子 Transform 作为骨骼。")]
        [SerializeField]
        private Transform skeletonRoot;

        [Tooltip("是否包含根节点自身参与骨骼构建。")]
        [SerializeField]
        private bool includeRoot = true;

        [Header("Display")]
        [Tooltip("骨骼形状。")]
        public BoneShape boneShape = BoneShape.Pyramid;

        [Tooltip("是否绘制骨骼。")]
        public bool drawBones = true;

        [Tooltip("是否在每根骨骼上绘制本地坐标轴。同样受分组绘制控制,只画可见骨骼的本地轴。")]
        public bool drawTripods = false;

        [Tooltip("骨骼大小。")]
        [Range(0.01f, 5.0f)]
        public float boneSize = 1.0f;

        [Tooltip("本地轴轴长。")]
        [Range(0.01f, 5.0f)]
        public float tripodSize = 1.0f;

        [Tooltip("默认骨骼颜色(未分组或无分组文件时使用)。")]
        public Color boneColor = new Color(0f, 0f, 1f, 0.5f);

        [Header("Grouping")]
        [Tooltip("骨骼分组 JSON(从 Blender 骨骼集合导出)。直接拖入项目内的 .json 即可。指定后完全由分组控制绘制:只画归属可见集合的骨骼。")]
        [SerializeField]
        private TextAsset groupJson;

        [Tooltip("被关闭显示的集合名称。可见性状态存在本组件上,分组 JSON 保持纯净以便多处复用。")]
        [SerializeField]
        private List<string> hiddenCollections = new List<string>();

        [Tooltip("被关闭 Scene 选择的集合名称。选择状态存在本组件上,不修改分组 JSON。")]
        [SerializeField]
        private List<string> unselectableCollections = new List<string>();

        [SerializeField]
        private Transform[] m_Transforms;

        // 由 groupJson 现场解析出的分组模型(内存态,不落盘)。
        private HoBoneGroupSet m_ParsedGroupSet;
        private TextAsset m_ParsedFrom;

        /// <summary>骨架根节点。赋值时自动重新采集骨骼(仅编辑器)。</summary>
        public Transform SkeletonRoot
        {
            get => skeletonRoot;
#if UNITY_EDITOR
            set
            {
                skeletonRoot = value;
                CollectFromSkeletonRoot();
            }
#endif
        }

        /// <summary>参与构建骨骼的 Transform 引用集合。</summary>
        public Transform[] Transforms => m_Transforms;

        /// <summary>分组 JSON(TextAsset)。赋值时重建骨骼。</summary>
        public TextAsset GroupJson
        {
            get => groupJson;
            set
            {
                groupJson = value;
                m_ParsedGroupSet = null;
                m_ParsedFrom = null;
#if UNITY_EDITOR
                ExtractBones();
#endif
            }
        }

        /// <summary>
        /// 当前生效的分组模型:由 groupJson 现场解析而来,未指定则为 null。
        /// 解析结果按来源缓存,内容不变时不重复解析。
        /// </summary>
        public HoBoneGroupSet GetActiveGroupSet()
        {
            if (groupJson == null)
            {
                m_ParsedGroupSet = null;
                m_ParsedFrom = null;
                return null;
            }

            if (m_ParsedGroupSet == null || m_ParsedFrom != groupJson)
            {
                m_ParsedGroupSet = HoBoneGroupSet.CreateFromJson(groupJson.text);
                m_ParsedFrom = groupJson;
            }

            return m_ParsedGroupSet;
        }

        /// <summary>
        /// 强制丢弃缓存并重新解析分组 JSON,再重建骨骼。
        /// 用于外部重新导出覆盖了同一个 .json(TextAsset 引用未变,缓存不会自动失效)时手动刷新。
        /// </summary>
        public void RefreshGroupJson()
        {
            m_ParsedGroupSet = null;
            m_ParsedFrom = null;
#if UNITY_EDITOR
            ExtractBones();
#endif
        }

        /// <summary>被关闭显示的集合名称列表。</summary>
        public List<string> HiddenCollections => hiddenCollections;

        /// <summary>被关闭 Scene 选择的集合名称列表。</summary>
        public List<string> UnselectableCollections
        {
            get
            {
                if (unselectableCollections == null)
                {
                    unselectableCollections = new List<string>();
                }

                return unselectableCollections;
            }
        }

        /// <summary>判断指定骨骼当前是否允许在 Scene 视图中被点击选择。</summary>
        public bool IsBoneSelectable(string boneName)
        {
            HoBoneGroupSet activeSet = GetActiveGroupSet();
            return activeSet == null || activeSet.IsBoneSelectable(boneName, UnselectableCollections);
        }

#if UNITY_EDITOR
        /// <summary>由两个 Transform 描述的一根骨骼,带显示颜色。</summary>
        public struct BonePair
        {
            public Transform first;
            public Transform second;
            public Color color;
        }

        private BonePair[] m_Bones;
        private Transform[] m_Tips;
        private Color[] m_TipColors;
        private Transform[] m_VisibleTransforms;

        /// <summary>从 Transform 引用解析出的骨骼。</summary>
        public BonePair[] Bones => m_Bones;

        /// <summary>末端骨骼(无子骨骼的节点)。</summary>
        public Transform[] Tips => m_Tips;

        /// <summary>末端骨骼对应颜色。</summary>
        public Color[] TipColors => m_TipColors;

        /// <summary>经分组可见性过滤后仍显示的 Transform(供本地轴绘制,与骨骼绘制同受分组控制)。</summary>
        public Transform[] VisibleTransforms => m_VisibleTransforms;

        public delegate void OnAddBoneRendererCallback(HoBoneRenderer boneRenderer);

        public delegate void OnRemoveBoneRendererCallback(HoBoneRenderer boneRenderer);

        /// <summary>组件 OnEnable 时的通知回调。</summary>
        public static OnAddBoneRendererCallback onAddBoneRenderer;

        /// <summary>组件 OnDisable 时的通知回调。</summary>
        public static OnRemoveBoneRendererCallback onRemoveBoneRenderer;

        private void OnEnable()
        {
            ExtractBones();
            onAddBoneRenderer?.Invoke(this);
        }

        private void OnDisable()
        {
            onRemoveBoneRenderer?.Invoke(this);
        }

        /// <summary>重置为默认值。</summary>
        public void Reset()
        {
            ClearBones();
        }

        /// <summary>重新采集并重建骨骼。</summary>
        public void Invalidate()
        {
            ExtractBones();
        }

        /// <summary>清空骨骼数据。</summary>
        public void ClearBones()
        {
            m_Bones = null;
            m_Tips = null;
            m_TipColors = null;
            m_VisibleTransforms = null;
        }

        /// <summary>从骨架根递归采集所有 Transform,并重建骨骼。</summary>
        public void CollectFromSkeletonRoot()
        {
            if (skeletonRoot == null)
            {
                m_Transforms = null;
                ClearBones();
                return;
            }

            var collected = new List<Transform>();
            if (includeRoot)
            {
                collected.Add(skeletonRoot);
            }

            CollectChildrenRecursive(skeletonRoot, collected);
            m_Transforms = collected.ToArray();
            ExtractBones();
        }

        private static void CollectChildrenRecursive(Transform parent, List<Transform> output)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                output.Add(child);
                CollectChildrenRecursive(child, output);
            }
        }

        /// <summary>从 Transform 引用构建骨骼与末端骨骼,并按分组可见性过滤。</summary>
        public void ExtractBones()
        {
            if (m_Transforms == null || m_Transforms.Length == 0)
            {
                ClearBones();
                return;
            }

            var transformsHashSet = new HashSet<Transform>(m_Transforms);
            var bonesList = new List<BonePair>(m_Transforms.Length);
            var tipsList = new List<Transform>(m_Transforms.Length);
            var tipColorsList = new List<Color>(m_Transforms.Length);
            var visibleList = new List<Transform>(m_Transforms.Length);

            for (int i = 0; i < m_Transforms.Length; ++i)
            {
                Transform transform = m_Transforms[i];
                if (transform == null)
                {
                    continue;
                }

                if (UnityEditor.SceneVisibilityManager.instance.IsHidden(transform.gameObject, false))
                {
                    continue;
                }

                int mask = UnityEditor.Tools.visibleLayers;
                if ((mask & (1 << transform.gameObject.layer)) == 0)
                {
                    continue;
                }

                // 分组可见性过滤:被关闭集合中的骨骼跳过。
                if (!ResolveBoneVisibility(transform.name, out Color boneVisibleColor))
                {
                    continue;
                }

                // 通过可见性过滤的节点,同时供本地轴绘制使用。
                visibleList.Add(transform);

                bool hasValidChildren = false;
                if (transform.childCount > 0)
                {
                    for (int k = 0; k < transform.childCount; ++k)
                    {
                        Transform childTransform = transform.GetChild(k);
                        if (transformsHashSet.Contains(childTransform))
                        {
                            bonesList.Add(new BonePair { first = transform, second = childTransform, color = boneVisibleColor });
                            hasValidChildren = true;
                        }
                    }
                }

                if (!hasValidChildren)
                {
                    tipsList.Add(transform);
                    tipColorsList.Add(boneVisibleColor);
                }
            }

            m_Bones = bonesList.ToArray();
            m_Tips = tipsList.ToArray();
            m_TipColors = tipColorsList.ToArray();
            m_VisibleTransforms = visibleList.ToArray();
        }

        /// <summary>
        /// 解析单根骨骼的可见性与颜色。无分组文件时始终可见并用默认色。
        /// </summary>
        private bool ResolveBoneVisibility(string boneName, out Color resolvedColor)
        {
            resolvedColor = boneColor;
            HoBoneGroupSet activeSet = GetActiveGroupSet();
            if (activeSet == null)
            {
                return true;
            }

            if (!activeSet.IsBoneVisible(boneName, hiddenCollections, out Color groupColor))
            {
                return false;
            }

            // 命中可见集合时用集合色;不属于任何集合时 groupColor 为 default,回退默认色。
            if (groupColor != default)
            {
                resolvedColor = groupColor;
            }

            return true;
        }
#endif // UNITY_EDITOR
    }
}
