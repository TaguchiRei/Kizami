using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;
using UsefulToolkit.BlackBoard.Logger;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// アタッカーが体の上のタレットから撃つ弾を扱う。EnemySpawnAdapter と同じ GameObject に置き、EnemySpawnAdapter が初期化と毎フレームの更新を呼ぶ。
    /// 撃つのは、射程の中にいて、攻撃部位が壊れておらず、銃口からプレイヤーまで壁のないアタッカー。体を貸していない敵も、敵の状態の値だけで撃つ。
    /// 弾の位置・速度・残りの寿命は NativeArray に持ち、Burst の Job で進めてプレイヤーのカプセルとの当たりを距離で調べ、前のフレームの位置からの間を SpherecastCommand で調べて壁と装甲で止める。
    /// 飛んでいる弾の数に上限を設け、1 体が 1 発ずつ撃つので、同時に撃つ敵の数の上限にもなる。弾の位置は GraphicsBuffer で VFX Graph へ渡して描く。
    /// </summary>
    /// <remarks>
    /// VFX Graph で受け取るものは次のとおり（名前を変えるときは、グラフの側も合わせる）。
    /// GraphicsBuffer の BulletStates（float4 の並び。xyz が位置、w が飛んでいれば 1・いなければ 0）、int の BulletCapacity（その数）。
    /// </remarks>
    public sealed class EnemyShooter : MonoBehaviour
    {
        /// <summary> 銃口からプレイヤーまで壁にさえぎられた敵が、撃てるかを調べ直すまでの時間（秒） </summary>
        private const float RETRY_DELAY = 0.5f;

        /// <summary> 弾の寿命を、射程を飛ぶ時間のこの倍にする </summary>
        private const float LIFETIME_RATE = 1.2f;

        private static readonly int _statesId = Shader.PropertyToID("BulletStates");
        private static readonly int _capacityId = Shader.PropertyToID("BulletCapacity");

        [SerializeField]
        [Tooltip("弾を描く VisualEffect。未設定なら、弾は見えないまま飛ぶ")]
        private VisualEffect _effect;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーとの距離がこの値（m）以内のアタッカーが撃つ")]
        private float _range = 50f;

        [SerializeField, Min(0.1f)]
        [Tooltip("弾の速さ（m/s）")]
        private float _speed = 20f;

        [SerializeField, Min(0)]
        [Tooltip("弾がプレイヤーに当たったときのダメージ")]
        private int _damage = 5;

        [SerializeField, Min(0.1f)]
        [Tooltip("1 体が撃つ間隔（秒）")]
        private float _interval = 3f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("撃つ間隔を、敵ごと・1 発ごとにずらす割合の幅（±）")]
        private float _intervalJitter = 0.3f;

        [SerializeField, Min(0.01f)]
        [Tooltip("弾の半径（m）")]
        private float _radius = 0.3f;

        [SerializeField, Min(1)]
        [Tooltip("同時に飛んでいる弾の数の上限。1 体が 1 発ずつ撃つので、同時に撃つ敵の数の上限でもある")]
        private int _maxBullets = 4;

        [SerializeField]
        [Tooltip("弾を止め、銃口からプレイヤーまでの間をさえぎる物のレイヤー（壁と装甲）")]
        private LayerMask _blockLayers = 1;

        /// <summary> 弾がプレイヤーに当たったときにダメージを与える関数。引数はダメージ量 </summary>
        private Action<int> _applyDamage;

        private Transform _target;
        private CapsuleCollider _targetCapsule;

        /// <summary> 攻撃部位のビット。ビット i が体の部位 i を表す </summary>
        private uint _attackPartMask;

        /// <summary> 体の根から見た銃口の位置 </summary>
        private float3 _muzzleOffset;

        private NativeArray<bool> _isFlying;
        private NativeArray<float3> _positions;
        private NativeArray<float3> _velocities;
        private NativeArray<float> _lifetimes;

        /// <summary> 弾ごとの、このフレームに進んだ間でプレイヤーに当たった所までの距離（m）。当たらなければ無限大 </summary>
        private NativeArray<float> _playerHitDistances;

        private NativeArray<SpherecastCommand> _castCommands;
        private NativeArray<RaycastHit> _castHits;

        /// <summary> 撃てる状態になったアタッカーの、敵の状態の番号 </summary>
        private NativeList<int> _candidates;

        private NativeArray<RaycastCommand> _sightCommands;
        private NativeArray<RaycastHit> _sightHits;

        private GraphicsBuffer _stateBuffer;

        /// <summary> VFX Graph へ渡す弾の状態。並びは弾の番号と同じ </summary>
        private Vector4[] _states;

        private bool _isReady;

        /// <summary> 飛んでいる弾の数 </summary>
        public int FlyingCount
        {
            get
            {
                if (!_isReady) return 0;

                var count = 0;
                foreach (var flying in _isFlying)
                {
                    if (flying) count++;
                }

                return count;
            }
        }

        /// <summary>
        /// 線分 p1-q1 と線分 p2-q2 の最も近い点どうしの距離の 2 乗と、線分 p1-q1 の上の最も近い点の割合（0〜1）を求める。
        /// </summary>
        private static float GetSegmentDistanceSq(float3 p1, float3 q1, float3 p2, float3 q2, out float s)
        {
            const float EPSILON = 1e-8f;
            var d1 = q1 - p1;
            var d2 = q2 - p2;
            var r = p1 - p2;
            var a = math.dot(d1, d1);
            var e = math.dot(d2, d2);
            var f = math.dot(d2, r);
            float t;

            if (a <= EPSILON && e <= EPSILON)
            {
                s = 0f;
                t = 0f;
            }
            else if (a <= EPSILON)
            {
                s = 0f;
                t = math.saturate(f / e);
            }
            else
            {
                var c = math.dot(d1, r);
                if (e <= EPSILON)
                {
                    t = 0f;
                    s = math.saturate(-c / a);
                }
                else
                {
                    var b = math.dot(d1, d2);
                    var denominator = a * e - b * b;
                    s = denominator > EPSILON ? math.saturate((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = math.saturate(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = math.saturate((b - c) / a);
                    }
                }
            }

            return math.distancesq(p1 + d1 * s, p2 + d2 * t);
        }

        /// <summary>
        /// アタッカーの体のプレハブから攻撃部位と銃口の位置を読み、弾の配列を作る。
        /// プレハブかプレイヤーがなければ、警告を出して撃たないままにする。
        /// </summary>
        /// <param name="attackerPrefab">アタッカーの体のプレハブ</param>
        /// <param name="target">狙うプレイヤー。子に CapsuleCollider を持つ</param>
        /// <param name="applyDamage">弾がプレイヤーに当たったときにダメージを与える関数。引数はダメージ量</param>
        public void Initialize(EnemyBody attackerPrefab, Transform target, Action<int> applyDamage)
        {
            _applyDamage = applyDamage;
            _target = target;
            _targetCapsule = target != null ? target.GetComponentInChildren<CapsuleCollider>() : null;
            if (attackerPrefab == null || _targetCapsule == null)
            {
                UsefulLogger.LogWarning("アタッカーの体のプレハブか、プレイヤーの CapsuleCollider がない為、弾を撃ちません。", this);
                return;
            }

            if (!TryReadAttackParts(attackerPrefab))
            {
                UsefulLogger.LogWarning("アタッカーの体に攻撃部位（AttackPartRole）がない為、弾を撃ちません。", this);
                return;
            }

            _isFlying = new NativeArray<bool>(_maxBullets, Allocator.Persistent);
            _positions = new NativeArray<float3>(_maxBullets, Allocator.Persistent);
            _velocities = new NativeArray<float3>(_maxBullets, Allocator.Persistent);
            _lifetimes = new NativeArray<float>(_maxBullets, Allocator.Persistent);
            _playerHitDistances = new NativeArray<float>(_maxBullets, Allocator.Persistent);
            _castCommands = new NativeArray<SpherecastCommand>(_maxBullets, Allocator.Persistent);
            _castHits = new NativeArray<RaycastHit>(_maxBullets, Allocator.Persistent);
            _sightCommands = new NativeArray<RaycastCommand>(_maxBullets, Allocator.Persistent);
            _sightHits = new NativeArray<RaycastHit>(_maxBullets, Allocator.Persistent);
            _candidates = new NativeList<int>(Allocator.Persistent);
            _states = new Vector4[_maxBullets];

            if (_effect == null)
            {
                UsefulLogger.LogWarning("弾の VisualEffect が設定されていない為、弾は見えないまま飛びます。", this);
            }
            else if (!_effect.HasGraphicsBuffer(_statesId) || !_effect.HasInt(_capacityId))
            {
                UsefulLogger.LogWarning("弾の VFX Graph に BulletStates か BulletCapacity がない為、弾は見えないまま飛びます。", this);
            }
            else
            {
                _stateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxBullets, sizeof(float) * 4);
                _stateBuffer.SetData(_states);
                _effect.SetGraphicsBuffer(_statesId, _stateBuffer);
                _effect.SetInt(_capacityId, _maxBullets);
            }

            _isReady = true;
        }

        /// <summary>
        /// 飛んでいる弾を進めて当たりを調べ、撃てる状態のアタッカーに撃たせ、弾の状態を VFX Graph へ渡す。EnemySpawnAdapter が毎フレーム、敵を動かしたあとに呼ぶ。
        /// </summary>
        /// <param name="agents">敵の状態。撃ったアタッカーの、次に撃てるまでの時間を書く</param>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Tick(NativeArray<EnemyAgent> agents, float deltaTime)
        {
            if (!_isReady || deltaTime <= 0f) return;

            GetTargetCapsule(out var capsuleStart, out var capsuleEnd, out var capsuleRadius);
            MoveBullets(deltaTime, capsuleStart, capsuleEnd, capsuleRadius);
            Fire(agents, deltaTime, (capsuleStart + capsuleEnd) * 0.5f);
            UpdateEffect();
        }

        private void OnDestroy()
        {
            if (_isFlying.IsCreated) _isFlying.Dispose();
            if (_positions.IsCreated) _positions.Dispose();
            if (_velocities.IsCreated) _velocities.Dispose();
            if (_lifetimes.IsCreated) _lifetimes.Dispose();
            if (_playerHitDistances.IsCreated) _playerHitDistances.Dispose();
            if (_castCommands.IsCreated) _castCommands.Dispose();
            if (_castHits.IsCreated) _castHits.Dispose();
            if (_sightCommands.IsCreated) _sightCommands.Dispose();
            if (_sightHits.IsCreated) _sightHits.Dispose();
            if (_candidates.IsCreated) _candidates.Dispose();
            _stateBuffer?.Release();
        }

        private void OnDrawGizmos()
        {
            if (!_isReady) return;

            Gizmos.color = Color.yellow;
            for (var i = 0; i < _maxBullets; i++)
            {
                if (_isFlying[i]) Gizmos.DrawWireSphere(_positions[i], _radius);
            }
        }

        /// <summary>
        /// 体のプレハブの部位から、攻撃部位のビットと、攻撃部位をまとめた範囲の前の端（体の根の空間の +Z の端）を銃口の位置として読む。攻撃部位がなければ false。
        /// </summary>
        private bool TryReadAttackParts(EnemyBody prefab)
        {
            var rootWorldToLocal = prefab.transform.worldToLocalMatrix;
            var hasBounds = false;
            var bounds = new Bounds();
            var parts = prefab.Parts;

            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].Role is not AttackPartRole || parts[i].Cuttable == null) continue;

                _attackPartMask |= 1u << i;
                var meshFilter = parts[i].Cuttable.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;

                var toRoot = rootWorldToLocal * parts[i].Cuttable.transform.localToWorldMatrix;
                var meshBounds = meshFilter.sharedMesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    var point = toRoot.MultiplyPoint3x4(local);
                    if (hasBounds)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }

            _muzzleOffset = new float3(bounds.center.x, bounds.center.y, bounds.max.z);
            return _attackPartMask != 0;
        }

        /// <summary>
        /// プレイヤーの CapsuleCollider の、軸の両端（ワールド座標）と半径を求める。カプセルは縦向き（Y 軸）とする。
        /// </summary>
        private void GetTargetCapsule(out float3 start, out float3 end, out float radius)
        {
            var capsuleTransform = _targetCapsule.transform;
            var scale = capsuleTransform.lossyScale;
            radius = _targetCapsule.radius * math.max(math.abs(scale.x), math.abs(scale.z));
            var halfAxis = math.max(_targetCapsule.height * 0.5f * math.abs(scale.y) - radius, 0f);
            var center = (float3)capsuleTransform.TransformPoint(_targetCapsule.center);
            var up = (float3)capsuleTransform.up;
            start = center - up * halfAxis;
            end = center + up * halfAxis;
        }

        /// <summary>
        /// 飛んでいる弾を Job で進め、SpherecastCommand で壁と装甲に当たったかを調べる。
        /// 壁より手前でプレイヤーに当たった弾はダメージを与えて消し、壁に当たった弾と寿命が尽きた弾も消す。
        /// </summary>
        private void MoveBullets(float deltaTime, float3 capsuleStart, float3 capsuleEnd, float capsuleRadius)
        {
            var handle = new MoveBulletsJob
            {
                IsFlying = _isFlying,
                Positions = _positions,
                Velocities = _velocities,
                Lifetimes = _lifetimes,
                PlayerHitDistances = _playerHitDistances,
                Commands = _castCommands,
                DeltaTime = deltaTime,
                Radius = _radius,
                CapsuleStart = capsuleStart,
                CapsuleEnd = capsuleEnd,
                CapsuleRadius = capsuleRadius,
                Query = new QueryParameters(_blockLayers, false, QueryTriggerInteraction.Ignore)
            }.Schedule(_maxBullets, 1);
            SpherecastCommand.ScheduleBatch(_castCommands, _castHits, 1, 1, handle).Complete();

            for (var i = 0; i < _maxBullets; i++)
            {
                if (!_isFlying[i]) continue;

                var wallDistance = _castHits[i].collider != null ? _castHits[i].distance : float.PositiveInfinity;
                var playerDistance = _playerHitDistances[i];
                if (!float.IsPositiveInfinity(playerDistance) && playerDistance <= wallDistance)
                {
                    _applyDamage?.Invoke(_damage);
                    _isFlying[i] = false;
                }
                else if (!float.IsPositiveInfinity(wallDistance) || _lifetimes[i] <= 0f)
                {
                    _isFlying[i] = false;
                }
            }
        }

        /// <summary>
        /// 撃てる状態のアタッカーを Job で選び、空いている弾の数まで、銃口からプレイヤーまで壁がないかを RaycastCommand で調べて撃たせる。
        /// 選ぶ順は毎フレームずらし、番号の小さい敵ばかりが撃たないようにする。
        /// </summary>
        private void Fire(NativeArray<EnemyAgent> agents, float deltaTime, float3 aimPoint)
        {
            new SelectShootersJob
            {
                Agents = agents,
                Candidates = _candidates,
                DeltaTime = deltaTime,
                Target = (float3)_target.position,
                RangeSq = _range * _range,
                AttackPartMask = _attackPartMask
            }.Schedule().Complete();

            var freeCount = _maxBullets - FlyingCount;
            var shooterCount = math.min(freeCount, _candidates.Length);
            if (shooterCount <= 0) return;

            var query = new QueryParameters(_blockLayers, false, QueryTriggerInteraction.Ignore);
            var start = Random.Range(0, _candidates.Length);
            for (var n = 0; n < shooterCount; n++)
            {
                var agent = agents[_candidates[(start + n) % _candidates.Length]];
                var muzzle = GetMuzzlePosition(agent);
                var toAim = aimPoint - muzzle;
                _sightCommands[n] = new RaycastCommand(muzzle, math.normalizesafe(toAim), query, math.length(toAim));
            }

            RaycastCommand.ScheduleBatch(_sightCommands.GetSubArray(0, shooterCount),
                _sightHits.GetSubArray(0, shooterCount), 1, 1).Complete();

            for (var n = 0; n < shooterCount; n++)
            {
                var index = _candidates[(start + n) % _candidates.Length];
                var agent = agents[index];
                if (_sightHits[n].collider != null)
                {
                    agent.AttackCooldown = RETRY_DELAY;
                }
                else
                {
                    var muzzle = GetMuzzlePosition(agent);
                    Spawn(muzzle, math.normalizesafe(aimPoint - muzzle));
                    agent.AttackCooldown = _interval * (1f + Random.Range(-_intervalJitter, _intervalJitter));
                }

                agents[index] = agent;
            }
        }

        private float3 GetMuzzlePosition(in EnemyAgent agent)
        {
            return agent.Position + math.mul(quaternion.RotateY(agent.Yaw), _muzzleOffset);
        }

        /// <summary>
        /// 空いている弾を、銃口から向き direction へ飛ばす。
        /// </summary>
        private void Spawn(float3 muzzle, float3 direction)
        {
            for (var i = 0; i < _maxBullets; i++)
            {
                if (_isFlying[i]) continue;

                _isFlying[i] = true;
                _positions[i] = muzzle;
                _velocities[i] = direction * _speed;
                _lifetimes[i] = _range / _speed * LIFETIME_RATE;
                return;
            }
        }

        private void UpdateEffect()
        {
            if (_stateBuffer == null) return;

            for (var i = 0; i < _maxBullets; i++)
            {
                var position = _positions[i];
                _states[i] = new Vector4(position.x, position.y, position.z, _isFlying[i] ? 1f : 0f);
            }

            _stateBuffer.SetData(_states);
        }

        /// <summary>
        /// 生きていて攻撃部位が壊れていないアタッカーの、次に撃てるまでの時間を減らし、撃てる状態で射程の中にいる敵を候補に集める。
        /// </summary>
        [BurstCompile]
        private struct SelectShootersJob : IJob
        {
            public NativeArray<EnemyAgent> Agents;
            public NativeList<int> Candidates;
            public float DeltaTime;
            public float3 Target;
            public float RangeSq;
            public uint AttackPartMask;

            public void Execute()
            {
                Candidates.Clear();
                for (var i = 0; i < Agents.Length; i++)
                {
                    var agent = Agents[i];
                    if (!agent.IsAlive || agent.Kind != EnemyKind.Attacker || (agent.BrokenParts & AttackPartMask) != 0) continue;

                    if (agent.AttackCooldown > 0f)
                    {
                        agent.AttackCooldown -= DeltaTime;
                        Agents[i] = agent;
                        continue;
                    }

                    if (math.distancesq(agent.Position, Target) <= RangeSq) Candidates.Add(i);
                }
            }
        }

        /// <summary>
        /// 飛んでいる弾を 1 つずつ進め、進んだ間の SpherecastCommand を作り、プレイヤーのカプセルに当たった所までの距離を求める。
        /// 飛んでいない弾は、調べる距離 0 の SpherecastCommand にする。
        /// </summary>
        [BurstCompile]
        private struct MoveBulletsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<bool> IsFlying;
            public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<float3> Velocities;
            public NativeArray<float> Lifetimes;
            public NativeArray<float> PlayerHitDistances;
            public NativeArray<SpherecastCommand> Commands;
            public float DeltaTime;
            public float Radius;
            public float3 CapsuleStart;
            public float3 CapsuleEnd;
            public float CapsuleRadius;
            public QueryParameters Query;

            public void Execute(int index)
            {
                var start = Positions[index];
                PlayerHitDistances[index] = float.PositiveInfinity;
                if (!IsFlying[index])
                {
                    Commands[index] = new SpherecastCommand(start, Radius, new float3(0f, 1f, 0f), Query, 0f);
                    return;
                }

                var step = Velocities[index] * DeltaTime;
                var end = start + step;
                var length = math.length(step);
                Positions[index] = end;
                Lifetimes[index] -= DeltaTime;
                Commands[index] = new SpherecastCommand(start, Radius,
                    length > 0f ? step / length : new float3(0f, 1f, 0f), Query, length);

                var hitRadius = Radius + CapsuleRadius;
                if (GetSegmentDistanceSq(start, end, CapsuleStart, CapsuleEnd, out var s) <= hitRadius * hitRadius)
                {
                    PlayerHitDistances[index] = s * length;
                }
            }
        }
    }
}
