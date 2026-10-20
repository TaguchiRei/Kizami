using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// インゲームの流れの状態（開始・プレイ中・クリア・失敗）とポーズ中か、クリアの判定に使う破壊対象の数、倒した敵の数とスコアを保持するステート。
    /// </summary>
    [RegisterBoard(typeof(StageBoard))]
    public sealed class StageFlowState : SceneStateBase, IStageFlowState
    {
        public StagePhase Phase { get; private set; } = StagePhase.Ready;
        public StageFailReason FailReason { get; private set; } = StageFailReason.None;
        public bool IsPaused { get; private set; }
        public int TargetCount { get; private set; }
        public int DestroyedTargetCount { get; private set; }
        public int RequiredTargetCount { get; private set; }
        public int DefeatCount { get; private set; }
        public int RequiredDefeatCount { get; private set; }
        public int Score { get; private set; }
        public float RemainingTime { get; private set; } = -1f;

        public override string GetLog()
        {
            return $"Phase: {Phase}  \nIsPaused: {IsPaused}  \nFailReason: {FailReason}  \nTargets: {DestroyedTargetCount} / {RequiredTargetCount} (of {TargetCount})  \nDefeats: {DefeatCount} / {RequiredDefeatCount}  \nScore: {Score}  \nRemainingTime: {RemainingTime}";
        }

        /// <summary>
        /// 制限時間の残りを設定する。
        /// </summary>
        /// <param name="remainingTime">残りの時間（秒）</param>
        public void SetRemainingTime(float remainingTime)
        {
            RemainingTime = remainingTime;
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
        /// ポーズ中かを設定する。
        /// </summary>
        /// <param name="isPaused">ポーズ中なら true</param>
        public void SetPaused(bool isPaused)
        {
            IsPaused = isPaused;
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
        /// 破壊対象の数と、破壊済みの数と、クリアに必要な破壊済みの数を設定する。
        /// </summary>
        /// <param name="targetCount">ステージにある破壊対象の数</param>
        /// <param name="destroyedCount">破壊済みの破壊対象の数</param>
        /// <param name="requiredCount">クリアに必要な破壊済みの数</param>
        public void SetTargets(int targetCount, int destroyedCount, int requiredCount)
        {
            TargetCount = targetCount;
            DestroyedTargetCount = destroyedCount;
            RequiredTargetCount = requiredCount;
        }

        /// <summary>
        /// 倒した敵の数を設定する。
        /// </summary>
        /// <param name="defeatCount">倒した敵の数</param>
        public void SetDefeatCount(int defeatCount)
        {
            DefeatCount = defeatCount;
        }

        /// <summary>
        /// クリアに必要な倒した敵の数を設定する。
        /// </summary>
        /// <param name="requiredDefeatCount">クリアに必要な倒した敵の数。0 なら条件にしない</param>
        public void SetRequiredDefeatCount(int requiredDefeatCount)
        {
            RequiredDefeatCount = requiredDefeatCount;
        }

        /// <summary>
        /// スコアを設定する。
        /// </summary>
        /// <param name="score">スコア</param>
        public void SetScore(int score)
        {
            Score = score;
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

        /// <summary> ポーズ中か。プレイ中にだけポーズできる </summary>
        bool IsPaused { get; }

        /// <summary> ステージにある破壊対象の数 </summary>
        int TargetCount { get; }

        /// <summary> 破壊済みの破壊対象の数 </summary>
        int DestroyedTargetCount { get; }

        /// <summary> クリアに必要な破壊済みの数。破壊対象の数を超えない </summary>
        int RequiredTargetCount { get; }

        /// <summary> プレイ中に倒した敵の数 </summary>
        int DefeatCount { get; }

        /// <summary> クリアに必要な倒した敵の数。0 なら敵の数を条件にしない </summary>
        int RequiredDefeatCount { get; }

        /// <summary> スコア。倒した敵1体につき1点と、クリアしたときの基礎スコアの合計 </summary>
        int Score { get; }

        /// <summary> 制限時間の残り（秒）。制限時間がないステージでは負の値 </summary>
        float RemainingTime { get; }
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
    /// 失敗した理由。失敗条件の型ごとに1つ持つ。
    /// </summary>
    public enum StageFailReason
    {
        /// <summary> 失敗していない </summary>
        None,

        /// <summary> プレイヤーの HP が0になった </summary>
        PlayerDeath,

        /// <summary> 制限時間が切れた </summary>
        TimeUp,
    }
}
