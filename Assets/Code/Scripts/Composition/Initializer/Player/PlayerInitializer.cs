using Kizami.Application.Runtime;
using Kizami.Composition.Runtime.Player;
using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.Initialize;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
using UsefulVr.Composition.Runtime.Player;

namespace Kizami.Composition.Runtime
{
    public class PlayerInitializer : InitializerBase, IInjectable<IInputDispatcher, IBladePresenter>
    {
        [SerializeField] private bool _isVr;
        [SerializeField] private VrPlayerInitializer _vrPlayerInitializer;
        [SerializeField] private MobilePlayerInitializer _mobilePlayerInitializer;

        private IInputDispatcher _inputDispatcher;
        private IBladePresenter _bladePresenter;

        public override void Initialize()
        {
            base.Initialize();
            if (_isVr)
            {
                _vrPlayerInitializer.Initialize(_inputDispatcher, _bladePresenter);
                _mobilePlayerInitializer.gameObject.SetActive(false);
            }
            else
            {
                _mobilePlayerInitializer.Initialize(_inputDispatcher, _bladePresenter);
                _vrPlayerInitializer.gameObject.SetActive(false);
            }
        }

        public void Inject(IInputDispatcher obj, IBladePresenter presenter)
        {
            _inputDispatcher = obj;
            _bladePresenter = presenter;
        }
    }
}