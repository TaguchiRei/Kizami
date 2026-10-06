using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の体のプールを持ち、ステージシーンの EnemySpawnSystem の設定に従って敵を出し、仮の移動をさせる Adapter。インゲームのシーンへ置く。
    /// 体は初期化のときに同時に存在する数の上限だけ作り、実行中は Instantiate しない。空きがなければ出さない。
    /// </summary>
    /// <remarks>
    /// 敵を出すのは MeshDataCache のストアができてから。部位を登録し直すのにストアが要る為。
    /// 生成の間隔と仮の移動は Time.deltaTime で数え、スローモード中は一緒に遅くなる。
    /// </remarks>
    public sealed class EnemySpawnAdapter : InitializableMonoBehaviour
    {
        private readonly List<EnemyBody> _bodies = new();
        private readonly List<EnemySpawnPoint> _spawnPoints = new();
        private readonly List<EnemyInitialSpawnArea> _initialSpawnAreas = new();

        [SerializeField]
        [Tooltip("敵の体のプレハブ")]
        private EnemyBody _bodyPrefab;

        [SerializeField]
        [Tooltip("仮の移動で近づく先（プレイヤー）")]
        private Transform _target;

        private EnemySpawnSystem _spawnSystem;

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
                var count = 0;
                foreach (var body in _bodies)
                {
                    if (body.IsSpawned) count++;
                }

                return count;
            }
        }

        /// <summary> プールにある体の数（同時に存在する数の上限） </summary>
        public int Capacity => _bodies.Count;

        /// <summary> ステージシーンの実行中の生成位置 </summary>
        public IReadOnlyList<EnemySpawnPoint> SpawnPoints => _spawnPoints;

        /// <summary>
        /// EnemyInitializer から呼ばれる。ステージシーンの EnemySpawnSystem を探し、上限の数だけ体を作る。
        /// 見つからないときは Update を止めたままにする。
        /// </summary>
        public override void Initialize()
        {
            if (_bodyPrefab == null)
            {
                UsefulLogger.LogError("敵の体のプレハブが設定されていません。", this);
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

            for (var i = 0; i < _spawnSystem.MaxAliveCount; i++)
            {
                var body = Instantiate(_bodyPrefab, transform);
                body.gameObject.SetActive(false);
                _bodies.Add(body);
            }

            base.Initialize();
        }

        private void Update()
        {
            var cache = MeshDataCache.Instance;
            if (cache == null || cache.Store == null) return;

            if (!_hasSpawnedInitial)
            {
                SpawnInitial(cache);
                _hasSpawnedInitial = true;
            }

            SpawnByInterval(cache);
            MoveBodies();
        }

        /// <summary>
        /// 初期生成情報の範囲に、決まった数の敵を置く。
        /// </summary>
        private void SpawnInitial(MeshDataCache cache)
        {
            foreach (var area in _initialSpawnAreas)
            {
                for (var i = 0; i < area.Count; i++)
                {
                    if (!TrySpawn(area.GetSpawnPosition(), cache)) return;
                }
            }
        }

        /// <summary>
        /// 生成情報ごとに間隔を数え、間隔が来たら次の有効な生成位置から、一度に出す数の上限まで出す。
        /// </summary>
        private void SpawnByInterval(MeshDataCache cache)
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
                    if (!TrySpawn(point.GetSpawnPosition(), cache)) break;
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
        /// プールの空いている体を、目標の方を向けて出す。空きがなければ出さない。
        /// </summary>
        private bool TrySpawn(Vector3 position, MeshDataCache cache)
        {
            foreach (var body in _bodies)
            {
                if (body.IsSpawned) continue;

                body.Spawn(position, GetRotationToTarget(position), cache);
                return true;
            }

            return false;
        }

        private Quaternion GetRotationToTarget(Vector3 position)
        {
            if (_target == null) return Quaternion.identity;

            var direction = _target.position - position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        }

        private void MoveBodies()
        {
            if (_target == null) return;

            var target = _target.position;
            var deltaTime = Time.deltaTime;

            foreach (var body in _bodies)
            {
                if (body.IsSpawned) body.MoveToward(target, deltaTime);
            }
        }
    }
}
