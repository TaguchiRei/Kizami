using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// サンプル範囲内の、形状の内側にあるサンプルを加熱し、融解させる。
    /// 温度はサンプルの素材の融点を 1 とした値で持ち、加える温度を融点で割る。融点の高い素材ほど温まりにくい。
    /// 距離は max(距離, (温度 - 1) × 切り詰め距離) へ引き上げる。温度が 1 以上なら外側になる。
    /// 温度が 0 のときの引き上げ先は -切り詰め距離 で、切り詰め済みの距離はそれ以上小さくならない為、距離は変わらない。
    /// MarkRoundness なら、距離が RoundnessChangeThreshold より変わったサンプルの丸みを最大にする。
    /// 具象の形状ごとに [assembly: RegisterGenericJobType(typeof(VoxelHeatJob&lt;形状&gt;))] で登録すること。
    /// 登録が無いと Burst でコンパイルされない。
    /// </summary>
    [BurstCompile]
    public struct VoxelHeatJob<TShape> : IJobParallelFor where TShape : struct, IVoxelShape
    {
        public VoxelGridLayout Layout;
        public TShape Shape;

        /// <summary> 形状の中心側で加える温度。負なら冷やす </summary>
        public float Amount;

        /// <summary> 形状の境界から内側へ、加える温度を 0 から Amount まで強めていく幅。0 なら内側全体に Amount を加える </summary>
        public float Falloff;

        public int3 RangeMin;
        public int3 RangeSize;
        public float TruncationDistance;
        public bool UseMaterials;

        /// <summary> UseMaterials でないときの、融点の逆数 </summary>
        public float UniformInverseMeltingPoint;

        public bool MarkRoundness;

        /// <summary> 丸みの印を付ける距離の変化（ローカル空間の長さ） </summary>
        public float RoundnessChangeThreshold;

        /// <summary> UseMaterials のときの、素材の番号ごとの融点の逆数 </summary>
        [ReadOnly] public NativeArray<float> InverseMeltingPoints;

        [ReadOnly] public NativeArray<byte> MaterialIds;

        /// <summary> 内側から外側になったサンプルの添字の出力先。容量は範囲のサンプル数以上にしておくこと </summary>
        public NativeList<int>.ParallelWriter MeltedSamples;

        [NativeDisableParallelForRestriction] public NativeArray<float> Samples;
        [NativeDisableParallelForRestriction] public NativeArray<float> Temperatures;

        /// <summary> MarkRoundness のときに印を書く、サンプルごとの丸み </summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> Roundness;

        /// <summary> 距離を書き換えたサンプルがあれば、0 番目に 1 を書く </summary>
        [NativeDisableParallelForRestriction] public NativeArray<int> Changed;

        public void Execute(int index)
        {
            var offset = new int3(
                index % RangeSize.x,
                index / RangeSize.x % RangeSize.y,
                index / (RangeSize.x * RangeSize.y));
            var sample = RangeMin + offset;

            var shapeDistance = Shape.Distance(Layout.ToLocalPosition(sample));
            if (shapeDistance >= 0f) return;

            var sampleIndex = Layout.ToSampleIndex(sample);
            var weight = Falloff > 0f ? math.saturate(-shapeDistance / Falloff) : 1f;
            var inverseMeltingPoint = UseMaterials
                ? InverseMeltingPoints[MaterialIds[sampleIndex]]
                : UniformInverseMeltingPoint;
            var temperature = math.max(Temperatures[sampleIndex] + Amount * weight * inverseMeltingPoint, 0f);
            Temperatures[sampleIndex] = temperature;

            var current = Samples[sampleIndex];
            var result = math.min(math.max(current, (temperature - 1f) * TruncationDistance), TruncationDistance);
            result = Layout.EnforceBoundary(sample, result);
            if (result == current) return;

            Samples[sampleIndex] = result;
            Changed[0] = 1;
            if (MarkRoundness && math.abs(result - current) > RoundnessChangeThreshold)
            {
                Roundness[sampleIndex] = byte.MaxValue;
            }

            if (current < 0f && result >= 0f) MeltedSamples.AddNoResize(sampleIndex);
        }
    }
}
