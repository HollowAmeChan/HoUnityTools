#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>
    /// Standalone editor port of HoGeometryDataOutlineCorrectionBuilder (SOLIDIFY_RAW2SMOOTH).
    /// RGB is a TBN direction; A retains GD's 0.5-neutral encoding, consumed directly as width
    /// by lilToon's _OutlineVertexR2Width = 2. Keep algorithm changes in sync with the GD builder.
    /// </summary>
    internal static class HoFbxOutlineCorrectionBuilder
    {
        private sealed class Face
        {
            internal int[] points;
            internal Vector3 normal;
        }

        private sealed class Edge
        {
            internal int a, b;
            internal readonly List<int> faces = new List<int>(2);
        }

        private readonly struct PointKey : IEquatable<PointKey>
        {
            private readonly Vector3 position;
            private readonly ulong deformation;

            internal PointKey(Vector3 position, ulong deformation)
            {
                this.position = position;
                this.deformation = deformation;
            }

            public bool Equals(PointKey other) => position.Equals(other.position) && deformation == other.deformation;
            public override bool Equals(object other) => other is PointKey key && Equals(key);
            public override int GetHashCode() => unchecked(position.GetHashCode() * 397 ^ deformation.GetHashCode());
        }

        private static ulong Hash(ulong h, int value) => unchecked((h ^ (uint)value) * 1099511628211UL);

        internal static Vector4[] Build(Mesh mesh, bool weldCoincidentVertices = true)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            // Import callbacks can access mesh data even when the final asset has Read/Write off.
            // Do not reject based on isReadable (GD's runtime builder has that extra restriction).
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            if (normals.Length != vertices.Length || tangents.Length != vertices.Length)
                throw new InvalidOperationException("Mesh 必须提供法线和切线，以编码 lilToon RGBA 描边方向。");

            var signature = new ulong[vertices.Length];
            for (int i = 0; i < signature.Length; i++) signature[i] = 1469598103934665603UL;
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                int offset = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    signature[i] = Hash(signature[i], counts[i]);
                    for (int j = 0; j < counts[i]; j++)
                    {
                        var weight = weights[offset++];
                        signature[i] = Hash(signature[i], weight.boneIndex);
                        signature[i] = Hash(signature[i], weight.weight.GetHashCode());
                    }
                }
            }

            // UV / hard-normal seam duplicates can weld only if their deformation also matches.
            if (mesh.blendShapeCount > 0)
            {
                var deltas = new Vector3[vertices.Length];
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, deltas, null, null);
                    for (int i = 0; i < deltas.Length; i++)
                    {
                        signature[i] = Hash(signature[i], deltas[i].x.GetHashCode());
                        signature[i] = Hash(signature[i], deltas[i].y.GetHashCode());
                        signature[i] = Hash(signature[i], deltas[i].z.GetHashCode());
                    }
                }
            }

            var pointLookup = new Dictionary<PointKey, int>();
            var positions = new List<Vector3>();
            var fallbackNormals = new List<Vector3>();
            var map = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = new PointKey(vertices[i], weldCoincidentVertices ? signature[i] : (ulong)i);
                if (!pointLookup.TryGetValue(key, out int point))
                {
                    point = positions.Count;
                    pointLookup.Add(key, point);
                    positions.Add(vertices[i]);
                    fallbackNormals.Add(Vector3.zero);
                }
                map[i] = point;
                fallbackNormals[point] += normals[i];
            }

            var faces = new List<Face>();
            var edges = new Dictionary<ulong, Edge>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                MeshTopology topology = mesh.GetTopology(submesh);
                int size = topology == MeshTopology.Triangles ? 3 : topology == MeshTopology.Quads ? 4 : 0;
                if (size == 0) throw new InvalidOperationException("描边修正仅支持三角形或四边形拓扑。");
                int[] indices = mesh.GetIndices(submesh, true);
                for (int start = 0; start < indices.Length; start += size)
                {
                    var pointIndices = new int[size];
                    for (int j = 0; j < size; j++) pointIndices[j] = map[indices[start + j]];
                    Vector3 normal = Vector3.zero;
                    for (int j = 0; j < size; j++)
                        normal += Vector3.Cross(positions[pointIndices[j]], positions[pointIndices[(j + 1) % size]]);
                    if (normal.sqrMagnitude < 1e-20f) continue;
                    int faceIndex = faces.Count;
                    faces.Add(new Face { points = pointIndices, normal = normal.normalized });
                    for (int j = 0; j < size; j++)
                    {
                        int a = pointIndices[j], b = pointIndices[(j + 1) % size];
                        if (a == b) continue;
                        ulong edgeKey = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                        if (!edges.TryGetValue(edgeKey, out var edge))
                        {
                            edge = new Edge { a = a, b = b };
                            edges.Add(edgeKey, edge);
                        }
                        edge.faces.Add(faceIndex);
                    }
                }
            }

            var direction = new Vector3[positions.Count];
            var neighbors = new List<int>[positions.Count];
            for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new List<int>();
            foreach (Edge edge in edges.Values)
            {
                neighbors[edge.a].Add(edge.b);
                neighbors[edge.b].Add(edge.a);
                Vector3 n;
                if (edge.faces.Count == 1) n = faces[edge.faces[0]].normal;
                else if (edge.faces.Count == 2)
                {
                    Vector3 a = faces[edge.faces[0]].normal, b = faces[edge.faces[1]].normal;
                    n = a + b;
                    n = n.sqrMagnitude == 0 ? a : n.normalized * Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1, 1));
                }
                else continue;
                direction[edge.a] += n;
                direction[edge.b] += n;
            }
            for (int i = 0; i < direction.Length; i++)
            {
                if (direction[i].sqrMagnitude == 0) direction[i] = fallbackNormals[i];
                if (direction[i].sqrMagnitude == 0) direction[i] = Vector3.forward;
                direction[i].Normalize();
            }

            var weighted = new float[positions.Count];
            var angles = new float[positions.Count];
            foreach (Face face in faces)
            for (int j = 0; j < face.points.Length; j++)
            {
                int v = face.points[j];
                int previous = face.points[(j + face.points.Length - 1) % face.points.Length];
                int next = face.points[(j + 1) % face.points.Length];
                Vector3 a = positions[previous] - positions[v], b = positions[next] - positions[v];
                float angle = a.sqrMagnitude == 0 || b.sqrMagnitude == 0 ? 0 :
                    Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1, 1));
                float cosine = Mathf.Abs(Vector3.Dot(direction[v], face.normal));
                float shell = cosine <= 1e-8f ? 1 : 1 / cosine;
                weighted[v] += shell * angle;
                angles[v] += angle;
            }

            var alpha = new float[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 laplacian = Vector3.zero;
                foreach (int n in neighbors[i]) laplacian += positions[n] - positions[i];
                float sign = Vector3.Dot(laplacian, direction[i]) > 0 ? -1 : 1;
                float shell = Mathf.Max(1, angles[i] > 1e-8f ? weighted[i] / angles[i] : 1);
                alpha[i] = Mathf.Clamp(0.5f + sign * (1 - 1 / shell) * 0.5f, 1e-4f, 1);
            }

            var encoded = new Vector4[vertices.Length];
            for (int i = 0; i < encoded.Length; i++)
            {
                Vector3 n = normals[i].normalized, t = ((Vector3)tangents[i]).normalized;
                Vector3 b = Vector3.Cross(n, t).normalized * tangents[i].w;
                Vector3 d = direction[map[i]];
                encoded[i] = new Vector4(Vector3.Dot(d, t) * 0.5f + 0.5f,
                    Vector3.Dot(d, b) * 0.5f + 0.5f, Vector3.Dot(d, n) * 0.5f + 0.5f, alpha[map[i]]);
            }
            return encoded;
        }
    }
}
#endif
