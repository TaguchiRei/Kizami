// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;
using UsefulTools.Infrastructure.Runtime;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.Infrastructure.Runtime
{
    public class UiInitializer : InitializerBase
    {
        [SerializeField] private bool _isVr;
        [SerializeField] private GameObject _mobileCanvas;
        [SerializeField] private MobileInput _mobileInput;
        [SerializeField] private GameObject _vrUi;

        public override void Initialize()
        {
            base.Initialize();
            if (_isVr)
            {
                Destroy(_mobileCanvas);
            }
            else
            {
                _mobileInput.Initialize();
                if (_vrUi != null) Destroy(_vrUi);
            }
        }
    }
}
#endif
