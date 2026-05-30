using UnityEngine;

namespace Kizami.Domain.Runtime.Enemy
{
    public class EnemyGroup
    {
        public EnemyColony[] Colonies { get; }

        public Vector3 CenterPosition { get; private set; }

        public EnemyGroup(EnemyColony[] colonies, Vector3 centerPosition)
        {
            Colonies = colonies;
            CenterPosition = centerPosition;
        }

        public void SetCenterPosition(Vector3 centerPosition)
        {
            CenterPosition = centerPosition;
        }
    }
}