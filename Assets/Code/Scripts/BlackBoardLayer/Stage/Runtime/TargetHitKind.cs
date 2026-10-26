namespace Kizami.BlackBoard
{
    /// <summary>
    /// 体力制の破壊対象への当たりの種類。EngineAdapterLayer（HealthTarget）が報告し、ApplicationLayer（TargetHealthService）が減らす量に直す。
    /// </summary>
    public enum TargetHitKind
    {
        /// <summary> 通常攻撃 1 回（1 回の振り）。大きさは使わない </summary>
        Melee,

        /// <summary> スキル 1 回（1 回の発動）。大きさは使わない </summary>
        Skill,

        /// <summary> 落ちてくる塊。大きさは塊の体積（m³） </summary>
        Collapse,

        /// <summary> 攻撃側が量を決めたダメージ。大きさは減らす量 </summary>
        Direct,
    }
}
