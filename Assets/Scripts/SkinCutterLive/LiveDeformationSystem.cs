using System.Collections.Generic;
using UnityEngine;

namespace SkinCutter.Live
{
    public class LiveDeformationSystem
    {
        private readonly LiveSkinTopology topology;

        private readonly List<int> springA = new List<int>();
        private readonly List<int> springB = new List<int>();
        private readonly List<float> springRest = new List<float>();
        private readonly List<EdgeRecInfo> edgeBuffer = new List<EdgeRecInfo>();

        private Vector3[] posBuffer;
        private bool[] pinnedFlags;

        private int? pinnedVertex;
        private Vector3 pinTarget;
        private readonly HashSet<int> permanentPins = new HashSet<int>();

        private Vector3 outlineMin;
        private Vector3 outlineMax;

        public float Stiffness { get; set; } = 1f;

        public LiveDeformationSystem(LiveSkinTopology topology)
        {
            this.topology = topology;
            ComputeOutline();
            ComputePermanentPins();
            RebuildSprings();
        }

        private void ComputeOutline()
        {
            var verts = topology.vertices;
            if (verts.Count == 0) { outlineMin = outlineMax = Vector3.zero; return; }
            Vector3 mn = verts[0].origPos;
            Vector3 mx = verts[0].origPos;
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
        }

        private void ComputePermanentPins()
        {
            permanentPins.Clear();
            var verts = topology.vertices;
            const float eps = 0.02f;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i].origPos;
                if (topology.isBox)
                {
                    if (IsOnBoxOutline(p, eps)) permanentPins.Add(i);
                }
                else
                {
                    bool onX = Mathf.Abs(p.x - outlineMin.x) < eps || Mathf.Abs(p.x - outlineMax.x) < eps;
                    bool onY = Mathf.Abs(p.y - outlineMin.y) < eps || Mathf.Abs(p.y - outlineMax.y) < eps;
                    if (onX || onY) permanentPins.Add(i);
                }
            }
        }

        private bool IsOnBoxOutline(Vector3 p, float eps)
        {
            Vector3[] corners =
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
            for (int a = 0; a < corners.Length; a++)
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
                        return true;
                }
            }
            return false;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-12f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Rebuild the spring list from the current active edges of the topology.</summary>
        public void RebuildSprings()
        {
            springA.Clear();
            springB.Clear();
            springRest.Clear();

            topology.GetActiveEdges(edgeBuffer);
            var verts = topology.vertices;
            for (int i = 0; i < edgeBuffer.Count; i++)
            {
                EdgeRecInfo e = edgeBuffer[i];
                springA.Add(e.a);
                springB.Add(e.b);
                springRest.Add(Vector3.Distance(verts[e.a].pos, verts[e.b].pos));
            }

            int vertCount = verts.Count;
            if (posBuffer == null || posBuffer.Length != vertCount)
                posBuffer = new Vector3[vertCount];
        }

        public void Pin(int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= topology.vertices.Count) return;
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

        public bool HasPin => pinnedVertex.HasValue;

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
                if (pin < 0 || pin >= vertCount) pin = -1;
            }
            if (pin >= 0) posBuffer[pin] = pinTarget;

            float stiffness = Mathf.Clamp01(Stiffness);
            int springCount = springA.Count;

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
