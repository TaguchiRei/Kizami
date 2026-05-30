using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Domain.Runtime.Enemy
{
    public class ArchimedesSpiral
    {
        private readonly float _angleStep;
        private readonly float _radiusStep;

        public ArchimedesSpiral(float angleStep, float radiusStep)
        {
            _angleStep = angleStep;
            _radiusStep = radiusStep;
        }

        /// <summary>
        /// 引数に受け取ったColonyの目標座標を更新する
        /// </summary>
        /// <param name="colony"></param>
        public void UpdateTargetPosition(EnemyColony colony)
        {
            EnemyData[] enemies = colony.Enemies;

            for (int i = 0; i < enemies.Length; i++)
            {
                ref EnemyData enemy = ref enemies[i];

                float angle = (enemy.Id + colony.GlobalIndex) * _angleStep;

                if (!colony.IsClockwise)
                {
                    angle = -angle;
                }

                float radius = angle * _radiusStep;

                Vector3 spiralPosition = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                enemy.TargetPosition = colony.CenterPosition + spiralPosition + enemy.PositionOffset;
            }
        }
    }
}