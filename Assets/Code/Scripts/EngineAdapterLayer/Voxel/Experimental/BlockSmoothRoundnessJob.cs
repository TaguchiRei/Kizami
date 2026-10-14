using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel.Experimental
{
    /// <summary>
    /// ボリューム全体の点ごとの丸み（0〜1）を求める試作。
    /// 読み込んだときの距離から ChangeThreshold より変わった点（削れた所・融けた所）を 1 とし、
    /// 1 セル進むごとに Step ずつ減らしながら周りへ広げ、最後に点の素材ごとの倍率を掛ける。
    /// 広げ方は軸ごとの往復で行う為、減り方はセル数のマンハッタン距離に比例する。
    /// </summary>
    [BurstCompile]
    public struct BlockSmoothRoundnessJob : IJob
    {
        public VoxelGridLayout Layout;

        /// <summary> 変わったとみなす距離の差 </summary>
        public float ChangeThreshold;

        /// <summary> 1 セル離れるごとに減らす丸み </summary>
        public float Step;

        /// <summary> 素材の番号を読むか。読まないなら全点に MaterialScales[0] を掛ける </summary>
        public bool HasMaterials;

        [ReadOnly] public NativeArray<float> Samples;
        [ReadOnly] public NativeArray<float> InitialSamples;
        [ReadOnly] public NativeArray<byte> MaterialIds;

        /// <summary> 素材の番号ごとの丸みの倍率 </summary>
        [ReadOnly] public NativeArray<float> MaterialScales;

        public NativeArray<float> Roundness;

        public void Execute()
        {
            for (var i = 0; i < Roundness.Length; i++)
            {
                Roundness[i] = math.abs(Samples[i] - InitialSamples[i]) > ChangeThreshold ? 1f : 0f;
            }

            for (var axis = 0; axis < 3; axis++)
            {
                Spread(axis);
            }

            for (var i = 0; i < Roundness.Length; i++)
            {
                var scale = MaterialScales[HasMaterials ? MaterialIds[i] : 0];
                Roundness[i] = math.saturate(Roundness[i] * scale);
            }
        }

        /// <summary>
        /// axis 方向の各列を往復し、隣の点の丸みから Step を引いた値の方が大きければ置き換える。
        /// </summary>
        private void Spread(int axis)
        {
            var count = Layout.SampleCount;
            var length = count[axis];
            var axis1 = (axis + 1) % 3;
            var axis2 = (axis + 2) % 3;

            for (var b = 0; b < count[axis2]; b++)
            for (var a = 0; a < count[axis1]; a++)
            {
                var sample = int3.zero;
                sample[axis1] = a;
                sample[axis2] = b;

                var carried = 0f;
                for (var i = 0; i < length; i++)
                {
                    sample[axis] = i;
                    var index = Layout.ToSampleIndex(sample);
                    carried = math.max(Roundness[index], carried - Step);
                    Roundness[index] = carried;
                }

                carried = 0f;
                for (var i = length - 1; i >= 0; i--)
                {
                    sample[axis] = i;
                    var index = Layout.ToSampleIndex(sample);
                    carried = math.max(Roundness[index], carried - Step);
                    Roundness[index] = carried;
                }
            }
        }
    }
}
