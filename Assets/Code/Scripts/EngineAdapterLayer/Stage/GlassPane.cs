using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 窓のガラスの1枚の板。ボクセルにはせず、攻撃が当たったら、あらかじめ割っておいた破片に差し替えて飛ばす。
    /// 破片は割れたときにプレハブから板の位置・向き・大きさで生成し、シーンの直下へ出して Rigidbody を付ける。板と一緒に動く建物から切り離して落とす為。
    /// 建物には板が数百枚並ぶので、割れる前の破片は持たない。破片は寿命が来たら消し、拾い直さない。
    /// </summary>
    public sealed class GlassPane : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("割れる前の板")]
        private GameObject _intact;

        [SerializeField]
        [Tooltip("破片をまとめたプレハブ。子の1つ1つが破片で、BoxCollider を付けておく。板と同じ原点で作る")]
        private GameObject _shardsPrefab;

        [SerializeField, Min(0f)]
        [Tooltip("割れの起点から破片を飛ばす速さ（m/s）")]
        private float _shardSpeed = 4f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("破片ごとの速さのばらつきの割合")]
        private float _speedJitter = 0.4f;

        [SerializeField, Min(0f)]
        [Tooltip("破片を回す角速度の最大（rad/s）")]
        private float _maxAngularSpeed = 10f;

        [SerializeField, Min(0.01f)]
        [Tooltip("破片の密度（kg/m³）。質量は 破片の大きさ × 密度")]
        private float _density = 2500f;

        [SerializeField, Min(0f)]
        [Tooltip("割れてから破片を消すまでの秒数")]
        private float _shardLifetime = 4f;

        /// <summary> 割れたか </summary>
        public bool IsBroken { get; private set; }

        /// <summary>
        /// 板を割り、破片を origin から離れる向きへ飛ばす。割れた後は何もしない。
        /// </summary>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public void Shatter(Vector3 origin)
        {
            if (IsBroken) return;
            IsBroken = true;

            _intact.SetActive(false);
            var shards = Instantiate(_shardsPrefab, transform, false).transform;

            // 子を外すと並びが変わるので、後ろから取り出す
            for (var i = shards.childCount - 1; i >= 0; i--)
            {
                var shard = shards.GetChild(i);
                shard.SetParent(null, true);

                var size = shard.TryGetComponent(out BoxCollider box) ? Vector3.Scale(box.size, shard.lossyScale) : Vector3.one * 0.01f;
                var body = shard.gameObject.AddComponent<Rigidbody>();
                body.mass = Mathf.Max(0.01f, size.x * size.y * size.z * _density);
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                var away = shard.position - origin;
                var direction = away.sqrMagnitude > 1e-6f ? away.normalized : transform.forward;
                body.linearVelocity = direction * (_shardSpeed * (1f + Random.Range(-_speedJitter, _speedJitter)));
                body.angularVelocity = Random.insideUnitSphere * _maxAngularSpeed;

                Destroy(shard.gameObject, _shardLifetime);
            }

            Destroy(shards.gameObject);
        }
    }
}
