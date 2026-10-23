namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーにかかる強化の種類。
    /// </summary>
    public enum EnhanceKind
    {
        /// <summary> 剣の切断にボクセル破壊の属性を足す </summary>
        Destroy,

        /// <summary> 剣の切断に装甲を一撃で壊す属性を足す </summary>
        ArmorBreak,

        /// <summary> 剣の切断の範囲の幅と届く距離に倍率を掛ける </summary>
        Range,

        /// <summary> 剣の攻撃間隔に倍率を掛ける </summary>
        Speed,

        /// <summary> 投げた・撃ったかけらが、当たった所で爆発する </summary>
        ExplosiveFragment
    }
}
