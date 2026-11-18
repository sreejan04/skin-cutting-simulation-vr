using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinCutting
{
    public struct SkinConstraint
    {
        public int A;
        public int B;
        public float RestLength;
        public float Stiffness;
        public bool IsBend;

        public SkinConstraint(int a, int b, float restLength, float stiffness, bool isBend)
        {
            A = a;
            B = b;
            RestLength = restLength;
            Stiffness = stiffness;
            IsBend = isBend;
        }
    }

    public struct SkinRaycastHit
    {
        public int TriangleIndex;
        public Vector3 LocalPoint;
        public Vector3 LocalNormal;
        public Vector3 WorldPoint;
        public Vector3 WorldNormal;
        public float Distance;
    }

    [DisallowMultipleComponent]
    public sealed class SkinMeshData : MonoBehaviour
    {
        public float Width = 4f;
        public float Height = 4f;
        public int GridX = 48;
        public int GridY = 48;
        public float Gravity = -2f;
        public int Substeps = 1;
        public int SolverIterations = 14;
        public int BendIterations = 7;
        public float StretchStiffness = 1f;
        public float BendStiffness = 1f;
        public float Damping = 0.96f;
        public float AttachmentStrength = 0.18f;
        public float MaxStretchRatio = 1.05f;
        public int StrainLimitIterations = 2;
        public int MaxSimulationSteps = 2;
        public int GrabSolverIterations = 14;
        public int GrabBendIterations = 7;
        public float GrabResponse = 35f;
        public float GrabHandleRadiusScale = 0.45f;
        public float GrabSoftRadiusScale = 4f;
        public float GrabMaxSoftRadius = 1.75f;
        public float GrabTetherReduction = 1f;
        public float GrabStiffness = 500f;
        public float GrabMaxSpeed = 15f;
        public float GrabMaxStretchRatio = 1.04f;
        public int GrabStrainLimitIterations = 6;

        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> previousPositions = new List<Vector3>();
        private readonly List<Vector3> velocities = new List<Vector3>();
        private readonly List<Vector3> restPositions = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<float> invMasses = new List<float>();
        private readonly List<bool> permanentPins = new List<bool>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<bool> triangleWallFlags = new List<bool>();
        private readonly List<int> surfaceTriangles = new List<int>();
        private readonly List<int> wallTriangles = new List<int>();
        private readonly List<SkinConstraint> constraints = new List<SkinConstraint>();

        private readonly Dictionary<long, float> stretchRestLengths = new Dictionary<long, float>();
        private readonly Dictionary<long, float> bendRestLengths = new Dictionary<long, float>();

        private Mesh mesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private float accumulator;
        private Vector3 grabCenter;
        private Vector3 grabTarget;
        private Vector3 grabTargetDesired;
        private Vector3 grabCurrentOffset;
        private readonly List<int> grabIndices = new List<int>();
        private readonly List<float> grabWeights = new List<float>();
        private readonly List<float> grabOriginalInvMasses = new List<float>();
        private readonly List<Vector3> grabRestPositions = new List<Vector3>();
        private readonly List<int> grabSoftIndices = new List<int>();
        private readonly List<float> grabSoftWeights = new List<float>();
        private readonly List<Vector3> grabSoftRestPositions = new List<Vector3>();
        private readonly List<bool> wallFollowers = new List<bool>();
        private readonly List<int> wallParents = new List<int>();
        private readonly List<int> wallChildren = new List<int>();
        private readonly List<Vector3> wallOffsets = new List<Vector3>();
        private bool[] grabMask;
        private float[] grabInfluence;

        public IReadOnlyList<Vector3> Positions => positions;
        public IReadOnlyList<Vector3> Velocities => velocities;
        public IReadOnlyList<Vector2> Uvs => uvs;
        public IReadOnlyList<int> Triangles => triangles;
        public IReadOnlyList<SkinConstraint> Constraints => constraints;
        public int VertexCount => positions.Count;
        public int TriangleCount => triangles.Count / 3;
        public int ConstraintCount => constraints.Count;
        public int LastScopedConstraintRemovals { get; private set; }
        public int CutSurfaceVersion { get; private set; }
        public bool IsGrabbing => grabIndices.Count > 0 || grabSoftIndices.Count > 0;
        public Vector3 GrabLocalPoint => IsGrabbing ? grabCenter : Vector3.zero;

        public void Initialize(float width, float height, int gridX, int gridY)
        {
            Width = Mathf.Max(0.1f, width);
            Height = Mathf.Max(0.1f, height);
            GridX = Mathf.Max(2, gridX);
            GridY = Mathf.Max(2, gridY);
            BuildFlatSheet();
        }

        public void BuildFlatSheet()
        {
            EndGrab();
            positions.Clear();
            previousPositions.Clear();
            velocities.Clear();
            restPositions.Clear();
            uvs.Clear();
            invMasses.Clear();
            permanentPins.Clear();
            triangles.Clear();
            triangleWallFlags.Clear();
            constraints.Clear();
            stretchRestLengths.Clear();
            bendRestLengths.Clear();
            wallFollowers.Clear();
            wallParents.Clear();
            wallChildren.Clear();
            wallOffsets.Clear();
            accumulator = 0f;

            int columns = GridX + 1;
            int rows = GridY + 1;

            for (int y = 0; y < rows; y++)
            {
                float v = y / (float)GridY;
                for (int x = 0; x < columns; x++)
                {
                    float u = x / (float)GridX;
                    Vector3 position = new Vector3(
                        (u - 0.5f) * Width,
                        (v - 0.5f) * Height,
                        0f);

                    bool pinned = x == 0 || y == 0 || x == GridX || y == GridY;
                    positions.Add(position);
                    previousPositions.Add(position);
                    velocities.Add(Vector3.zero);
                    restPositions.Add(position);
                    uvs.Add(new Vector2(u, v));
                    invMasses.Add(pinned ? 0f : 1f);
                    permanentPins.Add(pinned);
                    wallFollowers.Add(false);
                }
            }

            for (int y = 0; y < GridY; y++)
            {
                for (int x = 0; x < GridX; x++)
                {
                    int i0 = y * columns + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + columns;
                    int i3 = i2 + 1;

                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    triangles.Add(i1);
                    triangles.Add(i3);
                    triangles.Add(i2);
                }
            }

            for (int i = 0; i < triangles.Count / 3; i++)
            {
                triangleWallFlags.Add(false);
            }

            EnsureMeshComponents();
            mesh.Clear();
            RebuildConstraints();
            UploadMesh();
            CutSurfaceVersion++;
        }

        public void RebuildConstraints()
        {
            constraints.Clear();
            Dictionary<long, float> nextStretchRest = new Dictionary<long, float>();
            Dictionary<long, float> nextBendRest = new Dictionary<long, float>();
            Dictionary<long, int> edgeOpposite = new Dictionary<long, int>();

            for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
            {
                int i0 = triangles[triangle];
                int i1 = triangles[triangle + 1];
                int i2 = triangles[triangle + 2];

                AddEdgeConstraint(i0, i1, i2, nextStretchRest, nextBendRest, edgeOpposite);
                AddEdgeConstraint(i1, i2, i0, nextStretchRest, nextBendRest, edgeOpposite);
                AddEdgeConstraint(i2, i0, i1, nextStretchRest, nextBendRest, edgeOpposite);
            }

            stretchRestLengths.Clear();
            foreach (KeyValuePair<long, float> pair in nextStretchRest)
            {
                stretchRestLengths[pair.Key] = pair.Value;
            }

            bendRestLengths.Clear();
            foreach (KeyValuePair<long, float> pair in nextBendRest)
            {
                bendRestLengths[pair.Key] = pair.Value;
            }
        }

        private void AddEdgeConstraint(
            int a,
            int b,
            int opposite,
            Dictionary<long, float> nextStretchRest,
            Dictionary<long, float> nextBendRest,
            Dictionary<long, int> edgeOpposite)
        {
            if (IsWallFollower(a) || IsWallFollower(b) || IsWallFollower(opposite))
            {
                return;
            }

            long edgeKey = EdgeKey(a, b);
            if (!nextStretchRest.ContainsKey(edgeKey))
            {
                float currentLength = Vector3.Distance(positions[a], positions[b]);
                if (currentLength < 1e-7f)
                {
                    return;
                }

                float restLength;
                if (!stretchRestLengths.TryGetValue(edgeKey, out restLength))
                {
                    restLength = currentLength;
                }

                nextStretchRest[edgeKey] = restLength;
                constraints.Add(new SkinConstraint(a, b, restLength, StretchStiffness, false));
            }

            int previousOpposite;
            if (edgeOpposite.TryGetValue(edgeKey, out previousOpposite))
            {
                long bendKey = PairKey(previousOpposite, opposite);
                if (!nextBendRest.ContainsKey(bendKey))
                {
                    float currentLength = Vector3.Distance(positions[previousOpposite], positions[opposite]);
                    if (currentLength < 1e-7f)
                    {
                        return;
                    }

                    float restLength;
                    if (!bendRestLengths.TryGetValue(bendKey, out restLength))
                    {
                        restLength = currentLength;
                    }

                    nextBendRest[bendKey] = restLength;
                    constraints.Add(new SkinConstraint(previousOpposite, opposite, restLength, BendStiffness, true));
                }
            }
            else
            {
                edgeOpposite[edgeKey] = opposite;
            }
        }

        public void ReplaceTriangles(List<int> newTriangles)
        {
            ReplaceTrianglesDeferred(newTriangles);
            FinalizeTopology();
        }

        public void ReplaceTriangles(
            List<int> newTriangles,
            IReadOnlyList<bool> wallFlags,
            IReadOnlyList<int> touchedTriangles)
        {
            LastScopedConstraintRemovals = RemoveConstraintsForTouchedTriangles(
                touchedTriangles, newTriangles);
            ReplaceTrianglesDeferred(newTriangles, wallFlags);
            FinalizeTopology();
        }

        public void ReplaceTrianglesDeferred(List<int> newTriangles)
        {
            triangles.Clear();
            triangles.AddRange(newTriangles);
            triangleWallFlags.Clear();
            for (int triangle = 0; triangle < triangles.Count / 3; triangle++)
            {
                int a = triangles[triangle * 3];
                int b = triangles[triangle * 3 + 1];
                int c = triangles[triangle * 3 + 2];
                triangleWallFlags.Add(IsWallFollower(a) || IsWallFollower(b) || IsWallFollower(c));
            }
        }

        public void ReplaceTrianglesDeferred(
            List<int> newTriangles,
            IReadOnlyList<bool> wallFlags,
            IReadOnlyList<int> touchedTriangles)
        {
            LastScopedConstraintRemovals = 0;
            ReplaceTrianglesDeferred(newTriangles, wallFlags);
        }

        public void ReplaceTrianglesDeferred(
            List<int> newTriangles,
            IReadOnlyList<bool> wallFlags)
        {
            triangles.Clear();
            triangles.AddRange(newTriangles);
            triangleWallFlags.Clear();
            int triangleCount = triangles.Count / 3;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                bool isWall = wallFlags != null && triangle < wallFlags.Count && wallFlags[triangle];
                triangleWallFlags.Add(isWall);
            }
        }

        public bool IsWallTriangle(int triangleIndex)
        {
            return triangleIndex >= 0 &&
                   triangleIndex < triangleWallFlags.Count &&
                   triangleWallFlags[triangleIndex];
        }

        private int RemoveConstraintsForTouchedTriangles(
            IReadOnlyList<int> touchedTriangles,
            IReadOnlyList<int> newTriangles)
        {
            LastScopedConstraintRemovals = 0;
            if (touchedTriangles == null || touchedTriangles.Count == 0 || newTriangles == null)
            {
                return 0;
            }

            HashSet<long> retainedEdges = new HashSet<long>();
            for (int triangle = 0; triangle + 2 < newTriangles.Count; triangle += 3)
            {
                retainedEdges.Add(EdgeKey(newTriangles[triangle], newTriangles[triangle + 1]));
                retainedEdges.Add(EdgeKey(newTriangles[triangle + 1], newTriangles[triangle + 2]));
                retainedEdges.Add(EdgeKey(newTriangles[triangle + 2], newTriangles[triangle]));
            }

            HashSet<long> touchedEdges = new HashSet<long>();
            for (int i = 0; i < touchedTriangles.Count; i++)
            {
                int triangle = touchedTriangles[i] * 3;
                if (triangle + 2 >= triangles.Count)
                {
                    continue;
                }

                touchedEdges.Add(EdgeKey(triangles[triangle], triangles[triangle + 1]));
                touchedEdges.Add(EdgeKey(triangles[triangle + 1], triangles[triangle + 2]));
                touchedEdges.Add(EdgeKey(triangles[triangle + 2], triangles[triangle]));
            }

            int removed = 0;
            foreach (long key in touchedEdges)
            {
                if (!retainedEdges.Contains(key))
                {
                    removed++;
                }
            }

            return removed;
        }

        public void FinalizeTopology()
        {
            RebuildConstraints();
            UploadMesh();
            CutSurfaceVersion++;
        }

        public int AddVertex(Vector3 position, Vector3 velocity, Vector2 uv, bool pinned)
        {
            int index = positions.Count;
            positions.Add(position);
            previousPositions.Add(position);
            velocities.Add(velocity);
            restPositions.Add(position);
            uvs.Add(uv);
            invMasses.Add(pinned ? 0f : 1f);
            permanentPins.Add(pinned);
            wallFollowers.Add(false);
            return index;
        }

        public void RegisterWallFollower(int topIndex, int bottomIndex, Vector3 offset)
        {
            if (topIndex < 0 || topIndex >= positions.Count ||
                bottomIndex < 0 || bottomIndex >= positions.Count ||
                wallFollowers[bottomIndex])
            {
                return;
            }

            wallFollowers[bottomIndex] = true;
            wallParents.Add(topIndex);
            wallChildren.Add(bottomIndex);
            wallOffsets.Add(offset);
        }

        public bool IsWallFollower(int index)
        {
            return index >= 0 && index < wallFollowers.Count && wallFollowers[index];
        }

        public int AddMidpoint(int a, int b)
        {
            Vector3 point = (positions[a] + positions[b]) * 0.5f;
            Vector2 uv = (uvs[a] + uvs[b]) * 0.5f;
            Vector3 velocity = (velocities[a] + velocities[b]) * 0.5f;
            bool pinned = permanentPins[a] && permanentPins[b];
            return AddVertex(point, velocity, uv, pinned);
        }

        public bool IsPermanentlyPinned(int index)
        {
            return index >= 0 && index < permanentPins.Count && permanentPins[index];
        }

        public float GetInvMass(int index)
        {
            return index >= 0 && index < invMasses.Count ? invMasses[index] : 0f;
        }

        public Vector3 GetTriangleNormal(int triangleIndex)
        {
            int baseIndex = triangleIndex * 3;
            if (baseIndex + 2 >= triangles.Count)
            {
                return Vector3.forward;
            }

            Vector3 a = positions[triangles[baseIndex]];
            Vector3 b = positions[triangles[baseIndex + 1]];
            Vector3 c = positions[triangles[baseIndex + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            return normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.forward;
        }

        public void Simulate(float deltaTime)
        {
            if (positions.Count == 0 || triangles.Count == 0)
            {
                return;
            }

            float safeDelta = Mathf.Clamp(deltaTime, 0f, 0.1f);
            accumulator += safeDelta;
            const float fixedStep = 1f / 60f;
            int maxSteps = Mathf.Clamp(MaxSimulationSteps, 1, 8);
            int substeps = Mathf.Clamp(Substeps, 1, 8);
            float substepDelta = fixedStep / substeps;
            int steps = 0;

            while (accumulator >= fixedStep && steps < maxSteps)
            {
                for (int substep = 0; substep < substeps; substep++)
                {
                    Step(substepDelta);
                }
                accumulator -= fixedStep;
                steps++;
            }

            if (steps >= maxSteps)
            {
                accumulator = 0f;
            }

            if (steps > 0)
            {
                UploadDeformedMesh();
            }
        }

        private void Step(float dt)
        {
            bool hasGrab = IsGrabbing;
            if (hasGrab)
            {
                float alpha = 1f - Mathf.Exp(-Mathf.Max(1f, GrabResponse) * dt);
                grabTarget = Vector3.Lerp(grabTarget, grabTargetDesired, alpha);
            }

            ApplyGrabTargets(dt);

            Vector3 gravity = new Vector3(0f, Gravity, 0f);
            int grabMaskLength = grabMask != null ? grabMask.Length : 0;
            float damping = Mathf.Pow(Mathf.Clamp(Damping, 0.001f, 1f), dt * 60f);

            for (int i = 0; i < positions.Count; i++)
            {
                if (hasGrab && i < grabMaskLength && grabMask[i])
                {
                    continue;
                }

                previousPositions[i] = positions[i];
                float invMass = invMasses[i];
                if (invMass <= 0f)
                {
                    velocities[i] = Vector3.zero;
                    continue;
                }

                velocities[i] += gravity * dt;
                velocities[i] *= damping;
                positions[i] += velocities[i] * dt;
            }

            ApplyAttachments(dt);

            int iterations = Mathf.Max(1, hasGrab ? Mathf.Max(SolverIterations, GrabSolverIterations) : SolverIterations);
            int requestedBendIterations = hasGrab ? Mathf.Max(BendIterations, GrabBendIterations) : BendIterations;
            int bendIterations = Mathf.Clamp(requestedBendIterations, 0, iterations);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool solveBend = iteration < bendIterations;
                for (int c = 0; c < constraints.Count; c++)
                {
                    SkinConstraint constraint = constraints[c];
                    if (constraint.IsBend && !solveBend)
                    {
                        continue;
                    }

                    float wa = invMasses[constraint.A];
                    float wb = invMasses[constraint.B];
                    float totalWeight = wa + wb;
                    if (totalWeight <= 0f)
                    {
                        continue;
                    }

                    Vector3 positionA = positions[constraint.A];
                    Vector3 positionB = positions[constraint.B];
                    Vector3 delta = positionB - positionA;
                    float length = delta.magnitude;
                    if (length < 1e-8f)
                    {
                        continue;
                    }

                    float correction = ((length - constraint.RestLength) /
                                        length) * constraint.Stiffness;
                    Vector3 scaledDelta = delta * correction;
                    positions[constraint.A] = positionA + scaledDelta * (wa / totalWeight);
                    positions[constraint.B] = positionB - scaledDelta * (wb / totalWeight);
                }

            }

            ApplyStrainLimits(hasGrab);
            ApplyWallFollowers();

            for (int i = 0; i < positions.Count; i++)
            {
                if (IsWallFollower(i))
                {
                    velocities[i] = Vector3.zero;
                }
                else if (hasGrab && i < grabMaskLength && grabMask[i])
                {
                    velocities[i] = (positions[i] - previousPositions[i]) / dt;
                }
                else if (invMasses[i] <= 0f)
                {
                    velocities[i] = Vector3.zero;
                }
                else
                {
                    velocities[i] = (positions[i] - previousPositions[i]) / dt;
                }
            }
        }

        private void ApplyWallFollowers()
        {
            for (int i = 0; i < wallChildren.Count; i++)
            {
                int parent = wallParents[i];
                int child = wallChildren[i];
                if (parent < 0 || parent >= positions.Count ||
                    child < 0 || child >= positions.Count)
                {
                    continue;
                }

                positions[child] = positions[parent] + wallOffsets[i];
                previousPositions[child] = positions[child];
            }
        }

        private void ApplyStrainLimits(bool hasGrab)
        {
            float maxStretch = hasGrab
                ? Mathf.Max(1f, GrabMaxStretchRatio)
                : Mathf.Max(1f, MaxStretchRatio);
            int iterations = Mathf.Max(
                StrainLimitIterations,
                hasGrab ? GrabStrainLimitIterations : 0);
            if (maxStretch <= 1.0001f || iterations <= 0)
            {
                return;
            }

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool corrected = false;
                for (int c = 0; c < constraints.Count; c++)
                {
                    SkinConstraint constraint = constraints[c];
                    if (constraint.IsBend || constraint.RestLength < 1e-6f)
                    {
                        continue;
                    }

                    float wa = invMasses[constraint.A];
                    float wb = invMasses[constraint.B];
                    float totalWeight = wa + wb;
                    if (totalWeight <= 0f)
                    {
                        continue;
                    }

                    Vector3 positionA = positions[constraint.A];
                    Vector3 positionB = positions[constraint.B];
                    Vector3 delta = positionB - positionA;
                    float length = delta.magnitude;
                    float limit = constraint.RestLength * maxStretch;
                    if (length <= limit || length < 1e-8f)
                    {
                        continue;
                    }

                    float correction = (length - limit) / length;
                    Vector3 scaledDelta = delta * correction;
                    positions[constraint.A] = positionA + scaledDelta * (wa / totalWeight);
                    positions[constraint.B] = positionB - scaledDelta * (wb / totalWeight);
                    corrected = true;
                }

                if (!corrected)
                {
                    break;
                }
            }
        }

        private void ApplyAttachments(float dt)
        {
            float strength = 1f - Mathf.Exp(-Mathf.Max(0f, AttachmentStrength) * 60f * dt);
            if (strength <= 0f)
            {
                return;
            }

            bool hasGrab = IsGrabbing && grabInfluence != null;
            int influenceLength = grabInfluence != null ? grabInfluence.Length : 0;
            float tetherReduction = Mathf.Clamp01(GrabTetherReduction);

            for (int i = 0; i < positions.Count; i++)
            {
                if (invMasses[i] <= 0f)
                {
                    continue;
                }

                float attachmentScale = 1f;
                if (hasGrab && i < influenceLength)
                {
                    attachmentScale = 1f - tetherReduction * grabInfluence[i];
                }

                positions[i] += (restPositions[i] - positions[i]) * (strength * attachmentScale);
            }
        }

        private void ApplyGrabTargets(float dt)
        {
            if (!IsGrabbing)
            {
                return;
            }

            float blend = 1f - Mathf.Exp(-Mathf.Max(1f, GrabStiffness) * dt);
            Vector3 targetOffset = grabTarget - grabCenter;
            float maxStep = Mathf.Max(0f, GrabMaxSpeed) * dt;
            grabCurrentOffset = maxStep > 0f
                ? Vector3.MoveTowards(grabCurrentOffset, targetOffset, maxStep)
                : targetOffset;
            Vector3 delta = grabCurrentOffset;
            for (int i = 0; i < grabIndices.Count; i++)
            {
                int index = grabIndices[i];
                previousPositions[index] = positions[index];
                positions[index] = grabRestPositions[i] + delta;
            }

            for (int i = 0; i < grabSoftIndices.Count; i++)
            {
                int index = grabSoftIndices[i];
                Vector3 target = grabSoftRestPositions[i] + delta * grabSoftWeights[i];
                previousPositions[index] = positions[index];
                positions[index] = Vector3.Lerp(positions[index], target, blend);
            }
        }

        public bool Raycast(Ray worldRay, out SkinRaycastHit hit)
        {
            hit = default(SkinRaycastHit);
            if (triangles.Count == 0)
            {
                return false;
            }

            Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDirection = transform.InverseTransformDirection(worldRay.direction);
            if (localDirection.sqrMagnitude < 1e-10f)
            {
                return false;
            }
            localDirection.Normalize();

            float bestDistance = float.PositiveInfinity;
            int bestTriangle = -1;
            Vector3 bestNormal = Vector3.forward;

            for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
            {
                Vector3 a = positions[triangles[triangle]];
                Vector3 b = positions[triangles[triangle + 1]];
                Vector3 c = positions[triangles[triangle + 2]];

                float distance;
                if (!RayTriangle(localOrigin, localDirection, a, b, c, out distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTriangle = triangle;
                    bestNormal = Vector3.Cross(b - a, c - a);
                }
            }

            if (bestTriangle < 0)
            {
                return false;
            }

            if (bestNormal.sqrMagnitude < 1e-12f)
            {
                bestNormal = Vector3.forward;
            }
            else
            {
                bestNormal.Normalize();
            }

            if (Vector3.Dot(bestNormal, localDirection) > 0f)
            {
                bestNormal = -bestNormal;
            }

            Vector3 localPoint = localOrigin + localDirection * bestDistance;
            hit.TriangleIndex = bestTriangle / 3;
            hit.LocalPoint = localPoint;
            hit.LocalNormal = bestNormal;
            hit.WorldPoint = transform.TransformPoint(localPoint);
            hit.WorldNormal = transform.TransformDirection(bestNormal).normalized;
            hit.Distance = bestDistance;
            return true;
        }

        private static bool RayTriangle(
            Vector3 origin,
            Vector3 direction,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f)
            {
                return false;
            }

            float invDeterminant = 1f / determinant;
            Vector3 t = origin - a;
            float u = Vector3.Dot(t, p) * invDeterminant;
            if (u < -1e-5f || u > 1f + 1e-5f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) * invDeterminant;
            if (v < -1e-5f || u + v > 1f + 1e-5f)
            {
                return false;
            }

            distance = Vector3.Dot(edge2, q) * invDeterminant;
            return distance > 1e-5f;
        }

        public int FindClosestFreeVertex(Vector3 localPoint, float radius)
        {
            int bestIndex = -1;
            float bestSqrDistance = radius * radius;

            for (int i = 0; i < positions.Count; i++)
            {
                if (invMasses[i] <= 0f)
                {
                    continue;
                }

                float sqrDistance = (positions[i] - localPoint).sqrMagnitude;
                if (sqrDistance <= bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        public bool BeginGrab(Vector3 localPoint, float radius, int triangleIndex = -1)
        {
            EndGrab();

            int seed = FindGrabSeed(localPoint, radius, triangleIndex);
            if (seed < 0)
            {
                return false;
            }

            float safeRadius = Mathf.Max(1e-4f, radius);
            float handleRadius = safeRadius * Mathf.Clamp01(GrabHandleRadiusScale);
            float softRadius = safeRadius * Mathf.Max(1f, GrabSoftRadiusScale);
            if (GrabMaxSoftRadius > 0f)
            {
                softRadius = Mathf.Min(softRadius, Mathf.Max(handleRadius, GrabMaxSoftRadius));
            }
            float falloffRange = Mathf.Max(1e-4f, softRadius - handleRadius);
            float maxDistance = softRadius * 1.001f;

            grabCenter = localPoint;
            grabTarget = grabCenter;
            grabTargetDesired = grabCenter;
            grabCurrentOffset = Vector3.zero;
            grabMask = new bool[positions.Count];
            grabInfluence = new float[positions.Count];

            BuildGrabNeighborTable(out int[] edgeOffsets, out int[] edgeNeighbors);
            float[] distances = ComputeGrabDistances(seed, edgeOffsets, edgeNeighbors, maxDistance);

            for (int i = 0; i < positions.Count; i++)
            {
                if (invMasses[i] <= 0f)
                {
                    continue;
                }

                if (distances[i] <= handleRadius + 1e-5f)
                {
                    PinGrabVertex(i);
                }
            }

            if (grabIndices.Count == 0)
            {
                PinGrabVertex(seed);
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if (invMasses[i] <= 0f || grabMask[i])
                {
                    continue;
                }

                float distance = distances[i];
                if (distance > softRadius)
                {
                    continue;
                }

                float t = Mathf.Clamp01(1f - (distance - handleRadius) / falloffRange);
                float weight = t * t * (3f - 2f * t);
                if (weight <= 0.02f)
                {
                    continue;
                }

                grabSoftIndices.Add(i);
                grabSoftWeights.Add(weight);
                grabSoftRestPositions.Add(positions[i]);
                grabMask[i] = true;
                grabInfluence[i] = weight;
            }

            if (grabIndices.Count == 0 && grabSoftIndices.Count == 0)
            {
                grabMask = null;
                grabInfluence = null;
                return false;
            }

            return true;
        }

        private int FindGrabSeed(Vector3 localPoint, float radius, int triangleIndex)
        {
            if (triangleIndex >= 0 && triangleIndex * 3 + 2 < triangles.Count)
            {
                int best = -1;
                float bestSqrDistance = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    int index = triangles[triangleIndex * 3 + i];
                    if (invMasses[index] <= 0f)
                    {
                        continue;
                    }

                    float sqrDistance = (positions[index] - localPoint).sqrMagnitude;
                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        best = index;
                    }
                }

                if (best >= 0)
                {
                    return best;
                }
            }

            return FindClosestFreeVertex(localPoint, radius);
        }

        private void BuildGrabNeighborTable(out int[] edgeOffsets, out int[] edgeNeighbors)
        {
            int count = positions.Count;
            edgeOffsets = new int[count + 1];
            for (int i = 0; i < constraints.Count; i++)
            {
                SkinConstraint constraint = constraints[i];
                if (constraint.IsBend)
                {
                    continue;
                }

                edgeOffsets[constraint.A + 1]++;
                edgeOffsets[constraint.B + 1]++;
            }

            for (int i = 0; i < count; i++)
            {
                edgeOffsets[i + 1] += edgeOffsets[i];
            }

            edgeNeighbors = new int[edgeOffsets[count]];
            int[] cursor = new int[count];
            Array.Copy(edgeOffsets, cursor, count);
            for (int i = 0; i < constraints.Count; i++)
            {
                SkinConstraint constraint = constraints[i];
                if (constraint.IsBend)
                {
                    continue;
                }

                edgeNeighbors[cursor[constraint.A]++] = constraint.B;
                edgeNeighbors[cursor[constraint.B]++] = constraint.A;
            }
        }

        private float[] ComputeGrabDistances(
            int seed,
            int[] edgeOffsets,
            int[] edgeNeighbors,
            float maxDistance)
        {
            int count = positions.Count;
            float[] distances = new float[count];
            for (int i = 0; i < count; i++)
            {
                distances[i] = float.PositiveInfinity;
            }

            bool[] queued = new bool[count];
            Queue<int> queue = new Queue<int>();
            distances[seed] = 0f;
            queue.Enqueue(seed);
            queued[seed] = true;

            while (queue.Count > 0)
            {
                int vertex = queue.Dequeue();
                queued[vertex] = false;
                float currentDistance = distances[vertex];
                for (int edge = edgeOffsets[vertex]; edge < edgeOffsets[vertex + 1]; edge++)
                {
                    int neighbor = edgeNeighbors[edge];
                    float edgeLength = Vector3.Distance(positions[vertex], positions[neighbor]);
                    if (edgeLength < 1e-7f)
                    {
                        continue;
                    }

                    float nextDistance = currentDistance + edgeLength;
                    if (nextDistance >= distances[neighbor] || nextDistance > maxDistance)
                    {
                        continue;
                    }

                    distances[neighbor] = nextDistance;
                    if (!queued[neighbor])
                    {
                        queue.Enqueue(neighbor);
                        queued[neighbor] = true;
                    }
                }
            }

            return distances;
        }


        private void PinGrabVertex(int index)
        {
            grabIndices.Add(index);
            grabWeights.Add(1f);
            grabOriginalInvMasses.Add(invMasses[index]);
            grabRestPositions.Add(positions[index]);
            invMasses[index] = 0f;
            grabMask[index] = true;
            grabInfluence[index] = 1f;
        }

        public void MoveGrab(Vector3 localPoint)
        {
            if (IsGrabbing)
            {
                grabTargetDesired = localPoint;
            }
        }

        public void EndGrab()
        {
            if (!IsGrabbing)
            {
                grabMask = null;
                grabInfluence = null;
                grabCurrentOffset = Vector3.zero;
                return;
            }

            for (int i = 0; i < grabIndices.Count; i++)
            {
                int index = grabIndices[i];
                if (index < invMasses.Count)
                {
                    invMasses[index] = grabOriginalInvMasses[i];
                    previousPositions[index] = positions[index];
                    velocities[index] = Vector3.zero;
                }
            }

            for (int i = 0; i < grabSoftIndices.Count; i++)
            {
                int index = grabSoftIndices[i];
                previousPositions[index] = positions[index];
                velocities[index] = Vector3.zero;
            }

            grabIndices.Clear();
            grabWeights.Clear();
            grabOriginalInvMasses.Clear();
            grabRestPositions.Clear();
            grabSoftIndices.Clear();
            grabSoftWeights.Clear();
            grabSoftRestPositions.Clear();
            grabMask = null;
            grabInfluence = null;
            grabCurrentOffset = Vector3.zero;
        }

        public int SmoothBoundary(int iterations, float strength)
        {
            if (iterations <= 0 || positions.Count == 0 || triangles.Count == 0)
            {
                return 0;
            }

            Dictionary<long, int> edgeCounts = new Dictionary<long, int>(triangles.Count);
            for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
            {
                int i0 = triangles[triangle];
                int i1 = triangles[triangle + 1];
                int i2 = triangles[triangle + 2];
                CountEdge(edgeCounts, i0, i1);
                CountEdge(edgeCounts, i1, i2);
                CountEdge(edgeCounts, i2, i0);
            }

            Dictionary<int, List<int>> boundaryNeighbors = new Dictionary<int, List<int>>();
            for (int triangle = 0; triangle + 2 < triangles.Count; triangle += 3)
            {
                int i0 = triangles[triangle];
                int i1 = triangles[triangle + 1];
                int i2 = triangles[triangle + 2];
                AddBoundaryNeighbor(edgeCounts, boundaryNeighbors, i0, i1);
                AddBoundaryNeighbor(edgeCounts, boundaryNeighbors, i1, i2);
                AddBoundaryNeighbor(edgeCounts, boundaryNeighbors, i2, i0);
            }

            if (boundaryNeighbors.Count == 0)
            {
                return 0;
            }

            float blend = Mathf.Clamp01(strength);
            HashSet<int> moved = new HashSet<int>();

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                foreach (KeyValuePair<int, List<int>> entry in boundaryNeighbors)
                {
                    List<int> neighbors = entry.Value;
                    if (neighbors.Count != 2)
                    {
                        continue;
                    }

                    int index = entry.Key;
                    if (permanentPins[index] || invMasses[index] <= 0f)
                    {
                        continue;
                    }

                    int a = neighbors[0];
                    int b = neighbors[1];
                    Vector3 current = positions[index];
                    Vector3 toA = positions[a] - current;
                    Vector3 toB = positions[b] - current;
                    if (toA.sqrMagnitude < 1e-10f || toB.sqrMagnitude < 1e-10f)
                    {
                        continue;
                    }

                    float corner = Vector3.Dot(toA.normalized, toB.normalized);
                    if (corner > -0.5f)
                    {
                        continue;
                    }

                    Vector3 target = (positions[a] + positions[b]) * 0.5f;
                    Vector3 next = Vector3.Lerp(current, target, blend);
                    if ((next - current).sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    positions[index] = next;
                    previousPositions[index] = next;
                    velocities[index] = Vector3.zero;
                    restPositions[index] = next;
                    moved.Add(index);
                }
            }

            if (moved.Count == 0)
            {
                return 0;
            }

            RefreshRestLengths(moved);
            UploadDeformedMesh();
            CutSurfaceVersion++;
            return moved.Count;
        }

        private static void CountEdge(Dictionary<long, int> counts, int a, int b)
        {
            long key = EdgeKey(a, b);
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }

        private static void AddBoundaryNeighbor(
            Dictionary<long, int> edgeCounts,
            Dictionary<int, List<int>> neighbors,
            int a,
            int b)
        {
            int count;
            if (!edgeCounts.TryGetValue(EdgeKey(a, b), out count) || count != 1)
            {
                return;
            }

            AddNeighbor(neighbors, a, b);
            AddNeighbor(neighbors, b, a);
        }

        private static void AddNeighbor(Dictionary<int, List<int>> neighbors, int vertex, int neighbor)
        {
            List<int> list;
            if (!neighbors.TryGetValue(vertex, out list))
            {
                list = new List<int>(2);
                neighbors.Add(vertex, list);
            }

            if (!list.Contains(neighbor))
            {
                list.Add(neighbor);
            }
        }

        private void RefreshRestLengths(HashSet<int> moved)
        {
            for (int i = 0; i < constraints.Count; i++)
            {
                SkinConstraint constraint = constraints[i];
                if (!moved.Contains(constraint.A) && !moved.Contains(constraint.B))
                {
                    continue;
                }

                float length = Vector3.Distance(
                    positions[constraint.A],
                    positions[constraint.B]);
                if (length < 1e-7f)
                {
                    continue;
                }

                constraint.RestLength = length;
                constraints[i] = constraint;

                long key = EdgeKey(constraint.A, constraint.B);
                if (constraint.IsBend)
                {
                    bendRestLengths[key] = length;
                }
                else
                {
                    stretchRestLengths[key] = length;
                }
            }
        }

        private void EnsureMeshComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "SkinSheet";
                mesh.MarkDynamic();
                meshFilter.sharedMesh = mesh;
            }
        }

        private void UploadDeformedMesh()
        {
            if (mesh == null)
            {
                EnsureMeshComponents();
            }

            mesh.SetVertices(positions);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void UploadMesh()
        {
            if (mesh == null)
            {
                EnsureMeshComponents();
            }

            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);

            surfaceTriangles.Clear();
            wallTriangles.Clear();
            for (int triangle = 0; triangle < triangles.Count / 3; triangle++)
            {
                List<int> target = triangleWallFlags.Count > triangle &&
                                   triangleWallFlags[triangle]
                    ? wallTriangles
                    : surfaceTriangles;
                target.Add(triangles[triangle * 3]);
                target.Add(triangles[triangle * 3 + 1]);
                target.Add(triangles[triangle * 3 + 2]);
            }

            mesh.subMeshCount = 2;
            mesh.SetTriangles(surfaceTriangles, 0);
            mesh.SetTriangles(wallTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public static long EdgeKey(int a, int b)
        {
            if (a > b)
            {
                int temp = a;
                a = b;
                b = temp;
            }

            return ((long)a << 32) | (uint)b;
        }

        public static long PairKey(int a, int b)
        {
            return EdgeKey(a, b);
        }
    }
}







