using Code.Scripts.Domain.Player;
using UnityEngine;

namespace UsefulVr.Domain.Runtime.Player
{
    /// <summary>
    /// プレイヤーの移動状態を管理するエンティティ
    /// </summary>
    public class VrPlayerMovementEntity
    {
        public GravityValue Gravity { get; private set; }
        public MovePowerValue LastMovePower { get; private set; }
        public LookDirectionValue LookDirection { get; private set; }
        public MoveSpeed MoveSpeed { get; private set; }

        public float LookSpeed { get; private set; }

        public float DeadZone { get; private set; }

        public VrPlayerMovementEntity(
            GravityValue gravity,
            MoveSpeed moveSpeed,
            float lookSpeed,
            float deadZone)
        {
            Gravity = gravity;
            MoveSpeed = moveSpeed;

            LastMovePower = MovePowerValue.Zero;

            LookSpeed = lookSpeed;
            DeadZone = deadZone;

            // 初期値としてのみ使用
            LookDirection = new LookDirectionValue(Vector3.forward);
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