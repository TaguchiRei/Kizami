using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 1 つの液面チャンクの格子へ、そのチャンクに届く粒ごとの球を滑らかにつないだ距離場を書き込む。
    ///
    /// 格子を EmptyDistance で埋めた後、粒ごとに近くのサンプルへ球までの距離を smooth-min で合成する。
    /// 粒の球から Reach より離れたサンプルは書き換えない。
    /// 同じサンプルを複数の粒が書き換える為、1 チャンクの中は並列にせず 1 スレッドで実行する。
    /// </summary>
    [BurstCompile]
    public struct VoxelMeltSplatJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelMeltParticle> Particles;

        /// <summary> 液面チャンクごとの、書き込みが届く粒の添字 </summary>
        [ReadOnly] public NativeParallelMultiHashMap<int3, int> ParticlesByChunk;

        /// <summary> 書き込む液面チャンク </summary>
        public int3 ChunkKey;

        public NativeArray<float> Field;

        /// <summary> このチャンクの格子（余白を含む） </summary>
        public VoxelGridLayout Layout;

        /// <summary> 粒の半径（体積の等しい球の半径）に掛ける倍率 </summary>
        public float RadiusScale;

        /// <summary> 粒同士を滑らかにつなぐ幅（ワールド空間, m）。0 ならつながずに最小値を取る </summary>
        public float Blend;

        /// <summary> 粒の球の表面から、距離を書き込む範囲（ワールド空間, m） </summary>
        public float Reach;

        /// <summary> 粒から遠いサンプルの距離。書き込む距離の上限でもある </summary>
        public float EmptyDistance;

        public void Execute()
        {
            for (var i = 0; i < Field.Length; i++)
            {
                Field[i] = EmptyDistance;
            }

            if (!ParticlesByChunk.TryGetFirstValue(ChunkKey, out var particleIndex, out var iterator)) return;

            do
            {
                Splat(Particles[particleIndex]);
            } while (ParticlesByChunk.TryGetNextValue(out particleIndex, ref iterator));
        }

        private void Splat(in VoxelMeltParticle particle)
        {
            var radius = particle.Radius * RadiusScale;
            var extent = radius + Reach;
            var sampleMin = math.max((int3)math.floor(Layout.ToGridPosition(particle.Position - extent)), 0);
            var sampleMax = math.min((int3)math.ceil(Layout.ToGridPosition(particle.Position + extent)),
                Layout.SampleCount - 1);

            for (var z = sampleMin.z; z <= sampleMax.z; z++)
            for (var y = sampleMin.y; y <= sampleMax.y; y++)
            for (var x = sampleMin.x; x <= sampleMax.x; x++)
            {
                var sample = new int3(x, y, z);
                var sampleIndex = Layout.ToSampleIndex(sample);
                var distance = math.length(Layout.ToLocalPosition(sample) - particle.Position) - radius;

                Field[sampleIndex] = math.min(SmoothMin(Field[sampleIndex], distance, Blend), EmptyDistance);
            }
        }

        /// <summary>
        /// 2 つの距離の最小値を、差が blend より小さいところで滑らかにつないだ値。
        /// </summary>
        private static float SmoothMin(float a, float b, float blend)
        {
            if (blend <= 0f) return math.min(a, b);

            var h = math.max(blend - math.abs(a - b), 0f) / blend;
            return math.min(a, b) - h * h * blend * 0.25f;
        }
    }

    /// <summary>
    /// 粒ごとに、距離場の書き込みが届く液面チャンクを求める。
    /// 全ての粒をチャンクから引ける表と、動いている粒が届くチャンクの集合を作る。
    /// </summary>
    [BurstCompile]
    public struct VoxelMeltChunkKeysJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelMeltParticle> Particles;

        /// <summary> 液面チャンク 1 つの一辺の長さ（ワールド空間, m） </summary>
        public float ChunkWorldSize;

        /// <summary> 粒の半径（体積の等しい球の半径）に掛ける倍率 </summary>
        public float RadiusScale;

        /// <summary> 粒の球の表面から、チャンクの格子（余白を含む）へ影響する範囲（ワールド空間, m） </summary>
        public float Reach;

        public NativeParallelMultiHashMap<int3, int> ParticlesByChunk;
        public NativeParallelHashSet<int3> MovingChunks;

        public void Execute()
        {
            for (var i = 0; i < Particles.Length; i++)
            {
                var particle = Particles[i];
                var extent = particle.Radius * RadiusScale + Reach;
                var chunkMin = (int3)math.floor((particle.Position - extent) / ChunkWorldSize);
                var chunkMax = (int3)math.floor((particle.Position + extent) / ChunkWorldSize);

                for (var z = chunkMin.z; z <= chunkMax.z; z++)
                for (var y = chunkMin.y; y <= chunkMax.y; y++)
                for (var x = chunkMin.x; x <= chunkMax.x; x++)
                {
                    var key = new int3(x, y, z);
                    ParticlesByChunk.Add(key, i);
                    if (!particle.IsFrozen) MovingChunks.Add(key);
                }
            }
        }
    }
}
