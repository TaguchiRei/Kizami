using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 視線の先にある飛び移りの目印と、F キーの誘導を出すか、誘導を出す飛び先の位置を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class FlightPromptState : SceneStateBase, IFlightPromptState
    {
        public bool IsVisible { get; private set; }
        public Vector3 TargetPosition { get; private set; }

        public override string GetLog()
        {
            return $"IsVisible: {IsVisible}  \nTargetPosition: {TargetPosition}";
        }

        /// <summary>
        /// 誘導を出すかと、誘導を出す飛び先の位置を設定する。
        /// </summary>
        /// <param name="isVisible">飛び移れる目印が視線の先にあるとき true</param>
        /// <param name="targetPosition">飛び先（ワールド空間）。出さないときは読まれない</param>
        public void SetTarget(bool isVisible, Vector3 targetPosition)
        {
            IsVisible = isVisible;
            TargetPosition = targetPosition;
        }
    }

    /// <summary>
    /// 飛び移りの誘導の読み取り面。
    /// </summary>
    public interface IFlightPromptState : IStateGetter
    {
        /// <summary> F キーの誘導を出すか </summary>
        bool IsVisible { get; }

        /// <summary> 誘導を出す飛び先（ワールド空間）。IsVisible のときだけ意味を持つ </summary>
        Vector3 TargetPosition { get; }
    }

    /// <summary>
    /// 飛び移りの行き先。
    /// </summary>
    public readonly struct FlightTarget
    {
        /// <summary> 着地する位置（ワールド空間） </summary>
        public readonly Vector3 Position;

        /// <summary> 着地の周囲のボクセルを壊すか </summary>
        public readonly bool CarvesVoxels;

        /// <summary> ボクセルを壊す球の半径（m） </summary>
        public readonly float CarveRadius;

        /// <summary> 着地点より上に上がってから下りてくるときの、着地点からの余裕の高さ（m）。0 なら一直線に飛ぶ </summary>
        public readonly float ApproachClearance;

        public FlightTarget(Vector3 position, bool carvesVoxels, float carveRadius,
            float approachClearance)
        {
            Position = position;
            CarvesVoxels = carvesVoxels;
            CarveRadius = carveRadius;
            ApproachClearance = approachClearance;
        }
    }
}
