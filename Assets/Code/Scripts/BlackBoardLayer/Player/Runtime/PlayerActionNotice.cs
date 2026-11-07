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
        WallKicked
    }

    /// <summary>
    /// 発動したスキルの効果の分類。効果音など、効果ごとに演出を分けたい購読者が使う。
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary> 下の分類以外の効果 </summary>
        Common,

        /// <summary> 攻走の突進 </summary>
        Rush,

        /// <summary> 羽天轟号 </summary>
        SkyBeam,

        /// <summary> 回復 </summary>
        Heal
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

        /// <summary> 発動したスキルの効果の分類。SkillUsed のときだけ入り、それ以外は Common </summary>
        public SkillEffectKind SkillEffect { get; }

        /// <param name="kind">出来事の種類</param>
        /// <param name="name">出来事に付く名前</param>
        /// <param name="amount">出来事に付く量</param>
        /// <param name="skillEffect">発動したスキルの効果の分類</param>
        public PlayerActionNotice(PlayerActionKind kind, string name = "", int amount = 0,
            SkillEffectKind skillEffect = SkillEffectKind.Common)
        {
            Kind = kind;
            Name = name;
            Amount = amount;
            SkillEffect = skillEffect;
        }
    }
}
