using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行（GameProgressState）を生成し、書き込むユースケース。
    /// ステージの番号とステージの定義（StageData）の対応を持つ。常駐シーンで一度だけ生成する。
    /// </summary>
    public sealed class GameProgressService
    {
        private readonly GameProgressState _state = new();

        private StageData[] _stages = System.Array.Empty<StageData>();

        /// <summary> 選べるステージ。番号はこの並びの位置 </summary>
        public IReadOnlyList<StageData> Stages => _stages;

        /// <summary>
        /// GameProgressState を AppBoard へ登録する。
        /// </summary>
        /// <param name="blackBoard">GameProgressState の登録先</param>
        /// <param name="stages">選べるステージ。番号はこの並びの位置</param>
        public void Initialize(IBlackBoard blackBoard, StageData[] stages)
        {
            _stages = stages ?? System.Array.Empty<StageData>();

            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<IGameProgressState>(_state);
        }

        /// <summary>
        /// ステージを選ぶ。範囲外の番号は無視する。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0 から）</param>
        /// <returns>選べたか</returns>
        public bool SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= _stages.Length)
            {
                UsefulLogger.LogError($"ステージの番号 {stageIndex} は範囲外です。", this);
                return false;
            }

            _state.SetSelectedStageIndex(stageIndex);
            return true;
        }
    }
}
