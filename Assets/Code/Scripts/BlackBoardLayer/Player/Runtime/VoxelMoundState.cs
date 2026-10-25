using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 土遁・造山でボクセルを盛っている間かを保持するステート。
    /// 盛り始めと、止める操作やチャージ切れで止まったことを EngineAdapterLayer（VoxelMoundAdapter）が書き込み、ApplicationLayer（MeleeCutService）が読んで、その間は攻撃を出さない。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class VoxelMoundState : SceneStateBase, IVoxelMoundState
    {
        public bool IsMounding { get; private set; }

        public override string GetLog()
        {
            return $"IsMounding: {IsMounding}";
        }

        /// <summary>
        /// 盛っている間かを設定する。
        /// </summary>
        public void SetMounding(bool isMounding)
        {
            IsMounding = isMounding;
        }
    }

    /// <summary>
    /// 盛っている間かの読み取り面。
    /// </summary>
    public interface IVoxelMoundState : IStateGetter
    {
        /// <summary> 盛っている間か </summary>
        bool IsMounding { get; }
    }
}
