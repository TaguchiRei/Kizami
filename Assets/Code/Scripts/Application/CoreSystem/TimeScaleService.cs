using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 時間の倍率を保持する TimeScaleState を生成し、値を書き込むユースケース。
    /// 基本倍率（スローモードやポーズが決める値）とヒットストップの倍率を別々に持ち、掛け合わせた値を State に書く。
    /// </summary>
    public sealed class TimeScaleService : ITimeScaleController, IDisposable
    {
        private readonly TimeScaleState _state = new();

        private float _baseScale = 1f;
        private float _hitStopScale = 1f;
        private CancellationTokenSource _hitStopCancellation;

        /// <summary>
        /// TimeScaleState を AppBoard へ登録する。
        /// </summary>
        /// <param name="blackBoard">TimeScaleState の登録先</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<ITimeScaleState>(_state);
        }

        public void SetScale(float scale)
        {
            _baseScale = scale;
            Apply();
        }

        public void ResetScale()
        {
            _baseScale = 1f;
            Apply();
        }

        public void RequestHitStop(float duration, float scale)
        {
            if (duration <= 0f) return;

            CancelHitStop();

            _hitStopScale = scale;
            _hitStopCancellation = new CancellationTokenSource();
            Apply();
            HitStopAsync(duration, _hitStopCancellation.Token).Forget();
        }

        public void CancelHitStop()
        {
            if (_hitStopCancellation == null) return;

            _hitStopCancellation.Cancel();
            _hitStopCancellation.Dispose();
            _hitStopCancellation = null;
            _hitStopScale = 1f;
            Apply();
        }

        /// <summary>
        /// 実時間で duration 秒待ってから、ヒットストップを解く。
        /// </summary>
        private async UniTaskVoid HitStopAsync(float duration, CancellationToken cancellationToken)
        {
            var isCanceled = await UniTask.Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale: true,
                cancellationToken: cancellationToken).SuppressCancellationThrow();
            if (isCanceled) return;

            CancelHitStop();
        }

        private void Apply()
        {
            _state.SetScale(_baseScale * _hitStopScale);
        }

        public void Dispose()
        {
            CancelHitStop();
        }
    }

    /// <summary>
    /// 時間の倍率を変える操作面。
    /// </summary>
    public interface ITimeScaleController
    {
        /// <summary> 基本倍率を設定する。値は TimeScaleState の範囲にクランプされる </summary>
        /// <param name="scale">倍率</param>
        void SetScale(float scale);

        /// <summary> 基本倍率を1（等速）に戻す </summary>
        void ResetScale();

        /// <summary> 基本倍率に掛ける倍率を、実時間で指定した時間だけ scale にする。続いている間に呼ぶと、時間を数え直す </summary>
        /// <param name="duration">続ける実時間（秒）。0 以下なら何もしない</param>
        /// <param name="scale">掛ける倍率</param>
        void RequestHitStop(float duration, float scale);

        /// <summary> 続いているヒットストップを解く。ポーズが倍率を読む前に呼ぶ </summary>
        void CancelHitStop();
    }
}
