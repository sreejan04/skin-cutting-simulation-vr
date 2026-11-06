using UnityEngine;

namespace SkinCutter
{
    public enum ShapeType
    {
        Plane,
        Box
    }

    public class SkinMesh
    {
        public Mesh Mesh { get; private set; }
        public GameObject GameObject { get; }

        private readonly ShapeType shapeType;
        private Vector3[] positionCache;

        public SkinMesh(SkinTopology topology, Transform parent, Material material, ShapeType shapeType)
        {
            this.shapeType = shapeType;
            GameObject = new GameObject("Skin");
            GameObject.transform.SetParent(parent, false);

            var filter = GameObject.AddComponent<MeshFilter>();
            var renderer = GameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            Mesh = new Mesh { name = "Skin" };
            Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Mesh.MarkDynamic();
            filter.sharedMesh = Mesh;

            BuildGeometry(topology);
        }

        private void BuildGeometry(SkinTopology topology)
        {
            var verts = topology.vertices;
            var tris = topology.triangles;

            positionCache = new Vector3[verts.Count];
            var uvs = new Vector2[verts.Count];
            var indices = new int[tris.Count * 3];

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i].pos;
                positionCache[i] = p;

                if (shapeType == ShapeType.Box)
                {
                    float size = topology.boxSize != 0f ? topology.boxSize : 1f;
                    float half = size / 2f;
                    uvs[i] = new Vector2((p.x + half) / size, (p.y + half) / size);
                }
                else
                {
                    uvs[i] = new Vector2((p.x + 2f) / 4f, (p.y + 2f) / 4f);
                }
            }

            for (int i = 0; i < tris.Count; i++)
            {
                indices[i * 3] = tris[i].v1;
                indices[i * 3 + 1] = tris[i].v3;
                indices[i * 3 + 2] = tris[i].v2;
            }

            Mesh.Clear(false);
            Mesh.vertices = positionCache;
            Mesh.uv = uvs;
            Mesh.triangles = indices;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
        }

        public void Rebuild(SkinTopology topology)
        {
            BuildGeometry(topology);
        }

        public void UpdatePositions(SkinTopology topology, bool recalcEveryFrame = true)
        {
            if (positionCache == null) return;
            var verts = topology.vertices;
            if (positionCache.Length != verts.Count)
            {
                Rebuild(topology);
                return;
            }
            bool corrupted = false;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i].pos;
                if (!SkinMath.IsFinite(p))
                {
                    p = verts[i].origPos;
                    corrupted = true;
                }
                positionCache[i] = p;
            }
            if (corrupted)
            {
                for (int i = 0; i < verts.Count; i++) verts[i].pos = positionCache[i];
            }
            Mesh.vertices = positionCache;
            if (recalcEveryFrame)
            {
                Mesh.RecalculateNormals();
                Mesh.RecalculateBounds();
            }
        }

        public void Destroy()
        {
            Object.Destroy(GameObject);
        }
    }
}
