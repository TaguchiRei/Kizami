using Kizami.Presentation.Runtime.Enemy;
using UnityEngine;

namespace Kizami.View.Runtime.Enemy
{
    public class EnemyView : MonoBehaviour, IEnemyView
    {
        public Vector3 Offset { get; set; }
        public Vector3 TargetTransform { get; set; }
        
        
        public void Kill()
        {
            
        }
    }
}
