using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 融解した粒の見た目となる液面のメッシュ。
    ///
    /// ワールド空間を一辺 ChunkCells セルの液面チャンクに区切り、チャンクごとに
    /// 粒ごとの球を滑らかにつないだ距離場を書き込んで、Surface Nets でメッシュにする。
    /// 作り直すのは、動いている粒が届くチャンク・前回動いていた粒が届いていたチャンク・粒が消えたチャンクだけで、
    /// 止まった粒しか無いチャンクはメッシュを使い回す。頂点はワールド空間。
    /// </summary>
    public sealed class VoxelMeltSurface : IDisposable
    {
        /// <summary> 液面チャンク 1 つの、各軸のセル数 </summary>
        public const int ChunkCells = 16;

        /// <summary>
        /// チャンクの格子が、面を作るセル範囲の外側に持つ余白のセル数。
        /// SurfaceNetsJob が面を作るセル範囲の外を読む量と一致させること。足りないと、チャンクの境目で面が開く。
        /// </summary>
        private const int PaddingCells = VoxelGridLayout.MeshingReadMargin;

        private readonly Dictionary<int3, SurfaceChunk> _chunks = new();
        private readonly HashSet<int3> _previousMovingChunks = new();
        private readonly HashSet<int3> _pendingChunks = new();

        private float _voxelSize;
        private float _radiusScale;
        private float _blend;
        private bool _rebuildAll = true;

        /// <summary> 液面の三角形数 </summary>
        public int TriangleCount { get; private set; }

        /// <summary> 面を持つ液面チャンクの数 </summary>
        public int ChunkCount => _chunks.Count;

        /// <summary> 直近の作り直しで作り直したチャンクの数 </summary>
        public int LastRebuiltChunkCount { get; private set; }

        /// <summary> 直近の作り直しにかかった時間 </summary>
        public double LastBuildMilliseconds { get; private set; }

        private float ChunkWorldSize => ChunkCells * _voxelSize;

        /// <summary> 粒の球の表面から、距離を書き込む範囲 </summary>
        private float SplatReach => _blend + _voxelSize * VoxelGridLayout.MeshingReadMargin;

        /// <summary>
        /// 液面の作り方を設定する。前回と異なれば、次の Update で全チャンクを作り直す。
        /// </summary>
        /// <param name="voxelSize">格子の大きさ（ワールド空間, m）</param>
        /// <param name="radiusScale">粒の半径（体積の等しい球の半径）に掛ける倍率</param>
        /// <param name="blend">粒同士を滑らかにつなぐ幅（ワールド空間, m）</param>
        public void Configure(float voxelSize, float radiusScale, float blend)
        {
            if (voxelSize == _voxelSize && radiusScale == _radiusScale && blend == _blend) return;

            _voxelSize = voxelSize;
            _radiusScale = radiusScale;
            _blend = blend;
            ClearChunks();
            _rebuildAll = true;
        }

        /// <summary>
        /// 次の Update で全チャンクを作り直させる。
        /// </summary>
        public void MarkAllDirty()
        {
            _rebuildAll = true;
        }

        /// <summary>
        /// 粒が取り除かれたことを伝え、その粒が届いていたチャンクを次の Update で作り直させる。
        /// </summary>
        public void MarkRemoved(in VoxelMeltParticle particle)
        {
            if (_voxelSize <= 0f) return;

            var extent = particle.Radius * _radiusScale + GetKeyReach();
            var chunkMin = (int3)math.floor((particle.Position - extent) / ChunkWorldSize);
            var chunkMax = (int3)math.floor((particle.Position + extent) / ChunkWorldSize);

            for (var z = chunkMin.z; z <= chunkMax.z; z++)
            for (var y = chunkMin.y; y <= chunkMax.y; y++)
            for (var x = chunkMin.x; x <= chunkMax.x; x++)
            {
                _pendingChunks.Add(new int3(x, y, z));
            }
        }

        /// <summary>
        /// 作り直しが必要なチャンクだけを作り直す。作り直すものが無ければ何もしない。
        /// </summary>
        /// <param name="particles">全ての粒</param>
        /// <param name="hasMovingParticles">止まっていない粒があるか</param>
        public void Update(NativeArray<VoxelMeltParticle> particles, bool hasMovingParticles)
        {
            if (_voxelSize <= 0f) return;
            if (!_rebuildAll && !hasMovingParticles && _previousMovingChunks.Count == 0 && _pendingChunks.Count == 0)
            {
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var particlesByChunk = new NativeParallelMultiHashMap<int3, int>(math.max(particles.Length * 2, 16),
                Allocator.TempJob);
            var movingChunks = new NativeParallelHashSet<int3>(64, Allocator.TempJob);

            new VoxelMeltChunkKeysJob
            {
                Particles = particles,
                ChunkWorldSize = ChunkWorldSize,
                RadiusScale = _radiusScale,
                Reach = GetKeyReach(),
                ParticlesByChunk = particlesByChunk,
                MovingChunks = movingChunks
            }.Schedule().Complete();

            // 前回動いていた粒が届いていたチャンクも作り直し、動いて離れた粒の跡を消す
            var dirtyChunks = new HashSet<int3>(_pendingChunks);
            dirtyChunks.UnionWith(_previousMovingChunks);

            _previousMovingChunks.Clear();
            foreach (var key in movingChunks)
            {
                _previousMovingChunks.Add(key);
                dirtyChunks.Add(key);
            }

            if (_rebuildAll)
            {
                dirtyChunks.UnionWith(_chunks.Keys);

                var keys = particlesByChunk.GetKeyArray(Allocator.Temp);
                foreach (var key in keys)
                {
                    dirtyChunks.Add(key);
                }

                keys.Dispose();
            }

            RebuildChunks(dirtyChunks, particles, particlesByChunk);

            particlesByChunk.Dispose();
            movingChunks.Dispose();
            _pendingChunks.Clear();
            _rebuildAll = false;

            LastRebuiltChunkCount = dirtyChunks.Count;
            LastBuildMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// 面を持つ全チャンクを描く。
        /// </summary>
        /// <param name="renderParams">描画の設定。worldBounds はチャンクごとに上書きする</param>
        public void Draw(RenderParams renderParams)
        {
            foreach (var chunk in _chunks.Values)
            {
                renderParams.worldBounds = chunk.Mesh.bounds;
                Graphics.RenderMesh(renderParams, chunk.Mesh, 0, Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            ClearChunks();
        }

        /// <summary>
        /// 粒の球の表面から、チャンクの格子（余白を含む）へ影響する範囲。
        /// </summary>
        private float GetKeyReach()
        {
            return SplatReach + PaddingCells * _voxelSize;
        }

        /// <summary>
        /// チャンクの格子を作る。面を作るセル範囲の外側に、全方向へ PaddingCells セルの余白を付ける。
        /// </summary>
        private VoxelGridLayout CreateChunkLayout(int3 key)
        {
            var cellCount = ChunkCells + PaddingCells * 2;
            var origin = (float3)key * ChunkWorldSize - PaddingCells * _voxelSize;
            return new VoxelGridLayout(origin, new int3(cellCount), _voxelSize, cellCount);
        }

        /// <summary>
        /// チャンクごとに距離場の書き込みとメッシュ化を並列に行い、結果をメッシュへ反映する。
        /// 面が無くなったチャンクは破棄する。
        /// </summary>
        private void RebuildChunks(HashSet<int3> dirtyChunks, NativeArray<VoxelMeltParticle> particles,
            NativeParallelMultiHashMap<int3, int> particlesByChunk)
        {
            var count = dirtyChunks.Count;
            if (count == 0) return;

            var keys = new int3[count];
            dirtyChunks.CopyTo(keys);

            var buffers = new ChunkBuffers[count];
            var handles = new NativeArray<JobHandle>(count, Allocator.Temp);
            var reach = SplatReach;
            var emptyDistance = _voxelSize * VoxelVolume.TruncationVoxels;

            for (var i = 0; i < count; i++)
            {
                var layout = CreateChunkLayout(keys[i]);
                buffers[i] = new ChunkBuffers(layout.SampleTotal);

                var splatHandle = new VoxelMeltSplatJob
                {
                    Particles = particles,
                    ParticlesByChunk = particlesByChunk,
                    ChunkKey = keys[i],
                    Field = buffers[i].Field,
                    Layout = layout,
                    RadiusScale = _radiusScale,
                    Blend = _blend,
                    Reach = reach,
                    EmptyDistance = emptyDistance
                }.Schedule();

                handles[i] = new SurfaceNetsJob
                {
                    Samples = buffers[i].Field,
                    Layout = layout,
                    CellMin = new int3(PaddingCells),
                    CellMax = new int3(PaddingCells + ChunkCells),
                    Vertices = buffers[i].Vertices,
                    Normals = buffers[i].Normals,
                    Indices = buffers[i].Indices
                }.Schedule(splatHandle);
            }

            JobHandle.CombineDependencies(handles).Complete();
            handles.Dispose();

            for (var i = 0; i < count; i++)
            {
                ApplyChunk(keys[i], buffers[i]);
                buffers[i].Dispose();
            }
        }

        private void ApplyChunk(int3 key, ChunkBuffers buffers)
        {
            var triangleCount = buffers.Indices.Length / 3;
            _chunks.TryGetValue(key, out var chunk);

            if (triangleCount == 0)
            {
                if (chunk == null) return;

                TriangleCount -= chunk.TriangleCount;
                Object.Destroy(chunk.Mesh);
                _chunks.Remove(key);
                return;
            }

            if (chunk == null)
            {
                chunk = new SurfaceChunk(key);
                _chunks.Add(key, chunk);
            }

            TriangleCount += triangleCount - chunk.TriangleCount;
            chunk.SetMesh(buffers, triangleCount);
        }

        private void ClearChunks()
        {
            foreach (var chunk in _chunks.Values)
            {
                Object.Destroy(chunk.Mesh);
            }

            _chunks.Clear();
            _previousMovingChunks.Clear();
            _pendingChunks.Clear();
            TriangleCount = 0;
        }

        /// <summary>
        /// 1 チャンク分の距離場とメッシュ化の出力先。
        /// </summary>
        private readonly struct ChunkBuffers : IDisposable
        {
            public readonly NativeArray<float> Field;
            public readonly NativeList<float3> Vertices;
            public readonly NativeList<float3> Normals;
            public readonly NativeList<int> Indices;

            public ChunkBuffers(int sampleTotal)
            {
                Field = new NativeArray<float>(sampleTotal, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                Vertices = new NativeList<float3>(256, Allocator.TempJob);
                Normals = new NativeList<float3>(256, Allocator.TempJob);
                Indices = new NativeList<int>(1024, Allocator.TempJob);
            }

            public void Dispose()
            {
                Field.Dispose();
                Vertices.Dispose();
                Normals.Dispose();
                Indices.Dispose();
            }
        }

        /// <summary>
        /// 1 つの液面チャンクのメッシュ。
        /// </summary>
        private sealed class SurfaceChunk
        {
            public SurfaceChunk(int3 key)
            {
                Mesh = new Mesh
                {
                    name = $"VoxelMeltSurface_{key.x}_{key.y}_{key.z}",
                    indexFormat = IndexFormat.UInt32
                };
                Mesh.MarkDynamic();
            }

            public Mesh Mesh { get; }
            public int TriangleCount { get; private set; }

            public void SetMesh(ChunkBuffers buffers, int triangleCount)
            {
                Mesh.Clear();
                Mesh.SetVertices(buffers.Vertices.AsArray());
                Mesh.SetNormals(buffers.Normals.AsArray());
                Mesh.SetIndices(buffers.Indices.AsArray(), MeshTopology.Triangles, 0);
                TriangleCount = triangleCount;
            }
        }
    }
}
