using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Hollow.HoUnityTools.BoneRendering;

namespace Hollow.HoUnityTools.Editor.BoneRendering
{
    using BoneShape = HoBoneRenderer.BoneShape;

    /// <summary>
    /// 骨骼渲染器的场景视图绘制器 — 参考 Unity Animation Rigging 的 BoneRendererUtils 实现。
    /// 负责用 GPU 实例化批量绘制金字塔/盒子骨骼,以及在场景中点击拾取选中骨骼对应的 GameObject。
    /// </summary>
    [InitializeOnLoad]
    internal static class HoBoneRendererDrawer
    {
        private sealed class BatchRenderer
        {
            private const int MaxDrawMeshInstanceCount = 1023;

            public enum SubMeshType
            {
                BoneFaces,
                BoneWire,
                Count
            }

            public Mesh mesh;
            public Material material;

            private readonly List<Matrix4x4> m_Matrices = new List<Matrix4x4>();
            private readonly List<Vector4> m_Colors = new List<Vector4>();
            private readonly List<Vector4> m_Highlights = new List<Vector4>();

            public void AddInstance(Matrix4x4 matrix, Color color, Color highlight)
            {
                m_Matrices.Add(matrix);
                m_Colors.Add(color);
                m_Highlights.Add(highlight);
            }

            public void Clear()
            {
                m_Matrices.Clear();
                m_Colors.Clear();
                m_Highlights.Clear();
            }

            private static int RenderChunkCount(int totalCount)
            {
                return Mathf.CeilToInt(totalCount / (float)MaxDrawMeshInstanceCount);
            }

            private static T[] GetRenderChunk<T>(List<T> array, int chunkIndex)
            {
                int rangeCount = (chunkIndex < (RenderChunkCount(array.Count) - 1))
                    ? MaxDrawMeshInstanceCount
                    : array.Count - (chunkIndex * MaxDrawMeshInstanceCount);

                return array.GetRange(chunkIndex * MaxDrawMeshInstanceCount, rangeCount).ToArray();
            }

            public void Render()
            {
                if (m_Matrices.Count == 0 || m_Colors.Count == 0 || m_Highlights.Count == 0)
                {
                    return;
                }

                int count = System.Math.Min(m_Matrices.Count, System.Math.Min(m_Colors.Count, m_Highlights.Count));

                Material mat = material;
                mat.SetPass(0);

                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                CommandBuffer cb = new CommandBuffer();

                int chunkCount = RenderChunkCount(count);
                for (int i = 0; i < chunkCount; ++i)
                {
                    cb.Clear();
                    Matrix4x4[] matrices = GetRenderChunk(m_Matrices, i);
                    propertyBlock.SetVectorArray("_Color", GetRenderChunk(m_Colors, i));

                    material.DisableKeyword("WIRE_ON");
                    cb.DrawMeshInstanced(mesh, (int)SubMeshType.BoneFaces, material, 0, matrices, matrices.Length, propertyBlock);
                    Graphics.ExecuteCommandBuffer(cb);

                    cb.Clear();
                    propertyBlock.SetVectorArray("_Color", GetRenderChunk(m_Highlights, i));

                    material.EnableKeyword("WIRE_ON");
                    cb.DrawMeshInstanced(mesh, (int)SubMeshType.BoneWire, material, 0, matrices, matrices.Length, propertyBlock);
                    Graphics.ExecuteCommandBuffer(cb);
                }
            }
        }
        private static readonly List<HoBoneRenderer> s_BoneRenderers = new List<HoBoneRenderer>();

        private static BatchRenderer s_PyramidMeshRenderer;
        private static BatchRenderer s_BoxMeshRenderer;
        private static Material s_Material;

        private const float k_Epsilon = 1e-5f;
        private const float k_BoneBaseSize = 2f;
        private const float k_BoneTipSize = 0.5f;

        private static readonly int s_ButtonHash = "HoBoneHandle".GetHashCode();
        private static int s_VisibleLayersCache;

        static HoBoneRendererDrawer()
        {
            HoBoneRenderer.onAddBoneRenderer += OnAddBoneRenderer;
            HoBoneRenderer.onRemoveBoneRenderer += OnRemoveBoneRenderer;
            SceneVisibilityManager.visibilityChanged += OnVisibilityChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            SceneView.duringSceneGui += DrawSkeletons;

            s_VisibleLayersCache = Tools.visibleLayers;
        }

