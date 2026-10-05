using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// TimeScaleState の倍率を Time.timeScale と Time.fixedDeltaTime へ反映する Adapter。
    /// Time.timeScale と Time.fixedDeltaTime へ書き込むのはプロジェクト全体でこのクラスだけ。
    /// </summary>
    public sealed class TimeScaleAdapter : InitializableMonoBehaviour
    {
        private float _baseFixedDeltaTime;
        private IDisposable _stateWaiter;
        private IDisposable _scaleSubscription;

        /// <summary>
        /// TimeScaleState の登録を待ち受け、登録されたら倍率の購読を始める。
        /// </summary>
        /// <param name="appBoard">TimeScaleState の取得元</param>
        public void Initialize(AppBoard appBoard)
        {
            _baseFixedDeltaTime = Time.fixedDeltaTime;

            _stateWaiter = appBoard.SubscribeStateRegister<ITimeScaleState>(
                () =>
                {
                    if (!appBoard.TryGetGameState<ITimeScaleState>(out var state)) return;

                    _scaleSubscription?.Dispose();
                    _scaleSubscription = state.RegisterOnScaleChanged(Apply);
                    Apply(state.Scale);
                },
                invokeIfRegistered: true);

            Initialize();
        }

        /// <summary>
        /// 倍率を Time.timeScale へ、初期化時の fixedDeltaTime × 倍率を Time.fixedDeltaTime へ書き込む。
        /// </summary>
        /// <param name="scale">反映する倍率</param>
        private void Apply(float scale)
        {
            Time.timeScale = scale;

            if (scale <= 0f) return;

            Time.fixedDeltaTime = _baseFixedDeltaTime * scale;
        }

        private void OnDestroy()
        {
            _scaleSubscription?.Dispose();
            _stateWaiter?.Dispose();

            if (_baseFixedDeltaTime <= 0f) return;

            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
        }
    }
}
