using System.Collections.Generic;
using UnityEngine;

namespace SkinCutter
{
    public class DeformationSystem
    {
        private int[] springA;
        private int[] springB;
        private float[] springRest;
        private Vector3[] posBuffer;

        private int? pinnedVertex;
        private Vector3 pinTarget;
        private readonly HashSet<int> permanentPins = new HashSet<int>();
        private readonly SkinTopology topology;

        private Vector3 outlineMin;
        private Vector3 outlineMax;
        private bool hasOutline;

        public float Stiffness { get; set; }
        private bool[] pinnedFlags;

        public DeformationSystem(SkinTopology topology)
        {
            this.topology = topology;
            Stiffness = 1f;
            RebuildSprings();
        }

        public void RebuildSprings()
        {
            var seen = new HashSet<string>();
            var aList = new List<int>();
            var bList = new List<int>();
            var rList = new List<float>();

            var tris = topology.triangles;
            var verts = topology.vertices;

            for (int t = 0; t < tris.Count; t++)
            {
                Triangle tri = tris[t];
                for (int ei = 0; ei < 3; ei++)
                {
                    int va = tri[ei];
                    int vb = tri[(ei + 1) % 3];
                    string key = va < vb ? va + "-" + vb : vb + "-" + va;
                    if (!seen.Add(key)) continue;
                    aList.Add(va);
                    bList.Add(vb);
                    rList.Add(Vector3.Distance(verts[va].pos, verts[vb].pos));
                }
            }

            springA = aList.ToArray();
            springB = bList.ToArray();
            springRest = rList.ToArray();
            posBuffer = new Vector3[verts.Count];

            RefreshAnchors();
        }

        public void PinCorners(int rows, int cols)
        {
            var verts = topology.vertices;

            Vector3 mn = verts.Count > 0 ? verts[0].origPos : Vector3.zero;
            Vector3 mx = mn;
            for (int i = 1; i < verts.Count; i++)
            {
                Vector3 p = verts[i].origPos;
                if (p.x < mn.x) mn.x = p.x;
                if (p.y < mn.y) mn.y = p.y;
                if (p.z < mn.z) mn.z = p.z;
                if (p.x > mx.x) mx.x = p.x;
                if (p.y > mx.y) mx.y = p.y;
                if (p.z > mx.z) mx.z = p.z;
            }
            outlineMin = mn;
            outlineMax = mx;
            hasOutline = true;

            RefreshAnchors();
        }

