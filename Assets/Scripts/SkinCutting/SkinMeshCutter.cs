using System.Collections.Generic;
using UnityEngine;

namespace SkinCutting
{
    [RequireComponent(typeof(SkinMeshData))]
    public sealed class SkinMeshCutter : MonoBehaviour
    {
        public float CutGap = 0.0035f;
        public float CutDepth = 0.0025f;
        public float BladeRadius = 0.09f;
        public float MinSegmentLength = 0.004f;
        public float MinimumTriangleArea = 1e-9f;
        public float IntersectionAlpha = 0.0012f;
        public float MinimumBoundaryAngle = 8f;
        public bool DeferTopology;

        private SkinMeshData data;
        private readonly List<int> scratchTriangles = new List<int>();
        private readonly Dictionary<long, SplitVertices> scratchSplitCache =
            new Dictionary<long, SplitVertices>();
        private readonly Dictionary<int, SplitVertices> scratchVertexSplitCache =
            new Dictionary<int, SplitVertices>();
        private readonly List<int> positivePolygon = new List<int>(8);
        private readonly List<int> negativePolygon = new List<int>(8);
        private readonly List<int> positiveTriangles = new List<int>(12);
        private readonly List<int> negativeTriangles = new List<int>(12);
        private readonly List<bool> scratchWallFlags = new List<bool>();
        private readonly List<int> scratchTouchedTriangles = new List<int>();
        private readonly List<IntersectionSample> scratchIntersections = new List<IntersectionSample>();
        private readonly Dictionary<long, List<int>> scratchIntersectionBuckets =
            new Dictionary<long, List<int>>();
        private readonly Dictionary<long, int> scratchEdgeCounts = new Dictionary<long, int>();
        private readonly Dictionary<int, int> scratchBottomVertices = new Dictionary<int, int>();

        public int CutOperations { get; private set; }
        public float TotalCutLength { get; private set; }

        private sealed class SplitVertices
        {
            public int Positive;
            public int Negative;
        }

        private struct IntersectionSample
        {
            public int Index;
            public int Sign;
            public Vector3 Position;
        }

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
            CutOperations = 0;
            TotalCutLength = 0f;
        }

        public bool CutSegment(
            Vector3 start,
            Vector3 end,
            Vector3 normalStart,
            Vector3 normalEnd,
            out int clippedTriangles)
        {
            clippedTriangles = 0;
            SkinMeshData meshData = Data;
            if (meshData == null || meshData.TriangleCount == 0)
            {
                return false;
            }

            Vector3 segment = end - start;
            float length = segment.magnitude;
            if (length < Mathf.Max(1e-5f, MinSegmentLength))
            {
                return false;
            }

            Vector3 direction = segment / length;
            Vector3 averageNormal = normalStart + normalEnd;
            if (averageNormal.sqrMagnitude < 1e-8f)
            {
                averageNormal = Vector3.forward;
            }
            averageNormal.Normalize();

            Vector3 planeNormal = Vector3.Cross(averageNormal, direction);
            if (planeNormal.sqrMagnitude < 1e-8f)
            {
                planeNormal = Vector3.Cross(Vector3.forward, direction);
            }
            if (planeNormal.sqrMagnitude < 1e-8f)
            {
                planeNormal = Vector3.Cross(Vector3.up, direction);
            }
            planeNormal.Normalize();

            Vector3 planePoint = (start + end) * 0.5f;
            float radius = Mathf.Max(1e-4f, BladeRadius);
            float gap = Mathf.Max(0f, CutGap);
            float planeEpsilon = Mathf.Max(1e-6f, gap * 0.1f);

            List<int> newTriangles;
            List<bool> newWallFlags;
            List<int> touchedTriangles = scratchTouchedTriangles;
            int clipped;
            bool cut = ClipWithPlane(
                meshData, planePoint, planeNormal, start, end, radius, gap, planeEpsilon,
                out newTriangles, out newWallFlags, out touchedTriangles, out clipped);

            if (!cut)
            {
                float shift = Mathf.Max(planeEpsilon * 1.5f, radius * 0.12f);
                cut = ClipWithPlane(
                    meshData, planePoint + planeNormal * shift, planeNormal, start, end,
                    radius, gap, planeEpsilon,
                    out newTriangles, out newWallFlags, out touchedTriangles, out clipped);
            }

            if (!cut)
            {
                float shift = Mathf.Max(planeEpsilon * 1.5f, radius * 0.12f);
                cut = ClipWithPlane(
                    meshData, planePoint - planeNormal * shift, planeNormal, start, end,
                    radius, gap, planeEpsilon,
                    out newTriangles, out newWallFlags, out touchedTriangles, out clipped);
            }

            if (!cut)
            {
                return false;
            }

            if (DeferTopology)
            {
                meshData.ReplaceTrianglesDeferred(
                    newTriangles, newWallFlags, touchedTriangles);
            }
            else
            {
                meshData.ReplaceTriangles(newTriangles, newWallFlags, touchedTriangles);
            }

            CutOperations++;
            TotalCutLength += length;
            clippedTriangles = clipped;
            return true;
        }

