using Kizami.Composition.Runtime.Player;
using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.Initialize;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
using UsefulVr.Composition.Runtime.Player;
using Kizami.Application.Runtime;

namespace Kizami.Composition.Runtime.Player
{
    /// <summary>
    /// プラットフォームに応じてプレイヤーの初期化を切り替えるクラス
    /// </summary>
    public class PlayerPlatformSelector : InitializerBase, IInjectable<IInputDispatcher>, IInjectable<IBladePresenter>
    {
        private enum PlatformType
        {
            VR,
            Mobile
        }

        [SerializeField] private PlatformType _platformType;
        [SerializeField] private VrPlayerInitializer _vrPlayerInitializer;
        [SerializeField] private MobilePlayerInitializer _mobilePlayerInitializer;

        private IInputDispatcher _inputDispatcher;
        private IBladePresenter _bladePresenter;

        public override void Initialize()
        {
            base.Initialize();

            switch (_platformType)
            {
                case PlatformType.VR:
                    if (_vrPlayerInitializer != null)
                    {
                        _vrPlayerInitializer.gameObject.SetActive(true);
                        _vrPlayerInitializer.Initialize(_inputDispatcher);
                    }
                    if (_mobilePlayerInitializer != null) _mobilePlayerInitializer.gameObject.SetActive(false);
                    break;

                case PlatformType.Mobile:
                    if (_mobilePlayerInitializer != null)
                    {
                        _mobilePlayerInitializer.gameObject.SetActive(true);
                        _mobilePlayerInitializer.Initialize(_inputDispatcher, _bladePresenter);
                    }
                    if (_vrPlayerInitializer != null) _vrPlayerInitializer.gameObject.SetActive(false);
                    break;
            }
        }

        public void Inject(IInputDispatcher obj)
        {
            _inputDispatcher = obj;
        }

        public void Inject(IBladePresenter obj)
        {
            _bladePresenter = obj;
        }
    }
}
