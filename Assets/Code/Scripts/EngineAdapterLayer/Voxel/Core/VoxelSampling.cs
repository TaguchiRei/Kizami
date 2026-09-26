using Unity.Collections;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 格子に並んだ値を補間して読む計算。ジョブからも呼べるように static にしている。
    /// </summary>
    public static class VoxelSampling
    {
        /// <summary>
        /// ローカル座標を格子座標（サンプル間隔を 1 とした連続値）へ変換する。
        /// </summary>
        /// <returns>格子の範囲内なら true</returns>
        public static bool TryGetGridPosition(in VoxelGridLayout layout, float3 localPosition, out float3 grid)
        {
            grid = layout.ToGridPosition(localPosition);
            return !math.any(grid < 0f) && !math.any(grid > (float3)(layout.SampleCount - 1));
        }

        /// <summary>
        /// 格子座標の値をトリリニア補間で求める。格子の外を指した場合は、端のサンプルの値になる。
        /// </summary>
        public static float Trilinear(in NativeArray<float> values, in VoxelGridLayout layout, float3 grid)
        {
            var lower = math.clamp((int3)math.floor(grid), 0, layout.SampleCount - 2);
            var t = math.saturate(grid - lower);

            var c000 = values[layout.ToSampleIndex(lower)];
            var c100 = values[layout.ToSampleIndex(lower + new int3(1, 0, 0))];
            var c010 = values[layout.ToSampleIndex(lower + new int3(0, 1, 0))];
            var c110 = values[layout.ToSampleIndex(lower + new int3(1, 1, 0))];
            var c001 = values[layout.ToSampleIndex(lower + new int3(0, 0, 1))];
            var c101 = values[layout.ToSampleIndex(lower + new int3(1, 0, 1))];
            var c011 = values[layout.ToSampleIndex(lower + new int3(0, 1, 1))];
            var c111 = values[layout.ToSampleIndex(lower + new int3(1, 1, 1))];

            var z0 = math.lerp(math.lerp(c000, c100, t.x), math.lerp(c010, c110, t.x), t.y);
            var z1 = math.lerp(math.lerp(c001, c101, t.x), math.lerp(c011, c111, t.x), t.y);
            return math.lerp(z0, z1, t.z);
        }

        /// <summary>
        /// 格子座標での値の勾配を、サンプル 1 つ分の中心差分で求める（ローカル空間, 正規化前）。
        /// </summary>
        public static float3 Gradient(in NativeArray<float> values, in VoxelGridLayout layout, float3 grid)
        {
            var upper = math.max((float3)(layout.SampleCount - 2), 1f);
            var center = math.clamp(grid, 1f, upper);

            var x = new float3(1f, 0f, 0f);
            var y = new float3(0f, 1f, 0f);
            var z = new float3(0f, 0f, 1f);

            return new float3(
                Trilinear(values, layout, center + x) - Trilinear(values, layout, center - x),
                Trilinear(values, layout, center + y) - Trilinear(values, layout, center - y),
                Trilinear(values, layout, center + z) - Trilinear(values, layout, center - z));
        }
    }
}