        public void RefreshAnchors()
        {
            permanentPins.Clear();
            if (!hasOutline) return;

            var verts = topology.vertices;

            var edgeUse = new Dictionary<string, int>();
            foreach (var tri in topology.triangles)
            {
                for (int ei = 0; ei < 3; ei++)
                {
                    string key = SkinTopology.GetEdgeKey(tri[ei], tri[(ei + 1) % 3]);
                    edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            var boundaryVerts = new HashSet<int>();
            foreach (var tri in topology.triangles)
            {
                for (int ei = 0; ei < 3; ei++)
                {
                    string key = SkinTopology.GetEdgeKey(tri[ei], tri[(ei + 1) % 3]);
                    if (edgeUse[key] == 1)
                    {
                        boundaryVerts.Add(tri[ei]);
                        boundaryVerts.Add(tri[(ei + 1) % 3]);
                    }
                }
            }

            const float eps = 0.02f;
            for (int i = 0; i < verts.Count; i++)
            {
                if (!boundaryVerts.Contains(i)) continue;
                Vector3 p = verts[i].origPos;

                if (topology.isBox)
                {
                    var corners = new[]
                    {
                        new Vector3(outlineMin.x, outlineMin.y, outlineMin.z),
                        new Vector3(outlineMax.x, outlineMin.y, outlineMin.z),
                        new Vector3(outlineMin.x, outlineMax.y, outlineMin.z),
                        new Vector3(outlineMax.x, outlineMax.y, outlineMin.z),
                        new Vector3(outlineMin.x, outlineMin.y, outlineMax.z),
                        new Vector3(outlineMax.x, outlineMin.y, outlineMax.z),
                        new Vector3(outlineMin.x, outlineMax.y, outlineMax.z),
                        new Vector3(outlineMax.x, outlineMax.y, outlineMax.z)
                    };
                    bool pinned = false;
                    for (int a = 0; a < corners.Length && !pinned; a++)
                    {
                        for (int b = a + 1; b < corners.Length; b++)
                        {
                            Vector3 d = corners[a] - corners[b];
                            const float e = 1e-4f;
                            int nonZero = (Mathf.Abs(d.x) > e ? 1 : 0)
                                        + (Mathf.Abs(d.y) > e ? 1 : 0)
                                        + (Mathf.Abs(d.z) > e ? 1 : 0);
                            if (nonZero != 1) continue;
                            if (DistanceToSegment(p, corners[a], corners[b]) < eps + 0.01f)
                            {
                                pinned = true;
                                break;
                            }
                        }
                    }
                    if (pinned) permanentPins.Add(i);
                }
                else
                {
                    bool onX = Mathf.Abs(p.x - outlineMin.x) < eps || Mathf.Abs(p.x - outlineMax.x) < eps;
                    bool onY = Mathf.Abs(p.y - outlineMin.y) < eps || Mathf.Abs(p.y - outlineMax.y) < eps;
                    if (onX || onY) permanentPins.Add(i);
                }
            }
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-12f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
            return Vector3.Distance(p, a + ab * t);
        }

        public void Pin(int vertexIndex)
        {
            pinnedVertex = vertexIndex;
            pinTarget = topology.vertices[vertexIndex].pos;
        }

        public void MovePin(Vector3 target)
        {
            if (pinnedVertex.HasValue && SkinMath.IsFinite(target))
            {
                Vector3 anchor = topology.vertices[pinnedVertex.Value].pos;
                if ((target - anchor).sqrMagnitude > 225f)
                    target = anchor + (target - anchor).normalized * 15f;
            }
            pinTarget = target;
        }

        public void Release()
        {
            pinnedVertex = null;
        }

        public void Step(int iterations = 20)
        {
            var verts = topology.vertices;
            int vertCount = verts.Count;
            if (posBuffer == null || posBuffer.Length != vertCount)
                posBuffer = new Vector3[vertCount];

            for (int i = 0; i < vertCount; i++)
                posBuffer[i] = verts[i].pos;

            int pin = -1;
            if (pinnedVertex.HasValue && SkinMath.IsFinite(pinTarget))
            {
                pin = pinnedVertex.Value;
                posBuffer[pin] = pinTarget;
            }

            float stiffness = Mathf.Clamp01(Stiffness);
            int springCount = springA.Length;

            var isPinned = pinnedFlags;
            if (isPinned == null || isPinned.Length != vertCount)
            {
                isPinned = new bool[vertCount];
                pinnedFlags = isPinned;
            }
            else
            {
                for (int i = 0; i < vertCount; i++) isPinned[i] = false;
            }
            foreach (int pi in permanentPins)
                if (pi >= 0 && pi < vertCount) isPinned[pi] = true;
            if (pin >= 0) isPinned[pin] = true;

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int s = 0; s < springCount; s++)
                {
                    int i1 = springA[s];
                    int i2 = springB[s];
                    if (i1 == i2) continue;

                    bool p1 = isPinned[i1];
                    bool p2 = isPinned[i2];
                    if (p1 && p2) continue;

                    Vector3 diff = posBuffer[i2] - posBuffer[i1];
                    float dist = diff.magnitude;
                    if (!(dist > 1e-10f) || !(dist < 1e9f)) continue;

                    float correction = (springRest[s] - dist) * stiffness;

                    if (p1)
                        posBuffer[i2] += diff * (correction / dist);
                    else if (p2)
                        posBuffer[i1] -= diff * (correction / dist);
                    else
                    {
                        float scale = correction / (dist * 2f);
                        Vector3 delta = diff * scale;
                        posBuffer[i1] -= delta;
                        posBuffer[i2] += delta;
                    }
                }

                for (int i = 0; i < vertCount; i++)
                {
                    if (!isPinned[i]) continue;
                    posBuffer[i] = i == pin ? pinTarget : verts[i].origPos;
                }
            }

            for (int i = 0; i < vertCount; i++)
                verts[i].pos = posBuffer[i];
        }
    }
}
