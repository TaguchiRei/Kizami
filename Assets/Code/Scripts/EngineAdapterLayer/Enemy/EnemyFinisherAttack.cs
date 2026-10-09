using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// フィニッシャーの攻撃を扱う。EnemySpawnAdapter と同じ GameObject に置き、EnemySpawnAdapter が初期化と毎フレームの更新を呼ぶ。
    /// 攻撃するのはマップ全体で 1 体まで。グループが螺旋に並んでいて（EnemyGroup.HasArrived）、プレイヤーが射程の中にいる、冷却を終えたフィニッシャーが始める。
    /// 同じグループの最も近いメンバーを吸収の対象に選んで止め、その上へ飛んで腕を伸ばし、対象をディゾルブで消しながら粒を吸い込む。
    /// 吸収された敵はオーブもチャージも出さない。吸収したあとは分身してビームを撃ち（EnemyFinisherBeams）、撃ち終えたら冷却に入って、飛び立った位置へ戻る。
    /// 吸収する前に対象が倒れるか、グループの並びが解けたら、冷却に入って戻る。フィニッシャーが倒れたら、対象を放し、分身・ビーム・デカールを消してやめる。
    /// 吸収を始めたあとにフィニッシャーが倒れたときは、対象もそのまま消す。消えかけた体を元に戻して歩かせることはしない。
    /// </summary>
    /// <remarks>
    /// 吸収の粒は、崩落のエネルギーと同じ VFX Graph（EnemyEnergy.vfx）を別の VisualEffect で使い、吸い込む先（EnergyTarget）をフィニッシャーにする。
    /// VFX Graph で受け取るプロパティとイベントは EnemyEnergyAdapter と同じ。
    /// </remarks>
    public sealed class EnemyFinisherAttack : MonoBehaviour
    {
        /// <summary> 飛ぶ先にこの距離（m）まで近づいたら、着いたとみなす </summary>
        private const float ARRIVE_DISTANCE = 0.1f;

        private static readonly int _energyEventId = Shader.PropertyToID("OnEnergy");
        private static readonly int _positionsId = Shader.PropertyToID("EnergyPositions");
        private static readonly int _positionCountId = Shader.PropertyToID("EnergyPositionCount");
        private static readonly int _targetId = Shader.PropertyToID("EnergyTarget");
        private static readonly int _spawnCountId = Shader.PropertyToID("spawnCount");

        /// <summary> 吸収の対象の、残っている部位の中心。腕を伸ばす先と、粒を出す位置に使う作業用の一覧 </summary>
        private readonly List<Vector3> _partCenters = new();

        [SerializeField]
        [Tooltip("吸収の粒を出す VisualEffect。VFX Graph は崩落のエネルギーと同じ EnemyEnergy.vfx")]
        private VisualEffect _effect;

        [SerializeField]
        [Tooltip("吸収したあとの分身とビームを扱う EnemyFinisherBeams。未設定なら吸収したあとすぐに戻る")]
        private EnemyFinisherBeams _beams;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーがこの距離（m）以内にいると、攻撃を始める")]
        private float _range = 50f;

        [SerializeField, Min(0f)]
        [Tooltip("吸収するときに浮く、対象の体の根からの高さ（m）")]
        private float _hoverHeight = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("浮いてから、腕を対象へ伸ばしきるまでの時間（秒）")]
        private float _grabDuration = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("腕でつかんでから、対象が消えきるまでの時間（秒）")]
        private float _absorbDuration = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("攻撃を終えるかやめてから、次に攻撃できるまでの時間（秒）")]
        private float _cooldown = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("腕の先を、対象の体の中心から各腕の側へ離す長さ（m）")]
        private float _graspSpread = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("体を貸していない対象の、体の中心の体の根からの高さ（m）")]
        private float _targetCenterHeight = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("粒を吸い込ませる、フィニッシャーの体の中心の体の根からの高さ（m）")]
        private float _finisherCenterHeight = 3f;

        [SerializeField, Min(1)]
        [Tooltip("粒を出すたびに、対象の部位 1 つあたりに出す粒の数")]
        private int _particlesPerPart = 4;

        [SerializeField, Min(0.01f)]
        [Tooltip("吸収している間、粒を出す間隔（秒）")]
        private float _burstInterval = 0.1f;

        [SerializeField, Min(1)]
        [Tooltip("1 回に粒を出す位置（対象の部位）の数の上限")]
        private int _maxBurstPositions = 32;

        private Transform _target;
        private Material _dissolveMaterial;
        private GraphicsBuffer _positionBuffer;
        private VFXEventAttribute _eventAttribute;
        private Phase _phase;

        /// <summary> 攻撃しているフィニッシャーの番号。攻撃していなければ -1 </summary>
        private int _finisher = -1;

        /// <summary> 吸収の対象の番号。選んでいなければ -1 </summary>
        private int _absorbTarget = -1;

        /// <summary> 今の段階に入ってからの時間（秒） </summary>
        private float _phaseTime;

        /// <summary> 次に粒を出すまでの時間（秒） </summary>
        private float _burstTimer;

        /// <summary> フィニッシャーが飛び立った位置。戻るときはここへ降りる </summary>
        private float3 _homePosition;

        /// <summary> 分身とビームを使えるか </summary>
        private bool _hasBeams;

        private bool _isReady;

        /// <summary>
        /// 敵の体の脚を返す。体を貸していないか、脚を持たない体では null。
        /// </summary>
        private static EnemyLegs GetLegs(in EnemyAgent agent, EnemyBodyLender[] lenders)
        {
            var lender = lenders[(int)agent.Kind];
            return lender != null && agent.BodyIndex >= 0 ? lender.GetLegs(agent.BodyIndex) : null;
        }

        /// <summary>
        /// 生きているフィニッシャーの、次に攻撃できるまでの時間を減らす。
        /// </summary>
        private static void AdvanceCooldowns(NativeArray<EnemyAgent> agents, float deltaTime)
        {
            for (var i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (!agent.IsAlive || agent.Kind != EnemyKind.Finisher || agent.AttackCooldown <= 0f) continue;

                agent.AttackCooldown -= deltaTime;
                agents[i] = agent;
            }
        }

        /// <summary>
        /// 行動を始められるか。地面に立っていて、歩いていて、壊れた移動部位が上限に達していない敵。
        /// </summary>
        private static bool CanAct(in EnemyAgent agent)
        {
            return agent.IsGrounded && agent.MoveMode == EnemyMoveMode.Walking &&
                   agent.BrokenMovePartCount < agent.BrokenMovePartLimit;
        }

        /// <summary>
        /// フィニッシャー finisher と同じグループの、行動を始められるメンバーのうち、最も近い敵を返す。なければ -1。
        /// </summary>
        private static int FindAbsorbTarget(NativeArray<EnemyAgent> agents, int finisher)
        {
            var origin = agents[finisher].Position;
            var group = agents[finisher].GroupIndex;
            var nearest = -1;
            var nearestDistanceSq = float.PositiveInfinity;

            for (var i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (i == finisher || !agent.IsAlive || agent.GroupIndex != group || !CanAct(agent)) continue;

                var distanceSq = math.distancesq(agent.Position, origin);
                if (distanceSq >= nearestDistanceSq) continue;

                nearest = i;
                nearestDistanceSq = distanceSq;
            }

            return nearest;
        }

        /// <summary>
        /// 敵に貸している体を返す。体を貸していなければ null。
        /// </summary>
        private static EnemyBody GetBody(in EnemyAgent agent, EnemyBodyLender[] lenders)
        {
            var lender = lenders[(int)agent.Kind];
            return lender != null && agent.BodyIndex >= 0 ? lender.GetBody(agent.BodyIndex) : null;
        }

        /// <summary>
        /// フィニッシャーの腕を、休みの姿勢の足先の位置に留めて垂らす。
        /// </summary>
        private static void HangArms(EnemyLegs legs)
        {
            if (legs == null) return;

            for (var i = 0; i < legs.LegCount; i++)
            {
                legs.HoldFoot(i, legs.GetRestFootPosition(i));
            }
        }

        /// <summary>
        /// 粒を出す GraphicsBuffer を作る。プレイヤーがなければ、警告を出して攻撃しないままにする。
        /// VisualEffect か吸収のプロパティがなければ、警告を出して粒を出さずに攻撃する。
        /// </summary>
        /// <param name="target">プレイヤー</param>
        /// <param name="dissolveMaterial">吸収された敵を消すディゾルブのマテリアル。null なら対象はディゾルブせずに消える</param>
        /// <param name="applyDamage">ビームがプレイヤーに当たったときにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        public void Initialize(Transform target, Material dissolveMaterial, Action<int> applyDamage)
        {
            if (target == null)
            {
                UsefulLogger.LogWarning("プレイヤーがない為、フィニッシャーは攻撃しません。", this);
                return;
            }

            _target = target;
            _dissolveMaterial = dissolveMaterial;

            if (_beams == null)
            {
                UsefulLogger.LogWarning("EnemyFinisherBeams が設定されていない為、フィニッシャーは吸収したあと分身せずに戻ります。", this);
            }
            else
            {
                _hasBeams = _beams.Initialize(target, applyDamage);
            }

            if (_effect == null || !_effect.HasGraphicsBuffer(_positionsId) || !_effect.HasInt(_positionCountId) ||
                !_effect.HasVector3(_targetId))
            {
                UsefulLogger.LogWarning("吸収の VisualEffect がないか、VFX Graph に EnergyPositions・EnergyPositionCount・EnergyTarget がない為、吸収の粒を出しません。", this);
                _effect = null;
            }
            else
            {
                _positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _maxBurstPositions,
                    sizeof(float) * 3);
                _eventAttribute = _effect.CreateVFXEventAttribute();
                _effect.SetGraphicsBuffer(_positionsId, _positionBuffer);
            }

            _isReady = true;
        }

        /// <summary>
        /// フィニッシャーの冷却を進め、攻撃していなければ始める敵を探し、攻撃していれば段階を進める。
        /// EnemySpawnAdapter が毎フレーム、敵を動かして体の位置を合わせたあとに呼ぶ。
        /// </summary>
        /// <param name="agents">敵の状態</param>
        /// <param name="groups">グループ。並びが解けていないかを読む</param>
        /// <param name="lenders">種類ごとの体の貸し借り。並びは EnemyKind と同じで、設定のない種類は null</param>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <param name="grid">戻って放した腕を置く高さを読む経路の格子</param>
        public void Tick(NativeArray<EnemyAgent> agents, EnemyGroups groups, EnemyBodyLender[] lenders,
            float deltaTime, in EnemyNavigationGrid grid)
        {
            if (!_isReady) return;

            AdvanceCooldowns(agents, deltaTime);

            if (_phase == Phase.Idle)
            {
                TryStart(agents, groups);
                return;
            }

            if (!agents[_finisher].IsAlive)
            {
                Abort(agents);
                return;
            }

            _phaseTime += deltaTime;
            var finisherLegs = GetLegs(agents[_finisher], lenders);

            switch (_phase)
            {
                case Phase.Approaching:
                case Phase.Grabbing:
                    if (!IsTargetValid(agents, groups))
                    {
                        StartReturn(agents);
                        break;
                    }

                    UpdateGrab(agents, lenders, finisherLegs);
                    break;
                case Phase.Absorbing:
                    if (!agents[_absorbTarget].IsAlive)
                    {
                        StartReturn(agents);
                        break;
                    }

                    UpdateAbsorb(agents, lenders, finisherLegs, deltaTime);
                    break;
                case Phase.Barrage:
                    UpdateBarrage(agents, finisherLegs, deltaTime);
                    break;
                case Phase.Returning:
                    UpdateReturn(agents, finisherLegs, grid);
                    break;
            }

            if (_effect != null && _finisher >= 0)
            {
                _effect.SetVector3(_targetId, (Vector3)agents[_finisher].Position + Vector3.up * _finisherCenterHeight);
            }
        }

        /// <summary>
        /// 攻撃を始められるフィニッシャーと、その吸収の対象を探し、見つかれば始める。
        /// 始められるのは、地面に立って歩ける状態で、冷却中でなく、グループが螺旋に並んでいて、プレイヤーが射程の中にいるフィニッシャー。
        /// </summary>
        private void TryStart(NativeArray<EnemyAgent> agents, EnemyGroups groups)
        {
            var player = (float3)_target.position;
            var rangeSq = _range * _range;
            var groupStates = groups.Groups;

            for (var i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (!agent.IsAlive || agent.Kind != EnemyKind.Finisher || agent.AttackCooldown > 0f ||
                    !CanAct(agent) || agent.GroupIndex < 0) continue;

                var group = groupStates[agent.GroupIndex];
                if (!group.IsActive || !group.HasArrived || math.distancesq(agent.Position, player) > rangeSq) continue;

                var absorbTarget = FindAbsorbTarget(agents, i);
                if (absorbTarget < 0) continue;

                Begin(agents, i, absorbTarget);
                return;
            }
        }

        /// <summary>
        /// 吸収の対象を止め、フィニッシャーを飛ばして対象の上へ向かわせる。
        /// </summary>
        private void Begin(NativeArray<EnemyAgent> agents, int finisher, int absorbTarget)
        {
            var target = agents[absorbTarget];
            target.MoveMode = EnemyMoveMode.Held;
            agents[absorbTarget] = target;

            var agent = agents[finisher];
            _homePosition = agent.Position;
            agent.MoveMode = EnemyMoveMode.Flying;
            agent.IsGrounded = false;
            agent.VerticalSpeed = 0f;
            agent.FlyTarget = target.Position + new float3(0f, _hoverHeight, 0f);
            agents[finisher] = agent;

            _finisher = finisher;
            _absorbTarget = absorbTarget;
            SetPhase(Phase.Approaching);
        }

        /// <summary>
        /// 吸収の対象が生きていて、フィニッシャーのグループが螺旋に並んだままか。
        /// </summary>
        private bool IsTargetValid(NativeArray<EnemyAgent> agents, EnemyGroups groups)
        {
            if (!agents[_absorbTarget].IsAlive) return false;

            var groupIndex = agents[_finisher].GroupIndex;
            return groupIndex >= 0 && groups.Groups[groupIndex].IsActive && groups.Groups[groupIndex].HasArrived;
        }

        /// <summary>
        /// 対象の上へ飛び、着いたら腕を対象へ伸ばす。伸ばしきったら吸収を始める。飛んでいる間、腕は垂らす。
        /// </summary>
        private void UpdateGrab(NativeArray<EnemyAgent> agents, EnemyBodyLender[] lenders, EnemyLegs finisherLegs)
        {
            var target = agents[_absorbTarget];
            var agent = agents[_finisher];
            agent.FlyTarget = target.Position + new float3(0f, _hoverHeight, 0f);
            agents[_finisher] = agent;

            if (_phase == Phase.Approaching)
            {
                HangArms(finisherLegs);
                if (math.distance(agent.Position, agent.FlyTarget) <= ARRIVE_DISTANCE) SetPhase(Phase.Grabbing);
                return;
            }

            var reach = _grabDuration > 0f ? Mathf.Clamp01(_phaseTime / _grabDuration) : 1f;
            CollectTargetCenters(target, lenders);
            ReachArms(finisherLegs, reach);

            if (reach >= 1f)
            {
                _burstTimer = 0f;
                SetPhase(Phase.Absorbing);
            }
        }

        /// <summary>
        /// 対象をつかんだまま、ディゾルブで消し、粒を吸い込ませる。消えきったら対象をステージから消し、冷却に入って戻る。
        /// 対象に体がなければ、ディゾルブせずに粒だけ出す。
        /// </summary>
        private void UpdateAbsorb(NativeArray<EnemyAgent> agents, EnemyBodyLender[] lenders, EnemyLegs finisherLegs,
            float deltaTime)
        {
            var target = agents[_absorbTarget];
            var body = GetBody(target, lenders);
            var amount = Mathf.Clamp01(_phaseTime / _absorbDuration);
            if (body != null)
            {
                body.BeginDissolve(_dissolveMaterial);
                body.SetDissolveAmount(amount);
            }

            CollectTargetCenters(target, lenders);
            ReachArms(finisherLegs, 1f);

            _burstTimer -= deltaTime;
            if (_burstTimer <= 0f)
            {
                EmitParticles();
                _burstTimer += _burstInterval;
            }

            if (amount < 1f) return;

            // 体は、次の EnemyBodyLender.ReturnBodies で返る。倒れたことにしないので、オーブもチャージも出ない
            target.IsAlive = false;
            agents[_absorbTarget] = target;
            _absorbTarget = -1;

            if (!_hasBeams)
            {
                StartReturn(agents);
                return;
            }

            _beams.Begin(agents[_finisher].Position);
            SetPhase(Phase.Barrage);
        }

        /// <summary>
        /// 分身とビームの段階を進め、本体を EnemyFinisherBeams の示す位置へ飛ばす。撃ち終えたら冷却に入って戻る。飛んでいる間、腕は垂らす。
        /// </summary>
        private void UpdateBarrage(NativeArray<EnemyAgent> agents, EnemyLegs finisherLegs, float deltaTime)
        {
            HangArms(finisherLegs);

            var agent = agents[_finisher];
            if (!_beams.Tick(agent.Position, deltaTime, out var flyTarget))
            {
                StartReturn(agents);
                return;
            }

            agent.FlyTarget = flyTarget;
            agents[_finisher] = agent;
        }

        /// <summary>
        /// 飛び立った位置へ飛んで戻り、着いたら地面に立たせて歩かせ、腕を放す。飛んでいる間、腕は垂らす。
        /// </summary>
        private void UpdateReturn(NativeArray<EnemyAgent> agents, EnemyLegs finisherLegs, in EnemyNavigationGrid grid)
        {
            var agent = agents[_finisher];
            if (math.distance(agent.Position, agent.FlyTarget) > ARRIVE_DISTANCE)
            {
                HangArms(finisherLegs);
                return;
            }

            agent.Position = agent.FlyTarget;
            agent.MoveMode = EnemyMoveMode.Walking;
            agent.IsGrounded = true;
            agent.VerticalSpeed = 0f;
            agent.FallStartHeight = agent.Position.y;
            agents[_finisher] = agent;

            if (finisherLegs != null)
            {
                for (var i = 0; i < finisherLegs.LegCount; i++)
                {
                    finisherLegs.ReleaseFoot(i, grid);
                }
            }

            _finisher = -1;
            SetPhase(Phase.Idle);
        }

        /// <summary>
        /// 吸収の対象が生きていれば放して歩かせ、フィニッシャーを冷却に入れて飛び立った位置へ戻らせる。
        /// </summary>
        private void StartReturn(NativeArray<EnemyAgent> agents)
        {
            ReleaseTarget(agents);

            var agent = agents[_finisher];
            agent.FlyTarget = _homePosition;
            agent.AttackCooldown = _cooldown;
            agents[_finisher] = agent;
            SetPhase(Phase.Returning);
        }

        /// <summary>
        /// フィニッシャーが倒れたときに呼ぶ。吸収の対象を放してやめる。吸収を始めていれば、対象も消す。分身・ビーム・デカールを消す。
        /// </summary>
        private void Abort(NativeArray<EnemyAgent> agents)
        {
            if (_hasBeams) _beams.Stop();

            if (_phase == Phase.Absorbing)
            {
                var target = agents[_absorbTarget];
                target.IsAlive = false;
                agents[_absorbTarget] = target;
            }

            ReleaseTarget(agents);
            _finisher = -1;
            SetPhase(Phase.Idle);
        }

        /// <summary>
        /// 吸収の対象が生きていれば歩かせ、選んでいない状態にする。
        /// </summary>
        private void ReleaseTarget(NativeArray<EnemyAgent> agents)
        {
            if (_absorbTarget < 0) return;

            var target = agents[_absorbTarget];
            if (target.IsAlive)
            {
                target.MoveMode = EnemyMoveMode.Walking;
                agents[_absorbTarget] = target;
            }

            _absorbTarget = -1;
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        /// <summary>
        /// 吸収の対象の残っている部位の中心を _partCenters に集める。体を貸していないか部位が残っていなければ、体の中心の高さの 1 点にする。
        /// </summary>
        private void CollectTargetCenters(in EnemyAgent target, EnemyBodyLender[] lenders)
        {
            _partCenters.Clear();
            var body = GetBody(target, lenders);
            if (body != null) body.CollectPartCenters(_partCenters);
            if (_partCenters.Count == 0) _partCenters.Add((Vector3)target.Position + Vector3.up * _targetCenterHeight);
        }

        /// <summary>
        /// フィニッシャーの腕の先を、休みの姿勢の足先から、対象の体の中心を各腕の側へ _graspSpread 離した位置へ、割合 reach だけ伸ばす。
        /// 対象の体の中心は _partCenters の平均。
        /// </summary>
        private void ReachArms(EnemyLegs legs, float reach)
        {
            if (legs == null) return;

            var center = Vector3.zero;
            foreach (var partCenter in _partCenters)
            {
                center += partCenter;
            }

            center /= _partCenters.Count;

            var root = legs.transform.position;
            for (var i = 0; i < legs.LegCount; i++)
            {
                var rest = legs.GetRestFootPosition(i);
                var side = rest - root;
                side.y = 0f;
                var grasp = center + (side.sqrMagnitude > 0f ? side.normalized : Vector3.zero) * _graspSpread;
                legs.HoldFoot(i, Vector3.Lerp(rest, grasp, reach));
            }
        }

        /// <summary>
        /// 吸収の対象の部位の中心から、粒を出す。
        /// </summary>
        private void EmitParticles()
        {
            if (_effect == null) return;

            var count = Mathf.Min(_partCenters.Count, _maxBurstPositions);
            _positionBuffer.SetData(_partCenters, 0, 0, count);
            _effect.SetInt(_positionCountId, count);
            _eventAttribute.SetFloat(_spawnCountId, count * _particlesPerPart);
            _effect.SendEvent(_energyEventId, _eventAttribute);
        }

        private void OnDestroy()
        {
            _positionBuffer?.Release();
            _eventAttribute?.Dispose();
        }

        /// <summary>
        /// 攻撃の段階
        /// </summary>
        private enum Phase
        {
            /// <summary> 攻撃していない </summary>
            Idle,

            /// <summary> 吸収の対象の上へ飛んでいる </summary>
            Approaching,

            /// <summary> 対象の上に浮いて、腕を伸ばしている </summary>
            Grabbing,

            /// <summary> 腕でつかんで、対象を消している </summary>
            Absorbing,

            /// <summary> 分身してプレイヤーの上へ向かい、ビームを撃っている </summary>
            Barrage,

            /// <summary> 冷却に入り、飛び立った位置へ戻っている </summary>
            Returning
        }
    }
}
