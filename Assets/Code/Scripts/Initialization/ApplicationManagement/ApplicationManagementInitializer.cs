using Kizami.BlackBoard;
using UnityEngine.XR;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    public class ApplicationManagementInitializer : InitializerBase
    {
        private BuildModeState _buildModeState;

        public override void Initialize(IBlackBoard blackBoard)
        {
            base.Initialize( blackBoard);
            _buildModeState = new BuildModeState();
#if UNITY_STANDALONE
            _buildModeState.SetBuildMode(BuildMode.PC);
#else
            _buildModeState.SetBuildMode(XRSettings.isDeviceActive ? BuildMode.VR : BuildMode.Mobile);
#endif
        }
    }
}