using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct EarClippingAlgorithmJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> AllLoopVertices;
    [ReadOnly] public NativeArray<int2> LoopRanges;
    [ReadOnly] public NativeArray<float3> NewVertices;
    [ReadOnly] public NativeArray<NativePlane> Blades;
    [ReadOnly] public NativeArray<int> LoopObjectIndices;
    [ReadOnly] public NativeArray<int> WriteOffsets;

    /// <summary> x,y,z = VertexIndex, w = LoopIndex </summary> 
    [WriteOnly] public NativeArray<int4> OutCapTriangles;

    public void Execute(int index)
    {
        int2 range = LoopRanges[index];
        int writePos = WriteOffsets[index];
        NativePlane blade = Blades[LoopObjectIndices[index]];

        // NativeList を Allocator.Temp で確保。Execute 終了時に自動解放されます
        var points2D = new NativeList<float2>(range.y, Allocator.Temp);
        var activeIndices = new NativeList<int>(range.y, Allocator.Temp);

        float3 normal = blade.Normal;

        // 直交基底の作成
        float3 tangent = math.normalizesafe(math.cross(normal, new float3(0, 1, 0)));
        if (math.lengthsq(tangent) < 0.0001f) tangent = math.cross(normal, new float3(1, 0, 0));
        float3 bitangent = math.cross(normal, tangent);

        for (int i = 0; i < range.y; i++)
        {
            float3 v = NewVertices[AllLoopVertices[range.x + i]];
            points2D.Add(new float2(math.dot(v, tangent), math.dot(v, bitangent)));
            activeIndices.Add(i);
        }

        // --- 2. 耳切法 ---
        int triCount = 0;
        int maxTriangles = range.y - 2;
        int timeout = range.y * range.y; // 異常系回避用

        while (activeIndices.Length > 2 && timeout > 0)
        {
            timeout--;
            bool earFound = false;

            for (int i = 0; i < activeIndices.Length; i++)
            {
                int prevIdx = (i + activeIndices.Length - 1) % activeIndices.Length;
                int nextIdx = (i + 1) % activeIndices.Length;

                int p = activeIndices[prevIdx];
                int c = activeIndices[i];
                int n = activeIndices[nextIdx];

                if (IsEar(p, c, n, points2D, activeIndices))
                {
                    // 三角形確定
                    if (triCount < maxTriangles)
                    {
                        OutCapTriangles[writePos + triCount] = new int4(
                            AllLoopVertices[range.x + p],
                            AllLoopVertices[range.x + c],
                            AllLoopVertices[range.x + n],
                            index // Loop ID
                        );
                        triCount++;
                    }

                    activeIndices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound) break;
        }
    }

    /// <summary>
    /// どこに耳(凹形状)があるかを調べる
    /// </summary>
    /// <param name="p"></param>
    /// <param name="c"></param>
    /// <param name="n"></param>
    /// <param name="points"></param>
    /// <param name="activeIndices"></param>
    /// <returns></returns>
    private bool IsEar(int p, int c, int n, NativeList<float2> points, NativeList<int> activeIndices)
    {
        float2 va = points[p], vb = points[c], vc = points[n];

        float cross = (vb.x - va.x) * (vc.y - va.y) - (vb.y - va.y) * (vc.x - va.x);
        if (cross <= 0) return false;

        for (int i = 0; i < activeIndices.Length; i++)
        {
            int idx = activeIndices[i];
            if (idx == p || idx == c || idx == n) continue;

            if (IsPointInTriangle(points[idx], va, vb, vc)) return false;
        }

        return true;
    }

    private bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// math.crossは一度float3にキャストする工程が入るのでこちらの方が高速
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    private float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
}