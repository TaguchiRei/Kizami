using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// スローモード中かと、スローモード中に近接切断を振れる残りの回数を保持するステート。
    /// 倍率そのものの正本は TimeScaleState で、デバッグの操作でも変わるので、スローモードの発動中かはここで持つ。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class SlowModeState : SceneStateBase, ISlowModeState
    {
        public bool IsActive { get; private set; }
        public int RemainingCuts { get; private set; }

        public override string GetLog()
        {
            return $"SlowMode: {(IsActive ? "On" : "Off")}, RemainingCuts: {RemainingCuts}";
        }

        /// <summary>
        /// スローモード中かを設定する。
        /// </summary>
        /// <param name="isActive">スローモード中なら true</param>
        public void SetActive(bool isActive)
        {
            IsActive = isActive;
        }

        /// <summary>
        /// スローモード中に近接切断を振れる残りの回数を設定する。
        /// </summary>
        /// <param name="remainingCuts">残りの回数</param>
        public void SetRemainingCuts(int remainingCuts)
        {
            RemainingCuts = remainingCuts;
        }
    }

    /// <summary>
    /// スローモードの読み取り面。
    /// </summary>
    public interface ISlowModeState : IStateGetter
    {
        /// <summary> スローモード中か </summary>
        bool IsActive { get; }

        /// <summary> スローモード中に近接切断を振れる残りの回数 </summary>
        int RemainingCuts { get; }
    }
}
