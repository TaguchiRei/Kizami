using System;

namespace Kizami.External
{
    /// <summary>
    /// ステージの失敗条件 1 つの定義。StageData が [SerializeReference] で一覧を持ち、条件ごとに値を持てる。
    /// 判定は Application の StageFlowService が条件の型ごとに行う。
    /// </summary>
    [Serializable]
    public abstract class StageFailCondition
    {
    }

    /// <summary>
    /// プレイヤーの HP が 0 になったら失敗にする条件。
    /// </summary>
    [Serializable]
    public sealed class PlayerDeathFailCondition : StageFailCondition
    {
    }
}
