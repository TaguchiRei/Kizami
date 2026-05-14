using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

public class BurstBreakMesh : IDisposable
{
    public NativeList<float3> Vertices;
    public NativeList<float3> Normals;
    public NativeList<float2> Uvs;

    public List<NativeList<int>> Triangles;
    public int[] _addVerticesArray;

    public BurstBreakMesh(int vertCount)
    {
        _addVerticesArray = new int[vertCount];
        Array.Fill(_addVerticesArray, -1);

        Vertices = new NativeList<float3>(vertCount, Allocator.Persistent);
        Normals = new NativeList<float3>(vertCount, Allocator.Persistent);
        Uvs = new NativeList<float2>(vertCount, Allocator.Persistent);
        Triangles = new();
    }

    public void AddTriangleLegacyIndex(
        int p1, int p2, int p3,
        float3 v1, float3 v2, float3 v3,
        float3 n1, float3 n2, float3 n3,
        float2 u1, float2 u2, float2 u3, int submesh)
    {
        Triangles[submesh].Add(GetOrAddVertex(p1, v1, n1, u1));
        Triangles[submesh].Add(GetOrAddVertex(p2, v2, n2, u2));
        Triangles[submesh].Add(GetOrAddVertex(p3, v3, n3, u3));
    }

    public void AddTriangle(
        float3 v1, float3 v2, float3 v3,
        float3 n1, float3 n2, float3 n3,
        float2 u1, float2 u2, float2 u3,
        float3 faceNormal, int submesh)
    {
        float3 calculatedNormal = math.cross(v2 - v1, v3 - v1);

        int baseIndex = Vertices.Length;

        Triangles[submesh].Add(baseIndex);
        Triangles[submesh].Add(baseIndex + 1);
        Triangles[submesh].Add(baseIndex + 2);

        if (math.dot(calculatedNormal, faceNormal) < 0f)
        {
            Vertices.Add(v3);
            Vertices.Add(v2);
            Vertices.Add(v1);

            Normals.Add(n3);
            Normals.Add(n2);
            Normals.Add(n1);

            Uvs.Add(u3);
            Uvs.Add(u2);
            Uvs.Add(u1);
        }
        else
        {
            Vertices.Add(v1);
            Vertices.Add(v2);
            Vertices.Add(v3);

            Normals.Add(n1);
            Normals.Add(n2);
            Normals.Add(n3);

            Uvs.Add(u1);
            Uvs.Add(u2);
            Uvs.Add(u3);
        }
    }

    public void AddSubmesh()
    {
        Triangles.Add(new(Allocator.Persistent));
    }

    private int GetOrAddVertex(int index, float3 pos, float3 normal, float2 uv)
    {
        if (_addVerticesArray[index] != -1)
        {
            return _addVerticesArray[index];
        }

        int newIndex = Vertices.Length;
        _addVerticesArray[index] = newIndex;
        Vertices.Add(pos);
        Normals.Add(normal);
        Uvs.Add(uv);
        return newIndex;
    }

    public void Dispose()
    {
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Normals.IsCreated) Normals.Dispose();
        if (Uvs.IsCreated) Uvs.Dispose();

        if (Triangles != null)
        {
            foreach (var triangle in Triangles)
            {
                if (triangle.IsCreated)
                {
                    triangle.Dispose();
                }
            }

            Triangles.Clear();
        }
    }
}