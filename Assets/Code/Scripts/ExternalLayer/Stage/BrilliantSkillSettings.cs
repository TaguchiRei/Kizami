using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// Brilliant Skill の判定の条件と追加スコア。値は仮で、区間14C の街のマップができてから調整する。
    /// </summary>
    [Serializable]
    public sealed class BrilliantSkillSettings
    {
        [SerializeField, Min(1)]
        [Tooltip("連続キル：成立に必要な、続けて倒した敵の数")]
        private int _rampageDefeatCount = 10;

        [SerializeField, Min(0.1f)]
        [Tooltip("連続キル：前に倒してからこの時間（秒）以内なら、続けて倒したことになる")]
        private float _rampageInterval = 3f;

        [SerializeField, Min(1)]
        [Tooltip("倒壊：成立に必要な、倒れた建物で巻き込んだ敵の数")]
        private int _collapseDefeatCount = 10;

        [SerializeField, Min(0.1f)]
        [Tooltip("倒壊：建物が倒れ始めてから、巻き込んだ敵を数える時間（秒）。倒れきるまでの時間より長くする")]
        private float _collapseWindow = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("倒壊：倒れた建物の範囲に足す余白（m）。建物は倒れながら外へ広がる為")]
        private float _collapseMargin = 5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("一撃：成立に必要な、1回の攻撃で壊したボクセルの体積（m3）")]
        private float _shatterVolume = 30f;

        [SerializeField, Min(0.1f)]
        [Tooltip("投げた言葉が当たったときに、ボクセルを削り敵を倒す球の半径（m）")]
        private float _letterImpactRadius = 4f;

        [SerializeField, Min(0)]
        [Tooltip("連続キルの追加スコア")]
        private int _rampageScore = 100;

        [SerializeField, Min(0)]
        [Tooltip("倒壊の追加スコア")]
        private int _collapseScore = 100;

        [SerializeField, Min(0)]
        [Tooltip("一撃の追加スコア")]
        private int _shatterScore = 100;

        [SerializeField]
        [Tooltip("連続キルで出す言葉")]
        private string _rampageWord = "RAMPAGE";

        [SerializeField]
        [Tooltip("倒壊で出す言葉")]
        private string _collapseWord = "COLLAPSE";

        [SerializeField]
        [Tooltip("一撃で出す言葉")]
        private string _shatterWord = "SHATTER";

        public int RampageDefeatCount => _rampageDefeatCount;

        public float RampageInterval => _rampageInterval;

        public int CollapseDefeatCount => _collapseDefeatCount;

        public float CollapseWindow => _collapseWindow;

        public float CollapseMargin => _collapseMargin;

        public float ShatterVolume => _shatterVolume;

        public float WordImpactRadius => _letterImpactRadius;

        /// <summary>
        /// 種類ごとの追加スコアを返す。
        /// </summary>
        public int GetScore(BrilliantSkillKind kind)
        {
            return kind switch
            {
                BrilliantSkillKind.Rampage => _rampageScore,
                BrilliantSkillKind.Collapse => _collapseScore,
                BrilliantSkillKind.Shatter => _shatterScore,
                _ => 0
            };
        }

        /// <summary>
        /// 種類ごとの、文字で出す言葉を返す。
        /// </summary>
        public string GetWord(BrilliantSkillKind kind)
        {
            return kind switch
            {
                BrilliantSkillKind.Rampage => _rampageWord,
                BrilliantSkillKind.Collapse => _collapseWord,
                BrilliantSkillKind.Shatter => _shatterWord,
                _ => string.Empty
            };
        }
    }
}
