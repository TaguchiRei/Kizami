using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UsefulToolkit.Utility;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 割れたガラスの破片を、事前に作っておいて使い回すプール。ステージに1つ置く。
    /// 板1枚分の破片をまとめて1単位（GlassShardGroup）とし、全部使い中のときは最も古いものから再利用する。
    /// 破片は Rigidbody もコライダーも持たず、全グループの破片を 1 本の TransformAccessArray にして、Burst の Job でまとめて動かす。
    /// Job は Update でグループの寿命を進めたあとに投げ、LateUpdate で完了を待つ。破片の状態を書き換える Spawn は、先に Job の完了を待つ。
    /// 破片が止まる地面の高さは、板ごとに真下へレイを1本撃って求める。当たらなければ固定の高さを使う。
    /// 破片のプレハブは1つのプールにつき1種類で、プールは窓のガラス・バリア・装甲の種類ごとに置く（ShardPoolKind）。
    /// </summary>
    public sealed class GlassShardPool : MonoBehaviour
    {
        /// <summary> 種類ごとの、ステージにあるプール。置かれていなければ null </summary>
        private static readonly GlassShardPool[] _instances = new GlassShardPool[Enum.GetValues(typeof(ShardPoolKind)).Length];

        private GlassShardGroup[] _groups = Array.Empty<GlassShardGroup>();
        private RecycleBuffer<GlassShardGroup> _buffer;
        private GlassShardBuffers _buffers;
        private TransformAccessArray _transforms;
        private JobHandle _stepHandle;

        [SerializeField]
        [Tooltip("このプールが扱う破片の種類")]
        private ShardPoolKind _kind = ShardPoolKind.Glass;

        [SerializeField]
        [Tooltip("破片をまとめたプレハブ。子の1つ1つが破片で、板と同じ原点で作る。コライダーは付けない")]
        private GameObject _shardsPrefab;

        [SerializeField, Min(1)]
        [Tooltip("事前に作る破片のまとまりの数。板1枚で1つ使う")]
        private int _capacity = 400;

        [SerializeField]
        [Tooltip("破片が止まる地面のレイヤー。建物のレイヤーを含めない")]
        private LayerMask _groundLayers;

        [SerializeField, Min(0f)]
        [Tooltip("板の真下へ地面を探すレイの長さ（m）")]
        private float _groundRayLength = 300f;

        [SerializeField]
        [Tooltip("レイが地面に当たらなかったときに破片が止まる高さ（ワールド空間）")]
        private float _fallbackGroundY;

        [SerializeField, Min(0f)]
        [Tooltip("地面の高さからこの分だけ上で破片を止める（m）")]
        private float _landedHeight = 0.03f;

        [SerializeField, Min(0f)]
        [Tooltip("寿命の終わりのこの秒数をかけて、破片を縮めて消す。0 なら縮めずに消す")]
        private float _shrinkDuration;

        /// <summary> 窓のガラスのプール。置かれていなければ null </summary>
        public static GlassShardPool Instance => _instances[(int)ShardPoolKind.Glass];

        /// <summary> 事前の生成が終わり、破片を出せるか </summary>
        public bool IsGenerated { get; private set; }

        /// <summary>
        /// 種類 kind のプールを返す。置かれていなければ null。
        /// </summary>
        /// <param name="kind">破片の種類</param>
        public static GlassShardPool Get(ShardPoolKind kind)
        {
            return _instances[(int)kind];
        }

        /// <summary>
        /// 板 pane の破片を出して飛ばす。生成が終わる前や、破片のプレハブが未設定なら出さずに false を返す。
        /// </summary>
        /// <param name="pane">割れた板</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        /// <param name="speed">飛ばす速さ（m/s）</param>
        /// <param name="speedJitter">破片ごとの速さのばらつきの割合</param>
        /// <param name="maxAngularSpeed">回す角速度の最大（rad/s）</param>
        /// <param name="lifetime">飛ばし始めてから消すまでの秒数</param>
        public bool Spawn(Transform pane, Vector3 origin, float speed, float speedJitter, float maxAngularSpeed,
            float lifetime)
        {
            if (!IsGenerated) return false;

            var groundY = Physics.Raycast(pane.position, Vector3.down, out var hit, _groundRayLength, _groundLayers,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : _fallbackGroundY;

            _stepHandle.Complete();
            _buffer.Get().Spawn(pane, origin, groundY, speed, speedJitter, maxAngularSpeed, lifetime);
            return true;
        }

        private void Reset()
        {
            _groundLayers = 1 << LayerMask.NameToLayer("Ground");
        }

        private void Awake()
        {
            _instances[(int)_kind] = this;

            // 破片をワールド座標のまま動かす為、根は原点に等倍で置く
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// 破片のまとまりを事前に作る。大量の生成が1フレームに集中しないよう、非同期で作る。
        /// </summary>
        private async void Start()
        {
            if (_shardsPrefab == null) return;

            // 複製を非アクティブのまま作る為、非アクティブにした元を複製する
            var template = Instantiate(_shardsPrefab, transform);
            template.SetActive(false);
            var instances = await InstantiateAsync(template, _capacity, transform);
            if (this == null) return;

            Destroy(template);
            if (instances.Length == 0) return;

            var shardsPerGroup = instances[0].transform.childCount;
            _buffers = new GlassShardBuffers(instances.Length, shardsPerGroup);
            _groups = new GlassShardGroup[instances.Length];
            var shards = new Transform[instances.Length * shardsPerGroup];
            for (var i = 0; i < instances.Length; i++)
            {
                var root = instances[i];
                root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                root.SetActive(false);
                _groups[i] = new GlassShardGroup(root, i, _buffers);
                _groups[i].Shards.CopyTo(shards, i * shardsPerGroup);
            }

            _transforms = new TransformAccessArray(shards);
            _buffer = new RecycleBuffer<GlassShardGroup>(_groups);
            IsGenerated = true;
        }

        private void Update()
        {
            if (!IsGenerated) return;

            var deltaTime = Time.deltaTime;
            var hasActive = false;
            foreach (var group in _groups)
            {
                if (!group.IsActive) continue;

                if (group.Advance(deltaTime))
                {
                    hasActive = true;
                    continue;
                }

                group.OnRecycle();
                _buffer.Release(group);
            }

            if (!hasActive) return;

            _stepHandle = new StepShardsJob
            {
                Remaining = _buffers.Remaining,
                GroundY = _buffers.GroundY,
                SpawnScales = _buffers.SpawnScales,
                Velocities = _buffers.Velocities,
                AngularVelocities = _buffers.AngularVelocities,
                IsLanded = _buffers.IsLanded,
                ShardsPerGroup = _buffers.ShardsPerGroup,
                DeltaTime = deltaTime,
                GravityY = Physics.gravity.y,
                LandedHeight = _landedHeight,
                ShrinkDuration = _shrinkDuration
            }.Schedule(_transforms);
        }

        private void LateUpdate()
        {
            _stepHandle.Complete();
        }

        private void OnDestroy()
        {
            if (_instances[(int)_kind] == this) _instances[(int)_kind] = null;

            _stepHandle.Complete();
            if (_transforms.isCreated) _transforms.Dispose();
            _buffers?.Dispose();
        }

        /// <summary>
        /// 使用中のグループの破片を1つずつ、重力で進めて回し、地面の高さの少し上で止める。寿命の終わりでは大きさを縮める。
        /// </summary>
        [BurstCompile]
        private struct StepShardsJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<float> Remaining;
            [ReadOnly] public NativeArray<float> GroundY;
            [ReadOnly] public NativeArray<float3> SpawnScales;
            public NativeArray<float3> Velocities;
            public NativeArray<float3> AngularVelocities;
            public NativeArray<bool> IsLanded;
            public int ShardsPerGroup;
            public float DeltaTime;
            public float GravityY;
            public float LandedHeight;
            public float ShrinkDuration;

            public void Execute(int index, TransformAccess transform)
            {
                var group = index / ShardsPerGroup;
                var remaining = Remaining[group];
                if (remaining <= 0f) return;

                if (ShrinkDuration > 0f)
                {
                    var shrink = math.saturate(remaining / ShrinkDuration);
                    if (shrink < 1f) transform.localScale = SpawnScales[index] * shrink;
                }

                if (IsLanded[index]) return;

                var velocity = Velocities[index];
                velocity.y += GravityY * DeltaTime;
                Velocities[index] = velocity;

                float3 position = transform.position;
                position += velocity * DeltaTime;

                quaternion rotation = transform.rotation;
                var angularVelocity = AngularVelocities[index];
                var angularSpeed = math.length(angularVelocity);
                if (angularSpeed > 1e-4f)
                {
                    rotation = math.mul(quaternion.AxisAngle(angularVelocity / angularSpeed, angularSpeed * DeltaTime), rotation);
                }

                var floorY = GroundY[group] + LandedHeight;
                if (position.y <= floorY)
                {
                    position.y = floorY;
                    IsLanded[index] = true;
                }

                transform.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
