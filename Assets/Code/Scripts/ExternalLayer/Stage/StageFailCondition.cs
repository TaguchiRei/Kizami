using System;
using UnityEngine;

namespace Kizami.External
{
    /// <summary>
    /// ステージの失敗条件1つの定義。StageData が [SerializeReference] で一覧を持ち、条件ごとに値を持てる。
    /// 判定は Application の StageFlowService が条件の型ごとに行う。
    /// </summary>
    [Serializable]
    public abstract class StageFailCondition
    {
    }

    /// <summary>
    /// プレイヤーの HP が0になったら失敗にする条件。
    /// </summary>
    [Serializable]
    public sealed class PlayerDeathFailCondition : StageFailCondition
    {
    }

    /// <summary>
    /// プレイを始めてから、制限時間（実時間）が経ったら失敗にする条件。ポーズ中は数えない。
    /// </summary>
    [Serializable]
    public sealed class TimeLimitFailCondition : StageFailCondition
    {
        [SerializeField, Min(1f)]
        [Tooltip("制限時間（秒）")]
        private float _seconds = 300f;

        /// <summary> 制限時間（秒） </summary>
        public float Seconds => _seconds;
    }
}
