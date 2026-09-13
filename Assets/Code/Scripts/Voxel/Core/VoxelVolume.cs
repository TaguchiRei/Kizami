using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 格子点ごとに SDF（表面までの符号付き距離。負が内側）を持つボクセルデータ。
    ///
    /// 距離は ±TruncationDistance に切り詰めて保持する。
    /// 格子の最外周のサンプルは常に正（外側）に保たれる為、生成されるメッシュは必ず閉じる。
    /// </summary>
    public sealed class VoxelVolume : IDisposable
    {
        /// <summary>
        /// 切り詰め距離のボクセル数換算。
        /// 法線の計算が表面の前後 2 サンプルの距離の差を使う為、2 より大きく保つこと。
        /// </summary>
        public const float TruncationVoxels = 4f;

        private NativeArray<float> _samples;

        public VoxelGridLayout Layout { get; }
        public float TruncationDistance => Layout.VoxelSize * TruncationVoxels;
        public bool IsCreated => _samples.IsCreated;

        internal NativeArray<float> Samples => _samples;

        /// <summary>
        /// 全サンプルを外側（+切り詰め距離）で埋めた空のボリュームを作る。
        /// </summary>
        public VoxelVolume(VoxelGridLayout layout)
        {
            Layout = layout;
            _samples = new NativeArray<float>(layout.SampleTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            var empty = TruncationDistance;
            for (var i = 0; i < _samples.Length; i++)
            {
                _samples[i] = empty;
            }
        }

        /// <summary>
        /// 形状を CSG 演算で合成する。形状は Layout と同じローカル空間で表す。
        /// </summary>
        /// <param name="shape">合成する形状</param>
        /// <param name="operation">埋めるか削るか</param>
        /// <returns>値が書き換わりうるサンプル範囲。形状がボリュームの外にあれば HasChange が false</returns>
        public VoxelEditResult ApplyEdit<TShape>(in TShape shape, VoxelCsgOperation operation)
            where TShape : struct, IVoxelShape
        {
            // 形状から切り詰め距離より離れたサンプルは、合成しても値が変わらない
            var bounds = shape.Bounds.Expand(TruncationDistance);
            var sampleMin = math.max((int3)math.floor(Layout.ToGridPosition(bounds.Min)), 0);
            var sampleMax = math.min((int3)math.ceil(Layout.ToGridPosition(bounds.Max)), Layout.SampleCount - 1);

            if (math.any(sampleMin > sampleMax)) return default;

            var rangeSize = sampleMax - sampleMin + 1;
            var job = new VoxelCsgJob<TShape>
            {
                Samples = _samples,
                Layout = Layout,
                Shape = shape,
                Operation = operation,
                RangeMin = sampleMin,
                RangeSize = rangeSize,
                TruncationDistance = TruncationDistance
            };
            job.Schedule(rangeSize.x * rangeSize.y * rangeSize.z, 256).Complete();

            return new VoxelEditResult(sampleMin, sampleMax);
        }

        /// <summary>
        /// 事前ベイクした SDF をトリリニア補間でこのボリュームの格子へ写す。既存の値は全て上書きする。
        /// ベイクした範囲の外は外側として扱う。
        /// </summary>
        /// <param name="source">写す元の SDF。Layout と同じローカル空間でベイクしたもの</param>
        public void Resample(VoxelSdfData source)
        {
            var sourceSamples = new NativeArray<short>(source.Samples, Allocator.TempJob);

            new VoxelResampleJob
            {
                SourceSamples = sourceSamples,
                SourceSampleCount = source.SampleCount,
                SourceOrigin = source.Origin,
                SourceVoxelSize = source.VoxelSize,
                SourceMaxDistance = source.MaxDistance,
                Samples = _samples,
                Layout = Layout,
                TruncationDistance = TruncationDistance
            }.Schedule(_samples.Length, 256).Complete();

            sourceSamples.Dispose();
        }

        /// <summary>
        /// 内側のサンプルを 6 近傍でつながった塊に分ける。
        /// </summary>
        /// <param name="labels">サンプルごとの塊の番号の出力先。長さは Layout.SampleTotal。外側は VoxelComponentLabelJob.OutsideLabel</param>
        /// <param name="components">塊の一覧の出力先。添字が塊の番号と一致する</param>
        public void LabelComponents(NativeArray<int> labels, NativeList<VoxelComponent> components)
        {
            new VoxelComponentLabelJob
            {
                Samples = _samples,
                Layout = Layout,
                Labels = labels,
                Components = components
            }.Schedule().Complete();
        }

        /// <summary>
        /// 1 つの塊だけを取り出した新しいボリュームを作る。塊を囲むサンプル範囲に 1 サンプルの余白を付ける。
        /// ボクセルの大きさ・チャンクの大きさ・ローカル空間は元と同じ。
        /// </summary>
        /// <param name="labels">LabelComponents が振った塊の番号</param>
        /// <param name="component">取り出す塊</param>
        public VoxelVolume ExtractComponent(NativeArray<int> labels, in VoxelComponent component)
        {
            // 内側のサンプルは最外周に無い為、1 サンプルの余白を付けても元の格子の範囲に収まる
            var sampleMin = component.SampleMin - 1;
            var sampleMax = component.SampleMax + 1;
            var layout = new VoxelGridLayout(Layout.ToLocalPosition(sampleMin), sampleMax - sampleMin,
                Layout.VoxelSize, Layout.ChunkSize);
            var result = new VoxelVolume(layout);

            new VoxelExtractComponentJob
            {
                SourceSamples = _samples,
                SourceLabels = labels,
                SourceLayout = Layout,
                SourceSampleMin = sampleMin,
                Label = component.Label,
                Samples = result._samples,
                Layout = layout
            }.Schedule(layout.SampleTotal, 256).Complete();

            return result;
        }

        /// <summary>
        /// 1 つの塊のサンプルを外側にする。
        /// </summary>
        /// <param name="labels">LabelComponents が振った塊の番号</param>
        /// <param name="component">消す塊</param>
        public void EraseComponent(NativeArray<int> labels, in VoxelComponent component)
        {
            var rangeSize = component.SampleMax - component.SampleMin + 1;

            new VoxelEraseComponentJob
            {
                Samples = _samples,
                Labels = labels,
                Layout = Layout,
                RangeMin = component.SampleMin,
                RangeSize = rangeSize,
                Label = component.Label,
                TruncationDistance = TruncationDistance
            }.Schedule(rangeSize.x * rangeSize.y * rangeSize.z, 256).Complete();
        }

        public void Dispose()
        {
            if (_samples.IsCreated) _samples.Dispose();
        }
    }

    /// <summary>
    /// ApplyEdit で値が書き換わりうるサンプル範囲 [SampleMin, SampleMax]（両端を含む）。
    /// </summary>
    public readonly struct VoxelEditResult
    {
        public readonly bool HasChange;
        public readonly int3 SampleMin;
        public readonly int3 SampleMax;

        public VoxelEditResult(int3 sampleMin, int3 sampleMax)
        {
            HasChange = true;
            SampleMin = sampleMin;
            SampleMax = sampleMax;
        }
    }
}
