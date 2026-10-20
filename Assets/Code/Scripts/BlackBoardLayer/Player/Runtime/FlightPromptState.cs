using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 視線の先にある飛び移りの目印と、F キーの誘導を出すかを保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class FlightPromptState : SceneStateBase, IFlightPromptState
    {
        public bool IsVisible { get; private set; }

        public override string GetLog()
        {
            return $"IsVisible: {IsVisible}";
        }

        /// <summary>
        /// 誘導を出すかを設定する。
        /// </summary>
        /// <param name="isVisible">飛び移れる目印が視線の先にあるとき true</param>
        public void SetVisible(bool isVisible)
        {
            IsVisible = isVisible;
        }
    }

    /// <summary>
    /// 飛び移りの誘導の読み取り面。
    /// </summary>
    public interface IFlightPromptState : IStateGetter
    {
        /// <summary> F キーの誘導を出すか </summary>
        bool IsVisible { get; }
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

        public FlightTarget(Vector3 position, bool carvesVoxels, float carveRadius)
        {
            Position = position;
            CarvesVoxels = carvesVoxels;
            CarveRadius = carveRadius;
        }
    }
}
