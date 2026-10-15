using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.Application.Scene;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.External.Scene;

namespace Kizami.Application
{
    /// <summary>
    /// ゲームの場面（アウトゲーム / ステージごとのインゲーム）の単位でシーン遷移を要求するユースケース。
    /// アウトゲームに1つ、ステージごとに1つのシーングループを持ち、3ビルドで共通に使う。
    /// </summary>
    public sealed class GameSceneController : IGameSceneController
    {
        /// <summary> シーングループ配列の中で、アウトゲームのグループの位置 </summary>
        private const int OUT_GAME_INDEX = 0;

        /// <summary> シーングループ配列の中で、最初のステージのグループの位置。以降はステージの番号の順に並ぶ </summary>
        private const int FIRST_STAGE_INDEX = 1;

        private SceneLoadService _sceneLoadService;
        private GameProgressService _progressService;

        public IReadOnlyList<StageData> Stages => _progressService?.Stages ?? System.Array.Empty<StageData>();

        /// <summary>
        /// シーングループのロード役を用意する。
        /// </summary>
        /// <param name="blackBoard">ISceneState の取得元</param>
        /// <param name="outGameGroup">アウトゲームで読み込むシーングループ</param>
        /// <param name="progressService">選べるステージの取得元と、選んだステージの書き込み先</param>
        /// <returns>初期化できたか</returns>
        public bool Initialize(IBlackBoard blackBoard, SceneGroup outGameGroup, GameProgressService progressService)
        {
            if (blackBoard == null || outGameGroup == null || progressService == null)
            {
                UsefulLogger.LogError("シーン遷移の初期化に必要な引数が渡されていません。", this);
                return false;
            }

            var stages = progressService.Stages;
            var sceneGroups = new SceneGroup[FIRST_STAGE_INDEX + stages.Count];
            sceneGroups[OUT_GAME_INDEX] = outGameGroup;
            for (var i = 0; i < stages.Count; i++)
            {
                var groupData = stages[i] != null && stages[i].SceneGroup != null ? stages[i].SceneGroup.GroupData : null;
                if (groupData == null)
                {
                    UsefulLogger.LogError($"ステージ {i} のシーングループが設定されていません。", this);
                    return false;
                }

                sceneGroups[FIRST_STAGE_INDEX + i] = groupData;
            }

            _progressService = progressService;
            _sceneLoadService = new SceneLoadService(blackBoard, sceneGroups);
            return true;
        }

        /// <summary>
        /// 常駐シーンだけがロードされた起動直後の状態から、アウトゲームへ遷移する。
        /// </summary>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        public UniTask<bool> StartGameAsync(CancellationToken cancellationToken = default)
        {
            if (!TryGetService(out var service)) return UniTask.FromResult(false);

            return service.Initialize(OUT_GAME_INDEX, cancellationToken);
        }

        /// <summary>
        /// 場面のシーングループを上書きロードする。
        /// </summary>
        /// <param name="groupIndex">ロードするシーングループの位置</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        private UniTask<bool> LoadAsync(int groupIndex, CancellationToken cancellationToken)
        {
            if (!TryGetService(out var service)) return UniTask.FromResult(false);

            return service.LoadGroupAsync(groupIndex, true, cancellationToken);
        }

        private bool TryGetService(out SceneLoadService service)
        {
            service = _sceneLoadService;

            if (service != null) return true;

            UsefulLogger.LogError("シーン遷移が初期化されていません。", this);
            return false;
        }

        public UniTask<bool> GoToOutGameAsync(CancellationToken cancellationToken = default)
        {
            return LoadAsync(OUT_GAME_INDEX, cancellationToken);
        }

        public UniTask<bool> GoToStageAsync(int stageIndex, CancellationToken cancellationToken = default)
        {
            if (!TryGetService(out _) || !_progressService.SelectStage(stageIndex)) return UniTask.FromResult(false);

            return LoadAsync(FIRST_STAGE_INDEX + stageIndex, cancellationToken);
        }

        public UniTask<bool> RetryAsync(CancellationToken cancellationToken = default)
        {
            if (!TryGetService(out var service)) return UniTask.FromResult(false);

            var stageIndex = _progressService.SelectedStageIndex;
            if (stageIndex == GameProgressState.NO_STAGE)
            {
                UsefulLogger.LogError("ステージが選ばれていない為、読み直せません。", this);
                return UniTask.FromResult(false);
            }

            return service.ReloadGroupAsync(FIRST_STAGE_INDEX + stageIndex, cancellationToken);
        }
    }

    /// <summary>
    /// シーン遷移の操作面。
    /// </summary>
    public interface IGameSceneController
    {
        /// <summary> 選べるステージ。番号はこの並びの位置 </summary>
        IReadOnlyList<StageData> Stages { get; }

        /// <summary> アウトゲームへ遷移する </summary>
        UniTask<bool> GoToOutGameAsync(CancellationToken cancellationToken = default);

        /// <summary> ステージを選び、そのステージのインゲームへ遷移する </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        UniTask<bool> GoToStageAsync(int stageIndex, CancellationToken cancellationToken = default);

        /// <summary> 選んでいるステージのインゲームを読み直し、初めから始める </summary>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        UniTask<bool> RetryAsync(CancellationToken cancellationToken = default);
    }
}
