using UnityEngine;
using UsefulTools.AutoGenerate;
using UsefulTools.Infrastructure.Runtime.Input;

namespace Kizami.Application.Runtime
{
    public class SmartphonePlayerService
    {
        private IInputDispatcher _inputDispatcher;
        private IBladePresenter _bladePresenter;

        public SmartphonePlayerService(IInputDispatcher inputDispatcher, IBladePresenter bladePresenter)
        {
            _inputDispatcher = inputDispatcher;
            _bladePresenter = bladePresenter;
        }

        private void Registration(bool isRegister)
        {
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.Move,
                OnMove, isRegister);
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.Look,
                OnLook, isRegister);
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackHorizontal,
                OnAttackHorizontal, isRegister);
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackVertical,
                OnAttackVertical, isRegister);
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackUpperLeft,
                OnAttackUpperLeft, isRegister);
            _inputDispatcher.RegistrationStarted<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackUpperRight,
                OnAttackUpperRight, isRegister);
        }

        private void OnMove(InputContext<Vector2> input)
        {
        }

        private void OnLook(InputContext<Vector2> input)
        {
        }

        private void OnAttackHorizontal(InputContext<Vector2> input)
        {
        }

        private void OnAttackVertical(InputContext<Vector2> input)
        {
        }

        private void OnAttackUpperLeft(InputContext<Vector2> input)
        {
        }

        private void OnAttackUpperRight(InputContext<Vector2> input)
        {
        }

        private void OnAttack(InputContext<Vector2> input, Quaternion cutFaceRotation)
        {
        }
    }
}