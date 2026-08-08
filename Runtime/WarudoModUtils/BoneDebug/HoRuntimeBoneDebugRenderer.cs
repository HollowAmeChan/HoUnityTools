using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Hollow.HoUnityTools.BoneRendering;

namespace Hollow.HoUnityTools.WarudoModUtils
{
    /// <summary>
    /// Runtime skeleton diagnostics for Warudo and other player builds.
    /// This component intentionally has no UnityEditor or Warudo dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Bone Debug Renderer")]
    public sealed class HoRuntimeBoneDebugRenderer : MonoBehaviour, IHoWarudoRuntimeModule
    {
        [Header("Source")]
        [Tooltip("Skeleton root. All descendants are collected as debug nodes.")]
        public Transform skeletonRoot;

        [Tooltip("Include the skeleton root when drawing axes.")]
        public bool includeRoot = true;

        [Header("Display")]
        public bool drawBones = true;
        public bool drawAxes = true;
        [Tooltip("Fallback world-space width used only when a custom material does not expose _LineWidth.")]
        public float lineWidth = 0.006f;
        [Tooltip("Shader billboard path width in screen pixels.")]
        public float lineWidthPixels = 3f;
        public float axisLength = 0.04f;
        public Color boneColor = new Color(0.2f, 0.65f, 1f, 0.9f);
        public Color xAxisColor = new Color(1f, 0.15f, 0.15f, 0.95f);
        public Color yAxisColor = new Color(0.2f, 1f, 0.2f, 0.95f);
        public Color zAxisColor = new Color(0.2f, 0.45f, 1f, 0.95f);

        [Header("Grouping")]
        [Tooltip("Optional bone collection asset used to filter runtime drawing.")]
        public HoBoneGroupSet boneGroupSet;

        [Tooltip("Collections hidden by the runtime Hub. The asset itself is not modified.")]
        public List<string> hiddenCollections = new List<string>();

        [Tooltip("Use the collection color for bone links when a collection asset is assigned.")]
        public bool useCollectionColors = true;

        [Header("Runtime")]
        [Tooltip("Camera used to orient the line ribbons. Camera.main is used when empty.")]
        public Camera viewCamera;

        [Tooltip("Optional runtime-compatible material. When empty, the bundled debug shader is used.")]
        public Material debugMaterial;

        [Tooltip("Force the debug material into the overlay queue and disable depth testing when supported.")]
        public bool forceOverlay = true;

        [Tooltip("Draw from Camera.onPostRender so the debug geometry is composited after the character. Requires the built-in render pipeline.")]
        public bool drawAfterCamera = true;

        [Tooltip("Rebuild the collected Transform list after changing the hierarchy.")]
        public bool refreshOnEnable = true;

        private readonly List<Transform> m_Nodes = new List<Transform>();
        private readonly HashSet<Transform> m_NodeSet = new HashSet<Transform>();
        private readonly List<Vector3> m_Vertices = new List<Vector3>();
        private readonly List<Vector3> m_OtherVertices = new List<Vector3>();
        private readonly List<Color> m_Colors = new List<Color>();
        private readonly List<Vector2> m_Uvs = new List<Vector2>();
        private readonly List<int> m_Indices = new List<int>();

        private Mesh m_Mesh;
        private MeshFilter m_MeshFilter;
        private MeshRenderer m_MeshRenderer;
        private Material m_Material;
        private Transform m_DrawTransform;
        private Camera m_CachedCamera;
        private bool m_OwnsMaterial;
        private bool m_IsReady;
        private bool m_UsesShaderBillboard;
        private int m_VisibleNodeCount;
        private Vector2 m_RuntimeCollectionScroll;

        private static readonly int[] QuadTriangles = { 0, 1, 2, 2, 1, 3 };
        private static readonly List<HoRuntimeBoneDebugRenderer> ActiveRenderers =
            new List<HoRuntimeBoneDebugRenderer>();

        public string Id
        {
            get { return "HoRuntimeBoneDebugRenderer/" + GetInstanceID(); }
        }

        public string DisplayName
        {
            get { return "Bone Debug / " + gameObject.name; }
        }

        public int Order
        {
            get { return 100; }
        }

        private void OnEnable()
        {
            if (hiddenCollections == null)
                hiddenCollections = new List<string>();

            if (!ActiveRenderers.Contains(this))
                ActiveRenderers.Add(this);
            if (ActiveRenderers.Count == 1)
                Camera.onPostRender += DrawActiveRenderers;

            EnsureResources();
            if (refreshOnEnable)
                RefreshSkeleton();
            RebuildMesh();
        }

        private void Start()
        {
            HoWarudoRuntimeHub.Current?.Register(this);
        }

