using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// チャージを消費して発動するスキル 1 つの定義。効果の種類と、消費量、削る形の大きさ、装甲に与えるダメージを持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillData", menuName = "Kizami/Player/SkillData")]
    public sealed class SkillData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("HUD に出す名前")]
        private string _displayName = "Skill";

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

        [SerializeField, Min(0)]
        [Tooltip("装甲のパネル 1 枚に与えるダメージ")]
        private int _armorDamage = 3;

        /// <summary> HUD に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> 発動したときの効果 </summary>
        public SkillEffect Effect => _effect;

        /// <summary> 発動に使うチャージ量 </summary>
        public int Cost => _cost;

        /// <summary> 削る形の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> ビームの長さ（m） </summary>
        public float Length => _length;

        /// <summary> 装甲のパネル 1 枚に与えるダメージ </summary>
        public int ArmorDamage => _armorDamage;
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
    }
}
