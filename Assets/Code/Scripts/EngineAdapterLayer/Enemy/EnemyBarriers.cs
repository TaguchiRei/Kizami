using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ディフェンダーが張る球のバリアを扱う。EnemySpawnAdapter と同じ GameObject に置き、EnemySpawnAdapter が初期化と毎フレームの更新を呼ぶ。
    /// グループが目的地に着いて螺旋に並び（EnemyGroup.HasArrived）、隊列の 0 番（螺旋の中心）に生きているディフェンダーがいる間、そのディフェンダーを中心にバリアを張る。
    /// バリアの物（当たり判定と ArmorPanel を持つ球）は初期化のときにプールに作り、張っているグループにだけ貸すので、体を貸していない遠くのグループにも当たり判定がある。
    /// 当たった攻撃の数はグループの状態（EnemyGroup.BarrierHitCount）に持ち、物を返しても続く。壊れたバリアは張り直さない。崩落は防がない。
    /// 張っている間、体を貸しているディフェンダーは前脚の足を体の前で合わせる。
    /// 張っているかと、張ったときのアンカーとプレイヤーの距離をグループの状態に書き、EnemyGroupJob はそれを見て、プレイヤーが近づく分には群れをその位置に留める。
    /// </summary>
    /// <remarks>
    /// バリアの物は Barrier レイヤーに置く。物理でぶつかるのは Player（乗れる・入れない）と Shard（投げたかけらで割る）だけで、崩落の塊や敵の体は素通りする（崩落を防がない為）。
    /// 剣・スキル・敵の弾・ビームを止める判定は、それぞれのレイの対象のレイヤーに Barrier を入れて行う。
    /// プレイヤーが球の中にいる間は張らない。張った瞬間に、プレイヤーが当たり判定の中に閉じ込められる為。
    /// 代わりにプレイヤーを球の面より上へ打ち上げ、外へ出てから張る。真上に天井があれば打ち上げず、プレイヤーが自分で外へ出るまで張らない。
    /// </remarks>
    public sealed class EnemyBarriers : MonoBehaviour
    {
        private readonly List<ArmorPanel> _barriers = new();
        private readonly List<Rigidbody> _barrierBodies = new();
        private readonly Stack<int> _freeBarriers = new();

        [SerializeField]
        [Tooltip("バリアの物のプレハブ。根に ArmorPanel、SphereCollider（半径 0.5、Barrier レイヤー）、Kinematic の Rigidbody を付け、直径 1 の球で作る")]
        private ArmorPanel _barrierPrefab;

        [SerializeField, Min(1)]
        [Tooltip("同時に張れるバリアの数。初期化のときにこの数だけ作る")]
        private int _capacity = 16;

        [SerializeField, Min(0.5f)]
        [Tooltip("バリアの半径（m）。グループの螺旋の最も外のメンバーと、その体の端までを覆う")]
        private float _radius = 17f;

        [SerializeField]
        [Tooltip("バリアを張る間に前で合わせる、ディフェンダーの脚の番号（EnemyLegs の設定の並び）")]
        private int[] _guardLegs = { 0, 2 };

        [SerializeField]
        [Tooltip("前で合わせる足を伸ばす先（ディフェンダーの体の根の空間）。並びは _guardLegs と同じ")]
        private Vector3[] _guardFootPositions = { new(-0.35f, 1.6f, 3f), new(0.35f, 1.6f, 3f) };

        [SerializeField, Min(0f)]
        [Tooltip("打ち上げで、プレイヤーの足元が球の面の最も高い所からさらに上がる高さ（m）")]
        private float _launchMargin = 2f;

        [SerializeField]
        [Tooltip("打ち上げをさえぎる天井として調べるレイヤー。プレイヤー自身のレイヤーは外す")]
        private LayerMask _ceilingLayers;

        /// <summary> グループごとの、貸しているバリアの _barriers での番号。貸していなければ -1 </summary>
        private int[] _groupBarriers;

        private CapsuleCollider _targetCollider;
        private Rigidbody _targetBody;

        /// <summary> プレイヤーを真上へ打ち上げる関数。引数は上向きの打ち出し速度（m/s）。null なら打ち上げない </summary>
        private Action<float> _requestLaunch;

        /// <summary> バリアの耐久値の最大。プレハブの ArmorPanel から読む </summary>
        private int _maxDurability;

        private bool _isReady;

        /// <summary> 張っているバリアの数 </summary>
        public int RaisedCount => _isReady ? _capacity - _freeBarriers.Count : 0;

        /// <summary>
        /// バリアの物をプールに作る。プレハブかプレイヤーの当たり判定がなければ、警告を出して張らないままにする。
        /// </summary>
        /// <param name="target">プレイヤー。子に当たり判定を持つ</param>
        /// <param name="groupCapacity">グループの数（EnemyGroups.Groups の長さ）</param>
        /// <param name="requestLaunch">プレイヤーを真上へ打ち上げる関数（PlayerMovementService.RequestLaunch）。引数は上向きの打ち出し速度（m/s）</param>
        public void Initialize(Transform target, int groupCapacity, Action<float> requestLaunch)
        {
            _targetCollider = target != null ? target.GetComponentInChildren<CapsuleCollider>() : null;
            if (_barrierPrefab == null || _targetCollider == null)
            {
                UsefulLogger.LogWarning("バリアのプレハブか、プレイヤーの CapsuleCollider がない為、バリアを張りません。", this);
                return;
            }

            _targetBody = _targetCollider.attachedRigidbody;
            _requestLaunch = requestLaunch;
            if (_requestLaunch == null || _targetBody == null)
            {
                UsefulLogger.LogWarning("打ち上げの関数か、プレイヤーの Rigidbody がない為、バリアの中のプレイヤーを打ち上げません。", this);
            }

            _maxDurability = _barrierPrefab.MaxDurability;
            _groupBarriers = new int[groupCapacity];
            for (var g = 0; g < groupCapacity; g++) _groupBarriers[g] = -1;

            for (var i = 0; i < _capacity; i++)
            {
                var barrier = Instantiate(_barrierPrefab, transform);
                barrier.gameObject.SetActive(false);
                barrier.transform.localScale = Vector3.one * (_radius * 2f);
                _barriers.Add(barrier);
                _barrierBodies.Add(barrier.GetComponent<Rigidbody>());
                _freeBarriers.Push(i);
            }

            _isReady = true;
        }

        /// <summary>
        /// グループごとに、バリアを張るかを決めて物を貸し借りし、張っているバリアをディフェンダーへ動かす。当たった攻撃の数をグループの状態へ書き戻す。
        /// 張りたいときにプレイヤーが球の中にいれば、物を貸さずに打ち上げを要求する。
        /// EnemySpawnAdapter が毎フレーム、敵を動かして体の位置を合わせたあとに呼ぶ。
        /// </summary>
        /// <param name="agents">敵の状態</param>
        /// <param name="groups">グループ。当たった攻撃の数を書く</param>
        /// <param name="defenderLender">ディフェンダーの体の貸し借り。前脚を合わせる体を引く。ディフェンダーの設定がなければ null</param>
        /// <param name="grid">放した足を置く高さを読む経路の格子</param>
        public void Tick(NativeArray<EnemyAgent> agents, EnemyGroups groups, EnemyBodyLender defenderLender,
            in EnemyNavigationGrid grid)
        {
            if (!_isReady) return;

            var groupStates = groups.Groups;
            var targetBounds = _targetCollider.bounds;

            for (var g = 0; g < groupStates.Length; g++)
            {
                var group = groupStates[g];
                var barrier = _groupBarriers[g];
                if (!group.IsActive && barrier < 0) continue;

                var defender = group.IsActive ? groups.GetLeader(g) : -1;
                var hasDefender = defender >= 0 && agents[defender].IsAlive && agents[defender].GroupIndex == g &&
                                  agents[defender].Kind == EnemyKind.Defender;

                if (barrier >= 0)
                {
                    var panel = _barriers[barrier];
                    if (panel.IsBroken)
                    {
                        // TODO: バリアが割れた音を鳴らす（音の基盤ができてから）。割れる演出の作り込みは区間13
                        group.BarrierHitCount = _maxDurability;
                    }
                    else
                    {
                        group.BarrierHitCount = _maxDurability - panel.Durability;
                    }

                    groupStates[g] = group;
                }

                var wantsBarrier = hasDefender && group.HasArrived && group.BarrierHitCount < _maxDurability;
                if (wantsBarrier && barrier < 0)
                {
                    var center = (Vector3)agents[defender].Position;
                    if (targetBounds.SqrDistance(center) > _radius * _radius)
                    {
                        barrier = Lend(g, center, group.BarrierHitCount);
                    }
                    else
                    {
                        TryLaunch(center);
                    }
                }
                else if (!wantsBarrier && barrier >= 0)
                {
                    Return(g);
                    barrier = -1;
                }
                else if (barrier >= 0)
                {
                    _barrierBodies[barrier].MovePosition(agents[defender].Position);
                }

                var isRaised = barrier >= 0;
                if (isRaised != group.IsBarrierRaised)
                {
                    group.IsBarrierRaised = isRaised;
                    var toTarget = ((float3)targetBounds.center - group.AnchorPosition).xz;
                    group.BarrierStayDistance = isRaised ? math.length(toTarget) : 0f;
                    groupStates[g] = group;
                }

                if (hasDefender && defenderLender != null && agents[defender].BodyIndex >= 0)
                {
                    UpdateGuardPose(defenderLender.GetLegs(agents[defender].BodyIndex), barrier >= 0, grid);
                }
            }
        }

        /// <summary>
        /// 空いているバリアの物をグループ g に貸し、中心に置いて残りの耐久値を戻す。空きがなければ貸さずに -1 を返す。
        /// </summary>
        private int Lend(int g, Vector3 center, int hitCount)
        {
            if (_freeBarriers.Count == 0) return -1;

            var barrier = _freeBarriers.Pop();
            var panel = _barriers[barrier];
            panel.transform.position = center;
            panel.gameObject.SetActive(true);
            panel.SetDurability(_maxDurability - hitCount);
            _groupBarriers[g] = barrier;
            return barrier;
        }

        /// <summary>
        /// 中心が center の球の面の最も高い所より _launchMargin だけ上へ、プレイヤーの足元が届く速度で打ち上げを要求する。
        /// プレイヤーがすでにその速度近くで上がっているとき（打ち上げたあと）と、届く高さまでの間にカプセルが天井に当たるときは要求しない。
        /// </summary>
        private void TryLaunch(Vector3 center)
        {
            if (_requestLaunch == null || _targetBody == null) return;

            CapsuleGeometry.GetAxis(_targetCollider, out var bottomSphereCenter, out var topSphereCenter, out var radius);
            var height = center.y + _radius + _launchMargin - (bottomSphereCenter.y - radius);
            if (height <= 0f) return;

            // 高さ h まで上がる初速は v = √(2gh)。打ち上げたあとは上がるほど要る速度も同じだけ減るので、要る速度の 9 割を保つ間は要求し直さない
            var speed = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * height);
            if (_targetBody.linearVelocity.y >= speed * 0.9f) return;

            if (Physics.CapsuleCast(bottomSphereCenter, topSphereCenter, radius, Vector3.up, height, _ceilingLayers,
                    QueryTriggerInteraction.Ignore)) return;

            _requestLaunch(speed);
        }

        /// <summary>
        /// グループ g に貸していたバリアの物を非アクティブにしてプールへ返す。
        /// </summary>
        private void Return(int g)
        {
            var barrier = _groupBarriers[g];
            _barriers[barrier].gameObject.SetActive(false);
            _freeBarriers.Push(barrier);
            _groupBarriers[g] = -1;
        }

        /// <summary>
        /// バリアを張っている間は、前で合わせる脚の足を体の前へ伸ばして留める。張っていなければ放す。
        /// </summary>
        private void UpdateGuardPose(EnemyLegs legs, bool isRaised, in EnemyNavigationGrid grid)
        {
            if (legs == null) return;

            for (var i = 0; i < _guardLegs.Length && i < _guardFootPositions.Length; i++)
            {
                if (isRaised)
                {
                    legs.HoldFoot(_guardLegs[i], legs.transform.TransformPoint(_guardFootPositions[i]));
                }
                else
                {
                    legs.ReleaseFoot(_guardLegs[i], grid);
                }
            }
        }
    }
}
