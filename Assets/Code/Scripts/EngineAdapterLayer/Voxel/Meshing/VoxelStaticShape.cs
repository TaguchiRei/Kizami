using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 手付かずのパーツの形を、ボクセルにした直後と同じ処理（角ばった描き方の SurfaceNetsJob と VoxelBoxColliderJob）で 1 枚のメッシュと箱の組にしたもの。
    /// VoxelModelLoader.LoadStatic が、削られるまでの間の見た目と当たり判定に使う。読み込んだ直後のボクセルと形が一致する為、差し替えても見た目が変わらない。
    /// 同じベイク・ボクセルの大きさ・チャンクの大きさなら 1 つを使い回す。
    /// 建物のつながりの判定で手付かずの面を読む為、格子の距離と素材の番号の写しも持つ。
    /// </summary>
    public sealed class VoxelStaticShape
    {
        private static readonly Dictionary<(VoxelSdfData, float, int), VoxelStaticShape> _cache = new();

        private readonly float[] _samples;
        private readonly byte[] _materialIds;

        /// <summary> 写しを取った格子。ボクセルにしたときの VoxelPiece の格子と同じ </summary>
        public VoxelGridLayout Layout { get; }

        /// <summary> 素材ごとのサブメッシュを持つメッシュ（パーツのローカル空間） </summary>
        public Mesh Mesh { get; }

        /// <summary> 箱の中心（パーツのローカル空間） </summary>
        public IReadOnlyList<Vector3> BoxCenters { get; }

        /// <summary> 箱の大きさ（パーツのローカル空間） </summary>
        public IReadOnlyList<Vector3> BoxSizes { get; }

        private VoxelStaticShape(VoxelVolume volume, Mesh mesh, List<Vector3> boxCenters, List<Vector3> boxSizes)
        {
            Layout = volume.Layout;
            _samples = volume.Samples.ToArray();
            _materialIds = volume.HasMaterials ? volume.MaterialIds.ToArray() : null;
            Mesh = mesh;
            BoxCenters = boxCenters;
            BoxSizes = boxSizes;
        }

        /// <summary>
        /// ベイクとボクセルの大きさから形を作る。作ったことがあれば同じものを返す。
        /// </summary>
        /// <param name="source">パーツのローカル空間でベイクした SDF</param>
        /// <param name="localVoxelSize">パーツのローカル空間でのボクセルの大きさ</param>
        /// <param name="chunkSize">品質設定のチャンクの大きさ。格子の作り方を VoxelPiece とそろえる為に使う</param>
        /// <param name="materialCount">素材の数。1 なら素材で分けない</param>
        public static VoxelStaticShape GetOrCreate(VoxelSdfData source, float localVoxelSize, int chunkSize,
            int materialCount)
        {
            var key = (source, localVoxelSize, chunkSize);
            if (_cache.TryGetValue(key, out var shape) && shape.Mesh != null) return shape;

            shape = Create(source, localVoxelSize, chunkSize, materialCount);
            _cache[key] = shape;
            return shape;
        }

        private static VoxelStaticShape Create(VoxelSdfData source, float localVoxelSize, int chunkSize,
            int materialCount)
        {
            var layout = VoxelGridLayout.FromBounds(source.LocalBounds, localVoxelSize, chunkSize);
            using var volume = new VoxelVolume(layout);
            volume.Resample(source);

            var useMaterials = volume.HasMaterials && materialCount > 1;
            var meshMaterialCount = useMaterials ? materialCount : 1;
            var vertices = new NativeList<float3>(4096, Allocator.TempJob);
            var normals = new NativeList<float3>(4096, Allocator.TempJob);
            var smoothOffsets = new NativeList<float4>(4096, Allocator.TempJob);
            var indices = new NativeList<int>(16384, Allocator.TempJob);
            var subMeshIndexCounts = new NativeArray<int>(meshMaterialCount, Allocator.TempJob);
            var boxMins = new NativeList<int3>(64, Allocator.TempJob);
            var boxMaxs = new NativeList<int3>(64, Allocator.TempJob);

            // 手付かずの形なので丸みは全点 0、倍率はどれでも同じ
            var roundness = new NativeArray<byte>(layout.SampleTotal, Allocator.TempJob);
            var roundnessScales = new NativeArray<float>(meshMaterialCount, Allocator.TempJob);
            var emptyMaterialIds = useMaterials ? default : new NativeArray<byte>(1, Allocator.TempJob);

            new SurfaceNetsJob
            {
                Samples = volume.Samples,
                Layout = layout,
                CellMin = int3.zero,
                CellMax = layout.CellCount,
                Vertices = vertices,
                Normals = normals,
                Indices = indices,
                MaterialCount = meshMaterialCount,
                SubMeshIndexCounts = subMeshIndexCounts,
                MaterialIds = useMaterials ? volume.MaterialIds : emptyMaterialIds,
                UseBlockShape = true,
                SmoothOffsets = smoothOffsets,
                Roundness = roundness,
                RoundnessScales = roundnessScales
            }.Run();

            new VoxelBoxColliderJob
            {
                Layout = layout,
                SampleMin = int3.zero,
                SampleMax = layout.SampleCount,
                Samples = volume.Samples,
                BoxMins = boxMins,
                BoxMaxs = boxMaxs
            }.Run();

            var mesh = new Mesh
            {
                name = $"{source.Path}_Static",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices.AsArray());
            mesh.SetNormals(normals.AsArray());
            mesh.SetUVs(1, smoothOffsets.AsArray());
            mesh.subMeshCount = meshMaterialCount;
            var start = 0;
            for (var subMesh = 0; subMesh < meshMaterialCount; subMesh++)
            {
                var length = subMeshIndexCounts[subMesh];
                mesh.SetIndices(indices.AsArray().GetSubArray(start, length), MeshTopology.Triangles, subMesh);
                start += length;
            }

            var boxCenters = new List<Vector3>(boxMins.Length);
            var boxSizes = new List<Vector3>(boxMins.Length);
            for (var i = 0; i < boxMins.Length; i++)
            {
                boxCenters.Add(layout.Origin + (float3)(boxMins[i] + boxMaxs[i]) * 0.5f * layout.VoxelSize);
                boxSizes.Add((float3)(boxMaxs[i] - boxMins[i] + 1) * layout.VoxelSize);
            }

            vertices.Dispose();
            normals.Dispose();
            smoothOffsets.Dispose();
            indices.Dispose();
            subMeshIndexCounts.Dispose();
            boxMins.Dispose();
            boxMaxs.Dispose();
            roundness.Dispose();
            roundnessScales.Dispose();
            if (emptyMaterialIds.IsCreated) emptyMaterialIds.Dispose();

            return new VoxelStaticShape(volume, mesh, boxCenters, boxSizes);
        }

        /// <summary>
        /// VoxelVolume.ReadLayerWeights と同じく、y のサンプル番号 sampleY の段の内側のサンプルに素材ごとの重みを、外側に 0 を書く。
        /// </summary>
        internal void ReadLayerWeights(int sampleY, byte[] materialWeights, byte uniformWeight, byte[] weights)
        {
            var count = Layout.SampleCount;
            var useMaterials = _materialIds != null && materialWeights != null && materialWeights.Length > 0;

            for (var z = 0; z < count.z; z++)
            for (var x = 0; x < count.x; x++)
            {
                var index = Layout.ToSampleIndex(new int3(x, sampleY, z));
                byte weight = 0;
                if (_samples[index] < 0f) weight = useMaterials ? materialWeights[_materialIds[index]] : uniformWeight;

                weights[x + z * count.x] = weight;
            }
        }
    }
}