        private bool ClipWithPlane(
            SkinMeshData meshData,
            Vector3 planePoint,
            Vector3 planeNormal,
            Vector3 start,
            Vector3 end,
            float radius,
            float gap,
            float planeEpsilon,
            out List<int> newTriangles,
            out List<bool> newWallFlags,
            out List<int> touchedTriangles,
            out int clipped)
        {
            float seamEpsilon = Mathf.Max(planeEpsilon, gap);
            float alpha = Mathf.Max(1e-4f, IntersectionAlpha);
            float minAngleCos = Mathf.Cos(
                Mathf.Clamp(MinimumBoundaryAngle, 1f, 45f) * Mathf.Deg2Rad);
            int oldTriangleCount = meshData.Triangles.Count;
            bool[] oldWallFlags = new bool[oldTriangleCount / 3];
            for (int i = 0; i < oldWallFlags.Length; i++)
            {
                oldWallFlags[i] = meshData.IsWallTriangle(i);
            }

            newTriangles = scratchTriangles;
            newTriangles.Clear();
            if (newTriangles.Capacity < oldTriangleCount + 64)
            {
                newTriangles.Capacity = oldTriangleCount + 64;
            }

            newWallFlags = scratchWallFlags;
            newWallFlags.Clear();
            if (newWallFlags.Capacity < oldWallFlags.Length + 32)
            {
                newWallFlags.Capacity = oldWallFlags.Length + 32;
            }

            touchedTriangles = scratchTouchedTriangles;
            touchedTriangles.Clear();

            Dictionary<long, SplitVertices> splitCache = scratchSplitCache;
            splitCache.Clear();
            scratchVertexSplitCache.Clear();
            scratchIntersections.Clear();
            scratchIntersectionBuckets.Clear();
            clipped = 0;

            Vector3 segmentMin = Vector3.Min(start, end) - new Vector3(radius, radius, radius);
            Vector3 segmentMax = Vector3.Max(start, end) + new Vector3(radius, radius, radius);

            for (int triangle = 0; triangle + 2 < oldTriangleCount; triangle += 3)
            {
                int i0 = meshData.Triangles[triangle];
                int i1 = meshData.Triangles[triangle + 1];
                int i2 = meshData.Triangles[triangle + 2];
                bool wasWall = oldWallFlags[triangle / 3];

                Vector3 p0 = meshData.Positions[i0];
                Vector3 p1 = meshData.Positions[i1];
                Vector3 p2 = meshData.Positions[i2];

                if (TriangleOutsideBounds(p0, p1, p2, segmentMin, segmentMax))
                {
                    newTriangles.Add(i0);
                    newTriangles.Add(i1);
                    newTriangles.Add(i2);
                    newWallFlags.Add(wasWall);
                    continue;
                }

                bool hasPositive = false;
                bool hasNegative = false;
                bool hasOnPlane = false;
                float d0 = Vector3.Dot(planeNormal, p0 - planePoint);
                float d1 = Vector3.Dot(planeNormal, p1 - planePoint);
                float d2 = Vector3.Dot(planeNormal, p2 - planePoint);

                if (d0 > planeEpsilon || d1 > planeEpsilon || d2 > planeEpsilon)
                {
                    hasPositive = true;
                }
                if (d0 < -planeEpsilon || d1 < -planeEpsilon || d2 < -planeEpsilon)
                {
                    hasNegative = true;
                }
                if (Mathf.Abs(d0) <= seamEpsilon ||
                    Mathf.Abs(d1) <= seamEpsilon ||
                    Mathf.Abs(d2) <= seamEpsilon)
                {
                    hasOnPlane = true;
                }

                if (!TriangleNearSegment(p0, p1, p2, start, end, radius))
                {
                    newTriangles.Add(i0);
                    newTriangles.Add(i1);
                    newTriangles.Add(i2);
                    newWallFlags.Add(wasWall);
                    continue;
                }

                if (!hasPositive || !hasNegative)
                {
                    if (hasOnPlane && (hasPositive || hasNegative))
                    {
                        int before = newTriangles.Count;
                        CopyTriangleToSide(
                            meshData, i0, i1, i2, d0, d1, d2,
                            hasPositive, newTriangles, scratchVertexSplitCache,
                            planeNormal, gap, seamEpsilon, alpha, minAngleCos);
                        for (int entry = before; entry < newTriangles.Count; entry += 3)
                        {
                            newWallFlags.Add(wasWall);
                        }
                        if (newTriangles.Count > before)
                        {
                            touchedTriangles.Add(triangle / 3);
                        }
                        continue;
                    }

                    if (!hasPositive && !hasNegative)
                    {
                        touchedTriangles.Add(triangle / 3);
                        continue;
                    }

                    newTriangles.Add(i0);
                    newTriangles.Add(i1);
                    newTriangles.Add(i2);
                    newWallFlags.Add(wasWall);
                    touchedTriangles.Add(triangle / 3);
                    continue;
                }

                positivePolygon.Clear();
                negativePolygon.Clear();
                ClipTriangleToSide(
                    meshData, i0, i1, i2, d0, d1, d2, true, positivePolygon,
                    splitCache, scratchVertexSplitCache, planePoint, planeNormal,
                    gap, seamEpsilon, alpha);
                ClipTriangleToSide(
                    meshData, i0, i1, i2, d0, d1, d2, false, negativePolygon,
                    splitCache, scratchVertexSplitCache, planePoint, planeNormal,
                    gap, seamEpsilon, alpha);

                positiveTriangles.Clear();
                negativeTriangles.Clear();
                AddPolygonTriangles(
                    meshData, positivePolygon, positiveTriangles, alpha, minAngleCos);
                AddPolygonTriangles(
                    meshData, negativePolygon, negativeTriangles, alpha, minAngleCos);

                if (positiveTriangles.Count == 0 && negativeTriangles.Count == 0)
                {
                    touchedTriangles.Add(triangle / 3);
                    continue;
                }

                for (int i = 0; i < positiveTriangles.Count; i += 3)
                {
                    newTriangles.Add(positiveTriangles[i]);
                    newTriangles.Add(positiveTriangles[i + 1]);
                    newTriangles.Add(positiveTriangles[i + 2]);
                    newWallFlags.Add(wasWall);
                }
                for (int i = 0; i < negativeTriangles.Count; i += 3)
                {
                    newTriangles.Add(negativeTriangles[i]);
                    newTriangles.Add(negativeTriangles[i + 1]);
                    newTriangles.Add(negativeTriangles[i + 2]);
                    newWallFlags.Add(wasWall);
                }
                clipped++;
                touchedTriangles.Add(triangle / 3);
            }

            RemoveCutSliverTriangles(
                meshData, newTriangles, newWallFlags, planePoint, planeNormal,
                gap, alpha, minAngleCos);
            if (clipped > 0 && CutDepth > 0f)
            {
                AddCutBoundaryWalls(
                    meshData, newTriangles, newWallFlags, start, end, planePoint,
                    planeNormal, radius, gap, seamEpsilon, alpha, minAngleCos);
            }
            return clipped > 0;
        }

