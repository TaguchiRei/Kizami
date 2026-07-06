using System;
using UnityEngine;
using UsefulTools.AutoGenerate;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulVr.Domain.Runtime.Domain;
using UsefulVr.Domain.Runtime.Player;

namespace UsefulVr.Application.Runtime.Player
{
    /// <summary>
    /// プレイヤー移動・回転ユースケース
    /// </summary>
    public class VrPlayerMovementService : IDisposable
    {
        private const float CAMERA_OFFSET_THRESHOLD = 0.25f;

        private readonly IInputDispatcher _inputDispatcher;
        private readonly IVrPlayerPresenter _vrPlayerPresenter;
        private readonly VrPlayerMovementEntity _entity;

        public VrPlayerMovementService(
            IVrPlayerPresenter vrPlayerPresenter,
            VrPlayerMovementEntity entity,
            IInputDispatcher inputDispatcher)
        {
            _vrPlayerPresenter = vrPlayerPresenter;
            _entity = entity;
            _inputDispatcher = inputDispatcher;

            _inputDispatcher.EnableActionMap(ActionMaps.VRControllers);
            _inputDispatcher.EnableInput();

            Registration(true);
        }

        /// <summary>
        /// 入力移動
        /// </summary>
        public void Move(InputContext<Vector2> input)
        {
            if (!input.IsActive) return;

            Vector3 currentVelocity = _vrPlayerPresenter.Velocity;
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
                _vrPlayerPresenter.Velocity = velocityWithoutLastMove + moveVector;
            }
            else if (input.IsCanceled)
            {
                _vrPlayerPresenter.Velocity = velocityWithoutLastMove;
            }
        }

        /// <summary>
        /// コントローラー入力から視点を左右に振る処理
        /// </summary>
        public void Look(InputContext<Vector2> input)
        {
            if (!input.IsActive || Mathf.Abs(input.Value.x) < _entity.DeadZone) return;

            float turnInput = input.Value.x;
            float turnAngle = turnInput * _entity.LookSpeed * Time.deltaTime;

            // 現在の重力の逆方向のベクトルを旋回軸とする
            Vector3 rotationAxis = -_entity.Gravity.Direction.normalized;
            Quaternion deltaRotation = Quaternion.AngleAxis(turnAngle, rotationAxis);
            _vrPlayerPresenter.Rotation = deltaRotation * _vrPlayerPresenter.Rotation;

            // 本体が回転したため、Entityが保持しているLookDirectionも一緒に回転させて同期する
            Vector3 newLookDirection = deltaRotation * _entity.LookDirection.Value;
            _entity.UpdateLookDirection(newLookDirection.normalized);
        }

        /// <summary>
        /// 重力適用
        /// </summary>
        public void ApplyGravity()
        {
            _vrPlayerPresenter.AddForce(_entity.Gravity.GravityForce, ForceMode.Acceleration);
        }
        /// <summary>
        /// HMD位置補正
        /// </summary>
        public void ApplyCameraOffset()
        {
            Vector3 cameraLocalPosition = _vrPlayerPresenter.CameraLocalPosition;
            Vector3 offset = Vector3.ProjectOnPlane(cameraLocalPosition, _entity.Gravity.Direction.normalized);

            if (offset.sqrMagnitude < CAMERA_OFFSET_THRESHOLD * CAMERA_OFFSET_THRESHOLD)
            {
                return;
            }

            _vrPlayerPresenter.SetOffset(offset);
        }


        /// <summary>
        /// 入力イベントの登録状態を変更する
        /// </summary>
        private void Registration(bool isRegister)
        {
            // 移動の登録
            _inputDispatcher.RegistrationReadValue<Vector2, VRControllersActions>(
                ActionMaps.VRControllers,
                VRControllersActions.Move,
                Move,
                isRegister);

            // 回転入力の登録 
            _inputDispatcher.RegistrationReadValue<Vector2, VRControllersActions>(
                ActionMaps.VRControllers,
                VRControllersActions.Look,
                Look,
                isRegister);
        }

        public void Dispose()
        {
            Registration(false);
        }
    }
}