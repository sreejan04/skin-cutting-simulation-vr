using System.Collections.Generic;
using UnityEngine;

namespace SkinCutter.Live
{
    public class LiveSkinMesh
    {
        public Mesh Mesh { get; private set; }
        public GameObject GameObject { get; }

        private readonly LiveSkinTopology topology;
        private readonly bool isBox;
        private readonly float boxSize;

        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> indices = new List<int>();

        private int uploadedVertexCount = -1;
        private int uploadedTriangleCount = -1;

        public LiveSkinMesh(LiveSkinTopology topology, Transform parent, Material material)
        {
            this.topology = topology;
            isBox = topology.isBox;
            boxSize = topology.boxSize != 0f ? topology.boxSize : 1f;

            GameObject = new GameObject("Skin");
            GameObject.transform.SetParent(parent, false);

            var filter = GameObject.AddComponent<MeshFilter>();
            var renderer = GameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            Mesh = new Mesh { name = "Skin" };
            Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Mesh.MarkDynamic();
            filter.sharedMesh = Mesh;

            Upload(true);
        }

        public void Upload(bool topologyChanged, bool recalcNormals = true)
        {
            var verts = topology.vertices;
            var tris = topology.triangles;

            positions.Clear();
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i].pos;
                if (!SkinMath.IsFinite(p)) p = verts[i].origPos;
                positions.Add(p);
            }

            bool needTopology = topologyChanged
                || uploadedVertexCount != verts.Count
                || uploadedTriangleCount != tris.Count;

            if (needTopology)
            {
                uvs.Clear();
                for (int i = 0; i < verts.Count; i++)
                    uvs.Add(ComputeUv(positions[i]));

                indices.Clear();
                for (int i = 0; i < tris.Count; i++)
                {
                    indices.Add(tris[i].v1);
                    indices.Add(tris[i].v3);
                    indices.Add(tris[i].v2);
                }
            }

            Mesh.SetVertices(positions);
            if (needTopology)
            {
                Mesh.SetUVs(0, uvs);
                Mesh.SetTriangles(indices, 0, true);
                uploadedVertexCount = verts.Count;
                uploadedTriangleCount = tris.Count;
            }

            if (recalcNormals)
                Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
        }

        private Vector2 ComputeUv(Vector3 p)
        {
            if (isBox)
            {
                float half = boxSize / 2f;
                return new Vector2((p.x + half) / boxSize, (p.y + half) / boxSize);
            }
            return new Vector2((p.x + 2f) / 4f, (p.y + 2f) / 4f);
        }

        public void Destroy()
        {
            Object.Destroy(GameObject);
        }
    }
}
