namespace Kizami.BlackBoard
{
    /// <summary>
    /// 敵の部隊（グループ）の、持ち場と追跡範囲の関係で決まる状態。Application（EnemySquadService）が決める。
    /// </summary>
    public enum EnemyGroupState
    {
        /// <summary> 持ち場が追跡範囲の外にあり、持ち場で待つ </summary>
        Waiting,

        /// <summary> 持ち場が追跡範囲の中にあり、プレイヤーを追う </summary>
        Tracking,

        /// <summary> 持ち場が追跡範囲から外れ、追跡の間に通った道を逆にたどって持ち場へ戻る </summary>
        Returning
    }
}
