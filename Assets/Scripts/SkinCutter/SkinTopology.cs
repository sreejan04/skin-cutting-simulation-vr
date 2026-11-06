using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SkinCutter
{
    public class SkinTopology
    {
        public struct EdgeRef
        {
            public int v1;
            public int v2;
        }

        private struct SegmentHit
        {
            public bool hit;
            public float t;
            public float u;
        }

        public List<Vertex> vertices = new List<Vertex>();
        public List<Triangle> triangles = new List<Triangle>();
        public HashSet<int> cutVertexLeft = new HashSet<int>();
        public HashSet<int> cutVertexRight = new HashSet<int>();

        public Vector3 boundsMin;
        public Vector3 boundsMax;

        private Dictionary<string, EdgeRef> edgeMapCache;

        public bool isBox;
        public float boxSize;

        public void InitGrid(int rows, int cols, float width, float height)
        {
            vertices.Clear();
            triangles.Clear();
            cutVertexLeft.Clear();
            cutVertexRight.Clear();
            edgeMapCache = null;
            isBox = false;
            boxSize = 0f;

            float hw = width / 2f;
            float hh = height / 2f;

            for (int i = 0; i <= rows; i++)
            {
                for (int j = 0; j <= cols; j++)
                {
                    float x = -hw + (j / (float)cols) * width;
                    float y = -hh + (i / (float)rows) * height;
                    vertices.Add(new Vertex(new Vector3(x, y, 0f)));
                }
            }

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    int v0 = i * (cols + 1) + j;
                    int v1 = i * (cols + 1) + j + 1;
                    int v2 = (i + 1) * (cols + 1) + j;
                    int v3 = (i + 1) * (cols + 1) + j + 1;
                    triangles.Add(new Triangle(v0, v1, v2));
                    triangles.Add(new Triangle(v1, v3, v2));
                }
            }

            RecomputeBounds();
        }

        public void InitBox(float size, int subdiv)
        {
            vertices.Clear();
            triangles.Clear();
            cutVertexLeft.Clear();
            cutVertexRight.Clear();
            edgeMapCache = null;

            isBox = true;
            boxSize = size;

            float half = size / 2f;
            var vertMap = new Dictionary<string, int>();

            int GetOrCreate(float x, float y, float z)
            {
                string key = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6}", x, y, z);
                if (vertMap.TryGetValue(key, out int existing)) return existing;
                int idx = vertices.Count;
                vertices.Add(new Vertex(new Vector3(x, y, z)));
                vertMap[key] = idx;
                return idx;
            }

            void AddFace(Vector3 uAxis, Vector3 vAxis, Vector3 origin, bool flip)
            {
                for (int i = 0; i < subdiv; i++)
                {
                    for (int j = 0; j < subdiv; j++)
                    {
                        Vector3 u0 = origin + uAxis * ((j / (float)subdiv) * size) + vAxis * ((i / (float)subdiv) * size);
                        Vector3 u1 = origin + uAxis * (((j + 1) / (float)subdiv) * size) + vAxis * ((i / (float)subdiv) * size);
                        Vector3 u2 = origin + uAxis * ((j / (float)subdiv) * size) + vAxis * (((i + 1) / (float)subdiv) * size);
                        Vector3 u3 = origin + uAxis * (((j + 1) / (float)subdiv) * size) + vAxis * (((i + 1) / (float)subdiv) * size);

                        int v0 = GetOrCreate(u0.x, u0.y, u0.z);
                        int v1 = GetOrCreate(u1.x, u1.y, u1.z);
                        int v2 = GetOrCreate(u2.x, u2.y, u2.z);
                        int v3 = GetOrCreate(u3.x, u3.y, u3.z);

                        if (flip)
                        {
                            triangles.Add(new Triangle(v0, v2, v1));
                            triangles.Add(new Triangle(v1, v2, v3));
                        }
                        else
                        {
                            triangles.Add(new Triangle(v0, v1, v2));
                            triangles.Add(new Triangle(v1, v3, v2));
                        }
                    }
                }
            }

            AddFace(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(-half, -half, half), false);
            AddFace(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(-half, -half, -half), true);
            AddFace(new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f), new Vector3(half, -half, half), true);
            AddFace(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f), new Vector3(-half, -half, -half), false);
            AddFace(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(-half, half, half), true);
            AddFace(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(-half, -half, -half), false);

            RecomputeBounds();
        }

        private void RecomputeBounds()
        {
            if (vertices.Count == 0)
            {
                boundsMin = Vector3.zero;
                boundsMax = Vector3.zero;
                return;
            }
            Vector3 mn = vertices[0].pos;
            Vector3 mx = vertices[0].pos;
            for (int i = 1; i < vertices.Count; i++)
            {
                Vector3 p = vertices[i].pos;
                if (p.x < mn.x) mn.x = p.x;
                if (p.y < mn.y) mn.y = p.y;
                if (p.z < mn.z) mn.z = p.z;
                if (p.x > mx.x) mx.x = p.x;
                if (p.y > mx.y) mx.y = p.y;
                if (p.z > mx.z) mx.z = p.z;
            }
            boundsMin = mn;
            boundsMax = mx;
        }

        public static string GetEdgeKey(int a, int b)
        {
            return a < b ? a + "-" + b : b + "-" + a;
        }

        public Dictionary<string, EdgeRef> BuildEdgeMap()
        {
            if (edgeMapCache != null) return edgeMapCache;

            var edgeMap = new Dictionary<string, EdgeRef>();
            foreach (var tri in triangles)
            {
                for (int ei = 0; ei < 3; ei++)
                {
                    int a = tri[ei];
                    int b = tri[(ei + 1) % 3];
                    string key = GetEdgeKey(a, b);
                    if (!edgeMap.ContainsKey(key))
                    {
                        edgeMap[key] = new EdgeRef { v1 = a, v2 = b };
                    }
                }
            }
            edgeMapCache = edgeMap;
            return edgeMap;
        }

        private static SegmentHit EdgesIntersect(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2)
        {
            var result = default(SegmentHit);

            Vector3 uVec = p2 - p1;
            Vector3 vVec = q2 - q1;
            Vector3 w = p1 - q1;

            float a = Vector3.Dot(uVec, uVec);
            float b = Vector3.Dot(uVec, vVec);
            float c = Vector3.Dot(vVec, vVec);
            float d = Vector3.Dot(uVec, w);
            float e = Vector3.Dot(vVec, w);
            float det = a * c - b * b;

            if (Mathf.Abs(det) < 1e-10f)
            {
                float uLen = uVec.magnitude;
                if (uLen < 1e-10f || vVec.magnitude < 1e-10f) return result;

                Vector3 uDir = uVec / uLen;
                Vector3 q1ToP1 = p1 - q1;
                if (q1ToP1.magnitude > 1e-8f)
                {
                    Vector3 cross = Vector3.Cross(uDir, q1ToP1);
                    if (cross.magnitude > 1e-6f) return result;
                }

                float tProj1 = Vector3.Dot(p1, uDir);
                float tProj2 = Vector3.Dot(p2, uDir);
                float qProj1 = Vector3.Dot(q1, uDir);
                float qProj2 = Vector3.Dot(q2, uDir);
                float lo = Mathf.Max(Mathf.Min(tProj1, tProj2), Mathf.Min(qProj1, qProj2));
                float hi = Mathf.Min(Mathf.Max(tProj1, tProj2), Mathf.Max(qProj1, qProj2));
                if (hi < lo - 1e-6f) return result;

                float mid = (lo + hi) / 2f;
                result.hit = true;
                result.t = Mathf.Clamp((mid - tProj1) / uLen, 0f, 1f);
                result.u = Mathf.Clamp((mid - qProj1) / uLen, 0f, 1f);
                return result;
            }

            float s = (b * e - c * d) / det;
            float tVal = (a * e - b * d) / det;

            if (s < -1e-6f || s > 1f + 1e-6f || tVal < -1e-6f || tVal > 1f + 1e-6f) return result;

            Vector3 pClosest = p1 + uVec * s;
            Vector3 qClosest = q1 + vVec * tVal;
            if (Vector3.Distance(pClosest, qClosest) > 1e-6f) return result;

            result.hit = true;
            result.t = s;
            result.u = tVal;
            return result;
        }

        public List<EdgeCrossing> DetectCrossings(List<Vector3> pathPoints)
        {
            var crossings = new List<EdgeCrossing>();
            var edgeMap = BuildEdgeMap();

            for (int i = 1; i < pathPoints.Count; i++)
            {
                Vector3 A = pathPoints[i - 1];
                Vector3 B = pathPoints[i];

                foreach (var kv in edgeMap)
                {
                    Vector3 v1p = vertices[kv.Value.v1].pos;
                    Vector3 v2p = vertices[kv.Value.v2].pos;
                    SegmentHit result = EdgesIntersect(A, B, v1p, v2p);
                    if (!result.hit) continue;

                    crossings.Add(new EdgeCrossing
                    {
                        edgeKey = kv.Key,
                        v1 = kv.Value.v1,
                        v2 = kv.Value.v2,
                        t = result.t + i - 1,
                        point = new Vector3(
                            v1p.x + result.u * (v2p.x - v1p.x),
                            v1p.y + result.u * (v2p.y - v1p.y),
                            v1p.z + result.u * (v2p.z - v1p.z))
                    });
                }
            }

            crossings.Sort((x, y) => x.t.CompareTo(y.t));

            var unique = new List<EdgeCrossing>();
            var seen = new HashSet<string>();
            foreach (var c in crossings)
            {
                if (seen.Add(c.edgeKey)) unique.Add(c);
            }
            return unique;
        }

        public void ApplyCut(List<EdgeCrossing> crossings)
        {
            if (crossings.Count < 1) return;

            var pathVertices = new List<int>();

            foreach (var crossing in crossings)
            {
                int nv = vertices.Count;
                vertices.Add(new Vertex(crossing.point));
                pathVertices.Add(nv);

                var newTriangles = new List<Triangle>(triangles.Count + 2);
                foreach (var tri in triangles)
                {
                    int found = -1;
                    for (int ei = 0; ei < 3; ei++)
                    {
                        if (GetEdgeKey(tri[ei], tri[(ei + 1) % 3]) == crossing.edgeKey)
                        {
                            found = ei;
                            break;
                        }
                    }

                    if (found == -1)
                    {
                        newTriangles.Add(tri);
                    }
                    else
                    {
                        int va = tri[found];
                        int vb = tri[(found + 1) % 3];
                        int vc = tri[(found + 2) % 3];
                        newTriangles.Add(new Triangle(va, nv, vc));
                        newTriangles.Add(new Triangle(nv, vb, vc));
                    }
                }
                triangles = newTriangles;
            }

            SeparateAlongPath(pathVertices, crossings);

            edgeMapCache = null;
            RecomputeBounds();
        }

        private void SeparateAlongPath(List<int> pathVertices, List<EdgeCrossing> crossings)
        {
            if (pathVertices.Count < 1) return;

            var dupMap = new Dictionary<int, int>();
            foreach (int pv in pathVertices)
            {
                int dup = vertices.Count;
                vertices.Add(new Vertex(vertices[pv].pos, vertices[pv].origPos));
                dupMap[pv] = dup;
            }

            var pathSet = new HashSet<int>(pathVertices);
            Vector3 pathDir = ComputePathDirection(pathVertices);
            Vector3 surfaceNormal = GetPathSurfaceNormal(pathVertices);

            var newTriangles = new List<Triangle>(triangles.Count + pathVertices.Count);
            foreach (var tri in triangles)
            {
                var onPath = new List<int>(3);
                var offPath = new List<int>(3);
                for (int i = 0; i < 3; i++)
                {
                    int v = tri[i];
                    if (pathSet.Contains(v)) onPath.Add(v);
                    else offPath.Add(v);
                }

                if (onPath.Count == 0)
                {
                    newTriangles.Add(tri);
                }
                else if (onPath.Count == 1)
                {
                    int pv = onPath[0];
                    bool right = TriangleSideIsRight(tri, pv, pathDir, surfaceNormal);
                    if (right && dupMap.TryGetValue(pv, out int dp))
                    {
                        newTriangles.Add(ReplaceVert(tri, pv, dp));
                    }
                    else
                    {
                        newTriangles.Add(tri);
                    }
                }
                else if (onPath.Count == 2)
                {
                    int pv1 = onPath[0];
                    int pv2 = onPath[1];
                    int other = offPath[0];
                    Vector3 rel = vertices[other].pos - vertices[pv1].pos;
                    Vector3 sideAxis = Vector3.Cross(pathDir, surfaceNormal).normalized;
                    bool right = Vector3.Dot(rel, sideAxis) > 0f;
                    if (right)
                    {
                        var nt = tri;
                        if (dupMap.TryGetValue(pv1, out int dp1)) nt = ReplaceVert(nt, pv1, dp1);
                        if (dupMap.TryGetValue(pv2, out int dp2)) nt = ReplaceVert(nt, pv2, dp2);
                        newTriangles.Add(nt);
                    }
                    else
                    {
                        newTriangles.Add(tri);
                    }
                }
                else
                {
                    bool right = TriangleSideIsRight(tri, tri.v1, pathDir, surfaceNormal);
                    if (right)
                    {
                        var nt = tri;
                        for (int i = 0; i < 3; i++)
                        {
                            if (dupMap.TryGetValue(nt[i], out int dp)) nt = ReplaceVert(nt, nt[i], dp);
                        }
                        newTriangles.Add(nt);
                    }
                    else
                    {
                        newTriangles.Add(tri);
                    }
                }
            }
            triangles = newTriangles;

            foreach (int pv in pathVertices)
            {
                if (HasIncidentTriangle(pv)) cutVertexLeft.Add(pv);
                int dp = dupMap[pv];
                if (HasIncidentTriangle(dp)) cutVertexRight.Add(dp);
            }

            BreakSharedVertices(pathVertices, dupMap);
            OpenSlit(pathDir, surfaceNormal);
        }

        private static Triangle ReplaceVert(Triangle tri, int from, int to)
        {
            if (tri.v1 == from) tri.v1 = to;
            else if (tri.v2 == from) tri.v2 = to;
            else if (tri.v3 == from) tri.v3 = to;
            return tri;
        }

        private void OpenSlit(Vector3 pathDir, Vector3 surfaceNormal, float gapSize = 0.04f)
        {
            Vector3 perp = Vector3.Cross(pathDir, surfaceNormal);
            if (perp.magnitude < 1e-8f)
            {
                Vector3 up = Mathf.Abs(pathDir.y) < 0.9f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);
                perp = Vector3.Cross(pathDir, up);
            }
            perp = perp.normalized * (gapSize / 2f);

            foreach (int vi in cutVertexLeft)
            {
                vertices[vi].pos -= perp;
            }
            foreach (int vi in cutVertexRight)
            {
                vertices[vi].pos += perp;
            }
        }

        private void BreakSharedVertices(List<int> pathVertices, Dictionary<int, int> dupMap)
        {
            var pathSet = new HashSet<int>(pathVertices);
            var dupSet = new HashSet<int>(dupMap.Values);

            var triSide = new sbyte[triangles.Count];
            for (int i = 0; i < triangles.Count; i++)
            {
                bool hasLeft = false, hasRight = false;
                for (int k = 0; k < 3; k++)
                {
                    int v = triangles[i][k];
                    if (pathSet.Contains(v)) hasLeft = true;
                    if (dupSet.Contains(v)) hasRight = true;
                }
                if (hasLeft && !hasRight) triSide[i] = -1;
                else if (hasRight && !hasLeft) triSide[i] = 1;
            }

            var leftVerts = new HashSet<int>();
            var rightVerts = new HashSet<int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (triSide[i] == 0) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = triangles[i][k];
                    if (pathSet.Contains(v) || dupSet.Contains(v)) continue;
                    if (triSide[i] == -1) leftVerts.Add(v);
                    else rightVerts.Add(v);
                }
            }

            var nonPathDups = new Dictionary<int, int>();
            foreach (int vi in leftVerts)
            {
                if (!rightVerts.Contains(vi)) continue;
                int dup = vertices.Count;
                vertices.Add(new Vertex(vertices[vi].pos, vertices[vi].origPos));
                nonPathDups[vi] = dup;
            }

            if (nonPathDups.Count == 0) return;

            for (int i = 0; i < triangles.Count; i++)
            {
                if (triSide[i] != 1) continue;
                var tri = triangles[i];
                for (int k = 0; k < 3; k++)
                {
                    if (nonPathDups.TryGetValue(tri[k], out int dp)) tri = ReplaceVert(tri, tri[k], dp);
                }
                triangles[i] = tri;
            }
        }

        private Vector3 GetTriangleNormal(Triangle tri)
        {
            Vector3 ab = vertices[tri.v2].pos - vertices[tri.v1].pos;
            Vector3 ac = vertices[tri.v3].pos - vertices[tri.v1].pos;
            Vector3 n = Vector3.Cross(ab, ac);
            if (n.magnitude < 1e-10f) return new Vector3(0f, 0f, 1f);
            return n.normalized;
        }

        private Vector3 GetVertexNormal(int vi)
        {
            var avg = Vector3.zero;
            int count = 0;
            foreach (var tri in triangles)
            {
                if (tri.v1 == vi || tri.v2 == vi || tri.v3 == vi)
                {
                    avg += GetTriangleNormal(tri);
                    count++;
                }
            }
            if (count == 0) return new Vector3(0f, 0f, 1f);
            return (avg / count).normalized;
        }

        private Vector3 GetPathSurfaceNormal(List<int> pathVertices)
        {
            var avg = Vector3.zero;
            foreach (int pv in pathVertices)
            {
                avg += GetVertexNormal(pv);
            }
            if (avg.magnitude < 1e-10f) return new Vector3(0f, 0f, 1f);
            return avg.normalized;
        }

        private bool TriangleSideIsRight(Triangle tri, int pv, Vector3 pathDir, Vector3 surfaceNormal)
        {
            int other = tri.v1 != pv ? tri.v1 : tri.v2 != pv ? tri.v2 : tri.v3;
            Vector3 diff = vertices[other].pos - vertices[pv].pos;
            Vector3 sideAxis = Vector3.Cross(pathDir, surfaceNormal).normalized;
            return Vector3.Dot(diff, sideAxis) > 0f;
        }

        private Vector3 ComputePathDirection(List<int> pathVertices)
        {
            if (pathVertices.Count < 2) return new Vector3(1f, 0f, 0f);
            Vector3 dir = vertices[pathVertices[pathVertices.Count - 1]].pos - vertices[pathVertices[0]].pos;
            if (dir.magnitude < 1e-8f) dir.Set(1f, 0f, 0f);
            return dir.normalized;
        }

        private bool HasIncidentTriangle(int vi)
        {
            foreach (var t in triangles)
            {
                if (t.v1 == vi || t.v2 == vi || t.v3 == vi) return true;
            }
            return false;
        }
    }
}
