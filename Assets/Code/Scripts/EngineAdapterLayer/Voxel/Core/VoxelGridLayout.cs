using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// SDF 格子の寸法・位置・チャンク分割。
    ///
    /// 格子点（サンプル）は各軸 CellCount + 1 個並び、サンプル s のローカル座標は Origin + s * VoxelSize。
    /// セル c はサンプル c と c + 1 に挟まれた立方体。
    /// チャンクはセルを各軸 ChunkSize 個ずつ区切ったもので、メッシュとコライダーの生成単位。
    /// </summary>
    public readonly struct VoxelGridLayout
    {
        /// <summary>
        /// チャンクのメッシュ生成が読むサンプル範囲の、チャンクのセル範囲 [min, max) からのはみ出し量。
        /// SurfaceNetsJob はサンプル [min - 2, max + 2) を読む（隣接セルの頂点に 1、中心差分の法線に 1）。
        /// SurfaceNetsJob の読み取り範囲を変えたらこの値も合わせること。
        /// ずれると、編集後に再メッシュ化されず継ぎ目が開くチャンクが出る。
        /// </summary>
        public const int MeshingReadMargin = 2;

        /// <summary> 最外周のサンプルに強制する最小の距離（ボクセル数換算） </summary>
        private const float BoundaryMinVoxels = 0.01f;

        public readonly float3 Origin;
        public readonly float VoxelSize;
        public readonly int3 CellCount;
        public readonly int3 SampleCount;
        public readonly int ChunkSize;
        public readonly int3 ChunkCount;

        public VoxelGridLayout(float3 origin, int3 cellCount, float voxelSize, int chunkSize)
        {
            Origin = origin;
            VoxelSize = voxelSize;
            CellCount = cellCount;
            SampleCount = cellCount + 1;
            ChunkSize = chunkSize;
            ChunkCount = (cellCount + chunkSize - 1) / chunkSize;
        }

        public int SampleTotal => SampleCount.x * SampleCount.y * SampleCount.z;
        public int ChunkTotal => ChunkCount.x * ChunkCount.y * ChunkCount.z;

        /// <summary>
        /// 境界ボックスを覆う格子を作る。境界の外側に 1 ボクセルの余白を付ける。
        /// </summary>
        public static VoxelGridLayout FromBounds(VoxelBounds bounds, float voxelSize, int chunkSize)
        {
            var padded = bounds.Expand(voxelSize);
            var cellCount = math.max((int3)math.ceil(padded.Size / voxelSize), 1);
            return new VoxelGridLayout(padded.Min, cellCount, voxelSize, chunkSize);
        }

        public int ToSampleIndex(int3 sample)
        {
            return sample.x + SampleCount.x * (sample.y + SampleCount.y * sample.z);
        }

        public int3 ToSampleCoord(int index)
        {
            var yz = index / SampleCount.x;
            return new int3(index % SampleCount.x, yz % SampleCount.y, yz / SampleCount.y);
        }

        public float3 ToLocalPosition(int3 sample)
        {
            return Origin + (float3)sample * VoxelSize;
        }

        /// <summary>
        /// ローカル座標を格子座標（サンプル間隔を 1 とした連続値）に変換する。
        /// </summary>
        public float3 ToGridPosition(float3 localPosition)
        {
            return (localPosition - Origin) / VoxelSize;
        }

        /// <summary>
        /// 格子の最外周のサンプルか。
        /// </summary>
        public bool IsBoundarySample(int3 sample)
        {
            return math.any(sample == 0) || math.any(sample == SampleCount - 1);
        }

        /// <summary>
        /// 最外周のサンプルなら、距離を小さな正の値以上に引き上げる。
        /// 「最外周は常に外側」という不変条件を守る為、ボリュームへ距離を書き込む処理は全てこれを通すこと。
        /// 通さないと、格子の端でメッシュが開く。
        /// </summary>
        public float EnforceBoundary(int3 sample, float distance)
        {
            return IsBoundarySample(sample) ? math.max(distance, VoxelSize * BoundaryMinVoxels) : distance;
        }

        public int ToChunkIndex(int3 chunk)
        {
            return chunk.x + ChunkCount.x * (chunk.y + ChunkCount.y * chunk.z);
        }

        public int3 ToChunkCoord(int index)
        {
            var yz = index / ChunkCount.x;
            return new int3(index % ChunkCount.x, yz % ChunkCount.y, yz / ChunkCount.y);
        }

        /// <summary>
        /// チャンクが面を生成するセル範囲 [cellMin, cellMax) を返す。
        /// </summary>
        public void GetChunkCellRange(int3 chunk, out int3 cellMin, out int3 cellMax)
        {
            cellMin = chunk * ChunkSize;
            cellMax = math.min(cellMin + ChunkSize, CellCount);
        }

        /// <summary>
        /// チャンクが頂点を生成するセル範囲 [cellMin, cellMax) を返す。
        /// 面は隣接する 4 セルの頂点を結ぶ為、面を生成するセル範囲より最小側へ 1 セル広い。
        /// </summary>
        public void GetChunkVertexCellRange(int3 chunk, out int3 cellMin, out int3 cellMax)
        {
            GetChunkCellRange(chunk, out cellMin, out cellMax);
            cellMin = math.max(cellMin - 1, 0);
        }

        /// <summary>
        /// チャンクのメッシュが収まるローカル空間の境界。
        /// </summary>
        public VoxelBounds GetChunkMeshBounds(int3 chunk)
        {
            GetChunkVertexCellRange(chunk, out var cellMin, out var cellMax);
            return new VoxelBounds(ToLocalPosition(cellMin), ToLocalPosition(cellMax));
        }

        /// <summary>
        /// サンプル範囲 [sampleMin, sampleMax]（両端を含む）の変更で、
        /// 再メッシュ化が必要になるチャンク範囲 [chunkMin, chunkMax]（両端を含む）を返す。
        /// </summary>
        public void GetChunksAffectedBySamples(int3 sampleMin, int3 sampleMax, out int3 chunkMin, out int3 chunkMax)
        {
            chunkMin = math.max(FloorDiv(sampleMin - MeshingReadMargin, ChunkSize), 0);
            chunkMax = math.min(FloorDiv(sampleMax + MeshingReadMargin, ChunkSize), ChunkCount - 1);
        }

        private static int3 FloorDiv(int3 value, int divisor)
        {
            return (int3)math.floor((float3)value / divisor);
        }
    }
}
