using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// インゲームの流れの状態（開始・プレイ中・クリア・失敗）と、クリアの判定に使う破壊対象の数を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(StageBoard))]
    public sealed class StageFlowState : SceneStateBase, IStageFlowState
    {
        public StagePhase Phase { get; private set; } = StagePhase.Ready;
        public StageFailReason FailReason { get; private set; } = StageFailReason.None;
        public int TargetCount { get; private set; }
        public int DestroyedTargetCount { get; private set; }

        public override string GetLog()
        {
            return $"Phase: {Phase}  \nFailReason: {FailReason}  \nTargets: {DestroyedTargetCount} / {TargetCount}";
        }

        /// <summary>
        /// 流れの状態を設定する。
        /// </summary>
        /// <param name="phase">流れの状態</param>
        public void SetPhase(StagePhase phase)
        {
            Phase = phase;
        }

        /// <summary>
        /// 失敗した理由を設定する。
        /// </summary>
        /// <param name="reason">失敗した理由</param>
        public void SetFailReason(StageFailReason reason)
        {
            FailReason = reason;
        }

        /// <summary>
        /// 破壊対象の数と、破壊済みの数を設定する。
        /// </summary>
        /// <param name="targetCount">ステージにある破壊対象の数</param>
        /// <param name="destroyedCount">破壊済みの破壊対象の数</param>
        public void SetTargets(int targetCount, int destroyedCount)
        {
            TargetCount = targetCount;
            DestroyedTargetCount = destroyedCount;
        }
    }

    /// <summary>
    /// インゲームの流れの読み取り面。
    /// </summary>
    public interface IStageFlowState : IStateGetter
    {
        /// <summary> 流れの状態 </summary>
        StagePhase Phase { get; }

        /// <summary> 失敗した理由。失敗していなければ None </summary>
        StageFailReason FailReason { get; }

        /// <summary> ステージにある破壊対象の数 </summary>
        int TargetCount { get; }

        /// <summary> 破壊済みの破壊対象の数 </summary>
        int DestroyedTargetCount { get; }
    }

    /// <summary>
    /// インゲームの流れの状態。Cleared と Failed からは変わらない。
    /// </summary>
    public enum StagePhase
    {
        /// <summary> 初期化を終えて、プレイを始める前 </summary>
        Ready,

        /// <summary> プレイ中 </summary>
        Playing,

        /// <summary> すべての破壊対象を破壊した </summary>
        Cleared,

        /// <summary> 失敗条件のどれかを満たした </summary>
        Failed,
    }

    /// <summary>
    /// 失敗した理由。失敗条件の型ごとに 1 つ持つ。
    /// </summary>
    public enum StageFailReason
    {
        /// <summary> 失敗していない </summary>
        None,

        /// <summary> プレイヤーの HP が 0 になった </summary>
        PlayerDeath,
    }
}