        private static Material material
        {
            get
            {
                if (!s_Material)
                {
                    Shader shader = Shader.Find("Hidden/HoUnityTools/BoneHandles");
                    if (shader == null)
                    {
                        // Editor 文件夹下 Shader.Find 偶发找不到时,用 AssetDatabase 兜底。
                        string[] guids = AssetDatabase.FindAssets("HoBoneHandles t:Shader");
                        if (guids.Length > 0)
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                            shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                        }
                    }

                    if (shader == null)
                    {
                        return null;
                    }

                    s_Material = new Material(shader)
                    {
                        hideFlags = HideFlags.DontSaveInEditor
                    };
                    s_Material.enableInstancing = true;
                }

                return s_Material;
            }
        }

        private static BatchRenderer pyramidMeshRenderer
        {
            get
            {
                if (s_PyramidMeshRenderer == null)
                {
                    Mesh mesh = new Mesh
                    {
                        name = "HoBoneRendererPyramidMesh",
                        subMeshCount = (int)BatchRenderer.SubMeshType.Count,
                        hideFlags = HideFlags.DontSave
                    };

                    Vector3[] vertices =
                    {
                        new Vector3(0.0f, 1.0f, 0.0f),
                        new Vector3(0.0f, 0.0f, -1.0f),
                        new Vector3(-0.9f, 0.0f, 0.5f),
                        new Vector3(0.9f, 0.0f, 0.5f),
                    };
                    mesh.vertices = vertices;

                    int[] boneFaceIndices =
                    {
                        0, 2, 1,
                        0, 1, 3,
                        0, 3, 2,
                        1, 2, 3
                    };
                    mesh.SetIndices(boneFaceIndices, MeshTopology.Triangles, (int)BatchRenderer.SubMeshType.BoneFaces);

                    int[] boneWireIndices =
                    {
                        0, 1, 0, 2, 0, 3, 1, 2, 2, 3, 3, 1
                    };
                    mesh.SetIndices(boneWireIndices, MeshTopology.Lines, (int)BatchRenderer.SubMeshType.BoneWire);

                    s_PyramidMeshRenderer = new BatchRenderer
                    {
                        mesh = mesh,
                        material = material
                    };
                }

                return s_PyramidMeshRenderer;
            }
        }

        private static BatchRenderer boxMeshRenderer
        {
            get
            {
                if (s_BoxMeshRenderer == null)
                {
                    Mesh mesh = new Mesh
                    {
                        name = "HoBoneRendererBoxMesh",
                        subMeshCount = (int)BatchRenderer.SubMeshType.Count,
                        hideFlags = HideFlags.DontSave
                    };

                    Vector3[] vertices =
                    {
                        new Vector3(-0.5f, 0.0f, 0.5f),
                        new Vector3(0.5f, 0.0f, 0.5f),
                        new Vector3(0.5f, 0.0f, -0.5f),
                        new Vector3(-0.5f, 0.0f, -0.5f),
                        new Vector3(-0.5f, 1.0f, 0.5f),
                        new Vector3(0.5f, 1.0f, 0.5f),
                        new Vector3(0.5f, 1.0f, -0.5f),
                        new Vector3(-0.5f, 1.0f, -0.5f)
                    };
                    mesh.vertices = vertices;

                    int[] boneFaceIndices =
                    {
                        0, 2, 1, 0, 3, 2,
                        0, 1, 5, 0, 5, 4,
                        1, 2, 6, 1, 6, 5,
                        2, 3, 7, 2, 7, 6,
                        3, 0, 4, 3, 4, 7,
                        4, 5, 6, 4, 6, 7
                    };
                    mesh.SetIndices(boneFaceIndices, MeshTopology.Triangles, (int)BatchRenderer.SubMeshType.BoneFaces);

                    int[] boneWireIndices =
                    {
                        0, 1, 1, 2, 2, 3, 3, 0,
                        4, 5, 5, 6, 6, 7, 7, 4,
                        0, 4, 1, 5, 2, 6, 3, 7
                    };
                    mesh.SetIndices(boneWireIndices, MeshTopology.Lines, (int)BatchRenderer.SubMeshType.BoneWire);

                    s_BoxMeshRenderer = new BatchRenderer
                    {
                        mesh = mesh,
                        material = material
                    };
                }

                return s_BoxMeshRenderer;
            }
        }
        private static Matrix4x4 ComputeBoneMatrix(Vector3 start, Vector3 end, float length, float size)
        {
            Vector3 direction = (end - start) / length;
            Vector3 tangent = Vector3.Cross(direction, Vector3.up);
            if (Vector3.SqrMagnitude(tangent) < 0.1f)
            {
                tangent = Vector3.Cross(direction, Vector3.right);
            }

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(direction, tangent);

            float scale = length * k_BoneBaseSize * size;

            return new Matrix4x4(
                new Vector4(tangent.x * scale, tangent.y * scale, tangent.z * scale, 0f),
                new Vector4(direction.x * length, direction.y * length, direction.z * length, 0f),
                new Vector4(bitangent.x * scale, bitangent.y * scale, bitangent.z * scale, 0f),
                new Vector4(start.x, start.y, start.z, 1f));
        }

