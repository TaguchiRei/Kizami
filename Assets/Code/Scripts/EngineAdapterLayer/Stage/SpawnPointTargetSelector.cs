using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 殲滅のステージの目標を選ぶ。敵を出せるスポーン位置（有効で、保留されておらず、建物が壊れていないもの）を候補にする。台本が保留しているスポーン位置は、解放されるまで候補にしない。ステージシーンへ置く。
    /// </summary>
    public sealed class SpawnPointTargetSelector : TargetSelectorBase
    {
        private EnemySpawnPoint[] _spawnPoints = System.Array.Empty<EnemySpawnPoint>();

        protected override void OnInitialize()
        {
            // スポーン位置は街のシーンにあり、ステージシーンからは Inspector で渡せない為、探す
            _spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        protected override void CollectCandidates(List<Vector3> candidates)
        {
            foreach (var spawnPoint in _spawnPoints)
            {
                if (spawnPoint == null || !spawnPoint.CanSpawn) continue;

                candidates.Add(spawnPoint.transform.position);
            }
        }
    }
}
