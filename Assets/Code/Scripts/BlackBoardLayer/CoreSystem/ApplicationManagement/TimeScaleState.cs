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
        public const float MIN_SCALE = 0f;

        /// <summary> 倍率として受け付ける上限 </summary>
        public const float MAX_SCALE = 1f;

        private readonly ActionEntryList<float> _scaleChangedActions = new();

        public float Scale { get; private set; } = 1f;

        public override string GetLog()
        {
            return $"TimeScale: {Scale}";
        }

        /// <summary>
        /// 倍率を設定する。値は MIN_SCALE 〜 MAX_SCALE にクランプされる。
        /// </summary>
        /// <param name="scale">倍率</param>
        public void SetScale(float scale)
        {
            float clamped = Mathf.Clamp(scale, MIN_SCALE, MAX_SCALE);

            if (Mathf.Approximately(Scale, clamped)) return;

            Scale = clamped;
            _scaleChangedActions.Invoke(Scale);
        }

        public IDisposable RegisterOnScaleChanged(Action<float> callback)
        {
            return _scaleChangedActions.Register(new ActionEntry<float>(false, callback), nameof(callback));
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
