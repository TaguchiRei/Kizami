using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 視点操作の感度設定を保持するステート。シーンを跨いで保たれる。
    ///
    /// 感度は「基準 1.0 の倍率」であり、角速度のような物理量ではない。
    /// 視点入力の生の値は経路ごとに単位が違う (PC / スマホはスクリーン座標の delta、
    /// VR はスティックの -1〜1) 為、倍率と基準スケールを分けないと設定値の意味が経路ごとに変わる。
    /// 基準スケールは各入力経路・適用側が定数として持ち、ここには持たせない。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerOperationConfigState : GameStateBase, IPlayerOperationConfigState
    {
        /// <summary> 感度倍率として受け付ける下限 </summary>
        public const float MinSensitivity = 0.01f;

        /// <summary> 感度倍率として受け付ける上限 </summary>
        public const float MaxSensitivity = 10f;

        public float HorizontalSensitivity => _horizontalSensitivity;
        public float VerticalSensitivity => _verticalSensitivity;

        private float _horizontalSensitivity = 1f;
        private float _verticalSensitivity = 1f;

        private Action _sensitivityChangedCallback;

        /// <summary>
        /// 左右の視点操作の感度倍率を設定する。値は MinSensitivity 〜 MaxSensitivity にクランプされる。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetHorizontalSensitivity(float sensitivity)
        {
            float clamped = Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity);

            if (Mathf.Approximately(_horizontalSensitivity, clamped)) return;

            _horizontalSensitivity = clamped;
            _sensitivityChangedCallback?.Invoke();
        }

        /// <summary>
        /// 上下の視点操作の感度倍率を設定する。値は MinSensitivity 〜 MaxSensitivity にクランプされる。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetVerticalSensitivity(float sensitivity)
        {
            float clamped = Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity);

            if (Mathf.Approximately(_verticalSensitivity, clamped)) return;

            _verticalSensitivity = clamped;
            _sensitivityChangedCallback?.Invoke();
        }

        public IDisposable RegisterOnSensitivityChanged(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _sensitivityChangedCallback += callback;
            return new BoardDispose(() => _sensitivityChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"HorizontalSensitivity: {HorizontalSensitivity}  \nVerticalSensitivity: {VerticalSensitivity}";
        }
    }

    /// <summary>
    /// 視点操作の感度設定の読み取り面。
    /// </summary>
    public interface IPlayerOperationConfigState : IStateGetter
    {
        /// <summary> 左右の視点操作の感度倍率 </summary>
        float HorizontalSensitivity { get; }

        /// <summary>
        /// 上下の視点操作の感度倍率。
        /// VR では上下方向の入力を適用側が捨てる為、この値は結果に影響しない。
        /// </summary>
        float VerticalSensitivity { get; }

        /// <summary>
        /// 感度が変化した際に発火するイベントを登録する
        /// </summary>
        /// <param name="callback">変化時に実行する処理</param>
        IDisposable RegisterOnSensitivityChanged(Action callback);
    }
}