        private void AddCutBoundaryWalls(
            SkinMeshData meshData,
            List<int> triangles,
            List<bool> wallFlags,
            Vector3 start,
            Vector3 end,
            Vector3 planePoint,
            Vector3 planeNormal,
            float radius,
            float gap,
            float seamEpsilon,
            float alpha,
            float minAngleCos)
        {
            int triangleCount = triangles.Count;
            if (triangleCount == 0)
            {
                return;
            }

            float depth = Mathf.Clamp(CutDepth, 0.001f, 0.004f);
            float wallTolerance = Mathf.Max(seamEpsilon * 2f, gap * 2f);
            float segmentRadiusSquared = radius * radius * 2.25f;
            Dictionary<long, int> edgeCounts = scratchEdgeCounts;
            edgeCounts.Clear();
            for (int triangle = 0; triangle + 2 < triangleCount; triangle += 3)
            {
                int a = triangles[triangle];
                int b = triangles[triangle + 1];
                int c = triangles[triangle + 2];
                IncrementEdgeCount(edgeCounts, a, b);
                IncrementEdgeCount(edgeCounts, b, c);
                IncrementEdgeCount(edgeCounts, c, a);
            }

            Dictionary<int, int> bottomVertices = scratchBottomVertices;
            bottomVertices.Clear();
            for (int triangle = 0; triangle + 2 < triangleCount; triangle += 3)
            {
                int a = triangles[triangle];
                int b = triangles[triangle + 1];
                int c = triangles[triangle + 2];
                AddBoundaryWall(
                    meshData, triangles, wallFlags, edgeCounts, bottomVertices,
                    a, b, start, end, planePoint, planeNormal,
                    wallTolerance, segmentRadiusSquared, depth, alpha, minAngleCos);
                AddBoundaryWall(
                    meshData, triangles, wallFlags, edgeCounts, bottomVertices,
                    b, c, start, end, planePoint, planeNormal,
                    wallTolerance, segmentRadiusSquared, depth, alpha, minAngleCos);
                AddBoundaryWall(
                    meshData, triangles, wallFlags, edgeCounts, bottomVertices,
                    c, a, start, end, planePoint, planeNormal,
                    wallTolerance, segmentRadiusSquared, depth, alpha, minAngleCos);
            }
        }

