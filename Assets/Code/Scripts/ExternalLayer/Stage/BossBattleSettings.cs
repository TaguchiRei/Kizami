using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// ボス戦の行動の回数・間隔・待機の値と、足踏みを始めてからの猶予。
    /// </summary>
    [Serializable]
    public sealed class BossBattleSettings
    {
        [SerializeField, Min(0f)]
        [Tooltip("飛来で着いてから、足踏みを始めるまでの時間（秒）。脚が足を置き直す間は攻撃しない")]
        private float _landingPause = 1f;

        [SerializeField, Min(1)]
        [Tooltip("直接戦闘のコア露出の行動で、降下フィニッシャーを続けて出す数")]
        private int _meleeStrikeCount = 3;

        [SerializeField, Min(0.1f)]
        [Tooltip("直接戦闘のコア露出の行動で、降下フィニッシャーを出す間隔（秒）")]
        private float _meleeStrikeInterval = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("塔を投げてから、次の行動に移るまでの待機（秒）。この間は何もしない")]
        private float _towerRest = 10f;

        [SerializeField, Min(1)]
        [Tooltip("最終の段階で、コアを出す攻撃の前に足踏みする回数")]
        private int _finalStomps = 3;

        [SerializeField, Min(0f)]
        [Tooltip("足踏みを始めてから、無敵を解いて落ちてくる塊のダメージを数え始めるまでの猶予（秒）。飛来で崩れた塊が落ちきるのを待つ")]
        private float _collapseGrace = 2f;

        public float LandingPause => _landingPause;

        public int MeleeStrikeCount => _meleeStrikeCount;

        public float MeleeStrikeInterval => _meleeStrikeInterval;

        public float TowerRest => _towerRest;

        public int FinalStomps => _finalStomps;

        public float CollapseGrace => _collapseGrace;
    }
}
