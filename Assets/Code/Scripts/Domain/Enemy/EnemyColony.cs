using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Domain.Runtime.Enemy
{
    public class EnemyColony
    {
        public EnemyData[] Enemies { get; }

        public int GlobalIndex { get; private set; }

        public bool IsClockwise { get; private set; }

        public Vector3 CenterPosition { get; private set; }

        public Vector3 Velocity { get; private set; }

        public EnemyColony(EnemyData[] enemies, Vector3 centerPosition)
        {
            Enemies = enemies;
            CenterPosition = centerPosition;
        }

        public void SetCenterPosition(Vector3 centerPosition)
        {
            CenterPosition = centerPosition;
        }

        public void SetVelocity(Vector3 velocity)
        {
            Velocity = velocity;
        }

        public void IncrementIndex()
        {
            GlobalIndex++;
        }

        public void DecrementIndex()
        {
            GlobalIndex--;
        }

        public void ReverseRotation()
        {
            IsClockwise = !IsClockwise;
        }
    }
}