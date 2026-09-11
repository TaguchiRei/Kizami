using Kizami.Application;
using Kizami.BlackBoard;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// 視点操作の感度設定 (PlayerOperationConfigState) を生成する Initializer。常駐シーンへ置く。
    ///
    /// 設定はシーンを跨いで保たれる為、シーンごとの Initializer ではなくここで一度だけ生成する。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.InitializerEarly - 10)]
    public sealed class PlayerOperationConfigInitializer : InitializerBase
    {
        private PlayerOperationConfigService _configService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetStateBoard<PlayerBoard>(out var playerBoard))
            {
                UsefulLogger.LogError("PlayerBoard が未登録の為、操作設定を登録できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            _configService = new PlayerOperationConfigService(playerBoard);

            base.Initialize(blackBoard);
        }
    }
}
