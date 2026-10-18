namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの武装の種類。同じ種類の武装が複数あれば、順番に使う。
    /// </summary>
    public enum BossWeaponKind
    {
        /// <summary> 上部タレット。緩やかに追尾する射撃 </summary>
        Turret,

        /// <summary> 下部レーザー。ゆっくり回転する細いレーザー </summary>
        Laser,

        /// <summary> 脚のミサイル。プレイヤーのいる地点の地面を狙う </summary>
        Missile,

        /// <summary> アウトリガー。足踏みで広がる衝撃波の輪 </summary>
        Outrigger
    }
}
