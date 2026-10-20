using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 窓のガラスの1枚の板。ボクセルにはせず、攻撃が当たったら、板を消して GlassShardPool の破片を飛ばす。
    /// 建物には板が数百枚並ぶので、割れる前の破片は持たない。破片は物理を使わず、寿命が来たらプールへ戻る。
    /// </summary>
    public sealed class GlassPane : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("割れる前の板")]
        private GameObject _intact;

        [SerializeField, Min(0f)]
        [Tooltip("割れの起点から破片を飛ばす速さ（m/s）")]
        private float _shardSpeed = 4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("破片ごとの速さのばらつきの割合")]
        private float _speedJitter = 0.4f;

        [SerializeField, Min(0f)]
        [Tooltip("破片を回す角速度の最大（rad/s）")]
        private float _maxAngularSpeed = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("割れてから破片を消すまでの秒数")]
        private float _shardLifetime = 4f;

        /// <summary> 割れたか </summary>
        public bool IsBroken { get; private set; }

        /// <summary>
        /// 板を割り、破片を origin から離れる向きへ飛ばす。割れた後は何もしない。
        /// ステージに GlassShardPool が無い間は、板が消えるだけで破片は出ない。
        /// </summary>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public void Shatter(Vector3 origin)
        {
            if (IsBroken) return;
            IsBroken = true;

            _intact.SetActive(false);
            var pool = GlassShardPool.Instance;
            if (pool != null) pool.Spawn(transform, origin, _shardSpeed, _speedJitter, _maxAngularSpeed, _shardLifetime);
        }
    }
}
