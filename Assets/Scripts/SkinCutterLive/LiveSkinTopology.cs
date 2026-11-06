using System.Collections.Generic;
using UnityEngine;
using SkinCutter;

namespace SkinCutter.Live
{
    public class LiveSkinTopology
    {
        private struct EdgeRec
        {
            public int a;
            public int b;
            public int t0;
            public int t1;
        }

        private struct Hit
        {
            public int edgeIdx;
            public float tSeg;
            public float uEdge;
        }

        public readonly List<Vertex> vertices = new List<Vertex>();
        public List<Triangle> triangles = new List<Triangle>();
        public Vector3 boundsMin;
        public Vector3 boundsMax;
        public bool isBox;
        public float boxSize;

        private readonly List<EdgeRec> edges = new List<EdgeRec>();
        private readonly Dictionary<long, int> edgeLookup = new Dictionary<long, int>();
        private readonly Dictionary<long, int> splitVertexByEdge = new Dictionary<long, int>();

        private Dictionary<long, List<int>> cells;
        private bool hashDirty = true;
        private int[] visitStamp;
        private int stampCounter;

        private readonly List<int> candidateEdges = new List<int>();
        private readonly List<Hit> hits = new List<Hit>();
        private readonly HashSet<int> touched = new HashSet<int>();
        private readonly List<int> touchedList = new List<int>();
        private readonly List<int> segVerts = new List<int>();
        private readonly Dictionary<int, int> twinMap = new Dictionary<int, int>();

        private const float CellSize = 0.2f;

        public LiveSkinTopology(SkinTopology source)
        {
            isBox = source.isBox;
            boxSize = source.boxSize;
            for (int i = 0; i < source.vertices.Count; i++)
            {
                Vertex v = source.vertices[i];
                vertices.Add(new Vertex(v.pos, v.origPos));
            }
            triangles = new List<Triangle>(source.triangles);
            BuildAdjacency();
            RecomputeBounds();
        }

