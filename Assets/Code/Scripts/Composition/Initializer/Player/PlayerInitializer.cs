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

        private IInputDispatcher inputDispatcher;
        private IBladePresenter bladePresenter;

        public override void Initialize()
        {
            base.Initialize();
            if (_isVr)
            {
                _vrPlayerInitializer.Initialize(inputDispatcher, bladePresenter);
            }
            else
            {
                _mobilePlayerInitializer.Initialize(inputDispatcher, bladePresenter);
            }
        }

        public void Inject(IInputDispatcher obj, IBladePresenter presenter)
        {
        }
    }
}