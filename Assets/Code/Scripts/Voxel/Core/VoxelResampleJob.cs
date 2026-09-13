using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 量子化された元の SDF 格子を、ボリュームの各サンプル位置でトリリニア補間して書き込む。
    /// 元の格子の範囲外は +SourceMaxDistance（外側）として扱う。
    /// </summary>
    [BurstCompile]
    public struct VoxelResampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<short> SourceSamples;
        public int3 SourceSampleCount;
        public float3 SourceOrigin;
        public float SourceVoxelSize;
        public float SourceMaxDistance;

        [WriteOnly] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;
        public float TruncationDistance;

        public void Execute(int index)
        {
            var sample = Layout.ToSampleCoord(index);
            var sourceGrid = (Layout.ToLocalPosition(sample) - SourceOrigin) / SourceVoxelSize;

            var distance = math.any(sourceGrid < 0f) || math.any(sourceGrid > (float3)(SourceSampleCount - 1))
                ? SourceMaxDistance
                : SampleTrilinear(sourceGrid);
            distance = math.clamp(distance, -TruncationDistance, TruncationDistance);

            Samples[index] = Layout.EnforceBoundary(sample, distance);
        }

        private float SampleTrilinear(float3 sourceGrid)
        {
            var lower = math.min((int3)math.floor(sourceGrid), SourceSampleCount - 2);
            var t = sourceGrid - lower;

            var c000 = ReadSource(lower);
            var c100 = ReadSource(lower + new int3(1, 0, 0));
            var c010 = ReadSource(lower + new int3(0, 1, 0));
            var c110 = ReadSource(lower + new int3(1, 1, 0));
            var c001 = ReadSource(lower + new int3(0, 0, 1));
            var c101 = ReadSource(lower + new int3(1, 0, 1));
            var c011 = ReadSource(lower + new int3(0, 1, 1));
            var c111 = ReadSource(lower + new int3(1, 1, 1));

            var z0 = math.lerp(math.lerp(c000, c100, t.x), math.lerp(c010, c110, t.x), t.y);
            var z1 = math.lerp(math.lerp(c001, c101, t.x), math.lerp(c011, c111, t.x), t.y);
            return math.lerp(z0, z1, t.z);
        }

        private float ReadSource(int3 sample)
        {
            var index = sample.x + SourceSampleCount.x * (sample.y + SourceSampleCount.y * sample.z);
            return VoxelSdfEncoding.Dequantize(SourceSamples[index], SourceMaxDistance);
        }
    }
}
