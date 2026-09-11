using System.Threading;
using Cysharp.Threading.Tasks;
using Kizami.BlackBoard;
using UsefulToolkit.Application.Scene;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.External.Scene;

namespace Kizami.Application
{
    /// <summary>
    /// ゲームの場面（アウトゲーム / インゲーム）の単位でシーン遷移を要求するユースケース。
    ///
    /// 操作系の差はシーングループの選択に畳み込まれている。場面のシーン自体は 3 ビルドで共通で、
    /// 組み合わせる操作シーンだけが変わる為、遷移の呼び出し側は操作系を意識しなくてよい。
    /// </summary>
    public sealed class GameSceneController : IGameSceneController
    {
        /// <summary> シーングループ配列の中で、インゲーム側の並びが始まる位置 </summary>
        private const int InGameOffset = BuildModeSelector.Count;

        private SceneLoadService _sceneLoadService;
        private IBuildModeState _buildModeState;

        /// <summary>
        /// シーングループのロード役を用意する。
        /// </summary>
        /// <param name="blackBoard">ISceneState の取得元</param>
        /// <param name="sceneGroups">
        /// 操作対象のシーングループ。アウトゲームの全ビルドモード分、インゲームの全ビルドモード分の順に並べること
        /// </param>
        /// <param name="buildModeState">操作系の選択に使うビルドモード</param>
        /// <returns>初期化できたか</returns>
        public bool Initialize(IBlackBoard blackBoard, SceneGroup[] sceneGroups, IBuildModeState buildModeState)
        {
            if (blackBoard == null || sceneGroups == null || buildModeState == null)
            {
                UsefulLogger.LogError("シーン遷移の初期化に必要な引数が渡されていません。", this);
                return false;
            }

            if (sceneGroups.Length != InGameOffset + BuildModeSelector.Count)
            {
                UsefulLogger.LogError(
                    $"シーングループの数が {sceneGroups.Length} 件で、想定の " +
                    $"{InGameOffset + BuildModeSelector.Count} 件と一致しません。", this);
                return false;
            }

            _sceneLoadService = new SceneLoadService(blackBoard, sceneGroups);
            _buildModeState = buildModeState;
            return true;
        }

        /// <summary>
        /// 常駐シーンだけがロードされた起動直後の状態から、アウトゲームへ遷移する。
        /// </summary>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        public UniTask<bool> StartGameAsync(CancellationToken cancellationToken = default)
        {
            if (!TryGetService(out var service)) return UniTask.FromResult(false);

            return service.Initialize(ResolveGroupIndex(inGame: false), cancellationToken);
        }

        public UniTask<bool> GoToOutGameAsync(CancellationToken cancellationToken = default)
        {
            return LoadAsync(inGame: false, cancellationToken);
        }

        public UniTask<bool> GoToInGameAsync(CancellationToken cancellationToken = default)
        {
            return LoadAsync(inGame: true, cancellationToken);
        }

        /// <summary>
        /// 場面のシーングループを上書きロードする。
        /// 遷移元と遷移先で共通のシーン（操作シーン）はアンロードされずに残る為、
        /// 操作系の状態は場面をまたいでも保たれる。
        /// </summary>
        /// <param name="inGame">インゲームへ遷移するか</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        private UniTask<bool> LoadAsync(bool inGame, CancellationToken cancellationToken)
        {
            if (!TryGetService(out var service)) return UniTask.FromResult(false);

            return service.LoadGroupAsync(ResolveGroupIndex(inGame), true, cancellationToken);
        }

        /// <summary>
        /// 場面と現在のビルドモードから、ロードするシーングループの位置を求める。
        /// </summary>
        /// <param name="inGame">インゲームか</param>
        private int ResolveGroupIndex(bool inGame)
        {
            return (inGame ? InGameOffset : 0) + BuildModeSelector.IndexOf(_buildModeState.BuildMode);
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
