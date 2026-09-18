using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 融解した粒 1 個分にまとめたサンプル。
    /// </summary>
    public struct VoxelMeltGroup
    {
        /// <summary> まとめたサンプルの位置の平均（ボリュームのローカル空間） </summary>
        public float3 LocalPosition;

        /// <summary> まとめたサンプルの温度の平均 </summary>
        public float Temperature;

        /// <summary> まとめたサンプルの数 </summary>
        public int SampleCount;
    }

    /// <summary>
    /// サンプルを、格子を各軸 Coarseness 個ずつに区切った区画ごとにまとめる。
    /// </summary>
    [BurstCompile]
    public struct VoxelMeltGroupJob : IJob
    {
        [ReadOnly] public NativeArray<int> SampleIndices;
        [ReadOnly] public NativeArray<float> Temperatures;
        public VoxelGridLayout Layout;

        /// <summary> 1 つの区画の、各軸のサンプル数 </summary>
        public int Coarseness;

        /// <summary> 区画ごとのまとめた結果の出力先 </summary>
        public NativeList<VoxelMeltGroup> Groups;

        public void Execute()
        {
            var cellCount = Layout.SampleCount / Coarseness + 1;
            var groupIndices = new NativeHashMap<int, int>(SampleIndices.Length, Allocator.Temp);

            foreach (var sampleIndex in SampleIndices)
            {
                var sample = Layout.ToSampleCoord(sampleIndex);
                var cell = sample / Coarseness;
                var key = cell.x + cellCount.x * (cell.y + cellCount.y * cell.z);
                var position = Layout.ToLocalPosition(sample);
                var temperature = Temperatures[sampleIndex];

                if (groupIndices.TryGetValue(key, out var groupIndex))
                {
                    var group = Groups[groupIndex];
                    group.LocalPosition += position;
                    group.Temperature += temperature;
                    group.SampleCount++;
                    Groups[groupIndex] = group;
                }
                else
                {
                    groupIndices.Add(key, Groups.Length);
                    Groups.Add(new VoxelMeltGroup
                    {
                        LocalPosition = position,
                        Temperature = temperature,
                        SampleCount = 1
                    });
                }
            }

            for (var i = 0; i < Groups.Length; i++)
            {
                var group = Groups[i];
                group.LocalPosition /= group.SampleCount;
                group.Temperature /= group.SampleCount;
                Groups[i] = group;
            }

            groupIndices.Dispose();
        }
    }
}
