using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// プレイヤーの移動・視点まわり（Service / State / Abstractor）を生成して繋ぐだけの配線役。
    /// ロジックは持たない。操作シーンへ置く。
    ///
    /// 視点入力を実際の回転へどう変換するかは操作系ごとに違うが、その差は
    /// 各シーンへ置く PlayerMovementAbstractorBase の派生が吸収する為、ここは選び分けをしない。
    /// </summary>
    public sealed class PlayerInitializer : InitializerBase
    {
        [SerializeField] private PlayerMovementAdapterBase movementAdapter;
        [SerializeField] private PlayerCameraAdapterBase cameraAdapter;

        [SerializeField, Min(0f)]
        [Tooltip("MovementSpeed の上限値（m/s）")]
        private float _moveSpeed = 5f;

        private PlayerMovementService _movementService;
        private PlayerLookService _lookService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetStateBoard<PlayerBoard>(out var playerBoard))
            {
                UsefulLogger.LogError(
                    "PlayerBoard が未登録です。常駐シーンの Root Compositor を再生成してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            if (!blackBoard.TryGetStateBoard<InputBoard>(out var inputBoard))
            {
                UsefulLogger.LogError(
                    "InputBoard が未登録です。常駐シーンの Root Compositor を再生成してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            // IInputState は InputInitializer が GameState として登録する。
            // このシーンの Compositor は常駐シーンの初期化後に走るため、通常はここで取得できる。
            if (!inputBoard.TryGetGameState<IInputState>(out var inputState))
            {
                UsefulLogger.LogError(
                    "IInputState が未登録です。常駐シーンから Play しているか、InputInitializer の初期化順を確認してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            if (!playerBoard.TryGetGameState<IPlayerOperationConfigState>(out var configState))
            {
                UsefulLogger.LogError(
                    "IPlayerOperationConfigState が未登録です。常駐シーンの初期化順を確認してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            var sceneId = gameObject.scene.buildIndex;
            _movementService = new PlayerMovementService(playerBoard, inputState, sceneId, _moveSpeed);

            _lookService = new PlayerLookService(playerBoard, inputState, configState, sceneId);

            if (movementAdapter != null)
            {
                movementAdapter.Initialize(playerBoard);
            }
            else
            {
                UsefulLogger.LogError("PlayerMovementAbstractor が設定されていません。", this);
            }

            // カメラの上下方向反映は操作系によっては使わない（例: VR は HMD の姿勢が担う）為、未設定でもエラーにしない
            if (cameraAdapter != null)
            {
                cameraAdapter.Initialize(playerBoard);
            }

            base.Initialize(blackBoard);
        }

        private void OnDestroy()
        {
            _movementService?.Dispose();
            _lookService?.Dispose();
        }
    }
}
