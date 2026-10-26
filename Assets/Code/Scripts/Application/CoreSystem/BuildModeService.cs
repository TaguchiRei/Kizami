using Kizami.BlackBoard;
using UnityEngine.XR;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 実行環境からビルドモードを判定し、BuildModeState を生成して AppBoard へ登録するユースケース。
    /// ビルドモードは起動時に一度だけ決め、そのあとは変えない。
    /// </summary>
    public sealed class BuildModeService
    {
        private readonly BuildModeState _state = new();

        /// <summary>
        /// ビルドモードを判定して BuildModeState を AppBoard へ登録する。
        /// </summary>
        /// <param name="blackBoard">BuildModeState の登録先</param>
        /// <param name="editorOverride">エディタ上で固定するビルドモード。null なら実行環境から判定する。ビルドでは使わない</param>
        public void Initialize(IBlackBoard blackBoard, BuildMode? editorOverride)
        {
            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            _state.SetBuildMode(Resolve(editorOverride));
            appBoard.RegisterGameState<IBuildModeState>(_state);
        }

        /// <summary>
        /// 実行環境からビルドモードを判定する。
        /// XR デバイスが動いているかを最初に見る為、PCVR も VR として扱える。
        /// </summary>
        /// <param name="editorOverride">エディタ上で固定するビルドモード。null なら実行環境から判定する</param>
        private static BuildMode Resolve(BuildMode? editorOverride)
        {
#if UNITY_EDITOR
            if (editorOverride.HasValue) return editorOverride.Value;
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
