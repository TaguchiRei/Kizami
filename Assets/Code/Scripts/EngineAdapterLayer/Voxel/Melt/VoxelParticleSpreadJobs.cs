using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 粒の密度を読む為の、ワールド空間に揃えた疎な格子の計算。
    /// 格子点 c の位置は (c + 0.5) × CellSize で、粒の体積を周りの 8 格子点へ距離に応じて配る（Cloud-in-Cell）。
    /// </summary>
    public static class VoxelParticleDensity
    {
        /// <summary>
        /// 位置を囲む 8 格子点のうち最小側の格子点と、その格子点からの割合（0〜1）を求める。
        /// </summary>
        public static void GetCorner(float3 position, float cellSize, out int3 corner, out float3 fraction)
        {
            var grid = position / cellSize - 0.5f;
            var lower = math.floor(grid);
            corner = (int3)lower;
            fraction = grid - lower;
        }

        /// <summary>
        /// 位置における、格子点へ配られた体積をトリリニア補間で求める。
        /// </summary>
        public static float Sample(in NativeParallelHashMap<int3, float> density, float3 position, float cellSize)
        {
            GetCorner(position, cellSize, out var corner, out var fraction);

            var result = 0f;
            for (var i = 0; i < 8; i++)
            {
                var offset = new int3(i & 1, (i >> 1) & 1, i >> 2);
                if (!density.TryGetValue(corner + offset, out var volume)) continue;

                var weights = math.select(1f - fraction, fraction, offset == 1);
                result += weights.x * weights.y * weights.z * volume;
            }

            return result;
        }
    }

    /// <summary>
    /// 全ての粒の体積を、周りの 8 格子点へ距離に応じて配る。
    /// 同じ格子点へ複数の粒が足し込む為、並列にせず 1 スレッドで実行する。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleDensityJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelMeltParticle> Particles;

        /// <summary> 格子の間隔（ワールド空間, m） </summary>
        public float CellSize;

        /// <summary> 格子点ごとの、配られた体積（ワールド空間, m³）の出力先 </summary>
        public NativeParallelHashMap<int3, float> Density;

        public void Execute()
        {
            foreach (var particle in Particles)
            {
                VoxelParticleDensity.GetCorner(particle.Position, CellSize, out var corner, out var fraction);

                for (var i = 0; i < 8; i++)
                {
                    var offset = new int3(i & 1, (i >> 1) & 1, i >> 2);
                    var weights = math.select(1f - fraction, fraction, offset == 1);
                    var volume = weights.x * weights.y * weights.z * particle.Volume;

                    var key = corner + offset;
                    Density[key] = Density.TryGetValue(key, out var current) ? current + volume : volume;
                }
            }
        }
    }

    /// <summary>
    /// 粒が密集している所で、密度が下がる向きへ粒の速度を足し、粒を押し広げる。
    ///
    /// 位置を直接動かすと面をすり抜けうる為、速度だけを変え、移動と当たり判定は次のフレームの処理に任せる。
    /// 押すのは、このフレームの当たり判定で面に触れた粒だけで、向きは水平に限る。
    /// 空中の粒を押すと落ちている列が飛び散り、上向きに押すと打ち上がって空中で冷える為。
    /// 止まっている粒は動かさないが、密度には数える。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleSpreadJob : IJobParallelFor
    {
        public NativeArray<VoxelMeltParticle> Particles;
        [ReadOnly] public NativeParallelHashMap<int3, float> Density;

        /// <summary> 格子の間隔（ワールド空間, m） </summary>
        public float CellSize;

        /// <summary> 押し出しを始める充填率（格子のセルの体積に対する、配られた体積の割合） </summary>
        public float RestFill;

        /// <summary> 充填率が基準を 1 上回ったときに、押し出す向きへ与える速さ（m/s） </summary>
        public float SpreadSpeed;

        /// <summary> 同じ位置に重なった粒を散らす向きを決める乱数の種 </summary>
        public uint Seed;

        public void Execute(int index)
        {
            var particle = Particles[index];
            if (particle.IsFrozen || !particle.IsTouching) return;

            var cellVolume = CellSize * CellSize * CellSize;
            var fill = VoxelParticleDensity.Sample(Density, particle.Position, CellSize) / cellVolume;
            var excess = fill - RestFill;
            if (excess <= 0f) return;

            var direction = ComputeDirection(particle.Position, index);
            var targetSpeed = SpreadSpeed * math.min(excess, 1f);
            var speedAlong = math.dot(particle.Velocity, direction);
            if (speedAlong >= targetSpeed) return;

            particle.Velocity += direction * (targetSpeed - speedAlong);
            Particles[index] = particle;
        }

        /// <summary>
        /// 密度が下がる向きを、水平に限って求める。
        /// その向きが定まらないとき（同じ位置に重なっている）は、粒ごとに決まる水平の向きにする。
        /// </summary>
        private float3 ComputeDirection(float3 position, int index)
        {
            var half = CellSize * 0.5f;
            var x = new float3(half, 0f, 0f);
            var z = new float3(0f, 0f, half);

            var direction = -new float3(
                VoxelParticleDensity.Sample(Density, position + x, CellSize) -
                VoxelParticleDensity.Sample(Density, position - x, CellSize),
                0f,
                VoxelParticleDensity.Sample(Density, position + z, CellSize) -
                VoxelParticleDensity.Sample(Density, position - z, CellSize));

            var lengthSq = math.lengthsq(direction);
            if (lengthSq > 1e-20f) return direction * math.rsqrt(lengthSq);

            var random = Random.CreateFromIndex(Seed ^ (uint)index);
            var horizontal = random.NextFloat2Direction();
            return new float3(horizontal.x, 0f, horizontal.y);
        }
    }
}
