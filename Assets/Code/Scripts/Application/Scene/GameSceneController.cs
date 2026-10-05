using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.Application.Scene;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.External.Scene;

namespace Kizami.Application
{
    /// <summary>
    /// ゲームの場面（アウトゲーム / インゲーム）の単位でシーン遷移を要求するユースケース。
    /// 場面ごとに 1 つのシーングループを持ち、3 ビルドで共通に使う。
    /// </summary>
    public sealed class GameSceneController : IGameSceneController
    {
        /// <summary> シーングループ配列の中で、アウトゲームのグループの位置 </summary>
        private const int OUT_GAME_INDEX = 0;

        /// <summary> シーングループ配列の中で、インゲームのグループの位置 </summary>
        private const int IN_GAME_INDEX = 1;

        private SceneLoadService _sceneLoadService;

        /// <summary>
        /// シーングループのロード役を用意する。
        /// </summary>
        /// <param name="blackBoard">ISceneState の取得元</param>
        /// <param name="outGameGroup">アウトゲームで読み込むシーングループ</param>
        /// <param name="inGameGroup">インゲームで読み込むシーングループ</param>
        /// <returns>初期化できたか</returns>
        public bool Initialize(IBlackBoard blackBoard, SceneGroup outGameGroup, SceneGroup inGameGroup)
        {
            if (blackBoard == null || outGameGroup == null || inGameGroup == null)
            {
                UsefulLogger.LogError("シーン遷移の初期化に必要な引数が渡されていません。", this);
                return false;
            }

            var sceneGroups = new SceneGroup[2];
            sceneGroups[OUT_GAME_INDEX] = outGameGroup;
            sceneGroups[IN_GAME_INDEX] = inGameGroup;

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

        public UniTask<bool> GoToOutGameAsync(CancellationToken cancellationToken = default)
        {
            return LoadAsync(OUT_GAME_INDEX, cancellationToken);
        }

        public UniTask<bool> GoToInGameAsync(CancellationToken cancellationToken = default)
        {
            return LoadAsync(IN_GAME_INDEX, cancellationToken);
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
    }

    /// <summary>
    /// シーン遷移の操作面。DI コンテナ経由で配る。
    /// </summary>
    public interface IGameSceneController
    {
        /// <summary> アウトゲームへ遷移する </summary>
        UniTask<bool> GoToOutGameAsync(CancellationToken cancellationToken = default);

        /// <summary> インゲームへ遷移する </summary>
        UniTask<bool> GoToInGameAsync(CancellationToken cancellationToken = default);
    }
}
