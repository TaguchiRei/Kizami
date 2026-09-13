using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 凸包コライダーの頂点を選ぶ方向。球面上にほぼ均等に並ぶ Count 方向（フィボナッチ格子）。
    /// </summary>
    public static class VoxelHullDirections
    {
        /// <summary>
        /// 方向の数。凸包の頂点数はこの数以下になる。
        /// 凸包の面数が PhysX の上限 255 を超えると警告が出て部分的な凸包になる為、頂点数を抑えている。
        /// </summary>
        public const int Count = 64;

        private const float GoldenAngle = 2.39996323f;

        public static float3 Get(int index)
        {
            var y = 1f - (index + 0.5f) * 2f / Count;
            var radius = math.sqrt(1f - y * y);
            var phi = index * GoldenAngle;
            return new float3(math.cos(phi) * radius, y, math.sin(phi) * radius);
        }
    }

    /// <summary>
    /// 点群から、VoxelHullDirections の方向ごとに最も外側にある点を選ぶ。点が無ければ何も書き込まない。
    /// </summary>
    [BurstCompile]
    public struct ExtremePointsJob : IJob
    {
        [ReadOnly] public NativeArray<float3> Points;
        [WriteOnly] public NativeArray<float3> Extremes;

        public void Execute()
        {
            if (Points.Length == 0) return;

            for (var d = 0; d < VoxelHullDirections.Count; d++)
            {
                var direction = VoxelHullDirections.Get(d);
                var best = Points[0];
                var bestDot = math.dot(best, direction);

                for (var i = 1; i < Points.Length; i++)
                {
                    var dot = math.dot(Points[i], direction);
                    if (dot <= bestDot) continue;

                    bestDot = dot;
                    best = Points[i];
                }

                Extremes[d] = best;
            }
        }
    }
}
