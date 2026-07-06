using Kizami.Application.Runtime;
using Kizami.Application.Runtime.Player;
using Kizami.Composition.Runtime;
using Kizami.Composition.Runtime.Enemy;
using Kizami.Infrastructure.Runtime;
using MeshBreak.MeshCut.Version2;
using UnityEngine;
using UsefulTools.Composition.Runtime.Input;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
namespace UsefulTools.Composition.Runtime.Boot
{
    public class InGameBoot : MonoBehaviour
    {
        [SerializeField] private InGameContainer _container;

        [SerializeField] private InputInitializer _inputInitializer;
        [SerializeField] private UiInitializer _uiInitializer;
        [SerializeField] private EnemyInitializer _enemyInitializer;
        [SerializeField] private PlayerInitializer _playerInitializer;
        [SerializeField] private BladeInitializer _bladeInitializer;
        [SerializeField] private MeshDataCache _meshDataCache;

        private void Start()
        {
            Inject();
            Initialize();
        }

        private void Inject()
        {
            if (_enemyInitializer != null && _container.TryGet<IPlayerDataGateway>(out var argenemyInitializer_0))
            {
                _enemyInitializer.Inject(argenemyInitializer_0);
            }
            if (_playerInitializer != null && _container.TryGet<IInputDispatcher>(out var argplayerInitializer_0) && _container.TryGet<IBladePresenter>(out var argplayerInitializer_1))
            {
                _playerInitializer.Inject(argplayerInitializer_0, argplayerInitializer_1);
            }
        }

        private void Initialize()
        {
            if (_inputInitializer != null) _inputInitializer.Initialize();
            if (_uiInitializer != null) _uiInitializer.Initialize();
            if (_enemyInitializer != null) _enemyInitializer.Initialize();
            if (_playerInitializer != null) _playerInitializer.Initialize();
            if (_bladeInitializer != null) _bladeInitializer.Initialize();
            if (_meshDataCache != null) _meshDataCache.Initialize();
        }
    }
}
