using System;
using Kizami.Presentation.Runtime.Enemy;
using Kizami.Utility.Runtime.Enemy;
using UnityEngine;
using UnityEngine.VFX;
using UsefulTools.Utility.Runtime.Utility;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.View.Runtime.Enemy
{
    public class AllEnemyManager : InitializableMonoBehaviour, IAllEnemyManagementView
    {
        public int EnemyCount { get; private set; }
        public EnemyData[] Enemies { get; set; }

        [SerializeField] private Transform[] _enemies;
        [SerializeField] private Transform[] _enemyPrefab;
        [SerializeField] private EnemyEffectView _killVfxPrefab;

        [Header("エフェクト")] [SerializeField] private int _bufferCount;

        private RecycleBuffer<EnemyEffectView> _effectBuffer;

        public override void Initialize()
        {
            base.Initialize();

            EnemyEffectView[] effects = new EnemyEffectView[_bufferCount];

            for (int i = 0; i < _bufferCount; i++)
            {
                effects[i] = Instantiate(_killVfxPrefab, Camera.main.transform);
            }

            _effectBuffer = new(effects);
        }

        public void Kill(int index, Vector3 respawnPoint)
        {
            var enemy = _enemies[index];

            _effectBuffer.Get().PlayEffect(enemy.transform.position);

            _enemies[index].transform.position = respawnPoint;
        }

        public void Update()
        {
            for (int i = 0; i < _enemies.Length; i++)
            {
            }
        }
    }
}