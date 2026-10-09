namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループの、持ち場と追跡範囲の関係で決まる状態。
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
