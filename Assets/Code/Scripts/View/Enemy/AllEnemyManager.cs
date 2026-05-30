using Kizami.Presentation.Runtime.Enemy;
using Kizami.Utility.Runtime.Enemy;
using UnityEngine;
using UsefulTools.Utility.Runtime.Utility;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.View.Runtime.Enemy
{
    public class AllEnemyManager : InitializableMonoBehaviour, IAllEnemyManagementView
    {
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
                effects[i].gameObject.transform.position = Vector3.forward * 2;
            }

            _effectBuffer = new(effects);
        }

        public void Kill(int index, Vector3 respawnPoint)
        {
            var enemy = _enemies[index];

            _effectBuffer.Get().PlayEffect(enemy.transform.position);

            _enemies[index].transform.position = respawnPoint;
        }

        public void MoveEnemy(EnemyData[] enemies)
        {
            for (int i = 0; i < _enemies.Length; i++)
            {
                var enemyData = enemies[i];
                _enemies[i].position =
                    Vector3.MoveTowards(_enemies[i].position, enemyData.TargetPosition,
                        enemyData.Speed * Time.deltaTime);
            }
        }
    }
}