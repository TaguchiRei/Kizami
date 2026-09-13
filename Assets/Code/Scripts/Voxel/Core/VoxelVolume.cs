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
