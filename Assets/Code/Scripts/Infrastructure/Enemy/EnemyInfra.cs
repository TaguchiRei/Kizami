using System;
using Kizami.Application.Runtime;
using UnityEngine;

namespace Kizami.Infrastructure.Runtime.Enemy
{
    public class EnemyInfra : MonoBehaviour, IEnemyMoveInfra
    {
        public event Action<float> UpdateEvent;

        private void Update()
        {
            UpdateEvent?.Invoke(Time.deltaTime);
        }
    }
}