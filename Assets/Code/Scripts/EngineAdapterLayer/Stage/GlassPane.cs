using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 窓のガラスの1枚の板。ボクセルにはせず、攻撃が当たったら、板を消して GlassShardPool の破片を飛ばす。
    /// 板はコライダーを持たず、割れる前の板だけが静的な一覧に載る。範囲の判定は ShatterInSphere・ShatterInCapsule が板の箱との距離で行う。
    /// 割れる前の板は動かない前提で、有効になった時点の中心と外接球の半径を持ち、遠い板を Transform を読まずに除く。
    /// 建物には板が数百枚並ぶので、コライダーを付けると崩落で建物ごと落とすときの物理の負荷になる。
    /// 破片は物理を使わず、寿命が来たらプールへ戻る。
    /// </summary>
    public sealed class GlassPane : MonoBehaviour
    {
        private static readonly List<GlassPane> _intactPanes = new();

        [SerializeField]
        [Tooltip("割れる前の板")]
        private GameObject _intact;

        [SerializeField]
        [Tooltip("範囲の判定に使う板の箱の大きさ（ローカル空間, m）。原点を中心にする")]
        private Vector3 _size = new(1.6f, 2f, 0.05f);

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

        /// <summary> 有効になった時点の、板の中心（ワールド空間） </summary>
        private Vector3 _worldCenter;

        /// <summary> 有効になった時点の、板の箱の外接球の半径（ワールド空間, m） </summary>
        private float _boundRadius;

        /// <summary> 割れたか </summary>
        public bool IsBroken { get; private set; }

        /// <summary>
        /// 球に触れた割れる前の板をすべて、origin を起点に割る。
        /// </summary>
        /// <param name="center">球の中心（ワールド空間）</param>
        /// <param name="radius">球の半径（m）</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public static void ShatterInSphere(Vector3 center, float radius, Vector3 origin)
        {
            ShatterInCapsule(center, center, radius, origin);
        }

        /// <summary>
        /// 2点を結ぶカプセルに触れた割れる前の板をすべて、origin を起点に割る。
        /// 板の中心に最も近い線分上の点を中心にした球で、板の箱との距離を調べる。
        /// </summary>
        /// <param name="start">カプセルの一方の端（ワールド空間）</param>
        /// <param name="end">カプセルのもう一方の端（ワールド空間）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public static void ShatterInCapsule(Vector3 start, Vector3 end, float radius, Vector3 origin)
        {
            var segment = end - start;
            var segmentLength = segment.sqrMagnitude;

            // 割ると一覧から外れる為、後ろから調べる
            for (var i = _intactPanes.Count - 1; i >= 0; i--)
            {
                var pane = _intactPanes[i];
                var paneCenter = pane._worldCenter;

                var t = segmentLength > 1e-8f ? Mathf.Clamp01(Vector3.Dot(paneCenter - start, segment) / segmentLength) : 0f;
                var point = start + segment * t;

                var reach = radius + pane._boundRadius;
                if ((point - paneCenter).sqrMagnitude > reach * reach) continue;

                var paneTransform = pane.transform;
                var halfExtents = Vector3.Scale(pane._size, paneTransform.lossyScale) * 0.5f;
                var local = Quaternion.Inverse(paneTransform.rotation) * (point - paneCenter);
                var nearest = new Vector3(
                    Mathf.Clamp(local.x, -halfExtents.x, halfExtents.x),
                    Mathf.Clamp(local.y, -halfExtents.y, halfExtents.y),
                    Mathf.Clamp(local.z, -halfExtents.z, halfExtents.z));
                if ((local - nearest).sqrMagnitude > radius * radius) continue;

                pane.Shatter(origin);
            }
        }

        /// <summary>
        /// 板を割り、破片を origin から離れる向きへ飛ばす。割れた後は何もしない。
        /// ステージに GlassShardPool が無い間は、板が消えるだけで破片は出ない。
        /// </summary>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public void Shatter(Vector3 origin)
        {
            if (IsBroken) return;
            IsBroken = true;

            _intactPanes.Remove(this);
            _intact.SetActive(false);
            var pool = GlassShardPool.Instance;
            if (pool != null) pool.Spawn(transform, origin, _shardSpeed, _speedJitter, _maxAngularSpeed, _shardLifetime);
        }

        private void OnEnable()
        {
            if (IsBroken) return;

            _worldCenter = transform.position;
            _boundRadius = Vector3.Scale(_size, transform.lossyScale).magnitude * 0.5f;
            _intactPanes.Add(this);
        }

        private void OnDisable()
        {
            _intactPanes.Remove(this);
        }
    }
}
