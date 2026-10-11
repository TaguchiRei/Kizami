using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 敵の部隊（グループ）ごとの、Application が決めた命令を保持するステート。部隊の番号は Engine 側のグループの番号と同じ。
    /// ApplicationLayer（EnemySquadService）が毎フレーム書き込み、EngineAdapterLayer（EnemySpawnAdapter）がグループを動かす前に読む。
    /// </summary>
    [RegisterBoard(typeof(EnemyBoard))]
    public sealed class EnemySquadCommandState : SceneStateBase, IEnemySquadCommandState
    {
        private EnemySquadCommand[] _squads = Array.Empty<EnemySquadCommand>();

        public IReadOnlyList<EnemySquadCommand> Squads => _squads;

        public override string GetLog()
        {
            var waiting = 0;
            var tracking = 0;
            var returning = 0;
            var retreating = 0;
            foreach (var squad in _squads)
            {
                if (squad.IsRetreating) retreating++;

                switch (squad.State)
                {
                    case EnemyGroupState.Tracking:
                        tracking++;
                        break;
                    case EnemyGroupState.Returning:
                        returning++;
                        break;
                    default:
                        waiting++;
                        break;
                }
            }

            return $"Waiting: {waiting}  \nTracking: {tracking}  \nReturning: {returning}  \nRetreating: {retreating}";
        }

        /// <summary>
        /// 部隊の数を設定する。命令はすべて初期値に戻る。
        /// </summary>
        /// <param name="count">部隊の数。Engine 側のグループの数</param>
        public void SetSquadCount(int count)
        {
            _squads = new EnemySquadCommand[count];
        }

        /// <summary>
        /// 部隊 1 つの命令を設定する。
        /// </summary>
        /// <param name="index">部隊の番号</param>
        /// <param name="command">命令</param>
        public void SetSquad(int index, in EnemySquadCommand command)
        {
            _squads[index] = command;
        }
    }

    /// <summary>
    /// 敵の部隊ごとの命令の読み取り面。
    /// </summary>
    public interface IEnemySquadCommandState : IStateGetter
    {
        /// <summary> 部隊ごとの命令。番号は Engine 側のグループの番号 </summary>
        IReadOnlyList<EnemySquadCommand> Squads { get; }
    }

    /// <summary>
    /// 敵の部隊 1 つへの命令。
    /// </summary>
    public struct EnemySquadCommand
    {
        /// <summary> 待機・追跡・帰還の状態 </summary>
        public EnemyGroupState State;

        /// <summary> 持ち場。帰還の行き先で、待機する位置 </summary>
        public Vector3 HomePosition;

        /// <summary> 撤退中か。撤退は帰還の状態で、スポーン位置へ帰り着くまで追跡に戻らない </summary>
        public bool IsRetreating;

        /// <summary> 追跡中に向かう、プレイヤーを囲む螺旋の上の置き場の番号。持っていなければ -1 </summary>
        public int EncircleSlot;
    }
}
