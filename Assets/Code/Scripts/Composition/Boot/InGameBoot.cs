using Kizami.Application.Runtime;
using Kizami.Composition.Runtime;
using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
namespace UsefulTools.Composition.Runtime.Boot
{
    public class InGameBoot : MonoBehaviour
    {
        [SerializeField] private InGameContainer _container;

        [SerializeField] private PlayerInitializer _playerInitializer;

        private void Start()
        {
            Inject();
            Initialize();
        }

        private void Inject()
        {
            if (_playerInitializer != null && _container.TryGet<IInputDispatcher>(out var argplayerInitializer_0) && _container.TryGet<IBladePresenter>(out var argplayerInitializer_1))
            {
                _playerInitializer.Inject(argplayerInitializer_0, argplayerInitializer_1);
            }
        }

        private void Initialize()
        {
            if (_playerInitializer != null) _playerInitializer.Initialize();
        }
    }
}
