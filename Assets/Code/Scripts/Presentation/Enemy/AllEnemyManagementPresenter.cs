using Kizami.Application.Runtime.Enemy;
using Kizami.Presentation.Runtime.Enemy;
using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Presentation.Runtime
{
    public class AllEnemyManagementPresenter : IAllEnemyManagementPresenter
    {
        private readonly IAllEnemyManagementView _managementView;

        public AllEnemyManagementPresenter(IAllEnemyManagementView managementView)
        {
            _managementView = managementView;
        }

        public void SetPositionAll(Vector3[] positions)
        {
            _managementView.SetPositionAll(positions);
        }

        public void Kill(int index, Vector3 respawnPoint)
        {
            _managementView.Kill(index, respawnPoint);
        }

        public void MoveEnemy(EnemyData[] enemies)
        {
            _managementView.MoveEnemy(enemies);
        }
    }
}