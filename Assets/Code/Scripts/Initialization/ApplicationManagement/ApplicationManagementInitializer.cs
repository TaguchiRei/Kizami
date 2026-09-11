using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.XR;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// アプリケーション全体に関わるステートを生成する Initializer。常駐シーンへ置く。
    ///
    /// BuildModeState は入力経路とプレイヤーリグの選択が参照する為、
    /// 入力システム (InputInitializerBase) より先に初期化する必要がある。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.InitializerEarly - 10)]
    public sealed class ApplicationManagementInitializer : InitializerBase
    {
        /// <summary>
        /// エディタ上でビルドモードを固定する為の指定。Auto なら実行環境から判定する。
        /// ビルドには影響しない。
        /// </summary>
        private enum BuildModeOverride
        {
            Auto,
            PC,
            Mobile,
            VR
        }

        [SerializeField]
        [Tooltip("エディタ上でのみ有効。Auto 以外にすると実行環境の判定を無視して固定する。")]
        private BuildModeOverride _editorBuildModeOverride = BuildModeOverride.Auto;

        private BuildModeState _buildModeState;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetStateBoard<AppBoard>(out var appBoard))
            {
                UsefulLogger.LogError("AppBoard が未登録の為、BuildModeState を登録できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            _buildModeState = new BuildModeState();
            _buildModeState.SetBuildMode(ResolveBuildMode());
            appBoard.RegisterGameState<IBuildModeState>(_buildModeState);

            base.Initialize(blackBoard);
        }

        /// <summary>
        /// 実行環境からビルドモードを判定する。
        /// XR デバイスが動いているかを最初に見る為、PCVR も VR として扱える。
        /// </summary>
        private BuildMode ResolveBuildMode()
        {
#if UNITY_EDITOR
            if (_editorBuildModeOverride != BuildModeOverride.Auto)
            {
                return _editorBuildModeOverride switch
                {
                    BuildModeOverride.PC => BuildMode.PC,
                    BuildModeOverride.Mobile => BuildMode.Mobile,
                    _ => BuildMode.VR
                };
            }
#endif

            if (XRSettings.isDeviceActive) return BuildMode.VR;

#if UNITY_ANDROID || UNITY_IOS
            return BuildMode.Mobile;
#else
            return BuildMode.PC;
#endif
        }
    }
}
