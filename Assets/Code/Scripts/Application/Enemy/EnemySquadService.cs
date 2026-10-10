using System;
using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 敵の部隊（グループ）ごとに、待機・追跡・帰還の状態と持ち場を決めるユースケース。
    /// Engine 側の観測（EnemySquadObservationState）を読んで判断し、命令（EnemySquadCommandState）に書く。Step は EnemySpawnAdapter がグループを動かす前に毎フレーム呼ぶ。
    /// 持ち場が追跡範囲に入ったら追跡、外れたら帰還にし、帰還で持ち場に着いたら待機にする。
    /// 帰還の途中で進めない状態が RETURN_BLOCKED_DURATION 続いたら、アンカーの位置を新しい持ち場にして待機にする。元の持ち場へ移さないのは、持ち場が埋まっていることがあり、プレイヤーが敵を分断する遊び（橋を切るなど）を残す為。
    /// </summary>
    public sealed class EnemySquadService : IDisposable
    {
        /// <summary> 帰還の途中で進めない状態がこの時間（秒）続いたら、その位置を新しい持ち場にする </summary>
        private const float RETURN_BLOCKED_DURATION = 5f;

        private readonly EnemySquadCommandState _commandState = new();

        private IEnemySquadObservationState _observationState;
        private IDisposable _observationStateWaiter;

        /// <summary> 部隊ごとの、持ち場を決めたか。部隊が出たあとの最初の Step で、アンカーの位置を持ち場にする </summary>
        private bool[] _hasHome = Array.Empty<bool>();

        /// <summary> 部隊ごとの、帰還の途中で進めない状態が続いている時間（秒） </summary>
        private float[] _blockedTimes = Array.Empty<float>();

        /// <summary>
        /// EnemySquadCommandState を EnemyBoard へ登録する。部隊の数は、EnemySquadObservationState が登録されたときにそろえる。
        /// </summary>
        /// <param name="blackBoard">EnemySquadCommandState の登録先と、EnemySquadObservationState の取得元</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public void Initialize(IBlackBoard blackBoard, int sceneId)
        {
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
                },
                invokeIfRegistered: true);
        }

        /// <summary>
        /// 部隊ごとに、観測から状態と持ち場を決めて命令に書く。
        /// </summary>
        /// <param name="deltaTime">前の Step からの経過時間（秒）</param>
        public void Step(float deltaTime)
        {
            if (_observationState == null) return;

            var squads = _observationState.Squads;
            for (var i = 0; i < squads.Count; i++)
            {
                var observation = squads[i];
                if (!observation.IsActive) continue;

                var command = _commandState.Squads[i];
                Decide(i, observation, ref command, deltaTime);
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
                command.HomePosition = observation.AnchorPosition;
            }

            if (command.State == EnemyGroupState.Returning)
            {
                if (observation.HasReachedHome)
                {
                    command.State = EnemyGroupState.Waiting;
                    _blockedTimes[index] = 0f;
                    return;
                }

                _blockedTimes[index] = observation.IsReturnBlocked ? _blockedTimes[index] + deltaTime : 0f;
                if (_blockedTimes[index] >= RETURN_BLOCKED_DURATION)
                {
                    command.State = EnemyGroupState.Waiting;
                    command.HomePosition = observation.AnchorPosition;
                    _blockedTimes[index] = 0f;
                    return;
                }
            }

            if (command.State != EnemyGroupState.Tracking && observation.IsHomeTracked)
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
    }
}
