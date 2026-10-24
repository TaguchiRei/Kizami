using UnityEngine;
using UsefulToolkit.Attributes;

namespace Kizami.External
{
    /// <summary>
    /// チャージを消費して発動するスキル1つの定義。共通の項目（系統・名前・説明・ID・アイコン・腕のアニメーション・消費量）と、効果の種類ごとの設定（SkillEffectData）を持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillData", menuName = "Kizami/Player/SkillData")]
    public sealed class SkillData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("系統。None 以外なら、表示名の前に「系統・」が付く")]
        private SkillSchool _school;

        [SerializeField]
        [Tooltip("系統を除いた名前")]
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

        [SerializeReference, SubclassSelector]
        [Tooltip("発動したときの効果。選んだ効果の種類に応じた設定が出る")]
        private SkillEffectData _effect;

        [SerializeField]
        [Tooltip("発動したときに腕で再生するアニメーション。Player.controller の Trigger の名前と同じにする。空なら再生しない")]
        private string _armAnimation;

        [SerializeField, Min(0)]
        [Tooltip("発動に使うチャージ量")]
        private int _cost = 30;

        /// <summary> HUD や一覧に出す名前。系統があれば「系統・名前」の形にする </summary>
        public string DisplayName => _school == SkillSchool.None ? _displayName : $"{GetSchoolName(_school)}・{_displayName}";

        private static string GetSchoolName(SkillSchool school)
        {
            return school switch
            {
                SkillSchool.Body => "体術",
                SkillSchool.Blade => "刀術",
                SkillSchool.Earth => "土術",
                SkillSchool.Fire => "火術",
                SkillSchool.FireUltimate => "火術奥義",
                _ => string.Empty
            };
        }

        /// <summary> 保存データでスキルを見分ける ID。未設定ならアセット名 </summary>
        public string Id => string.IsNullOrEmpty(_id) ? name : _id;

        /// <summary> スキル一覧に出す説明文。なければ空 </summary>
        public string Description => _description;

        /// <summary> HUD に出すアイコン </summary>
        public Texture2D Icon => _icon;

        /// <summary> 発動したときの効果 </summary>
        public SkillEffectData Effect => _effect;

        /// <summary> 発動したときに腕で再生するアニメーションの Trigger 名。空なら再生しない </summary>
        public string ArmAnimation => _armAnimation;

        /// <summary> 発動に使うチャージ量 </summary>
        public int Cost => _cost;
    }

    /// <summary>
    /// スキルの系統。表示名の前に付く。
    /// </summary>
    public enum SkillSchool
    {
        /// <summary> 系統を持たない。テスト用や試作のスキル </summary>
        None,

        /// <summary> 体術 </summary>
        Body,

        /// <summary> 刃術 </summary>
        Blade,

        /// <summary> 土術 </summary>
        Earth,

        /// <summary> 火術 </summary>
        Fire,

        /// <summary> 火術奥義 </summary>
        FireUltimate
    }
}
