using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace UsefulTools.Composition.Runtime.Input
{
    public class InputInitializer : InitializerBase
    {
        [SerializeField] private InputDispatcher _inputDispatcher;

        private void Awake()
        {
            //SceneBootに登録する処理
        }

        public override void Initialize()
        {
            base.Initialize();
            _inputDispatcher.Initialize();
        }
    }
}