using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// スローモード中かを保持するステート。
    /// 倍率そのものの正本は TimeScaleState で、デバッグの操作でも変わるので、スローモードの発動中かはここで持つ。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class SlowModeState : SceneStateBase, ISlowModeState
    {
        public bool IsActive { get; private set; }

        public override string GetLog()
        {
            return $"SlowMode: {(IsActive ? "On" : "Off")}";
        }

        /// <summary>
        /// スローモード中かを設定する。
        /// </summary>
        /// <param name="isActive">スローモード中なら true</param>
        public void SetActive(bool isActive)
        {
            IsActive = isActive;
        }
    }

    /// <summary>
    /// スローモードの読み取り面。
    /// </summary>
    public interface ISlowModeState : IStateGetter
    {
        /// <summary> スローモード中か </summary>
        bool IsActive { get; }
    }
}
