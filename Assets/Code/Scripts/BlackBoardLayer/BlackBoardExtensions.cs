using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// IBlackBoard から ChildBoard と、その ChildBoard に載った State を取り出す拡張。
    /// 取り出せなかったときは、何が足りないかをエラーログに出す。
    /// </summary>
    public static class BlackBoardExtensions
    {
        /// <summary>
        /// ChildBoard を取り出す。
        /// </summary>
        /// <param name="blackBoard">取り出し元</param>
        /// <param name="board">取り出した ChildBoard</param>
        /// <param name="logContext">取り出せなかったときのエラーログの出力元</param>
        /// <returns>取り出せたか</returns>
        public static bool TryGetBoard<TBoard>(this IBlackBoard blackBoard, out TBoard board, object logContext)
            where TBoard : ChildStateBoardBase
        {
            if (blackBoard.TryGetStateBoard(out board)) return true;

            UsefulLogger.LogError(
                $"{typeof(TBoard).Name} が未登録です。常駐シーンから Play しているか確認してください。", logContext);
            return false;
        }

        /// <summary>
        /// ChildBoard に登録されたゲームステートを取り出す。
        /// </summary>
        /// <param name="blackBoard">取り出し元</param>
        /// <param name="state">取り出したゲームステートの読み取り面</param>
        /// <param name="logContext">取り出せなかったときのエラーログの出力元</param>
        /// <returns>取り出せたか</returns>
        public static bool TryGetGameState<TBoard, TState>(this IBlackBoard blackBoard, out TState state,
            object logContext)
            where TBoard : ChildStateBoardBase
            where TState : IStateGetter
        {
            state = default;
            if (!blackBoard.TryGetBoard<TBoard>(out var board, logContext)) return false;
            if (board.TryGetGameState(out state)) return true;

            UsefulLogger.LogError(
                $"{typeof(TState).Name} が {typeof(TBoard).Name} に未登録です。Initializer の初期化順を確認してください。",
                logContext);
            return false;
        }

        /// <summary>
        /// ChildBoard に登録されたシーンステートを取り出す。
        /// </summary>
        /// <param name="blackBoard">取り出し元</param>
        /// <param name="state">取り出したシーンステートの読み取り面</param>
        /// <param name="logContext">取り出せなかったときのエラーログの出力元</param>
        /// <returns>取り出せたか</returns>
        public static bool TryGetSceneState<TBoard, TState>(this IBlackBoard blackBoard, out TState state,
            object logContext)
            where TBoard : ChildStateBoardBase
            where TState : IStateGetter
        {
            state = default;
            if (!blackBoard.TryGetBoard<TBoard>(out var board, logContext)) return false;
            if (board.TryGetSceneState(out state, out _)) return true;

            UsefulLogger.LogError(
                $"{typeof(TState).Name} が {typeof(TBoard).Name} に未登録です。Initializer の初期化順を確認してください。",
                logContext);
            return false;
        }
    }
}