        private void AddBoundaryWall(
            SkinMeshData meshData,
            List<int> triangles,
            List<bool> wallFlags,
            Dictionary<long, int> edgeCounts,
            Dictionary<int, int> bottomVertices,
            int a,
            int b,
            Vector3 start,
            Vector3 end,
            Vector3 planePoint,
            Vector3 planeNormal,
            float wallTolerance,
            float segmentRadiusSquared,
            float depth,
            float alpha,
            float minAngleCos)
        {
            int count;
            if (!edgeCounts.TryGetValue(EdgeKey(a, b), out count) || count != 1)
            {
                return;
            }

            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            float da = Vector3.Dot(planeNormal, pa - planePoint);
            float db = Vector3.Dot(planeNormal, pb - planePoint);
            if (Mathf.Abs(da) > wallTolerance || Mathf.Abs(db) > wallTolerance)
            {
                return;
            }
            if (da * db < 0f)
            {
                return;
            }

            Vector3 midpoint = (pa + pb) * 0.5f;
            if (DistancePointSegmentSquared(midpoint, start, end) > segmentRadiusSquared)
            {
                return;
            }

            float side = da + db;
            if (Mathf.Abs(side) < 1e-9f)
            {
                return;
            }

            Vector3 desiredNormal = side > 0f ? -planeNormal : planeNormal;
            Vector3 inset = Vector3.forward * -depth;
            Vector3 bottomA = pa + inset;
            Vector3 bottomB = pb + inset;
            int bottomIndexA = GetBottomVertex(meshData, bottomVertices, a, bottomA);
            int bottomIndexB = GetBottomVertex(meshData, bottomVertices, b, bottomB);

            Vector3 wallNormal = Vector3.Cross(pb - pa, bottomB - pa);
            if (Vector3.Dot(wallNormal, desiredNormal) < 0f)
            {
                int swap = a;
                a = b;
                b = swap;
                swap = bottomIndexA;
                bottomIndexA = bottomIndexB;
                bottomIndexB = swap;
            }

            AddWallTriangle(
                meshData, triangles, wallFlags, a, b, bottomIndexB, alpha, minAngleCos);
            AddWallTriangle(
                meshData, triangles, wallFlags, a, bottomIndexB, bottomIndexA, alpha, minAngleCos);
        }

