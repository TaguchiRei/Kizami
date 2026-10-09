using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Burst;
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
    /// 敵の状態の数は EnemySpawnSystem の同時に存在する数の上限で、出ている敵は敵の種類ごとの EnemyCrowdRenderer でまとめて描画する。
    /// 経路の格子は初期化のときに EnemySpawnSystem の範囲で作り、ステージのボクセルのモデルの形が変わったら、その範囲を調べ直す。
    /// 距離マップは、プレイヤーの近く（追跡範囲。区画の大きさは EnemySpawnSystem の設定）だけを、プレイヤーのいるノードか追跡範囲が変わるか、格子を調べ直すたびに計算し直す。
    /// 追跡範囲の中でプレイヤーへたどり着けない状態が続いた敵は、カメラに映っていなければ自分のグループの持ち場へ戻す。
    /// 足場ごと一定の高さ以上落ちた敵と、ボクセルから切り離されて落ちてくる塊に潰された敵は、崩落で倒す。体を貸していれば返し、かけらは出さない。
    /// 生成した敵は出した順にグループ（EnemyGroups）へ入れ、毎フレーム EnemyGroupJob でグループのアンカーを、EnemyMoveJob で敵を隊列の位置へ動かす。
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

        /// <summary> 別のグループの敵どうしが重なっているとみなす、水平の距離（m） </summary>
        private const float OVERLAP_DISTANCE = 2f;

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
        private readonly List<EnemyInitialSpawnArea> _initialSpawnAreas = new();

        /// <summary> カメラの視錐台の面。戻れない敵がカメラに映っているかを調べる作業用の配列 </summary>
        private readonly Plane[] _frustumPlanes = new Plane[6];

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

        [SerializeField]
        [Tooltip("別のグループの敵どうしが 2m 以内に重なる組の数を毎フレーム数える（デバッグ表示用）。敵の数の 2 乗に比例して重いので、計測のときは切る")]
        private bool _countsOverlaps;

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
        [Tooltip("初期生成で、グループのメンバーを置く範囲の半径（m）。グループの中心は初期生成の範囲から選ぶ")]
        private float _groupSpawnRadius = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("追跡範囲の中でプレイヤーへたどり着けない状態がこの時間（秒）続いた敵は、カメラに映っていなければ自分のグループの持ち場へ戻す。動けない敵と、体を貸している敵は戻さない")]
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

        /// <summary> 重なる組の数の Job の結果。移動中の組と、両方のグループが着いた組 </summary>
        private NativeArray<int> _overlapCounts;

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

        /// <summary> 別のグループの敵どうしが重なる組のうち、どちらかのグループが移動中の組の数。数えていなければ -1 </summary>
        public int MovingOverlapCount => _countsOverlaps && _overlapCounts.IsCreated ? _overlapCounts[0] : -1;

        /// <summary> 別のグループの敵どうしが重なる組のうち、両方のグループが置き場に着いている組の数。数えていなければ -1 </summary>
        public int ArrivedOverlapCount => _countsOverlaps && _overlapCounts.IsCreated ? _overlapCounts[1] : -1;

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
        /// <param name="applyDamage">敵の攻撃がプレイヤーに当たったときにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <param name="requestLaunch">プレイヤーを真上へ打ち上げる関数（PlayerMovementService.RequestLaunch）。引数は上向きの打ち出し速度（m/s）</param>
        public void Initialize(Action<Vector3> spawnOrb, Action<Vector3> emitEnergy, Action<int> applyDamage,
            Action<float> requestLaunch)
        {
            _emitEnergy = emitEnergy;

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
            _distanceField = new EnemyDistanceField(_spawnSystem.NavigationBounds, _cellSize, _enemyHeight, _climbHeight,
                _dropHeight, _groundLayers, _spawnSystem.SectionSize);
            _collapseDetector = new EnemyCollapseDetector(_crushMinFallSpeed, _crushMinVolume, _crushBodyCenterHeight,
                _crushSurfaceMargin);
            foreach (var loader in FindObjectsByType<VoxelModelLoader>(FindObjectsSortMode.None))
            {
                _distanceField.Watch(loader);
                _collapseDetector.Watch(loader);
            }

            _groups = new EnemyGroups(_agents.Length);
            WarnUnstandableSpawnPoints();
            WarnUnconfiguredKinds();
            _overlapCounts = new NativeArray<int>(2, Allocator.Persistent);
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
                _finisherAttack.Initialize(_target, _debrisMaterial);
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
            if (_overlapCounts.IsCreated) _overlapCounts.Dispose();
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
        /// グループを更新してから、敵を動かす。動かしたあと、グループを 1 つ整える（穴詰め・合流・並べ替え）。
        /// </summary>
        private void MoveAgents()
        {
            var deltaTime = Time.deltaTime;
            var playerPosition = _target != null ? (float3)_target.position : float3.zero;
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
                FallDefeatHeight = _fallDefeatHeight
            }.Schedule(_agents.Length, 64, groupHandle).Complete();

            _groups.MaintainNext(_agents, _distanceField.Grid, _distanceField.Distances, _formation);

            if (_countsOverlaps)
            {
                new OverlapCountJob
                {
                    Agents = _agents,
                    Groups = _groups.Groups,
                    OverlapDistanceSq = OVERLAP_DISTANCE * OVERLAP_DISTANCE,
                    Counts = _overlapCounts
                }.Schedule().Complete();
            }
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
        /// グループを持たない敵は、次の有効な生成位置へ移す。移した敵は元のグループから抜き、持ち場ごとに新しいグループにする（持ち場は引き継ぐ）。部位の状態はそのまま持ち続ける。
        /// 持ち場が追跡範囲の中で、そこからもプレイヤーへたどり着けない（分断されている）ときは戻さない。
        /// 動けない敵は戻さない（同時に存在する数の上限を埋め続ける、仕様の戦略の為）。体を貸している敵も戻さない（プレイヤーの近くにいる為）。
        /// 行動中の敵（動き方が Walking でない敵）も戻さない。フィニッシャーの攻撃が位置を決めている為。
        /// </summary>
        private void ReturnStrandedAgents()
        {
            var camera = Camera.main;
            if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

            var hasOpenGroup = false;
            var openHome = Vector3.zero;

            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                if (!agent.IsAlive || agent.StrandedTime < _strandedReturnDelay) continue;
                if (agent.BodyIndex >= 0 || agent.BrokenMovePartCount >= agent.BrokenMovePartLimit ||
                    agent.MoveMode != EnemyMoveMode.Walking) continue;

                var bounds = new Bounds((Vector3)agent.Position + Vector3.up * VISIBILITY_HEIGHT,
                    Vector3.one * VISIBILITY_SIZE);
                if (camera != null && GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) continue;

                if (!TryGetReturnHome(agent, out var home, out var spawnPoint)) continue;

                if (!hasOpenGroup || home != openHome)
                {
                    _groups.CloseGroup();
                    hasOpenGroup = true;
                    openHome = home;
                }

                EnemyGroups.Leave(ref agent);
                var offset = Random.insideUnitCircle * _groupSpawnRadius;
                var position = spawnPoint != null
                    ? PickStandablePosition(spawnPoint.GetSpawnPosition, home)
                    : home + new Vector3(offset.x, 0f, offset.y);
                agent.Position = position;
                agent.Yaw = GetYawToTarget(position);
                agent.IsGrounded = false;
                agent.VerticalSpeed = 0f;
                agent.FallStartHeight = position.y;
                agent.StrandedTime = 0f;
                _groups.TryAdd(i, ref agent, home, GetYawToTarget(home), Random.Range(0f, _formation.HoldDuration),
                    _formation);
                _agents[i] = agent;
            }

            if (hasOpenGroup) _groups.CloseGroup();
        }

        /// <summary>
        /// 戻れない敵を戻す持ち場を返す。グループを持てばその持ち場、持たなければ次の有効な生成位置（spawnPoint に入れる）。
        /// 持ち場が追跡範囲の中でプレイヤーへたどり着けないとき、生成位置がないときは false。
        /// </summary>
        private bool TryGetReturnHome(in EnemyAgent agent, out Vector3 home, out EnemySpawnPoint spawnPoint)
        {
            spawnPoint = null;
            if (agent.GroupIndex >= 0 && _groups.Groups[agent.GroupIndex].IsActive)
            {
                home = _groups.Groups[agent.GroupIndex].HomePosition;
                return !_distanceField.IsCutOff(home);
            }

            home = Vector3.zero;
            if (!TryGetNextSpawnPoint(out spawnPoint)) return false;

            home = spawnPoint.transform.position;
            return true;
        }

        /// <summary>
        /// 初期生成情報の範囲に、決まった数の敵を置く。グループの人数ずつ、範囲から選んだ中心の周りにまとめて置く。種類は範囲の編成で決め、体の設定のない種類は置かない。
        /// 中心とメンバーの位置は、立てる所（IsStandable）に来るまで選び直す。中心が見つからないグループは置かず、警告を出す。
        /// </summary>
        private void SpawnInitial()
        {
            foreach (var area in _initialSpawnAreas)
            {
                var center = Vector3.zero;
                var hasCenter = false;
                for (var i = 0; i < area.Count; i++)
                {
                    if (i % _formation.GroupSize == 0)
                    {
                        _groups.CloseGroup();
                        hasCenter = TryPickStandablePosition(area.GetSpawnPosition, out center);
                        if (!hasCenter)
                        {
                            UsefulLogger.LogWarning(
                                $"初期生成の範囲 {area.name} から、敵が立てる所を {SPAWN_POSITION_ATTEMPTS} 回で選べなかった為、1 グループ分を置きません。範囲を床の上へ動かしてください。",
                                area);
                        }
                    }

                    var kind = area.GetKind(i % _formation.GroupSize);
                    if (!hasCenter || _bodyPrefabs[(int)kind] == null) continue;

                    var groupCenter = center;
                    var position = PickStandablePosition(() =>
                    {
                        var offset = Random.insideUnitCircle * _groupSpawnRadius;
                        return groupCenter + new Vector3(offset.x, 0f, offset.y);
                    }, center);
                    if (!TrySpawn(kind, position, center)) return;
                }
            }

            _groups.CloseGroup();
        }

        /// <summary>
        /// 生成情報ごとに間隔を数え、間隔が来たら次の有効な生成位置から、一度に出す数の上限まで出す。
        /// 一度に出した敵は、グループの人数ずつ新しいグループにする。種類は生成情報の編成で決め、体の設定のない種類は出さない。
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
                    var kind = info.GetKind(n % _formation.GroupSize);
                    if (_bodyPrefabs[(int)kind] == null) continue;

                    if (!TrySpawn(kind, PickStandablePosition(point.GetSpawnPosition, point.transform.position),
                            point.transform.position)) break;
                }

                _groups.CloseGroup();
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
        /// 中心が立てる所にない生成位置を、名前つきで警告する。その生成位置から出したグループのアンカーは床に乗れず、動かない為。
        /// </summary>
        private void WarnUnstandableSpawnPoints()
        {
            foreach (var point in _spawnPoints)
            {
                if (point == null || IsStandable(point.transform.position)) continue;

                UsefulLogger.LogWarning(
                    $"生成位置 {point.name} の中心に、敵が立てる所がありません。そこから出したグループは動けないので、床の上へ動かしてください。",
                    point);
            }
        }

        /// <summary>
        /// 初期生成の範囲と生成情報の編成に、体の設定のない種類があれば、名前つきで警告する。その種類のメンバーは出さない為。
        /// </summary>
        private void WarnUnconfiguredKinds()
        {
            foreach (var area in _initialSpawnAreas)
            {
                foreach (var kind in area.Composition)
                {
                    if (_bodyPrefabs[(int)kind] != null) continue;

                    UsefulLogger.LogWarning($"初期生成の範囲 {area.name} の編成にある {kind} は、体の設定がない為に置きません。", area);
                }
            }

            var spawnInfos = _spawnSystem.SpawnInfos;
            for (var i = 0; i < spawnInfos.Count; i++)
            {
                foreach (var kind in spawnInfos[i].Composition)
                {
                    if (_bodyPrefabs[(int)kind] != null) continue;

                    UsefulLogger.LogWarning($"生成情報 {i} 番の編成にある {kind} は、体の設定がない為に出しません。", _spawnSystem);
                }
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
        /// <param name="kind">出す敵の種類。体の設定のある種類に限る</param>
        /// <param name="position">出す位置</param>
        /// <param name="groupCenter">新しいグループを作るときの、アンカーの位置</param>
        private bool TrySpawn(EnemyKind kind, Vector3 position, Vector3 groupCenter)
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

        /// <summary>
        /// 別のグループに入っている出ている敵どうしで、水平の距離が OverlapDistanceSq の平方根より近い組を数える。
        /// 両方のグループが置き場に着いている組と、それ以外（移動中）の組に分ける。総当たりなので、デバッグ表示のときだけ回す。
        /// </summary>
        [BurstCompile]
        private struct OverlapCountJob : IJob
        {
            [ReadOnly] public NativeArray<EnemyAgent> Agents;
            [ReadOnly] public NativeArray<EnemyGroup> Groups;
            public float OverlapDistanceSq;

            /// <summary> 0 番に移動中の組、1 番に着いた組の数を書く </summary>
            public NativeArray<int> Counts;

            public void Execute()
            {
                var moving = 0;
                var arrived = 0;
                for (var i = 0; i < Agents.Length; i++)
                {
                    var a = Agents[i];
                    if (!a.IsAlive || a.GroupIndex < 0) continue;

                    for (var j = i + 1; j < Agents.Length; j++)
                    {
                        var b = Agents[j];
                        if (!b.IsAlive || b.GroupIndex < 0 || b.GroupIndex == a.GroupIndex) continue;
                        if (math.distancesq(a.Position.xz, b.Position.xz) >= OverlapDistanceSq) continue;

                        if (Groups[a.GroupIndex].HasArrived && Groups[b.GroupIndex].HasArrived) arrived++;
                        else moving++;
                    }
                }

                Counts[0] = moving;
                Counts[1] = arrived;
            }
        }
    }
}
