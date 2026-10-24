using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 切り離されて Rigidbody になったボクセルの物の重心を、高くして倒れやすくし、水平方向は一番近い引き寄せ対象の側へずらす。InGame のシーンへ置く。
    /// 引き寄せ対象は、優先の対象（破壊対象とボス、IAttractionTarget）と、敵の部隊の中心。届く距離の中に優先の対象があれば、敵の部隊より先に選ぶ。
    /// </summary>
    /// <remarks>
    /// かけらは実行中に作られて Initializer から配線できない為、SoundPlayer と同じく Instance で参照する。
    /// </remarks>
    public sealed class VoxelAttraction : MonoBehaviour
    {
        private readonly List<IAttractionTarget> _priorityTargets = new();
        private readonly List<Vector3> _squadCenters = new();

        [SerializeField, Min(0f)]
        [Tooltip("重心を上げる量。物の高さの半分に対する割合")]
        private float _liftRatio = 0.6f;

        [SerializeField, Min(0f)]
        [Tooltip("重心を引き寄せ対象の側へずらす量。物の水平の大きさの半分に対する割合")]
        private float _leanRatio = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("引き寄せ対象が届く距離（m）")]
        private float _range = 60f;

        /// <summary> InGame のシーンにある VoxelAttraction。置かれていなければ null </summary>
        public static VoxelAttraction Instance { get; private set; }

        /// <summary>
        /// body の重心を、形から求めた重心からずらす。VoxelAttraction が無ければ何もしない。
        /// </summary>
        /// <param name="body">切り離されたばかりの物の Rigidbody。重心は形から求めたままのもの</param>
        /// <param name="bounds">物の範囲（ワールド空間）。ずらす量の基準にする</param>
        public static void Apply(Rigidbody body, Bounds bounds)
        {
            if (Instance != null && body != null) Instance.ShiftCenterOfMass(body, bounds);
        }

        /// <summary>
        /// 優先の引き寄せ対象を加える。同じ対象は 1 度だけ加わる。
        /// </summary>
        public void AddPriorityTarget(IAttractionTarget target)
        {
            if (!_priorityTargets.Contains(target)) _priorityTargets.Add(target);
        }

        public void RemovePriorityTarget(IAttractionTarget target)
        {
            _priorityTargets.Remove(target);
        }

        /// <summary>
        /// 敵の部隊の中心を、すべて置き換える。
        /// </summary>
        /// <param name="centers">生きているメンバーがいる部隊の、メンバーの位置の平均</param>
        public void SetSquadCenters(IReadOnlyList<Vector3> centers)
        {
            _squadCenters.Clear();
            for (var i = 0; i < centers.Count; i++)
            {
                _squadCenters.Add(centers[i]);
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ShiftCenterOfMass(Rigidbody body, Bounds bounds)
        {
            var center = body.worldCenterOfMass;
            var shifted = center + Vector3.up * (bounds.extents.y * _liftRatio);
            if (TryFindTarget(center, out var target))
            {
                var toward = target - center;
                toward.y = 0f;
                var distance = toward.magnitude;
                if (distance > 1e-3f)
                {
                    var lean = Mathf.Max(bounds.extents.x, bounds.extents.z) * _leanRatio;
                    shifted += toward / distance * Mathf.Min(lean, distance);
                }
            }

            body.centerOfMass = body.transform.InverseTransformPoint(shifted);
        }

        /// <summary>
        /// from から届く距離の中で、一番近い優先の対象を探し、無ければ一番近い敵の部隊の中心を探す。
        /// </summary>
        private bool TryFindTarget(Vector3 from, out Vector3 target)
        {
            var bestSqr = _range * _range;
            var isFound = false;
            target = default;
            for (var i = _priorityTargets.Count - 1; i >= 0; i--)
            {
                var priority = _priorityTargets[i];
                // 破棄された Unity のオブジェクトは、インターフェース越しでは == null で見分けられない
                if (priority is Object unityObject && unityObject == null)
                {
                    _priorityTargets.RemoveAt(i);
                    continue;
                }

                if (!priority.TryGetAttractionPoint(out var point)) continue;

                var sqr = (point - from).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                target = point;
                isFound = true;
            }

            if (isFound) return true;

            foreach (var squadCenter in _squadCenters)
            {
                var sqr = (squadCenter - from).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                target = squadCenter;
                isFound = true;
            }

            return isFound;
        }
    }
}
