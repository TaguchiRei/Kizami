using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーの移動の目標を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerMovementState : SceneStateBase, IPlayerMovementState
    {
        public Vector3 TargetVelocity { get; private set; }
        public PlayerMoveMode Mode { get; private set; }

        private Action<PlayerMoveMode, PlayerMoveMode> _modeChangedCallback;

        /// <summary>
        /// 移動モードを設定する。移動モードが変わったときだけ変化の通知を流す。
        /// </summary>
        /// <param name="mode">移動モード</param>
        public void SetMode(PlayerMoveMode mode)
        {
            if (Mode == mode) return;

            var previous = Mode;
            Mode = mode;
            _modeChangedCallback?.Invoke(previous, mode);
        }

        /// <summary>
        /// 目標速度を設定する。
        /// </summary>
        /// <param name="targetVelocity">ワールド空間の目標速度（m/s）</param>
        public void SetTargetVelocity(Vector3 targetVelocity)
        {
            TargetVelocity = targetVelocity;
        }

        public IDisposable RegisterOnModeChanged(Action<PlayerMoveMode, PlayerMoveMode> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _modeChangedCallback += callback;
            return new BoardDispose(() => _modeChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"Mode: {Mode}  \nTargetVelocity: {TargetVelocity}";
        }
    }

    /// <summary>
    /// プレイヤーの移動の目標の読み取り面。
    /// </summary>
    public interface IPlayerMovementState : IStateGetter
    {
        /// <summary> ワールド空間の目標速度（m/s）。通常の移動と壁走りでは Y 成分が 0、ワープ中は Y 成分も使う </summary>
        Vector3 TargetVelocity { get; }

        /// <summary> 移動モード </summary>
        PlayerMoveMode Mode { get; }

        /// <summary>
        /// 移動モードが変化した際に発火するイベントを登録する。ワープのエフェクトなどの差し込み口。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に変化前と変化後の値が入る</param>
        IDisposable RegisterOnModeChanged(Action<PlayerMoveMode, PlayerMoveMode> callback);
    }

    /// <summary>
    /// プレイヤーの移動モード。地面と空中の区別は PlayerContactState が持つ。
    /// </summary>
    public enum PlayerMoveMode
    {
        /// <summary> 地上・空中での通常の移動。重力を受ける </summary>
        Normal,

        /// <summary> 壁走り。重力を無視して壁に沿って進み、移動入力が 0 のときはその場にとどまる（ラッチ） </summary>
        WallRunning,

        /// <summary> 短距離ワープ。重力を無視して、決まった時間だけ決まった速度で進む </summary>
        Warping
    }
}
