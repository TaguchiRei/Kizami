using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Presentation.Runtime.Enemy
{
    public interface IAllEnemyManagementView
    {
        EnemyData[] Enemies { get; set; }

        /// <summary>
        /// 体力を0にして爆散させる
        /// </summary>
        void Kill(int index, Vector3 respawnPoint);
    }
}