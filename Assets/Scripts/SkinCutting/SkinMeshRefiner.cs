using System.Collections.Generic;
using UnityEngine;

namespace SkinCutting
{
    [RequireComponent(typeof(SkinMeshData))]
    public sealed class SkinMeshRefiner : MonoBehaviour
    {
        public bool RefinementEnabled = true;
        public bool EnableEdgeFlips = true;
        public int MaxLevels = 3;
        public float TargetEdgeLength = 0.03f;
        public int MaxTriangles = 18000;
        public float MinimumTriangleArea = 1e-9f;
        public bool DeferTopology;

        private SkinMeshData data;

        public int RefinementPasses { get; private set; }
        public int AddedVertices { get; private set; }
        public int EdgeFlips { get; private set; }

        private SkinMeshData Data
        {
            get
            {
                if (data == null)
                {
                    data = GetComponent<SkinMeshData>();
                }

                return data;
            }
        }

        public void ResetStats()
        {
            RefinementPasses = 0;
            AddedVertices = 0;
            EdgeFlips = 0;
        }

        public int RefineNearSegment(Vector3 start, Vector3 end, float radius, int levels)
        {
            return RefineNearSegment(start, end, radius, levels, true);
        }

        public int RefineNearSegment(
            Vector3 start,
            Vector3 end,
            float radius,
            int levels,
            bool flipEdges)
        {
            if (!RefinementEnabled)
            {
                return 0;
            }

            SkinMeshData meshData = Data;
            if (meshData == null || meshData.TriangleCount == 0)
            {
                return 0;
            }

            int passCount = Mathf.Clamp(levels, 1, Mathf.Max(1, MaxLevels));
            float targetLength = Mathf.Max(1e-4f, TargetEdgeLength);
            float refineRadius = Mathf.Max(1e-4f, radius);
            int addedBefore = meshData.VertexCount;
            bool topologyChanged = false;

            for (int level = 0; level < passCount; level++)
            {
                HashSet<long> edgesToSplit = new HashSet<long>();
                int triangleCount = meshData.TriangleCount;

                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    int baseIndex = triangle * 3;
                    int i0 = meshData.Triangles[baseIndex];
                    int i1 = meshData.Triangles[baseIndex + 1];
                    int i2 = meshData.Triangles[baseIndex + 2];

                    Vector3 centroid = (meshData.Positions[i0] +
                                        meshData.Positions[i1] +
                                        meshData.Positions[i2]) / 3f;
                    if (PointSegmentSqrDistance(centroid, start, end) >
                        refineRadius * refineRadius)
                    {
                        continue;
                    }

                    float length0 = Vector3.Distance(meshData.Positions[i0], meshData.Positions[i1]);
                    float length1 = Vector3.Distance(meshData.Positions[i1], meshData.Positions[i2]);
                    float length2 = Vector3.Distance(meshData.Positions[i2], meshData.Positions[i0]);

                    int longestEdge = 0;
                    float longestLength = length0;
                    if (length1 > longestLength)
                    {
                        longestLength = length1;
                        longestEdge = 1;
                    }
                    if (length2 > longestLength)
                    {
                        longestLength = length2;
                        longestEdge = 2;
                    }

                    if (longestLength <= targetLength)
                    {
                        continue;
                    }

                    if (longestEdge == 0)
                    {
                        edgesToSplit.Add(SkinMeshData.EdgeKey(i0, i1));
                    }
                    else if (longestEdge == 1)
                    {
                        edgesToSplit.Add(SkinMeshData.EdgeKey(i1, i2));
                    }
                    else
                    {
                        edgesToSplit.Add(SkinMeshData.EdgeKey(i2, i0));
                    }
                }

                if (edgesToSplit.Count == 0)
                {
                    break;
                }

                List<int> newTriangles = new List<int>(meshData.Triangles.Count + edgesToSplit.Count * 6);
                Dictionary<long, int> midpointCache = new Dictionary<long, int>();
                int beforePassVertices = meshData.VertexCount;

                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    int baseIndex = triangle * 3;
                    int i0 = meshData.Triangles[baseIndex];
                    int i1 = meshData.Triangles[baseIndex + 1];
                    int i2 = meshData.Triangles[baseIndex + 2];

                    bool split0 = edgesToSplit.Contains(SkinMeshData.EdgeKey(i0, i1));
                    bool split1 = edgesToSplit.Contains(SkinMeshData.EdgeKey(i1, i2));
                    bool split2 = edgesToSplit.Contains(SkinMeshData.EdgeKey(i2, i0));
                    int splitCount = (split0 ? 1 : 0) + (split1 ? 1 : 0) + (split2 ? 1 : 0);

                    if (splitCount == 0)
                    {
                        AddTriangle(meshData, newTriangles, i0, i1, i2);
                        continue;
                    }

                    int m0 = split0 ? GetMidpoint(meshData, midpointCache, i0, i1) : -1;
                    int m1 = split1 ? GetMidpoint(meshData, midpointCache, i1, i2) : -1;
                    int m2 = split2 ? GetMidpoint(meshData, midpointCache, i2, i0) : -1;

                    if (splitCount == 1)
                    {
                        if (split0)
                        {
                            AddTriangle(meshData, newTriangles, i0, m0, i2);
                            AddTriangle(meshData, newTriangles, m0, i1, i2);
                        }
                        else if (split1)
                        {
                            AddTriangle(meshData, newTriangles, i1, m1, i0);
                            AddTriangle(meshData, newTriangles, m1, i2, i0);
                        }
                        else
                        {
                            AddTriangle(meshData, newTriangles, i2, m2, i1);
                            AddTriangle(meshData, newTriangles, m2, i0, i1);
                        }
                    }
                    else if (splitCount == 2)
                    {
                        if (split0 && split1)
                        {
                            AddTriangle(meshData, newTriangles, i0, m0, m1);
                            AddTriangle(meshData, newTriangles, i0, m1, i2);
                            AddTriangle(meshData, newTriangles, m0, i1, m1);
                        }
                        else if (split1 && split2)
                        {
                            AddTriangle(meshData, newTriangles, i1, m1, m2);
                            AddTriangle(meshData, newTriangles, i1, m2, i0);
                            AddTriangle(meshData, newTriangles, m1, i2, m2);
                        }
                        else
                        {
                            AddTriangle(meshData, newTriangles, i2, m2, m0);
                            AddTriangle(meshData, newTriangles, i2, m0, i1);
                            AddTriangle(meshData, newTriangles, m2, i0, m0);
                        }
                    }
                    else
                    {
                        AddTriangle(meshData, newTriangles, i0, m0, m2);
                        AddTriangle(meshData, newTriangles, m0, i1, m1);
                        AddTriangle(meshData, newTriangles, m2, m1, i2);
                        AddTriangle(meshData, newTriangles, m0, m1, m2);
                    }
                }

