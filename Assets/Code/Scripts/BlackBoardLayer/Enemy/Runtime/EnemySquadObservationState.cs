using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 敵の部隊（グループ）ごとの、Engine 側で動かした結果を保持するステート。部隊の番号は Engine 側のグループの番号と同じ。
    /// EngineAdapterLayer（EnemySpawnAdapter）が、グループを動かす Job の完了後に毎フレーム書き込む。
    /// </summary>
    [RegisterBoard(typeof(EnemyBoard))]
    public sealed class EnemySquadObservationState : SceneStateBase, IEnemySquadObservationState
    {
        private EnemySquadObservation[] _squads = Array.Empty<EnemySquadObservation>();

        public IReadOnlyList<EnemySquadObservation> Squads => _squads;

        public override string GetLog()
        {
            var active = 0;
            var arrived = 0;
            foreach (var squad in _squads)
            {
                if (!squad.IsActive) continue;

                active++;
                if (squad.HasArrived) arrived++;
            }

            return $"Squads: {active} / {_squads.Length}  \nArrived: {arrived}";
        }

        /// <summary>
        /// 部隊の数を設定する。観測はすべて初期値に戻る。
        /// </summary>
        /// <param name="count">部隊の数。Engine 側のグループの数</param>
        public void SetSquadCount(int count)
        {
            _squads = new EnemySquadObservation[count];
        }

        /// <summary>
        /// 部隊 1 つの観測を設定する。
        /// </summary>
        /// <param name="index">部隊の番号</param>
        /// <param name="observation">観測</param>
        public void SetSquad(int index, in EnemySquadObservation observation)
        {
            _squads[index] = observation;
        }
    }

    /// <summary>
    /// 敵の部隊ごとの観測の読み取り面。
    /// </summary>
    public interface IEnemySquadObservationState : IStateGetter
    {
        /// <summary> 部隊ごとの観測。番号は Engine 側のグループの番号 </summary>
        IReadOnlyList<EnemySquadObservation> Squads { get; }
    }

    /// <summary>
    /// 敵の部隊 1 つの観測。
    /// </summary>
    public struct EnemySquadObservation
    {
        /// <summary> 使われているか。メンバーが全員ステージから消えたら false </summary>
        public bool IsActive;

        /// <summary> アンカー（部隊の先頭の仮想の隊長）の位置 </summary>
        public Vector3 AnchorPosition;

        /// <summary> メンバーの数。倒れたメンバーも、隊列を詰めるまでは数える </summary>
        public int MemberCount;

        /// <summary> 持ち場が追跡範囲（距離マップを計算した範囲）の中にあるか </summary>
        public bool IsHomeTracked;

        /// <summary> アンカーがプレイヤーを囲む螺旋の置き場に着いているか </summary>
        public bool HasArrived;
    }
}
