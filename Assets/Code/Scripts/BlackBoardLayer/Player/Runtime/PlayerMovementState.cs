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

        /// <summary>
        /// 移動モードを設定する。
        /// </summary>
        /// <param name="mode">移動モード</param>
        public void SetMode(PlayerMoveMode mode)
        {
            Mode = mode;
        }

        /// <summary>
        /// 目標速度を設定する。
        /// </summary>
        /// <param name="targetVelocity">ワールド空間の目標速度（m/s）</param>
        public void SetTargetVelocity(Vector3 targetVelocity)
        {
            TargetVelocity = targetVelocity;
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
        /// <summary>
        /// ワールド空間の目標速度（m/s）。通常の移動と壁走りでは水平成分だけを使い、Y 成分は 0 になる。
        /// </summary>
        Vector3 TargetVelocity { get; }

        /// <summary> 移動モード </summary>
        PlayerMoveMode Mode { get; }
    }

    /// <summary>
    /// プレイヤーの移動モード。地面にいるか空中にいるかは PlayerContactState で表し、ここでは区別しない。
    /// </summary>
    public enum PlayerMoveMode
    {
        /// <summary> 地上・空中での通常の移動。重力を受ける </summary>
        Normal,

        /// <summary> 壁走り。重力を受けず、壁に沿って進む。移動入力がなければその場にとどまる（ラッチ） </summary>
        WallRunning
    }
}
