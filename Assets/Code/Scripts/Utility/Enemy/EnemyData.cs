using UnityEngine;

namespace Kizami.Utility.Runtime.Enemy
{
    public struct EnemyData
    {
        public EnemyData(Vector3 targetPosition, Vector3 positionOffset, float speed)
        {
            TargetPosition = targetPosition;
            PositionOffset = positionOffset;
            Speed = speed;
        }

        public Vector3 TargetPosition;
        public Vector3 PositionOffset;

        public float Speed;
    }
}