        public static long Key(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private int AddVertex(Vector3 p)
        {
            int idx = vertices.Count;
            vertices.Add(new Vertex(p));
            return idx;
        }

        private void BuildAdjacency()
        {
            edges.Clear();
            edgeLookup.Clear();
            for (int ti = 0; ti < triangles.Count; ti++)
                AddTriangleAdjacency(ti, triangles[ti]);
            hashDirty = true;
        }

        private int GetOrCreateEdge(int a, int b)
        {
            long key = Key(a, b);
            if (edgeLookup.TryGetValue(key, out int idx)) return idx;
            idx = edges.Count;
            edges.Add(new EdgeRec { a = a, b = b, t0 = -1, t1 = -1 });
            edgeLookup[key] = idx;
            return idx;
        }

        private void AddTriangleAdjacency(int ti, Triangle tri)
        {
            int e0 = GetOrCreateEdge(tri.v1, tri.v2);
            int e1 = GetOrCreateEdge(tri.v2, tri.v3);
            int e2 = GetOrCreateEdge(tri.v3, tri.v1);
            Attach(e0, ti);
            Attach(e1, ti);
            Attach(e2, ti);
        }

        private void Attach(int edgeIdx, int ti)
        {
            EdgeRec e = edges[edgeIdx];
            if (e.t0 == -1) e.t0 = ti;
            else if (e.t1 == -1) e.t1 = ti;
            edges[edgeIdx] = e;
        }

        private void RemoveTriangleAdjacency(int ti, Triangle tri)
        {
            Detach(tri.v1, tri.v2, ti);
            Detach(tri.v2, tri.v3, ti);
            Detach(tri.v3, tri.v1, ti);
        }

        private void Detach(int a, int b, int ti)
        {
            long key = Key(a, b);
            if (!edgeLookup.TryGetValue(key, out int idx)) return;
            EdgeRec e = edges[idx];
            if (e.t0 == ti) { e.t0 = e.t1; e.t1 = -1; }
            else if (e.t1 == ti) { e.t1 = -1; }
            edges[idx] = e;
        }

        private static int FindEdgeSlot(Triangle tri, int ea, int eb)
        {
            long target = Key(ea, eb);
            for (int i = 0; i < 3; i++)
            {
                int x = tri[i];
                int y = tri[(i + 1) % 3];
                if (Key(x, y) == target) return i;
            }
            return -1;
        }

        private int SplitTriangleAcrossEdge(int ti, int ea, int eb, int n)
        {
            if (ti < 0 || ti >= triangles.Count) return -1;
            Triangle tri = triangles[ti];
            int i = FindEdgeSlot(tri, ea, eb);
            if (i < 0) return -1;

            int x = tri[i];
            int y = tri[(i + 1) % 3];
            int c = tri[(i + 2) % 3];

            RemoveTriangleAdjacency(ti, tri);

            Triangle a1 = new Triangle(x, n, c);
            Triangle a2 = new Triangle(n, y, c);

            triangles[ti] = a1;
            AddTriangleAdjacency(ti, a1);

            int ni = triangles.Count;
            triangles.Add(a2);
            AddTriangleAdjacency(ni, a2);
            return ni;
        }

        public bool CutSegment(Vector3 a, Vector3 b, List<int> newVerts, float gap = 0.04f)
        {
            newVerts.Clear();
            if ((b - a).sqrMagnitude < 1e-10f) return false;

            hashDirty = true;
            EnsureSpatialHash();
            QueryEdges(a, b, candidateEdges);

            hits.Clear();
            for (int k = 0; k < candidateEdges.Count; k++)
            {
                int idx = candidateEdges[k];
                EdgeRec rec = edges[idx];
                if (rec.t0 < 0 && rec.t1 < 0) continue;
                if (splitVertexByEdge.ContainsKey(Key(rec.a, rec.b))) continue;

                if (SegmentSegment(a, b, vertices[rec.a].pos, vertices[rec.b].pos,
                        out float tSeg, out float uEdge))
                {
                    hits.Add(new Hit { edgeIdx = idx, tSeg = tSeg, uEdge = uEdge });
                }
            }

            if (hits.Count == 0) return false;

            hits.Sort((p, q) => p.tSeg.CompareTo(q.tSeg));

            touched.Clear();
            touchedList.Clear();
            segVerts.Clear();

            Vector3 segDir = (b - a).normalized;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 normal = Vector3.forward;
            bool haveNormal = false;

            for (int h = 0; h < hits.Count; h++)
            {
                int idx = hits[h].edgeIdx;
                EdgeRec rec = edges[idx];
                long key = Key(rec.a, rec.b);
                if (splitVertexByEdge.ContainsKey(key)) continue;

                int t0 = rec.t0;
                int t1 = rec.t1;
                if (t0 < 0 && t1 < 0) continue;

                if (!haveNormal)
                {
                    normal = t0 >= 0 ? TriangleNormal(triangles[t0]) : TriangleNormal(triangles[t1]);
                    haveNormal = true;
                }

                Vector3 p = Vector3.Lerp(vertices[rec.a].pos, vertices[rec.b].pos, hits[h].uEdge);
                int n = AddVertex(p);
                splitVertexByEdge[key] = n;
                segVerts.Add(n);

                if (t0 >= 0)
                {
                    int a2 = SplitTriangleAcrossEdge(t0, rec.a, rec.b, n);
                    Touch(t0);
                    Touch(a2);
                }
                if (t1 >= 0)
                {
                    int a2 = SplitTriangleAcrossEdge(t1, rec.a, rec.b, n);
                    Touch(t1);
                    Touch(a2);
                }
            }

            if (segVerts.Count == 0) return false;

            Vector3 sideAxis = Vector3.Cross(segDir, normal);
            if (sideAxis.sqrMagnitude < 1e-10f) sideAxis = Vector3.right;
            sideAxis = sideAxis.normalized;

            twinMap.Clear();
            for (int i = 0; i < segVerts.Count; i++)
            {
                int n = segVerts[i];
                twinMap[n] = AddVertex(vertices[n].pos);
            }

            for (int i = 0; i < touchedList.Count; i++)
            {
                int ti = touchedList[i];
                Triangle tri = triangles[ti];

                Vector3 centroid = TriangleCentroid(ti);
                if (Vector3.Dot(centroid - mid, sideAxis) <= 0f) continue;

                int v1 = tri.v1;
                int v2 = tri.v2;
                int v3 = tri.v3;
                if (twinMap.TryGetValue(v1, out int d1)) v1 = d1;
                if (twinMap.TryGetValue(v2, out int d2)) v2 = d2;
                if (twinMap.TryGetValue(v3, out int d3)) v3 = d3;

                if (v1 != tri.v1 || v2 != tri.v2 || v3 != tri.v3)
                    SetTriangle(ti, new Triangle(v1, v2, v3));
            }

            float half = gap * 0.5f;
            for (int i = 0; i < segVerts.Count; i++)
            {
                int n = segVerts[i];
                int twin = twinMap[n];
                vertices[n].pos -= sideAxis * half;
                vertices[twin].pos += sideAxis * half;
                newVerts.Add(n);
                newVerts.Add(twin);
            }

            hashDirty = true;
            RecomputeBounds();
            return true;
        }

        private void Touch(int ti)
        {
            if (ti < 0) return;
            if (touched.Add(ti)) touchedList.Add(ti);
        }

        private void SetTriangle(int ti, Triangle newTri)
        {
            Triangle old = triangles[ti];
            RemoveTriangleAdjacency(ti, old);
            triangles[ti] = newTri;
            AddTriangleAdjacency(ti, newTri);
        }

        public void ResetCutTracking()
        {
            splitVertexByEdge.Clear();
        }

        private Vector3 TriangleNormal(Triangle tri)
        {
            Vector3 ab = vertices[tri.v2].pos - vertices[tri.v1].pos;
            Vector3 ac = vertices[tri.v3].pos - vertices[tri.v1].pos;
            Vector3 n = Vector3.Cross(ab, ac);
            if (n.sqrMagnitude < 1e-12f) return Vector3.forward;
            return n.normalized;
        }

        private Vector3 TriangleCentroid(int ti)
        {
            Triangle tri = triangles[ti];
            return (vertices[tri.v1].pos + vertices[tri.v2].pos + vertices[tri.v3].pos) / 3f;
        }


        private void EnsureSpatialHash()
        {
            if (!hashDirty && cells != null) return;
            if (cells == null) cells = new Dictionary<long, List<int>>();
            else foreach (KeyValuePair<long, List<int>> kv in cells) kv.Value.Clear();

            for (int i = 0; i < edges.Count; i++)
            {
                EdgeRec e = edges[i];
                if (e.t0 < 0 && e.t1 < 0) continue;

                Vector3 p0 = vertices[e.a].pos;
                Vector3 p1 = vertices[e.b].pos;
                Vector3 mn = Vector3.Min(p0, p1) - Vector3.one * 0.001f;
                Vector3 mx = Vector3.Max(p0, p1) + Vector3.one * 0.001f;

                int x0 = Mathf.FloorToInt(mn.x / CellSize);
                int y0 = Mathf.FloorToInt(mn.y / CellSize);
                int z0 = Mathf.FloorToInt(mn.z / CellSize);
                int x1 = Mathf.FloorToInt(mx.x / CellSize);
                int y1 = Mathf.FloorToInt(mx.y / CellSize);
                int z1 = Mathf.FloorToInt(mx.z / CellSize);

                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long ck = CellKey(x, y, z);
                            if (!cells.TryGetValue(ck, out List<int> list))
                            {
                                list = new List<int>(8);
                                cells[ck] = list;
                            }
                            list.Add(i);
                        }
            }
            hashDirty = false;
        }

