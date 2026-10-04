using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 世界全体の時間の倍率を保持するゲームステート。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class TimeScaleState : GameStateBase, ITimeScaleState
    {
        /// <summary> 倍率として受け付ける下限 </summary>
        public const float MinScale = 0f;

        /// <summary> 倍率として受け付ける上限 </summary>
        public const float MaxScale = 1f;

        public float Scale => _scale;

        private float _scale = 1f;

        private Action<float> _scaleChangedCallback;

        /// <summary>
        /// 倍率を設定する。値は MinScale 〜 MaxScale にクランプされる。
        /// </summary>
        /// <param name="scale">倍率</param>
        public void SetScale(float scale)
        {
            float clamped = Mathf.Clamp(scale, MinScale, MaxScale);

            if (Mathf.Approximately(_scale, clamped)) return;

            _scale = clamped;
            _scaleChangedCallback?.Invoke(_scale);
        }

        public IDisposable RegisterOnScaleChanged(Action<float> callback)
        {
            _scaleChangedCallback += callback ?? throw new ArgumentNullException(nameof(callback));
            return new BoardDispose(() => _scaleChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"TimeScale: {Scale}";
        }
    }

    /// <summary>
    /// 時間の倍率の読み取り面。
    /// </summary>
    public interface ITimeScaleState : IStateGetter
    {
        /// <summary> 時間の倍率。1 が等速、0 が停止 </summary>
        float Scale { get; }

        /// <summary>
        /// 倍率が変化した際に発火するイベントを登録する
        /// </summary>
        /// <param name="callback">変化後の倍率を受け取る処理</param>
        IDisposable RegisterOnScaleChanged(Action<float> callback);
    }
}