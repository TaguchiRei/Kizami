using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の状態（EnemyAgent）の配列と体のプールを持ち、ステージシーンの EnemySpawnSystem の設定に従って敵を出す Adapter。インゲームのシーンへ置く。
    /// 開始時にスポーン位置（EnemySpawnPoint）ごとに編成の 1 グループを出す。敵の状態の数は有効なスポーン位置の編成の人数の合計、グループの数は有効なスポーン位置の数（グループの番号はスポーン位置の番号）で、出ている敵は敵の種類ごとの EnemyCrowdRenderer でまとめて描画する。
    /// 経路の格子は初期化のときに EnemySpawnSystem の範囲で作り、ステージのボクセルのモデルの形が変わったら、その範囲を調べ直す。
    /// 距離マップは、プレイヤーの近く（追跡範囲。区画の大きさは EnemySpawnSystem の設定）だけを、プレイヤーのいるノードか追跡範囲が変わるか、格子を調べ直すたびに計算し直す。
    /// 追跡範囲の中でプレイヤーへたどり着けない状態が続いた敵は、カメラに映っていなければ自分のグループの持ち場へ戻す。
    /// 足場ごと一定の高さ以上落ちた敵と、ボクセルから切り離されて落ちてくる塊に潰された敵は、崩落で倒す。体を貸していれば返し、かけらは出さない。
    /// 毎フレーム、前のフレームのグループごとの結果を EnemySquadObservationState に書き、部隊の命令を決める関数を呼んで、EnemySquadCommandState の状態と持ち場をグループに写す。
    /// そのあと EnemyGroupJob でグループのアンカーを、EnemyMoveJob で敵を隊列の位置へ動かす。
    /// 切断できる体（EnemyBody）の貸し借りと近接切断の結果の受け渡しは敵の種類ごとの EnemyBodyLender が、体から外れた切っていない部位の見た目用の物は EnemyDebrisSpawner が行う。
    /// アタッカーの弾は同じ GameObject の EnemyShooter が、ディフェンダーのバリアは EnemyBarriers が扱う。
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

        /// <summary> 生成する位置が立てる所に来るまで、選び直す回数の上限 </summary>
        private const int SPAWN_POSITION_ATTEMPTS = 16;

        private const string UPDATE_MARKER_NAME = "Kizami.Enemy.Update";
        private const string MOVE_MARKER_NAME = "Kizami.Enemy.Move";
        private const string BODY_MARKER_NAME = "Kizami.Enemy.Body";
        private const string RENDER_MARKER_NAME = "Kizami.Enemy.Render";

        private static readonly ProfilerMarker _updateMarker = new(UPDATE_MARKER_NAME);
        private static readonly ProfilerMarker _moveMarker = new(MOVE_MARKER_NAME);
        private static readonly ProfilerMarker _bodyMarker = new(BODY_MARKER_NAME);
        private static readonly ProfilerMarker _renderMarker = new(RENDER_MARKER_NAME);

        /// <summary> 敵の種類の数。種類ごとの配列の長さ </summary>
        private static readonly int _kindCount = Enum.GetValues(typeof(EnemyKind)).Length;

        private readonly List<EnemySpawnPoint> _spawnPoints = new();

        /// <summary> 初期化のときに有効で、編成を持つスポーン位置。1 つにつき 1 グループを出す。位置の区画は互いに重ならない </summary>
        private readonly List<EnemySpawnPoint> _activeSpawnPoints = new();

        /// <summary> カメラの視錐台の面。戻れない敵がカメラに映っているかを調べる作業用の配列 </summary>
        private readonly Plane[] _frustumPlanes = new Plane[6];

        /// <summary> スポーン位置を集めるときに、すでにスポーン位置のある区画を覚える作業用の集合 </summary>
        private readonly HashSet<Vector2Int> _usedSections = new();

        /// <summary> 有効なスポーン位置ごとの、編成のうち体の設定のある種類の人数。部隊の満員の人数 </summary>
        private readonly List<int> _fullMemberCounts = new();

        private readonly EnemySquadObservationState _observationState = new();

        [SerializeField]
        [Tooltip("敵の種類ごとの体の設定。種類 1 つにつき 1 件。設定のない種類の敵は出さない")]
        private EnemyKindSettings[] _kindSettings = Array.Empty<EnemyKindSettings>();

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
        [Tooltip("体を返した敵の、短くなった部位の形を預かれる数（敵の種類ごと）。初期化のときに種類ごとにこの数だけ保管用の物を作る")]
        private int _shapeKeeperCapacity = 64;

        [SerializeField]
        [Tooltip("かけらのプール。体に残す側のかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("アタッカーの弾を扱う EnemyShooter。未設定ならアタッカーは撃たない")]
        private EnemyShooter _shooter;

        [SerializeField]
        [Tooltip("ディフェンダーのバリアを扱う EnemyBarriers。未設定ならバリアを張らない")]
        private EnemyBarriers _barriers;

        [SerializeField]
        [Tooltip("フィニッシャーの攻撃を扱う EnemyFinisherAttack。未設定ならフィニッシャーは攻撃しない")]
        private EnemyFinisherAttack _finisherAttack;

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
        [Tooltip("飛んでいる敵（攻撃中のフィニッシャー）の速さ（m/s）")]
        private float _flySpeed = 8f;

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
        [Tooltip("戻れない敵を持ち場の周りへ移すときの、持ち場からの半径（m）")]
        private float _strandedReturnRadius = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("追跡範囲の中でプレイヤーへたどり着けない状態がこの時間（秒）続いた敵は、カメラに映っていなければ自分のグループの持ち場へ戻す。動けない敵と、体を貸している敵は戻さない")]
        private float _strandedReturnDelay = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("この高さ（m）以上落ちて着地した敵を、崩落で倒す。歩いて降りられる高さより高くする。宙に浮いている敵は倒れない")]
        private float _fallDefeatHeight = 3f;

        [SerializeField, Min(0.1f)]
        [Tooltip("宙に浮いている敵が落ちる速さの上限（m/s）")]
        private float _floatingFallSpeed = 2f;

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

        /// <summary> 敵の種類ごとの体のプレハブ。番号は EnemyKind の値で、設定のない種類は null </summary>
        private EnemyBody[] _bodyPrefabs;

        /// <summary> 敵の種類ごとのまとめて描画。番号は EnemyKind の値で、設定のない種類は null </summary>
        private EnemyCrowdRenderer[] _crowdRenderers;

        /// <summary> 敵の種類ごとの体の貸し借り。番号は EnemyKind の値で、設定のない種類は null </summary>
        private EnemyBodyLender[] _bodyLenders;

        private EnemyDistanceField _distanceField;

        private EnemyGroups _groups;

        private EnemyCollapseDetector _collapseDetector;

        /// <summary> 体から外れた部位の見た目用の物。ディゾルブのマテリアルが未設定なら null </summary>
        private EnemyDebrisSpawner _debrisSpawner;

        /// <summary> 崩落で倒した敵のエネルギーを出す関数。引数は倒した敵の体の中心の位置 </summary>
        private Action<Vector3> _emitEnergy;

        /// <summary> 部隊の命令を決める関数（EnemySquadService.Step）。引数は経過時間（秒） </summary>
        private Action<float> _stepSquads;

        private IEnemySquadCommandState _commandState;

        /// <summary> 置き場の位置を求めたときのプレイヤーの位置。置き場の螺旋の中心 </summary>
        private float3 _encircleCenter;

        private ProfilerRecorder _updateRecorder;
        private ProfilerRecorder _moveRecorder;
        private ProfilerRecorder _bodyRecorder;
        private ProfilerRecorder _renderRecorder;

        private bool _hasSpawnedSquads;

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

        /// <summary> 敵の状態の数（有効なスポーン位置の編成の人数の合計） </summary>
        public int Capacity => _agents.IsCreated ? _agents.Length : 0;

        /// <summary> 使われているグループの数 </summary>
        public int GroupCount => _groups?.ActiveCount ?? 0;

        /// <summary> 敵に貸している体の数（全種類の合計） </summary>
        public int LentBodyCount => SumOverLenders(lender => lender.LentBodyCount);

        /// <summary> 体の数（全種類の合計） </summary>
        public int BodyCount => SumOverLenders(lender => lender.BodyCount);

        /// <summary> 体を返した敵から預かっている、短くなった部位の数（全種類の合計） </summary>
        public int KeptShapeCount => SumOverLenders(lender => lender.KeptShapeCount);

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

        /// <summary> ステージシーンのスポーン位置 </summary>
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
        /// <param name="blackBoard">EnemySquadObservationState の登録先と、EnemySquadCommandState の取得元</param>
        /// <param name="spawnOrb">倒れた体に残っていた切断済みの部位を、オーブにする関数。引数はオーブを出す位置</param>
        /// <param name="emitEnergy">崩落で倒した敵のエネルギーを出す関数。引数は倒した敵の体の中心の位置</param>
        /// <param name="applyDamage">敵の攻撃がプレイヤーに当たったときにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <param name="requestLaunch">プレイヤーを真上へ打ち上げる関数（PlayerMovementService.RequestLaunch）。引数は上向きの打ち出し速度（m/s）</param>
        /// <param name="stepSquads">部隊の命令を決める関数（EnemySquadService.Step）。引数は経過時間（秒）</param>
        public void Initialize(IBlackBoard blackBoard, Action<Vector3> spawnOrb, Action<Vector3> emitEnergy, Action<int> applyDamage,
            Action<float> requestLaunch, Action<float> stepSquads)
        {
            _emitEnergy = emitEnergy;
            _stepSquads = stepSquads;

            if (_fragmentPool == null)
            {
                UsefulLogger.LogError("かけらのプールが設定されていません。", this);
                return;
            }

            if (!TryCollectBodyPrefabs()) return;

            _spawnSystem = FindAnyObjectByType<EnemySpawnSystem>();
            if (_spawnSystem == null)
            {
                UsefulLogger.LogError("ステージシーンに EnemySpawnSystem が見つからない為、敵を出せません。", this);
                return;
            }

            var agentCount = CollectSpawnPoints();

            if (_debrisMaterial == null)
            {
                UsefulLogger.LogWarning("ディゾルブのマテリアルが設定されていない為、体から外れた部位はその場で消えます。", this);
            }
            else
            {
                _debrisSpawner = new EnemyDebrisSpawner(transform, _debrisMaterial, _debrisCapacity, _debrisLifetime,
                    _debrisOutwardSpeed, _debrisUpwardSpeed, _debrisAngularSpeed);
            }

            _agents = new NativeArray<EnemyAgent>(agentCount, Allocator.Persistent);
            _distanceField = new EnemyDistanceField(_spawnSystem.NavigationBounds, _cellSize, _enemyHeight, _climbHeight,
                _dropHeight, _groundLayers, _spawnSystem.SectionSize, _spawnSystem.SectionOrigin);
            _collapseDetector = new EnemyCollapseDetector(_crushMinFallSpeed, _crushMinVolume, _crushBodyCenterHeight,
                _crushSurfaceMargin);
            foreach (var loader in FindObjectsByType<VoxelModelLoader>(FindObjectsSortMode.None))
            {
                _distanceField.Watch(loader);
                _collapseDetector.Watch(loader);
            }

            _groups = new EnemyGroups(_activeSpawnPoints.Count);
            if (!blackBoard.TryGetBoard<EnemyBoard>(out var enemyBoard, this) ||
                !blackBoard.TryGetSceneState<EnemyBoard, IEnemySquadCommandState>(out _commandState, this)) return;

            _observationState.SetSquadCount(_groups.Groups.Length);
            _observationState.SetEncircleSlotCount(EnemyGroups.MAX_ENCIRCLE_SLOTS);
            enemyBoard.RegisterSceneState<IEnemySquadObservationState>(_observationState, gameObject.scene.buildIndex);
            WarnUnstandableSpawnPoints();
            _updateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, UPDATE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _moveRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MOVE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _bodyRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, BODY_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, RENDER_MARKER_NAME, TIMING_SAMPLE_COUNT);

            _crowdRenderers = new EnemyCrowdRenderer[_kindCount];
            _bodyLenders = new EnemyBodyLender[_kindCount];
            foreach (var settings in _kindSettings)
            {
                var kind = settings.Kind;
                _crowdRenderers[(int)kind] = new EnemyCrowdRenderer(kind, settings.BodyPrefab, _agents.Length);
                _bodyLenders[(int)kind] = new EnemyBodyLender(kind, settings.BodyPrefab, transform, settings.BodyCount,
                    _agents.Length, _shapeKeeperCapacity, _fragmentPool, _lendDistance, _returnDistance,
                    _reclaimDistance, _reclaimMargin, spawnOrb, _debrisSpawner != null ? _debrisSpawner.Spawn : null);
            }

            if (_shooter == null)
            {
                UsefulLogger.LogWarning("EnemyShooter が設定されていない為、アタッカーは撃ちません。", this);
            }
            else
            {
                _shooter.Initialize(_bodyPrefabs[(int)EnemyKind.Attacker], _target, applyDamage);
            }

            if (_barriers == null)
            {
                UsefulLogger.LogWarning("EnemyBarriers が設定されていない為、ディフェンダーはバリアを張りません。", this);
            }
            else
            {
                _barriers.Initialize(_target, _groups.Groups.Length, requestLaunch);
            }

            if (_finisherAttack == null)
            {
                UsefulLogger.LogWarning("EnemyFinisherAttack が設定されていない為、フィニッシャーは攻撃しません。", this);
            }
            else
            {
                _finisherAttack.Initialize(_target, _debrisMaterial, applyDamage);
            }

            base.Initialize();
        }

        /// <summary>
        /// 使われているグループを、待機・追跡・帰還の状態ごとに数える。初期化の前は 0。
        /// </summary>
        public void CountGroupStates(out int waiting, out int tracking, out int returning)
        {
            waiting = tracking = returning = 0;
            _groups?.CountStates(out waiting, out tracking, out returning);
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

            foreach (var lender in _bodyLenders)
            {
                lender?.ReceiveCutResults(results, plane, _agents);
            }
        }

        private void Update()
        {
            var cache = MeshDataCache.Instance;
            if (cache == null || cache.Store == null) return;

            using (_updateMarker.Auto())
            {
                if (!_hasSpawnedSquads)
                {
                    SpawnSquads();
                    _hasSpawnedSquads = true;
                }

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
                        foreach (var lender in _bodyLenders)
                        {
                            if (lender == null) continue;

                            lender.ReturnBodies(_agents, target);
                            lender.LendBodies(_agents, target, cache, _distanceField.Grid);
                            lender.SyncBodyTransforms(_agents, Time.deltaTime, _distanceField.Grid);
                        }
                    }
                }

                if (_barriers != null)
                {
                    _barriers.Tick(_agents, _groups, _bodyLenders[(int)EnemyKind.Defender], _distanceField.Grid);
                }

                if (_shooter != null) _shooter.Tick(_agents, Time.deltaTime);

                if (_finisherAttack != null)
                {
                    _finisherAttack.Tick(_agents, _groups, _bodyLenders, Time.deltaTime, _distanceField.Grid);
                }

                using (_renderMarker.Auto())
                {
                    foreach (var crowdRenderer in _crowdRenderers)
                    {
                        crowdRenderer?.Render(_agents);
                    }
                }

                _debrisSpawner?.Tick(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            if (_crowdRenderers != null)
            {
                foreach (var crowdRenderer in _crowdRenderers)
                {
                    crowdRenderer?.Dispose();
                }
            }

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
        /// 種類ごとの体の設定を、EnemyKind の値を番号にした体のプレハブの配列にまとめる。
        /// 空の設定、プレハブのない設定、同じ種類の設定が 2 件以上あるとき、設定が 1 件もないときは、すべてエラーログに出して false を返す。
        /// </summary>
        private bool TryCollectBodyPrefabs()
        {
            _bodyPrefabs = new EnemyBody[_kindCount];
            var isValid = _kindSettings.Length > 0;
            if (!isValid) UsefulLogger.LogError("敵の種類ごとの体の設定が 1 件もありません。", this);

            for (var i = 0; i < _kindSettings.Length; i++)
            {
                var settings = _kindSettings[i];
                if (settings == null || settings.BodyPrefab == null)
                {
                    UsefulLogger.LogError($"敵の種類ごとの体の設定 {i} 番に、体のプレハブが設定されていません。", this);
                    isValid = false;
                    continue;
                }

                if (_bodyPrefabs[(int)settings.Kind] != null)
                {
                    UsefulLogger.LogError($"敵の種類 {settings.Kind} の体の設定が 2 件以上あります。", this);
                    isValid = false;
                    continue;
                }

                _bodyPrefabs[(int)settings.Kind] = settings.BodyPrefab;
            }

            return isValid;
        }

        /// <summary>
        /// 種類ごとの体の貸し借りの値を合計する。初期化の前は 0。
        /// </summary>
        private int SumOverLenders(Func<EnemyBodyLender, int> select)
        {
            if (_bodyLenders == null) return 0;

            var total = 0;
            foreach (var lender in _bodyLenders)
            {
                if (lender != null) total += select(lender);
            }

            return total;
        }

        /// <summary>
        /// 置き場の位置を求め、部隊の命令を決めてグループに写し、グループを更新してから、敵を動かす。動かしたあと、グループを 1 つ整える（穴詰め・並べ替え）。
        /// </summary>
        private void MoveAgents()
        {
            var deltaTime = Time.deltaTime;
            var playerPosition = _target != null ? (float3)_target.position : float3.zero;
            _encircleCenter = playerPosition;
            _groups.BuildSlotPoints(_distanceField.Grid, _distanceField.Distances, _distanceField.TrackingMin,
                _distanceField.TrackingMax, _formation, playerPosition);
            WriteObservations();
            _stepSquads?.Invoke(deltaTime);
            ApplyCommands();

            var groupHandle = _groups.Schedule(_agents, _distanceField.Grid, _distanceField.Distances,
                _distanceField.TrackingMin, _distanceField.TrackingMax, _formation, playerPosition, _moveSpeed, deltaTime);

            new EnemyMoveJob
            {
                Agents = _agents,
                Grid = _distanceField.Grid,
                Distances = _distanceField.Distances,
                TrackingMin = _distanceField.TrackingMin,
                TrackingMax = _distanceField.TrackingMax,
                Groups = _groups.Groups,
                Paths = _groups.Paths,
                Formation = _formation,
                PlayerPosition = playerPosition,
                DeltaTime = deltaTime,
                MoveSpeed = _moveSpeed,
                FlySpeed = _flySpeed,
                TurnSpeed = math.radians(_turnSpeed),
                StopDistance = _stopDistance,
                Gravity = -Physics.gravity.y,
                FallDefeatHeight = _fallDefeatHeight,
                FloatingFallSpeed = _floatingFallSpeed
            }.Schedule(_agents.Length, 64, groupHandle).Complete();

            _groups.MaintainNext(_agents, _distanceField.Grid, _distanceField.Distances);
        }

        /// <summary>
        /// グループごとの、前のフレームで動かした結果と、このフレームで求めた置き場を EnemySquadObservationState に書く。追跡範囲の判定は、このフレームの距離マップで行う。
        /// </summary>
        private void WriteObservations()
        {
            var groups = _groups.Groups;
            for (var g = 0; g < groups.Length; g++)
            {
                var group = groups[g];
                var point = _activeSpawnPoints[g];
                _observationState.SetSquad(g, new EnemySquadObservation
                {
                    IsActive = group.IsActive,
                    AnchorPosition = group.AnchorPosition,
                    MemberCount = group.IsActive ? _groups.CountAliveMembers(g, _agents) : 0,
                    FullMemberCount = _fullMemberCounts[g],
                    SpawnPosition = point != null ? point.transform.position : group.HomePosition,
                    RespawnInterval = point != null ? point.RespawnInterval : 0f,
                    CanSpawn = point != null && point.CanSpawn,
                    IsHomeTracked = IsTracked(group.HomePosition),
                    HasArrived = group.HasArrived,
                    HasReachedHome = group.HasReachedHome,
                    IsReturnBlocked = group.IsReturnBlocked
                });
            }

            var slotPoints = _groups.SlotPoints;
            for (var slot = 0; slot < slotPoints.Length; slot++)
            {
                var offset = EnemyFormationSettings.GetSpiralOffset(slot, _formation.EncircleInnerRadius,
                    _formation.EncircleLoopSpacing, _formation.EncircleSlotSpacing);
                var radius = EnemyFormationSettings.GetSpiralRadius(slot, _formation.EncircleInnerRadius,
                    _formation.EncircleLoopSpacing, _formation.EncircleSlotSpacing);
                _observationState.SetEncircleSlot(slot, new EnemyEncircleSlot
                {
                    Position = slotPoints[slot],
                    Ring = (int)math.floor((radius - _formation.EncircleInnerRadius) / _formation.EncircleLoopSpacing),
                    Angle = math.atan2(offset.x, offset.y)
                });
            }

            _observationState.SetEncircleArea(_encircleCenter.xz, _groups.LastUsableSlot);
        }

        /// <summary>
        /// EnemySquadCommandState の状態・持ち場・置き場を、使われているグループに写す。状態の切り替えは EnemyGroupJob が行う。
        /// </summary>
        private void ApplyCommands()
        {
            var groups = _groups.Groups;
            var commands = _commandState.Squads;
            var count = Mathf.Min(groups.Length, commands.Count);
            for (var g = 0; g < count; g++)
            {
                var group = groups[g];
                if (!group.IsActive) continue;

                var command = commands[g];
                group.CommandedState = command.State;
                group.HomePosition = command.HomePosition;
                group.EncircleSlot = command.EncircleSlot;
                groups[g] = group;
            }
        }

        /// <summary>
        /// 位置の真下の列が、距離マップを計算した追跡範囲の中にあるか。EnemyGroupJob の判定と同じ。
        /// </summary>
        private bool IsTracked(float3 position)
        {
            var grid = _distanceField.Grid;
            if (!grid.TryGetColumn(position, out var column)) return false;

            var x = column % grid.Width;
            var z = column / grid.Width;
            var min = _distanceField.TrackingMin;
            var max = _distanceField.TrackingMax;
            return x >= min.x && x <= max.x && z >= min.y && z <= max.y;
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
        /// 追跡範囲の中でプレイヤーへたどり着けない状態が戻す時間を超えた敵のうち、カメラに映っていない敵を、自分のグループの持ち場の周りへ移す。
        /// 移した敵はグループに残り、隊列の位置へ歩いて戻る。部位の状態はそのまま持ち続ける。
        /// 持ち場が追跡範囲の中で、そこからもプレイヤーへたどり着けない（分断されている）ときは戻さない。
        /// 動けない敵は戻さない（グループに残ったまま、その場に留まる）。体を貸している敵も戻さない（プレイヤーの近くにいる為）。
        /// 行動中の敵（動き方が Walking でない敵）も戻さない。フィニッシャーの攻撃が位置を決めている為。
        /// </summary>
        private void ReturnStrandedAgents()
        {
            var camera = Camera.main;
            if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                if (!agent.IsAlive || agent.StrandedTime < _strandedReturnDelay || agent.GroupIndex < 0) continue;
                if (agent.BodyIndex >= 0 || agent.BrokenMovePartCount >= agent.BrokenMovePartLimit ||
                    agent.MoveMode != EnemyMoveMode.Walking) continue;

                var bounds = new Bounds((Vector3)agent.Position + Vector3.up * VISIBILITY_HEIGHT,
                    Vector3.one * VISIBILITY_SIZE);
                if (camera != null && GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) continue;

                var group = _groups.Groups[agent.GroupIndex];
                if (!group.IsActive || _distanceField.IsCutOff(group.HomePosition)) continue;

                Vector3 home = group.HomePosition;
                var position = PickStandablePosition(() =>
                {
                    var offset = Random.insideUnitCircle * _strandedReturnRadius;
                    return home + new Vector3(offset.x, 0f, offset.y);
                }, home);
                agent.Position = position;
                agent.Yaw = GetYawToTarget(position);
                agent.IsGrounded = false;
                agent.VerticalSpeed = 0f;
                agent.FallStartHeight = position.y;
                agent.StrandedTime = 0f;
                _agents[i] = agent;
            }
        }

        /// <summary>
        /// 有効なスポーン位置ごとに、同じ番号のグループで編成の 1 グループを出す。
        /// </summary>
        private void SpawnSquads()
        {
            for (var g = 0; g < _activeSpawnPoints.Count; g++)
            {
                SpawnSquad(g);
            }
        }

        /// <summary>
        /// スポーン位置 g から、同じ番号のグループを開いて編成の全員を出す。グループのアンカーと持ち場はスポーン位置の中心にし、メンバーはスポーン位置の半径の中の立てる所に出す。
        /// 体の設定のない種類のメンバーは出さない。
        /// </summary>
        private void SpawnSquad(int g)
        {
            var point = _activeSpawnPoints[g];
            var center = point.transform.position;
            _groups.Open(g, center, GetYawToTarget(center), _formation);
            foreach (var kind in point.Composition.Members)
            {
                if (_bodyPrefabs[(int)kind] == null) continue;

                TrySpawn(g, kind, PickStandablePosition(point.GetSpawnPosition, center));
            }
        }

        /// <summary>
        /// 位置の真下に、その高さから乗れる立てる層があるか。グループのアンカーが床に乗る規則（EnemyGroupJob.TryFitToFloor）と同じ。
        /// 橋のスロープの中のように、上に床があって足元に立てる層がない所を、生成する位置に選ばない為。
        /// </summary>
        private bool IsStandable(Vector3 position)
        {
            var grid = _distanceField.Grid;
            return grid.TryGetColumn(position, out var column)
                   && grid.GetHighestNodeBelow(column, position.y + grid.ClimbHeight) >= 0;
        }

        /// <summary>
        /// pick で選んだ位置が立てる所に来るまで、SPAWN_POSITION_ATTEMPTS 回まで選び直す。見つからなければ false。
        /// </summary>
        private bool TryPickStandablePosition(Func<Vector3> pick, out Vector3 position)
        {
            for (var attempt = 0; attempt < SPAWN_POSITION_ATTEMPTS; attempt++)
            {
                position = pick();
                if (IsStandable(position)) return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// pick で選んだ位置が立てる所に来るまで選び直し、見つからなければ fallback を返す。
        /// </summary>
        private Vector3 PickStandablePosition(Func<Vector3> pick, Vector3 fallback)
        {
            return TryPickStandablePosition(pick, out var position) ? position : fallback;
        }

        /// <summary>
        /// 中心が立てる所にないスポーン位置を、名前つきで警告する。そのスポーン位置から出したグループのアンカーは床に乗れず、動かない為。
        /// </summary>
        private void WarnUnstandableSpawnPoints()
        {
            foreach (var point in _activeSpawnPoints)
            {
                if (IsStandable(point.transform.position)) continue;

                UsefulLogger.LogWarning(
                    $"スポーン位置 {point.name} の中心に、敵が立てる所がありません。そこから出したグループは動けないので、床の上へ動かしてください。",
                    point);
            }
        }

        /// <summary>
        /// 有効なスポーン位置を集め、出す敵の数（編成の人数の合計）を返す。
        /// 1 つの区画に置けるスポーン位置は 1 つで、位置の区画にすでに有効なスポーン位置があれば、Hierarchy で後ろのものは出さない。
        /// 編成のないスポーン位置、編成に体の設定のない種類があるスポーン位置、区画の番号が位置と食い違うスポーン位置、区画が重なったスポーン位置は、名前つきで警告する。
        /// </summary>
        private int CollectSpawnPoints()
        {
            _spawnSystem.GetComponentsInChildren(true, _spawnPoints);
            _activeSpawnPoints.Clear();
            _usedSections.Clear();
            _fullMemberCounts.Clear();
            var agentCount = 0;

            foreach (var point in _spawnPoints)
            {
                if (point == null || !point.IsEnabled) continue;

                if (point.Composition == null)
                {
                    UsefulLogger.LogWarning($"スポーン位置 {point.name} に編成が設定されていない為、敵を出しません。", point);
                    continue;
                }

                var section = _spawnSystem.GetSection(point.transform.position);
                if (!_usedSections.Add(section))
                {
                    UsefulLogger.LogWarning(
                        $"スポーン位置 {point.name} の区画 {section} には、すでに別のスポーン位置がある為、敵を出しません。1 つの区画に置けるスポーン位置は 1 つです。",
                        point);
                    continue;
                }

                if (section != point.Section)
                {
                    UsefulLogger.LogWarning(
                        $"スポーン位置 {point.name} の区画の番号 {point.Section} が、位置の区画 {section} と食い違っています。区画の番号か位置を直してください。",
                        point);
                }

                var memberCount = 0;
                foreach (var kind in point.Composition.Members)
                {
                    if (_bodyPrefabs[(int)kind] != null)
                    {
                        memberCount++;
                        continue;
                    }

                    UsefulLogger.LogWarning($"スポーン位置 {point.name} の編成にある {kind} は、体の設定がない為に出しません。", point);
                }

                agentCount += memberCount;
                _activeSpawnPoints.Add(point);
                _fullMemberCounts.Add(memberCount);
            }

            return agentCount;
        }

        /// <summary>
        /// 空いている敵の状態を使い、目標の方を向けて出し、グループ g の隊列の最後に入れる。敵の状態かグループに空きがなければ出さない。
        /// </summary>
        /// <param name="g">入れるグループの番号。使われているグループに限る</param>
        /// <param name="kind">出す敵の種類。体の設定のある種類に限る</param>
        /// <param name="position">出す位置</param>
        private bool TrySpawn(int g, EnemyKind kind, Vector3 position)
        {
            var bodyPrefab = _bodyPrefabs[(int)kind];

            for (var i = 0; i < _agents.Length; i++)
            {
                if (_agents[i].IsAlive) continue;

                // この状態を前に使っていた敵は別の種類のことがあるので、すべての種類の預かり分を捨てる
                foreach (var lender in _bodyLenders)
                {
                    lender?.DiscardKeptShapes(i);
                }

                var agent = new EnemyAgent
                {
                    IsAlive = true,
                    Kind = kind,
                    BrokenMovePartLimit = bodyPrefab.BrokenMovePartLimit,
                    IsFloating = bodyPrefab.IsFloating,
                    Position = position,
                    FallStartHeight = position.y,
                    Yaw = GetYawToTarget(position),
                    BodyIndex = -1,
                    GroupIndex = -1
                };

                if (!_groups.TryAdd(g, i, ref agent)) return false;

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
