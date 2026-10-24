using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// 体力制の破壊対象で、当たりの種類ごとに減る体力の量。
    /// </summary>
    [Serializable]
    public sealed class TargetDamageSettings
    {
        [SerializeField, Min(0f)]
        [Tooltip("通常攻撃1回（1回の振り）で減る体力")]
        private float _meleeDamage = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("スキル1回（1回の発動）で減る体力")]
        private float _skillDamage = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("落ちてくる塊の体積1m³あたりで減る体力")]
        private float _collapseDamagePerCubicMeter = 2f;

        public float MeleeDamage => _meleeDamage;

        public float SkillDamage => _skillDamage;

        public float CollapseDamagePerCubicMeter => _collapseDamagePerCubicMeter;
    }
}