        private void LateUpdate()
        {
            if (!m_IsReady)
                return;

            RebuildMesh();
        }

        private void OnDrawGizmos()
        {
            if (skeletonRoot == null)
                return;

            Color previousColor = Gizmos.color;
            if (includeRoot)
            {
                DrawSceneGizmos(skeletonRoot);
            }
            else
            {
                for (int i = 0; i < skeletonRoot.childCount; i++)
                    DrawSceneGizmos(skeletonRoot.GetChild(i));
            }

            Gizmos.color = previousColor;
        }

        private void DrawSceneGizmos(Transform node)
        {
            Color nodeColor;
            bool nodeVisible = TryGetNodeColor(node, out nodeColor);

            if (nodeVisible && drawBones)
            {
                for (int i = 0; i < node.childCount; i++)
                {
                    Transform child = node.GetChild(i);
                    Color childColor;
                    if (TryGetNodeColor(child, out childColor))
                    {
                        Gizmos.color = ResolveBoneColor(nodeColor);
                        Gizmos.DrawLine(node.position, child.position);
                    }
                }
            }

            if (nodeVisible && drawAxes)
            {
                float length = Mathf.Max(0f, axisLength);
                if (length > 0f)
                {
                    Gizmos.color = xAxisColor;
                    Gizmos.DrawLine(node.position, node.position + node.right * length);
                    Gizmos.color = yAxisColor;
                    Gizmos.DrawLine(node.position, node.position + node.up * length);
                    Gizmos.color = zAxisColor;
                    Gizmos.DrawLine(node.position, node.position + node.forward * length);
                }
            }

            for (int i = 0; i < node.childCount; i++)
                DrawSceneGizmos(node.GetChild(i));
        }

        private bool TryGetNodeColor(Transform node, out Color collectionColor)
        {
            if (node == null)
            {
                collectionColor = boneColor;
                return false;
            }

            if (boneGroupSet == null)
            {
                collectionColor = boneColor;
                return true;
            }

            if (boneGroupSet.collections == null)
            {
                collectionColor = boneColor;
                return true;
            }

            return boneGroupSet.IsBoneVisible(node.name, hiddenCollections, out collectionColor);
        }

        private Color ResolveBoneColor(Color collectionColor)
        {
            return boneGroupSet != null && useCollectionColors ? collectionColor : boneColor;
        }

        private void OnDisable()
        {
            HoWarudoRuntimeHub.Current?.Unregister(this);

            if (m_MeshRenderer != null)
                m_MeshRenderer.enabled = false;

            ActiveRenderers.Remove(this);
            if (ActiveRenderers.Count == 0)
                Camera.onPostRender -= DrawActiveRenderers;
        }

        private void OnDestroy()
        {
            HoWarudoRuntimeHub.Current?.Unregister(this);

            ActiveRenderers.Remove(this);
            if (ActiveRenderers.Count == 0)
                Camera.onPostRender -= DrawActiveRenderers;

            if (m_Mesh != null)
                Destroy(m_Mesh);
            if (m_OwnsMaterial && m_Material != null)
                Destroy(m_Material);
            if (m_DrawTransform != null)
                Destroy(m_DrawTransform.gameObject);
        }

        private static void DrawActiveRenderers(Camera camera)
        {
            for (int i = ActiveRenderers.Count - 1; i >= 0; i--)
            {
                HoRuntimeBoneDebugRenderer renderer = ActiveRenderers[i];
                if (renderer == null)
                {
                    ActiveRenderers.RemoveAt(i);
                    continue;
                }

                renderer.DrawAfterCamera(camera);
            }
        }

        /// <summary>
        /// Recollect the skeleton hierarchy. Call this after adding or removing bones at runtime.
        /// </summary>
        public void RefreshSkeleton()
        {
            m_Nodes.Clear();
            m_NodeSet.Clear();

            if (skeletonRoot == null)
                return;

            if (includeRoot)
                AddNode(skeletonRoot);
            CollectChildren(skeletonRoot);
        }

