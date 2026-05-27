using Kizami.Application.Runtime;
using Kizami.Composition.Runtime.Player;
using UnityEngine;
using UsefulTools.Composition.Runtime.Input;
using UsefulTools.Infrastructure.Runtime.Input;

namespace VRTest.Composition.Runtime.Boot
{
    public class VRTestSceneBoot : MonoBehaviour
    {
        [SerializeField] private VRTestSceneContainer _container;

        [SerializeField] private InputInitializer _inputInitializer;
        [SerializeField] private PlayerPlatformSelector _playerPlatformSelector;

        private void Start()
        {
            Inject();
            Initialize();
        }

        private void Inject()
        {
            if (_playerPlatformSelector != null)
            {
                if (_container.TryGet<IInputDispatcher>(out var dispatcher))
                {
                    _playerPlatformSelector.Inject(dispatcher);
                }
                
                if (_container.TryGet<IBladePresenter>(out var bladePresenter))
                {
                    _playerPlatformSelector.Inject(bladePresenter);
                }
            }
        }

        private void Initialize()
        {
            if (_inputInitializer != null) _inputInitializer.Initialize();
            if (_playerPlatformSelector != null) _playerPlatformSelector.Initialize();
        }
    }
}

