using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineService;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// プレイヤーの移動まわり（Service / State / Abstracter）を生成して繋ぐだけの配線役。
    /// ロジックは持たない。
    /// </summary>
    public sealed class PlayerInitializer : InitializerBase
    {
        [SerializeField] private PlayerMovementAbstracter _movementAbstracter;

        [SerializeField, Min(0f)]
        [Tooltip("MovementSpeed の上限値（m/s）")]
        private float _moveSpeed = 5f;

        private PlayerMovementService _movementService;

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

            var sceneId = gameObject.scene.buildIndex;
            _movementService = new PlayerMovementService(playerBoard, inputState, sceneId, _moveSpeed);

            if (_movementAbstracter != null)
            {
                _movementAbstracter.Initialize(playerBoard);
            }
            else
            {
                UsefulLogger.LogError("PlayerMovementAbstracter が設定されていません。", this);
            }

            base.Initialize(blackBoard);
        }

        private void OnDestroy()
        {
            _movementService?.Dispose();
        }
    }
}
