using System;
using Kizami.Application.Runtime.Player;
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
        private readonly IPlayerInfra _playerInfra;

        public MobilePlayerService(
            IInputDispatcher inputDispatcher,
            IBladePresenter bladePresenter,
            IMobilePlayerPresenter mobilePlayerPresenter,
            MobilePlayerMovementEntity entity,
            IPlayerInfra playerInfra)
        {
            _inputDispatcher = inputDispatcher;
            _bladePresenter = bladePresenter;
            _mobilePlayerPresenter = mobilePlayerPresenter;
            _entity = entity;
            _playerInfra = playerInfra;

            _playerInfra.UpdateEvent += OnUpdate;

            _inputDispatcher.EnableActionMap(ActionMaps.Player);
            _inputDispatcher.EnableInput();

            Registration(true);
        }

        private void Registration(bool isRegister)
        {
            _inputDispatcher.RegistrationReadValue<Vector2, PlayerActions>(
                ActionMaps.Player,
                PlayerActions.Move,
                OnMoveInput, isRegister);
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

        private void OnMoveInput(InputContext<Vector2> input)
        {
            if (input.IsPerformed)
            {
                _entity.UpdateMovementState(true, input.Value);
            }
            else if (input.IsCanceled)
            {
                _entity.UpdateMovementState(false, Vector2.zero);
                
                // 停止処理
                Vector3 currentVelocity = _mobilePlayerPresenter.Velocity;
                Vector3 velocityWithoutLastMove =
                    MovementLogic.CalculateVelocityAfterStop(currentVelocity, _entity.LastMovePower.Value);
                _mobilePlayerPresenter.Velocity = velocityWithoutLastMove;
                _entity.UpdateMovePower(Vector3.zero);
            }
        }

        private void OnUpdate()
        {
            if (!_entity.IsMoving) return;

            Vector3 currentVelocity = _mobilePlayerPresenter.Velocity;
            // 前回移動分除去
            Vector3 velocityWithoutLastMove =
                MovementLogic.CalculateVelocityAfterStop(currentVelocity, _entity.LastMovePower.Value);

            // 新規移動方向 
            Vector3 moveVector = MovementLogic.CalculateMoveVector(
                _entity.InputVector, _entity.Gravity.Direction,
                _entity.LookDirection.Value);
            moveVector *= _entity.MoveSpeed.Value;

            // Entity更新
            _entity.UpdateMovePower(moveVector);

            // Velocity反映
            _mobilePlayerPresenter.Velocity = velocityWithoutLastMove + moveVector;
        }

        private void OnLook(InputContext<Vector2> input)
        {
            if (!input.IsActive || !input.IsPerformed) return;

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
            OnAttack(input, Quaternion.identity);
        }

        private void OnAttackVertical(InputContext<float> input)
        {
            OnAttack(input, Quaternion.Euler(0f, 0f, 90));
        }

        private void OnAttackUpperLeft(InputContext<float> input)
        {
            OnAttack(input, Quaternion.Euler(0f, 0f, 135));
        }

        private void OnAttackUpperRight(InputContext<float> input)
        {
            OnAttack(input, Quaternion.Euler(0f, 0f, 45));
        }

        private void OnAttack(InputContext<float> input, Quaternion cutFaceRotation)
        {
            if (input.IsStarted)
            {
                Debug.Log("切断処理が呼ばれた");
                _bladePresenter.SetRotation(cutFaceRotation * _bladePresenter.DefaultRotation);
                _bladePresenter.Cut(null);
            }
        }

        public void Dispose()
        {
            Registration(false);
            _playerInfra.UpdateEvent -= OnUpdate;
        }
    }
}