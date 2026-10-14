using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 1 階分ずつベイクしたボクセルのモデルを、縦に積み重ねた大きな建物。ステージシーンへ置く。
    /// 階ごとに別の VoxelModelLoader にし、削ったときの分離の判定を削った階だけで済ませる。
    /// 階は Awake で作り、各 VoxelModelLoader の Start で読み込む。
    /// 模様が階をまたいで続くよう、階ごとにマテリアルを複製し、_PatternOffset の Y に「階の番号 × 階の高さ」を書く。
    /// MaterialPropertyBlock は SRP Batcher の対象から外れる為、使わない。
    /// 隣り合う階の境目ごとに、下の階の上の面と上の階の下の面から半ボクセル内側の段で、両方が中身の点を数えてつながりを判定する。
    /// 判定は階のピースの形が変わったとき（分離の処理のあと）に、その階の上下の境目について行う。1 階の下は地面で、地面側は常に中身とみなす。
    /// 階とパーツは回転させずに置く前提で、点の対応は建物のローカル空間の水平座標を格子の番号にして取る。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/LargeObjectVoxel.md">説明ドキュメント: 巨大なオブジェクトへのボクセルの適用</seealso>
    public sealed class VoxelBuilding : MonoBehaviour
    {
        private static readonly int _patternOffsetId = Shader.PropertyToID("_PatternOffset");

        private readonly List<VoxelModelLoader> _floors = new();
        private readonly List<Material> _floorMaterials = new();
        private readonly List<IDisposable> _subscriptions = new();

        [SerializeField]
        [Tooltip("1 階のモデル")]
        private VoxelModelAsset _groundFloor;

        [SerializeField]
        [Tooltip("2 階から上の階のモデル")]
        private VoxelModelAsset _middleFloor;

        [SerializeField]
        [Tooltip("一番上に積む屋上のモデル。未設定なら積まない")]
        private VoxelModelAsset _roof;

        [SerializeField, Min(1)]
        [Tooltip("屋上を除いた階の数")]
        private int _floorCount = 5;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 階の高さ（m）。ボクセルの大きさの倍数にする")]
        private float _floorHeight = 4f;

        [SerializeField]
        [Tooltip("各階のボクセルの大きさ・チャンクの大きさ・再メッシュ化の上限")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("融解・蒸発した分の送り先と、素材の融点の設定")]
        private VoxelMeltSystem _meltSystem;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("つながっている面積が、元の面積のこの割合を下回ったら切れたとみなす")]
        private float _connectionThreshold = 0.05f;

        [SerializeField, Range(1, 255)]
        [Tooltip("つながりの面積を数えるときの、硬い素材の点の倍率。鉄筋が残る間は倒れにくくする")]
        private int _hardWeight = 4;

        private float[] _initialAreas = Array.Empty<float>();
        private float[] _connectionRatios = Array.Empty<float>();
        private byte[] _layerBuffer = Array.Empty<byte>();

        /// <summary> 面の重みの配列が覆う、建物の水平方向の格子の番号の最小 </summary>
        private int2 _faceMin;

        /// <summary> 面の重みの配列が覆う格子の数 </summary>
        private int2 _faceSize;

        private byte[] _lowerFace = Array.Empty<byte>();
        private byte[] _upperFace = Array.Empty<byte>();
        private int _loadedCount;

        /// <summary> 下から順の階。屋上を積んだ場合は最後の要素 </summary>
        public IReadOnlyList<VoxelModelLoader> Floors => _floors;

        public float FloorHeight => _floorHeight;

        /// <summary> 境目の数。境目 i は、i 階とその下（0 なら地面）の境目 </summary>
        public int JointCount => _connectionRatios.Length;

        /// <summary> 全部の階を読み込み、つながりの元の面積を測ったか </summary>
        public bool IsConnectionReady { get; private set; }

        /// <summary>
        /// 境目 jointIndex の、今のつながっている面積 ÷ 元の面積。元の面積が 0 なら 0。
        /// </summary>
        public float GetConnectionRatio(int jointIndex)
        {
            return _connectionRatios[jointIndex];
        }

        /// <summary>
        /// 境目 jointIndex がつながっているか。
        /// </summary>
        public bool IsConnected(int jointIndex)
        {
            return _connectionRatios[jointIndex] >= _connectionThreshold;
        }

        /// <summary>
        /// floorIndex 階のモデル。1 階・中間階・屋上の順に決まる。
        /// </summary>
        private VoxelModelAsset GetFloorModel(int floorIndex)
        {
            if (floorIndex == 0) return _groundFloor;

            return floorIndex < _floorCount ? _middleFloor : _roof;
        }

        /// <summary>
        /// 階を作り、読み込んだら階ごとのマテリアルを渡す。
        /// </summary>
        private void CreateFloor(int floorIndex, VoxelModelAsset model)
        {
            var floorObject = new GameObject($"Floor_{floorIndex:00}")
            {
                layer = gameObject.layer
            };
            floorObject.transform.SetParent(transform, false);
            floorObject.transform.localPosition = new Vector3(0f, floorIndex * _floorHeight, 0f);

            var loader = floorObject.AddComponent<VoxelModelLoader>();
            loader.SetModel(model);
            loader.SetQuality(_quality);
            loader.SetMeltSystem(_meltSystem);

            var materials = CreateFloorMaterials(model, floorIndex);
            _subscriptions.Add(loader.RegisterOnLoaded(loaded =>
            {
                foreach (var part in loaded.Parts)
                {
                    part.SetMaterials(materials);
                }

                OnFloorLoaded();
            }));
            _subscriptions.Add(loader.RegisterOnPieceShapeChanged(_ => UpdateConnections(floorIndex)));

            _floors.Add(loader);
        }

        /// <summary>
        /// 全部の階が読み込まれたら、各境目の元の面積を測る。
        /// </summary>
        private void OnFloorLoaded()
        {
            _loadedCount++;
            if (_loadedCount < _floors.Count) return;

            InitializeFaces();
            _initialAreas = new float[_floors.Count];
            _connectionRatios = new float[_floors.Count];
            for (var i = 0; i < _floors.Count; i++)
            {
                _initialAreas[i] = MeasureJointArea(i);
                _connectionRatios[i] = _initialAreas[i] > 0f ? 1f : 0f;
            }

            IsConnectionReady = true;
        }

        /// <summary>
        /// floorIndex 階の上下の境目のつながりを数え直す。切れたらログを出す。
        /// </summary>
        private void UpdateConnections(int floorIndex)
        {
            if (!IsConnectionReady) return;

            for (var joint = floorIndex; joint <= floorIndex + 1 && joint < _floors.Count; joint++)
            {
                var wasConnected = IsConnected(joint);
                _connectionRatios[joint] = _initialAreas[joint] > 0f ? MeasureJointArea(joint) / _initialAreas[joint] : 0f;

                if (wasConnected && !IsConnected(joint))
                {
                    UsefulLogger.Log($"{joint} 階の下の境目が切れました（元の面積の {_connectionRatios[joint]:P1}）。", this);
                }
            }
        }

        /// <summary>
        /// 境目 jointIndex で、上の階の下の面と下の階の上の面の両方が中身の点を、重みの大きい方で数える。境目 0 では地面側を常に中身とみなす。
        /// </summary>
        private float MeasureJointArea(int jointIndex)
        {
            var halfVoxel = _quality.VoxelSize * 0.5f;
            ReadFace(jointIndex, halfVoxel, _upperFace);
            var area = 0f;
            if (jointIndex == 0)
            {
                foreach (var weight in _upperFace)
                {
                    area += weight;
                }

                return area;
            }

            ReadFace(jointIndex - 1, _floorHeight - halfVoxel, _lowerFace);
            for (var i = 0; i < _upperFace.Length; i++)
            {
                if (_upperFace[i] != 0 && _lowerFace[i] != 0) area += Math.Max(_upperFace[i], _lowerFace[i]);
            }

            return area;
        }

        /// <summary>
        /// floorIndex 階の、階のローカル空間で高さ floorLocalY の段の中身の点を、建物の水平方向の格子の番号ごとの重みにする。
        /// パーツが重なる点は重みの大きい方を使う。
        /// </summary>
        private void ReadFace(int floorIndex, float floorLocalY, byte[] face)
        {
            Array.Clear(face, 0, face.Length);
            var floor = _floors[floorIndex];

            foreach (var part in floor.Parts)
            {
                var volume = part != null ? part.VolumeData : null;
                if (volume == null) continue;

                var layout = volume.Layout;
                var floorPoint = floor.transform.TransformPoint(0f, floorLocalY, 0f);
                var partLocalY = part.transform.InverseTransformPoint(floorPoint).y;
                var sampleY = (int)math.round((partLocalY - layout.Origin.y) / layout.VoxelSize);
                if (sampleY < 0 || sampleY >= layout.SampleCount.y) continue;

                var count = layout.SampleCount;
                if (_layerBuffer.Length < count.x * count.z) _layerBuffer = new byte[count.x * count.z];
                volume.ReadLayerWeights(sampleY, CreateMaterialWeights(part), 1, _layerBuffer);

                // 回転させずに置く前提なので、建物の格子の番号はサンプル番号と同じだけ進む
                var key = ToFaceKey(part, layout, sampleY) - _faceMin;
                for (var z = 0; z < count.z; z++)
                for (var x = 0; x < count.x; x++)
                {
                    var weight = _layerBuffer[x + z * count.x];
                    var cell = key + new int2(x, z);
                    if (weight == 0 || math.any(cell < 0) || math.any(cell >= _faceSize)) continue;

                    var index = cell.x + cell.y * _faceSize.x;
                    face[index] = Math.Max(face[index], weight);
                }
            }
        }

        /// <summary>
        /// 全部の階のパーツを覆う、建物の水平方向の格子の範囲を求め、面の重みの配列を作る。
        /// </summary>
        private void InitializeFaces()
        {
            var min = new int2(int.MaxValue);
            var max = new int2(int.MinValue);
            foreach (var floor in _floors)
            foreach (var part in floor.Parts)
            {
                var volume = part != null ? part.VolumeData : null;
                if (volume == null) continue;

                var key = ToFaceKey(part, volume.Layout, 0);
                min = math.min(min, key);
                max = math.max(max, key + volume.Layout.SampleCount.xz - 1);
            }

            _faceMin = min;
            _faceSize = math.max(max - min + 1, 0);
            _lowerFace = new byte[_faceSize.x * _faceSize.y];
            _upperFace = new byte[_faceSize.x * _faceSize.y];
        }

        /// <summary>
        /// パーツのサンプル (0, sampleY, 0) の、建物の水平方向の格子の番号。格子の点はボクセルの倍数 ＋ 半ボクセルに並ぶ。
        /// </summary>
        private int2 ToFaceKey(VoxelPiece part, VoxelGridLayout layout, int sampleY)
        {
            var voxelSize = _quality.VoxelSize;
            var origin = transform.InverseTransformPoint(
                part.transform.TransformPoint(layout.ToLocalPosition(new int3(0, sampleY, 0))));
            return new int2(
                (int)math.round(origin.x / voxelSize - 0.5f),
                (int)math.round(origin.z / voxelSize - 0.5f));
        }

        /// <summary>
        /// パーツの素材の番号ごとの、つながりの面積の重みを作る。
        /// </summary>
        private byte[] CreateMaterialWeights(VoxelPiece part)
        {
            var weights = new byte[part.Strengths.Count];
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = part.Strengths[i] == VoxelStrength.Hard ? (byte)_hardWeight : (byte)1;
            }

            return weights;
        }

        /// <summary>
        /// モデルの素材のマテリアルを複製し、模様を階の高さだけずらす。同じマテリアルは階の中で 1 つの複製を使い回す。
        /// </summary>
        private VoxelMaterialEntry[] CreateFloorMaterials(VoxelModelAsset model, int floorIndex)
        {
            var offset = new Vector4(0f, floorIndex * _floorHeight, 0f, 0f);
            var copies = new Dictionary<Material, Material>();
            var entries = new VoxelMaterialEntry[model.Materials.Count];

            for (var i = 0; i < entries.Length; i++)
            {
                var source = model.Materials[i].Material;
                if (source != null && !copies.TryGetValue(source, out var copy))
                {
                    copy = new Material(source) { name = $"{source.name}_Floor{floorIndex:00}" };
                    if (copy.HasProperty(_patternOffsetId)) copy.SetVector(_patternOffsetId, offset);
                    copies.Add(source, copy);
                    _floorMaterials.Add(copy);
                }

                entries[i] = new VoxelMaterialEntry(source != null ? copies[source] : null, model.Materials[i].Strength);
            }

            return entries;
        }

        private void Awake()
        {
            if (_groundFloor == null || _middleFloor == null || _quality == null)
            {
                UsefulLogger.LogError("1 階・中間階のモデル、または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            var layerCount = _floorCount + (_roof != null ? 1 : 0);
            for (var i = 0; i < layerCount; i++)
            {
                CreateFloor(i, GetFloorModel(i));
            }
        }

        private void OnDestroy()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();

            foreach (var material in _floorMaterials)
            {
                Destroy(material);
            }

            _floorMaterials.Clear();
        }
    }
}
