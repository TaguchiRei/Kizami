using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;
using UsefulToolkit.Utility;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の状態（EnemyAgent）の配列と体のプールを持ち、ステージシーンの EnemySpawnSystem の設定に従って敵を出す Adapter。インゲームのシーンへ置く。
    /// 敵の状態の数は EnemySpawnSystem の同時に存在する数の上限で、出ている敵は EnemyCrowdRenderer でまとめて描画する。
    /// 経路の格子は初期化のときに EnemySpawnSystem の範囲で作り、距離マップはプレイヤーのいるノードが変わるたびに計算し直す。
    /// 出ている敵は、毎フレーム EnemyMoveJob で距離マップを下って歩かせ、落とす。
    /// 切断できる体（EnemyBody）は、プレイヤーから貸す距離の中にいる敵へ近い順に貸し、返す距離より離れたら返す。返す距離は貸す距離より遠い。
    /// 近接切断の結果は、切られた部位を持つ体へ渡し、体の部位の状態を敵の状態へ書き戻す。体から外れた切っていない部位は、見た目用の物（EnemyDebris）で散らばらせて消す。
    /// 敵の状態、体、見た目用の物は初期化のときに作り、実行中は作らない。敵の状態に空きがなければ出さず、見た目用の物に空きがなければ最も古い物を使い回す。
    /// </summary>
    /// <remarks>
    /// 敵を出すのは MeshDataCache のストアができてから。体を貸すときに部位を登録し直すのにストアが要る為。
    /// 生成の間隔、敵の移動、見た目用の物の動きは Time.deltaTime で数え、スローモード中は一緒に遅くなる。
    /// </remarks>
    public sealed class EnemySpawnAdapter : InitializableMonoBehaviour
    {
        /// <summary> かかった時間を平均するフレームの数 </summary>
        private const int TIMING_SAMPLE_COUNT = 30;

        private const string UPDATE_MARKER_NAME = "Kizami.Enemy.Update";
        private const string MOVE_MARKER_NAME = "Kizami.Enemy.Move";
        private const string BODY_MARKER_NAME = "Kizami.Enemy.Body";
        private const string RENDER_MARKER_NAME = "Kizami.Enemy.Render";

        private static readonly ProfilerMarker _updateMarker = new(UPDATE_MARKER_NAME);
        private static readonly ProfilerMarker _moveMarker = new(MOVE_MARKER_NAME);
        private static readonly ProfilerMarker _bodyMarker = new(BODY_MARKER_NAME);
        private static readonly ProfilerMarker _renderMarker = new(RENDER_MARKER_NAME);

        private readonly List<EnemyBody> _bodies = new();
        private readonly List<EnemySpawnPoint> _spawnPoints = new();
        private readonly List<EnemyInitialSpawnArea> _initialSpawnAreas = new();

        /// <summary> 部位から、その部位を持つ体の _bodies での番号を引く表 </summary>
        private readonly Dictionary<CuttableObject, int> _partOwners = new();

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

        private ProfilerRecorder _updateRecorder;
        private ProfilerRecorder _moveRecorder;
        private ProfilerRecorder _bodyRecorder;
        private ProfilerRecorder _renderRecorder;

        /// <summary> 体ごとの、貸している敵の _agents での番号。貸していなければ -1。並びは _bodies と同じ </summary>
        private int[] _bodyAgents;

        /// <summary> 体を貸す候補の敵の、プレイヤーとの距離の 2 乗。並べ替えに使う作業用の配列 </summary>
        private float[] _lendCandidateDistances;

        /// <summary> 体を貸す候補の敵の _agents での番号。並びは _lendCandidateDistances と同じ </summary>
        private int[] _lendCandidates;

        /// <summary> 見た目用の部位。並びは _debrisBuffer の RecycleId と同じ </summary>
        private EnemyDebris[] _debris;

        private RecycleBuffer<EnemyDebris> _debrisBuffer;

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

        /// <summary> 敵の状態の数（同時に存在する数の上限） </summary>
        public int Capacity => _agents.IsCreated ? _agents.Length : 0;

        /// <summary> 敵に貸している体の数 </summary>
        public int LentBodyCount
        {
            get
            {
                var count = 0;
                foreach (var body in _bodies)
                {
                    if (body.IsLent) count++;
                }

                return count;
            }
        }

        /// <summary> 体の数 </summary>
        public int BodyCount => _bodies.Count;

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
        /// EnemyInitializer から呼ばれる。ステージシーンの EnemySpawnSystem を探し、上限の数だけ敵の状態と、体と、見た目用の部位を作る。
        /// 見つからないときは Update を止めたままにする。
        /// </summary>
        /// <param name="spawnOrb">倒れた体に残っていた切断済みの部位を、オーブにする関数。引数はオーブを出す位置</param>
        public void Initialize(Action<Vector3> spawnOrb)
        {
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
                _debris = new EnemyDebris[_debrisCapacity];
                for (var i = 0; i < _debris.Length; i++)
                {
                    _debris[i] = new EnemyDebris(transform, _debrisMaterial);
                }

                _debrisBuffer = new RecycleBuffer<EnemyDebris>(_debris);
            }

            _agents = new NativeArray<EnemyAgent>(_spawnSystem.MaxAliveCount, Allocator.Persistent);
            _crowdRenderer = new EnemyCrowdRenderer(_bodyPrefab, _agents.Length);
            _distanceField = new EnemyDistanceField(_spawnSystem.NavigationBounds, _cellSize, _enemyHeight, _climbHeight,
                _groundLayers);
            _updateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, UPDATE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _moveRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MOVE_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _bodyRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, BODY_MARKER_NAME, TIMING_SAMPLE_COUNT);
            _lendCandidateDistances = new float[_agents.Length];
            _lendCandidates = new int[_agents.Length];
            _bodyAgents = new int[_bodyCount];
            _renderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, RENDER_MARKER_NAME, TIMING_SAMPLE_COUNT);

            for (var i = 0; i < _bodyCount; i++)
            {
                var body = Instantiate(_bodyPrefab, transform);
                body.gameObject.SetActive(false);
                body.Initialize(spawnOrb, SpawnDebris);
                _bodies.Add(body);
                _bodyAgents[i] = -1;

                foreach (var part in body.Parts)
                {
                    if (part.Cuttable != null) _partOwners.Add(part.Cuttable, i);
                }
            }

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

            foreach (var result in results)
            {
                if (result.Original == null || !_partOwners.TryGetValue(result.Original, out var bodyIndex)) continue;

                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var body = _bodies[bodyIndex];
                body.ReceiveCut(result, plane, _fragmentPool);

                var agent = _agents[agentIndex];
                body.WriteState(ref agent);
                if (!agent.IsAlive)
                {
                    agent.BodyIndex = -1;
                    _bodyAgents[bodyIndex] = -1;
                }

                _agents[agentIndex] = agent;
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
                }

                if (_target != null)
                {
                    using (_bodyMarker.Auto())
                    {
                        ReturnBodies();
                        LendBodies(cache);
                        SyncBodyTransforms();
                    }
                }

                using (_renderMarker.Auto())
                {
                    _crowdRenderer.Render(_agents);
                }

                UpdateDebris();
            }
        }

        private void OnDestroy()
        {
            _crowdRenderer?.Dispose();
            _distanceField?.Dispose();
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

        private void MoveAgents()
        {
            new EnemyMoveJob
            {
                Agents = _agents,
                Grid = _distanceField.Grid,
                Distances = _distanceField.Distances,
                DeltaTime = Time.deltaTime,
                MoveSpeed = _moveSpeed,
                TurnSpeed = math.radians(_turnSpeed),
                StopDistance = _stopDistance,
                Gravity = -Physics.gravity.y
            }.Schedule(_agents.Length, 64).Complete();
        }

        /// <summary>
        /// 返す距離より離れた敵と、落ちてステージから消えた敵から、体を返す。部位の状態は敵の状態へ書き戻してから返す。
        /// </summary>
        private void ReturnBodies()
        {
            var target = (float3)_target.position;
            var returnDistanceSq = _returnDistance * _returnDistance;

            for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
            {
                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var agent = _agents[agentIndex];
                if (agent.IsAlive && math.distancesq(agent.Position, target) <= returnDistanceSq) continue;

                var body = _bodies[bodyIndex];
                if (agent.IsAlive) body.WriteState(ref agent);
                body.Return();

                agent.BodyIndex = -1;
                _agents[agentIndex] = agent;
                _bodyAgents[bodyIndex] = -1;
            }
        }

        /// <summary>
        /// 貸す距離の中にいる、体を貸していない敵へ、近い順に空いている体を貸す。
        /// </summary>
        private void LendBodies(MeshDataCache cache)
        {
            var freeCount = 0;
            foreach (var agentIndex in _bodyAgents)
            {
                if (agentIndex < 0) freeCount++;
            }

            if (freeCount == 0) return;

            var target = (float3)_target.position;
            var lendDistanceSq = _lendDistance * _lendDistance;
            var candidateCount = 0;

            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                if (!agent.IsAlive || agent.BodyIndex >= 0) continue;

                var distanceSq = math.distancesq(agent.Position, target);
                if (distanceSq > lendDistanceSq) continue;

                _lendCandidateDistances[candidateCount] = distanceSq;
                _lendCandidates[candidateCount] = i;
                candidateCount++;
            }

            if (candidateCount == 0) return;

            Array.Sort(_lendCandidateDistances, _lendCandidates, 0, candidateCount);

            var nextBody = 0;
            for (var c = 0; c < candidateCount; c++)
            {
                while (nextBody < _bodyAgents.Length && _bodyAgents[nextBody] >= 0) nextBody++;
                if (nextBody == _bodyAgents.Length) return;

                var agentIndex = _lendCandidates[c];
                var agent = _agents[agentIndex];
                _bodies[nextBody].Lend(agent, cache);

                agent.BodyIndex = nextBody;
                _agents[agentIndex] = agent;
                _bodyAgents[nextBody] = agentIndex;
            }
        }

        /// <summary>
        /// 貸している体の位置と向きを、敵の状態に合わせる。
        /// </summary>
        private void SyncBodyTransforms()
        {
            for (var bodyIndex = 0; bodyIndex < _bodies.Count; bodyIndex++)
            {
                var agentIndex = _bodyAgents[bodyIndex];
                if (agentIndex < 0) continue;

                var agent = _agents[agentIndex];
                _bodies[bodyIndex].transform.SetPositionAndRotation(agent.Position,
                    Quaternion.Euler(0f, math.degrees(agent.Yaw), 0f));
            }
        }

        /// <summary>
        /// 初期生成情報の範囲に、決まった数の敵を置く。
        /// </summary>
        private void SpawnInitial()
        {
            foreach (var area in _initialSpawnAreas)
            {
                for (var i = 0; i < area.Count; i++)
                {
                    if (!TrySpawn(area.GetSpawnPosition())) return;
                }
            }
        }

        /// <summary>
        /// 生成情報ごとに間隔を数え、間隔が来たら次の有効な生成位置から、一度に出す数の上限まで出す。
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

                for (var n = 0; n < info.MaxCountPerSpawn; n++)
                {
                    if (!TrySpawn(point.GetSpawnPosition())) break;
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
        /// 空いている敵の状態を使い、目標の方を向けて出す。空きがなければ出さない。
        /// </summary>
        private bool TrySpawn(Vector3 position)
        {
            for (var i = 0; i < _agents.Length; i++)
            {
                if (_agents[i].IsAlive) continue;

                _agents[i] = new EnemyAgent
                {
                    IsAlive = true,
                    Position = position,
                    Yaw = GetYawToTarget(position),
                    BodyIndex = -1
                };
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
        /// 部位と同じ形の見た目用の物を出し、体の中心から外向きと上向きに飛ばす。
        /// </summary>
        /// <param name="part">体から外れた、切っていない部位</param>
        /// <param name="origin">散らばる中心（体の位置）</param>
        private void SpawnDebris(CuttableObject part, Vector3 origin)
        {
            if (_debrisBuffer == null || part == null) return;

            var outward = part.transform.position - origin;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0f)
            {
                outward.Normalize();
            }
            else
            {
                var circle = Random.insideUnitCircle.normalized;
                outward = new Vector3(circle.x, 0f, circle.y);
            }

            var velocity = outward * _debrisOutwardSpeed + Vector3.up * _debrisUpwardSpeed;
            _debrisBuffer.Get().Show(part, velocity, Random.onUnitSphere * _debrisAngularSpeed);
        }

        /// <summary>
        /// 出ている見た目用の部位を動かし、消え終わったものをバッファへ返す。
        /// </summary>
        private void UpdateDebris()
        {
            if (_debris == null) return;

            var deltaTime = Time.deltaTime;

            foreach (var debris in _debris)
            {
                if (!debris.IsActive || debris.Tick(deltaTime, _debrisLifetime)) continue;

                debris.OnRecycle();
                _debrisBuffer.Release(debris);
            }
        }
    }
}
