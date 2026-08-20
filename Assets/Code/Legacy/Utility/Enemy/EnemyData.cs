// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace Kizami.Utility.Runtime.Enemy
{
    public struct EnemyData
    {
        public EnemyData(Vector3 targetPosition, Vector3 positionOffset, float speed, int id)
        {
            TargetPosition = targetPosition;
            PositionOffset = positionOffset;
            Speed = speed;
            Id = id;
        }

        public Vector3 TargetPosition;
        public Vector3 PositionOffset;

        public float Speed;
        public int Id;
    }
}
#endif
