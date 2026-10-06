using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの開始時に敵がいる範囲。EnemySpawnSystem の子に置き、複数置ける。初期化のときだけ使う。
    /// </summary>
    public sealed class EnemyInitialSpawnArea : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("この半径（m）の円の中に置く")]
        private float _radius = 5f;

        [SerializeField, Min(0)]
        [Tooltip("置く敵の数")]
        private int _count = 1;

        /// <summary> 置く敵の数 </summary>
        public int Count => _count;

        /// <summary> 半径の円の中から、置く位置を 1 つ選ぶ </summary>
        public Vector3 GetSpawnPosition()
        {
            var offset = Random.insideUnitCircle * _radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
