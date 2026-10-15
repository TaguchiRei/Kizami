using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// y のサンプル番号 SampleY の段について、内側のサンプルに素材ごとの重みを、外側のサンプルに 0 を書く。
    /// 出力の添字は x + z × SampleCount.x。
    /// </summary>
    [BurstCompile]
    public struct VoxelLayerWeightsJob : IJob
    {
        public VoxelGridLayout Layout;
        public int SampleY;
        public bool UseMaterials;

        /// <summary> UseMaterials でないときの重み </summary>
        public byte UniformWeight;

        [ReadOnly] public NativeArray<float> Samples;
        [ReadOnly] public NativeArray<byte> MaterialIds;

        /// <summary> UseMaterials のときの、素材の番号ごとの重み </summary>
        [ReadOnly] public NativeArray<byte> MaterialWeights;

        public NativeArray<byte> Weights;

        public void Execute()
        {
            var count = Layout.SampleCount;
            for (var z = 0; z < count.z; z++)
            for (var x = 0; x < count.x; x++)
            {
                var index = Layout.ToSampleIndex(new Unity.Mathematics.int3(x, SampleY, z));
                byte weight = 0;
                if (Samples[index] < 0f)
                {
                    weight = UseMaterials ? MaterialWeights[MaterialIds[index]] : UniformWeight;
                }

                Weights[x + z * count.x] = weight;
            }
        }
    }
}
