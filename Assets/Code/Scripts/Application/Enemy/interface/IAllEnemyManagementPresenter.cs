using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Application.Runtime.Enemy
{
    public interface IAllEnemyManagementPresenter
    {
        /// <summary>
        /// 体力を0にして爆散させる
        /// </summary>
        void Kill(int index, Vector3 respawnPoint);

        void MoveEnemy(EnemyData[] enemies);
    }
}