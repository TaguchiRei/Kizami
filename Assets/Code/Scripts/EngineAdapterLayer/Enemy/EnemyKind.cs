namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 雑魚敵の種類。種類ごとに体のプレハブと固有のアクションが違う。
    /// </summary>
    public enum EnemyKind
    {
        /// <summary> 攻撃する敵。体の上のタレットから弾を撃つ </summary>
        Attacker,

        /// <summary> シールドを持つ敵。グループの中心でバリアを張る </summary>
        Defender,

        /// <summary> 吸収型の敵。仲間を吸収してビームを撃つ </summary>
        Finisher
    }
}
