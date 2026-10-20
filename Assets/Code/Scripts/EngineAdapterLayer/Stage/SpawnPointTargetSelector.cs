using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 殲滅のステージの目標を選ぶ。有効で、建物が壊れていない敵のスポーン位置を候補にする。ステージシーンへ置く。
    /// </summary>
    public sealed class SpawnPointTargetSelector : TargetSelectorBase
    {
        private EnemySpawnPoint[] _spawnPoints = System.Array.Empty<EnemySpawnPoint>();

        protected override void OnInitialize()
        {
            _spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        protected override void CollectCandidates(List<Vector3> candidates)
        {
            foreach (var spawnPoint in _spawnPoints)
            {
                if (spawnPoint == null || !spawnPoint.IsEnabled || spawnPoint.IsBroken) continue;

                candidates.Add(spawnPoint.transform.position);
            }
        }
    }
}
