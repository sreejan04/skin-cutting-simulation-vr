using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkinCutter
{
    public enum ToolMode
    {
        Viewport,
        Grab,
        Knife
    }

    public enum GrabStyle
    {
        Ray,
        Direct
    }

    public class InteractionManager : MonoBehaviour
    {
        public ToolMode mode = ToolMode.Viewport;
        public GrabStyle grabStyle = GrabStyle.Direct;
        public bool IsDragging { get; private set; }
        public event Action<ToolMode> OnModeChange;

        private Camera cam;
        private OrbitCamera orbitCam;
        private SkinTopology topology;
        private SkinMesh skinMesh;
        private DeformationSystem deformation;

        private readonly List<Vector3> pathPoints = new List<Vector3>();
        private int? grabbedVertex;

        public void Initialize(Camera cameraRef, OrbitCamera orbitRef, SkinTopology topologyRef,
            SkinMesh meshRef, DeformationSystem deformationRef)
        {
            cam = cameraRef;
            orbitCam = orbitRef;
            topology = topologyRef;
            skinMesh = meshRef;
            deformation = deformationRef;
            SetMode(mode);
        }

        public void ResetState()
        {
            IsDragging = false;
            pathPoints.Clear();
            grabbedVertex = null;
            if (deformation != null) deformation.Release();
        }

        public void SetMode(ToolMode newMode)
        {
            mode = newMode;
            ResetState();
            if (orbitCam != null) orbitCam.SetToolModeActive(newMode != ToolMode.Viewport);
            OnModeChange?.Invoke(newMode);
        }

        public void CycleGrabStyle()
        {
            grabStyle = grabStyle == GrabStyle.Ray ? GrabStyle.Direct : GrabStyle.Ray;
        }

        // ---- Shared operations used by both the desktop (mouse) and VR input paths ----

        public bool RaycastMesh(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            if (topology == null) return false;
            if (!RayIntersectsBounds(ray)) return false;

            bool hitAny = false;
            float bestDist = float.MaxValue;

            var tris = topology.triangles;
            var verts = topology.vertices;

            for (int i = 0; i < tris.Count; i++)
            {
                Triangle t = tris[i];
                if (RayTriangle(ray, verts[t.v1].pos, verts[t.v2].pos, verts[t.v3].pos, out float dist)
                    && dist < bestDist)
                {
                    bestDist = dist;
                    point = ray.origin + ray.direction * dist;
                    hitAny = true;
                }
            }
            return hitAny;
        }

        public Vector3? NearestVertexWithin(Vector3 point, float radius)
        {
            int? v = FindClosestVertex(point, radius);
            if (!v.HasValue) return null;
            return topology.vertices[v.Value].pos;
        }

        public bool TryBeginGrab(Vector3 point, bool direct)
        {
            if (mode != ToolMode.Grab) return false;
            int? v = FindClosestVertex(point, direct ? 0.4f : 0.3f);
            if (!v.HasValue) return false;
            grabbedVertex = v.Value;
            deformation.Pin(v.Value);
            deformation.MovePin(point);
            IsDragging = true;
            return true;
        }

        public void UpdateGrab(Vector3 point)
        {
            if (grabbedVertex.HasValue) deformation.MovePin(point);
        }

        public void EndGrab()
        {
            grabbedVertex = null;
            deformation?.Release();
            IsDragging = false;
        }

        public bool TryBeginCut(Vector3 point)
        {
            if (mode != ToolMode.Knife) return false;
            pathPoints.Clear();
            pathPoints.Add(point);
            IsDragging = true;
            return true;
        }

        public void UpdateCut(Vector3 point)
        {
            if (pathPoints.Count == 0)
            {
                pathPoints.Add(point);
                return;
            }
            Vector3 last = pathPoints[pathPoints.Count - 1];
            if (Vector3.Distance(last, point) > 0.02f)
            {
                pathPoints.Add(point);
            }
        }

        public void EndCut()
        {
            IsDragging = false;
            if (pathPoints.Count < 2) { pathPoints.Clear(); return; }

            List<EdgeCrossing> crossings = topology.DetectCrossings(pathPoints);
            if (crossings.Count < 1)
            {
                pathPoints.Clear();
                return;
            }

            topology.ApplyCut(crossings);
            skinMesh.Rebuild(topology);
            deformation.RebuildSprings();
            pathPoints.Clear();
        }

        // ---- Desktop (mouse) path ----

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null || mode == ToolMode.Viewport) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TryGetMouse(out Vector3 p))
                {
                    if (mode == ToolMode.Knife) TryBeginCut(p);
                    else if (mode == ToolMode.Grab) TryBeginGrab(p, grabStyle == GrabStyle.Direct);
                }
            }

            if (IsDragging && mouse.leftButton.isPressed && TryGetMouse(out Vector3 cur))
            {
                if (mode == ToolMode.Knife) UpdateCut(cur);
                else if (mode == ToolMode.Grab) UpdateGrab(cur);
            }

            if (IsDragging && (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed))
            {
                if (mode == ToolMode.Knife) EndCut();
                else if (mode == ToolMode.Grab) EndGrab();
            }
        }

        private bool TryGetMouse(out Vector3 point)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return RaycastMesh(ray, out point);
        }

        private bool RayIntersectsBounds(Ray ray)
        {
            Vector3 min = topology.boundsMin;
            Vector3 max = topology.boundsMax;
            Vector3 dir = ray.direction;
            Vector3 orig = ray.origin;

            float tmin = 0f;
            float tmax = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float d = dir[axis];
                float o = orig[axis];
                float mn = min[axis];
                float mx = max[axis];

                if (Mathf.Abs(d) < 1e-10f)
                {
                    if (o < mn || o > mx) return false;
                    continue;
                }

                float invD = 1f / d;
                float t1 = (mn - o) * invD;
                float t2 = (mx - o) * invD;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmax < tmin) return false;
            }
            return true;
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float dist)
        {
            const float eps = 1e-8f;
            dist = 0f;

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 pvec = Vector3.Cross(ray.direction, ac);
            float det = Vector3.Dot(ab, pvec);
            if (Mathf.Abs(det) < eps) return false;

            float invDet = 1f / det;
            Vector3 tvec = ray.origin - a;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f) return false;

            Vector3 qvec = Vector3.Cross(tvec, ab);
            float v = Vector3.Dot(ray.direction, qvec) * invDet;
            if (v < 0f || u + v > 1f) return false;

            dist = Vector3.Dot(ac, qvec) * invDet;
            return dist > eps;
        }

        private int? FindClosestVertex(Vector3 point, float maxDist)
        {
            int? closest = null;
            float minDist = maxDist;
            for (int i = 0; i < topology.vertices.Count; i++)
            {
                float d = Vector3.Distance(topology.vertices[i].pos, point);
                if (!(d < minDist)) continue;
                minDist = d;
                closest = i;
            }
            return closest;
        }
    }
}
