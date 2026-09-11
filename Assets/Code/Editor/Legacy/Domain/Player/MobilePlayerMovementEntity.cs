// [Legacy] 作り直しに伴い全体を無効化
#if false
using Code.Scripts.Domain.Player;
using UnityEngine;
using UsefulVr.Domain.Runtime.Player;

namespace Kizami.Domain.Runtime.Player
{
    /// <summary>
    /// モバイルプレイヤーの移動状態を管理するエンティティ
    /// </summary>
    public class MobilePlayerMovementEntity
    {
        public GravityValue Gravity { get; private set; }
        public MovePowerValue LastMovePower { get; private set; }
        public LookDirectionValue LookDirection { get; private set; }
        public MoveSpeed MoveSpeed { get; private set; }
        public float LookSensitivity { get; private set; }
        public bool IsMoving { get; private set; }
        public Vector2 InputVector { get; private set; }

        public MobilePlayerMovementEntity(
            GravityValue gravity,
            MoveSpeed moveSpeed,
            float lookSensitivity)
        {
            Gravity = gravity;
            MoveSpeed = moveSpeed;
            LookSensitivity = lookSensitivity;

            LastMovePower = MovePowerValue.Zero;

            // 初期値としてのみ使用
            LookDirection = new LookDirectionValue(Vector3.forward);
        }

        public void UpdateMovementState(bool isMoving, Vector2 inputVector)
        {
            IsMoving = isMoving;
            InputVector = inputVector;
        }

        public void UpdateMovePower(Vector3 newPower)
        {
            LastMovePower = new MovePowerValue(newPower);
        }

        public void UpdateGravity(GravityValue gravity)
        {
            Gravity = gravity;
        }

        public void UpdateLookDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            LookDirection =
                new LookDirectionValue(direction.normalized);
        }

        public void UpdateMoveSpeed(MoveSpeed moveSpeed)
        {
            MoveSpeed = moveSpeed;
        }
    }
}
#endif
