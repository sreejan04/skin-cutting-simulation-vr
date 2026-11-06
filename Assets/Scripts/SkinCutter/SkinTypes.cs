using UnityEngine;

namespace SkinCutter
{
    public class Vertex
    {
        public Vector3 pos;
        public Vector3 origPos;

        public Vertex(Vector3 position)
        {
            pos = position;
            origPos = position;
        }

        public Vertex(Vector3 position, Vector3 originalPosition)
        {
            pos = position;
            origPos = originalPosition;
        }
    }

    public struct Triangle
    {
        public int v1;
        public int v2;
        public int v3;

        public Triangle(int a, int b, int c)
        {
            v1 = a;
            v2 = b;
            v3 = c;
        }

        public int this[int i] => i == 0 ? v1 : i == 1 ? v2 : v3;
    }

    public class EdgeCrossing
    {
        public string edgeKey;
        public int v1;
        public int v2;
        public float t;
        public Vector3 point;
    }

    public static class SkinMath
    {
        public static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }
    }
}