        private static int GetBottomVertex(
            SkinMeshData meshData,
            Dictionary<int, int> bottomVertices,
            int topIndex,
            Vector3 position)
        {
            int bottomIndex;
            if (bottomVertices.TryGetValue(topIndex, out bottomIndex))
            {
                return bottomIndex;
            }

            bottomIndex = meshData.AddVertex(
                position, Vector3.zero, meshData.Uvs[topIndex],
                meshData.IsPermanentlyPinned(topIndex));
            meshData.RegisterWallFollower(
                topIndex, bottomIndex, position - meshData.Positions[topIndex]);
            bottomVertices[topIndex] = bottomIndex;
            return bottomIndex;
        }

        private void AddWallTriangle(
            SkinMeshData meshData,
            List<int> triangles,
            List<bool> wallFlags,
            int a,
            int b,
            int c,
            float alpha,
            float minAngleCos)
        {
            if (TryAddTriangle(meshData, a, b, c, triangles, alpha, minAngleCos, false))
            {
                wallFlags.Add(true);
            }
        }

        private static void IncrementEdgeCount(Dictionary<long, int> edgeCounts, int a, int b)
        {
            long key = EdgeKey(a, b);
            int count;
            edgeCounts.TryGetValue(key, out count);
            edgeCounts[key] = count + 1;
        }

