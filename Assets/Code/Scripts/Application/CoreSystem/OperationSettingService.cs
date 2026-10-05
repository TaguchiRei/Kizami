using Kizami.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 操作に関わる設定を保持する OperationSettingState を生成し、値を書き込むユースケース。
    /// </summary>
    public sealed class OperationSettingService
    {
        private readonly OperationSettingState _state = new();

        /// <param name="appBoard">OperationSettingState の登録先</param>
        /// <param name="sprintInputMode">ダッシュ入力の受け付け方の初期値</param>
        /// <param name="cutRotateStepAngle">切断面の回転入力 1 回あたりの回転角度（度）の初期値</param>
        public OperationSettingService(AppBoard appBoard, SprintInputMode sprintInputMode, float cutRotateStepAngle)
        {
            // TODO: 保存済みの設定を ExternalLayer から読み出して流し込む
            _state.SetSprintInputMode(sprintInputMode);
            _state.SetCutRotateStepAngle(cutRotateStepAngle);
            appBoard.RegisterGameState<IOperationSettingState>(_state);
        }

        /// <summary>
        /// 左右の視点操作の感度倍率を変更する。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetHorizontalSensitivity(float sensitivity) => _state.SetHorizontalSensitivity(sensitivity);

        /// <summary>
        /// 上下の視点操作の感度倍率を変更する。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetVerticalSensitivity(float sensitivity) => _state.SetVerticalSensitivity(sensitivity);
    }
}
