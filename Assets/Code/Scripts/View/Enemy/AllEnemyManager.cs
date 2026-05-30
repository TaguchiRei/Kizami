using Kizami.Presentation.Runtime.Enemy;
using Kizami.Utility.Runtime.Enemy;
using MeshBreak.MeshCut.Version2;
using UnityEngine;
using UsefulTools.Utility.Runtime.Utility;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.View.Runtime.Enemy
{
    public class AllEnemyManager : InitializableMonoBehaviour, IAllEnemyManagementView
    {
        [HideInInspector] public int EnemyCount;

        [SerializeField] private MeshDataCache _meshDataCache;
        [SerializeField] private Transform _enemyPrefab;
        [SerializeField] private EnemyEffectView _killVfxPrefab;

        [Header("エフェクト")] [SerializeField] private int _bufferCount;

        private Transform[] _enemies;
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


            for (int i = 0; i < EnemyCount; i++)
            {
                _enemies[i] = Instantiate(_enemyPrefab, _meshDataCache.transform);
            }

            _effectBuffer = new(effects);
        }

        public void SetPositionAll(Vector3[] positions)
        {
            for (int i = 0; i < _enemies.Length; i++)
            {
                _enemies[i].position = positions[i];
            }
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