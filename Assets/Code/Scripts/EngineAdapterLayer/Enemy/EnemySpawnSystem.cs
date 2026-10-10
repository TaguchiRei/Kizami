using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージごとの敵の出し方と経路の格子の設定。ステージシーンに置き、子に EnemySpawnPoint を置く。
    /// インゲームの EnemySpawnAdapter が FindAnyObjectByType で見つけて読む。
    /// 区画は、格子の範囲の中心が区画の中心に来るように並べる。
    /// </summary>
    public sealed class EnemySpawnSystem : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("敵の経路の格子を作る範囲（ワールド座標）。床を探すレイは上面から底面まで撃つので、天井より下に置く")]
        private Bounds _navigationBounds = new(Vector3.zero, new Vector3(100f, 30f, 100f));

        [SerializeField, Min(1f)]
        [Tooltip("距離マップを計算する区画の一辺（m）。プレイヤーのいる区画とその周りの 3×3（追跡範囲）だけを計算し、持ち場がその外のグループは追わない。区画は格子の範囲の中心が区画の中心に来るように並べる")]
        private float _sectionSize = 100f;

        /// <summary> 敵の経路の格子を作る範囲（ワールド座標） </summary>
        public Bounds NavigationBounds => _navigationBounds;

        /// <summary> 距離マップを計算する区画の一辺（m） </summary>
        public float SectionSize => _sectionSize;

        /// <summary> 番号 (0, 0) の区画の最小の角（ワールド座標の x, z） </summary>
        public Vector2 SectionOrigin =>
            new Vector2(_navigationBounds.center.x, _navigationBounds.center.z) - Vector2.one * (_sectionSize * 0.5f);

        /// <summary>
        /// 位置が入る区画の番号を返す。
        /// </summary>
        public Vector2Int GetSection(Vector3 position)
        {
            var local = (new Vector2(position.x, position.z) - SectionOrigin) / _sectionSize;
            return new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(_navigationBounds.center, _navigationBounds.size);
        }
    }
}
