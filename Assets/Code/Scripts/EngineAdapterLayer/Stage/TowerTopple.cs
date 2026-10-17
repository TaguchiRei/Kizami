using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 天照塔を、ボクセルの帯（VoxelBuilding）が倒壊したときに、指定の向きへ倒して橋にする。
    /// 塔の上の部分は一番上の階の付属物としてメッシュのまま置き、その MeshCollider を倒れる間だけ凸包にする。
    /// VoxelBuilding が倒れた階を止めてキネマティックにしたら、元の形の MeshCollider に戻して歩ける橋にする。
    /// VoxelBuilding はピースの体積だけで質量を決め、塔の上の部分を数えない為、質量を足す。
    /// 帯が壊れただけでは塔は真下へ落ちるので、倒れる向きへ角速度を与える。
    /// </summary>
    public sealed class TowerTopple : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("塔の帯を積んだ建物。倒壊を受け取る")]
        private VoxelBuilding _building;

        [SerializeField]
        [Tooltip("倒れる先の目印。建物から見た水平方向がそのまま倒れる向きになる")]
        private Transform _fallTarget;

        [SerializeField, Min(0f)]
        [Tooltip("倒れ始めに与える、倒れる向きの軸まわりの角速度（rad/s）")]
        private float _toppleAngularSpeed = 0.05f;

        [SerializeField, Min(0f)]
        [Tooltip("塔の上の部分の質量（kg）。倒れた階のまとまりの質量に足す")]
        private float _upperMass = 1000000f;

        private IDisposable _subscription;
        private MeshCollider _upperCollider;
        private bool _hasToppled;

        private void Awake()
        {
            _subscription = _building.RegisterOnCollapsed(OnCollapsed);
        }

        private void Update()
        {
            if (_upperCollider == null) return;

            // 動く間は凸包でないと Rigidbody に付けられない。止まった（キネマティックの）間だけ元の形にする
            var body = _upperCollider.attachedRigidbody;
            var shouldBeConvex = body != null && !body.isKinematic;
            if (_upperCollider.convex != shouldBeConvex) _upperCollider.convex = shouldBeConvex;
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        /// <summary>
        /// 倒れた階のまとまりに塔の上の部分が含まれていれば、質量を足し、最初の1回だけ倒れる向きへ回す。
        /// 上の部分を持たないまとまり（上の階が先に倒れたあとの下の階）は何もしない。
        /// </summary>
        private void OnCollapsed(Rigidbody body)
        {
            var collider = body.GetComponentInChildren<MeshCollider>();
            if (collider == null) return;

            _upperCollider = collider;
            collider.convex = true;
            body.mass += _upperMass;

            if (_hasToppled) return;

            _hasToppled = true;
            var direction = _fallTarget.position - _building.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;

            // 水平な倒れる向きに対して、上向きとの外積が倒れる向きに傾ける回転軸になる
            var axis = Vector3.Cross(Vector3.up, direction.normalized);
            body.angularVelocity = axis * _toppleAngularSpeed;
        }
    }
}
