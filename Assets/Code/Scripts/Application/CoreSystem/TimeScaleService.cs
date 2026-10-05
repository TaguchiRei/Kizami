using Kizami.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 時間の倍率を保持する TimeScaleState を生成し、値を書き込むユースケース。
    /// </summary>
    public sealed class TimeScaleService : ITimeScaleController
    {
        private readonly TimeScaleState _state = new();

        /// <summary>
        /// TimeScaleState を AppBoard へ登録する。
        /// </summary>
        /// <param name="appBoard">TimeScaleState の登録先</param>
        public void Initialize(AppBoard appBoard)
        {
            appBoard.RegisterGameState<ITimeScaleState>(_state);
        }

        public void SetScale(float scale) => _state.SetScale(scale);

        public void ResetScale() => _state.SetScale(1f);
    }

    /// <summary>
    /// 時間の倍率を変える操作面。DI コンテナ経由で配る。
    /// </summary>
    public interface ITimeScaleController
    {
        /// <summary> 倍率を設定する。値は TimeScaleState の範囲にクランプされる </summary>
        /// <param name="scale">倍率</param>
        void SetScale(float scale);

        /// <summary> 倍率を 1（等速）に戻す </summary>
        void ResetScale();
    }
}
