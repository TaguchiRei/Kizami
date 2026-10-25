using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// ボス戦の攻撃が与えるダメージ量。BossInitializer が持ち、攻撃の Adapter へ渡す。
    /// </summary>
    [Serializable]
    public sealed class BossDamageSettings
    {
        [SerializeField, Min(0)]
        [Tooltip("衝撃波に当たったときのダメージ量")]
        private int _shockwave = 5;

        [SerializeField, Min(0)]
        [Tooltip("下部レーザーに当たったときのダメージ量")]
        private int _laser = 5;

        [SerializeField, Min(0)]
        [Tooltip("上部タレットの弾に当たったときのダメージ量")]
        private int _turret = 5;

        [SerializeField, Min(0)]
        [Tooltip("頭部のレーザーに当たったときのダメージ量")]
        private int _headLaser = 10;

        [SerializeField, Min(0)]
        [Tooltip("降下フィニッシャーのビームに当たっているときに、間隔ごとに与えるダメージ量")]
        private int _finisherStrike = 1;

        [SerializeField, Min(0)]
        [Tooltip("円形の空間の外にいるときのダメージ量")]
        private int _arenaOutside = 10;

        [SerializeField, Min(0)]
        [Tooltip("円形の空間の柵のビームに当たっているときに、間隔ごとに与えるダメージ量")]
        private int _arenaBeam = 1;

        [SerializeField, Min(0)]
        [Tooltip("飛んでいる塔がプレイヤーに触れたときのダメージ量")]
        private int _towerToPlayer = 60;

        [SerializeField, Min(0f)]
        [Tooltip("倒れた塔がボスに触れたときの、ボスの体力のダメージ量")]
        private float _towerToBoss = 30f;

        /// <summary> 衝撃波に当たったときのダメージ量 </summary>
        public int Shockwave => _shockwave;

        /// <summary> 下部レーザーに当たったときのダメージ量 </summary>
        public int Laser => _laser;

        /// <summary> 上部タレットの弾に当たったときのダメージ量 </summary>
        public int Turret => _turret;

        /// <summary> 頭部のレーザーに当たったときのダメージ量 </summary>
        public int HeadLaser => _headLaser;

        /// <summary> 降下フィニッシャーのビームに当たっているときに、間隔ごとに与えるダメージ量 </summary>
        public int FinisherStrike => _finisherStrike;

        /// <summary> 円形の空間の外にいるときのダメージ量 </summary>
        public int ArenaOutside => _arenaOutside;

        /// <summary> 円形の空間の柵のビームに当たっているときに、間隔ごとに与えるダメージ量 </summary>
        public int ArenaBeam => _arenaBeam;

        /// <summary> 飛んでいる塔がプレイヤーに触れたときのダメージ量 </summary>
        public int TowerToPlayer => _towerToPlayer;

        /// <summary> 倒れた塔がボスに触れたときの、ボスの体力のダメージ量 </summary>
        public float TowerToBoss => _towerToBoss;
    }
}
