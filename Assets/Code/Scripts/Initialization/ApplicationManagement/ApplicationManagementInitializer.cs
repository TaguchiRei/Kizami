using Kizami.BlackBoard;
using Kizami.Application;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// アプリケーション全体に関わるユースケース（BuildModeService）を初期化する Initializer。常駐シーンへ置く。
    /// BuildModeState は入力経路とプレイヤーリグの選択が参照するので、入力システム（InputInitializerBase）より先に初期化する。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.InitializerEarly - 10)]
    public sealed class ApplicationManagementInitializer : InitializerBase
    {
        /// <summary>
        /// エディタ上でビルドモードを固定するための指定。Auto なら実行環境から判定する。ビルドには影響しない。
        /// </summary>
        private enum BuildModeOverride
        {
            Auto,
            PC,
            Mobile,
            VR
        }

        private readonly BuildModeService _buildModeService = new();

        [SerializeField]
        [Tooltip("エディタ上でのみ有効。Auto 以外にすると実行環境の判定を無視して固定する。")]
        private BuildModeOverride _editorBuildModeOverride = BuildModeOverride.Auto;

        public override void Initialize(IBlackBoard blackBoard)
        {
            BuildMode? editorOverride = _editorBuildModeOverride switch
            {
                BuildModeOverride.PC => BuildMode.PC,
                BuildModeOverride.Mobile => BuildMode.Mobile,
                BuildModeOverride.VR => BuildMode.VR,
                _ => null
            };
            _buildModeService.Initialize(blackBoard, editorOverride);

            base.Initialize(blackBoard);
        }
    }
}
