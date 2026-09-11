using Kizami.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 視点操作の感度設定を保持する PlayerOperationConfigState を生成し、値を書き込むユースケース。
    /// PlayerOperationConfigState の具象インスタンスはこのクラスだけが保持する (Single Writer)。
    ///
    /// 現状は既定値を入れるだけだが、保存済みの設定を ExternalLayer から読み出して
    /// 流し込む差込口はここになる。
    /// </summary>
    public sealed class PlayerOperationConfigService
    {
        private readonly PlayerOperationConfigState _state = new();

        /// <param name="playerBoard">PlayerOperationConfigState の登録先</param>
        public PlayerOperationConfigService(PlayerBoard playerBoard)
        {
            playerBoard.RegisterGameState<IPlayerOperationConfigState>(_state);
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