        public void DrawRuntimeGUI(HoWarudoRuntimeGUIContext context)
        {
            drawBones = context.Toggle("Draw bones", drawBones);
            drawAxes = context.Toggle("Draw axes", drawAxes);
            includeRoot = context.Toggle("Include root", includeRoot);
            useCollectionColors = context.Toggle("Use collection colors", useCollectionColors);
            lineWidth = context.Slider("World line width", lineWidth, 0.0001f, 0.05f);
            lineWidthPixels = context.Slider("Screen line width", lineWidthPixels, 0.5f, 12f);
            axisLength = context.Slider("Axis length", axisLength, 0f, 0.3f);

            if (context.Button("Refresh skeleton"))
            {
                RefreshSkeleton();
                RebuildMesh();
            }

            context.Label("Collected nodes: " + m_Nodes.Count);
            context.Label("Visible nodes: " + m_VisibleNodeCount);

            if (boneGroupSet == null)
            {
                context.Label("Collection filter: none");
                return;
            }

            context.Space(6f);
            if (boneGroupSet.collections == null)
            {
                context.Label("Collection asset has no collections");
                return;
            }

            context.Label("Collections: " + boneGroupSet.name);
            m_RuntimeCollectionScroll = context.BeginScrollView(m_RuntimeCollectionScroll, GUILayout.Height(120f));
            for (int i = 0; i < boneGroupSet.collections.Count; i++)
            {
                HoBoneCollection collection = boneGroupSet.collections[i];
                if (collection == null || string.IsNullOrEmpty(collection.name))
                    continue;

                bool visible = !hiddenCollections.Contains(collection.name);
                bool nextVisible = context.Toggle(collection.name, visible);
                if (nextVisible != visible)
                {
                    if (nextVisible)
                        hiddenCollections.Remove(collection.name);
                    else if (!hiddenCollections.Contains(collection.name))
                        hiddenCollections.Add(collection.name);
                }
            }
            context.EndScrollView();
        }

        private void AddNode(Transform node)
        {
            if (node != null && m_NodeSet.Add(node))
                m_Nodes.Add(node);
        }

        private void CollectChildren(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                AddNode(child);
                CollectChildren(child);
            }
        }

        private void EnsureResources()
        {
            if (m_IsReady)
                return;

            GameObject drawObject = new GameObject("__HoRuntimeBoneDebug");
            drawObject.hideFlags = HideFlags.DontSave;
            drawObject.layer = gameObject.layer;
            m_DrawTransform = drawObject.transform;
            m_DrawTransform.SetParent(transform, false);

            m_MeshFilter = drawObject.AddComponent<MeshFilter>();
            m_MeshRenderer = drawObject.AddComponent<MeshRenderer>();
            m_MeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_MeshRenderer.receiveShadows = false;
            m_MeshRenderer.lightProbeUsage = LightProbeUsage.Off;
            m_MeshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            m_Mesh = new Mesh { name = "Ho Runtime Bone Debug Mesh" };
            m_Mesh.MarkDynamic();
            m_MeshFilter.sharedMesh = m_Mesh;

            if (debugMaterial != null)
            {
                m_Material = new Material(debugMaterial)
                {
                    name = "Ho Runtime Bone Debug Material",
                    hideFlags = HideFlags.DontSave
                };
                m_OwnsMaterial = true;
            }
            else
            {
                Shader shader = Resources.Load<Shader>("HoRuntimeDebugLine");
                if (shader == null)
                    shader = Shader.Find("Hidden/HoUnityTools/WarudoModUtils/DebugLine");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");

                if (shader != null)
                {
                    m_Material = new Material(shader)
                    {
                        name = "Ho Runtime Bone Debug Material",
                        hideFlags = HideFlags.DontSave
                    };
                    m_OwnsMaterial = true;
                }
            }

            if (m_Material != null)
            {
                m_MeshRenderer.sharedMaterial = m_Material;
                m_UsesShaderBillboard = m_Material.HasProperty("_LineWidth");
                ConfigureMaterial();
            }

            m_IsReady = m_MeshRenderer != null && m_Mesh != null && m_Material != null;
        }

        private void ConfigureMaterial()
        {
            if (!forceOverlay || m_Material == null)
                return;

            m_Material.renderQueue = (int)RenderQueue.Overlay;
            if (m_Material.HasProperty("_ZTest"))
                m_Material.SetInt("_ZTest", (int)CompareFunction.Always);
            if (m_Material.HasProperty("_ZWrite"))
                m_Material.SetInt("_ZWrite", 0);
        }

