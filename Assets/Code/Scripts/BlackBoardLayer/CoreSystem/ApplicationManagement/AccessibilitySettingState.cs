using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 操作のしやすさに関わる設定を保持するゲームステート。シーンを跨いで保たれる。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class AccessibilitySettingState : GameStateBase, IAccessibilitySettingState
    {
        public SprintInputMode SprintInputMode { get; private set; } = SprintInputMode.Hold;

        /// <summary>
        /// ダッシュ入力の受け付け方を設定する。
        /// </summary>
        /// <param name="mode">ダッシュ入力の受け付け方</param>
        public void SetSprintInputMode(SprintInputMode mode)
        {
            SprintInputMode = mode;
        }

        public override string GetLog()
        {
            return $"SprintInputMode: {SprintInputMode}";
        }
    }

    /// <summary>
    /// 操作のしやすさに関わる設定の読み取り面。
    /// </summary>
    public interface IAccessibilitySettingState : IStateGetter
    {
        /// <summary> ダッシュ入力の受け付け方 </summary>
        SprintInputMode SprintInputMode { get; }
    }

    /// <summary>
    /// ダッシュ入力の受け付け方。
    /// </summary>
    public enum SprintInputMode
    {
        /// <summary> 押している間だけダッシュする </summary>
        Hold,

        /// <summary> 押すたびにダッシュの有無を切り替える。移動入力が 0 になると解除する </summary>
        Toggle
    }
}
