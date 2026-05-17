using UnityEngine;

namespace MeshBreak.MeshCut.Version2
{
    public class CachedMeshData
    {
        public readonly string MeshName;
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] UVs;
        public readonly int[][] SubMeshTriangles;
        public readonly int VertexCount;

        public CachedMeshData(Mesh mesh)
        {
            MeshName = mesh.name;
            Vertices = mesh.vertices;
            Normals = mesh.normals;
            UVs = mesh.uv;

            int subCount = mesh.subMeshCount;
            SubMeshTriangles = new int[subCount][];
            for (int i = 0; i < subCount; i++)
                SubMeshTriangles[i] = mesh.GetTriangles(i);

            VertexCount = mesh.vertexCount;
        }
    }
}