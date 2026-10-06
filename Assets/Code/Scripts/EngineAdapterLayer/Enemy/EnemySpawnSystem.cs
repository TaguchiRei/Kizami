using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージごとの敵の出し方の設定。ステージシーンに置き、子に EnemySpawnPoint と EnemyInitialSpawnArea を置く。
    /// インゲームの EnemySpawnAdapter が FindAnyObjectByType で見つけて読む。
    /// </summary>
    public sealed class EnemySpawnSystem : MonoBehaviour
    {
        [SerializeField, Min(0)]
        [Tooltip("同時に存在する敵の数の上限。インゲームの EnemySpawnAdapter は、この数の敵の状態を作る")]
        private int _maxAliveCount = 10;

        [SerializeField]
        [Tooltip("実行中に出す敵の情報")]
        private EnemySpawnInfo[] _spawnInfos = Array.Empty<EnemySpawnInfo>();

        /// <summary> 同時に存在する敵の数の上限 </summary>
        public int MaxAliveCount => _maxAliveCount;

        /// <summary> 実行中に出す敵の情報 </summary>
        public IReadOnlyList<EnemySpawnInfo> SpawnInfos => _spawnInfos;
    }

    /// <summary>
    /// 実行中に出す敵の情報 1 件。間隔ごとに、生成位置 1 つから上限の数まで出す。
    /// </summary>
    // TODO: 区間9で、出す敵の種類を持たせる
    [Serializable]
    public sealed class EnemySpawnInfo
    {
        [SerializeField, Min(1)]
        [Tooltip("一度に出す数の上限")]
        private int _maxCountPerSpawn = 1;

        [SerializeField, Min(0.1f)]
        [Tooltip("出す間隔（秒）")]
        private float _interval = 3f;

        /// <summary> 一度に出す数の上限 </summary>
        public int MaxCountPerSpawn => _maxCountPerSpawn;

        /// <summary> 出す間隔（秒） </summary>
        public float Interval => _interval;
    }
}
