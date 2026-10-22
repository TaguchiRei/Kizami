using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 窓のガラスの1枚の板。ボクセルにはせず、攻撃が当たったら、板を消して GlassShardPool の破片を飛ばす。
    /// 板はコライダーを持たず、割れる前の板だけが静的な空間グリッドに載る。範囲の判定は ShatterInSphere・ShatterInCapsule が、範囲が通るセルの板だけを板の箱との距離で調べる。
    /// 割れる前の板は動かない前提で、有効になった時点の中心と外接球の半径でセルに登録し、遠い板を Transform を読まずに除く。
    /// 建物には板が数百枚並ぶので、コライダーを付けると崩落で建物ごと落とすときの物理の負荷になる。
    /// 破片は物理を使わず、寿命が来たらプールへ戻る。
    /// </summary>
    public sealed class GlassPane : MonoBehaviour
    {
        /// <summary> 空間グリッドの1セルの一辺（m）。板の大きさとビームの穴の半径より少し大きくする </summary>
        private const float CELL_SIZE = 8f;

        /// <summary> セルの座標を1つの long に詰めるときの、1軸あたりのビット数 </summary>
        private const int CELL_BITS = 21;

        private const long CELL_MASK = (1L << CELL_BITS) - 1;

        /// <summary> セルごとの、外接球がそのセルに触れる割れる前の板 </summary>
        private static readonly Dictionary<long, List<GlassPane>> _cells = new();

        /// <summary> 1回の判定で割ると決めた板。調べ終えてから割る </summary>
        private static readonly List<GlassPane> _hits = new();

        /// <summary> 判定の通し番号。同じ判定で板を二度調べないように、板に記録して比べる </summary>
        private static int _queryStamp;

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

        /// <summary> 空間グリッドに登録しているか </summary>
        private bool _isRegistered;

        /// <summary> 最後に調べられた判定の通し番号 </summary>
        private int _checkedStamp;

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
        /// 線分を半セルずつ進み、各点から半径の範囲にかかるセルの板を調べる。板は外接球が触れるセルすべてに登録しているので、カプセルに触れうる板はこれで漏れない。
        /// 板の中心に最も近い線分上の点を中心にした球で、板の箱との距離を調べる。
        /// </summary>
        /// <param name="start">カプセルの一方の端（ワールド空間）</param>
        /// <param name="end">カプセルのもう一方の端（ワールド空間）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        /// <param name="origin">割れの起点（ワールド空間）</param>
        public static void ShatterInCapsule(Vector3 start, Vector3 end, float radius, Vector3 origin)
        {
            if (_cells.Count == 0) return;

            _queryStamp++;
            var segment = end - start;
            var length = segment.magnitude;
            var stepCount = Mathf.CeilToInt(length / (CELL_SIZE * 0.5f));
            var reach = radius + CELL_SIZE * 0.5f;
            for (var step = 0; step <= stepCount; step++)
            {
                var point = stepCount > 0 ? start + segment * ((float)step / stepCount) : start;
                var min = ToCell(point - Vector3.one * reach);
                var max = ToCell(point + Vector3.one * reach);
                for (var x = min.x; x <= max.x; x++)
                for (var y = min.y; y <= max.y; y++)
                for (var z = min.z; z <= max.z; z++)
                {
                    if (!_cells.TryGetValue(ToKey(x, y, z), out var panes)) continue;

                    foreach (var pane in panes)
                    {
                        if (pane._checkedStamp == _queryStamp) continue;

                        pane._checkedStamp = _queryStamp;
                        if (pane.IsTouchedBy(start, segment, radius)) _hits.Add(pane);
                    }
                }
            }

            // 割るとセルの一覧から外れる為、調べ終えてから割る
            foreach (var pane in _hits)
            {
                pane.Shatter(origin);
            }

            _hits.Clear();
        }

        private static Vector3Int ToCell(Vector3 position)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x / CELL_SIZE),
                Mathf.FloorToInt(position.y / CELL_SIZE),
                Mathf.FloorToInt(position.z / CELL_SIZE));
        }

        private static long ToKey(int x, int y, int z)
        {
            return ((x & CELL_MASK) << (CELL_BITS * 2)) | ((y & CELL_MASK) << CELL_BITS) | (z & CELL_MASK);
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

            Unregister();
            _intact.SetActive(false);
            SoundPlayer.Play(SoundEffectKind.GlassBreak, _worldCenter);
            var pool = GlassShardPool.Instance;
            if (pool != null) pool.Spawn(transform, origin, _shardSpeed, _speedJitter, _maxAngularSpeed, _shardLifetime);
        }

        private void OnEnable()
        {
            if (IsBroken) return;

            _worldCenter = transform.position;
            _boundRadius = Vector3.Scale(_size, transform.lossyScale).magnitude * 0.5f;
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        /// <summary>
        /// 線分 start から start + segment を中心線にした半径 radius のカプセルが、板の箱に触れるか。
        /// </summary>
        private bool IsTouchedBy(Vector3 start, Vector3 segment, float radius)
        {
            var segmentLength = segment.sqrMagnitude;
            var t = segmentLength > 1e-8f ? Mathf.Clamp01(Vector3.Dot(_worldCenter - start, segment) / segmentLength) : 0f;
            var point = start + segment * t;

            var reach = radius + _boundRadius;
            if ((point - _worldCenter).sqrMagnitude > reach * reach) return false;

            var halfExtents = Vector3.Scale(_size, transform.lossyScale) * 0.5f;
            var local = Quaternion.Inverse(transform.rotation) * (point - _worldCenter);
            var nearest = new Vector3(
                Mathf.Clamp(local.x, -halfExtents.x, halfExtents.x),
                Mathf.Clamp(local.y, -halfExtents.y, halfExtents.y),
                Mathf.Clamp(local.z, -halfExtents.z, halfExtents.z));
            return (local - nearest).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// 外接球が触れるセルすべてに、この板を登録する。
        /// </summary>
        private void Register()
        {
            if (_isRegistered) return;

            _isRegistered = true;
            GetCellRange(out var min, out var max);
            for (var x = min.x; x <= max.x; x++)
            for (var y = min.y; y <= max.y; y++)
            for (var z = min.z; z <= max.z; z++)
            {
                var key = ToKey(x, y, z);
                if (!_cells.TryGetValue(key, out var panes))
                {
                    panes = new List<GlassPane>();
                    _cells.Add(key, panes);
                }

                panes.Add(this);
            }
        }

        /// <summary>
        /// 登録したセルすべてから、この板を外す。板が無くなったセルは消す。
        /// </summary>
        private void Unregister()
        {
            if (!_isRegistered) return;

            _isRegistered = false;
            GetCellRange(out var min, out var max);
            for (var x = min.x; x <= max.x; x++)
            for (var y = min.y; y <= max.y; y++)
            for (var z = min.z; z <= max.z; z++)
            {
                var key = ToKey(x, y, z);
                if (!_cells.TryGetValue(key, out var panes)) continue;

                panes.Remove(this);
                if (panes.Count == 0) _cells.Remove(key);
            }
        }

        private void GetCellRange(out Vector3Int min, out Vector3Int max)
        {
            var extent = Vector3.one * _boundRadius;
            min = ToCell(_worldCenter - extent);
            max = ToCell(_worldCenter + extent);
        }
    }
}
