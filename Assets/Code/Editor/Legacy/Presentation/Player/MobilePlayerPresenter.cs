// [Legacy] 作り直しに伴い全体を無効化
#if false
using Kizami.Application.Runtime;
using Kizami.Presentation.Runtime.Player;
using UnityEngine;

namespace Kizami.Presentation.Runtime
{
    public class MobilePlayerPresenter : IMobilePlayerPresenter
    {
        public Vector3 Velocity
        {
            get => _mobilePlayerView.Velocity;
            set => _mobilePlayerView.Velocity = value;
        }

        public Quaternion Rotation
        {
            get => _mobilePlayerView.Rotation;
            set => _mobilePlayerView.Rotation = value;
        }

        private IMobilePlayerView _mobilePlayerView;

        public MobilePlayerPresenter(IMobilePlayerView mobilePlayerView)
        {
            _mobilePlayerView = mobilePlayerView;
        }

        public void AddForce(Vector3 force, ForceMode mode)
        {
            _mobilePlayerView.AddForce(force, mode);
        }
    }
}
#endif
