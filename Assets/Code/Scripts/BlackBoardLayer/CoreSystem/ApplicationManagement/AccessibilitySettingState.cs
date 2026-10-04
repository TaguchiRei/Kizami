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

        public float CutRotateStepAngle { get; private set; } = 15f;

        /// <summary>
        /// ダッシュ入力の受け付け方を設定する。
        /// </summary>
        /// <param name="mode">ダッシュ入力の受け付け方</param>
        public void SetSprintInputMode(SprintInputMode mode)
        {
            SprintInputMode = mode;
        }

        /// <summary>
        /// 切断面の回転入力 1 回あたりの回転角度を設定する。
        /// </summary>
        /// <param name="angle">回転角度（度）</param>
        public void SetCutRotateStepAngle(float angle)
        {
            CutRotateStepAngle = angle;
        }

        public override string GetLog()
        {
            return $"SprintInputMode: {SprintInputMode}  \nCutRotateStepAngle: {CutRotateStepAngle}";
        }
    }

    /// <summary>
    /// 操作のしやすさに関わる設定の読み取り面。
    /// </summary>
    public interface IAccessibilitySettingState : IStateGetter
    {
        /// <summary> ダッシュ入力の受け付け方 </summary>
        SprintInputMode SprintInputMode { get; }

        /// <summary> 切断面の回転入力（ホイール 1 段）1 回あたりの回転角度（度） </summary>
        float CutRotateStepAngle { get; }
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
