using System;
using Kizami.Domain.Runtime.Player;
using UnityEngine;
using UsefulTools.AutoGenerate;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulVr.Domain.Runtime.Domain;

namespace Kizami.Application.Runtime.Input
{
    public class MobilePlayerService : IDisposable
    {
        private readonly IInputDispatcher _inputDispatcher;
        private readonly IBladePresenter _bladePresenter;
        private readonly IMobilePlayerPresenter _mobilePlayerPresenter;
        private readonly MobilePlayerMovementEntity _entity;

        public MobilePlayerService(
            IInputDispatcher inputDispatcher,
            IBladePresenter bladePresenter,
            IMobilePlayerPresenter mobilePlayerPresenter,
            MobilePlayerMovementEntity entity)
        {
            _inputDispatcher = inputDispatcher;
            _bladePresenter = bladePresenter;
            _mobilePlayerPresenter = mobilePlayerPresenter;
            _entity = entity;

            _inputDispatcher.EnableActionMap(ActionMaps.Player);
            _inputDispatcher.EnableInput();

            Registration(true);
        }

        private void Registration(bool isRegister)
        {
            _inputDispatcher.RegistrationReadValue<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.Move,
                OnMove, isRegister);
            _inputDispatcher.RegistrationAll<Vector2, ExternalActions>(
                ActionMaps.ExternalInput,
                ExternalActions.MobileInput,
                OnLook, isRegister);
            _inputDispatcher.RegistrationStarted<float, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackHorizontal,
                OnAttackHorizontal, isRegister);
            _inputDispatcher.RegistrationStarted<float, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackVertical,
                OnAttackVertical, isRegister);
            _inputDispatcher.RegistrationStarted<float, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackUpperLeft,
                OnAttackUpperLeft, isRegister);
            _inputDispatcher.RegistrationStarted<float, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.AttackUpperRight,
                OnAttackUpperRight, isRegister);
        }

        private void OnMove(InputContext<Vector2> input)
        {
            if (!input.IsActive || !input.IsPerformed) return;

            Vector3 currentVelocity = _mobilePlayerPresenter.Velocity;
            // 前回移動分除去
            Vector3 velocityWithoutLastMove =
                MovementLogic.CalculateVelocityAfterStop(currentVelocity, _entity.LastMovePower.Value);

            if (input.IsPerformed)
            {
                // 新規移動方向 
                Vector3 moveVector = MovementLogic.CalculateMoveVector(
                    input.Value, _entity.Gravity.Direction,
                    _entity.LookDirection.Value);
                moveVector *= _entity.MoveSpeed.Value;

                // Entity更新
                _entity.UpdateMovePower(moveVector);

                // Velocity反映
                _mobilePlayerPresenter.Velocity = velocityWithoutLastMove + moveVector;
            }
            else if (input.IsCanceled)
            {
                _mobilePlayerPresenter.Velocity = velocityWithoutLastMove;
                _entity.UpdateMovePower(Vector3.zero);
            }
        }

        private void OnLook(InputContext<Vector2> input)
        {
            Debug.Log("Input On Look");
            if (!input.IsActive || !input.IsPerformed) return;
            Debug.Log($"InputValue{input}");

            // スマホ版のOnLookはDeltaを想定しているため、そのまま感度を掛けて回転させる
            float turnAngle = input.Value.x * _entity.LookSensitivity;

            // 現在の重力の逆方向のベクトルを旋回軸とする
            Vector3 rotationAxis = -_entity.Gravity.Direction.normalized;
            Quaternion deltaRotation = Quaternion.AngleAxis(turnAngle, rotationAxis);
            _mobilePlayerPresenter.Rotation = deltaRotation * _mobilePlayerPresenter.Rotation;

            // Entityが保持しているLookDirectionも一緒に回転させて同期する
            Vector3 newLookDirection = deltaRotation * _entity.LookDirection.Value;
            _entity.UpdateLookDirection(newLookDirection.normalized);
        }

        public void ApplyGravity()
        {
            _mobilePlayerPresenter.AddForce(_entity.Gravity.GravityForce, ForceMode.Acceleration);
        }

        private void OnAttackHorizontal(InputContext<float> input)
        {
        }

        private void OnAttackVertical(InputContext<float> input)
        {
        }

        private void OnAttackUpperLeft(InputContext<float> input)
        {
        }

        private void OnAttackUpperRight(InputContext<float> input)
        {
        }

        private void OnAttack(InputContext<float> input, Quaternion cutFaceRotation)
        {
            _bladePresenter.SetRotation(cutFaceRotation);
            _bladePresenter.Cut(null);
        }

        public void Dispose()
        {
            Registration(false);
        }
    }
}