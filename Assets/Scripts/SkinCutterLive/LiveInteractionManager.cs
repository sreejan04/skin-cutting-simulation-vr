using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SkinCutter.Live
{
    public enum LiveToolMode
    {
        Viewport,
        Grab,
        Knife
    }

    public enum LiveGrabStyle
    {
        Ray,
        Direct
    }

    public class LiveInteractionManager : MonoBehaviour
    {
        public LiveToolMode mode = LiveToolMode.Grab;
        public LiveGrabStyle grabStyle = LiveGrabStyle.Direct;

        public bool IsDragging { get; private set; }
        public bool IsCutting { get; private set; }

        private Camera cam;
        private OrbitCamera orbitCam;
        private LiveSkinTopology topology;
        private LiveSkinMesh skinMesh;
        private LiveDeformationSystem deformation;

        private int? grabbedVertex;
        private Vector3 lastCutPoint;
        private bool hasLastCutPoint;
        private float normalRecalcTimer;

        private const float NormalRecalcInterval = 0.1f;

        private readonly List<Vector3> trailPoints = new List<Vector3>();
        private readonly List<int> newVerts = new List<int>();
        private LineRenderer trail;

        private const int MaxTrailPoints = 256;

        public void Initialize(Camera cameraRef, LiveSkinTopology topologyRef,
            LiveSkinMesh meshRef, LiveDeformationSystem deformationRef)
        {
            Initialize(cameraRef, null, topologyRef, meshRef, deformationRef);
        }

        public void Initialize(Camera cameraRef, OrbitCamera orbitRef, LiveSkinTopology topologyRef,
            LiveSkinMesh meshRef, LiveDeformationSystem deformationRef)
        {
            cam = cameraRef;
            orbitCam = orbitRef;
            topology = topologyRef;
            skinMesh = meshRef;
            deformation = deformationRef;
            if (trail == null) CreateTrail();
            SetMode(mode);
        }

        private void CreateTrail()
        {
            var go = new GameObject("KnifeTrail");
            go.transform.SetParent(transform, false);
            trail = go.AddComponent<LineRenderer>();
            trail.useWorldSpace = true;
            trail.widthMultiplier = 0.03f;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.positionCount = 0;
            trail.enabled = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "KnifeTrailMat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.25f, 0.25f, 1f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(1f, 0.25f, 0.25f, 1f));
            trail.material = mat;
        }

        public void ResetState()
        {
            IsDragging = false;
            IsCutting = false;
            grabbedVertex = null;
            hasLastCutPoint = false;
            normalRecalcTimer = 0f;
            trailPoints.Clear();
            if (trail != null) { trail.positionCount = 0; trail.enabled = false; }
            deformation?.Release();
        }

        public void SetMode(LiveToolMode newMode)
        {
            mode = newMode;
            ResetState();
            if (orbitCam != null) orbitCam.SetToolModeActive(newMode != LiveToolMode.Viewport);
        }

        public void CycleGrabStyle()
        {
            grabStyle = grabStyle == LiveGrabStyle.Ray ? LiveGrabStyle.Direct : LiveGrabStyle.Ray;
        }


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

        public bool ClosestPointOnMesh(Vector3 p, float maxDist, out Vector3 closest)
        {
            closest = Vector3.zero;
            if (topology == null) return false;

            float bestSq = maxDist * maxDist;
            bool found = false;

            var tris = topology.triangles;
            var verts = topology.vertices;

            for (int i = 0; i < tris.Count; i++)
            {
                Triangle t = tris[i];
                Vector3 cp = ClosestPointOnTriangle(p, verts[t.v1].pos, verts[t.v2].pos, verts[t.v3].pos);
                float d = (cp - p).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                closest = cp;
                found = true;
            }
            return found;
        }

        public bool TryGetSurfacePoint(Vector3 origin, Vector3 dir, float probeRadius, out Vector3 point)
        {
            if (RaycastMesh(new Ray(origin, dir), out point)) return true;
            return ClosestPointOnMesh(origin, probeRadius, out point);
        }

        public bool TryGetCutPoint(Vector3 origin, Vector3 dir, float probeRadius, out Vector3 point)
        {
            if (ClosestPointOnMesh(origin, probeRadius, out point)) return true;
            return RaycastMesh(new Ray(origin, dir), out point);
        }

        public bool TryBeginGrab(Vector3 point, bool direct)
        {
            if (mode != LiveToolMode.Grab) return false;
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

        public bool BeginCut(Vector3 point)
        {
            if (mode != LiveToolMode.Knife) return false;
            topology.ResetCutTracking();
            trailPoints.Clear();
            trailPoints.Add(point);
            lastCutPoint = point;
            hasLastCutPoint = true;
            IsDragging = true;
            IsCutting = true;
            if (trail != null) trail.enabled = true;
            UpdateTrail();
            return true;
        }

        public void UpdateCut(Vector3 point)
        {
            if (!IsCutting) return;

            if (hasLastCutPoint && (point - lastCutPoint).sqrMagnitude > 1e-6f)
            {
                if (topology.CutSegment(lastCutPoint, point, newVerts))
                {
                    deformation.RebuildSprings();
                    normalRecalcTimer += Time.deltaTime;
                    bool recalcNormals = normalRecalcTimer >= NormalRecalcInterval;
                    if (recalcNormals) normalRecalcTimer = 0f;
                    skinMesh.Upload(true, recalcNormals);
                }
                lastCutPoint = point;
            }

            trailPoints.Add(point);
            if (trailPoints.Count > MaxTrailPoints)
                trailPoints.RemoveAt(0);
            UpdateTrail();
        }

        public void EndCut()
        {
            IsCutting = false;
            IsDragging = false;
            hasLastCutPoint = false;
            normalRecalcTimer = 0f;
            trailPoints.Clear();
            if (trail != null) { trail.positionCount = 0; trail.enabled = false; }
        }

        private void UpdateTrail()
        {
            if (trail == null) return;
            trail.positionCount = trailPoints.Count;
            for (int i = 0; i < trailPoints.Count; i++)
                trail.SetPosition(i, trailPoints[i] + Vector3.forward * 0.002f);
        }


        private void Update()
        {
            if (XRSettings.enabled) return;
            var mouse = Mouse.current;
            if (mouse == null || cam == null || mode == LiveToolMode.Viewport) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (TryGetMouse(out Vector3 p))
                {
                    if (mode == LiveToolMode.Knife) BeginCut(p);
                    else TryBeginGrab(p, grabStyle == LiveGrabStyle.Direct);
                }
            }

            if (IsDragging && mouse.leftButton.isPressed && TryGetMouse(out Vector3 cur))
            {
                if (mode == LiveToolMode.Knife) UpdateCut(cur);
                else UpdateGrab(cur);
            }

            if (IsDragging && (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed))
            {
                if (mode == LiveToolMode.Knife) EndCut();
                else EndGrab();
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

        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + ab * v;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + ac * w;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + (c - b) * w;
            }

            float denom = 1f / (va + vb + vc);
            float vv = vb * denom;
            float ww = vc * denom;
            return a + ab * vv + ac * ww;
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
