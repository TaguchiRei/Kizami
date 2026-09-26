using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 格子点ごとに SDF（表面までの符号付き距離。負が内側）と温度を持つボクセルデータ。
    ///
    /// 距離は ±TruncationDistance に切り詰めて保持する。
    /// 格子の最外周のサンプルは常に正（外側）に保たれる為、生成されるメッシュは必ず閉じる。
    /// 温度は常温を 0、融点を 1 とした値で、初めて加熱されたときに配列を確保する。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    public sealed class VoxelVolume : IDisposable
    {
        /// <summary>
        /// 切り詰め距離のボクセル数換算。
        /// 法線の計算が表面の前後 2 サンプルの距離の差を使う為、2 より大きく保つこと。
        /// </summary>
        public const float TruncationVoxels = 4f;

        private readonly bool[] _isHotChunk;
        private readonly List<int> _hotChunks = new();
        private NativeArray<float> _samples;
        private NativeArray<float> _temperatures;

        public VoxelGridLayout Layout { get; }
        public float TruncationDistance => Layout.VoxelSize * TruncationVoxels;
        public bool IsCreated => _samples.IsCreated;

        /// <summary> 温度の配列を確保済みか </summary>
        public bool HasTemperatures => _temperatures.IsCreated;

        /// <summary> 温度が 0 より高いサンプルを含みうるチャンクがあるか </summary>
        public bool HasHotChunks => _hotChunks.Count > 0;

        internal NativeArray<float> Samples => _samples;

        /// <summary>
        /// 全サンプルを外側（+切り詰め距離）で埋めた空のボリュームを作る。
        /// </summary>
        public VoxelVolume(VoxelGridLayout layout)
        {
            Layout = layout;
            _isHotChunk = new bool[layout.ChunkTotal];
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
            if (!TryGetSampleRange(shape.Bounds.Expand(TruncationDistance), out var sampleMin, out var sampleMax))
            {
                return default;
            }

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
        /// 形状の内側のサンプルを加熱し、温度が融点（1）以上になった内側のサンプルを外側にする。
        /// 形状は Layout と同じローカル空間で表す。温度は 0 未満にならない。
        /// 温度が融点に近づくと表面近くの距離も引き上げる為、融解の境目は格子の段差にならずに進む。
        /// </summary>
        /// <param name="shape">加熱する範囲</param>
        /// <param name="amount">形状の中心側で加える温度。負なら冷やす</param>
        /// <param name="falloff">形状の境界から内側へ、加える温度を 0 から amount まで強めていく幅。0 なら内側全体に amount を加える</param>
        /// <param name="meltedSamples">内側から外側になったサンプルの添字の出力先。呼ぶ前の中身は消す</param>
        /// <returns>距離を書き換えたサンプルを含む範囲。距離が変わらなければ HasChange が false</returns>
        public VoxelEditResult ApplyHeat<TShape>(in TShape shape, float amount, float falloff,
            NativeList<int> meltedSamples)
            where TShape : struct, IVoxelShape
        {
            meltedSamples.Clear();
            if (!TryGetSampleRange(shape.Bounds, out var sampleMin, out var sampleMax)) return default;

            if (!_temperatures.IsCreated)
            {
                _temperatures = new NativeArray<float>(Layout.SampleTotal, Allocator.Persistent);
            }

            var rangeSize = sampleMax - sampleMin + 1;
            var rangeTotal = rangeSize.x * rangeSize.y * rangeSize.z;
            if (meltedSamples.Capacity < rangeTotal) meltedSamples.Capacity = rangeTotal;

            var changed = new NativeArray<int>(1, Allocator.TempJob);
            new VoxelHeatJob<TShape>
            {
                Samples = _samples,
                Temperatures = _temperatures,
                Layout = Layout,
                Shape = shape,
                Amount = amount,
                Falloff = falloff,
                RangeMin = sampleMin,
                RangeSize = rangeSize,
                TruncationDistance = TruncationDistance,
                MeltedSamples = meltedSamples.AsParallelWriter(),
                Changed = changed
            }.Schedule(rangeTotal, 256).Complete();

            var hasChange = changed[0] != 0;
            changed.Dispose();

            if (amount > 0f) MarkHot(sampleMin, sampleMax);

            return hasChange ? new VoxelEditResult(sampleMin, sampleMax) : default;
        }

        /// <summary>
        /// 温度が 0 より高いサンプルを含みうるチャンクの温度を amount 下げる。温度は 0 未満にならない。
        /// 全サンプルが 0 になったチャンクは、以降の対象から外す。
        /// </summary>
        public void Cool(float amount)
        {
            if (_hotChunks.Count == 0 || amount <= 0f) return;

            var chunkIndices = new NativeArray<int>(_hotChunks.Count, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var maxTemperatures = new NativeArray<float>(_hotChunks.Count, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < _hotChunks.Count; i++)
            {
                chunkIndices[i] = _hotChunks[i];
            }

            new VoxelCoolJob
            {
                Temperatures = _temperatures,
                ChunkIndices = chunkIndices,
                Layout = Layout,
                Amount = amount,
                MaxTemperatures = maxTemperatures
            }.Schedule(chunkIndices.Length, 1).Complete();

            _hotChunks.Clear();
            for (var i = 0; i < chunkIndices.Length; i++)
            {
                if (maxTemperatures[i] > 0f)
                {
                    _hotChunks.Add(chunkIndices[i]);
                }
                else
                {
                    _isHotChunk[chunkIndices[i]] = false;
                }
            }

            chunkIndices.Dispose();
            maxTemperatures.Dispose();
        }

        /// <summary>
        /// ローカル座標の距離をトリリニア補間で求める。格子の外は +TruncationDistance（外側）を返す。
        /// </summary>
        public float SampleDistance(float3 localPosition)
        {
            return VoxelSampling.TryGetGridPosition(Layout, localPosition, out var grid)
                ? VoxelSampling.Trilinear(_samples, Layout, grid)
                : TruncationDistance;
        }

        /// <summary>
        /// ローカル座標の温度をトリリニア補間で求める。格子の外と、加熱されたことが無いボリュームでは 0 を返す。
        /// </summary>
        public float SampleTemperature(float3 localPosition)
        {
            if (!_temperatures.IsCreated) return 0f;

            return VoxelSampling.TryGetGridPosition(Layout, localPosition, out var grid)
                ? VoxelSampling.Trilinear(_temperatures, Layout, grid)
                : 0f;
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
        /// ボクセルの大きさ・チャンクの大きさ・ローカル空間は元と同じ。温度があれば、同じ範囲の温度も写す。
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

            if (_temperatures.IsCreated)
            {
                result._temperatures = new NativeArray<float>(layout.SampleTotal, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                new VoxelCopySamplesJob
                {
                    SourceValues = _temperatures,
                    SourceLayout = Layout,
                    SourceSampleMin = sampleMin,
                    Values = result._temperatures,
                    Layout = layout
                }.Schedule(layout.SampleTotal, 256).Complete();

                result.MarkHot(int3.zero, layout.SampleCount - 1);
            }

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

        /// <summary>
        /// 1 つの塊に属するサンプルの添字を集める。
        /// </summary>
        /// <param name="labels">LabelComponents が振った塊の番号</param>
        /// <param name="component">集める塊</param>
        /// <param name="sampleIndices">添字の出力先。呼ぶ前の中身に追加する</param>
        public void CollectComponentSamples(NativeArray<int> labels, in VoxelComponent component,
            NativeList<int> sampleIndices)
        {
            new VoxelCollectComponentSamplesJob
            {
                Labels = labels,
                Layout = Layout,
                RangeMin = component.SampleMin,
                RangeMax = component.SampleMax,
                Label = component.Label,
                SampleIndices = sampleIndices
            }.Schedule().Complete();
        }

        /// <summary>
        /// サンプルを、格子を各軸 coarseness 個ずつに区切った区画ごとにまとめ、位置と温度の平均を求める。
        /// 温度の配列が無ければ何もしない。
        /// </summary>
        /// <param name="sampleIndices">まとめるサンプルの添字</param>
        /// <param name="coarseness">1 つの区画の、各軸のサンプル数</param>
        /// <param name="groups">区画ごとの結果の出力先。呼ぶ前の中身は消す</param>
        public void GroupSamples(NativeArray<int> sampleIndices, int coarseness, NativeList<VoxelMeltGroup> groups)
        {
            groups.Clear();
            if (!_temperatures.IsCreated || sampleIndices.Length == 0) return;

            new VoxelMeltGroupJob
            {
                SampleIndices = sampleIndices,
                Temperatures = _temperatures,
                Layout = Layout,
                Coarseness = math.max(coarseness, 1),
                Groups = groups
            }.Schedule().Complete();
        }

        public void Dispose()
        {
            if (_samples.IsCreated) _samples.Dispose();
            if (_temperatures.IsCreated) _temperatures.Dispose();
        }

        /// <summary>
        /// ローカル空間の範囲を、格子に収まるサンプル範囲 [sampleMin, sampleMax]（両端を含む）へ変換する。
        /// </summary>
        /// <returns>範囲が格子と重ならなければ false</returns>
        private bool TryGetSampleRange(VoxelBounds bounds, out int3 sampleMin, out int3 sampleMax)
        {
            sampleMin = math.max((int3)math.floor(Layout.ToGridPosition(bounds.Min)), 0);
            sampleMax = math.min((int3)math.ceil(Layout.ToGridPosition(bounds.Max)), Layout.SampleCount - 1);
            return !math.any(sampleMin > sampleMax);
        }

        /// <summary>
        /// サンプル範囲 [sampleMin, sampleMax] を受け持つチャンクを、冷却の対象にする。
        /// </summary>
        private void MarkHot(int3 sampleMin, int3 sampleMax)
        {
            var chunkMin = Layout.ToChunkCoordOfSample(sampleMin);
            var chunkMax = Layout.ToChunkCoordOfSample(sampleMax);

            for (var z = chunkMin.z; z <= chunkMax.z; z++)
            for (var y = chunkMin.y; y <= chunkMax.y; y++)
            for (var x = chunkMin.x; x <= chunkMax.x; x++)
            {
                var chunkIndex = Layout.ToChunkIndex(new int3(x, y, z));
                if (_isHotChunk[chunkIndex]) continue;

                _isHotChunk[chunkIndex] = true;
                _hotChunks.Add(chunkIndex);
            }
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