        private static long EdgeKey(int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private static float DistancePointSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 1e-10f)
            {
                return (point - start).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * t)).sqrMagnitude;
        }

        private static void RemoveCutSliverTriangles(
            SkinMeshData meshData,
            List<int> triangles,
            List<bool> wallFlags,
            Vector3 planePoint,
            Vector3 planeNormal,
            float gap,
            float alpha,
            float minAngleCos)
        {
            float maxLength = Mathf.Max(1e-4f, gap * 2f);
            float maxLengthSquared = maxLength * maxLength;
            float planeTolerance = Mathf.Max(1e-4f, gap * 3f);
            int write = 0;
            int writeFlags = 0;
            for (int read = 0; read + 2 < triangles.Count; read += 3)
            {
                int a = triangles[read];
                int b = triangles[read + 1];
                int c = triangles[read + 2];
                int flagIndex = read / 3;
                bool isWall = flagIndex < wallFlags.Count && wallFlags[flagIndex];
                if (HasShortSeamEdge(
                        meshData, a, b, planePoint, planeNormal,
                        maxLengthSquared, planeTolerance) ||
                    HasShortSeamEdge(
                        meshData, b, c, planePoint, planeNormal,
                        maxLengthSquared, planeTolerance) ||
                    HasShortSeamEdge(
                        meshData, c, a, planePoint, planeNormal,
                        maxLengthSquared, planeTolerance) ||
                    !HasMinimumEdgeLength(meshData, a, b, c, alpha) ||
                    (!isWall && !HasValidAngles(meshData, a, b, c, minAngleCos)))
                {
                    continue;
                }

                triangles[write] = a;
                triangles[write + 1] = b;
                triangles[write + 2] = c;
                if (writeFlags < wallFlags.Count)
                {
                    wallFlags[writeFlags] = isWall;
                }
                else
                {
                    wallFlags.Add(isWall);
                }
                writeFlags++;
                write += 3;
            }

            if (write < triangles.Count)
            {
                triangles.RemoveRange(write, triangles.Count - write);
            }
            if (writeFlags < wallFlags.Count)
            {
                wallFlags.RemoveRange(writeFlags, wallFlags.Count - writeFlags);
            }
        }

        private static bool HasShortSeamEdge(
            SkinMeshData meshData,
            int a,
            int b,
            Vector3 planePoint,
            Vector3 planeNormal,
            float maxLengthSquared,
            float planeTolerance)
        {
            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            if ((pa - pb).sqrMagnitude > maxLengthSquared)
            {
                return false;
            }

            Vector3 midpoint = (pa + pb) * 0.5f;
            return Mathf.Abs(Vector3.Dot(planeNormal, midpoint - planePoint)) <= planeTolerance;
        }

        private void CopyTriangleToSide(
            SkinMeshData meshData,
            int i0,
            int i1,
            int i2,
            float d0,
            float d1,
            float d2,
            bool positive,
            List<int> output,
            Dictionary<int, SplitVertices> vertexSplitCache,
            Vector3 planeNormal,
            float gap,
            float planeEpsilon,
            float alpha,
            float minAngleCos)
        {
            int a = Mathf.Abs(d0) <= planeEpsilon
                ? GetOnPlaneSplitVertex(
                    meshData, i0, positive ? 1 : -1, vertexSplitCache, planeNormal, gap, alpha)
                : i0;
            int b = Mathf.Abs(d1) <= planeEpsilon
                ? GetOnPlaneSplitVertex(
                    meshData, i1, positive ? 1 : -1, vertexSplitCache, planeNormal, gap, alpha)
                : i1;
            int c = Mathf.Abs(d2) <= planeEpsilon
                ? GetOnPlaneSplitVertex(
                    meshData, i2, positive ? 1 : -1, vertexSplitCache, planeNormal, gap, alpha)
                : i2;
            TryAddTriangle(meshData, a, b, c, output, alpha, minAngleCos, true);
        }

        private void ClipTriangleToSide(
            SkinMeshData meshData,
            int i0,
            int i1,
            int i2,
            float d0,
            float d1,
            float d2,
            bool positive,
            List<int> output,
            Dictionary<long, SplitVertices> splitCache,
            Dictionary<int, SplitVertices> vertexSplitCache,
            Vector3 planePoint,
            Vector3 planeNormal,
            float gap,
            float planeEpsilon,
            float alpha)
        {
            for (int i = 0; i < 3; i++)
            {
                int current;
                int next;
                float currentDistance;
                float nextDistance;

                if (i == 0)
                {
                    current = i0;
                    next = i1;
                    currentDistance = d0;
                    nextDistance = d1;
                }
                else if (i == 1)
                {
                    current = i1;
                    next = i2;
                    currentDistance = d1;
                    nextDistance = d2;
                }
                else
                {
                    current = i2;
                    next = i0;
                    currentDistance = d2;
                    nextDistance = d0;
                }

                bool currentInside = positive ? currentDistance >= 0f : currentDistance <= 0f;
                bool nextInside = positive ? nextDistance >= 0f : nextDistance <= 0f;

                if (currentInside)
                {
                    int vertex = Mathf.Abs(currentDistance) <= planeEpsilon
                        ? GetOnPlaneSplitVertex(
                            meshData,
                            current,
                            positive ? 1 : -1,
                            vertexSplitCache,
                            planeNormal,
                            gap,
                            alpha)
                        : current;
                    output.Add(vertex);
                }

                if (currentInside != nextInside)
                {
                    output.Add(GetSplitVertex(
                        meshData,
                        current,
                        next,
                        positive ? 1 : -1,
                        splitCache,
                        planePoint,
                        planeNormal,
                        gap,
                        alpha));
                }
            }

            RemoveConsecutiveDuplicates(output);
        }

        private static long IntersectionBucketKey(int x, int y, int z)
        {
            return (((long)x * 73856093L) ^ ((long)y * 19349663L) ^ ((long)z * 83492791L));
        }

        private long GetIntersectionBucketKey(Vector3 position, float alpha)
        {
            return IntersectionBucketKey(
                Mathf.FloorToInt(position.x / alpha),
                Mathf.FloorToInt(position.y / alpha),
                Mathf.FloorToInt(position.z / alpha));
        }

        private int FindNearbyIntersection(Vector3 position, int sign, float alpha)
        {
            float alphaSquared = alpha * alpha;
            int cx = Mathf.FloorToInt(position.x / alpha);
            int cy = Mathf.FloorToInt(position.y / alpha);
            int cz = Mathf.FloorToInt(position.z / alpha);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long key = IntersectionBucketKey(cx + dx, cy + dy, cz + dz);
                        List<int> candidates;
                        if (!scratchIntersectionBuckets.TryGetValue(key, out candidates))
                        {
                            continue;
                        }

                        for (int i = 0; i < candidates.Count; i++)
                        {
                            IntersectionSample sample = scratchIntersections[candidates[i]];
                            if (sample.Sign == sign &&
                                (sample.Position - position).sqrMagnitude <= alphaSquared)
                            {
                                return sample.Index;
                            }
                        }
                    }
                }
            }

            return -1;
        }

        private void RecordIntersection(int index, Vector3 position, int sign, float alpha)
        {
            IntersectionSample sample;
            sample.Index = index;
            sample.Position = position;
            sample.Sign = sign;
            scratchIntersections.Add(sample);

            long key = GetIntersectionBucketKey(position, alpha);
            List<int> candidates;
            if (!scratchIntersectionBuckets.TryGetValue(key, out candidates))
            {
                candidates = new List<int>(4);
                scratchIntersectionBuckets[key] = candidates;
            }
            candidates.Add(scratchIntersections.Count - 1);
        }

        private int GetOnPlaneSplitVertex(
            SkinMeshData meshData,
            int vertex,
            int sign,
            Dictionary<int, SplitVertices> vertexSplitCache,
            Vector3 planeNormal,
            float gap,
            float alpha)
        {
            SplitVertices split;
            if (!vertexSplitCache.TryGetValue(vertex, out split))
            {
                Vector3 offset = planeNormal * (gap * 0.5f);
                Vector3 positivePoint = meshData.Positions[vertex] + offset;
                Vector3 negativePoint = meshData.Positions[vertex] - offset;

                split = new SplitVertices();
                split.Positive = FindNearbyIntersection(positivePoint, 1, alpha);
                if (split.Positive < 0)
                {
                    split.Positive = meshData.AddVertex(
                        positivePoint,
                        meshData.Velocities[vertex],
                        meshData.Uvs[vertex],
                        meshData.IsPermanentlyPinned(vertex));
                    RecordIntersection(split.Positive, positivePoint, 1, alpha);
                }

                split.Negative = FindNearbyIntersection(negativePoint, -1, alpha);
                if (split.Negative < 0)
                {
                    split.Negative = meshData.AddVertex(
                        negativePoint,
                        meshData.Velocities[vertex],
                        meshData.Uvs[vertex],
                        meshData.IsPermanentlyPinned(vertex));
                    RecordIntersection(split.Negative, negativePoint, -1, alpha);
                }

                vertexSplitCache[vertex] = split;
            }

            return sign > 0 ? split.Positive : split.Negative;
        }

        private int GetSplitVertex(
            SkinMeshData meshData,
            int a,
            int b,
            int sign,
            Dictionary<long, SplitVertices> splitCache,
            Vector3 planePoint,
            Vector3 planeNormal,
            float gap,
            float alpha)
        {
            long key = SkinMeshData.EdgeKey(a, b);
            SplitVertices split;
            if (!splitCache.TryGetValue(key, out split))
            {
                int low = Mathf.Min(a, b);
                int high = Mathf.Max(a, b);
                float lowDistance = Vector3.Dot(planeNormal, meshData.Positions[low] - planePoint);
                float highDistance = Vector3.Dot(planeNormal, meshData.Positions[high] - planePoint);
                float denominator = lowDistance - highDistance;
                float t = Mathf.Abs(denominator) > 1e-9f
                    ? Mathf.Clamp01(lowDistance / denominator)
                    : 0.5f;

                Vector3 point = Vector3.LerpUnclamped(
                    meshData.Positions[low],
                    meshData.Positions[high],
                    t);
                Vector2 uv = Vector2.LerpUnclamped(meshData.Uvs[low], meshData.Uvs[high], t);
                Vector3 velocity = Vector3.LerpUnclamped(
                    meshData.Velocities[low],
                    meshData.Velocities[high],
                    t);
                bool pinned = meshData.IsPermanentlyPinned(low) &&
                              meshData.IsPermanentlyPinned(high);

                Vector3 offset = planeNormal * (gap * 0.5f);
                Vector3 positivePoint = point + offset;
                Vector3 negativePoint = point - offset;

                split = new SplitVertices();
                split.Positive = FindNearbyIntersection(positivePoint, 1, alpha);
                if (split.Positive < 0)
                {
                    split.Positive = meshData.AddVertex(positivePoint, velocity, uv, pinned);
                    RecordIntersection(split.Positive, positivePoint, 1, alpha);
                }

                split.Negative = FindNearbyIntersection(negativePoint, -1, alpha);
                if (split.Negative < 0)
                {
                    split.Negative = meshData.AddVertex(negativePoint, velocity, uv, pinned);
                    RecordIntersection(split.Negative, negativePoint, -1, alpha);
                }

                splitCache[key] = split;
            }

            return sign > 0 ? split.Positive : split.Negative;
        }

        private void AddPolygonTriangles(
            SkinMeshData meshData,
            List<int> polygon,
            List<int> output,
            float alpha,
            float minAngleCos)
        {
            if (polygon.Count < 3)
            {
                return;
            }

            for (int i = 1; i < polygon.Count - 1; i++)
            {
                TryAddTriangle(
                    meshData, polygon[0], polygon[i], polygon[i + 1],
                    output, alpha, minAngleCos, true);
            }
        }

        private bool TryAddTriangle(
            SkinMeshData meshData,
            int a,
            int b,
            int c,
            List<int> output,
            float alpha,
            float minAngleCos,
            bool enforceAngle)
        {
            if (a == b || b == c || a == c)
            {
                return false;
            }

            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            Vector3 pc = meshData.Positions[c];
            Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
            if (cross.sqrMagnitude <= MinimumTriangleArea ||
                !HasMinimumEdgeLength(meshData, a, b, c, alpha) ||
                (enforceAngle && !HasValidAngles(meshData, a, b, c, minAngleCos)))
            {
                return false;
            }

            output.Add(a);
            output.Add(b);
            output.Add(c);
            return true;
        }

        private static bool HasMinimumEdgeLength(
            SkinMeshData meshData,
            int a,
            int b,
            int c,
            float alpha)
        {
            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            Vector3 pc = meshData.Positions[c];
            float alphaSquared = alpha * alpha;
            return (pb - pa).sqrMagnitude > alphaSquared &&
                   (pc - pa).sqrMagnitude > alphaSquared &&
                   (pc - pb).sqrMagnitude > alphaSquared;
        }

        private static bool HasValidAngles(
            SkinMeshData meshData,
            int a,
            int b,
            int c,
            float minAngleCos)
        {
            Vector3 pa = meshData.Positions[a];
            Vector3 pb = meshData.Positions[b];
            Vector3 pc = meshData.Positions[c];
            Vector3 ab = pb - pa;
            Vector3 ac = pc - pa;
            Vector3 ba = pa - pb;
            Vector3 bc = pc - pb;
            Vector3 ca = pa - pc;
            Vector3 cb = pb - pc;
            float cosSquared = minAngleCos * minAngleCos;
            if (!HasAngle(ab, ac, cosSquared) ||
                !HasAngle(ba, bc, cosSquared) ||
                !HasAngle(ca, cb, cosSquared))
            {
                return false;
            }

            return true;
        }

        private static bool HasAngle(Vector3 a, Vector3 b, float cosSquared)
        {
            float dot = Vector3.Dot(a, b);
            if (dot <= 0f)
            {
                return true;
            }

            float threshold = cosSquared * a.sqrMagnitude * b.sqrMagnitude;
            return dot * dot < threshold;
        }

        private static void RemoveConsecutiveDuplicates(List<int> indices)
        {
            for (int i = indices.Count - 1; i > 0; i--)
            {
                if (indices[i] == indices[i - 1])
                {
                    indices.RemoveAt(i);
                }
            }

            if (indices.Count > 1 && indices[0] == indices[indices.Count - 1])
            {
                indices.RemoveAt(indices.Count - 1);
            }
        }

        private static bool TriangleOutsideBounds(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 boundsMin,
            Vector3 boundsMax)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            float maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

            return maxX < boundsMin.x || minX > boundsMax.x ||
                   maxY < boundsMin.y || minY > boundsMax.y ||
                   maxZ < boundsMin.z || minZ > boundsMax.z;
        }

        private static bool TriangleNearSegment(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 start,
            Vector3 end,
            float radius)
        {
            float radiusSquared = radius * radius;
            Vector3 centroid = (a + b + c) / 3f;

            if (PointSegmentSqrDistance(centroid, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance(a, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance(b, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance(c, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance((a + b) * 0.5f, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance((b + c) * 0.5f, start, end) <= radiusSquared)
            {
                return true;
            }
            if (PointSegmentSqrDistance((c + a) * 0.5f, start, end) <= radiusSquared)
            {
                return true;
            }

            return false;
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



