using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Presentation.Runtime.Enemy
{
    public interface IAllEnemyManagementView
    {
        /// <summary>
        /// すべてのオブジェクトの座標を指定する
        /// </summary>
        /// <param name="positions"></param>
        void SetPositionAll(Vector3[] positions);

        /// <summary>
        /// 体力を0にして爆散させる
        /// </summary>
        void Kill(int index, Vector3 respawnPoint);

        void MoveEnemy(EnemyData[] enemies);
    }
}