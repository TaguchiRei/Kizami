using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;
using UsefulToolkit.Utility;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の体のプールを持ち、ステージシーンの EnemySpawnSystem の設定に従って敵を出し、仮の移動をさせる Adapter。インゲームのシーンへ置く。
    /// 近接切断の結果は、切られた部位を持つ体へ渡す。体から外れた切っていない部位は、見た目用の物（EnemyDebris）で散らばらせて消す。
    /// 体と見た目用の物は初期化のときに作り、実行中は作らない。体に空きがなければ出さず、見た目用の物に空きがなければ最も古い物を使い回す。
    /// </summary>
    /// <remarks>
    /// 敵を出すのは MeshDataCache のストアができてから。部位を登録し直すのにストアが要る為。
    /// 生成の間隔、仮の移動、見た目用の物の動きは Time.deltaTime で数え、スローモード中は一緒に遅くなる。
    /// </remarks>
    public sealed class EnemySpawnAdapter : InitializableMonoBehaviour
    {
        private readonly List<EnemyBody> _bodies = new();
        private readonly List<EnemySpawnPoint> _spawnPoints = new();
        private readonly List<EnemyInitialSpawnArea> _initialSpawnAreas = new();

        /// <summary> 部位から、その部位を持つ体を引く表 </summary>
        private readonly Dictionary<CuttableObject, EnemyBody> _partOwners = new();

        [SerializeField]
        [Tooltip("敵の体のプレハブ")]
        private EnemyBody _bodyPrefab;

        [SerializeField]
        [Tooltip("かけらのプール。体に残す側のかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("仮の移動で近づく先（プレイヤー）")]
        private Transform _target;

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
        /// EnemyInitializer から呼ばれる。ステージシーンの EnemySpawnSystem を探し、上限の数だけ体と、見た目用の部位を作る。
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

            for (var i = 0; i < _spawnSystem.MaxAliveCount; i++)
            {
                var body = Instantiate(_bodyPrefab, transform);
                body.gameObject.SetActive(false);
                body.Initialize(spawnOrb, SpawnDebris);
                _bodies.Add(body);

                foreach (var part in body.Parts)
                {
                    if (part.Cuttable != null) _partOwners.Add(part.Cuttable, body);
                }
            }

            base.Initialize();
        }

        /// <summary>
        /// 切断の結果のうち、敵の部位を元の対象とするものを、その部位を持つ体へ渡す。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        /// <param name="plane">振ったときの切断面。法線は表のかけらの側を向く</param>
        public void ReceiveCutResults(MultiCutResult[] results, Plane plane)
        {
            if (!Initialized || results == null) return;

            foreach (var result in results)
            {
                if (result.Original == null || !_partOwners.TryGetValue(result.Original, out var body)) continue;

                body.ReceiveCut(result, plane, _fragmentPool);
            }
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
            UpdateDebris();
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