        private static void DrawSkeletons(SceneView sceneView)
        {
            if (Tools.visibleLayers != s_VisibleLayersCache)
            {
                OnVisibilityChanged();
                s_VisibleLayersCache = Tools.visibleLayers;
            }

            if (material == null)
            {
                return;
            }

            Color gizmoColor = Gizmos.color;

            pyramidMeshRenderer.Clear();
            boxMeshRenderer.Clear();

            for (int i = 0; i < s_BoneRenderers.Count; i++)
            {
                HoBoneRenderer boneRenderer = s_BoneRenderers[i];
                if (boneRenderer == null || boneRenderer.Bones == null)
                {
                    continue;
                }

                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                {
                    StageHandle stageHandle = prefabStage.stageHandle;
                    if (stageHandle.IsValid() && !stageHandle.Contains(boneRenderer.gameObject))
                    {
                        continue;
                    }
                }

                if (boneRenderer.drawBones)
                {
                    float size = boneRenderer.boneSize * 0.025f;
                    BoneShape shape = boneRenderer.boneShape;

                    HoBoneRenderer.BonePair[] bones = boneRenderer.Bones;
                    for (int j = 0; j < bones.Length; j++)
                    {
                        HoBoneRenderer.BonePair bone = bones[j];
                        if (bone.first == null || bone.second == null)
                        {
                            continue;
                        }

                        DoBoneRender(bone.first, bone.second, shape, bone.color, size, boneRenderer.IsBoneSelectable(bone.first.name));
                    }

                    Transform[] tips = boneRenderer.Tips;
                    Color[] tipColors = boneRenderer.TipColors;
                    for (int k = 0; k < tips.Length; k++)
                    {
                        Transform tip = tips[k];
                        if (tip == null)
                        {
                            continue;
                        }

                        Color tipColor = (tipColors != null && k < tipColors.Length) ? tipColors[k] : boneRenderer.boneColor;
                        DoBoneRender(tip, null, shape, tipColor, size, boneRenderer.IsBoneSelectable(tip.name));
                    }
                }

                if (boneRenderer.drawTripods)
                {
                    float size = boneRenderer.tripodSize * 0.025f;
                    // 只在通过分组可见性过滤的节点上绘制本地轴。
                    Transform[] transforms = boneRenderer.VisibleTransforms;
                    if (transforms == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < transforms.Length; j++)
                    {
                        Transform transform = transforms[j];
                        if (transform == null)
                        {
                            continue;
                        }

                        Vector3 position = transform.position;
                        Vector3 xAxis = position + transform.rotation * Vector3.right * size;
                        Vector3 yAxis = position + transform.rotation * Vector3.up * size;
                        Vector3 zAxis = position + transform.rotation * Vector3.forward * size;

                        Handles.color = Color.red;
                        Handles.DrawLine(position, xAxis);
                        Handles.color = Color.green;
                        Handles.DrawLine(position, yAxis);
                        Handles.color = Color.blue;
                        Handles.DrawLine(position, zAxis);
                    }
                }
            }

            pyramidMeshRenderer.Render();
            boxMeshRenderer.Render();

            Gizmos.color = gizmoColor;
        }

