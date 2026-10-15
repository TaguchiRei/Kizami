using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 格子点ごとに SDF（表面までの符号付き距離。負が内側）と温度を持つボクセルデータ。
    /// 距離は ±TruncationDistance に切り詰めて保持する。
    /// 格子の最外周のサンプルは常に正（外側）に保たれる為、生成されるメッシュは必ず閉じる。
    /// 温度は常温を0、融点を1とした値で、初めて加熱されたときに配列を確保する。
    /// EnableRoundness を呼んだボリュームは、点ごとの丸み（0〜255）も持つ。削る・加熱で距離が変わった点を255にし、周りへ薄めて広げる。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    public sealed class VoxelVolume : IDisposable
    {
        /// <summary>
        /// 切り詰め距離のボクセル数換算。
        /// 法線の計算が表面の前後2サンプルの距離の差を使うので、2より大きく保つこと。
        /// </summary>
        public const float TRUNCATION_VOXELS = 4f;

        private readonly bool[] _isHotChunk;
        private readonly List<int> _hotChunks = new();
        private NativeArray<float> _samples;
        private NativeArray<float> _temperatures;
        private NativeArray<byte> _materialIds;
        private NativeArray<byte> _roundness;
        private float _roundnessChangeThreshold;
        private float _roundnessFalloffCells;

        public VoxelGridLayout Layout { get; }
        public float TruncationDistance => Layout.VoxelSize * TRUNCATION_VOXELS;
        public bool IsCreated => _samples.IsCreated;

        /// <summary> 温度の配列を確保済みか </summary>
        public bool HasTemperatures => _temperatures.IsCreated;

        /// <summary> サンプルごとの素材の番号を持つか </summary>
        public bool HasMaterials => _materialIds.IsCreated;

        /// <summary> 温度が0より高いサンプルを含みうるチャンクがあるか </summary>
        public bool HasHotChunks => _hotChunks.Count > 0;

        /// <summary> 点ごとの丸みを持つか </summary>
        public bool HasRoundness => _roundness.IsCreated;

        /// <summary> 印を付けた点から丸みが広がるセル数。丸みを持たなければ0 </summary>
        public int RoundnessSpreadCells => HasRoundness ? (int)math.ceil(_roundnessFalloffCells) : 0;

        internal NativeArray<float> Samples => _samples;

        /// <summary> サンプルごとの、モデルの素材の一覧での番号。HasMaterials でなければ未作成 </summary>
        internal NativeArray<byte> MaterialIds => _materialIds;

        /// <summary> サンプルごとの丸み。255が削った・融けた点で、そこから離れるほど0に近づく。HasRoundness でなければ未作成 </summary>
        internal NativeArray<byte> Roundness => _roundness;

        /// <summary> 格子のローカル空間での範囲（最外周のサンプルを含む） </summary>
        public VoxelBounds LocalBounds => new(Layout.ToLocalPosition(int3.zero), Layout.ToLocalPosition(Layout.SampleCount - 1));

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
        /// 距離の配列をそのまま受け取ったボリュームを作る。samples の所有権はこのボリュームへ移る。
        /// </summary>
        private VoxelVolume(VoxelGridLayout layout, NativeArray<float> samples)
        {
            Layout = layout;
            _isHotChunk = new bool[layout.ChunkTotal];
            _samples = samples;
        }

        /// <summary>
        /// 同じ格子・同じ距離・同じ温度・同じ素材を持つ複製を作る。冷却の対象のチャンクも引き継ぐ。
        /// </summary>
        public VoxelVolume Clone()
        {
            var result = new VoxelVolume(Layout, new NativeArray<float>(_samples, Allocator.Persistent));

            if (_temperatures.IsCreated)
            {
                result._temperatures = new NativeArray<float>(_temperatures, Allocator.Persistent);
            }

            if (_materialIds.IsCreated)
            {
                result._materialIds = new NativeArray<byte>(_materialIds, Allocator.Persistent);
            }

            if (_roundness.IsCreated)
            {
                result._roundness = new NativeArray<byte>(_roundness, Allocator.Persistent);
                result.CopyRoundnessSettings(this);
            }

            Array.Copy(_isHotChunk, result._isHotChunk, _isHotChunk.Length);
            result._hotChunks.AddRange(_hotChunks);

            return result;
        }

        /// <summary>
        /// 内側（距離が負）のサンプルの数を数える。
        /// </summary>
        public int CountInside()
        {
            var count = new NativeArray<int>(1, Allocator.TempJob);
            new VoxelCountInsideJob
            {
                Samples = _samples,
                Count = count
            }.Schedule().Complete();

            var result = count[0];
            count.Dispose();
            return result;
        }

        /// <summary>
        /// 点ごとの丸みを持たせる。丸みは全点0から始め、以降の削る・加熱で印を付ける。既に持っていれば設定だけを差し替える。
        /// </summary>
        /// <param name="changeVoxels">印を付ける距離の変化（ボクセル数換算）</param>
        /// <param name="falloffCells">印の付いた点から丸みが0になるまでのセル数</param>
        public void EnableRoundness(float changeVoxels, float falloffCells)
        {
            _roundnessChangeThreshold = changeVoxels * Layout.VoxelSize;
            _roundnessFalloffCells = math.max(falloffCells, 1f);
            if (!_roundness.IsCreated) _roundness = new NativeArray<byte>(Layout.SampleTotal, Allocator.Persistent);
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
            return ApplyCsg(shape, operation, 0f, default);
        }

        /// <summary>
        /// 強さと減衰率で削る攻撃を合成する。形状は、融点0の素材まで届く範囲（半径が 強さ ÷ 減衰率）を Layout と同じローカル空間で表す。
        /// 融点 s の素材は、形状の距離に s ÷ 減衰率（ローカル空間の長さ）を足した形で削れ、半径が (強さ − s) ÷ 減衰率 になる。
        /// </summary>
        /// <param name="reach">融点0の素材まで届く範囲</param>
        /// <param name="materialOffsets">素材の番号ごとの 融点 ÷ 減衰率。HasMaterials でなければ使わない</param>
        /// <param name="uniformOffset">素材を持たないときの 融点 ÷ 減衰率</param>
        /// <returns>値が書き換わりうるサンプル範囲。形状がボリュームの外にあれば HasChange が false</returns>
        public VoxelEditResult ApplyStrike<TShape>(in TShape reach, NativeArray<float> materialOffsets,
            float uniformOffset)
            where TShape : struct, IVoxelShape
        {
            return ApplyCsg(reach, VoxelCsgOperation.Subtract, uniformOffset,
                HasMaterials ? materialOffsets : default);
        }

        /// <summary>
        /// 形状の内側のサンプルを加熱し、温度が融点以上になった内側のサンプルを外側にする。
        /// 温度はサンプルの素材の融点を1とした値で持ち、加える温度を融点で割る。
        /// 形状は Layout と同じローカル空間で表す。温度は0未満にならない。
        /// 温度が融点に近づくと表面近くの距離も引き上げる為、融解の境目は格子の段差にならずに進む。
        /// </summary>
        /// <param name="shape">加熱する範囲</param>
        /// <param name="amount">形状の中心側で加える温度。負なら冷やす</param>
        /// <param name="falloff">形状の境界から内側へ、加える温度を0から amount まで強めていく幅。0なら内側全体に amount を加える</param>
        /// <param name="meltedSamples">内側から外側になったサンプルの添字の出力先。呼ぶ前の中身は消す</param>
        /// <param name="inverseMeltingPoints">素材の番号ごとの融点の逆数。HasMaterials でなければ使わない</param>
        /// <param name="uniformInverseMeltingPoint">素材を持たないときの融点の逆数</param>
        /// <returns>距離を書き換えたサンプルを含む範囲。距離が変わらなければ HasChange が false</returns>
        public VoxelEditResult ApplyHeat<TShape>(in TShape shape, float amount, float falloff,
            NativeList<int> meltedSamples, NativeArray<float> inverseMeltingPoints, float uniformInverseMeltingPoint)
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

            var useMaterials = HasMaterials && inverseMeltingPoints.IsCreated;

            // ジョブの入力には作った配列を渡す必要がある為、使わないときは1要素の配列を渡す
            var emptyInverses = useMaterials ? default : new NativeArray<float>(1, Allocator.TempJob);
            var emptyIds = useMaterials ? default : new NativeArray<byte>(1, Allocator.TempJob);

            var emptyRoundness = HasRoundness ? default : new NativeArray<byte>(1, Allocator.TempJob);

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
                Changed = changed,
                UseMaterials = useMaterials,
                UniformInverseMeltingPoint = uniformInverseMeltingPoint,
                InverseMeltingPoints = useMaterials ? inverseMeltingPoints : emptyInverses,
                MaterialIds = useMaterials ? _materialIds : emptyIds,
                MarkRoundness = HasRoundness,
                RoundnessChangeThreshold = _roundnessChangeThreshold,
                Roundness = HasRoundness ? _roundness : emptyRoundness
            }.Schedule(rangeTotal, 256).Complete();

            if (emptyInverses.IsCreated) emptyInverses.Dispose();
            if (emptyIds.IsCreated) emptyIds.Dispose();
            if (emptyRoundness.IsCreated) emptyRoundness.Dispose();

            var hasChange = changed[0] != 0;
            changed.Dispose();

            if (amount > 0f) MarkHot(sampleMin, sampleMax);
            if (hasChange) SpreadRoundness(sampleMin, sampleMax);

            return hasChange ? new VoxelEditResult(sampleMin, sampleMax) : default;
        }

        /// <summary>
        /// 温度が0より高いサンプルを含みうるチャンクの温度を amount 下げる。温度は0未満にならない。
        /// 全サンプルが0になったチャンクは、以降の対象から外す。
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
        /// ローカル座標の温度をトリリニア補間で求める。格子の外と、加熱されたことが無いボリュームでは0を返す。
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

            if (_materialIds.IsCreated) _materialIds.Dispose();
            if (!source.HasMaterials) return;

            _materialIds = new NativeArray<byte>(Layout.SampleTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            var sourceMaterialIds = new NativeArray<byte>(source.MaterialIds, Allocator.TempJob);
            new VoxelResampleMaterialJob
            {
                SourceMaterialIds = sourceMaterialIds,
                SourceSampleCount = source.SampleCount,
                SourceOrigin = source.Origin,
                SourceVoxelSize = source.VoxelSize,
                MaterialIds = _materialIds,
                Layout = Layout
            }.Schedule(_materialIds.Length, 256).Complete();
            sourceMaterialIds.Dispose();
        }

        /// <summary>
        /// y のサンプル番号 sampleY の段について、内側のサンプルに素材ごとの重みを、外側のサンプルに0を書く。
        /// 出力の添字は x + z × SampleCount.x。
        /// </summary>
        /// <param name="materialWeights">素材の番号ごとの重み。HasMaterials でなければ使わない</param>
        /// <param name="uniformWeight">素材を持たないときの重み</param>
        /// <param name="weights">出力先。長さは SampleCount.x × SampleCount.z 以上</param>
        internal void ReadLayerWeights(int sampleY, byte[] materialWeights, byte uniformWeight, byte[] weights)
        {
            var count = Layout.SampleCount;
            var length = count.x * count.z;
            var useMaterials = HasMaterials && materialWeights != null && materialWeights.Length > 0;

            // ジョブの入力には作った配列を渡す必要がある為、使わないときは1要素の配列を渡す
            var nativeMaterialWeights = useMaterials
                ? new NativeArray<byte>(materialWeights, Allocator.TempJob)
                : new NativeArray<byte>(1, Allocator.TempJob);
            var emptyIds = useMaterials ? default : new NativeArray<byte>(1, Allocator.TempJob);
            var output = new NativeArray<byte>(length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            new VoxelLayerWeightsJob
            {
                Layout = Layout,
                SampleY = sampleY,
                UseMaterials = useMaterials,
                UniformWeight = uniformWeight,
                Samples = _samples,
                MaterialIds = useMaterials ? _materialIds : emptyIds,
                MaterialWeights = nativeMaterialWeights,
                Weights = output
            }.Schedule().Complete();

            NativeArray<byte>.Copy(output, weights, length);

            output.Dispose();
            nativeMaterialWeights.Dispose();
            if (emptyIds.IsCreated) emptyIds.Dispose();
        }

        /// <summary>
        /// 内側のサンプルを6近傍でつながった塊に分ける。
        /// </summary>
        /// <param name="labels">サンプルごとの塊の番号の出力先。長さは Layout.SampleTotal。外側は VoxelComponentLabelJob.OUTSIDE_LABEL</param>
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
        /// 1つの塊だけを取り出した新しいボリュームを作る。塊を囲むサンプル範囲に1サンプルの余白を付ける。
        /// ボクセルの大きさ・チャンクの大きさ・ローカル空間は元と同じ。温度・素材・丸みがあれば、同じ範囲の値も写す。
        /// </summary>
        /// <param name="labels">LabelComponents が振った塊の番号</param>
        /// <param name="component">取り出す塊</param>
        public VoxelVolume ExtractComponent(NativeArray<int> labels, in VoxelComponent component)
        {
            // 内側のサンプルは最外周に無い為、1サンプルの余白を付けても元の格子の範囲に収まる
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

                new VoxelCopySamplesJob<float>
                {
                    SourceValues = _temperatures,
                    SourceLayout = Layout,
                    SourceSampleMin = sampleMin,
                    Values = result._temperatures,
                    Layout = layout
                }.Schedule(layout.SampleTotal, 256).Complete();

                result.MarkHot(int3.zero, layout.SampleCount - 1);
            }

            if (_materialIds.IsCreated)
            {
                result._materialIds = new NativeArray<byte>(layout.SampleTotal, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                new VoxelCopySamplesJob<byte>
                {
                    SourceValues = _materialIds,
                    SourceLayout = Layout,
                    SourceSampleMin = sampleMin,
                    Values = result._materialIds,
                    Layout = layout
                }.Schedule(layout.SampleTotal, 256).Complete();
            }

            if (_roundness.IsCreated)
            {
                result._roundness = new NativeArray<byte>(layout.SampleTotal, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                result.CopyRoundnessSettings(this);

                new VoxelCopySamplesJob<byte>
                {
                    SourceValues = _roundness,
                    SourceLayout = Layout,
                    SourceSampleMin = sampleMin,
                    Values = result._roundness,
                    Layout = layout
                }.Schedule(layout.SampleTotal, 256).Complete();
            }

            return result;
        }

        /// <summary>
        /// 1つの塊のサンプルを外側にする。
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
        /// 1つの塊に属するサンプルの添字を集める。
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
        /// </summary>
        /// <param name="sampleIndices">まとめるサンプルの添字</param>
        /// <param name="coarseness">1つの区画の、各軸のサンプル数</param>
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

        /// <summary>
        /// 印を付けうるサンプル範囲 [sampleMin, sampleMax] を広がる幅だけ広げた範囲で、丸みを周りへ広げる。丸みを持たなければ何もしない。
        /// </summary>
        private void SpreadRoundness(int3 sampleMin, int3 sampleMax)
        {
            if (!HasRoundness) return;

            var spread = RoundnessSpreadCells;
            new VoxelRoundnessSpreadJob
            {
                Layout = Layout,
                RangeMin = math.max(sampleMin - spread, 0),
                RangeMax = math.min(sampleMax + spread, Layout.SampleCount - 1),
                Step = byte.MaxValue / _roundnessFalloffCells,
                Roundness = _roundness
            }.Schedule().Complete();
        }

        private void CopyRoundnessSettings(VoxelVolume source)
        {
            _roundnessChangeThreshold = source._roundnessChangeThreshold;
            _roundnessFalloffCells = source._roundnessFalloffCells;
        }

        /// <summary>
        /// 形状の距離にずらし量を足して CSG 合成する。ずらし量は正なので、範囲は形状の範囲に収まる。
        /// </summary>
        /// <param name="materialOffsets">素材の番号ごとのずらし量。作られていなければ uniformOffset を使う</param>
        private VoxelEditResult ApplyCsg<TShape>(in TShape shape, VoxelCsgOperation operation, float uniformOffset,
            NativeArray<float> materialOffsets)
            where TShape : struct, IVoxelShape
        {
            // 形状から切り詰め距離より離れたサンプルは、合成しても値が変わらない
            if (!TryGetSampleRange(shape.Bounds.Expand(TruncationDistance), out var sampleMin, out var sampleMax))
            {
                return default;
            }

            var useMaterials = materialOffsets.IsCreated;

            // ジョブの入力には作った配列を渡す必要がある為、使わないときは1要素の配列を渡す
            var emptyOffsets = useMaterials ? default : new NativeArray<float>(1, Allocator.TempJob);
            var emptyIds = useMaterials ? default : new NativeArray<byte>(1, Allocator.TempJob);
            var emptyRoundness = HasRoundness ? default : new NativeArray<byte>(1, Allocator.TempJob);

            var rangeSize = sampleMax - sampleMin + 1;
            var job = new VoxelCsgJob<TShape>
            {
                Samples = _samples,
                Layout = Layout,
                Shape = shape,
                Operation = operation,
                RangeMin = sampleMin,
                RangeSize = rangeSize,
                TruncationDistance = TruncationDistance,
                UseMaterials = useMaterials,
                UniformOffset = uniformOffset,
                MaterialOffsets = useMaterials ? materialOffsets : emptyOffsets,
                MaterialIds = useMaterials ? _materialIds : emptyIds,
                MarkRoundness = HasRoundness,
                RoundnessChangeThreshold = _roundnessChangeThreshold,
                Roundness = HasRoundness ? _roundness : emptyRoundness
            };
            job.Schedule(rangeSize.x * rangeSize.y * rangeSize.z, 256).Complete();

            if (emptyOffsets.IsCreated) emptyOffsets.Dispose();
            if (emptyIds.IsCreated) emptyIds.Dispose();
            if (emptyRoundness.IsCreated) emptyRoundness.Dispose();

            SpreadRoundness(sampleMin, sampleMax);

            return new VoxelEditResult(sampleMin, sampleMax);
        }

        public void Dispose()
        {
            if (_samples.IsCreated) _samples.Dispose();
            if (_temperatures.IsCreated) _temperatures.Dispose();
            if (_materialIds.IsCreated) _materialIds.Dispose();
            if (_roundness.IsCreated) _roundness.Dispose();
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
