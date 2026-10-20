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

        [SerializeField, TextArea(2, 4)]
        [Tooltip("スキル一覧に出す説明文。空なら出さない")]
        private string _description;

        [SerializeField]
        [Tooltip("保存データでスキルを見分ける ID。並びを変えても装備が残るよう、番号ではなく ID で保存する。空ならアセットの名前を使う")]
        private string _id;

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

        [SerializeField, Min(0f)]
        [Tooltip("範囲の中心を、プレイヤーの正面へずらす距離（m）。崩爆は爆弾を投げる起点、衝波と攻走は削る範囲の中心をずらす")]
        private float _forwardOffset;

        [SerializeField, Min(0.01f)]
        [Tooltip("突進の所要時間（秒）。攻走でだけ使う。進む距離は飛距離、削る半径は半径を使う")]
        private float _duration = 0.3f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る強さが 1m あたりに減る量。普通の素材（コンクリート）は半径まで削れる。硬い素材（鉄筋）は、減衰率 × 半径が 1 を超えると少しだけ削れる。衝波と攻走でだけ使う")]
        private float _falloff = 0.5f;

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

        /// <summary> HUD に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> 保存データでスキルを見分ける ID。未設定ならアセット名 </summary>
        public string Id => string.IsNullOrEmpty(_id) ? name : _id;

        /// <summary> スキル一覧に出す説明文。なければ空 </summary>
        public string Description => _description;

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

        /// <summary> 範囲の中心を、プレイヤーの正面へずらす距離（m） </summary>
        public float ForwardOffset => _forwardOffset;

        /// <summary> 突進の所要時間（秒） </summary>
        public float Duration => _duration;

        /// <summary> 削る強さが 1m あたりに減る量 </summary>
        public float Falloff => _falloff;

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

        /// <summary> 強化型スキルか </summary>
        public bool IsEnhancement => _effect >= SkillEffect.EnhanceDestroy && _effect <= SkillEffect.ExplosiveFragment;
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

        /// <summary> 長押しの間、視線の先にボクセルを盛る。アセットが値で保存しているので、番号を変えない </summary>
        Mound,

        /// <summary> 前後左右へ爆弾を1つずつ投げ、落ちた所で球で削る </summary>
        Bomb,

        /// <summary> 正面の横長の範囲を、水平のカプセルで一度だけ削る </summary>
        Sweep,

        /// <summary> 移動入力の向き（なければ正面）へ突進し、通る経路を削る </summary>
        Rush,
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
