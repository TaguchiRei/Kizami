namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 砕けたものの破片を出すプールの種類。種類ごとに破片のプレハブとプールが別になる。
    /// </summary>
    public enum ShardPoolKind
    {
        /// <summary> プールを使わない </summary>
        None = 0,

        /// <summary> 窓のガラス </summary>
        Glass = 1,

        /// <summary> ディフェンダーの球のバリア </summary>
        Barrier = 2,

        /// <summary> 装甲のパネル </summary>
        Armor = 3
    }
}
