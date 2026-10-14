using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 1 階分ずつベイクしたボクセルのモデルを、縦に積み重ねた大きな建物。ステージシーンへ置く。
    /// 階ごとに別の VoxelModelLoader にし、削ったときの分離の判定を削った階だけで済ませる。
    /// 階は Awake で作り、Start で手付かずの形のメッシュ（VoxelModelLoader.LoadStatic）で置く。
    /// プレイヤーから差し替える距離の中にかかった階と、攻撃が当たった階（VoxelDestructionAdapter が読み込む）だけをボクセルにする。
    /// 模様が階をまたいで続くよう、階ごとにマテリアルを複製し、_PatternOffset の Y に「階の番号 × 階の高さ」を書く。
    /// MaterialPropertyBlock は SRP Batcher の対象から外れる為、使わない。
    /// 隣り合う階の境目ごとに、下の階の上の面と上の階の下の面から半ボクセル内側の段で、両方が中身の点を数えてつながりを判定する。
    /// 判定は階のピースの形が変わったとき（分離の処理のあと）に、その階の上下の境目について行う。1 階の下は地面で、地面側は常に中身とみなす。
    /// 階とパーツは回転させずに置く前提で、点の対応は建物のローカル空間の水平座標を格子の番号にして取る。
    /// 境目が切れたら、その上の階をまとめて 1 つの Rigidbody の子にして落とす。当たり判定は各階のチャンクの BoxCollider をそのまま使う。
    /// 落ちた Rigidbody が眠ったらキネマティックにして止め、地形として残す。落ちた階は、以後つながりを判定しない。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/LargeObjectVoxel.md">説明ドキュメント: 巨大なオブジェクトへのボクセルの適用</seealso>
    public sealed class VoxelBuilding : MonoBehaviour
    {
        private static readonly int _patternOffsetId = Shader.PropertyToID("_PatternOffset");

        private readonly List<VoxelModelLoader> _floors = new();
        private readonly List<Material> _floorMaterials = new();
        private readonly List<IDisposable> _subscriptions = new();
        private readonly List<Rigidbody> _fallingBodies = new();
        private readonly ActionChannel<Rigidbody> _collapsed = new();

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

        [SerializeField, Min(0.001f)]
        [Tooltip("倒れる階のまとまりの密度（kg/m³）。質量は ピースの体積の合計 × 密度")]
        private float _collapseDensity = 500f;

        [SerializeField]
        [Tooltip("差し替えの距離を測るプレイヤー。未設定なら MainCamera から測る")]
        private Transform _target;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーからこの距離（m）の中にかかるメッシュのままの階を、ボクセルにする")]
        private float _swapDistance = 40f;

        private float[] _initialAreas = Array.Empty<float>();
        private float[] _connectionRatios = Array.Empty<float>();
        private byte[] _layerBuffer = Array.Empty<byte>();

        /// <summary> 面の重みの配列が覆う、建物の水平方向の格子の番号の最小 </summary>
        private int2 _faceMin;

        /// <summary> 面の重みの配列が覆う格子の数 </summary>
        private int2 _faceSize;

        private byte[] _lowerFace = Array.Empty<byte>();
        private byte[] _upperFace = Array.Empty<byte>();
        private Bounds[] _floorBounds = Array.Empty<Bounds>();

        /// <summary> 階ごとの、素材の番号ごとのつながりの面積の重み </summary>
        private byte[][] _materialWeights = Array.Empty<byte[]>();

        /// <summary> 倒れずに立っている階の数。この番号から上の階は倒れた </summary>
        private int _standingFloorCount;

        /// <summary> 下から順の階。屋上を積んだ場合は最後の要素 </summary>
        public IReadOnlyList<VoxelModelLoader> Floors => _floors;

        public float FloorHeight => _floorHeight;

        /// <summary> 境目の数。境目 i は、i 階とその下（0 なら地面）の境目 </summary>
        public int JointCount => _connectionRatios.Length;

        /// <summary> 全部の階を置き、つながりの元の面積を測ったか </summary>
        public bool IsConnectionReady { get; private set; }

        /// <summary>
        /// 境目が切れて上の階が倒れ始めたときに呼ぶ処理を登録する。引数は、倒れた階をまとめた Rigidbody。
        /// 倒れた階のピースは、その Rigidbody の子孫にある。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnCollapsed(Action<Rigidbody> callback)
        {
            return _collapsed.Register(callback);
        }

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
        /// 階を作る。読み込みは Start で、手付かずの形のメッシュから始める。
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
            loader.SetLoadOnStart(false);
            loader.SetMaterialEntries(CreateFloorMaterials(model, floorIndex));
            _subscriptions.Add(loader.RegisterOnPieceShapeChanged(_ => UpdateConnections(floorIndex)));

            _floors.Add(loader);
        }

        /// <summary>
        /// 全部の階を手付かずの形のメッシュで置き、各境目の元の面積と、差し替えの判定に使う階の範囲を測る。
        /// </summary>
        private void InitializeFloors()
        {
            _floorBounds = new Bounds[_floors.Count];
            _materialWeights = new byte[_floors.Count][];
            for (var i = 0; i < _floors.Count; i++)
            {
                _floors[i].LoadStatic();
                _floorBounds[i] = MeasureFloorBounds(_floors[i]);
                _materialWeights[i] = CreateMaterialWeights(i);
            }

            InitializeFaces();
            _initialAreas = new float[_floors.Count];
            _connectionRatios = new float[_floors.Count];
            for (var i = 0; i < _floors.Count; i++)
            {
                _initialAreas[i] = MeasureJointArea(i);
                _connectionRatios[i] = _initialAreas[i] > 0f ? 1f : 0f;
            }

            _standingFloorCount = _floors.Count;
            IsConnectionReady = true;
        }

        /// <summary>
        /// メッシュのままの階のうち、プレイヤーから差し替える距離の中にかかる階を、近い順に 1 フレーム 1 階ずつボクセルにする。
        /// 1 階の差し替えに数 ms かかる為、近づいて何階も範囲に入っても 1 フレームに集めない。
        /// </summary>
        private void SwapNearFloors()
        {
            // プレイヤーは常駐シーンにいて参照を持てない為、未設定なら MainCamera から測る
            var target = _target != null ? _target : Camera.main != null ? Camera.main.transform : null;
            if (target == null) return;

            var position = target.position;
            var nearest = -1;
            var nearestSqrDistance = _swapDistance * _swapDistance;
            for (var i = 0; i < _standingFloorCount; i++)
            {
                if (!_floors[i].IsStatic) continue;

                var sqrDistance = _floorBounds[i].SqrDistance(position);
                if (sqrDistance > nearestSqrDistance) continue;

                nearest = i;
                nearestSqrDistance = sqrDistance;
            }

            if (nearest >= 0) _floors[nearest].Load();
        }

        private Bounds MeasureFloorBounds(VoxelModelLoader floor)
        {
            var bounds = new Bounds(floor.transform.position, Vector3.zero);
            foreach (var staticPart in floor.StaticParts)
            {
                var localBounds = staticPart.Shape.Mesh.bounds;
                bounds.Encapsulate(staticPart.Target.TransformPoint(localBounds.min));
                bounds.Encapsulate(staticPart.Target.TransformPoint(localBounds.max));
            }

            return bounds;
        }

        /// <summary>
        /// floorIndex 階の上下の境目のつながりを数え直す。切れたらログを出す。
        /// </summary>
        private void UpdateConnections(int floorIndex)
        {
            if (!IsConnectionReady || floorIndex >= _standingFloorCount) return;

            for (var joint = floorIndex; joint <= floorIndex + 1 && joint < _standingFloorCount; joint++)
            {
                var wasConnected = IsConnected(joint);
                _connectionRatios[joint] = _initialAreas[joint] > 0f ? MeasureJointArea(joint) / _initialAreas[joint] : 0f;

                if (wasConnected && !IsConnected(joint))
                {
                    UsefulLogger.Log($"{joint} 階の下の境目が切れました（元の面積の {_connectionRatios[joint]:P1}）。", this);
                    Collapse(joint);
                    return;
                }
            }
        }

        /// <summary>
        /// firstFloor 階から上の立っている階を、1 つの Rigidbody の子にまとめて落とす。
        /// </summary>
        private void Collapse(int firstFloor)
        {
            var groupObject = new GameObject($"Collapsed_{firstFloor:00}")
            {
                layer = gameObject.layer
            };
            groupObject.transform.SetParent(transform, false);

            var volume = 0f;
            for (var i = firstFloor; i < _standingFloorCount; i++)
            {
                // メッシュのままの階もボクセルにし、敵の格子と崩落の判定がピースとして見張れるようにする
                if (_floors[i].IsStatic) _floors[i].Load();

                _floors[i].transform.SetParent(groupObject.transform, true);
                foreach (var part in _floors[i].Parts)
                {
                    if (part != null) volume += part.Volume;
                }
            }

            _standingFloorCount = firstFloor;

            var body = groupObject.AddComponent<Rigidbody>();
            body.mass = math.max(volume * _collapseDensity, 0.01f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            _fallingBodies.Add(body);
            _collapsed.Invoke(body);
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
        /// ボクセルの階はピースの今の形、メッシュのままの階は手付かずの形から読む。パーツが重なる点は重みの大きい方を使う。
        /// </summary>
        private void ReadFace(int floorIndex, float floorLocalY, byte[] face)
        {
            Array.Clear(face, 0, face.Length);
            var floor = _floors[floorIndex];
            var floorPoint = floor.transform.TransformPoint(0f, floorLocalY, 0f);
            var weights = _materialWeights[floorIndex];

            foreach (var part in floor.Parts)
            {
                var volume = part != null ? part.VolumeData : null;
                if (volume == null) continue;

                if (!TryGetSampleY(part.transform, volume.Layout, floorPoint, out var sampleY)) continue;

                PrepareLayerBuffer(volume.Layout);
                volume.ReadLayerWeights(sampleY, weights, 1, _layerBuffer);
                AddLayer(part.transform, volume.Layout, sampleY, face);
            }

            foreach (var staticPart in floor.StaticParts)
            {
                var layout = staticPart.Shape.Layout;
                if (!TryGetSampleY(staticPart.Target, layout, floorPoint, out var sampleY)) continue;

                PrepareLayerBuffer(layout);
                staticPart.Shape.ReadLayerWeights(sampleY, weights, 1, _layerBuffer);
                AddLayer(staticPart.Target, layout, sampleY, face);
            }
        }

        private bool TryGetSampleY(Transform partTransform, VoxelGridLayout layout, Vector3 worldPoint,
            out int sampleY)
        {
            var partLocalY = partTransform.InverseTransformPoint(worldPoint).y;
            sampleY = (int)math.round((partLocalY - layout.Origin.y) / layout.VoxelSize);
            return sampleY >= 0 && sampleY < layout.SampleCount.y;
        }

        private void PrepareLayerBuffer(VoxelGridLayout layout)
        {
            var length = layout.SampleCount.x * layout.SampleCount.z;
            if (_layerBuffer.Length < length) _layerBuffer = new byte[length];
        }

        /// <summary>
        /// _layerBuffer に読んだ 1 段分の重みを、面の配列へ重みの大きい方で重ねる。
        /// </summary>
        private void AddLayer(Transform partTransform, VoxelGridLayout layout, int sampleY, byte[] face)
        {
            var count = layout.SampleCount;

            // 回転させずに置く前提なので、建物の格子の番号はサンプル番号と同じだけ進む
            var key = ToFaceKey(partTransform, layout, sampleY) - _faceMin;
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

        /// <summary>
        /// 全部の階の手付かずの形を覆う、建物の水平方向の格子の範囲を求め、面の重みの配列を作る。
        /// ボクセルにしたあとのピースの格子は手付かずの形と同じなので、範囲は変わらない。
        /// </summary>
        private void InitializeFaces()
        {
            var min = new int2(int.MaxValue);
            var max = new int2(int.MinValue);
            foreach (var floor in _floors)
            foreach (var staticPart in floor.StaticParts)
            {
                var layout = staticPart.Shape.Layout;
                var key = ToFaceKey(staticPart.Target, layout, 0);
                min = math.min(min, key);
                max = math.max(max, key + layout.SampleCount.xz - 1);
            }

            _faceMin = min;
            _faceSize = math.max(max - min + 1, 0);
            _lowerFace = new byte[_faceSize.x * _faceSize.y];
            _upperFace = new byte[_faceSize.x * _faceSize.y];
        }

        /// <summary>
        /// パーツのサンプル (0, sampleY, 0) の、建物の水平方向の格子の番号。格子の点はボクセルの倍数 ＋ 半ボクセルに並ぶ。
        /// </summary>
        private int2 ToFaceKey(Transform partTransform, VoxelGridLayout layout, int sampleY)
        {
            var voxelSize = _quality.VoxelSize;
            var origin = transform.InverseTransformPoint(
                partTransform.TransformPoint(layout.ToLocalPosition(new int3(0, sampleY, 0))));
            return new int2(
                (int)math.round(origin.x / voxelSize - 0.5f),
                (int)math.round(origin.z / voxelSize - 0.5f));
        }

        /// <summary>
        /// floorIndex 階の素材の番号ごとの、つながりの面積の重みを作る。
        /// </summary>
        private byte[] CreateMaterialWeights(int floorIndex)
        {
            var weights = new byte[GetFloorModel(floorIndex).Materials.Count];
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = _floors[floorIndex].GetMaterialStrength(i) == VoxelStrength.Hard ? (byte)_hardWeight : (byte)1;
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

        private void Start()
        {
            if (_floors.Count > 0) InitializeFloors();
        }

        private void Update()
        {
            if (IsConnectionReady) SwapNearFloors();

            for (var i = _fallingBodies.Count - 1; i >= 0; i--)
            {
                var body = _fallingBodies[i];
                if (body != null && !body.IsSleeping()) continue;

                // 敵の格子は眠ったことで止まったと気づく為、Rigidbody は消さずにキネマティックにする
                if (body != null) body.isKinematic = true;
                _fallingBodies.RemoveAt(i);
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
