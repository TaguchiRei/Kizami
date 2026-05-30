using Kizami.Utility.Runtime.Enemy;
using UnityEngine;

namespace Kizami.Domain.Runtime.Enemy
{
    /// <summary>
    /// 敵の群体情報を保持するクラス
    /// </summary>
    public class EnemyColony
    {
        public EnemyData[] Enemies { get; }

        /// <summary> 群体全体のインデックスオフセット </summary>
        public int GlobalIndex { get; private set; }

        /// <summary> 回転方向　 </summary>
        public bool IsClockwise { get; private set; }

        /// <summary> 群体の中心座標 </summary>
        public Vector3 CenterPosition { get; private set; }

        public EnemyColony(
            EnemyData[] enemies,
            Vector3 centerPosition)
        {
            Enemies = enemies;
            CenterPosition = centerPosition;
        }

        public void SetCenterPosition(Vector3 centerPosition)
        {
            CenterPosition = centerPosition;
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