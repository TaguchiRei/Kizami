using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行（選んだステージ）を保持するゲームステート。シーンを跨いで保たれる。
    /// ステージは番号で持ち、番号とステージの定義の対応は Application が持つ。BlackBoard から External を参照できない為。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class GameProgressState : GameStateBase, IGameProgressState
    {
        /// <summary> ステージを選んでいないことを表す番号 </summary>
        public const int NO_STAGE = -1;

        public int SelectedStageIndex { get; private set; } = NO_STAGE;

        public override string GetLog()
        {
            return $"SelectedStageIndex: {SelectedStageIndex}";
        }

        /// <summary>
        /// 選んだステージの番号を設定する。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0 から）</param>
        public void SetSelectedStageIndex(int stageIndex)
        {
            SelectedStageIndex = stageIndex;
        }
    }

    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行の読み取り面。
    /// </summary>
    public interface IGameProgressState : IStateGetter
    {
        /// <summary> 選んだステージの番号。選んでいなければ GameProgressState.NO_STAGE </summary>
        int SelectedStageIndex { get; }
    }
}
