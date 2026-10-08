using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の状態（EnemyAgent）の配列と体のプールを持ち、ステージシーンの EnemySpawnSystem の設定に従って敵を出す Adapter。インゲームのシーンへ置く。
    /// 敵の状態の数は EnemySpawnSystem の同時に存在する数の上限で、出ている敵は EnemyCrowdRenderer でまとめて描画する。
    /// 経路の格子は初期化のときに EnemySpawnSystem の範囲で作り、ステージのボクセルのモデルの形が変わったら、その範囲を調べ直す。
    /// 距離マップは、プレイヤーのいるノードが変わるか、格子を調べ直すたびに計算し直す。
    /// プレイヤーへたどり着けない状態が続いた敵は、カメラに映っていなければ生成位置へ戻す。
    /// 足場ごと一定の高さ以上落ちた敵と、ボクセルから切り離されて落ちてくる塊に潰された敵は、崩落で倒す。体を貸していれば返し、かけらは出さない。
    /// 生成した敵は出した順にグループ（EnemyGroups）へ入れ、毎フレーム EnemyGroupJob でグループのアンカーを、EnemyMoveJob で敵を隊列の位置へ動かす。
    /// 切断できる体（EnemyBody）の貸し借りと近接切断の結果の受け渡しは EnemyBodyLender が、体から外れた切っていない部位の見た目用の物は EnemyDebrisSpawner が行う。
    /// 敵の状態、体、見た目用の物は初期化のときに作り、実行中は作らない。敵の状態に空きがなければ出さない。
    /// </summary>
    /// <remarks>
    /// 敵を出すのは MeshDataCache のストアができてから。体を貸すときに部位を登録し直すのにストアが要る為。
    /// </remarks>
    public sealed class EnemySpawnAdapter : InitializableMonoBehaviour
    {
        /// <summary> かかった時間を平均するフレームの数 </summary>
        private const int TIMING_SAMPLE_COUNT = 30;

        /// <summary> 戻れない敵がカメラに映っているかを調べる箱の、体の根からの中心の高さ（m） </summary>
        private const float VISIBILITY_HEIGHT = 2f;

        /// <summary> 戻れない敵がカメラに映っているかを調べる箱の一辺（m）。体の前後の長さ（約 10.5m）を囲む </summary>
        private const float VISIBILITY_SIZE = 11f;

        private const string UPDATE_MARKER_NAME = "Kizami.Enemy.Update";
        private const string MOVE_MARKER_NAME = "Kizami.Enemy.Move";
        private const string BODY_MARKER_NAME = "Kizami.Enemy.Body";
        private const string RENDER_MARKER_NAME = "Kizami.Enemy.Render";

        private static readonly ProfilerMarker _updateMarker = new(UPDATE_MARKER_NAME);
        private static readonly ProfilerMarker _moveMarker = new(MOVE_MARKER_NAME);
        private static readonly ProfilerMarker _bodyMarker = new(BODY_MARKER_NAME);
        private static readonly ProfilerMarker _renderMarker = new(RENDER_MARKER_NAME);

        private readonly List<EnemySpawnPoint> _spawnPoints = new();
        private readonly List<EnemyInitialSpawnArea> _initialSpawnAreas = new();

        /// <summary> カメラの視錐台の面。戻れない敵がカメラに映っているかを調べる作業用の配列 </summary>
        private readonly Plane[] _frustumPlanes = new Plane[6];

        [SerializeField]
        [Tooltip("敵の体のプレハブ。まとめて描画する部位のメッシュ・マテリアル・位置もここから読む")]
        private EnemyBody _bodyPrefab;

        [SerializeField, Min(0)]
        [Tooltip("体の数。近くの敵に貸す切断できる体で、初期化のときにこの数だけ作る")]
        private int _bodyCount = 32;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーとの距離がこの値（m）以下の敵に、体を貸す")]
        private float _lendDistance = 12f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーとの距離がこの値（m）より離れた敵から、体を返す。貸す距離より遠くする")]
        private float _returnDistance = 18f;

        [SerializeField, Min(0f)]
        [Tooltip("体の空きがないとき、プレイヤーとの距離がこの値（m）以下の体を持たない敵には、より遠い敵から体を取り上げて貸す。切断の届く距離と、敵の体の根から部位の端までの長さを足した値より大きくする")]
        private float _reclaimDistance = 9f;

        [SerializeField, Min(0f)]
        [Tooltip("体を取り上げる相手は、貸す敵よりこの値（m）以上遠い敵に限る。近い 2 体の間で体が行き来しないようにする")]
        private float _reclaimMargin = 3f;

        [SerializeField, Min(0)]
        [Tooltip("体を返した敵の、短くなった部位の形を預かれる数。初期化のときにこの数だけ保管用の物を作る")]
        private int _shapeKeeperCapacity = 64;

        [SerializeField]
        [Tooltip("かけらのプール。体に残す側のかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("敵が向かう先（プレイヤー）。距離マップはここからの距離を持つ")]
        private Transform _target;

        [SerializeField, Min(0.1f)]
        [Tooltip("経路の格子のマスの一辺（m）")]
        private float _cellSize = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("敵の背丈（m）。床の上にこの高さだけ物がなければ、立てる層にする")]
        private float _enemyHeight = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("敵が登れる段差の高さ（m）")]
        private float _climbHeight = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("敵が歩いて降りられる段差の高さ（m）。これより高い所からは降りない。足場が壊れて落ちるのは、この高さによらない")]
        private float _dropHeight = 2f;

        [SerializeField]
        [Tooltip("経路の格子を作るときに、床と障害物として扱うレイヤー")]
        private LayerMask _groundLayers = 1;

        [SerializeField]
        [Tooltip("実行中に、プレイヤーの周りの距離マップをギズモで描く")]
        private bool _drawDistanceField;

        [SerializeField, Min(0f)]
        [Tooltip("距離マップをギズモで描く、プレイヤーからの半径（m）")]
        private float _distanceGizmoRadius = 25f;

        [SerializeField, Min(0f)]
        [Tooltip("敵が歩く速さ（m/s）")]
        private float _moveSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("敵が向きを変える速さ（度/秒）")]
        private float _turnSpeed = 180f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーまでの経路の長さ（距離マップの値）がこの値（m）以下になったら止まる")]
        private float _stopDistance = 6f;

        [SerializeField]
        [Tooltip("グループの隊列と、アンカー（グループの先頭）の動き")]
        private EnemyFormationSettings _formation = EnemyFormationSettings.Default;

        [SerializeField, Min(0f)]
        [Tooltip("初期生成で、グループのメンバーを置く範囲の半径（m）。グループの中心は初期生成の範囲から選ぶ")]
        private float _groupSpawnRadius = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーへたどり着けない状態がこの時間（秒）続いた敵は、カメラに映っていなければ生成位置へ戻す。動けない敵と、体を貸している敵は戻さない")]
        private float _strandedReturnDelay = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("この高さ（m）以上落ちて着地した敵を、崩落で倒す。歩いて降りられる高さより高くする")]
        private float _fallDefeatHeight = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ボクセルから切り離されて落ちてくる塊のうち、下向きの速さがこの値（m/s）以上のものに入った敵を潰す")]
        private float _crushMinFallSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ボクセルから切り離されて落ちてくる塊のうち、体積がこの値（m³）以上のものに入った敵を潰す")]
        private float _crushMinVolume = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("潰されたかを調べる体の中心の、体の根からの高さ（m）")]
        private float _crushBodyCenterHeight = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("体の中心が塊の表面からこの距離（m）以内なら、潰されたとする")]
        private float _crushSurfaceMargin = 0.3f;

        [SerializeField]
        [Tooltip("体から外れた切っていない部位を消すディゾルブのマテリアル。シェーダーは float のプロパティ _DissolveAmount（0〜1）で消える")]
        private Material _debrisMaterial;

        [SerializeField, Min(1)]
        [Tooltip("同時に出せる見た目用の部位の数")]
        private int _debrisCapacity = 32;

        [SerializeField, Min(0f)]
        [Tooltip("見た目用の部位が出てから消え終わるまでの時間（秒）")]
        private float _debrisLifetime = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("見た目用の部位が、体の中心から外へ飛ぶ速さ（m/s）")]
        private float _debrisOutwardSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("見た目用の部位が、上へ飛ぶ速さ（m/s）")]
        private float _debrisUpwardSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("見た目用の部位が回る速さ（度/秒）")]
        private float _debrisAngularSpeed = 360f;

        private EnemySpawnSystem _spawnSystem;

        private NativeArray<EnemyAgent> _agents;

        private EnemyCrowdRenderer _crowdRenderer;

        private EnemyDistanceField _distanceField;

        private EnemyGroups _groups;

        private EnemyCollapseDetector _collapseDetector;

        private EnemyBodyLender _bodyLender;

        /// <summary> 体から外れた部位の見た目用の物。ディゾルブのマテリアルが未設定なら null </summary>
        private EnemyDebrisSpawner _debrisSpawner;

        /// <summary> 崩落で倒した敵のエネルギーを出す関数。引数は倒した敵の体の中心の位置 </summary>
        private Action<Vector3> _emitEnergy;

        private ProfilerRecorder _updateRecorder;
        private ProfilerRecorder _moveRecorder;
        private ProfilerRecorder _bodyRecorder;
        private ProfilerRecorder _renderRecorder;

        /// <summary> 生成情報ごとの、前に出してからの経過時間（秒） </summary>
        private float[] _spawnTimers;

        /// <summary> 次に使う生成位置の、_spawnPoints の中の位置 </summary>
        private int _nextSpawnPointIndex;

        private bool _hasSpawnedInitial;

        /// <summary> 出ている敵の数 </summary>
        public int SpawnedCount
        {
            get
            {
                if (!_agents.IsCreated) return 0;

                var count = 0;
                foreach (var agent in _agents)
                {
                    if (agent.IsAlive) count++;
                }

                return count;
            }
        }

        /// <summary> 崩落（足場ごとの落下と、落ちてくる塊）で倒した敵の数の累計 </summary>
        public int CollapseDefeatCount { get; private set; }

        /// <summary> 崩落で倒した敵のうち、落ちてくる塊に潰された敵の数の累計 </summary>
        public int CrushDefeatCount { get; private set; }

        /// <summary> 敵の状態の数（同時に存在する数の上限） </summary>
        public int Capacity => _agents.IsCreated ? _agents.Length : 0;

        /// <summary> 使われているグループの数 </summary>
        public int GroupCount => _groups?.ActiveCount ?? 0;

        /// <summary> 敵に貸している体の数 </summary>
        public int LentBodyCount => _bodyLender?.LentBodyCount ?? 0;

        /// <summary> 体の数 </summary>
        public int BodyCount => _bodyLender?.BodyCount ?? 0;

        /// <summary> 体を返した敵から預かっている、短くなった部位の数 </summary>
        public int KeptShapeCount => _bodyLender?.KeptShapeCount ?? 0;

        /// <summary> Update 全体にかかったメインスレッドの時間（ms）。直近のフレームの平均 </summary>
        public double UpdateMilliseconds => GetAverageMilliseconds(_updateRecorder);

        /// <summary> まとめて描画の準備にかかったメインスレッドの時間（ms）。直近のフレームの平均 </summary>
        public double RenderMilliseconds => GetAverageMilliseconds(_renderRecorder);

        /// <summary> 敵の移動（Job の完了待ちを含む）にかかったメインスレッドの時間（ms）。直近のフレームの平均 </summary>
        public double MoveMilliseconds => GetAverageMilliseconds(_moveRecorder);

        /// <summary> 体の貸し出し・返却と位置の同期にかかったメインスレッドの時間（ms）。直近のフレームの平均 </summary>
        public double BodyMilliseconds => GetAverageMilliseconds(_bodyRecorder);

        /// <summary> 経路の格子と距離マップ。初期化の前は null </summary>
        public EnemyDistanceField DistanceField => _distanceField;

        /// <summary> ステージシーンの実行中の生成位置 </summary>
        public IReadOnlyList<EnemySpawnPoint> SpawnPoints => _spawnPoints;

        /// <summary>
        /// ProfilerRecorder に残っている直近のフレームの時間（ns）を平均し、ms で返す。
        /// </summary>
        private static double GetAverageMilliseconds(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0) return 0d;

            var total = 0L;
            for (var i = 0; i < recorder.Count; i++)
            {
                total += recorder.GetSample(i).Value;
            }

            return total / (double)recorder.Count * 1e-6;
        }

        /// <summary>
        /// ステージシーンの EnemySpawnSystem を探し、上限の数だけ敵の状態と、体と、見た目用の部位を作る。
        /// 見つからないときは Update を止めたままにする。
        /// </summary>
        /// <param name="spawnOrb">倒れた体に残っていた切断済みの部位を、オーブにする関数。引数はオーブを出す位置</param>
        /// <param name="emitEnergy">崩落で倒した敵のエネルギーを出す関数。引数は倒した敵の体の中心の位置</param>
        public void Initialize(Action<Vector3> spawnOrb, Action<Vector3> emitEnergy)
        {
            _emitEnergy = emitEnergy;

            if (_bodyPrefab == null || _fragmentPool == null)
            {
                UsefulLogger.LogError("敵の体のプレハブか、かけらのプールが設定されていません。", this);
                return;
            }

            _spawnSystem = FindAnyObjectByType<EnemySpawnSystem>();
            if (_spawnSystem == null)
            {
                UsefulLogger.LogError("ステージシーンに EnemySpawnSystem が見つからない為、敵を出せません。", this);
                return;
            }

            _spawnSystem.GetComponentsInChildren(true, _spawnPoints);
            _spawnSystem.GetComponentsInChildren(true, _initialSpawnAreas);
            _spawnTimers = new float[_spawnSystem.SpawnInfos.Count];

            if (_debrisMaterial == null)
            {
                UsefulLogger.LogWarning("ディゾルブのマテリアルが設定されていない為、体から外れた部位はその場で消えます。", this);
            }
            else
            {
                _debrisSpawner = new EnemyDebrisSpawner(transform, _debrisMaterial, _debrisCapacity, _debrisLifetime,
                    _debrisOutwardSpeed, _debrisUpwardSpeed, _debrisAngularSpeed);
            }

            _agents = new NativeArray<EnemyAgent>(_spawnSystem.MaxAliveCount, Allocator.Persistent);
            _crowdRenderer = new EnemyCrowdRenderer(_bodyPrefab, _agents.Length);
            _distanceField = new EnemyDistanceField(_spawnSystem.NavigationBounds, _cellSize, _enemyHeight, _climbHeight,
                _dropHeight, _groundLayers);
            _collapseDetector = new EnemyCollapseDetector(_crushMinFallSpeed, _crushMinVolume, _crushBodyCenterHeight,
                _crushSurfaceMargin);
            foreach (var loader in FindObjectsByType<VoxelModelLoader>(FindObjectsSortMode.None))
            {
                _distanceField.Watch(loader);
                _collapseDetector.Watch(loader);
            }

            _groups = new EnemyGroups(_agents.Length);
            _updateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, UPDATE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _moveRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MOVE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _bodyRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, BODY_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, RENDER_MARKER_NAME, TIMING_SAMPLE_COUNT);

            _bodyLender = new EnemyBodyLender(_bodyPrefab, transform, _bodyCount, _agents.Length,
                _shapeKeeperCapacity, _fragmentPool, _lendDistance, _returnDistance, _reclaimDistance, _reclaimMargin,
                spawnOrb, _debrisSpawner != null ? _debrisSpawner.Spawn : null);

            base.Initialize();
        }

        /// <summary>
        /// 切断の結果のうち、敵の部位を元の対象とするものを、その部位を持つ体へ渡し、体の部位の状態を敵の状態へ書き戻す。
        /// 体が倒れたら、敵をステージから消して体を空ける。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        /// <param name="plane">振ったときの切断面。法線は表のかけらの側を向く</param>
        public void ReceiveCutResults(MultiCutResult[] results, Plane plane)
        {
            if (!Initialized || results == null) return;

            _bodyLender.ReceiveCutResults(results, plane, _agents);
        }

        private void Update()
        {
            var cache = MeshDataCache.Instance;
            if (cache == null || cache.Store == null) return;

            using (_updateMarker.Auto())
            {
                if (!_hasSpawnedInitial)
                {
                    SpawnInitial();
                    _hasSpawnedInitial = true;
                }

                SpawnByInterval();

                if (_target != null) _distanceField.Update(_target.position);

                using (_moveMarker.Auto())
                {
                    MoveAgents();
                    ReturnStrandedAgents();
                    CrushDefeatCount += _collapseDetector.Detect(_agents);
                    CountCollapseDefeats();
                }

                if (_target != null)
                {
                    using (_bodyMarker.Auto())
                    {
                        var target = (float3)_target.position;
                        _bodyLender.ReturnBodies(_agents, target);
                        _bodyLender.LendBodies(_agents, target, cache, _distanceField.Grid);
                        _bodyLender.SyncBodyTransforms(_agents, Time.deltaTime, _distanceField.Grid);
                    }
                }

                using (_renderMarker.Auto())
                {
                    _crowdRenderer.Render(_agents);
                }

                _debrisSpawner?.Tick(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            _crowdRenderer?.Dispose();
            _distanceField?.Dispose();
            _collapseDetector?.Dispose();
            _groups?.Dispose();
            if (_agents.IsCreated) _agents.Dispose();
            _updateRecorder.Dispose();
            _moveRecorder.Dispose();
            _bodyRecorder.Dispose();
            _renderRecorder.Dispose();
        }

        private void OnDrawGizmos()
        {
            if (!_drawDistanceField || _distanceField == null || _target == null) return;

            _distanceField.DrawGizmos(_target.position, _distanceGizmoRadius);
        }

        /// <summary>
        /// グループを更新してから、敵を動かす。動かしたあと、グループを 1 つ整える（穴詰め・合流・並べ替え）。
        /// </summary>
        private void MoveAgents()
        {
            var deltaTime = Time.deltaTime;
            var playerPosition = _target != null ? (float3)_target.position : float3.zero;
            var groupHandle = _groups.Schedule(_agents, _distanceField.Grid, _distanceField.Distances, _formation,
                playerPosition, _moveSpeed, deltaTime);

            new EnemyMoveJob
            {
                Agents = _agents,
                Grid = _distanceField.Grid,
                Distances = _distanceField.Distances,
                Groups = _groups.Groups,
                Paths = _groups.Paths,
                EngageSlots = _groups.EngageSlots,
                Formation = _formation,
                PlayerPosition = playerPosition,
                DeltaTime = deltaTime,
                MoveSpeed = _moveSpeed,
                TurnSpeed = math.radians(_turnSpeed),
                StopDistance = _stopDistance,
                Gravity = -Physics.gravity.y,
                BrokenMovePartLimit = _bodyPrefab.BrokenMovePartLimit,
                FallDefeatHeight = _fallDefeatHeight
            }.Schedule(_agents.Length, 64, groupHandle).Complete();

            _groups.MaintainNext(_agents, _distanceField.Grid, _distanceField.Distances, _formation,
                _bodyPrefab.BrokenMovePartLimit);
        }

        /// <summary>
        /// 崩落で倒されたことが記録された敵を数えて体の中心からエネルギーを出し、記録を消す。倒された敵の体は、次の ReturnBodies で返す。
        /// </summary>
        private void CountCollapseDefeats()
        {
            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                if (!agent.IsDefeatedByCollapse) continue;

                agent.IsDefeatedByCollapse = false;
                _agents[i] = agent;
                CollapseDefeatCount++;
                _emitEnergy?.Invoke((Vector3)agent.Position + Vector3.up * _crushBodyCenterHeight);
            }
        }

        /// <summary>
        /// プレイヤーへたどり着けない状態が戻す時間を超えた敵のうち、カメラに映っていない敵を、次の有効な生成位置へ移す。
        /// 移した敵は元のグループから抜き、その生成位置で新しいグループにする。部位の状態はそのまま持ち続ける。
        /// 動けない敵は戻さない（同時に存在する数の上限を埋め続ける、仕様の戦略の為）。体を貸している敵も戻さない（プレイヤーの近くにいる為）。
        /// </summary>
        private void ReturnStrandedAgents()
        {
            var camera = Camera.main;
            if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

            EnemySpawnPoint point = null;
            var brokenMovePartLimit = _bodyPrefab.BrokenMovePartLimit;

            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                if (!agent.IsAlive || agent.StrandedTime < _strandedReturnDelay) continue;
                if (agent.BodyIndex >= 0 || agent.BrokenMovePartCount >= brokenMovePartLimit) continue;

                var bounds = new Bounds((Vector3)agent.Position + Vector3.up * VISIBILITY_HEIGHT,
                    Vector3.one * VISIBILITY_SIZE);
                if (camera != null && GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) continue;

                if (point == null)
                {
                    if (!TryGetNextSpawnPoint(out point)) return;

                    _groups.CloseGroup();
                }

                EnemyGroups.Leave(ref agent);
                var position = point.GetSpawnPosition();
                agent.Position = position;
                agent.Yaw = GetYawToTarget(position);
                agent.IsGrounded = false;
                agent.VerticalSpeed = 0f;
                agent.FallStartHeight = position.y;
                agent.StrandedTime = 0f;
                _groups.TryAdd(i, ref agent, point.transform.position, GetYawToTarget(point.transform.position),
                    Random.Range(0f, _formation.HoldDuration), _formation);
                _agents[i] = agent;
            }

            if (point != null) _groups.CloseGroup();
        }

        /// <summary>
        /// 初期生成情報の範囲に、決まった数の敵を置く。グループの人数ずつ、範囲から選んだ中心の周りにまとめて置く。
        /// </summary>
        private void SpawnInitial()
        {
            foreach (var area in _initialSpawnAreas)
            {
                var center = Vector3.zero;
                for (var i = 0; i < area.Count; i++)
                {
                    if (i % _formation.GroupSize == 0)
                    {
                        center = area.GetSpawnPosition();
                        _groups.CloseGroup();
                    }

                    var offset = Random.insideUnitCircle * _groupSpawnRadius;
                    if (!TrySpawn(center + new Vector3(offset.x, 0f, offset.y), center)) return;
                }
            }

            _groups.CloseGroup();
        }

        /// <summary>
        /// 生成情報ごとに間隔を数え、間隔が来たら次の有効な生成位置から、一度に出す数の上限まで出す。
        /// 一度に出した敵は、グループの人数ずつ新しいグループにする。
        /// </summary>
        private void SpawnByInterval()
        {
            var spawnInfos = _spawnSystem.SpawnInfos;

            for (var i = 0; i < spawnInfos.Count; i++)
            {
                var info = spawnInfos[i];
                _spawnTimers[i] += Time.deltaTime;
                if (_spawnTimers[i] < info.Interval) continue;

                _spawnTimers[i] -= info.Interval;
                if (!TryGetNextSpawnPoint(out var point)) continue;

                _groups.CloseGroup();
                for (var n = 0; n < info.MaxCountPerSpawn; n++)
                {
                    if (!TrySpawn(point.GetSpawnPosition(), point.transform.position)) break;
                }

                _groups.CloseGroup();
            }
        }

        /// <summary>
        /// 生成位置を順番に回し、次の有効な生成位置を返す。無効な生成位置は飛ばす。
        /// </summary>
        private bool TryGetNextSpawnPoint(out EnemySpawnPoint point)
        {
            for (var i = 0; i < _spawnPoints.Count; i++)
            {
                var candidate = _spawnPoints[_nextSpawnPointIndex];
                _nextSpawnPointIndex = (_nextSpawnPointIndex + 1) % _spawnPoints.Count;

                if (candidate == null || !candidate.IsEnabled) continue;

                point = candidate;
                return true;
            }

            point = null;
            return false;
        }

        /// <summary>
        /// 空いている敵の状態を使い、目標の方を向けて出し、生成中のグループの隊列の最後に入れる。空きがなければ出さない。
        /// </summary>
        /// <param name="position">出す位置</param>
        /// <param name="groupCenter">新しいグループを作るときの、アンカーの位置</param>
        private bool TrySpawn(Vector3 position, Vector3 groupCenter)
        {
            for (var i = 0; i < _agents.Length; i++)
            {
                if (_agents[i].IsAlive) continue;

                _bodyLender.DiscardKeptShapes(i);
                var agent = new EnemyAgent
                {
                    IsAlive = true,
                    Position = position,
                    FallStartHeight = position.y,
                    Yaw = GetYawToTarget(position),
                    BodyIndex = -1,
                    GroupIndex = -1
                };

                var phaseTimer = Random.Range(0f, _formation.HoldDuration);
                _groups.TryAdd(i, ref agent, groupCenter, GetYawToTarget(groupCenter), phaseTimer, _formation);
                _agents[i] = agent;
                return true;
            }

            return false;
        }

        private float GetYawToTarget(Vector3 position)
        {
            if (_target == null) return 0f;

            var direction = _target.position - position;
            return direction.x == 0f && direction.z == 0f ? 0f : math.atan2(direction.x, direction.z);
        }
    }
}
