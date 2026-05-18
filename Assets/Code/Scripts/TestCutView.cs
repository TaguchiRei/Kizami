using UnityEngine;
using UsefulAttribute;
using UsefulTools.AutoGenerate;
using UsefulTools.Infrastructure.Runtime.Input;

namespace View
{
    public class TestCutView : MethodExecutorBehaviour
    {
        [SerializeField] private MultiCutBlade _blade;

        private IInputDispatcher _inputDispatcher;

        private void Start()
        {
            // CompositionRootから注入されることを想定していますが、
            // テスト用に簡易的に取得を試みます
            _inputDispatcher = FindFirstObjectByType<InputDispatcher>();

            if (_inputDispatcher != null)
            {
                _inputDispatcher.RegistrationPerformed<float, VRControllersActions>(
                    ActionMaps.Player,
                    VRControllersActions.PushGripLeft,
                    OnAttackPerformed,
                    true
                );
            }
        }

        private void OnDestroy()
        {
            if (_inputDispatcher != null)
            {
                _inputDispatcher.RegistrationPerformed<float, VRControllersActions>(
                    ActionMaps.Player,
                    VRControllersActions.PushGripLeft,
                    OnAttackPerformed,
                    false
                );
            }
        }

        private void OnAttackPerformed(InputContext<float> context)
        {
            if (_blade == null) return;

            _blade.Test();
        }
    }
}