using System;
using Kizami.Application.Runtime.Enemy;
using Kizami.Application.Runtime.Player;
using Kizami.Domain.Runtime.Enemy;
using Kizami.Infrastructure.Runtime.Enemy;
using Kizami.Presentation.Runtime;
using Kizami.Utility.Runtime.Enemy;
using Kizami.View.Runtime.Enemy;
using UnityEngine;
using UsefulTools.UtilityUnity.Runtime.Initialize;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.Composition.Runtime.Enemy
{
    public class EnemyInitializer : InitializerBase, IInjectable<IPlayerDataGateway>
    {
        [SerializeField] private EnemyInfra _enemyInfra;
        [SerializeField] private AllEnemyManager _allEnemyManager;
        [SerializeField] private Vector3 centerPosition;

        [Header("敵全体の設定")] [SerializeField] private int _colonyCount;
        [SerializeField] private int _colonyEnemyCount;

        [Header("敵小隊のの設定")] [SerializeField] private float _moveForce;
        [SerializeField] private float _colonyRepulsionRadius;
        [SerializeField] private float _colonyRepulsionForce;
        [SerializeField] private float _playerRepulsionRadius;
        [SerializeField] private float _playerRepulsionForce;
        [SerializeField] private float _maxSpeed;

        [Header("敵単体ごとの設定")] [SerializeField] private float _moveSpeed;

        [Header("アルキメデスの螺旋の設定")] [SerializeField]
        private float _angleStep;

        [SerializeField] private float _radiusStep;

        private EnemyMoveApplication _enemyMoveApplication;
        private AllEnemyManagementPresenter _enemyPresenter;
        private IPlayerDataGateway _playerDataGateway;

        public override void Initialize()
        {
            _allEnemyManager.EnemyCount = _colonyEnemyCount * _colonyCount;
            _allEnemyManager.Initialize();
            _enemyPresenter = new AllEnemyManagementPresenter(_allEnemyManager);

            EnemyColony[] colonies = new EnemyColony[_colonyCount];
            for (int i = 0; i < _colonyCount; i++)
            {
                EnemyData[] data = new EnemyData[_colonyEnemyCount];
                for (int k = 0; k < _colonyEnemyCount; k++)
                {
                    data[k] = new EnemyData(Vector3.zero, Vector3.zero, _moveSpeed, k);
                }

                colonies[i] = new EnemyColony(data, Vector3.zero);
            }

            EnemyGroup group = new(colonies, Vector3.zero, _moveForce, _colonyRepulsionRadius,
                _colonyRepulsionForce, _playerRepulsionRadius, _playerRepulsionForce, _maxSpeed);

            ArchimedesSpiral archimedes = new ArchimedesSpiral(_angleStep, _radiusStep);

            _enemyMoveApplication = new(group, archimedes, _enemyPresenter, _playerDataGateway, _enemyInfra);
        }

        public void Inject(IPlayerDataGateway obj)
        {
            _playerDataGateway = obj;
        }
    }
}