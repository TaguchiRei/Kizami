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
            return $"TargetVelocity: {TargetVelocity}";
        }
    }

    /// <summary>
    /// プレイヤーの移動の目標の読み取り面。
    /// </summary>
    public interface IPlayerMovementState : IStateGetter
    {
        /// <summary>
        /// ワールド空間の目標速度（m/s）。通常の移動では水平成分だけを使い、Y 成分は 0 になる。
        /// </summary>
        Vector3 TargetVelocity { get; }
    }
}
