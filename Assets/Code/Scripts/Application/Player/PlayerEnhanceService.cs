using System.Collections.Generic;
using Kizami.External;
using UnityEngine;

namespace Kizami.Application
{
    /// <summary>
    /// 強化型スキルの効果を、効果の種類ごとに持つユースケース。
    /// 効果は、実時間で決めた時間が経つか、プレイヤーが決めた回数だけ攻撃すると切れる。同じ種類を重ねて使うと最初から数え直し、違う種類は同時にかかる。
    /// </summary>
    public sealed class PlayerEnhanceService
    {
        private readonly Dictionary<SkillEffect, Enhancement> _enhancements = new();

        /// <summary>
        /// スキルの強化を、最初から数え直してかける。
        /// </summary>
        /// <param name="skill">かける強化型スキル</param>
        public void Begin(SkillData skill)
        {
            _enhancements[skill.Effect] = skill.EnhanceLimit == EnhanceLimit.Count
                ? new Enhancement(double.PositiveInfinity, skill.EnhanceCount, skill.EnhanceScale)
                : new Enhancement(Time.unscaledTimeAsDouble + skill.EnhanceDuration, -1, skill.EnhanceScale);
        }

        /// <summary>
        /// 強化がかかっているかを返す。回数は使わない。
        /// </summary>
        /// <param name="effect">強化の種類</param>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ 1</param>
        public bool IsActive(SkillEffect effect, out float scale)
        {
            scale = 1f;
            if (!_enhancements.TryGetValue(effect, out var enhancement)) return false;

            if (!enhancement.IsActive(Time.unscaledTimeAsDouble))
            {
                _enhancements.Remove(effect);
                return false;
            }

            scale = enhancement.Scale;
            return true;
        }

        /// <summary>
        /// 攻撃を 1 回行うときに呼び、強化がかかっていれば、回数で切れる強化の残りを 1 減らす。
        /// </summary>
        /// <param name="effect">強化の種類</param>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ 1</param>
        /// <returns>この攻撃に強化がかかるなら true</returns>
        public bool TryUse(SkillEffect effect, out float scale)
        {
            if (!IsActive(effect, out scale)) return false;

            var enhancement = _enhancements[effect];
            if (enhancement.RemainingCount < 0) return true;

            enhancement.RemainingCount--;
            if (enhancement.RemainingCount > 0)
            {
                _enhancements[effect] = enhancement;
            }
            else
            {
                _enhancements.Remove(effect);
            }

            return true;
        }

        /// <summary>
        /// かかっている 1 つの強化。時間で切れる強化は残りの回数を負にし、回数で切れる強化は終わる時刻を無限にする。
        /// </summary>
        private struct Enhancement
        {
            /// <summary> 切れる時刻（unscaled の秒） </summary>
            public readonly double EndTime;

            /// <summary> 強化の倍率 </summary>
            public readonly float Scale;

            /// <summary> 残りの回数。負なら回数では切れない </summary>
            public int RemainingCount;

            public Enhancement(double endTime, int remainingCount, float scale)
            {
                EndTime = endTime;
                RemainingCount = remainingCount;
                Scale = scale;
            }

            public bool IsActive(double now)
            {
                return now < EndTime && RemainingCount != 0;
            }
        }
    }
}
