using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// スキルを発動したときの効果1つの定義。SkillData が [SerializeReference] で1つ持ち、効果ごとに必要な値だけを持つ。
    /// 効果の実行は Application の SkillService が効果の型ごとに行う。
    /// </summary>
    [Serializable]
    public abstract class SkillEffectData
    {
    }

    /// <summary>
    /// カメラの位置から視線の向きへ、カプセルで一度だけ削る効果。
    /// </summary>
    [Serializable]
    public sealed class BeamEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("削る形の半径（m）")]
        private float _radius = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("ビームの長さ（m）")]
        private float _length = 30f;

        /// <summary> 削る形の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> ビームの長さ（m） </summary>
        public float Length => _length;
    }

    /// <summary>
    /// カメラの位置を中心に、球で一度だけ削る効果。
    /// </summary>
    [Serializable]
    public sealed class ExplosionEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("削る球の半径（m）")]
        private float _radius = 4f;

        /// <summary> 削る球の半径（m） </summary>
        public float Radius => _radius;
    }

    /// <summary>
    /// HP を回復する効果。最大 HP を超えた分は捨てる。
    /// </summary>
    [Serializable]
    public sealed class HealEffect : SkillEffectData
    {
        [SerializeField, Min(0)]
        [Tooltip("回復する HP")]
        private int _healAmount = 30;

        /// <summary> 回復する HP </summary>
        public int HealAmount => _healAmount;
    }

    /// <summary>
    /// 前後左右へ爆弾を1つずつ投げ、落ちた所で球で削る効果。
    /// </summary>
    [Serializable]
    public sealed class BombEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("爆弾が落ちた所で削る球の半径（m）")]
        private float _radius = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("爆弾を投げる距離（m）")]
        private float _distance = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("爆弾を投げる起点を、プレイヤーの正面へずらす距離（m）")]
        private float _forwardOffset = 7.5f;

        /// <summary> 爆弾が落ちた所で削る球の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> 爆弾を投げる距離（m） </summary>
        public float Distance => _distance;

        /// <summary> 爆弾を投げる起点を、プレイヤーの正面へずらす距離（m） </summary>
        public float ForwardOffset => _forwardOffset;
    }

    /// <summary>
    /// 正面の横長の範囲を、水平のカプセルで一度だけ削る効果。
    /// </summary>
    [Serializable]
    public sealed class SweepEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("削る範囲の横幅（m）")]
        private float _width = 20f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る範囲の奥行き（m）")]
        private float _depth = 10f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る強さが 1m あたりに減る量。普通の素材（コンクリート）は半径まで削れる。硬い素材（鉄筋）は、減衰率 × 半径が 1 を超えると少しだけ削れる")]
        private float _falloff = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("削る範囲の中心を、プレイヤーの正面へずらす距離（m）")]
        private float _forwardOffset = 5f;

        /// <summary> 削る範囲の横幅（m） </summary>
        public float Width => _width;

        /// <summary> 削る範囲の奥行き（m） </summary>
        public float Depth => _depth;

        /// <summary> 削る強さが 1m あたりに減る量 </summary>
        public float Falloff => _falloff;

        /// <summary> 削る範囲の中心を、プレイヤーの正面へずらす距離（m） </summary>
        public float ForwardOffset => _forwardOffset;
    }

    /// <summary>
    /// 移動入力の向き（なければ正面）へ突進し、通る経路を削る効果。
    /// </summary>
    [Serializable]
    public sealed class RushEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("削る形の半径（m）")]
        private float _radius = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("進む距離（m）")]
        private float _distance = 15f;

        [SerializeField, Min(0.01f)]
        [Tooltip("突進の所要時間（秒）")]
        private float _duration = 0.3f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る強さが 1m あたりに減る量。普通の素材（コンクリート）は半径まで削れる。硬い素材（鉄筋）は、減衰率 × 半径が 1 を超えると少しだけ削れる")]
        private float _falloff = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("削る範囲の中心を、プレイヤーの正面へずらす距離（m）")]
        private float _forwardOffset = 5f;

        /// <summary> 削る形の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> 進む距離（m） </summary>
        public float Distance => _distance;

        /// <summary> 突進の所要時間（秒） </summary>
        public float Duration => _duration;

        /// <summary> 削る強さが 1m あたりに減る量 </summary>
        public float Falloff => _falloff;

        /// <summary> 削る範囲の中心を、プレイヤーの正面へずらす距離（m） </summary>
        public float ForwardOffset => _forwardOffset;
    }

    /// <summary>
    /// 上空へ上がって無敵で静止し、視点の先へ止めるまでビームを撃つ効果。もう一度発動すると止めて、発動した地点へ戻る。
    /// </summary>
    [Serializable]
    public sealed class SkyBeamEffect : SkillEffectData
    {
        [SerializeField]
        [Tooltip("上昇先のワールド高度（Y）。今の高さがこれより高ければ上がらない")]
        private float _altitude = 100f;

        [SerializeField, Min(0.01f)]
        [Tooltip("上昇と戻りの所要時間（秒）")]
        private float _riseDuration = 0.7f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る形の半径（m）")]
        private float _radius = 16f;

        [SerializeField, Min(0)]
        [Tooltip("照射中の判定 1 回ごとに使うチャージ量。足りなければ止める")]
        private int _sustainCost = 15;

        [SerializeField, Min(0.05f)]
        [Tooltip("照射中に地形を壊す判定の間隔（秒）。照射の開始時に 1 回目を行う")]
        private float _sustainInterval = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("照射が当たっている体力制の対象（ボスなど）の体力を、1 秒あたりに減らす量")]
        private float _healthDamagePerSecond = 12f;

        /// <summary> 上昇先のワールド高度（Y） </summary>
        public float Altitude => _altitude;

        /// <summary> 上昇と戻りの所要時間（秒） </summary>
        public float RiseDuration => _riseDuration;

        /// <summary> 削る形の半径（m） </summary>
        public float Radius => _radius;

        /// <summary> 照射中の判定 1 回ごとに使うチャージ量 </summary>
        public int SustainCost => _sustainCost;

        /// <summary> 照射中に地形を壊す判定の間隔（秒） </summary>
        public float SustainInterval => _sustainInterval;

        /// <summary> 照射が当たっている体力制の対象の体力を、1 秒あたりに減らす量 </summary>
        public float HealthDamagePerSecond => _healthDamagePerSecond;
    }

    /// <summary>
    /// 長押しの間、視線の先にボクセルを盛る効果。
    /// </summary>
    [Serializable]
    public sealed class MoundEffect : SkillEffectData
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("盛る球の半径（m）")]
        private float _radius = 1.2f;

        /// <summary> 盛る球の半径（m） </summary>
        public float Radius => _radius;
    }

    /// <summary>
    /// 強化型スキルの効果。発動すると、決めた回数だけ攻撃に強化がかかり、回数を使い切ると切れる。
    /// </summary>
    [Serializable]
    public abstract class EnhanceEffect : SkillEffectData
    {
        [SerializeField, Min(1)]
        [Tooltip("強化がかかる攻撃の回数")]
        private int _count = 5;

        /// <summary> 強化がかかる攻撃の回数 </summary>
        public int Count => _count;

        /// <summary> 強化の倍率。倍率を持たない強化は1 </summary>
        public virtual float Scale => 1f;
    }

    /// <summary>
    /// 剣の切断にボクセル破壊の属性を足し、当たったボクセルを切断面で切り分ける強化。
    /// </summary>
    [Serializable]
    public sealed class DestroyEnhanceEffect : EnhanceEffect
    {
    }

    /// <summary>
    /// 剣の切断にシルブレ（装甲を一撃で壊す属性）を足す強化。
    /// </summary>
    [Serializable]
    public sealed class ArmorBreakEnhanceEffect : EnhanceEffect
    {
    }

    /// <summary>
    /// 剣の切断の範囲の幅と届く距離に倍率を掛ける強化。
    /// </summary>
    [Serializable]
    public sealed class RangeEnhanceEffect : EnhanceEffect
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("刃の幅と届く距離に掛ける倍率")]
        private float _scale = 1.5f;

        public override float Scale => _scale;
    }

    /// <summary>
    /// 剣の攻撃間隔に倍率を掛ける強化。
    /// </summary>
    [Serializable]
    public sealed class SpeedEnhanceEffect : EnhanceEffect
    {
        [SerializeField, Min(0.01f)]
        [Tooltip("攻撃間隔に掛ける倍率。1 より小さいと速くなる")]
        private float _scale = 0.5f;

        public override float Scale => _scale;
    }

    /// <summary>
    /// 投げた・撃ったかけらが、当たった所で爆発する強化。
    /// </summary>
    [Serializable]
    public sealed class ExplosiveFragmentEnhanceEffect : EnhanceEffect
    {
    }
}
