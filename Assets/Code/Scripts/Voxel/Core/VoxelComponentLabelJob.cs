using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 内側（距離が負）のサンプルが 6 近傍でつながった 1 つの塊。
    /// </summary>
    public readonly struct VoxelComponent
    {
        /// <summary> VoxelComponentLabelJob が振った塊の番号 </summary>
        public readonly int Label;

        /// <summary> 塊に含まれる内側サンプルの数 </summary>
        public readonly int SampleCount;

        /// <summary> 塊を囲むサンプル範囲 [SampleMin, SampleMax]（両端を含む） </summary>
        public readonly int3 SampleMin;

        public readonly int3 SampleMax;

        public VoxelComponent(int label, int sampleCount, int3 sampleMin, int3 sampleMax)
        {
            Label = label;
            SampleCount = sampleCount;
            SampleMin = sampleMin;
            SampleMax = sampleMax;
        }
    }

    /// <summary>
    /// 内側のサンプルを 6 近傍の連結成分（塊）に分け、サンプルごとに塊の番号を振る。
    /// 外側のサンプルには OutsideLabel を振る。塊の番号は Components の添字と一致する。
    /// </summary>
    [BurstCompile]
    public struct VoxelComponentLabelJob : IJob
    {
        public const int OutsideLabel = -1;
        private const int UnvisitedLabel = -2;

        [ReadOnly] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;

        public NativeArray<int> Labels;
        public NativeList<VoxelComponent> Components;

        public void Execute()
        {
            for (var i = 0; i < Samples.Length; i++)
            {
                Labels[i] = Samples[i] < 0f ? UnvisitedLabel : OutsideLabel;
            }

            var strides = new int3(1, Layout.SampleCount.x, Layout.SampleCount.x * Layout.SampleCount.y);
            var stack = new NativeList<int>(1024, Allocator.Temp);

            for (var start = 0; start < Labels.Length; start++)
            {
                if (Labels[start] != UnvisitedLabel) continue;

                var label = Components.Length;
                var count = 0;
                var sampleMin = new int3(int.MaxValue);
                var sampleMax = new int3(int.MinValue);

                Labels[start] = label;
                stack.Add(start);

                while (stack.Length > 0)
                {
                    var index = stack[stack.Length - 1];
                    stack.RemoveAtSwapBack(stack.Length - 1);

                    var sample = Layout.ToSampleCoord(index);
                    count++;
                    sampleMin = math.min(sampleMin, sample);
                    sampleMax = math.max(sampleMax, sample);

                    for (var axis = 0; axis < 3; axis++)
                    {
                        if (sample[axis] > 0) Visit(index - strides[axis], label, ref stack);
                        if (sample[axis] < Layout.SampleCount[axis] - 1) Visit(index + strides[axis], label, ref stack);
                    }
                }

                Components.Add(new VoxelComponent(label, count, sampleMin, sampleMax));
            }

            stack.Dispose();
        }

        private void Visit(int index, int label, ref NativeList<int> stack)
        {
            if (Labels[index] != UnvisitedLabel) return;

            Labels[index] = label;
            stack.Add(index);
        }
    }
}