        private void RebuildMesh()
        {
            if (!m_IsReady || skeletonRoot == null || m_Nodes.Count == 0)
            {
                m_VisibleNodeCount = 0;
                if (m_MeshRenderer != null)
                    m_MeshRenderer.enabled = false;
                return;
            }

            Vector3 cameraPosition = Vector3.zero;
            if (!m_UsesShaderBillboard)
            {
                Camera camera = ResolveCamera();
                cameraPosition = camera != null
                    ? camera.transform.position
                    : skeletonRoot.position + Vector3.back * 10f;
            }
            float safeLineWidth = Mathf.Max(0.0001f, lineWidth);
            float safeAxisLength = Mathf.Max(0f, axisLength);

            m_Vertices.Clear();
            m_OtherVertices.Clear();
            m_Colors.Clear();
            m_Uvs.Clear();
            m_Indices.Clear();
            m_VisibleNodeCount = 0;

            for (int i = 0; i < m_Nodes.Count; i++)
            {
                Transform node = m_Nodes[i];
                if (node == null)
                    continue;

                Color nodeColor;
                if (!TryGetNodeColor(node, out nodeColor))
                    continue;

                m_VisibleNodeCount++;

                if (drawBones)
                {
                    for (int childIndex = 0; childIndex < node.childCount; childIndex++)
                    {
                        Transform child = node.GetChild(childIndex);
                        Color childColor;
                        if (m_NodeSet.Contains(child) && TryGetNodeColor(child, out childColor))
                            AddSegment(node.position, child.position, ResolveBoneColor(nodeColor), safeLineWidth, cameraPosition);
                    }
                }

                if (drawAxes && safeAxisLength > 0f)
                {
                    Vector3 origin = node.position;
                    AddSegment(origin, origin + node.right * safeAxisLength, xAxisColor, safeLineWidth, cameraPosition);
                    AddSegment(origin, origin + node.up * safeAxisLength, yAxisColor, safeLineWidth, cameraPosition);
                    AddSegment(origin, origin + node.forward * safeAxisLength, zAxisColor, safeLineWidth, cameraPosition);
                }
            }

            if (m_Vertices.Count == 0)
            {
                m_MeshRenderer.enabled = false;
                return;
            }

            m_Mesh.Clear(false);
            m_Mesh.SetVertices(m_Vertices);
            m_Mesh.SetUVs(0, m_Uvs);
            m_Mesh.SetUVs(1, m_OtherVertices);
            m_Mesh.SetColors(m_Colors);
            m_Mesh.SetIndices(m_Indices, MeshTopology.Triangles, 0, false);
            m_Mesh.RecalculateBounds();
            m_MeshRenderer.enabled = !drawAfterCamera;

            if (m_UsesShaderBillboard && m_Material.HasProperty("_LineWidth"))
                m_Material.SetFloat("_LineWidth", Mathf.Max(0.5f, lineWidthPixels));
        }

        private void DrawAfterCamera(Camera camera)
        {
            if (!drawAfterCamera || !m_IsReady || m_Mesh == null || m_Mesh.vertexCount == 0)
                return;

            Camera targetCamera = ResolveCamera();
            if (targetCamera != null && camera != targetCamera)
                return;

            if (m_Material == null || !m_Material.SetPass(0))
                return;

            Graphics.DrawMeshNow(m_Mesh, m_DrawTransform.localToWorldMatrix);
        }

        private Camera ResolveCamera()
        {
            if (viewCamera != null)
                return viewCamera;

            if (m_CachedCamera == null)
                m_CachedCamera = Camera.main;
            return m_CachedCamera;
        }

        private void AddSegment(Vector3 start, Vector3 end, Color color, float width, Vector3 cameraPosition)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length < 0.00001f)
                return;

            if (m_UsesShaderBillboard)
            {
                AddShaderSegment(start, end, color);
                return;
            }

            direction /= length;
            Vector3 viewDirection = ((start + end) * 0.5f - cameraPosition).normalized;
            Vector3 side = Vector3.Cross(direction, viewDirection);
            if (side.sqrMagnitude < 0.000001f)
                side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 0.000001f)
                side = Vector3.Cross(direction, Vector3.right);
            side = side.normalized * (width * 0.5f);

            int vertexStart = m_Vertices.Count;
            AddVertex(start - side, color, Vector2.zero, end);
            AddVertex(start + side, color, Vector2.up, end);
            AddVertex(end - side, color, Vector2.right, start);
            AddVertex(end + side, color, Vector2.one, start);

            for (int i = 0; i < QuadTriangles.Length; i++)
                m_Indices.Add(vertexStart + QuadTriangles[i]);
        }

        private void AddShaderSegment(Vector3 start, Vector3 end, Color color)
        {
            AddVertex(start, color, new Vector2(0f, -1f), end);
            AddVertex(start, color, new Vector2(0f, 1f), end);
            AddVertex(end, color, new Vector2(1f, -1f), start);
            AddVertex(end, color, new Vector2(1f, 1f), start);

            int vertexStart = m_Vertices.Count - 4;
            for (int i = 0; i < QuadTriangles.Length; i++)
                m_Indices.Add(vertexStart + QuadTriangles[i]);
        }

        private void AddVertex(Vector3 worldPosition, Color color, Vector2 uv, Vector3 otherPosition)
        {
            m_Vertices.Add(m_DrawTransform.InverseTransformPoint(worldPosition));
            m_OtherVertices.Add(m_DrawTransform.InverseTransformPoint(otherPosition));
            m_Colors.Add(color);
            m_Uvs.Add(uv);
        }
    }
}
