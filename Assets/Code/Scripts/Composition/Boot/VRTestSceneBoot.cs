using UnityEngine;
using UsefulTools.Composition.Runtime.Input;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulVr.Composition.Runtime.Player;

namespace VRTest.Composition.Runtime.Boot
{
    public class VRTestSceneBoot : MonoBehaviour
    {
        [SerializeField] private VRTestSceneContainer _container;

        [SerializeField] private InputInitializer _inputInitializer;
        [SerializeField] private VrPlayerInitializer vrPlayerInitializer;

        private void Start()
        {
            Inject();
            Initialize();
        }

        private void Inject()
        {
            if (vrPlayerInitializer != null && _container.TryGet<IInputDispatcher>(out var arg_playerInitializer_0))
            {
                vrPlayerInitializer.Inject(arg_playerInitializer_0);
            }
        }

        private void Initialize()
        {
            if (_inputInitializer != null) _inputInitializer.Initialize();
            if (vrPlayerInitializer != null) vrPlayerInitializer.Initialize();
        }
    }
}
