using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 敵の部隊（グループ）ごとに、待機・追跡・帰還の状態と持ち場を決めるユースケース。
    /// Engine 側の観測（EnemySquadObservationState）を読んで判断し、命令（EnemySquadCommandState）に書く。Step は EnemySpawnAdapter がグループを動かす前に毎フレーム呼ぶ。
    /// 持ち場が追跡範囲に入ったら追跡、外れたら帰還にし、帰還で持ち場に着いたら待機にする。
    /// 帰還の途中で進めない状態が RETURN_BLOCKED_DURATION 続いたら、アンカーの位置を新しい持ち場（臨時の拠点）にして待機にする。元の持ち場へ移さないのは、持ち場が埋まっていることがあり、プレイヤーが敵を分断する遊び（橋を切るなど）を残す為。
    /// 追跡中に満員の人数の撤退する損耗の割合以上を失った部隊は、撤退してスポーン位置へ帰る。撤退中は、持ち場が追跡範囲にあっても追跡に戻らない。
    /// 補充を受けられない部隊（臨時の拠点にいる部隊と、スポーン位置から出せない部隊）は撤退しない。帰り着いても満員に戻らず、追跡と撤退を繰り返す為。
    /// 追跡中の部隊には、プレイヤーを囲む螺旋の上の置き場を 1 つずつ割り当てる。置き場の位置は Engine 側がそのフレームのプレイヤーの位置で求めたものを観測から読む。
    /// </summary>
    public sealed class EnemySquadService : IDisposable
    {
        /// <summary> 帰還の途中で進めない状態がこの時間（秒）続いたら、その位置を新しい持ち場にする </summary>
        private const float RETURN_BLOCKED_DURATION = 5f;

        private readonly EnemySquadCommandState _commandState = new();

        /// <summary> 満員の人数のうち、この割合以上を失ったら撤退する </summary>
        private float _retreatLossRatio;

        private IEnemySquadObservationState _observationState;
        private IDisposable _observationStateWaiter;

        /// <summary> 部隊ごとの、持ち場を決めたか。部隊が出たあとの最初の Step で、スポーン位置を持ち場にする </summary>
        private bool[] _hasHome = Array.Empty<bool>();

        /// <summary> 部隊ごとの、帰還の途中で進めない状態が続いている時間（秒） </summary>
        private float[] _blockedTimes = Array.Empty<float>();

        /// <summary> 部隊ごとの、持ち場が臨時の拠点か。全滅するまでそこを拠点にする </summary>
        private bool[] _hasTemporaryHome = Array.Empty<bool>();

        /// <summary> 置き場ごとの、部隊に割り当てているか。Step のたびに命令から作り直す </summary>
        private bool[] _usedSlots = Array.Empty<bool>();

        /// <summary>
        /// EnemySquadCommandState を EnemyBoard へ登録する。部隊の数は、EnemySquadObservationState が登録されたときにそろえる。
        /// </summary>
        /// <param name="blackBoard">EnemySquadCommandState の登録先と、EnemySquadObservationState の取得元</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        /// <param name="retreatLossRatio">満員の人数のうち、この割合以上を失ったら撤退する（0〜1）</param>
        public void Initialize(IBlackBoard blackBoard, int sceneId, float retreatLossRatio)
        {
            _retreatLossRatio = retreatLossRatio;

            if (!blackBoard.TryGetBoard<EnemyBoard>(out var enemyBoard, this)) return;

            enemyBoard.RegisterSceneState<IEnemySquadCommandState>(_commandState, sceneId);

            // EnemySquadObservationState は EngineAdapterLayer が登録するので、登録を待ち受けて拾う
            _observationStateWaiter = enemyBoard.SubscribeStateRegister<IEnemySquadObservationState>(
                () =>
                {
                    if (!enemyBoard.TryGetSceneState<IEnemySquadObservationState>(out var state, out _)) return;

                    _observationState = state;
                    var count = state.Squads.Count;
                    _commandState.SetSquadCount(count);
                    _hasHome = new bool[count];
                    _blockedTimes = new float[count];
                    _hasTemporaryHome = new bool[count];
                },
                invokeIfRegistered: true);
        }

        /// <summary>
        /// 部隊ごとに、観測から状態・持ち場・置き場を決めて命令に書く。いなくなった部隊の置き場は手放す。
        /// </summary>
        /// <param name="deltaTime">前の Step からの経過時間（秒）</param>
        public void Step(float deltaTime)
        {
            if (_observationState == null) return;

            var squads = _observationState.Squads;
            CollectUsedSlots(squads);
            for (var i = 0; i < squads.Count; i++)
            {
                var observation = squads[i];
                var command = _commandState.Squads[i];
                if (!observation.IsActive)
                {
                    if (command.EncircleSlot < 0) continue;

                    command.EncircleSlot = -1;
                    _commandState.SetSquad(i, command);
                    continue;
                }

                Decide(i, observation, ref command, deltaTime);
                if (command.State == EnemyGroupState.Tracking)
                {
                    UpdateEncircleSlot(observation, ref command);
                }
                else
                {
                    ReleaseSlot(ref command);
                }

                _commandState.SetSquad(i, command);
            }
        }

        public void Dispose()
        {
            _observationStateWaiter?.Dispose();
        }

        /// <summary>
        /// 部隊 1 つの状態と持ち場を決める。
        /// 帰還を終えた（着いた、または進めずに持ち場を変えた）ときは、追跡範囲の判定を次の Step に回す。観測の追跡範囲の判定は、変える前の持ち場で行ったものである為。
        /// </summary>
        private void Decide(int index, in EnemySquadObservation observation, ref EnemySquadCommand command,
            float deltaTime)
        {
            if (!_hasHome[index])
            {
                _hasHome[index] = true;
                command.State = EnemyGroupState.Waiting;
                command.HomePosition = observation.SpawnPosition;
                command.EncircleSlot = -1;
            }

            if (command.State == EnemyGroupState.Returning)
            {
                if (observation.HasReachedHome)
                {
                    // TODO: 撤退した部隊は、帰り着いたら補充してから撤退を終える
                    command.State = EnemyGroupState.Waiting;
                    command.IsRetreating = false;
                    _blockedTimes[index] = 0f;
                    return;
                }

                _blockedTimes[index] = observation.IsReturnBlocked ? _blockedTimes[index] + deltaTime : 0f;
                if (_blockedTimes[index] >= RETURN_BLOCKED_DURATION)
                {
                    command.State = EnemyGroupState.Waiting;
                    command.HomePosition = observation.AnchorPosition;
                    command.IsRetreating = false;
                    _hasTemporaryHome[index] = true;
                    _blockedTimes[index] = 0f;
                    return;
                }
            }

            if (command.State == EnemyGroupState.Tracking && ShouldRetreat(index, observation))
            {
                command.State = EnemyGroupState.Returning;
                command.IsRetreating = true;
                _blockedTimes[index] = 0f;
                return;
            }

            if (command.State != EnemyGroupState.Tracking && !command.IsRetreating && observation.IsHomeTracked)
            {
                command.State = EnemyGroupState.Tracking;
                _blockedTimes[index] = 0f;
                return;
            }

            if (command.State == EnemyGroupState.Tracking && !observation.IsHomeTracked)
            {
                command.State = EnemyGroupState.Returning;
                _blockedTimes[index] = 0f;
            }
        }

        /// <summary>
        /// 部隊が撤退するか。補充を受けられて、満員の人数の撤退する損耗の割合以上を失っていれば撤退する。
        /// </summary>
        private bool ShouldRetreat(int index, in EnemySquadObservation observation)
        {
            if (_hasTemporaryHome[index] || !observation.CanSpawn || observation.FullMemberCount <= 0) return false;

            var lost = observation.FullMemberCount - observation.MemberCount;
            return lost >= observation.FullMemberCount * _retreatLossRatio;
        }

        /// <summary>
        /// 使われている部隊の命令から、割り当てている置き場を集める。
        /// </summary>
        private void CollectUsedSlots(IReadOnlyList<EnemySquadObservation> squads)
        {
            var slotCount = _observationState.EncircleSlots.Count;
            if (_usedSlots.Length != slotCount) _usedSlots = new bool[slotCount];

            Array.Clear(_usedSlots, 0, slotCount);
            for (var i = 0; i < squads.Count; i++)
            {
                var slot = _commandState.Squads[i].EncircleSlot;
                if (squads[i].IsActive && slot >= 0 && slot < slotCount) _usedSlots[slot] = true;
            }
        }

        /// <summary>
        /// 置き場を持っていれば、使える置き場はそのまま持ち続け、使えなくなった置き場は手放す。待つ置き場を持っていれば、使える置き場が空いたら移る。
        /// 持っていなければ、空いている使える置き場から、それもなければ待つ置き場から、
        /// 「周の番号 × 90° ＋ 螺旋の中心から見たアンカーの角度との差（ラジアン）」が最も小さいものを受け取る。
        /// 内側の周から埋まり、来た向きから外れた（部隊どうしの道が交差しやすい）置き場は取りにくくなる。
        /// 受け取った置き場は、帰還で手放すまで持ち続ける。
        /// </summary>
        private void UpdateEncircleSlot(in EnemySquadObservation observation, ref EnemySquadCommand command)
        {
            var slots = _observationState.EncircleSlots;
            var lastUsableSlot = _observationState.LastUsableEncircleSlot;
            var slot = command.EncircleSlot;
            if (slot >= slots.Count) slot = -1;

            var isUsable = slot >= 0 && slots[slot].IsUsable;
            if (slot >= 0 && slot <= lastUsableSlot && isUsable) return;

            if (slot >= 0 && (slot <= lastUsableSlot || !isUsable))
            {
                ReleaseSlot(ref command);
                slot = -1;
            }

            var best = FindFreeSlot(observation, 0, lastUsableSlot);
            if (best < 0 && slot >= 0) return;
            if (best < 0) best = FindFreeSlot(observation, lastUsableSlot + 1, slots.Count - 1);
            if (best < 0) return;

            ReleaseSlot(ref command);
            _usedSlots[best] = true;
            command.EncircleSlot = best;
        }

        /// <summary>
        /// 番号 first〜last の置き場のうち、空いていて使えるもので、
        /// 「周の番号 × 90° ＋ 螺旋の中心から見たアンカーの角度との差（ラジアン）」が最も小さいものを返す。なければ -1。
        /// </summary>
        private int FindFreeSlot(in EnemySquadObservation observation, int first, int last)
        {
            var slots = _observationState.EncircleSlots;
            var center = _observationState.EncircleCenter;
            var fromCenter = new Vector2(observation.AnchorPosition.x, observation.AnchorPosition.z) - center;
            var squadAngle = Mathf.Atan2(fromCenter.x, fromCenter.y);
            var best = -1;
            var bestCost = float.MaxValue;
            for (var slot = first; slot <= last; slot++)
            {
                if (_usedSlots[slot] || !slots[slot].IsUsable) continue;

                var difference = slots[slot].Angle - squadAngle;
                var angleDifference = Mathf.Abs(Mathf.Atan2(Mathf.Sin(difference), Mathf.Cos(difference)));
                var cost = slots[slot].Ring * (Mathf.PI * 0.5f) + angleDifference;
                if (cost >= bestCost) continue;

                bestCost = cost;
                best = slot;
            }

            return best;
        }

        /// <summary>
        /// 部隊の置き場を手放す。
        /// </summary>
        private void ReleaseSlot(ref EnemySquadCommand command)
        {
            if (command.EncircleSlot < 0) return;

            if (command.EncircleSlot < _usedSlots.Length) _usedSlots[command.EncircleSlot] = false;
            command.EncircleSlot = -1;
        }
    }
}
