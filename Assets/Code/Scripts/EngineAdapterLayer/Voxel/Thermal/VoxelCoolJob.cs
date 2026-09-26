using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// チャンクごとに、受け持つサンプルの温度を一定量下げる。温度は 0 未満にしない。
    /// </summary>
    [BurstCompile]
    public struct VoxelCoolJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<float> Temperatures;
        [ReadOnly] public NativeArray<int> ChunkIndices;
        public VoxelGridLayout Layout;

        /// <summary> 下げる温度 </summary>
        public float Amount;

        /// <summary> チャンクごとの、下げた後の最高温度の出力先。添字は ChunkIndices と一致する </summary>
        [WriteOnly] public NativeArray<float> MaxTemperatures;

        public void Execute(int index)
        {
            Layout.GetChunkSampleRange(Layout.ToChunkCoord(ChunkIndices[index]), out var sampleMin, out var sampleMax);

            var maxTemperature = 0f;
            for (var z = sampleMin.z; z < sampleMax.z; z++)
            for (var y = sampleMin.y; y < sampleMax.y; y++)
            for (var x = sampleMin.x; x < sampleMax.x; x++)
            {
                var sampleIndex = Layout.ToSampleIndex(new int3(x, y, z));
                var temperature = math.max(Temperatures[sampleIndex] - Amount, 0f);
                Temperatures[sampleIndex] = temperature;
                maxTemperature = math.max(maxTemperature, temperature);
            }

            MaxTemperatures[index] = maxTemperature;
        }
    }
}
