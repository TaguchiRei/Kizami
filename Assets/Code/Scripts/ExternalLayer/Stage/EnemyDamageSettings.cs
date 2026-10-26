using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// 敵の攻撃が与えるダメージ量。EnemyInitializer が持ち、敵の Adapter へ渡す。
    /// </summary>
    [Serializable]
    public sealed class EnemyDamageSettings
    {
        [SerializeField, Min(0)]
        [Tooltip("アタッカーの弾がプレイヤーに当たったときのダメージ量")]
        private int _shot = 5;

        [SerializeField, Min(0)]
        [Tooltip("フィニッシャーのビームに当たっているときに、間隔ごとに与えるダメージ量")]
        private int _finisherBeam = 1;

        /// <summary> アタッカーの弾がプレイヤーに当たったときのダメージ量 </summary>
        public int Shot => _shot;

        /// <summary> フィニッシャーのビームに当たっているときに、間隔ごとに与えるダメージ量 </summary>
        public int FinisherBeam => _finisherBeam;
    }
}