        private static void DoBoneRender(Transform transform, Transform childTransform, BoneShape shape, Color color, float size, bool selectable)
        {
            Vector3 start = transform.position;
            Vector3 end = childTransform != null ? childTransform.position : start;

            GameObject boneGO = transform.gameObject;
            bool pickingEnabled = selectable && !SceneVisibilityManager.instance.IsPickingDisabled(boneGO, false);

            float length = (end - start).magnitude;
            bool tipBone = length < k_Epsilon;

            int id = GUIUtility.GetControlID(s_ButtonHash, FocusType.Passive);
            Event evt = Event.current;

            switch (evt.GetTypeForControl(id))
            {
                case EventType.Layout:
                {
                    if (pickingEnabled)
                    {
                        HandleUtility.AddControl(id, tipBone
                            ? HandleUtility.DistanceToCircle(start, k_BoneTipSize * size * 0.5f)
                            : HandleUtility.DistanceToLine(start, end));
                    }
                    break;
                }
                case EventType.MouseMove:
                    if (id == HandleUtility.nearestControl)
                    {
                        HandleUtility.Repaint();
                    }

                    break;
                case EventType.MouseDown:
                {
                    if (evt.alt)
                    {
                        break;
                    }

                    if (pickingEnabled && HandleUtility.nearestControl == id && evt.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        HandleClickSelection(boneGO, evt);
                        evt.Use();
                    }

                    break;
                }
                case EventType.MouseDrag:
                {
                    if (!evt.alt && GUIUtility.hotControl == id)
                    {
                        if (pickingEnabled)
                        {
                            DragAndDrop.PrepareStartDrag();
                            DragAndDrop.objectReferences = new Object[] { transform };
                            DragAndDrop.StartDrag(ObjectNames.GetDragAndDropTitle(transform));

                            GUIUtility.hotControl = 0;
                            evt.Use();
                        }
                    }

                    break;
                }
                case EventType.MouseUp:
                {
                    if (GUIUtility.hotControl == id && (evt.button == 0 || evt.button == 2))
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }

                    break;
                }
                case EventType.Repaint:
                {
                    Color highlight = color;

                    bool hoveringBone = GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id;
                    hoveringBone = hoveringBone && pickingEnabled;

                    if (hoveringBone)
                    {
                        highlight = Handles.preselectionColor;
                    }
                    else if (Selection.Contains(boneGO) || Selection.activeObject == boneGO)
                    {
                        highlight = Handles.selectedColor;
                    }

                    if (tipBone)
                    {
                        Handles.color = highlight;
                        Handles.SphereHandleCap(0, start, Quaternion.identity, k_BoneTipSize * size, EventType.Repaint);
                    }
                    else if (shape == BoneShape.Line)
                    {
                        Handles.color = highlight;
                        Handles.DrawLine(start, end);
                    }
                    else if (shape == BoneShape.Pyramid)
                    {
                        pyramidMeshRenderer.AddInstance(ComputeBoneMatrix(start, end, length, size), color, highlight);
                    }
                    else // Box
                    {
                        boxMeshRenderer.AddInstance(ComputeBoneMatrix(start, end, length, size), color, highlight);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// 点击选中骨骼对应的 GameObject,支持 Ctrl/Cmd 与 Shift 加减选。
        /// 替代 Animation Rigging 内部的 EditorHelper.HandleClickSelection。
        /// </summary>
        private static void HandleClickSelection(GameObject gameObject, Event evt)
        {
            bool additive = evt.control || evt.command;
            bool range = evt.shift;

            if (additive || range)
            {
                var selection = new List<Object>(Selection.objects);
                if (selection.Contains(gameObject))
                {
                    selection.Remove(gameObject);
                }
                else
                {
                    selection.Add(gameObject);
                }

                Selection.objects = selection.ToArray();
            }
            else
            {
                Selection.activeGameObject = gameObject;
            }
        }

        private static void OnAddBoneRenderer(HoBoneRenderer boneRenderer)
        {
            s_BoneRenderers.Add(boneRenderer);
        }

        private static void OnRemoveBoneRenderer(HoBoneRenderer boneRenderer)
        {
            s_BoneRenderers.Remove(boneRenderer);
        }

        private static void OnVisibilityChanged()
        {
            foreach (HoBoneRenderer boneRenderer in s_BoneRenderers)
            {
                if (boneRenderer != null)
                {
                    boneRenderer.Invalidate();
                }
            }

            SceneView.RepaintAll();
        }

        private static void OnHierarchyChanged()
        {
            foreach (HoBoneRenderer boneRenderer in s_BoneRenderers)
            {
                if (boneRenderer != null)
                {
                    boneRenderer.Invalidate();
                }
            }

            SceneView.RepaintAll();
        }
    }
}
