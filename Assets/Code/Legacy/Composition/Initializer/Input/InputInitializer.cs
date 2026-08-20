// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;
using UsefulTools.Composition.Runtime.Boot;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace UsefulTools.Composition.Runtime.Input
{
    public class InputInitializer : InitializerBase
    {
        [SerializeField] private InputDispatcher _inputDispatcher;

        private void Awake()
        {
            InGameContainer.Register<IInputDispatcher>(_inputDispatcher);
        }

        public override void Initialize()
        {
            base.Initialize();
            _inputDispatcher.Initialize();
        }
    }
}
#endif
