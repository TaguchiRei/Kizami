using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 元のボリュームのサンプル範囲を、1 つの塊だけを残して新しい格子へ写す。
    /// 範囲内にある別の塊の内側サンプルは、符号を反転して外側にする。
    /// </summary>
    [BurstCompile]
    public struct VoxelExtractComponentJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> SourceSamples;
        [ReadOnly] public NativeArray<int> SourceLabels;
        public VoxelGridLayout SourceLayout;

        /// <summary> 新しい格子のサンプル 0 に対応する、元の格子のサンプル </summary>
        public int3 SourceSampleMin;

        public int Label;

        [WriteOnly] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;

        public void Execute(int index)
        {
            var sample = Layout.ToSampleCoord(index);
            var sourceIndex = SourceLayout.ToSampleIndex(SourceSampleMin + sample);

            var distance = SourceSamples[sourceIndex];
            if (distance < 0f && SourceLabels[sourceIndex] != Label) distance = -distance;

            Samples[index] = Layout.EnforceBoundary(sample, distance);
        }
    }

    /// <summary>
    /// 元の格子のサンプル範囲の値を、新しい格子へそのまま写す。
    /// </summary>
    [BurstCompile]
    public struct VoxelCopySamplesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> SourceValues;
        public VoxelGridLayout SourceLayout;

        /// <summary> 新しい格子のサンプル 0 に対応する、元の格子のサンプル </summary>
        public int3 SourceSampleMin;

        [WriteOnly] public NativeArray<float> Values;
        public VoxelGridLayout Layout;

        public void Execute(int index)
        {
            var sample = Layout.ToSampleCoord(index);
            Values[index] = SourceValues[SourceLayout.ToSampleIndex(SourceSampleMin + sample)];
        }
    }

    /// <summary>
    /// サンプル範囲 [RangeMin, RangeMax]（両端を含む）内の、指定した塊のサンプルの添字を集める。
    /// </summary>
    [BurstCompile]
    public struct VoxelCollectComponentSamplesJob : IJob
    {
        [ReadOnly] public NativeArray<int> Labels;
        public VoxelGridLayout Layout;
        public int3 RangeMin;
        public int3 RangeMax;
        public int Label;
        public NativeList<int> SampleIndices;

        public void Execute()
        {
            for (var z = RangeMin.z; z <= RangeMax.z; z++)
            for (var y = RangeMin.y; y <= RangeMax.y; y++)
            for (var x = RangeMin.x; x <= RangeMax.x; x++)
            {
                var sampleIndex = Layout.ToSampleIndex(new int3(x, y, z));
                if (Labels[sampleIndex] == Label) SampleIndices.Add(sampleIndex);
            }
        }
    }

    /// <summary>
    /// サンプル範囲内の、指定した塊のサンプルを外側（+TruncationDistance）にする。
    /// </summary>
    [BurstCompile]
    public struct VoxelEraseComponentJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<float> Samples;
        [ReadOnly] public NativeArray<int> Labels;
        public VoxelGridLayout Layout;
        public int3 RangeMin;
        public int3 RangeSize;
        public int Label;
        public float TruncationDistance;

        public void Execute(int index)
        {
            var offset = new int3(
                index % RangeSize.x,
                index / RangeSize.x % RangeSize.y,
                index / (RangeSize.x * RangeSize.y));
            var sampleIndex = Layout.ToSampleIndex(RangeMin + offset);

            if (Labels[sampleIndex] == Label) Samples[sampleIndex] = TruncationDistance;
        }
    }
}
