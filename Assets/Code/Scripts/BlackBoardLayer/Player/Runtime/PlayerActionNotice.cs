namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーの出来事の種類。
    /// </summary>
    public enum PlayerActionKind
    {
        /// <summary> スキルを発動した。名前にスキル名が入る </summary>
        SkillUsed,

        /// <summary> スキルの発動でチャージを消費した。量に消費したチャージ量が入る。SkillUsed の直後に流れる </summary>
        ChargeSpent,

        /// <summary> 通常攻撃（近接切断）を振った </summary>
        MeleeSwung,

        /// <summary> 地面からジャンプした </summary>
        Jumped,

        /// <summary> 壁走りに入った </summary>
        WallRunStarted,

        /// <summary> 壁走りの最中にジャンプして、壁を蹴った </summary>
        WallKicked,

        /// <summary> F キーで飛び移りの目印へ瞬間移動した </summary>
        Flew
    }

    /// <summary>
    /// プレイヤーの出来事1件の通知。
    /// </summary>
    public readonly struct PlayerActionNotice
    {
        /// <summary> 出来事の種類 </summary>
        public PlayerActionKind Kind { get; }

        /// <summary> 出来事に付く名前。SkillUsed のときだけスキル名が入り、それ以外は空 </summary>
        public string Name { get; }

        /// <summary> 出来事に付く量。ChargeSpent のときだけ消費したチャージ量が入り、それ以外は0 </summary>
        public int Amount { get; }

        /// <param name="kind">出来事の種類</param>
        /// <param name="name">出来事に付く名前</param>
        /// <param name="amount">出来事に付く量</param>
        public PlayerActionNotice(PlayerActionKind kind, string name = "", int amount = 0)
        {
            Kind = kind;
            Name = name;
            Amount = amount;
        }
    }
}