        private void QueryEdges(Vector3 a, Vector3 b, List<int> outEdges)
        {
            outEdges.Clear();
            if (visitStamp == null || visitStamp.Length < edges.Count)
                visitStamp = new int[edges.Count + 16];
            stampCounter++;
            if (stampCounter == int.MaxValue)
            {
                for (int i = 0; i < visitStamp.Length; i++) visitStamp[i] = 0;
                stampCounter = 1;
            }

            const float QueryMargin = 0.05f;
            Vector3 mn = Vector3.Min(a, b) - Vector3.one * QueryMargin;
            Vector3 mx = Vector3.Max(a, b) + Vector3.one * QueryMargin;

            int x0 = Mathf.FloorToInt(mn.x / CellSize);
            int y0 = Mathf.FloorToInt(mn.y / CellSize);
            int z0 = Mathf.FloorToInt(mn.z / CellSize);
            int x1 = Mathf.FloorToInt(mx.x / CellSize);
            int y1 = Mathf.FloorToInt(mx.y / CellSize);
            int z1 = Mathf.FloorToInt(mx.z / CellSize);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!cells.TryGetValue(CellKey(x, y, z), out List<int> list)) continue;
                        for (int k = 0; k < list.Count; k++)
                        {
                            int idx = list[k];
                            if (visitStamp[idx] == stampCounter) continue;
                            visitStamp[idx] = stampCounter;
                            outEdges.Add(idx);
                        }
                    }
        }

        private static long CellKey(int x, int y, int z)
        {
            long cx = (uint)x & 0x1FFFFF;
            long cy = (uint)y & 0x1FFFFF;
            long cz = (uint)z & 0x1FFFFF;
            return cx | (cy << 21) | (cz << 42);
        }


        private static bool SegmentSegment(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2,
            out float s, out float t)
        {
            Vector3 d1 = p2 - p1;
            Vector3 d2 = q2 - q1;
            Vector3 r = p1 - q1;

            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a < 1e-10f && e < 1e-10f) { s = 0f; t = 0f; return (p1 - q1).sqrMagnitude < 1e-6f; }

            if (a < 1e-10f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                if (denom > 1e-10f)
                {
                    s = Mathf.Clamp01((b * f - c * e) / denom);
                }
                else
                {
                    s = 0f;
                }
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }

            Vector3 cp1 = p1 + d1 * s;
            Vector3 cp2 = q1 + d2 * t;
            return (cp1 - cp2).sqrMagnitude < 4e-4f;
        }

        private void RecomputeBounds()
        {
            if (vertices.Count == 0) { boundsMin = boundsMax = Vector3.zero; return; }
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

        public List<EdgeRecInfo> GetActiveEdges(List<EdgeRecInfo> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < edges.Count; i++)
            {
                EdgeRec e = edges[i];
                if (e.t0 < 0 && e.t1 < 0) continue;
                buffer.Add(new EdgeRecInfo { key = Key(e.a, e.b), a = e.a, b = e.b });
            }
            return buffer;
        }
    }

    public struct EdgeRecInfo
    {
        public long key;
        public int a;
        public int b;
    }
}
