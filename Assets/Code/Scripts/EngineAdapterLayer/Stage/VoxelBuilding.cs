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
    /// 1階分ずつベイクしたボクセルのモデルを、縦に積み重ねた大きな建物。ステージシーンへ置く。
    /// 階は区間の並びで決め、区間ごとに同じモデルを指定の回数だけ積む。上下で平面の大きさが違う階も積める。
    /// 階ごとに別の VoxelModelLoader にし、削ったときの分離の判定を削った階だけで済ませる。
    /// 階は Awake で作り、Start で手付かずの形のメッシュ（VoxelModelLoader.LoadStatic）で置く。
    /// プレイヤーから差し替える距離の中にかかった階と、攻撃が当たった階（VoxelDestructionAdapter が読み込む）だけをボクセルにする。
    /// 模様が階をまたいで続くよう、階ごとにマテリアルを複製し、_PatternOffset の Y に「階の番号 × 階の高さ」を書く。
    /// MaterialPropertyBlock は SRP Batcher の対象から外れる為、使わない。
    /// 隣り合う階の境目ごとに、両側の段が共有する中身の点でつながりを判定し、階のピースの形が変わるたびにその階の上下の境目を数え直す。
    /// 階とパーツは回転させずに置く前提で、点の対応は建物のローカル空間の水平座標を格子の番号にして取る。
    /// 境目が切れたら、その上の階をまとめて1つの Rigidbody の子にして落とす。当たり判定は各階のチャンクの BoxCollider をそのまま使う。
    /// 落ちた Rigidbody が眠ったらキネマティックにして止め、地形として残す。落ちた階は、以後つながりを判定しない。
    /// 階ごとに、ガラスの板などボクセルにしない付属物のプレハブを、階の子として置く。階と一緒に落ち、倒れ始めたらガラスを割る。
    /// 区間に遠景用のプレハブ（ベイクの元のモデル）があれば、手付かずの間は遠くでそれを描く（VoxelModelLoader.SetStaticProxy）。マテリアルは階ごとの複製に差し替え、模様を続ける。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/LargeObjectVoxel.md">説明ドキュメント: 巨大なオブジェクトへのボクセルの適用</seealso>
    public sealed class VoxelBuilding : MonoBehaviour
    {
        /// <summary> 切り離された塊を隣の階にくっつけるとき、2つの階の向きがそろっているとみなす角度の差（度） </summary>
        private const float ALIGNED_ANGLE = 1f;

        /// <summary> 1階に記録する攻撃と切断の数の上限。超えた階はメッシュに戻さずボクセルのまま残す </summary>
        private const int MAX_EDIT_RECORDS = 128;

        private static readonly int _patternOffsetId = Shader.PropertyToID("_PatternOffset");

        /// <summary> 先読みの円の中心（ワールド座標の x・z） </summary>
        private static Vector2 _preloadCenter;

        /// <summary> 先読みの円の半径（m） </summary>
        private static float _preloadRadius;

        private static bool _hasPreloadArea;

        /// <summary> 先読みで最後に階をボクセルにしたフレーム。全部の建物を合わせて1フレーム1階にする </summary>
        private static int _preloadFrame = -1;

        private readonly List<VoxelModelLoader> _floors = new();

        /// <summary> 階ごとの、本体のピースに当たった攻撃と切断の記録を、起きた順に持つ。上限を超えた階は上限 ＋ 1件で止める </summary>
        private readonly List<List<FloorEdit>> _editRecords = new();

        /// <summary> 階ごとの、記録を当て直したフレーム。そのフレームに切り離された塊は落とさずに扱う </summary>
        private readonly List<int> _replayFrames = new();
        private readonly List<Material> _floorMaterials = new();
        private readonly List<IDisposable> _subscriptions = new();
        private readonly List<Rigidbody> _fallingBodies = new();
        private readonly List<Rigidbody> _restingBodies = new();
        private readonly ActionChannel<Rigidbody> _collapsed = new();
        private readonly ActionChannel<Transform> _collapsing = new();
        private readonly ActionChannel<Bounds> _collapsedBounds = new();

        [SerializeField]
        [Tooltip("下から順に積む区間。区間ごとに、モデルと付属物を指定の回数だけ積む")]
        private FloorSection[] _sections = Array.Empty<FloorSection>();

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
        [Tooltip("倒れる階がメッシュのままのとき、ボクセルにしてから倒すか。しないと倒れる瞬間は軽いが、倒れたあとは削れず、質量にもその階の体積が入らない")]
        private bool _loadOnCollapse = true;

        [SerializeField]
        [Tooltip("差し替えの距離を測るプレイヤー。未設定なら MainCamera から測る")]
        private Transform _target;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーからこの距離（m）の中にかかるメッシュのままの階を、ボクセルにする")]
        private float _swapDistance = 40f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("手付かずの階を、画面に対する高さの割合がこれを下回ったら遠景用のプレハブで描く")]
        private float _proxyTransitionHeight = 0.3f;

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

        /// <summary> 傷を残す範囲（ワールド座標の x・z）。外に出た倒れていない階はメッシュに戻す </summary>
        private Rect _retainArea;

        private bool _hasRetainArea;

        private float _lostVolumeRatio;

        /// <summary> 下から順の階。屋上を積んだ場合は最後の要素 </summary>
        public IReadOnlyList<VoxelModelLoader> Floors => _floors;

        public float FloorHeight => _floorHeight;

        /// <summary> 境目の数。境目 i は、i 階とその下（0なら地面）の境目 </summary>
        public int JointCount => _connectionRatios.Length;

        /// <summary> 全部の階を置き、つながりの元の面積を測ったか </summary>
        public bool IsConnectionReady { get; private set; }

        /// <summary> 全部の階を手付かずの形で置いたときの範囲の中心（ワールド座標）。階を置く前は建物の位置 </summary>
        public Vector3 InitialCenter
        {
            get
            {
                if (_floorBounds.Length == 0) return transform.position;

                var bounds = _floorBounds[0];
                for (var i = 1; i < _floorBounds.Length; i++)
                {
                    bounds.Encapsulate(_floorBounds[i]);
                }

                return bounds.center;
            }
        }

        /// <summary>
        /// ボクセルにした階の、読み込んだ時点の体積のうち削れた割合（0〜1）の最大値。
        /// 階はメッシュに戻されると体積を測れなくなる為、一度削れた割合は戻さずに持つ。測るのはこのプロパティを読んだときで、呼ばれない間に削れてメッシュに戻った分は数えない。
        /// </summary>
        public float LostVolumeRatio
        {
            get
            {
                var initialVolume = 0f;
                var volume = 0f;
                foreach (var floor in _floors)
                {
                    if (!floor.IsLoaded) continue;

                    initialVolume += floor.InitialVolume;
                    volume += floor.Volume;
                }

                if (initialVolume > 0f) _lostVolumeRatio = Mathf.Max(_lostVolumeRatio, 1f - volume / initialVolume);
                return _lostVolumeRatio;
            }
        }

        /// <summary>
        /// 先読みの円（ワールド座標の x・z）を設定する。円にかかる建物の立っている階を、差し替えの空いたフレームに1階ずつボクセルにし、プレイヤーから遠くてもメッシュに戻さない。
        /// 大量の破壊が来ると分かっている場所（ボスの着地点など）で、崩落の瞬間にまとめてボクセルにする負荷を前もって散らす。
        /// </summary>
        /// <param name="center">円の中心（ワールド座標）。高さは見ない</param>
        /// <param name="radius">円の半径（m）</param>
        public static void SetPreloadArea(Vector3 center, float radius)
        {
            _preloadCenter = new Vector2(center.x, center.z);
            _preloadRadius = radius;
            _hasPreloadArea = true;
        }

        /// <summary>
        /// 先読みの円を外す。ボクセルにした階は、以後ふつうの差し替えの規則でメッシュに戻る。
        /// </summary>
        public static void ClearPreloadArea()
        {
            _hasPreloadArea = false;
        }

        /// <summary>
        /// 先読みの円にかかる建物の立っている階を、今すぐ全部ボクセルにする。ステージの読み込み中など、止まっても構わないときに呼ぶ。
        /// 階を置き終えていない建物は、差し替えの空いたフレームに1階ずつ読み込む。
        /// </summary>
        public static void LoadPreloadAreaNow()
        {
            if (!_hasPreloadArea) return;

            foreach (var building in FindObjectsByType<VoxelBuilding>(FindObjectsSortMode.None))
            {
                if (!building.IsConnectionReady || !building.IsInPreloadArea()) continue;

                for (var i = 0; i < building._standingFloorCount; i++)
                {
                    if (building._floors[i].IsStatic) building._floors[i].Load();
                }
            }
        }

        /// <summary>
        /// 倒れ始めた階のピースをすべて含む範囲を求める。RegisterOnCollapsed の通知で受け取った Rigidbody に使う。
        /// </summary>
        /// <param name="body">倒れた階のまとまりの Rigidbody</param>
        /// <param name="bounds">ピースをすべて含む範囲（ワールド空間）</param>
        /// <returns>ピースが 1 つもなければ false</returns>
        public static bool TryGetPiecesBounds(Rigidbody body, out Bounds bounds)
        {
            var pieces = body.GetComponentsInChildren<VoxelPiece>();
            bounds = default;
            if (pieces.Length == 0) return false;

            bounds = pieces[0].WorldBounds;
            for (var i = 1; i < pieces.Length; i++)
            {
                bounds.Encapsulate(pieces[i].WorldBounds);
            }

            return true;
        }

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
        /// 境目が切れて上の階が倒れ始めたときに、倒れた階のピースをすべて含む範囲（ワールド空間）を受け取る関数を登録する。
        /// </summary>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnCollapsedBounds(Action<Bounds> callback)
        {
            return _collapsedBounds.Register(callback);
        }

        /// <summary>
        /// 階のまとまりを作って階を子に移したあと、Rigidbody を付ける前の通知を受け取る関数を登録する。
        /// Rigidbody を付けるときの負荷は子のコライダーの数で決まる為、減らしたいコライダーをここで外す。
        /// </summary>
        /// <param name="callback">階のまとまりの Transform を受け取る関数</param>
        /// <returns>Dispose すると登録を解除する</returns>
        public IDisposable RegisterOnCollapsing(Action<Transform> callback)
        {
            return _collapsing.Register(callback);
        }

        /// <summary>
        /// つながりの面積に関わらず、firstFloor 階から上の立っている階を倒す。倒れ始めた通知（RegisterOnCollapsed）も出る。
        /// 境目の元の面積が測れる（IsConnectionReady）前や、すでに倒れた階には何もしない。
        /// </summary>
        /// <param name="firstFloor">倒す一番下の階の番号。0 なら建物全体</param>
        public void ForceCollapse(int firstFloor)
        {
            if (!IsConnectionReady || firstFloor < 0 || firstFloor >= _standingFloorCount) return;

            _connectionRatios[firstFloor] = 0f;
            Collapse(firstFloor);
        }

        /// <summary>
        /// 傷を残す範囲（敵の追跡範囲。ワールド座標の x・z）を設定する。範囲の外に出た倒れていない階はメッシュに戻し、記録のある階が範囲に入ったら当て直す。
        /// </summary>
        public void SetRetainArea(Rect area)
        {
            _retainArea = area;
            _hasRetainArea = true;
        }

        /// <summary>
        /// 境目 jointIndex の、今のつながっている面積 ÷ 元の面積。元の面積が0なら0。
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
        /// floorIndex 階が属する区間。
        /// </summary>
        private FloorSection GetSection(int floorIndex)
        {
            foreach (var section in _sections)
            {
                if (floorIndex < section.Count) return section;

                floorIndex -= section.Count;
            }

            throw new ArgumentOutOfRangeException(nameof(floorIndex));
        }

        /// <summary>
        /// 階を作る。読み込みは Start で、手付かずの形のメッシュから始める。付属物があれば階の子に置く。
        /// </summary>
        private void CreateFloor(int floorIndex, FloorSection section)
        {
            var model = section.Model;
            var floorObject = new GameObject($"Floor_{floorIndex:00}")
            {
                layer = gameObject.layer
            };
            floorObject.transform.SetParent(transform, false);
            floorObject.transform.localPosition = new Vector3(0f, floorIndex * _floorHeight, 0f);

            var loader = floorObject.AddComponent<VoxelModelLoader>();
            // VoxelModelLoader は Awake で子の MeshRenderer を元のモデルとして控えて隠すので、付属物はその後に置く
            if (section.Attachments != null) Instantiate(section.Attachments, floorObject.transform, false);
            loader.SetModel(model);
            loader.SetQuality(_quality);
            loader.SetMeltSystem(_meltSystem);
            loader.SetLoadOnStart(false);
            var materials = CreateFloorMaterials(model, floorIndex);
            loader.SetMaterialEntries(materials);
            if (section.Proxy != null) loader.SetStaticProxy(CreateProxy(section.Proxy, floorObject.transform, model, materials), _proxyTransitionHeight);
            _subscriptions.Add(loader.RegisterOnPieceShapeChanged(_ => OnFloorShapeChanged(floorIndex)));
            _subscriptions.Add(loader.RegisterOnPieceSplit(pieces => AttachSeparatedPieces(floorIndex, pieces)));
            _subscriptions.Add(loader.RegisterOnPieceStruck(struck => Record(floorIndex, struck.Piece, FloorEdit.Strike(struck.Record))));
            _subscriptions.Add(loader.RegisterOnPieceSliced(sliced => Record(floorIndex, sliced.Piece, FloorEdit.Slice(sliced.Record))));
            _subscriptions.Add(loader.RegisterOnLoaded(_ => ReplayEdits(floorIndex)));
            _editRecords.Add(new List<FloorEdit>());
            _replayFrames.Add(-1);

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
        /// メッシュのままの階のうち、プレイヤーから差し替える距離の中にかかる階と、傷の記録があり傷を残す範囲にかかる階を、
        /// 近い順に1フレーム1階ずつボクセルにする。
        /// 1階の差し替えに数 ms かかる為、近づいて何階も範囲に入っても1フレームに集めない。
        /// どちらにも当たらなければ、先読みの範囲にかかる建物の階を、全部の建物を合わせて1フレーム1階まで、下から順にボクセルにする。
        /// </summary>
        private void SwapNearFloors(Vector3 position)
        {
            var nearest = -1;
            var nearestSqrDistance = float.PositiveInfinity;
            var swapSqrDistance = _swapDistance * _swapDistance;
            for (var i = 0; i < _standingFloorCount; i++)
            {
                if (!_floors[i].IsStatic) continue;

                var sqrDistance = _floorBounds[i].SqrDistance(position);
                var isNeeded = sqrDistance <= swapSqrDistance || (_editRecords[i].Count > 0 && IsInRetainArea(i));
                if (!isNeeded || sqrDistance >= nearestSqrDistance) continue;

                nearest = i;
                nearestSqrDistance = sqrDistance;
            }

            if (nearest >= 0)
            {
                _floors[nearest].Load();
                return;
            }

            if (_preloadFrame == Time.frameCount || !IsInPreloadArea()) return;

            for (var i = 0; i < _standingFloorCount; i++)
            {
                if (!_floors[i].IsStatic) continue;

                _preloadFrame = Time.frameCount;
                _floors[i].Load();
                return;
            }
        }

        /// <summary>
        /// ボクセルにした倒れていない階のうち、傷を残す範囲の外にあり、プレイヤーから差し替える距離より遠い階を、1フレーム1階ずつメッシュに戻す。
        /// 記録が上限を超えた階は、当て直せないので戻さない。範囲を渡されていなければ戻さない。先読みの範囲にかかる建物の階は戻さない。
        /// </summary>
        private void RestoreFarFloors(Vector3 position)
        {
            if (!_hasRetainArea || IsInPreloadArea()) return;

            var swapSqrDistance = _swapDistance * _swapDistance;
            for (var i = 0; i < _standingFloorCount; i++)
            {
                if (!_floors[i].IsLoaded || _editRecords[i].Count > MAX_EDIT_RECORDS) continue;
                if (IsInRetainArea(i) || _floorBounds[i].SqrDistance(position) <= swapSqrDistance) continue;

                _floors[i].Unload();
                return;
            }
        }

        /// <summary>
        /// 立っている階のどれかの水平の範囲が、先読みの円にかかるか。崩れると上の階がまとめてボクセルになる為、建物単位で決める。
        /// </summary>
        private bool IsInPreloadArea()
        {
            if (!_hasPreloadArea) return false;

            var radiusSqr = _preloadRadius * _preloadRadius;
            for (var i = 0; i < _standingFloorCount; i++)
            {
                var bounds = _floorBounds[i];
                var nearest = new Vector2(
                    Mathf.Clamp(_preloadCenter.x, bounds.min.x, bounds.max.x),
                    Mathf.Clamp(_preloadCenter.y, bounds.min.z, bounds.max.z));
                if ((nearest - _preloadCenter).sqrMagnitude <= radiusSqr) return true;
            }

            return false;
        }

        private bool IsInRetainArea(int floorIndex)
        {
            var bounds = _floorBounds[floorIndex];
            return _hasRetainArea && _retainArea.Overlaps(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
        }

        /// <summary>
        /// floorIndex 階を読み込んだら、記録した攻撃と切断を起きた順に当て直す。当て直す間は記録しない。
        /// 当て直しで切り離された塊は、元のプレイ中に落ちたかけらにあたるので、上下の階にくっつかなければ消す（AttachSeparatedPieces）。
        /// </summary>
        private void ReplayEdits(int floorIndex)
        {
            var records = _editRecords[floorIndex];
            if (records.Count == 0) return;

            var parts = _floors[floorIndex].Parts;
            if (parts.Count == 0 || parts[0] == null) return;

            // 当て直す攻撃と切断も ApplyStrike・Slice を通る為、記録を一度外して二重に記録しないようにする
            _editRecords[floorIndex] = new List<FloorEdit>();
            var part = parts[0];
            foreach (var record in records)
            {
                if (record.IsSlice)
                {
                    var plane = record.SliceRecord;
                    part.Slice(part.transform.TransformPoint(plane.PlanePoint),
                        part.transform.TransformDirection(plane.PlaneNormal));
                }
                else
                {
                    part.ApplyStrike(record.StrikeRecord.Reach, record.StrikeRecord.Falloff, Space.Self);
                }
            }

            _editRecords[floorIndex] = records;
            _replayFrames[floorIndex] = Time.frameCount;
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
            if (!IsConnectionReady) return;

            if (floorIndex >= _standingFloorCount)
            {
                UpdateFallenConnections(floorIndex);
                return;
            }

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
        /// 倒れたまとまりの中の floorIndex 階の上下の境目を数え直す。同じまとまりの階どうしの境目が切れたら、上の階をまとまりから分ける。
        /// </summary>
        private void UpdateFallenConnections(int floorIndex)
        {
            for (var joint = floorIndex; joint <= floorIndex + 1 && joint < _floors.Count; joint++)
            {
                if (joint <= _standingFloorCount) continue;

                var group = _floors[joint].transform.parent;
                if (_floors[joint - 1].transform.parent != group) continue;

                var wasConnected = IsConnected(joint);
                _connectionRatios[joint] = _initialAreas[joint] > 0f ? MeasureJointArea(joint) / _initialAreas[joint] : 0f;

                if (wasConnected && !IsConnected(joint))
                {
                    UsefulLogger.Log($"倒れた {group.name} の {joint} 階の下の境目が切れました（元の面積の {_connectionRatios[joint]:P1}）。", this);
                    SplitFallenGroup(group.GetComponent<Rigidbody>(), joint);
                    return;
                }
            }
        }

        /// <summary>
        /// firstFloor 階から上の立っている階を、1つの Rigidbody の子にまとめて落とす。
        /// </summary>
        private void Collapse(int firstFloor)
        {
            var groupObject = CreateGroupObject(firstFloor);
            groupObject.transform.SetParent(transform, false);

            for (var i = firstFloor; i < _standingFloorCount; i++)
            {
                // メッシュのままの階もボクセルにし、敵の格子と崩落の判定がピースとして見張れるようにする
                if (_loadOnCollapse && _floors[i].IsStatic) _floors[i].Load();

                _floors[i].transform.SetParent(groupObject.transform, true);
            }

            _standingFloorCount = firstFloor;

            _collapsing.Invoke(groupObject.transform);
            var body = AddGroupBody(groupObject);
            _fallingBodies.Add(body);
            ShatterGlass(groupObject.transform, body.worldCenterOfMass);
            SoundPlayer.Play(SoundEffectKind.VoxelCollapse, body.worldCenterOfMass);
            NotifyCollapsed(body);
        }

        /// <summary>
        /// 倒れ始めた階のまとまりを、Rigidbody と範囲の購読者へ通知する。
        /// </summary>
        private void NotifyCollapsed(Rigidbody body)
        {
            _collapsed.Invoke(body);
            if (TryGetPiecesBounds(body, out var bounds)) _collapsedBounds.Invoke(bounds);
        }

        /// <summary>
        /// group の子にあるガラスの板をすべて、origin から外へ破片を飛ばして割る。
        /// </summary>
        private static void ShatterGlass(Transform group, Vector3 origin)
        {
            foreach (var glass in group.GetComponentsInChildren<GlassPane>())
            {
                glass.Shatter(origin);
            }
        }

        /// <summary>
        /// 倒れたまとまり source のうち、firstFloor 階から上の階を新しいまとまりに移して分ける。
        /// 新しいまとまりは、分ける前の自分の位置での source の速度と角速度を引き継ぐ。
        /// </summary>
        private void SplitFallenGroup(Rigidbody source, int firstFloor)
        {
            if (source == null) return;

            var groupObject = CreateGroupObject(firstFloor);
            groupObject.transform.SetParent(transform, false);
            groupObject.transform.SetPositionAndRotation(source.position, source.rotation);

            for (var i = firstFloor; i < _floors.Count; i++)
            {
                if (_floors[i].transform.parent != source.transform) break;

                _floors[i].transform.SetParent(groupObject.transform, true);
            }

            source.mass = MeasureGroupMass(source.transform);
            source.isKinematic = false;
            source.WakeUp();
            // 上の階が抜けて形が変わった為、重心を形から求め直してからずらす
            source.ResetCenterOfMass();
            Attract(source);

            var body = AddGroupBody(groupObject);
            body.linearVelocity = source.GetPointVelocity(body.worldCenterOfMass);
            body.angularVelocity = source.angularVelocity;
            _fallingBodies.Add(body);
            SoundPlayer.Play(SoundEffectKind.VoxelCollapse, body.worldCenterOfMass);
            NotifyCollapsed(body);
        }

        private GameObject CreateGroupObject(int firstFloor)
        {
            return new GameObject($"Collapsed_{firstFloor:00}")
            {
                layer = gameObject.layer
            };
        }

        private Rigidbody AddGroupBody(GameObject groupObject)
        {
            var body = groupObject.AddComponent<Rigidbody>();
            body.mass = MeasureGroupMass(groupObject.transform);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Attract(body);
            return body;
        }

        /// <summary>
        /// まとまりの重心を、ピースをすべて含む範囲を基準に、高くして引き寄せ対象の側へずらす。
        /// </summary>
        private static void Attract(Rigidbody body)
        {
            if (TryGetPiecesBounds(body, out var bounds)) VoxelAttraction.Apply(body, bounds);
        }

        /// <summary>
        /// まとまりの子の階のピースの体積の合計 × 密度。
        /// </summary>
        private float MeasureGroupMass(Transform group)
        {
            var volume = 0f;
            foreach (var floor in _floors)
            {
                if (floor.transform.parent != group) continue;

                foreach (var part in floor.Parts)
                {
                    if (part != null) volume += part.Volume;
                }
            }

            return math.max(volume * _collapseDensity, 0.01f);
        }

        /// <summary>
        /// floorIndex 階のピース（くっつけた塊も含む）の形が変わったら、止まっている倒れた階のまとまりを起こし、つながりを数え直す。
        /// 止まったまとまりはキネマティックで、支えを削られても自分からは落ちない為、建物のどこかの形が変わるたびに落とし直す。
        /// 支えが残っていれば、すぐ眠ってまた止まる。
        /// </summary>
        private void OnFloorShapeChanged(int floorIndex)
        {
            foreach (var body in _restingBodies)
            {
                if (body == null) continue;

                body.isKinematic = false;
                body.WakeUp();
                _fallingBodies.Add(body);
            }

            _restingBodies.Clear();
            UpdateConnections(floorIndex);
        }

        /// <summary>
        /// floorIndex 階の本体のピースに当たった攻撃か切断を記録する。本体はその階の GameObject にあるので、記録は階のローカル空間になる。
        /// 切り離された塊に当たった分は記録しない。
        /// </summary>
        private void Record(int floorIndex, VoxelPiece piece, in FloorEdit record)
        {
            var records = _editRecords[floorIndex];
            if (piece.Generation > 0 || records.Count > MAX_EDIT_RECORDS) return;

            records.Add(record);
        }

        /// <summary>
        /// floorIndex 階から切り離された塊のうち、上下の階と境目の面で中身の点を共有する塊を、その階の子にして一緒に動かす。
        /// 1階の塊は、下の面に中身の点があれば地面にくっついているとみなし、建物の子にしてその場に残す。
        /// 共有する点が多い方につける。くっつけた塊は飾りとして動くだけで、つながりの判定には数えない。
        /// 当たり判定は切り離したときの凸包のまま使う。同じ剛体の子どうしは当たらない為。
        /// </summary>
        private void AttachSeparatedPieces(int floorIndex, VoxelPiece[] pieces)
        {
            if (!IsConnectionReady) return;

            var halfVoxel = _quality.VoxelSize * 0.5f;
            for (var i = 1; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;

                var upperArea = CountSharedFace(piece, floorIndex, floorIndex + 1, _floorHeight - halfVoxel, halfVoxel);
                var lowerArea = floorIndex == 0
                    ? CountPieceFace(piece, floorIndex, halfVoxel)
                    : CountSharedFace(piece, floorIndex, floorIndex - 1, halfVoxel, _floorHeight - halfVoxel);
                if (upperArea == 0f && lowerArea == 0f)
                {
                    if (_replayFrames[floorIndex] == Time.frameCount) Destroy(piece.gameObject);
                    continue;
                }

                Transform target;
                if (upperArea >= lowerArea) target = _floors[floorIndex + 1].transform;
                else target = floorIndex == 0 ? transform : _floors[floorIndex - 1].transform;

                if (piece.TryGetComponent<Rigidbody>(out var body))
                {
                    body.isKinematic = true;
                    Destroy(body);
                }

                piece.transform.SetParent(target, true);
            }
        }

        /// <summary>
        /// ピースの floorLocalY の段と、otherFloor 階の otherLocalY の段の両方が中身の点を、重みの大きい方で数える。
        /// 2つの階が積んだときの位置と向きのままそろっていないときは0。
        /// 切り離しは形の変化の通知より後に届く為、境目が切れて倒れ始めたばかりの階とも比べられるよう、親ではなく位置で判定する。
        /// </summary>
        private float CountSharedFace(VoxelPiece piece, int floorIndex, int otherFloor, float floorLocalY, float otherLocalY)
        {
            if (otherFloor < 0 || otherFloor >= _floors.Count) return 0f;

            var floor = _floors[floorIndex].transform;
            var other = _floors[otherFloor].transform;
            var expected = floor.TransformPoint(0f, (otherFloor - floorIndex) * _floorHeight, 0f);
            if (Vector3.Distance(other.position, expected) > _quality.VoxelSize * 0.5f ||
                Quaternion.Angle(other.rotation, floor.rotation) > ALIGNED_ANGLE)
            {
                return 0f;
            }

            Array.Clear(_upperFace, 0, _upperFace.Length);
            ReadPieceLayer(piece, floorIndex, floor.TransformPoint(0f, floorLocalY, 0f), floor, _upperFace);

            ReadFace(otherFloor, otherLocalY, _lowerFace);
            var area = 0f;
            for (var i = 0; i < _upperFace.Length; i++)
            {
                if (_upperFace[i] != 0 && _lowerFace[i] != 0) area += Math.Max(_upperFace[i], _lowerFace[i]);
            }

            return area;
        }

        /// <summary>
        /// ピースの floorLocalY の段の中身の点を、重みつきで数える。
        /// </summary>
        private float CountPieceFace(VoxelPiece piece, int floorIndex, float floorLocalY)
        {
            var floor = _floors[floorIndex].transform;
            Array.Clear(_upperFace, 0, _upperFace.Length);
            ReadPieceLayer(piece, floorIndex, floor.TransformPoint(0f, floorLocalY, 0f), floor, _upperFace);

            var area = 0f;
            foreach (var weight in _upperFace)
            {
                area += weight;
            }

            return area;
        }

        /// <summary>
        /// 境目 jointIndex で、上の階の下の面と下の階の上の面の両方が中身の点を、重みの大きい方で数える。境目0では地面側を常に中身とみなす。
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
                ReadPieceLayer(part, floorIndex, floorPoint, floor.transform, face);
            }

            foreach (var staticPart in floor.StaticParts)
            {
                var layout = staticPart.Shape.Layout;
                if (!TryGetSampleY(staticPart.Target, layout, floorPoint, out var sampleY)) continue;

                PrepareLayerBuffer(layout);
                staticPart.Shape.ReadLayerWeights(sampleY, weights, 1, _layerBuffer);
                AddLayer(staticPart.Target, layout, sampleY, floor.transform, face);
            }
        }

        /// <summary>
        /// floorIndex 階の素材の重みで、ピースの worldPoint の高さの段を、frame の水平方向の格子の番号ごとに face へ重ねる。
        /// </summary>
        private void ReadPieceLayer(VoxelPiece piece, int floorIndex, Vector3 worldPoint, Transform frame, byte[] face)
        {
            var volume = piece != null ? piece.VolumeData : null;
            if (volume == null) return;

            if (!TryGetSampleY(piece.transform, volume.Layout, worldPoint, out var sampleY)) return;

            PrepareLayerBuffer(volume.Layout);
            volume.ReadLayerWeights(sampleY, _materialWeights[floorIndex], 1, _layerBuffer);
            AddLayer(piece.transform, volume.Layout, sampleY, frame, face);
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
        /// _layerBuffer に読んだ1段分の重みを、面の配列へ重みの大きい方で重ねる。
        /// </summary>
        private void AddLayer(Transform partTransform, VoxelGridLayout layout, int sampleY, Transform frame, byte[] face)
        {
            var count = layout.SampleCount;

            // パーツは階に対して回転させずに置く前提なので、格子の番号はサンプル番号と同じだけ進む
            var key = ToFaceKey(partTransform, layout, sampleY, frame) - _faceMin;
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
                var key = ToFaceKey(staticPart.Target, layout, 0, floor.transform);
                min = math.min(min, key);
                max = math.max(max, key + layout.SampleCount.xz - 1);
            }

            _faceMin = min;
            _faceSize = math.max(max - min + 1, 0);
            _lowerFace = new byte[_faceSize.x * _faceSize.y];
            _upperFace = new byte[_faceSize.x * _faceSize.y];
        }

        /// <summary>
        /// パーツのサンプル (0, sampleY, 0) の、階 frame の水平方向の格子の番号。格子の点はボクセルの倍数 ＋ 半ボクセルに並ぶ。
        /// 階は建物から高さだけずらして積む為、どの階で測っても建物の格子の番号と同じになり、同じまとまりで倒れた階どうしでも比べられる。
        /// </summary>
        private int2 ToFaceKey(Transform partTransform, VoxelGridLayout layout, int sampleY, Transform frame)
        {
            var voxelSize = _quality.VoxelSize;
            var origin = frame.InverseTransformPoint(
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
            var weights = new byte[GetSection(floorIndex).Model.Materials.Count];
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = _floors[floorIndex].GetMaterialStrength(i) == VoxelStrength.Hard ? (byte)_hardWeight : (byte)1;
            }

            return weights;
        }

        /// <summary>
        /// モデルの素材のマテリアルを複製し、模様を階の高さだけずらす。同じマテリアルは階の中で1つの複製を使い回す。
        /// </summary>
        /// <summary>
        /// 遠景用のプレハブを階の子に置き、元のマテリアルを階ごとの複製に差し替える。複製の対応は、モデルの素材の一覧の元のマテリアルで取る。
        /// </summary>
        private static GameObject CreateProxy(GameObject prefab, Transform floor, VoxelModelAsset model, VoxelMaterialEntry[] floorMaterials)
        {
            var copies = new Dictionary<Material, Material>();
            for (var i = 0; i < floorMaterials.Length; i++)
            {
                var source = model.Materials[i].Material;
                if (source != null) copies[source] = floorMaterials[i].Material;
            }

            var proxy = Instantiate(prefab, floor, false);
            foreach (var proxyRenderer in proxy.GetComponentsInChildren<Renderer>())
            {
                var shared = proxyRenderer.sharedMaterials;
                for (var i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null && copies.TryGetValue(shared[i], out var copy)) shared[i] = copy;
                }

                proxyRenderer.sharedMaterials = shared;
            }

            return proxy;
        }

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
            if (_sections.Length == 0 || Array.Exists(_sections, section => section.Model == null) || _quality == null)
            {
                UsefulLogger.LogError("区間、区間のモデル、または VoxelQualitySettings が設定されていません。", this);
                return;
            }

            var floorIndex = 0;
            foreach (var section in _sections)
            {
                for (var i = 0; i < section.Count; i++)
                {
                    CreateFloor(floorIndex++, section);
                }
            }
        }

        private void Start()
        {
            if (_floors.Count > 0) InitializeFloors();

            if (Array.Exists(_sections, section => section.Attachments != null) && GlassShardPool.Instance == null)
            {
                UsefulLogger.LogWarning("GlassShardPool がステージにない為、ガラスの板は割れても破片が出ません。", this);
            }
        }

        private void Update()
        {
            // プレイヤーは常駐シーンにいて参照を持てない為、未設定なら MainCamera から測る
            var target = _target != null ? _target : Camera.main != null ? Camera.main.transform : null;
            if (IsConnectionReady && target != null)
            {
                SwapNearFloors(target.position);
                RestoreFarFloors(target.position);
            }

            for (var i = _fallingBodies.Count - 1; i >= 0; i--)
            {
                var body = _fallingBodies[i];
                if (body != null && !body.IsSleeping()) continue;

                if (body != null)
                {
                    // 敵の格子は眠ったことで止まったと気づく為、Rigidbody は消さずにキネマティックにする
                    body.isKinematic = true;
                    _restingBodies.Add(body);
                }

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

        /// <summary>
        /// 同じモデルの階を続けて積む区間。
        /// </summary>
        [Serializable]
        private sealed class FloorSection
        {
            [SerializeField]
            [Tooltip("1 階分のモデル。一番上の区間以外は、高さを階の高さにそろえる")]
            private VoxelModelAsset _model;

            [SerializeField]
            [Tooltip("階ごとに置く付属物（ガラスの板など）。未設定なら置かない")]
            private GameObject _attachments;

            [SerializeField]
            [Tooltip("手付かずの間に遠くで描くプレハブ（ベイクの元のモデル）。未設定なら手付かずの形のメッシュだけで描く")]
            private GameObject _proxy;

            [SerializeField, Min(1)]
            [Tooltip("積む回数")]
            private int _count = 1;

            public VoxelModelAsset Model => _model;
            public GameObject Attachments => _attachments;
            public GameObject Proxy => _proxy;
            public int Count => _count;
        }

        /// <summary>
        /// 階の本体のピースに起きた攻撃か切断の記録1件。
        /// </summary>
        private readonly struct FloorEdit
        {
            public readonly bool IsSlice;
            public readonly VoxelStrikeRecord StrikeRecord;
            public readonly VoxelSliceRecord SliceRecord;

            private FloorEdit(bool isSlice, VoxelStrikeRecord strikeRecord, VoxelSliceRecord sliceRecord)
            {
                IsSlice = isSlice;
                StrikeRecord = strikeRecord;
                SliceRecord = sliceRecord;
            }

            public static FloorEdit Strike(in VoxelStrikeRecord record)
            {
                return new FloorEdit(false, record, default);
            }

            public static FloorEdit Slice(in VoxelSliceRecord record)
            {
                return new FloorEdit(true, default, record);
            }
        }
    }
}
