using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.XR;
using UsefulToolkit.Architecture;

namespace Kizami.Initialization
{
    public class ApplicationManagementInitializer : InitializerBase
    {
        private BuildModeState _buildModeState;

        public override void Initialize()
        {
            base.Initialize();
            _buildModeState = new BuildModeState();
#if UNITY_STANDALONE
            _buildModeState.SetBuildMode(BuildMode.PC);
#else
            _buildModeState.SetBuildMode(XRSettings.isDeviceActive ? BuildMode.VR : BuildMode.Mobile);
#endif
        }
    }
}