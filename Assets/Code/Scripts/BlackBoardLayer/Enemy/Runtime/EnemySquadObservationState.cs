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
        private EnemyEncircleSlot[] _encircleSlots = Array.Empty<EnemyEncircleSlot>();

        public IReadOnlyList<EnemySquadObservation> Squads => _squads;
        public IReadOnlyList<EnemyEncircleSlot> EncircleSlots => _encircleSlots;
        public Vector2 EncircleCenter { get; private set; }
        public int LastUsableEncircleSlot { get; private set; } = -1;

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

            return $"Squads: {active} / {_squads.Length}  \nArrived: {arrived}  \nLastUsableEncircleSlot: {LastUsableEncircleSlot}";
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
        /// 置き場の数を設定する。置き場はすべて初期値に戻る。
        /// </summary>
        /// <param name="count">プレイヤーを囲む螺旋の上の置き場の数</param>
        public void SetEncircleSlotCount(int count)
        {
            _encircleSlots = new EnemyEncircleSlot[count];
        }

        /// <summary>
        /// 置き場の螺旋の中心と、使える置き場のうち最も外の置き場の番号を設定する。
        /// </summary>
        /// <param name="center">螺旋の中心（水平の x, z）。置き場の位置を求めたときのプレイヤーの位置</param>
        /// <param name="lastUsableSlot">使える置き場のうち最も外の置き場の番号。なければ -1</param>
        public void SetEncircleArea(Vector2 center, int lastUsableSlot)
        {
            EncircleCenter = center;
            LastUsableEncircleSlot = lastUsableSlot;
        }

        /// <summary>
        /// 置き場 1 つを設定する。
        /// </summary>
        /// <param name="index">置き場の番号</param>
        /// <param name="slot">置き場</param>
        public void SetEncircleSlot(int index, in EnemyEncircleSlot slot)
        {
            _encircleSlots[index] = slot;
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

        /// <summary> プレイヤーを囲む螺旋の上の置き場。番号の小さい順に内側から並ぶ </summary>
        IReadOnlyList<EnemyEncircleSlot> EncircleSlots { get; }

        /// <summary> 置き場の螺旋の中心（水平の x, z） </summary>
        Vector2 EncircleCenter { get; }

        /// <summary> 使える置き場のうち最も外の置き場の番号。これより外の置き場は、置き場を持てない部隊が待つ置き場。なければ -1 </summary>
        int LastUsableEncircleSlot { get; }
    }

    /// <summary>
    /// プレイヤーを囲む螺旋の上の置き場 1 つ。部隊の目標位置になる。
    /// </summary>
    public struct EnemyEncircleSlot
    {
        /// <summary> 立てる列へずらした位置（水平の x, z）。使えない置き場は NaN </summary>
        public Vector2 Position;

        /// <summary> 螺旋の内側から数えた周の番号 </summary>
        public int Ring;

        /// <summary> ずらす前の位置の、螺旋の中心から見た角度（ラジアン）。+Z が 0 で、+X へ向かって増える </summary>
        public float Angle;

        /// <summary> 使えるか。立てる列が周りにない置き場は使えない </summary>
        public bool IsUsable => !float.IsNaN(Position.x);
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

        /// <summary> 帰還中のアンカーが、帰りの道筋をたどり終えて持ち場に着いたか </summary>
        public bool HasReachedHome;

        /// <summary> 帰還中のアンカーが、床に乗れないか止まっていて進めないか </summary>
        public bool IsReturnBlocked;
    }
}
