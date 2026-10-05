using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
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
        private IDisposable _scaleSubscription;

        /// <summary>
        /// TimeScaleState を取得し、倍率の購読を始める。TimeScaleState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">TimeScaleState の取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetGameState<AppBoard, ITimeScaleState>(out var state, this)) return;

            _baseFixedDeltaTime = Time.fixedDeltaTime;
            _scaleSubscription = state.RegisterOnScaleChanged(Apply);
            Apply(state.Scale);

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

            if (_baseFixedDeltaTime <= 0f) return;

            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
        }
    }
}
