using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// チャージを消費して発動するスキル1つの定義。効果の種類と、消費量、削る形の大きさ、回復量、強化の続き方と倍率を持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillData", menuName = "Kizami/Player/SkillData")]
    public sealed class SkillData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("HUD に出す名前")]
        private string _displayName = "Skill";

        [SerializeField]
        [Tooltip("HUD に出すアイコン")]
        private Texture2D _icon;

        [SerializeField]
        [Tooltip("発動したときの効果")]
        private SkillEffect _effect;

        [SerializeField, Min(0)]
        [Tooltip("発動に使うチャージ量")]
        private int _cost = 30;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る形の半径（m）")]
        private float _radius = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("ビームの長さ（m）。ビームでだけ使う")]
        private float _length = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("爆弾を投げる距離（m）。爆弾でだけ使う")]
        private float _distance = 5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る範囲の横幅（m）。衝波でだけ使う")]
        private float _width = 4f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る範囲の奥行き（m）。衝波でだけ使う")]
        private float _depth = 2f;

        [SerializeField, Min(0)]
        [Tooltip("回復する HP。回復でだけ使う")]
        private int _healAmount = 30;

        [SerializeField]
        [Tooltip("強化が切れる条件。強化型でだけ使う")]
        private EnhanceLimit _enhanceLimit;

        [SerializeField, Min(0f)]
        [Tooltip("強化が続く実時間（秒）。時間で切れる強化でだけ使う")]
        private float _enhanceDuration = 10f;

        [SerializeField, Min(1)]
        [Tooltip("強化が続く攻撃の回数。回数で切れる強化でだけ使う")]
        private int _enhanceCount = 5;

        [SerializeField, Min(0.01f)]
        [Tooltip("強化の倍率。範囲アップは刃の幅と届く距離に、速さアップは攻撃間隔に掛ける")]
        private float _enhanceScale = 1.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("強化の攻撃間隔の倍率。範囲と速さの強化でだけ使う")]
        private float _enhanceIntervalScale = 1f;

        /// <summary> HUD に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> HUD に出すアイコン </summary>
        public Texture2D Icon => _icon;

        /// <summary> 発動したときの効果 </summary>
        public SkillEffect Effect => _effect;

        /// <summary> 発動に使うチャージ量 </summary>
        public int Cost => _cost;

        /// <summary> 削る形の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> ビームの長さ（m） </summary>
        public float Length => _length;

        /// <summary> 爆弾を投げる距離（m） </summary>
        public float Distance => _distance;

        /// <summary> 削る範囲の横幅（m） </summary>
        public float Width => _width;

        /// <summary> 削る範囲の奥行き（m） </summary>
        public float Depth => _depth;

        /// <summary> 回復する HP </summary>
        public int HealAmount => _healAmount;

        /// <summary> 強化が切れる条件 </summary>
        public EnhanceLimit EnhanceLimit => _enhanceLimit;

        /// <summary> 強化が続く実時間（秒） </summary>
        public float EnhanceDuration => _enhanceDuration;

        /// <summary> 強化が続く攻撃の回数 </summary>
        public int EnhanceCount => _enhanceCount;

        /// <summary> 強化の倍率 </summary>
        public float EnhanceScale => _enhanceScale;

        /// <summary> 強化の攻撃間隔の倍率 </summary>
        public float EnhanceIntervalScale => _enhanceIntervalScale;

        /// <summary> 強化型スキルか </summary>
        public bool IsEnhancement => _effect >= SkillEffect.EnhanceDestroy && _effect <= SkillEffect.EnhanceRangeSpeed;
    }

    /// <summary>
    /// スキルを発動したときの効果。
    /// </summary>
    public enum SkillEffect
    {
        /// <summary> カメラの位置から視線の向きへ、カプセルで一度だけ削る </summary>
        Beam,

        /// <summary> カメラの位置を中心に、球で一度だけ削る </summary>
        Explosion,

        /// <summary> HP を回復する。最大 HP を超えた分は捨てる </summary>
        Heal,

        /// <summary> 強化型。剣の切断にボクセル破壊の属性を足し、当たったボクセルを切断面で切り分ける </summary>
        EnhanceDestroy,

        /// <summary> 強化型。剣の切断にシルブレ（装甲を一撃で壊す属性）を足す </summary>
        EnhanceArmorBreak,

        /// <summary> 強化型。剣の切断の範囲の幅と届く距離に倍率を掛ける </summary>
        EnhanceRange,

        /// <summary> 強化型。剣の攻撃間隔に倍率を掛ける </summary>
        EnhanceSpeed,

        /// <summary> 強化型。投げた・撃ったかけらが、当たった所で爆発する </summary>
        ExplosiveFragment,

        /// <summary> 強化型。剣の切断の範囲に倍率を掛け、攻撃間隔にも倍率を掛ける </summary>
        EnhanceRangeSpeed,

        /// <summary> 前後左右へ爆弾を1つずつ投げ、落ちた所で球で削る </summary>
        Bomb,

        /// <summary> 正面の横長の範囲を、水平のカプセルで一度だけ削る </summary>
        Sweep,
    }

    /// <summary>
    /// 強化型スキルの効果が切れる条件。
    /// </summary>
    public enum EnhanceLimit
    {
        /// <summary> 決めた実時間が経つと切れる </summary>
        Duration,

        /// <summary> プレイヤーが決めた回数だけ攻撃すると切れる </summary>
        Count,
    }
}
