using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class MultiCutContext : IDisposable
{
    public NativeArray<float3> BaseVertices;
    public NativeArray<float3> BaseNormals;
    public NativeArray<float2> BaseUvs;

    public NativeArray<float3> NewVertices;
    public NativeArray<float3> NewNormals;
    public NativeArray<float2> NewUvs;
    public NativeArray<NewTriangle> NewTriangles;
    public NativeParallelMultiHashMap<int, int2> CutEdges;
    public List<BurstBreakMesh> breakMeshes;

    /// <summary> オブジェクトごとの切断処理に使う </summary>
    public NativeArray<NativePlane> Blades;

    /// <summary> 各頂点が表裏のどちらにあるのかを保持する </summary>
    public NativeArray<int> BaseVertexSide;

    /// <summary> 各頂点のオブジェクト番号 </summary>
    public NativeArray<int> VertexObjectIndex;

    /// <summary> 切断が必要な面を保存する </summary>
    public NativeList<int3> CutFaces;

    /// <summary> 切断面のサブメッシュ番号 </summary>
    public NativeList<int> CutFaceSubmeshId;

    /// <summary> どちらに孤立頂点があるかなどの情報が分かる </summary>
    public NativeList<int> CutStatus;

    /// <summary> 三角形のオブジェクト番号 </summary>
    public NativeList<int> TriangleObjectIndex;

    /// <summary> ループごとのオブジェクトおよび切断面のどちら側にあるかを保持 </summary>
    public NativeList<int2> LoopRanges;

    /// <summary> 頂点配列のオブジェクト毎の開始位置を保持する </summary>
    public List<int> StartIndex;

    public MultiCutContext(int objectCount)
    {
        StartIndex = new List<int>(objectCount);
        // NativeList群をコンストラクタで初期化
        CutFaces = new NativeList<int3>(Allocator.Persistent);
        CutFaceSubmeshId = new NativeList<int>(Allocator.Persistent);
        CutStatus = new NativeList<int>(Allocator.Persistent);
        TriangleObjectIndex = new NativeList<int>(Allocator.Persistent);
        LoopRanges = new NativeList<int2>(Allocator.Persistent);
    }

    public void Dispose()
    {
        if (BaseVertices.IsCreated) BaseVertices.Dispose();
        if (BaseNormals.IsCreated) BaseNormals.Dispose();
        if (BaseUvs.IsCreated) BaseUvs.Dispose();
        if (NewVertices.IsCreated) NewVertices.Dispose();
        if (NewNormals.IsCreated) NewNormals.Dispose();
        if (NewUvs.IsCreated) NewUvs.Dispose();
        if (NewTriangles.IsCreated) NewTriangles.Dispose();
        if (CutEdges.IsCreated) CutEdges.Dispose();
        if (Blades.IsCreated) Blades.Dispose();
        if (BaseVertexSide.IsCreated) BaseVertexSide.Dispose();
        if (VertexObjectIndex.IsCreated) VertexObjectIndex.Dispose();
        if (CutFaces.IsCreated) CutFaces.Dispose();
        if (CutFaceSubmeshId.IsCreated) CutFaceSubmeshId.Dispose();
        if (CutStatus.IsCreated) CutStatus.Dispose();
        if (TriangleObjectIndex.IsCreated) TriangleObjectIndex.Dispose();
        if (LoopRanges.IsCreated) LoopRanges.Dispose();
        foreach (var burstBreakMesh in breakMeshes)
        {
            burstBreakMesh.Dispose();
        }

        breakMeshes.Clear();
    }
}

public class MeshBoundary
{
    public int StartIndex;
    public int Length;
}