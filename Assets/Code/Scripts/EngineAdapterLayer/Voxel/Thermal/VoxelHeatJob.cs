using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// サンプル範囲内の、形状の内側にあるサンプルを加熱し、融解させる。
    ///
    /// 距離は max(距離, (温度 - 1) × 切り詰め距離) へ引き上げる。温度が融点（1）以上なら外側になる。
    /// 温度が 0 のときの引き上げ先は -切り詰め距離 で、切り詰め済みの距離はそれ以上小さくならない為、距離は変わらない。
    ///
    /// 具象の形状ごとに [assembly: RegisterGenericJobType(typeof(VoxelHeatJob&lt;形状&gt;))] で登録すること。
    /// 登録が無いと Burst でコンパイルされない。
    /// </summary>
    [BurstCompile]
    public struct VoxelHeatJob<TShape> : IJobParallelFor where TShape : struct, IVoxelShape
    {
        [NativeDisableParallelForRestriction] public NativeArray<float> Samples;
        [NativeDisableParallelForRestriction] public NativeArray<float> Temperatures;
        public VoxelGridLayout Layout;
        public TShape Shape;

        /// <summary> 形状の中心側で加える温度。負なら冷やす </summary>
        public float Amount;

        /// <summary> 形状の境界から内側へ、加える温度を 0 から Amount まで強めていく幅。0 なら内側全体に Amount を加える </summary>
        public float Falloff;

        public int3 RangeMin;
        public int3 RangeSize;
        public float TruncationDistance;

        /// <summary> 内側から外側になったサンプルの添字の出力先。容量は範囲のサンプル数以上にしておくこと </summary>
        public NativeList<int>.ParallelWriter MeltedSamples;

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
            var temperature = math.max(Temperatures[sampleIndex] + Amount * weight, 0f);
            Temperatures[sampleIndex] = temperature;

            var current = Samples[sampleIndex];
            var result = math.min(math.max(current, (temperature - 1f) * TruncationDistance), TruncationDistance);
            result = Layout.EnforceBoundary(sample, result);
            if (result == current) return;

            Samples[sampleIndex] = result;
            Changed[0] = 1;
            if (current < 0f && result >= 0f) MeltedSamples.AddNoResize(sampleIndex);
        }
    }
}
