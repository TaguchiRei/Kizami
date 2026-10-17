using System;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 天照塔を、ボクセルの帯（VoxelBuilding）が倒壊したときに、少ないボクセルの側へ倒して橋にする。
    /// 帯のボクセルの削れた割合が基準を超えたら、帯が自然に倒れるのを待たずに、帯の根元から倒す。
    /// 倒れる向きは、帯に残ったボクセルの重心から見て反対の側（ボクセルの少ない側）にする。倒れる先の目印を指定したときは、その向きを使う。
    /// 塔の上の部分は一番上の階の付属物としてメッシュのまま置き、その MeshCollider を倒れる間だけ凸包にする。
    /// VoxelBuilding が倒れた階を止めてキネマティックにしたら、元の形の MeshCollider に戻して歩ける橋にする。
    /// VoxelBuilding はピースの体積だけで質量を決め、塔の上の部分を数えない為、質量を足す。
    /// 帯が壊れただけでは塔は根元の面に乗ったまま止まるので、倒れる向きへ角速度を与え、傾きが一定の角度に届くまで角加速度を与え続ける。
    /// </summary>
    public sealed class TowerTopple : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("塔の帯を積んだ建物。倒壊を受け取る")]
        private VoxelBuilding _building;

        [SerializeField]
        [Tooltip("倒れる先の目印。建物から見た水平方向がそのまま倒れる向きになる。未設定なら、帯に残ったボクセルの少ない側へ倒す")]
        private Transform _fallTarget;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("帯のボクセルが、読み込んだ時点の体積からこの割合以上削れたら、根元から倒す")]
        private float _triggerRatio = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れ始めに与える、倒れる向きの軸まわりの角速度（rad/s）")]
        private float _toppleAngularSpeed = 0.05f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れる向きの軸まわりに、傾きが次の角度に届くまで与え続ける角加速度（rad/s²）。質量によらない。倒れ始めの勢いだけでは、根元の面に乗ったまま眠って止まる為")]
        private float _toppleAngularAcceleration = 0.05f;

        [SerializeField, Range(1f, 90f)]
        [Tooltip("傾きがこの角度（度）に届くまで、角加速度を与え続ける。届いたあとは重力に任せる")]
        private float _driveUntilAngle = 20f;

        [SerializeField, Min(0f)]
        [Tooltip("塔の上の部分の質量（kg）。倒れた階のまとまりの質量に足す")]
        private float _upperMass = 1000000f;

        private IDisposable _subscription;
        private Rigidbody _drivenBody;
        private Vector3 _toppleAxis;
        private float _defaultSleepThreshold;
        private MeshCollider _upperCollider;
        private bool _hasToppled;
        private bool _hasReleasedSpawns;

        private void Awake()
        {
            _subscription = _building.RegisterOnCollapsed(OnCollapsed);
        }

        private void Update()
        {
            // 削れた割合は読んだときに測る為、毎フレーム読んで、階がメッシュに戻る前の値を取りこぼさない
            if (!_hasToppled && _building.IsConnectionReady && _building.LostVolumeRatio >= _triggerRatio)
            {
                _building.ForceCollapse(0);
            }

            if (_upperCollider == null) return;

            // 倒れて止まったら（VoxelBuilding がキネマティックにしたら）、橋の上に敵を出す
            if (_hasToppled && !_hasReleasedSpawns && _upperCollider.attachedRigidbody != null &&
                _upperCollider.attachedRigidbody.isKinematic)
            {
                _hasReleasedSpawns = true;
                ReleaseTopFaceSpawns();
            }

            // 動く間は凸包でないと Rigidbody に付けられない。止まった（キネマティックの）間だけ元の形にする
            var body = _upperCollider.attachedRigidbody;
            var shouldBeConvex = body != null && !body.isKinematic;
            if (_upperCollider.convex != shouldBeConvex) _upperCollider.convex = shouldBeConvex;
        }

        private void FixedUpdate()
        {
            if (_drivenBody == null) return;

            if (Vector3.Angle(_drivenBody.transform.up, Vector3.up) >= _driveUntilAngle)
            {
                // 以降は重力に任せ、止まったら VoxelBuilding がキネマティックにする
                _drivenBody.sleepThreshold = _defaultSleepThreshold;
                _drivenBody = null;
                return;
            }

            _drivenBody.AddTorque(_toppleAxis * _toppleAngularAcceleration, ForceMode.Acceleration);
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
            var collider = FindUpperCollider(body);
            if (collider == null) return;

            _upperCollider = collider;
            collider.convex = true;
            body.mass += _upperMass;

            if (_hasToppled) return;

            _hasToppled = true;
            var direction = _fallTarget != null
                ? _fallTarget.position - _building.transform.position
                : EstimateFallDirection();
            direction.y = 0f;
            UsefulLogger.Log($"天照塔が倒れ始めました。倒れる向き {direction.normalized}（水平の長さ {direction.magnitude:F3}）", this);
            if (direction.sqrMagnitude < 1e-6f) return;

            // 水平な倒れる向きに対して、上向きとの外積が倒れる向きに傾ける回転軸になる
            _toppleAxis = Vector3.Cross(Vector3.up, direction.normalized);
            body.angularVelocity = _toppleAxis * _toppleAngularSpeed;
            _drivenBody = body;
            _defaultSleepThreshold = body.sleepThreshold;
            body.sleepThreshold = 0f;
        }

        /// <summary>
        /// 塔の上の部分の面のうち、法線が最も上を向いた面を選び、その面の子のスポーン位置の保留を解く。
        /// </summary>
        private void ReleaseTopFaceSpawns()
        {
            TowerFace top = null;
            var bestUp = float.NegativeInfinity;
            foreach (var face in _upperCollider.transform.parent.GetComponentsInChildren<TowerFace>())
            {
                var up = face.WorldNormal.y;
                if (up <= bestUp) continue;

                bestUp = up;
                top = face;
            }

            if (top == null)
            {
                UsefulLogger.LogWarning("塔の上の部分に TowerFace がない為、橋の上に敵を出せません。", this);
                return;
            }

            var count = 0;
            foreach (var point in top.GetComponentsInChildren<EnemySpawnPoint>(true))
            {
                point.Release();
                count++;
            }

            UsefulLogger.Log($"橋の上の面 {top.name} のスポーン位置 {count} 個を解放しました。", this);
        }

        /// <summary>
        /// 倒れた階のまとまりの中から、塔の上の部分の MeshCollider を探す。階のピースの MeshCollider（凸包）は除く。
        /// </summary>
        private static MeshCollider FindUpperCollider(Rigidbody body)
        {
            foreach (var collider in body.GetComponentsInChildren<MeshCollider>())
            {
                // 階のピースの MeshCollider は凸包。塔の上の部分は、倒れる前の今は凸包ではない
                if (!collider.convex && collider.GetComponent<VoxelPiece>() == null) return collider;
            }

            return null;
        }

        /// <summary>
        /// 帯に残ったボクセルの重心（ピースの範囲の中心を体積で重みづけ）から、帯の根元の中心へ向かう向きを返す。ボクセルの少ない側を指す。残りがなければゼロ。
        /// </summary>
        private Vector3 EstimateFallDirection()
        {
            var weighted = Vector3.zero;
            var total = 0f;
            foreach (var floor in _building.Floors)
            {
                foreach (var part in floor.Parts)
                {
                    if (part == null) continue;

                    var volume = part.Volume;
                    weighted += part.WorldBounds.center * volume;
                    total += volume;
                }
            }

            return total > 0f ? _building.transform.position - weighted / total : Vector3.zero;
        }
    }
}