                meshData.ReplaceTrianglesDeferred(newTriangles);
                topologyChanged = true;
                RefinementPasses++;
                AddedVertices += meshData.VertexCount - beforePassVertices;

                if (meshData.VertexCount == beforePassVertices)
                {
                    break;
                }
                if (meshData.TriangleCount >= MaxTriangles)
                {
                    break;
                }
            }

            if (EnableEdgeFlips && flipEdges)
            {
                int flips = FlipInteriorEdges(start, end, refineRadius);
                EdgeFlips += flips;
                if (flips > 0)
                {
                    topologyChanged = true;
                }
            }

            if (topologyChanged && !DeferTopology)
            {
                meshData.FinalizeTopology();
            }

            return meshData.VertexCount - addedBefore;
        }

        private int FlipInteriorEdges(Vector3 start, Vector3 end, float radius)
        {
            SkinMeshData meshData = Data;
            if (meshData == null || meshData.TriangleCount == 0)
            {
                return 0;
            }

            Dictionary<long, EdgePair> adjacency = new Dictionary<long, EdgePair>();
            HashSet<long> existingEdges = new HashSet<long>();
            int triangleCount = meshData.TriangleCount;

            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int baseIndex = triangle * 3;
                int i0 = meshData.Triangles[baseIndex];
                int i1 = meshData.Triangles[baseIndex + 1];
                int i2 = meshData.Triangles[baseIndex + 2];
                if (i0 == i1 || i1 == i2 || i2 == i0)
                {
                    continue;
                }

                AddEdgeUse(adjacency, existingEdges, i0, i1, i2, triangle);
                AddEdgeUse(adjacency, existingEdges, i1, i2, i0, triangle);
                AddEdgeUse(adjacency, existingEdges, i2, i0, i1, triangle);
            }

            float radiusSquared = radius * radius;
            HashSet<int> lockedVertices = new HashSet<int>();
            List<EdgeFlip> flips = new List<EdgeFlip>();

            foreach (KeyValuePair<long, EdgePair> entry in adjacency)
            {
                EdgePair pair = entry.Value;
                if (!pair.HasSecond)
                {
                    continue;
                }

                EdgeUse first = pair.First;
                EdgeUse second = pair.Second;
                if (first.A != second.B || first.B != second.A)
                {
                    continue;
                }

                int a = first.A;
                int b = first.B;
                int c = first.Opposite;
                int d = second.Opposite;
                if (a == c || a == d || b == c || b == d || c == d)
                {
                    continue;
                }

                if (lockedVertices.Contains(a) || lockedVertices.Contains(b) ||
                    lockedVertices.Contains(c) || lockedVertices.Contains(d))
                {
                    continue;
                }

                if (existingEdges.Contains(SkinMeshData.EdgeKey(c, d)))
                {
                    continue;
                }

                Vector3 pa = meshData.Positions[a];
                Vector3 pb = meshData.Positions[b];
                Vector3 pc = meshData.Positions[c];
                Vector3 pd = meshData.Positions[d];
                Vector3 midpoint = (pa + pb) * 0.5f;
                if (PointSegmentSqrDistance(midpoint, start, end) > radiusSquared)
                {
                    continue;
                }

                Vector3 normalOld0 = Vector3.Cross(pb - pa, pc - pa);
                Vector3 normalOld1 = Vector3.Cross(pa - pb, pd - pb);
                if (normalOld0.sqrMagnitude <= MinimumTriangleArea ||
                    normalOld1.sqrMagnitude <= MinimumTriangleArea ||
                    Vector3.Dot(normalOld0, normalOld1) <= 0f)
                {
                    continue;
                }

                Vector3 normalNew0 = Vector3.Cross(pa - pc, pd - pc);
                Vector3 normalNew1 = Vector3.Cross(pc - pb, pd - pb);
                if (normalNew0.sqrMagnitude <= MinimumTriangleArea ||
                    normalNew1.sqrMagnitude <= MinimumTriangleArea ||
                    Vector3.Dot(normalOld0, normalNew0) <= 0f ||
                    Vector3.Dot(normalOld0, normalNew1) <= 0f)
                {
                    continue;
                }

                float areaOld = normalOld0.magnitude + normalOld1.magnitude;
                float areaNew = normalNew0.magnitude + normalNew1.magnitude;
                if (Mathf.Abs(areaNew - areaOld) > areaOld * 0.02f + 1e-6f)
                {
                    continue;
                }

                float qualityOld = Mathf.Min(
                    TriangleQuality(pa, pb, pc),
                    TriangleQuality(pb, pa, pd));
                float qualityNew = Mathf.Min(
                    TriangleQuality(pc, pa, pd),
                    TriangleQuality(pb, pc, pd));
                if (qualityNew <= qualityOld * 1.01f + 1e-6f)
                {
                    continue;
                }

                flips.Add(new EdgeFlip
                {
                    TriangleA = first.Triangle,
                    TriangleB = second.Triangle,
                    A = a,
                    B = b,
                    C = c,
                    D = d
                });
                lockedVertices.Add(a);
                lockedVertices.Add(b);
                lockedVertices.Add(c);
                lockedVertices.Add(d);
            }

            if (flips.Count == 0)
            {
                return 0;
            }

            List<int> triangles = new List<int>(meshData.Triangles);
            for (int i = 0; i < flips.Count; i++)
            {
                EdgeFlip flip = flips[i];
                SetTriangle(triangles, flip.TriangleA, flip.C, flip.A, flip.D);
                SetTriangle(triangles, flip.TriangleB, flip.B, flip.C, flip.D);
            }

            meshData.ReplaceTrianglesDeferred(triangles);
            return flips.Count;
        }

        private static void AddEdgeUse(
            Dictionary<long, EdgePair> adjacency,
            HashSet<long> existingEdges,
            int a,
            int b,
            int opposite,
            int triangle)
        {
            long key = SkinMeshData.EdgeKey(a, b);
            existingEdges.Add(key);
            EdgeUse use = new EdgeUse
            {
                A = a,
                B = b,
                Opposite = opposite,
                Triangle = triangle
            };

            EdgePair pair;
            if (!adjacency.TryGetValue(key, out pair))
            {
                pair = new EdgePair { First = use };
                adjacency.Add(key, pair);
            }
            else if (!pair.HasSecond)
            {
                pair.Second = use;
                pair.HasSecond = true;
            }
        }

        private static void SetTriangle(List<int> triangles, int triangle, int a, int b, int c)
        {
            int baseIndex = triangle * 3;
            triangles[baseIndex] = a;
            triangles[baseIndex + 1] = b;
            triangles[baseIndex + 2] = c;
        }

        private static float TriangleQuality(Vector3 a, Vector3 b, Vector3 c)
        {
            float edgeSum = (b - a).sqrMagnitude +
                            (c - b).sqrMagnitude +
                            (a - c).sqrMagnitude;
            if (edgeSum < 1e-12f)
            {
                return 0f;
            }

            return Vector3.Cross(b - a, c - a).magnitude / edgeSum;
        }

        private struct EdgeUse
        {
            public int A;
            public int B;
            public int Opposite;
            public int Triangle;
        }

        private sealed class EdgePair
        {
            public EdgeUse First;
            public EdgeUse Second;
            public bool HasSecond;
        }

        private struct EdgeFlip
        {
            public int TriangleA;
            public int TriangleB;
            public int A;
            public int B;
            public int C;
            public int D;
        }

        private static int GetMidpoint(
            SkinMeshData meshData,
            Dictionary<long, int> midpointCache,
            int a,
            int b)
        {
            long key = SkinMeshData.EdgeKey(a, b);
            int midpoint;
            if (!midpointCache.TryGetValue(key, out midpoint))
            {
                midpoint = meshData.AddMidpoint(a, b);
                midpointCache[key] = midpoint;
            }

            return midpoint;
        }

        private void AddTriangle(
            SkinMeshData meshData,
            List<int> triangles,
            int a,
            int b,
            int c)
        {
            if (a == b || b == c || a == c)
            {
                return;
            }

            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            Vector3 pc = meshData.Positions[c];
            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            if (cross.sqrMagnitude <= MinimumTriangleArea)
            {
                return;
            }

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static float PointSegmentSqrDistance(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 1e-10f)
            {
                return (point - start).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            Vector3 closest = start + segment * t;
            return (point - closest).sqrMagnitude;
        }
    }
}
