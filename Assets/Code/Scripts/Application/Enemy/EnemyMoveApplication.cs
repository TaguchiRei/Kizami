using Kizami.Domain.Runtime.Enemy;
using Kizami.Utility.Runtime.Enemy;
using Kizami.Application.Runtime.Player;
using UnityEngine;

namespace Kizami.Application.Runtime.Enemy
{
    public class EnemyMoveApplication
    {
        private readonly EnemyGroup _group;
        private readonly ArchimedesSpiral _spiral;
        private readonly IAllEnemyManagementPresenter _presenter;
        private readonly IPlayerDataGateway _player;

        public EnemyMoveApplication(
            EnemyGroup group,
            ArchimedesSpiral spiral,
            IAllEnemyManagementPresenter presenter,
            IPlayerDataGateway player)
        {
            _group = group;
            _spiral = spiral;
            _presenter = presenter;
            _player = player;
        }

        public void Update(float deltaTime)
        {
            // プレイヤー位置取得
            Vector3 playerPosition = _player.Position;


            // 群れ全体の移動
            _group.Update(playerPosition, deltaTime);


            // 各コロニーの螺旋配置更新
            EnemyColony[] colonies = _group.Colonies;

            for (int i = 0; i < colonies.Length; i++)
            {
                _spiral.UpdateTargetPosition(colonies[i]);
            }

            //計算結果の適用
            _presenter.MoveEnemy(Collect(colonies));
        }

        private EnemyData[] Collect(EnemyColony[] colonies)
        {
            int total = 0;

            for (int i = 0; i < colonies.Length; i++)
            {
                total += colonies[i].Enemies.Length;
            }

            EnemyData[] result = new EnemyData[total];

            int index = 0;

            for (int i = 0; i < colonies.Length; i++)
            {
                EnemyData[] enemies = colonies[i].Enemies;

                for (int j = 0; j < enemies.Length; j++)
                {
                    result[index++] = enemies[j];
                }
            }

            return result;
        }
    }
}